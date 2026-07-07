using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Находит корень установки RailWorks и производные пути (Signals, Switches,
/// плагин RailDriver). Один источник правды — чтобы стрелки, сигналы, карта и
/// память искали игру одинаково.
///
/// Способ №1 (основной, самый надёжный): взять путь из ЗАПУЩЕННОГО процесса
/// RailWorks64.exe. Когда игрок в мультиплеере — игра всегда открыта, а её exe
/// лежит ровно в корне RailWorks, на каком бы диске игра ни стояла.
/// Фолбэки, если игра ещё не запущена: реестр Steam + libraryfolders.vdf, затем
/// список типичных путей.
/// </summary>
public static class GamePaths
{
    private static string? _root; // кэш найденного корня

    /// <summary>Корень RailWorks (папка с RailWorks64.exe) или null.</summary>
    public static string? RailWorksRoot()
    {
        if (_root != null && Directory.Exists(_root)) return _root;
        _root = FromProcess() ?? FromSteam() ?? FromFallbacks();
        return _root;
    }

    /// <summary>…\RailWorks\PCIMP\Assets\Signals</summary>
    public static string? SignalsDir()
    {
        var r = RailWorksRoot();
        return r == null ? null : Path.Combine(r, "PCIMP", "Assets", "Signals");
    }

    /// <summary>…\RailWorks\PCIMP\Assets\Switches\Scripts\switches_registry.json</summary>
    public static string? SwitchesRegistry()
    {
        var r = RailWorksRoot();
        return r == null ? null : Path.Combine(r, "PCIMP", "Assets", "Switches", "Scripts", "switches_registry.json");
    }

    /// <summary>…\RailWorks\plugins\RailDriver64.dll (или null).</summary>
    public static string? RailDriverDll()
    {
        var r = RailWorksRoot();
        if (r == null) return null;
        var p = Path.Combine(r, "plugins", "RailDriver64.dll");
        return File.Exists(p) ? p : null;
    }

    // ── способ 1: из запущенного процесса игры ──────────────────────────
    private static string? FromProcess()
    {
        foreach (var name in new[] { "RailWorks64", "RailWorks" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var exe = p.MainModule?.FileName;   // …\RailWorks\RailWorks64.exe
                    if (!string.IsNullOrEmpty(exe))
                    {
                        var dir = Path.GetDirectoryName(exe);
                        if (dir != null && LooksLikeRoot(dir)) return dir;
                    }
                }
                catch { /* нет доступа к модулю — пробуем дальше */ }
                finally { p.Dispose(); }
            }
        }
        return null;
    }

    // ── фолбэк: Steam (реестр + библиотеки на всех дисках) ──────────────
    private static string? FromSteam()
    {
        try
        {
            using var reg = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Valve\Steam");
            if (reg?.GetValue("InstallPath") is not string steam) return null;

            // основная библиотека
            var main = Path.Combine(steam, "steamapps", "common", "RailWorks");
            if (LooksLikeRoot(main)) return main;

            // дополнительные библиотеки из libraryfolders.vdf
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                var text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                {
                    var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                    var cand = Path.Combine(lib, "steamapps", "common", "RailWorks");
                    if (LooksLikeRoot(cand)) return cand;
                }
            }
        }
        catch { }
        return null;
    }

    // ── фолбэк: типичные пути ───────────────────────────────────────────
    private static string? FromFallbacks()
    {
        string[] roots =
        {
            @"D:\My Steam\steamapps\common\RailWorks",
            @"D:\Steam\steamapps\common\RailWorks",
            @"E:\My Steam\steamapps\common\RailWorks",
            @"E:\Steam\steamapps\common\RailWorks",
            @"C:\Program Files (x86)\Steam\steamapps\common\RailWorks",
            @"C:\Program Files\Steam\steamapps\common\RailWorks",
        };
        foreach (var r in roots) if (LooksLikeRoot(r)) return r;
        return null;
    }

    private static bool LooksLikeRoot(string dir)
    {
        try { return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "RailWorks64.exe")); }
        catch { return false; }
    }
}
