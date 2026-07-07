using System;
using System.Collections.Generic;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Поиск и перемещение объектов сцены (вагонов/локомотивов пула) по их GUID.
///
/// Механика (проверена в рабочем Python TrainReplay):
///   • Находим объект по 16 байтам его GUID в памяти.
///   • По цепочке указателей выходим на структуру cPosOri: guid-0x88 → subobj,
///     затем перебираем поля subobj (0x00..0x20) и берём то, где реально лежит
///     cPosOri — валидация СТРОГО по подписи vtable (module_base + RVA). Это
///     отсекает cSceneryRender и прочий мусор с похожими байтами.
///   • Пишем позицию: матрица поворота 3×4 + тайл/оффсет/высота одним
///     WriteProcessMemory в posori+0x38. Один вызов = атомарно для движка,
///     иначе culling ловит полу-обновление и объект мерцает.
///
/// ВАЖНО: перед выходом из сценария объекты нужно вернуть на исходные позиции
/// (RestoreOriginals) — иначе движок крашится при выгрузке на сдвинутых
/// координатах.
/// </summary>
public sealed class ObjectWriter
{
    // Стабильная подпись cPosOri: vtable = module_base + этот RVA (одинаков у всех cPosOri)
    private const ulong CposoriVtableRva = 0x41267780;

    private readonly GameMemory _mem;
    private ulong _cposoriVtable;

    public ObjectWriter(GameMemory mem) => _mem = mem;

    private ulong CposoriVtable => _cposoriVtable != 0
        ? _cposoriVtable
        : (_cposoriVtable = _mem.ModuleBase + CposoriVtableRva);

    /// <summary>Найденный объект: адрес cPosOri (+ доп. узлы, если есть).</summary>
    public readonly record struct GameObject(ulong PosOri, ulong LodNode, ulong SceneNode);

    /// <summary>Позиция объекта в мире (для запоминания исходных).</summary>
    public readonly record struct ObjPos(ulong PosOri, ulong LodNode,
                                         double X, double Z, double Y, double Rot);

    // ── GUID «string» → 16 байт в порядке, как хранит движок ────────────
    public static byte[] GuidToBytes(string guid)
    {
        string p = guid.Replace("-", "");
        byte[] Rev(int a, int len)
        {
            var b = new byte[len];
            for (int i = 0; i < len; i++)
                b[i] = Convert.ToByte(p.Substring((a + i) * 2, 2), 16);
            Array.Reverse(b);
            return b;
        }
        byte[] Raw(int a, int len)
        {
            var b = new byte[len];
            for (int i = 0; i < len; i++)
                b[i] = Convert.ToByte(p.Substring((a + i) * 2, 2), 16);
            return b;
        }
        // первые три группы — little-endian (reverse), последние две — как есть
        var res = new byte[16];
        int o = 0;
        foreach (var chunk in new[] { Rev(0, 4), Rev(4, 2), Rev(6, 2), Raw(8, 8) })
        {
            Array.Copy(chunk, 0, res, o, chunk.Length);
            o += chunk.Length;
        }
        return res;
    }

    // ── валидация: по адресу реально лежит cPosOri (по vtable) ───────────
    private bool IsCposori(ulong posori)
        => posori > 0x1000000 && posori < 0xF00000000 && _mem.ReadUInt64(posori) == CposoriVtable;

    // ── по адресу GUID → cPosOri (+ узлы), строго по vtable ──────────────
    private GameObject? Resolve(ulong guidAddr)
    {
        ulong subobj = _mem.ReadUInt64(guidAddr - 0x88);
        if (!(subobj > 0x1000000 && subobj < 0xF00000000)) return null;

        foreach (ulong poff in new ulong[] { 0x00, 0x08, 0x10, 0x18, 0x20 })
        {
            ulong posori = _mem.ReadUInt64(subobj + poff);
            if (!IsCposori(posori)) continue;

            ulong lod = _mem.ReadUInt64(subobj + 0xD0);
            if (!(lod > 0x100000 && lod < 0xF00000000)) lod = 0;
            ulong scene = _mem.ReadUInt64(subobj + 0x08);
            if (!(scene > 0x100000 && scene < 0xF00000000)) scene = 0;
            return new GameObject(posori, lod, scene);
        }
        return null;
    }

