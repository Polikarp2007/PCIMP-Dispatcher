using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCMPDispatcher.Services;

/// <summary>Статичные данные рейса для карты (то, что не меняется в поездке).</summary>
public sealed class MapPlayerInfo
{
    public string Name = "";
    public string LastName = "";
    public string TrainType = "";
    public string TrainNumber = "";
    public string Locomotive = "";
    public int    WagonCount;
    public string RouteFrom = "";
    public string RouteTo = "";
    public string DepartureTime = "";
    public Dictionary<string, int> IntermediateStops = new();
}

/// <summary>
/// Мост «игра → онлайн-карта» (map.poli-co.com). Запускается после Connect to
/// PC|MP в режиме драйвера и раз в 2 секунды шлёт живую позицию локомотива.
///
/// Координаты берутся не из памяти, а через официальный плагин RailWorks —
/// RailDriver64.dll (тот же способ, что в старом лаунчере): виртуальные
/// «контроллеры» отдают готовые GPS lat/lon и скорость. Разные локомотивы дают
/// скорость то на контроллере 58, то на 60 — читаем оба и берём валидный.
///
/// Отправка идёт на НАШУ auth-систему (auth.poli-co.com/map/update) с token+hwid:
/// сервер сам проверяет личность и кладёт позицию под проверенным username, так
/// что подделать чужой маркер невозможно. Карта читает /mp-players.
/// </summary>
public static class MapBridge
{
    private const string Base = "https://auth.poli-co.com";

    // Контроллеры RailDriver.
    private const int ID_SPEED_A = 58;
    private const int ID_SPEED_B = 60; // некоторые локомотивы отдают скорость сюда
    private const int ID_LAT     = 400;
    private const int ID_LON     = 401;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetControllerValueDelegate(int ctrl, int param);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetBoolDelegate([MarshalAs(UnmanagedType.Bool)] bool val);

    private static readonly string[] SearchPaths =
    {
        @"D:\My Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
        @"D:\Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
        @"E:\My Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
        @"E:\Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
        @"C:\Program Files (x86)\Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
        @"C:\Program Files\Steam\steamapps\common\RailWorks\plugins\RailDriver64.dll",
    };

    private static IntPtr _rd = IntPtr.Zero;
    private static GetControllerValueDelegate? _getCV;
    private static CancellationTokenSource? _cts;
    private static volatile bool _running;
    private static MapPlayerInfo _info = new();

    public static void Start(MapPlayerInfo info)
    {
        if (_running) return;
        if (string.IsNullOrEmpty(UserSession.Token) || string.IsNullOrEmpty(UserSession.Hwid)) return;

        _running = true;
        _info = info ?? new MapPlayerInfo();
        _cts = new CancellationTokenSource();
        TryLoadRailDriver();
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        _ = SendLeave();
        if (_rd != IntPtr.Zero) { try { NativeLibrary.Free(_rd); } catch { } _rd = IntPtr.Zero; _getCV = null; }
    }

    private static async Task Loop(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        while (_running && !token.IsCancellationRequested)
        {
            try
            {
                float lat = 0f, lon = 0f, speed = 0f;
                if (_getCV != null)
                {
                    lat = _getCV(ID_LAT, 0);
                    lon = _getCV(ID_LON, 0);
                    // Скорость: разные локо отдают на 58 или 60 — берём валидный.
                    float s = _getCV(ID_SPEED_A, 0);
                    if (s == 0f) s = _getCV(ID_SPEED_B, 0);
                    speed = s;
                }

                var payload = new
                {
                    token = UserSession.Token,
                    hwid  = UserSession.Hwid,
                    name      = _info.Name,
                    last_name = _info.LastName,
                    lat   = (double)lat,
                    lon   = (double)lon,
                    speed = Math.Round((double)speed, 1),
                    train_type   = _info.TrainType,
                    train_number = _info.TrainNumber,
                    locomotive   = _info.Locomotive,
                    wagon_count  = _info.WagonCount,
                    route_from   = _info.RouteFrom,
                    route_to     = _info.RouteTo,
                    departure_time     = _info.DepartureTime,
                    intermediate_stops = _info.IntermediateStops,
                };
                // JsonSerializer пишет числа с точкой (инвариантно) — RU-локаль не
                // ломает JSON.
                var json = JsonSerializer.Serialize(payload);
                using var body = new StringContent(json, Encoding.UTF8, "application/json");
                await http.PostAsync($"{Base}/map/update", body, token);
            }
            catch (OperationCanceledException) { break; }
            catch { /* нет сети — не страшно, попробуем через 2 сек */ }

            try { await Task.Delay(2000, token); } catch { break; }
        }
    }

    private static async Task SendLeave()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = JsonSerializer.Serialize(new { token = UserSession.Token, hwid = UserSession.Hwid });
            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            await http.PostAsync($"{Base}/map/leave", body);
        }
        catch { }
    }

    private static void TryLoadRailDriver()
    {
        var candidates = new List<string>();
        try
        {
            using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Valve\Steam");
            if (reg?.GetValue("InstallPath") is string steam)
                candidates.Add(Path.Combine(steam, "steamapps", "common", "RailWorks", "plugins", "RailDriver64.dll"));
        }
        catch { }
        candidates.AddRange(SearchPaths);

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                _rd = NativeLibrary.Load(path);
                var cvPtr = NativeLibrary.GetExport(_rd, "GetControllerValue");
                _getCV = Marshal.GetDelegateForFunctionPointer<GetControllerValueDelegate>(cvPtr);
                if (NativeLibrary.TryGetExport(_rd, "SetRailSimConnected", out var simPtr))
                    Marshal.GetDelegateForFunctionPointer<SetBoolDelegate>(simPtr)(true);
                if (NativeLibrary.TryGetExport(_rd, "SetRailDriverConnected", out var rdPtr))
                    Marshal.GetDelegateForFunctionPointer<SetBoolDelegate>(rdPtr)(true);
                Debug.WriteLine($"[MapBridge] RailDriver loaded: {path}");
                return;
            }
            catch (Exception ex) { Debug.WriteLine($"[MapBridge] load failed ({path}): {ex.Message}"); }
        }
        Debug.WriteLine("[MapBridge] RailDriver64.dll not found — map coords will be 0");
    }
}
