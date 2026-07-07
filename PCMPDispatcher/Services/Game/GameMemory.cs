using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Низкоуровневый доступ к памяти процесса игры (RailWorks64.exe).
///
/// Это построчный перенос того, что делал Python через ctypes: те же самые
/// функции WinAPI (OpenProcess / ReadProcessMemory / WriteProcessMemory /
/// VirtualQueryEx), только нативно из C# и заметно быстрее. Игра 64-битная,
/// поэтому все адреса — 64-битные (ulong).
///
/// Класс держит один открытый хэндл процесса и умеет:
///   • читать/писать примитивы (float/int/long) и блоки байт;
///   • перечислять committed-регионы памяти (для AOB-сканов);
///   • отдавать базу модуля (для валидации объектов по vtable).
///
/// Ничего в игру он сам не пишет — только предоставляет инструменты; вся
/// логика (поиск осей, объектов, запись позиций) живёт в слоях выше.
/// </summary>
public sealed class GameMemory : IDisposable
{
    public const string ProcessName = "RailWorks64.exe";

    // ── WinAPI ───────────────────────────────────────────────────────────
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr h, ulong addr, byte[] buf, ulong size, out ulong read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr h, ulong addr, byte[] buf, ulong size, out ulong written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ulong VirtualQueryEx(
        IntPtr h, ulong addr, out MEMORY_BASIC_INFORMATION mbi, ulong len);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public uint  AllocationProtect;
        public ulong RegionSize;
        public uint  State;
        public uint  Protect;
        public uint  Type;
    }

    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint MEM_COMMIT   = 0x1000;
    private const uint MEM_PRIVATE  = 0x20000;
    // читаемые/пишемые страницы: READONLY, READWRITE, EXECUTE_READ, EXECUTE_READWRITE
    private static bool Readable(uint p) => p is 0x04 or 0x08 or 0x40 or 0x80;

    private const ulong ScanFloor   = 0x1000000;
    private const ulong ScanCeiling = 0x800000000;
    private const ulong MaxRegion   = 128UL * 1024 * 1024; // не читаем гигантские регионы

    // ── состояние ────────────────────────────────────────────────────────
    private IntPtr _handle;
    public int  Pid  { get; private set; }
    public bool IsOpen => _handle != IntPtr.Zero;

    /// <summary>Найти процесс игры и открыть его. false — игра не запущена.</summary>
    public bool Open()
    {
        Close();
        var procs = Process.GetProcessesByName("RailWorks64");
        if (procs.Length == 0) return false;
        Pid = procs[0].Id;
        _handle = OpenProcess(PROCESS_ALL_ACCESS, false, (uint)Pid);
        foreach (var pr in procs) pr.Dispose();
        return _handle != IntPtr.Zero;
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero) { CloseHandle(_handle); _handle = IntPtr.Zero; }
        Pid = 0;
    }

    public void Dispose() => Close();

    // ── чтение ───────────────────────────────────────────────────────────
    /// <summary>Прочитать size байт по адресу. null — не удалось (регион ушёл).</summary>
    public byte[]? Read(ulong addr, int size)
    {
        var buf = new byte[size];
        return ReadProcessMemory(_handle, addr, buf, (ulong)size, out var n) && n == (ulong)size
            ? buf : null;
    }

    public float ReadFloat(ulong addr)
    {
        var b = Read(addr, 4);
        return b != null ? BitConverter.ToSingle(b, 0) : 0f;
    }

    public int ReadInt(ulong addr)
    {
        var b = Read(addr, 4);
        return b != null ? BitConverter.ToInt32(b, 0) : 0;
    }

    public ulong ReadUInt64(ulong addr)
    {
        var b = Read(addr, 8);
        return b != null ? BitConverter.ToUInt64(b, 0) : 0UL;
    }

    // ── запись ───────────────────────────────────────────────────────────
    /// <summary>Записать блок байт по адресу одним вызовом (атомарно для движка).</summary>
    public bool Write(ulong addr, byte[] data)
        => WriteProcessMemory(_handle, addr, data, (ulong)data.Length, out var n) && n == (ulong)data.Length;

    // ── регионы памяти ───────────────────────────────────────────────────
    public readonly record struct Region(ulong Base, ulong Size, uint Type);

    /// <summary>
    /// Перечислить committed-регионы, годные для сканирования (читаемые, не
    /// гигантские). privateOnly=true → только private-heap (быстрый первый
    /// проход, объекты игры живут там). Порядок и фильтры — как в Python.
    /// </summary>
    public IEnumerable<Region> EnumerateRegions(ulong floor = ScanFloor, bool privateOnly = false)
    {
        ulong addr = floor;
        while (addr < ScanCeiling)
        {
            if (VirtualQueryEx(_handle, addr, out var mbi, (ulong)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                break;
            ulong bas = mbi.BaseAddress, size = mbi.RegionSize;
            if (size == 0) break;

            bool ok = mbi.State == MEM_COMMIT && Readable(mbi.Protect) && size <= MaxRegion;
            if (privateOnly) ok = ok && mbi.Type == MEM_PRIVATE;
            if (ok) yield return new Region(bas, size, mbi.Type);

            addr = bas + size;
        }
    }

    // ── база модуля (для vtable-валидации объектов) ──────────────────────
    private ulong _moduleBase;

    /// <summary>Базовый адрес модуля RailWorks64.exe (кэшируется).</summary>
    public ulong ModuleBase
    {
        get
        {
            if (_moduleBase != 0) return _moduleBase;
            try
            {
                using var p = Process.GetProcessById(Pid);
                foreach (ProcessModule m in p.Modules)
                {
                    if (string.Equals(m.ModuleName, ProcessName, StringComparison.OrdinalIgnoreCase))
                    {
                        _moduleBase = (ulong)m.BaseAddress.ToInt64();
                        break;
                    }
                }
            }
            catch { }
            if (_moduleBase == 0) _moduleBase = 0x140000000; // дефолт как в Python
            return _moduleBase;
        }
    }

    /// <summary>Базовый адрес произвольного модуля процесса по имени (напр.
    /// "GameManagerVC64.dll"). 0 — модуль не найден. Нужен для стрелок: их
    /// менеджер живёт в отдельной DLL, а не в основном exe.</summary>
    public ulong GetModuleBase(string moduleName)
    {
        try
        {
            using var p = Process.GetProcessById(Pid);
            foreach (ProcessModule m in p.Modules)
                if (string.Equals(m.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    return (ulong)m.BaseAddress.ToInt64();
        }
        catch { }
        return 0;
    }
}
