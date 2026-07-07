using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Живая синхронизация поездов между игроками. Стартует ТОЛЬКО после Connect to
/// PC|MP (из MpSession). Целиком на стороне лаунчера, как войс.
///
/// Три потока вокруг одного хэндла памяти:
///   • Sender  — читает позицию своего локомотива и шлёт на сервер ≈30 Гц.
///   • Receive — принимает с сервера ростер/вход/позиции/выход других игроков.
///   • Render  — ≈60 Гц двигает объекты чужих поездов (локо + вагоны следом),
///               с интерполяцией по времени (плавно даже при рваной сети).
///
/// Тяжёлые сканы памяти (поиск своего локо и поиск объектов чужого поезда)
/// вынесены в отдельный воркер, чтобы не подтормаживать отправку и рендер.
/// При остановке все чужие объекты возвращаются на исходные места.
/// </summary>
public static class PositionSync
{
    private const string Base = "https://auth.poli-co.com";

    // Точность системного таймера: по умолчанию Thread.Sleep округляется до
    // ~15 мс, из-за чего 60 Гц «плавают» и движение дёргается. timeBeginPeriod(1)
    // = ровно 1 мс, как timer_high_precision в Python.
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint p);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint p);

    private static GameMemory? _mem;
    private static LocoReader? _reader;
    private static ObjectWriter? _writer;

    private static ClientWebSocket? _ws;
    private static CancellationTokenSource? _cts;
    private static readonly SemaphoreSlim _sendLock = new(1, 1);

    private static volatile bool _running;
    private static int _wagonCount;
    private static string _ownId = ""; // мой id (из ростера) — чтобы не рендерить себя

    private static LocoReader.AxlePair? _ownLoco;

    private static readonly ConcurrentDictionary<string, RemoteTrain> _remotes = new();
    private static readonly BlockingCollection<RemoteTrain> _resolveQueue = new();

    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static double NowMs => _clock.Elapsed.TotalMilliseconds;

    // Отладочный лог отключён (файл на рабочий стол больше не пишем).
    private static void Log(string s) { }

    // ── жизненный цикл ───────────────────────────────────────────────────
    public static void Start(int wagonCount)
    {
        if (_running) return;
        string token = UserSession.Token, hwid = UserSession.Hwid;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hwid)) return;

        _running = true;
        _wagonCount = Math.Max(0, wagonCount);
        _cts = new CancellationTokenSource();

        _mem = new GameMemory();
        _reader = null; _writer = null; _ownLoco = null;

        Log($"=== старт (мои вагоны: {_wagonCount}) ===");

        new Thread(SetupLoop)   { IsBackground = true, Name = "TrainSetup"   }.Start();
        new Thread(ResolveLoop) { IsBackground = true, Name = "TrainResolve" }.Start();
        new Thread(RenderLoop)  { IsBackground = true, Name = "TrainRender"  }.Start();
        _ = Task.Run(ConnectLoop);
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }

        // вернуть все чужие объекты на место (пока хэндл ещё жив)
        try
        {
            if (_writer != null)
                foreach (var t in _remotes.Values) t.Restore(_writer);
        }
        catch { }
        _remotes.Clear();

        try { _ws?.Abort(); } catch { }
        try { _mem?.Dispose(); } catch { }
        _mem = null; _reader = null; _writer = null; _ws = null;
        Log("=== стоп ===");
    }

    // ── подготовка: открыть игру, найти свой локомотив ───────────────────
    private static void SetupLoop()
    {
        while (_running && _mem != null && !_mem.Open()) Thread.Sleep(1000);
        if (!_running || _mem == null) return;
        _reader = new LocoReader(_mem);
        _writer = new ObjectWriter(_mem);
        Log($"игра PID {_mem.Pid}, ищу свой локомотив...");

        LocoReader.AxlePair? pair = null;
        while (_running && pair == null)
        {
            pair = _reader.FindLoco();
            if (pair == null) Thread.Sleep(1000);
        }
        _ownLoco = pair;
        Log(pair != null ? "свой локомотив найден, шлю позицию" : "локомотив не найден");
    }

    // ── воркер тяжёлых сканов: находит объекты чужих поездов ─────────────
    private static void ResolveLoop()
    {
        foreach (var train in _resolveQueue.GetConsumingEnumerable())
        {
            if (!_running) break;
            if (_writer == null) { Thread.Sleep(200); }
            try
            {
                if (_writer != null && train.Resolve(_writer))
                    Log($"поезд {train.Nickname}: локо + вагоны {train.WagonsFound}/{train.WagonsTotal} найдены");
                else
                    Log($"[!] поезд {train.Nickname}: локо не найден (loco={train.LocoFound}, вагоны {train.WagonsFound}/{train.WagonsTotal})");
            }
            catch (Exception e) { Log($"resolve {train.Nickname} ошибка: {e.Message}"); }
        }
    }

    // ── рендер: двигаем чужие поезда ─────────────────────────────────────
    private static void RenderLoop()
    {
        timeBeginPeriod(1); // точный 1 мс таймер — иначе 60 Гц «плавают» и дёргает
        try
        {
            var sw = Stopwatch.StartNew();
            long frame = 0;
            const double dt = 1000.0 / 60.0; // 60 Гц
            while (_running)
            {
                if (_writer != null)
                {
                    double now = NowMs;
                    foreach (var t in _remotes.Values)
                        try { t.Update(_writer, now); } catch { }
                }
                frame++;
                double sleep = frame * dt - sw.Elapsed.TotalMilliseconds;
                if (sleep > 0) Thread.Sleep((int)sleep);
            }
        }
        finally { timeEndPeriod(1); }
    }

    // ── сеть: подключение + приём ────────────────────────────────────────
    private static async Task ConnectLoop()
    {
        string url = $"{Base}/trainsync/ws?token={Uri.EscapeDataString(UserSession.Token)}"
                   + $"&hwid={Uri.EscapeDataString(UserSession.Hwid)}&wagons={_wagonCount}";
        url = url.Replace("https://", "wss://").Replace("http://", "ws://");

        while (_running)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(url), _cts!.Token);
                Log("сокет подключён");
                _ = Task.Run(SenderLoop);
                await ReceiveLoop();
            }
            catch (Exception e) { if (_running) Log($"сеть: {e.Message}"); }
            finally { try { _ws?.Dispose(); } catch { } _ws = null; }
            if (_running) { try { await Task.Delay(2000, _cts!.Token); } catch { } }
        }
    }

    private static async Task SenderLoop()
    {
        var ws = _ws;
        while (_running && ws != null && ws.State == WebSocketState.Open)
        {
            if (_reader != null && _ownLoco != null)
            {
                var s = _reader.Read(_ownLoco.Value);
                // ВАЖНО: точка-разделитель (InvariantCulture). В русской локали
                // "F3" даёт запятую → JSON становится битым и сервер его выбросит.
                var ci = CultureInfo.InvariantCulture;
                string json = "{\"type\":\"pos\""
                    + ",\"x\":" + s.X.ToString("F3", ci)
                    + ",\"z\":" + s.Z.ToString("F3", ci)
                    + ",\"y\":" + s.Y.ToString("F3", ci)
                    + ",\"r\":" + s.Rot.ToString("F3", ci) + "}";
                await Send(json);
            }
            try { await Task.Delay(33, _cts!.Token); } catch { break; } // ≈30 Гц
        }
    }

    private static async Task ReceiveLoop()
    {
        var buf = new byte[16384];
        var sb = new StringBuilder();
        while (_running && _ws?.State == WebSocketState.Open)
        {
            sb.Clear();
            WebSocketReceiveResult res;
            do
            {
                res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts!.Token);
                if (res.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
            } while (!res.EndOfMessage);

            try { HandleMessage(sb.ToString()); } catch (Exception e) { Log($"msg: {e.Message}"); }
        }
    }

    private static void HandleMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string type = root.GetProperty("type").GetString() ?? "";

        switch (type)
        {
            case "roster":
                if (root.TryGetProperty("you", out var you) && you.TryGetProperty("id", out var yid))
                    _ownId = yid.GetString() ?? "";
                if (root.TryGetProperty("players", out var players))
                    foreach (var p in players.EnumerateArray()) AddRemote(p);
                break;
            case "join":
                if (root.TryGetProperty("player", out var pl)) AddRemote(pl);
                break;
            case "pos":
                {
                    string id = root.GetProperty("id").GetString() ?? "";
                    if (_remotes.TryGetValue(id, out var t))
                        t.PushPosition(
                            root.GetProperty("x").GetDouble(),
                            root.GetProperty("z").GetDouble(),
                            root.GetProperty("y").GetDouble(),
                            root.GetProperty("r").GetDouble(),
                            NowMs);
                    break;
                }
            case "leave":
                {
                    string id = root.GetProperty("id").GetString() ?? "";
                    if (_remotes.TryRemove(id, out var t))
                    {
                        try { if (_writer != null) t.Restore(_writer); } catch { }
                        Log($"игрок {t.Nickname} вышел — поезд убран");
                    }
                    break;
                }
            case "full":
                Log("сервер: локомотивы кончились (full)");
                break;
        }
    }

    private static void AddRemote(JsonElement p)
    {
        string id   = p.GetProperty("id").GetString() ?? "";
        if (string.IsNullOrEmpty(id) || _remotes.ContainsKey(id)) return;
        if (id == _ownId) { Log($"игнор: это я сам ({id})"); return; } // себя не рендерим
        string nick = p.TryGetProperty("nickname", out var n) ? n.GetString() ?? id : id;
        string loco = p.TryGetProperty("loco", out var l) ? l.GetString() ?? "" : "";
        var wagons = new List<string>();
        if (p.TryGetProperty("wagons", out var ws))
            foreach (var w in ws.EnumerateArray()) wagons.Add(w.GetString() ?? "");
        if (string.IsNullOrEmpty(loco)) return;

        var train = new RemoteTrain(id, nick, loco, wagons.ToArray());
        // начальная позиция, если сервер прислал (для мгновенного спавна на месте)
        if (p.TryGetProperty("pos", out var pos) && pos.ValueKind == JsonValueKind.Object)
            train.PushPosition(pos.GetProperty("x").GetDouble(), pos.GetProperty("z").GetDouble(),
                               pos.GetProperty("y").GetDouble(), pos.GetProperty("r").GetDouble(), NowMs);

        _remotes[id] = train;
        _resolveQueue.Add(train);
        Log($"новый игрок {nick}: локо + {wagons.Count} вагонов — ищу в памяти");
    }

    private static async Task Send(string json)
    {
        var ws = _ws;
        if (ws == null || ws.State != WebSocketState.Open) return;
        var data = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync();
        try { await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts!.Token); }
        catch { }
        finally { _sendLock.Release(); }
    }
}
