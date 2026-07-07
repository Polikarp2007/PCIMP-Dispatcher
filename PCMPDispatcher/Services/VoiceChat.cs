using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PCMPDispatcher.Services;

/// <summary>
/// Голосовая рация «все слышат всех» целиком на стороне лаунчера.
/// Захват микрофона и воспроизведение — через NAudio (WinMM), связь — по
/// WebSocket к серверу (/voice/ws). Push-to-talk на ЛЕВОМ Ctrl.
///
/// HUD-оверлей звук НЕ трогает — он лишь показывает анимацию говорящего,
/// получая с сервера управляющие сообщения (отдельное mode=control соединение).
///
/// Формат звука жёстко один и тот же на запись и на воспроизведение —
/// 16 kHz, 16-бит, моно. Микрофон держим включённым ВСЁ время сессии и
/// буферим последние кадры (pre-roll), чтобы не терять первое слово; после
/// отпускания шлём ещё чуть-чуть (post-roll), чтобы не рубить конец фразы.
/// Перед отправкой голос прогоняется через «рацийный» фильтр (полоса
/// 300–3000 Гц + сатурация) — отсюда характерный эфирный тембр и громкость.
/// </summary>
public static class VoiceChat
{
    private const string Base = "https://auth.poli-co.com";
    private static readonly WaveFormat Format = new WaveFormat(16000, 16, 1);
    private const int VK_LCONTROL = 0xA2;

    // ── настройки звука рации (крути тут) ────────────────────────────────
    private const float PreGain  = 9.0f;   // подусиление до фильтра (громкость)
    private const float Drive    = 2.5f;   // сатурация: грит + компрессия громкости
    private const float PostGain = 2.2f;   // финальное усиление после фильтра
    private const double HpHz = 300.0;     // срез низов (рация не даёт баса)
    private const double LpHz = 3300.0;    // срез верхов (узкая «эфирная» полоса)
    private const int PrerollFrames = 6;   // ~240мс до нажатия — ловим первое слово
    private const int PostRollMs   = 250;  // хвост после отпускания — не режем конец
    private const float HissLevel   = 0.012f; // постоянное шипение «открытого эфира»
    private const double CrackleProb = 0.0005; // вероятность щелчка-помехи на сэмпл
    private const long MaxTalkMs   = 60_000;  // лимит непрерывной передачи — 1 минута

    private static readonly Random _rng = new();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static ClientWebSocket? _ws;
    private static CancellationTokenSource? _cts;
    private static readonly SemaphoreSlim _sendLock = new(1, 1);

    private static WaveInEvent? _mic;
    private static WaveOutEvent? _out;
    private static BufferedWaveProvider? _playBuffer;

    private static Thread? _pttThread;
    private static volatile bool _running;
    private static volatile bool _transmitting;
    private static long _stopAtTicks;      // != 0 → финализировать stop после этого времени
    private static long _txStartTicks;     // когда начали передачу (для лимита 60с)

    private static readonly object _preLock = new();
    private static readonly Queue<byte[]> _preroll = new();

    private static readonly Biquad _hp = Biquad.HighPass(16000, HpHz, 0.707);
    private static readonly Biquad _lp = Biquad.LowPass(16000, LpHz, 0.707);

