using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCMPDispatcher.Services;

/// <summary>
/// Owns the live multiplayer session: tells the auth server the driver has
/// connected (with their run sheet), launches the PC|MP HUD overlay over the
/// game, and tears both down on disconnect. One HUD process at a time.
/// </summary>
public static class MpSession
{
    private const string Base = "https://auth.poli-co.com";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static Process? _hud;
    private static long _chatSince;   // chat id baseline handed to the HUD on connect
    private static bool _connected;   // защита от двойного дисконнекта

    public static bool IsConnected => _hud is { HasExited: false };

    /// <summary>Register the session on the server and start the HUD overlay.</summary>
    public static async Task<bool> ConnectAsync(object run, int wagonCount = 0, MapPlayerInfo? mapInfo = null)
    {
        string token = UserSession.Token, hwid = UserSession.Hwid;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hwid))
            return false;

        // 1) Tell the server the driver is on the line (posts the join chat message).
        try
        {
            var body = JsonSerializer.Serialize(new { hwid, token, run });
            using var payload = new StringContent(body, Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{Base}/mp/connect", payload);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!(doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean()))
                return false;
            _chatSince = doc.RootElement.TryGetProperty("chat_since", out var cs) ? cs.GetInt64() : 0;
        }
        catch { return false; }

        // 2) Launch the HUD overlay, handing it everything it needs to pull its data.
        LaunchHud();

        // 3) Start the in-launcher voice radio (mic capture, playback, PTT).
        //    The HUD only animates; all audio lives here.
        try { VoiceChat.Start(); } catch { }

        // 4) Живая синхронизация поездов между игроками начинается ТОЛЬКО
        //    после Connect. Число вагонов — сколько игрок выбрал в лаунчере.
        try { Game.PositionSync.Start(wagonCount); } catch { }

        // 5) Диспетчер стрелок: посекундно принимает с сервера глобальное
        //    положение всех стрелок и применяет в память игры.
        try { Game.SwitchSync.Start(); } catch { }

        // 6) Мост на онлайн-карту: раз в 2 сек шлём GPS+скорость из RailDriver.
        try { if (mapInfo != null) MapBridge.Start(mapInfo); } catch { }
        _connected = true;
        return true;
    }

    /// <summary>Close the HUD and clear the server-side session. Идемпотентно —
    /// повторные вызовы (кнопка Disconnect + закрытие окна) ничего не делают,
    /// чтобы не слать «disconnected» в чат дважды.</summary>
    public static async Task DisconnectAsync()
    {
        if (!_connected) return;
        _connected = false;

        try { VoiceChat.Stop(); } catch { }
        try { Game.PositionSync.Stop(); } catch { }
        try { Game.SwitchSync.Stop(); } catch { }
        try { MapBridge.Stop(); } catch { }
        StopHud();

        string token = UserSession.Token, hwid = UserSession.Hwid;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(hwid)) return;
        try
        {
            var body = JsonSerializer.Serialize(new { hwid, token });
            using var payload = new StringContent(body, Encoding.UTF8, "application/json");
            await Http.PostAsync($"{Base}/mp/disconnect", payload);
        }
        catch { }
    }

    private static void LaunchHud()
    {
        StopHud(); // never run two overlays

        string? exe = FindHudExe();
        if (exe == null) return;

        string args =
            $"--server \"{Base}\" --token \"{UserSession.Token}\" --hwid \"{UserSession.Hwid}\" " +
            $"--nickname \"{UserSession.VisibleName}\" --avatar-url \"{UserSession.SteamAvatarUrl}\" " +
            $"--since {_chatSince}";

        try
        {
            // UseShellExecute=false → the HUD is a direct child of the (already
            // elevated) launcher: it inherits admin rights, so it never fires its
            // own UAC prompt or self-relaunches, and we keep a real handle to it.
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            };
            if (exe.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "python";
                psi.Arguments = $"\"{exe}\" {args}";
            }
            else psi.Arguments = args;

            _hud = Process.Start(psi);
        }
        catch { _hud = null; }
    }

    private static void StopHud()
    {
        try { if (_hud is { HasExited: false }) _hud.Kill(true); } catch { }
        _hud = null;
    }

    // Look for the HUD executable in the bundled location first, then dev builds.
    private static string? FindHudExe()
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "PCIMP HUD", "PCIMP Hud.exe"),
            @"C:\PC Hud\dist\PCIMP Hud\PCIMP Hud.exe",
            @"C:\PC Hud\build\PCIMP Hud\PCIMP Hud.exe",
            @"C:\PC Hud\pcImp_v1.py",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }
}