    /// <summary>
    /// Найти сразу ВСЕ объекты по их GUID за проходы памяти. guidMap: label→guid.
    /// Сначала быстрый проход по private-heap, затем полный. Возвращает найденные.
    /// </summary>
    public Dictionary<string, GameObject> FindAll(IReadOnlyDictionary<string, string> guidMap)
    {
        // GUID-байты → список меток (один GUID может встречаться у дублей)
        var gbToLabels = new Dictionary<string, List<string>>();
        var gbBytes = new Dictionary<string, byte[]>();
        foreach (var (label, guid) in guidMap)
        {
            var gb = GuidToBytes(guid);
            string key = Convert.ToHexString(gb);
            if (!gbToLabels.TryGetValue(key, out var list))
            {
                gbToLabels[key] = list = new List<string>();
                gbBytes[key] = gb;
            }
            list.Add(label);
        }

        var found = new Dictionary<string, GameObject>();

        void Sweep(bool privateOnly)
        {
            if (found.Count >= guidMap.Count) return;
            foreach (var reg in _mem.EnumerateRegions(privateOnly: privateOnly))
            {
                var data = _mem.Read(reg.Base, (int)reg.Size);
                if (data == null) continue;
                var span = new ReadOnlySpan<byte>(data);

                foreach (var (key, labels) in gbToLabels)
                {
                    bool allFound = true;
                    foreach (var l in labels) if (!found.ContainsKey(l)) { allFound = false; break; }
                    if (allFound) continue;

                    var gb = gbBytes[key];
                    int start = 0;
                    while (true)
                    {
                        int idx = span.Slice(start).IndexOf(gb);
                        if (idx < 0) break;
                        int at = start + idx;
                        var obj = Resolve(reg.Base + (ulong)at);
                        if (obj != null)
                        {
                            foreach (var l in labels)
                                if (!found.ContainsKey(l)) found[l] = obj.Value;
                            break;
                        }
                        start = at + 1;
                    }
                }
                if (found.Count >= guidMap.Count) return;
            }
        }

        Sweep(privateOnly: true);   // быстрый проход по heap
        Sweep(privateOnly: false);  // полный проход
        return found;
    }

    /// <summary>Найти один объект по GUID.</summary>
    public GameObject? Find(string guid)
    {
        var r = FindAll(new Dictionary<string, string> { ["_"] = guid });
        return r.TryGetValue("_", out var o) ? o : null;
    }

    // ── чтение текущей позиции объекта ──────────────────────────────────
    public ObjPos ReadPos(GameObject obj)
    {
        int tx = _mem.ReadInt(obj.PosOri + 0x68); float ox = _mem.ReadFloat(obj.PosOri + 0x6C);
        int tz = _mem.ReadInt(obj.PosOri + 0x70); float oz = _mem.ReadFloat(obj.PosOri + 0x74);
        float gy = _mem.ReadFloat(obj.PosOri + 0x78);
        float sy = _mem.ReadFloat(obj.PosOri + 0x58);
        float cy = _mem.ReadFloat(obj.PosOri + 0x60);
        double rot = Math.Atan2(sy, cy) * 180.0 / Math.PI;
        return new ObjPos(obj.PosOri, obj.LodNode, tx * 1024.0 + ox, tz * 1024.0 + oz, gy, rot);
    }

    // ── запись позиции (матрица yaw+roll + тайл/оффсет/высота) ───────────
    /// <summary>
    /// Поставить объект в мировую позицию (gx, gz, gy) с курсом rotDeg и креном
    /// rollDeg. Весь блок 0x38..0x7B пишется одним вызовом (атомарно).
    /// </summary>
    public void Write(ulong posori, double gx, double gz, double gy, double rotDeg, double rollDeg = 0.0)
    {
        long tx = (long)Math.Floor(gx / 1024.0);
        long tz = (long)Math.Floor(gz / 1024.0);
        double ox = gx - tx * 1024.0;
        double oz = gz - tz * 1024.0;

        double y = rotDeg * Math.PI / 180.0; double sy = Math.Sin(y), cy = Math.Cos(y);
        double r = rollDeg * Math.PI / 180.0; double sr = Math.Sin(r), cr = Math.Cos(r);

        // Матрица (см. Python write_obj): row0=(cr·cy, sr, -cr·sy), row1=(-sr·cy, cr, sr·sy), row2=(sy,0,cy)
        var buf = new byte[12 * 4 + 4 + 4 + 4 + 4 + 4]; // 12 float + i f i f f
        int p = 0;
        void F(double v) { BitConverter.GetBytes((float)v).CopyTo(buf, p); p += 4; }
        void I(long v)   { BitConverter.GetBytes((int)v).CopyTo(buf, p);   p += 4; }

        F(cr * cy); F(sr);  F(-cr * sy); F(0.0); // row0
        F(-sr * cy); F(cr); F(sr * sy);  F(0.0); // row1
        F(sy);      F(0.0); F(cy);       F(0.0); // row2
        I(tx); F(ox); I(tz); F(oz); F(gy);        // tile/offset/Y

        _mem.Write(posori + 0x38, buf);
    }

    public void Write(GameObject obj, double gx, double gz, double gy, double rotDeg, double rollDeg = 0.0)
        => Write(obj.PosOri, gx, gz, gy, rotDeg, rollDeg);

    // ── исходные позиции: запомнить и вернуть перед выходом ──────────────
    public List<ObjPos> CaptureOriginals(IEnumerable<GameObject> objs)
    {
        var list = new List<ObjPos>();
        foreach (var o in objs) list.Add(ReadPos(o));
        return list;
    }

    /// <summary>Вернуть объекты на исходные позиции (ОБЯЗАТЕЛЬНО перед выгрузкой).</summary>
    public void RestoreOriginals(IEnumerable<ObjPos> originals)
    {
        foreach (var o in originals) Write(o.PosOri, o.X, o.Z, o.Y, o.Rot, 0.0);
    }
}
