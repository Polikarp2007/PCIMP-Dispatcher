using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Перевод стрелок (turnouts) в памяти игры — построчный перенос рабочего
/// switch_control.py.
///
/// Стрелки живут НЕ там, где вагоны: ими владеет менеджер в отдельной библиотеке
/// GameManagerVC64.dll. От её базы по фиксированному смещению лежит указатель на
/// менеджер, а в нём — связный список всех загруженных стрелок. Проходим список,
/// сверяем 16 байт GUID каждой стрелки с реестром, и для нужных пишем состояние.
///
/// Реестр (switches_registry.json) на каждую стрелку хранит сырые байты, которые
/// надо записать для LEFT и для RIGHT: два float-поля, два state-поля и «phys»
/// (int на структуре tp — по нему же читаем текущее положение). Пишем трижды с
/// паузой 50 мс, как в Python, чтобы движок гарантированно принял новое значение.
///
/// GUID в реестре — уже сырые байты из памяти (в том порядке, как их отдаёт
/// движок), поэтому сравниваем напрямую, без переупаковки как у объектов сцены.
/// </summary>
public sealed class SwitchWriter
{
    private const string Dll = "GameManagerVC64.dll";

    // Смещения (из switch_control.py) — стабильны для текущей сборки игры.
    private const ulong MGR_PTR_OFF   = 0x17ACC80; // dll_base + off → указатель на менеджер
    private const ulong LIST_OFF      = 0x50;      // менеджер + off → голова списка
    private const ulong NODE_TP       = 0x18;      // узел + off → tp (structure)
    private const ulong NODE_OBJ      = 0x38;      // узел + off → obj (анимируемая часть)
    private const ulong PHYS_OFF      = 0x64;      // tp + off → phys (int, текущее положение)
    private const ulong OWNED_ENT_OFF = 0x48;      // tp + off → сущность
    private const ulong GUID_OFF      = 0xB8;      // сущность + off → 16 байт GUID
    private const ulong A_FLOAT1      = 0x18;      // obj + off
    private const ulong A_FLOAT2      = 0x1C;
    private const ulong A_STATE1      = 0x40;
    private const ulong A_STATE2      = 0x44;

    /// <summary>Одна запись реестра: что писать для LEFT / RIGHT.</summary>
    public sealed class Entry
    {
        public byte[] Guid   = Array.Empty<byte>(); // 16 сырых байт
        public int    LPhys, RPhys;
        public byte[] LFloat = Array.Empty<byte>(); // 4 байта
        public byte[] RFloat = Array.Empty<byte>();
        public byte[] LState = Array.Empty<byte>();
        public byte[] RState = Array.Empty<byte>();
    }

    private readonly GameMemory _mem;
    private ulong _dllBase;
    private readonly Dictionary<string, Entry> _reg = new(StringComparer.OrdinalIgnoreCase);

    public SwitchWriter(GameMemory mem) => _mem = mem;

    public int  Count => _reg.Count;
    public bool Ready => _dllBase != 0 && _reg.Count > 0;

    /// <summary>Найти базу GameManagerVC64.dll. false — DLL ещё не загружена.</summary>
    public bool ResolveModule()
    {
        _dllBase = _mem.GetModuleBase(Dll);
        return _dllBase != 0;
    }

    public Entry? Get(string name) => _reg.TryGetValue(name, out var e) ? e : null;

    // ── загрузка реестра из JSON ─────────────────────────────────────────
    public void LoadRegistry(string path)
    {
        _reg.Clear();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var o = prop.Value;
            _reg[prop.Name] = new Entry
            {
                Guid   = Bytes(o, "guid"),
                LPhys  = o.TryGetProperty("l_phys", out var lp) ? lp.GetInt32() : 1,
                RPhys  = o.TryGetProperty("r_phys", out var rp) ? rp.GetInt32() : 0,
                LFloat = Bytes(o, "l_float"),
                RFloat = Bytes(o, "r_float"),
                LState = Bytes(o, "l_state"),
                RState = Bytes(o, "r_state"),
            };
        }
    }

    private static byte[] Bytes(JsonElement o, string key)
    {
        if (!o.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<byte>();
        var list = new List<byte>();
        foreach (var el in arr.EnumerateArray()) list.Add((byte)el.GetInt32());
        return list.ToArray();
    }

    // ── проход связного списка: найти загруженные известные нам стрелки ───
    private static bool RqValid(ulong v) => v > 0x10000 && v < 0x7FFFFFFFFFFF;
    private ulong Rq(ulong addr) { ulong v = _mem.ReadUInt64(addr); return RqValid(v) ? v : 0; }

    /// <summary>Метка → (tp, obj) для всех стрелок из реестра, что сейчас в сцене.</summary>
    public Dictionary<string, (ulong tp, ulong obj)> Scan()
    {
        var found = new Dictionary<string, (ulong, ulong)>();
        ulong mgr = Rq(_dllBase + MGR_PTR_OFF);
        if (mgr == 0) return found;

        ulong listHead = mgr + LIST_OFF;
        ulong node = Rq(listHead);
        var visited = new HashSet<ulong>();
        while (node != 0 && node != listHead && visited.Add(node))
        {
            ulong tp  = Rq(node + NODE_TP);
            ulong obj = Rq(node + NODE_OBJ);
            if (tp != 0 && obj != 0)
            {
                ulong oe = Rq(tp + OWNED_ENT_OFF);
                if (oe != 0)
                {
                    var guid = _mem.Read(oe + GUID_OFF, 16);
                    if (guid != null)
                    {
                        var name = MatchGuid(guid);
                        if (name != null && !found.ContainsKey(name)) found[name] = (tp, obj);
                    }
                }
            }
            node = Rq(node);
        }
        return found;
    }

    private string? MatchGuid(byte[] guid)
    {
        foreach (var (name, e) in _reg)
            if (BytesEqual(e.Guid, guid)) return name;
        return null;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ── чтение текущего / запись нового положения ────────────────────────
    /// <summary>Текущее положение стрелки: "LEFT" или "RIGHT" (по phys на tp).</summary>
    public string ReadPosition(ulong tp, Entry e)
        => _mem.ReadInt(tp + PHYS_OFF) == e.LPhys ? "LEFT" : "RIGHT";

    /// <summary>Перевести стрелку в положение "LEFT"/"RIGHT" (пишем трижды).</summary>
    public void Apply(ulong tp, ulong obj, Entry e, string position)
    {
        bool left = position == "LEFT";
        byte[] fb = left ? e.LFloat : e.RFloat;
        byte[] sb = left ? e.LState : e.RState;
        byte[] phys = BitConverter.GetBytes(left ? e.LPhys : e.RPhys);

        for (int i = 0; i < 3; i++)
        {
            _mem.Write(obj + A_FLOAT1, fb);
            _mem.Write(obj + A_FLOAT2, fb);
            _mem.Write(obj + A_STATE1, sb);
            _mem.Write(obj + A_STATE2, sb);
            _mem.Write(tp + PHYS_OFF, phys);
            Thread.Sleep(50);
        }
    }
}
