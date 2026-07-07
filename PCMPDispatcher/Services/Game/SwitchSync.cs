using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Клиент диспетчерской системы стрелок. Стартует вместе с сессией (из
/// MpSession), как и позиционная синхронизация.
///
/// Схема ровно та, что просил оператор: сервер ГЛОБАЛЬНО рассылает желаемое
/// положение всех управляемых стрелок (одинаковое для всех игроков — одна карта),
/// а лаунчер посекундно применяет это в память своей игры. Никаких локальных
/// txt-файлов: единственный источник истины — сервер.
///
/// Два потока:
///   • Receive — принимает с /dispatch/ws карту {стрелка: LEFT|RIGHT} и кладёт
///               в _desired.
///   • Apply   — раз в секунду проходит загруженные в сцене стрелки и переводит
///               те, чьё текущее положение не совпадает с желаемым.
///
/// Реестр стрелок (сырые байты для записи) забандлен в лаунчер; сервер шлёт лишь
/// LEFT/RIGHT. Память стрелок — отдельный механизм (GameManagerVC64.dll), поэтому
/// держим собственный GameMemory-хэндл, не мешая PositionSync.
/// </summary>
public static class SwitchSync
{
    private const string Base = "https://auth.poli-co.com";

    private static GameMemory? _mem;
    private static SwitchWriter? _sw;
    private static SignalWriter? _signals;
    private static ClientWebSocket? _ws;
    private static CancellationTokenSource? _cts;
    private static volatile bool _running;

    // Желаемое положение с сервера: стрелка → "LEFT"/"RIGHT".
    private static readonly ConcurrentDictionary<string, string> _desired = new();

    private static void Log(string s) { } // отладка отключена

    // ── жизненный цикл ───────────────────────────────────────────────────
    public static void Start()
    {
        if (_running) return;
        string token = UserSession.Token, hwid = UserSession.Hwid;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hwid)) return;

        _running = true;
        _cts = new CancellationTokenSource();
        _mem = new GameMemory();
        _sw = null;
        _signals = new SignalWriter(); // сигналы = txt-файлы, память не нужна
        _desired.Clear();

        new Thread(ApplyLoop) { IsBackground = true, Name = "SwitchApply" }.Start();
        _ = Task.Run(ConnectLoop);
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        try { _ws?.Abort(); } catch { }
        try { _mem?.Dispose(); } catch { }
        _mem = null; _sw = null; _ws = null;
        _desired.Clear();
    }

    // ── подготовка памяти + посекундное применение ───────────────────────
    private static void ApplyLoop()
    {
        // ждём игру
        while (_running && _mem != null && !_mem.Open()) Thread.Sleep(1000);
        if (!_running || _mem == null) return;

        _sw = new SwitchWriter(_mem);
        try { _sw.LoadRegistry(RegistryPath()); } catch { }

        // ждём загрузку менеджера стрелок (GameManagerVC64.dll)
        while (_running && !_sw.ResolveModule()) Thread.Sleep(1000);

        while (_running)
        {
            try
            {
                if (_sw != null && _sw.Ready && !_desired.IsEmpty)
                {
                    var loaded = _sw.Scan(); // стрелки, что сейчас в сцене
                    foreach (var (name, addr) in loaded)
                    {
                        if (!_desired.TryGetValue(name, out var want)) continue;
                        var e = _sw.Get(name);
                        if (e == null) continue;
                        if (_sw.ReadPosition(addr.tp, e) != want)
                            _sw.Apply(addr.tp, addr.obj, e, want);
                    }
                }
            }
            catch { }
            Thread.Sleep(1000);
        }
    }

    // Реестр рядом с exe (Switches\switches_registry.json), с фолбэками для dev.
    private static string RegistryPath()
    {
        // Сначала реестр из установки игры (актуальнее), затем забандленный.
        var fromGame = GamePaths.SwitchesRegistry();
        if (fromGame != null && File.Exists(fromGame)) return fromGame;
        return Path.Combine(AppContext.BaseDirectory, "Switches", "switches_registry.json");
    }

    // ── сеть: подключение + приём карты стрелок ──────────────────────────
    private static async Task ConnectLoop()
    {
        string url = $"{Base}/dispatch/ws?token={Uri.EscapeDataString(UserSession.Token)}"
                   + $"&hwid={Uri.EscapeDataString(UserSession.Hwid)}";
        url = url.Replace("https://", "wss://").Replace("http://", "ws://");

        while (_running)
        {
            try
            {
                _ws = new ClientWebSocket();
                await _ws.ConnectAsync(new Uri(url), _cts!.Token);
                await ReceiveLoop();
            }
            catch (Exception e) { if (_running) Log($"сеть: {e.Message}"); }
            finally { try { _ws?.Dispose(); } catch { } _ws = null; }
            if (_running) { try { await Task.Delay(2000, _cts!.Token); } catch { } }
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
        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

        switch (type)
        {
            case "switches":
                if (root.TryGetProperty("switches", out var sw) && sw.ValueKind == JsonValueKind.Object)
                    foreach (var p in sw.EnumerateObject())
                    {
                        var pos = p.Value.GetString();
                        if (pos == "LEFT" || pos == "RIGHT") _desired[p.Name] = pos;
                    }
                break;

            case "signals":
                // Аспекты светофоров → пишем в txt-файлы (id = "Station/NAME").
                if (root.TryGetProperty("signals", out var sig) && sig.ValueKind == JsonValueKind.Object && _signals != null)
                    foreach (var p in sig.EnumerateObject())
                    {
                        var aspect = p.Value.GetString();
                        if (!string.IsNullOrEmpty(aspect)) _signals.Write(p.Name, aspect);
                    }
                break;

            case "kick":
                // Сервер выгоняет за нарушение (напр. проезд в сторону Soimos).
                _ = Services.MpSession.DisconnectAsync();
                break;
        }
    }
}