    // ── громкость воспроизведения (0..1), можно менять в любой момент ───
    private static float _volume = 0.8f;
    public static float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_out != null) _out.Volume = _volume;
        }
    }

    // ── жизненный цикл ───────────────────────────────────────────────────

    public static void Start()
    {
        if (_running) return;
        string token = UserSession.Token, hwid = UserSession.Hwid;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hwid)) return;

        _running = true;
        _cts = new CancellationTokenSource();

        try { SetupAudio(); }
        catch (Exception e) { Console.WriteLine($"[voice] audio init failed: {e.Message}"); }

        _ = Task.Run(ConnectLoop);

        _pttThread = new Thread(PttLoop) { IsBackground = true, Name = "VoicePTT" };
        _pttThread.Start();
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        _transmitting = false;

        try { _cts?.Cancel(); } catch { }
        try { _mic?.StopRecording(); } catch { }
        try { _mic?.Dispose(); } catch { }
        try { _out?.Stop(); } catch { }
        try { _out?.Dispose(); } catch { }
        try { _ws?.Abort(); } catch { }

        _mic = null; _out = null; _playBuffer = null; _ws = null;
        lock (_preLock) _preroll.Clear();
    }

    // ── звук ─────────────────────────────────────────────────────────────

    private static void SetupAudio()
    {
        _playBuffer = new BufferedWaveProvider(Format)
        {
            BufferDuration = TimeSpan.FromSeconds(4),
            DiscardOnBufferOverflow = true,
        };
        _out = new WaveOutEvent { DesiredLatency = 140 };
        _out.Init(_playBuffer);
        _out.Volume = _volume;
        _out.Play();

        // Микрофон включён всю сессию; отправляем только когда зажат PTT.
        _mic = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 40 };
        _mic.DataAvailable += OnMicData;
        _mic.StartRecording();
    }

    private static void OnMicData(object? sender, WaveInEventArgs e)
    {
        int n = e.BytesRecorded;
        if (n <= 0) return;
        var raw = new byte[n];
        Buffer.BlockCopy(e.Buffer, 0, raw, 0, n);

        // Всегда пополняем кольцевой буфер последних кадров (pre-roll).
        lock (_preLock)
        {
            _preroll.Enqueue(raw);
            while (_preroll.Count > PrerollFrames) _preroll.Dequeue();
        }

        // Отложенная остановка (post-roll): хвост фразы доезжает.
        long stopAt = _stopAtTicks;
        if (stopAt != 0 && DateTime.UtcNow.Ticks >= stopAt)
        {
            _stopAtTicks = 0;
            _transmitting = false;
            _ = SendTextAsync("{\"type\":\"stop\"}");
        }

        // Лимит 1 минута: если PTT завис/держат слишком долго — рубим сами,
        // чтобы не шипело бесконечно (сервер тоже отпустит эфир по своему таймеру).
        if (_transmitting && _txStartTicks != 0
            && DateTime.UtcNow.Ticks - _txStartTicks >= TimeSpan.FromMilliseconds(MaxTalkMs).Ticks)
        {
            _transmitting = false;
            _stopAtTicks = 0;
            _ = SendTextAsync("{\"type\":\"stop\"}");
        }

        if (!_transmitting) return;
        _ = SendRawAsync(ProcessRadio(raw), WebSocketMessageType.Binary);

        // Уровень громкости речи (0..100) — HUD рисует по нему прыгающую шкалу.
        _ = SendTextAsync($"{{\"type\":\"level\",\"value\":{RawLevel(raw)}}}");
    }

    // Громкость сырого кадра → 0..100 для шкалы в HUD. Нелинейная (sqrt)
    // кривая: даже тихая речь даёт заметный размах столбиков, чувствительно.
    private static int RawLevel(byte[] pcm)
    {
        int peak = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2)
        {
            short s = (short)(pcm[i] | (pcm[i + 1] << 8));
            int a = s < 0 ? -s : s;
            if (a > peak) peak = a;
        }
        double p = peak / 32768.0;
        double norm = Math.Min(1.0, Math.Sqrt(p) * 2.4); // sqrt → чувствительность к тихому
        return (int)(norm * 100);
    }

    // «Рацийный» фильтр: полоса 300–3000 Гц + мягкая сатурация. Здесь же вся
    // громкость. Стерео-состояние фильтров непрерывно между кадрами.
    private static byte[] ProcessRadio(byte[] pcm)
    {
        int n = pcm.Length;
        var outp = new byte[n];
        for (int i = 0; i + 1 < n; i += 2)
        {
            short s = (short)(pcm[i] | (pcm[i + 1] << 8));
            float x = s / 32768f;
            x *= PreGain;
            x = _hp.Process(x);
            x = _lp.Process(x);
            // Эфирный «бед»: постоянное шипение + редкие щелчки-помехи.
            x += (float)(_rng.NextDouble() * 2 - 1) * HissLevel;
            if (_rng.NextDouble() < CrackleProb)
                x += (float)(_rng.NextDouble() * 2 - 1) * 0.6f;
            x = (float)Math.Tanh(x * Drive) / (float)Math.Tanh(Drive); // сатурация + нормализация
            x *= PostGain;
            if (x > 1f) x = 1f; else if (x < -1f) x = -1f;
            short o = (short)(x * 32767f);
            outp[i] = (byte)(o & 0xFF);
            outp[i + 1] = (byte)((o >> 8) & 0xFF);
        }
        return outp;
    }

    // ── push-to-talk ─────────────────────────────────────────────────────

    private static void PttLoop()
    {
        bool prev = false;
        while (_running)
        {
            bool down = (GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0;
            if (down != prev)
            {
                prev = down;
                if (down) BeginTransmit();
                else EndTransmit();
            }
            Thread.Sleep(20);
        }
    }

    private static void BeginTransmit()
    {
        if (_transmitting) { _stopAtTicks = 0; return; } // снова зажал — отменяем хвост
        _stopAtTicks = 0;
        _txStartTicks = DateTime.UtcNow.Ticks;
        _hp.Reset(); _lp.Reset();
        _transmitting = true;
        _ = SendTextAsync("{\"type\":\"start\"}");

        // pre-roll: отправляем последние буферизованные кадры, чтобы первое
        // слово (сказанное в момент нажатия) не потерялось.
        byte[][] pre;
        lock (_preLock) pre = _preroll.ToArray();
        foreach (var f in pre)
            _ = SendRawAsync(ProcessRadio(f), WebSocketMessageType.Binary);
    }

    private static void EndTransmit()
    {
        if (!_transmitting) return;
        // Не рубим сразу — планируем стоп через PostRollMs, живой звук всё это
        // время ещё шлётся (хвост фразы сохраняется).
        _stopAtTicks = DateTime.UtcNow.Ticks + TimeSpan.FromMilliseconds(PostRollMs).Ticks;
    }

    // ── сеть ─────────────────────────────────────────────────────────────

    private static async Task ConnectLoop()
    {
        string url = $"{Base}/voice/ws?token={Uri.EscapeDataString(UserSession.Token)}"
                   + $"&hwid={Uri.EscapeDataString(UserSession.Hwid)}";
        // Самопрослушивание для теста в одиночку: сервер вернёт твой голос тебе же.
        // Включается только если задана переменная окружения PCIMP_VOICE_ECHO=1.
        if (Environment.GetEnvironmentVariable("PCIMP_VOICE_ECHO") == "1")
            url += "&echo=1";
        url = url.Replace("https://", "wss://").Replace("http://", "ws://");

        while (_running)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(url), _cts!.Token);
                await ReceiveLoop();
            }
            catch (Exception e)
            {
                if (_running) Console.WriteLine($"[voice] link lost: {e.Message}");
            }
            finally
            {
                try { _ws?.Dispose(); } catch { }
                _ws = null;
            }
            if (_running) { try { await Task.Delay(2000, _cts!.Token); } catch { } }
        }
    }

    private static async Task ReceiveLoop()
    {
        var buf = new byte[16384];
        while (_running && _ws?.State == WebSocketState.Open)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult res;
            do
            {
                res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts!.Token);
                if (res.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buf, 0, res.Count);
            } while (!res.EndOfMessage);

            if (res.MessageType == WebSocketMessageType.Binary)
            {
                var audio = ms.ToArray();
                try { _playBuffer?.AddSamples(audio, 0, audio.Length); } catch { }
            }
            else // текстовые команды сервера
            {
                var txt = Encoding.UTF8.GetString(ms.ToArray());
                // denied — эфир занят другим; release — эфир отобрали по лимиту 60с.
                // В обоих случаях прекращаем свою передачу немедленно.
                if (txt.Contains("\"denied\"") || txt.Contains("\"release\""))
                {
                    _transmitting = false;
                    _stopAtTicks = 0;
                }
            }
        }
    }

    private static Task SendTextAsync(string json)
        => SendRawAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text);

    private static async Task SendRawAsync(byte[] data, WebSocketMessageType type)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(new ArraySegment<byte>(data), type, true, _cts!.Token); }
        catch { }
        finally { _sendLock.Release(); }
    }

    // ── простой биквад-фильтр (RBJ cookbook), Transposed Direct Form II ───
    private sealed class Biquad
    {
        private readonly float _b0, _b1, _b2, _a1, _a2;
        private float _z1, _z2;

        private Biquad(float b0, float b1, float b2, float a1, float a2)
        { _b0 = b0; _b1 = b1; _b2 = b2; _a1 = a1; _a2 = a2; }

        public void Reset() { _z1 = _z2 = 0f; }

        public float Process(float x)
        {
            float y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        public static Biquad LowPass(double fs, double f0, double q)
        {
            double w0 = 2 * Math.PI * f0 / fs, c = Math.Cos(w0), s = Math.Sin(w0), alpha = s / (2 * q);
            double a0 = 1 + alpha;
            return new Biquad((float)((1 - c) / 2 / a0), (float)((1 - c) / a0), (float)((1 - c) / 2 / a0),
                              (float)(-2 * c / a0), (float)((1 - alpha) / a0));
        }

        public static Biquad HighPass(double fs, double f0, double q)
        {
            double w0 = 2 * Math.PI * f0 / fs, c = Math.Cos(w0), s = Math.Sin(w0), alpha = s / (2 * q);
            double a0 = 1 + alpha;
            return new Biquad((float)((1 + c) / 2 / a0), (float)(-(1 + c) / a0), (float)((1 + c) / 2 / a0),
                              (float)(-2 * c / a0), (float)((1 - alpha) / a0));
        }
    }
}
