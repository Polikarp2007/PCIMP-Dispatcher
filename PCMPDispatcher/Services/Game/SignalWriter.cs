using System;
using System.Collections.Generic;
using System.IO;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Пишет аспекты светофоров в txt-файлы, которые читает игра. Управление
/// сигналами идёт НЕ через память, а через файлы:
///   &lt;RailWorks&gt;\PCIMP\Assets\Signals\&lt;Station&gt;\&lt;NAME&gt;\&lt;NAME&gt;.txt
/// внутри — одно слово-аспект (RED / YELLOW / GREEN / …).
///
/// Сервер шлёт карту {id: aspect}, где id = "Station/NAME" (например
/// "Radna/RADNA_X1_IES"). Пишем только если значение изменилось, чтобы не
/// дёргать диск каждую секунду.
/// </summary>
public sealed class SignalWriter
{
    // последнее записанное значение по id — чтобы не переписывать без нужды
    private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Есть ли доступная папка Signals (игра найдена).</summary>
    public bool Ready => GamePaths.SignalsDir() is { } d && Directory.Exists(d);

    /// <summary>Записать аспект в файл сигнала. id = "Station/NAME".</summary>
    public bool Write(string id, string aspect)
    {
        if (_last.TryGetValue(id, out var prev) && prev == aspect) return true; // без изменений
        var path = FilePath(id);
        if (path == null) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, aspect);
            _last[id] = aspect;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Применить всю карту {id: aspect}.</summary>
    public void WriteAll(IReadOnlyDictionary<string, string> aspects)
    {
        foreach (var (id, aspect) in aspects) Write(id, aspect);
    }

    // "Radna/RADNA_X1_IES" → …\Signals\Radna\RADNA_X1_IES\RADNA_X1_IES.txt
    private static string? FilePath(string id)
    {
        var dir = GamePaths.SignalsDir();
        if (dir == null) return null;
        id = id.Replace('\\', '/').Trim('/');
        var leaf = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id;
        var parts = id.Split('/');
        var full = dir;
        foreach (var seg in parts) full = Path.Combine(full, seg);
        return Path.Combine(full, leaf + ".txt");
    }
}
