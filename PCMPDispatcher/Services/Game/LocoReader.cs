using System;
using System.Collections.Generic;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Чтение позиции СВОЕГО локомотива из памяти игры.
///
/// Механика (проверена в рабочем Python TrainReplay):
///   1. AOB-скан осей — у каждой тележки (cTrackFollower) в памяти лежит
///      сигнатура из 8 байт. Отбираем те, у кого флаг «на рельсах» (+0x44==1)
///      и высота ≈ 124.75 (уровень пути этого маршрута).
///   2. Пара осей на расстоянии ≈ 8.19 м (колёсная база локо) = наш локомотив.
///   3. Снимок: усредняем глобальные координаты передней и задней оси, берём
///      высоту и курс (из sin/cos), получаем X, Z, Y, ROT в мировых координатах.
///
/// Глобальная координата = тайл × 1024 + оффсет внутри тайла (движок хранит мир
/// плитками по 1024 м). Курс собирается из синуса (+0x30) и косинуса (+0x38).
/// </summary>
public sealed class LocoReader
{
    // Сигнатура оси cTrackFollower
    private static readonly byte[] Aob = { 0x28, 0x07, 0x28, 0x81, 0x01, 0x00, 0x00, 0x00 };

    private const float  YTarget = 124.75f; // высота пути на маршруте
    private const float  YTol    = 8.0f;    // допуск по высоте
    private const double Gap      = 8.19;   // колёсная база локо, м
    private const double GapTol   = 0.8;    // допуск расстояния между осями

    private readonly GameMemory _mem;

    public LocoReader(GameMemory mem) => _mem = mem;

    /// <summary>Позиция локомотива в мировых координатах.</summary>
    public readonly record struct Snapshot(double X, double Z, double Y, double Rot);

    /// <summary>Пара адресов осей (перед/зад), опознанная как локомотив.</summary>
    public readonly record struct AxlePair(ulong Front, ulong Rear);

    // ── глобальная позиция оси (тайл×1024 + оффсет) ─────────────────────
    private (double gx, double gz) AxleGlobal(ulong a)
    {
        int tx = _mem.ReadInt(a + 0x20); float ox = _mem.ReadFloat(a + 0x24);
        int tz = _mem.ReadInt(a + 0x28); float oz = _mem.ReadFloat(a + 0x2C);
        return (tx * 1024.0 + ox, tz * 1024.0 + oz);
    }

    // ── скан всех осей игрока (на рельсах, нужной высоты) ────────────────
    private List<ulong> ScanAxles()
    {
        var axles = new List<ulong>();
        // оси живут высоко в адресном пространстве — стартуем с 0x40000000
        foreach (var reg in _mem.EnumerateRegions(floor: 0x40000000))
        {
            var data = _mem.Read(reg.Base, (int)reg.Size);
            if (data == null) continue;

            var span = new ReadOnlySpan<byte>(data);
            int start = 0;
            while (true)
            {
                int idx = span.Slice(start).IndexOf(Aob);
                if (idx < 0) break;
                int at = start + idx;
                ulong a = reg.Base + (ulong)at;

                // флаг «на рельсах»
                var flag = _mem.Read(a + 0x44, 1);
                if (flag != null && flag[0] == 1)
                {
                    float y = _mem.ReadFloat(a + 0x4C);
                    if (Math.Abs(y - YTarget) < YTol) axles.Add(a);
                }
                start = at + 1;
            }
        }
        return axles;
    }

    /// <summary>
    /// Найти локомотив = пара осей на дистанции колёсной базы. Возвращает
    /// null, если поезд ещё не заспавнен / не на рельсах.
    /// </summary>
    public AxlePair? FindLoco()
    {
        var axles = ScanAxles();
        var g = new (double x, double z)[axles.Count];
        for (int i = 0; i < axles.Count; i++) g[i] = AxleGlobal(axles[i]);

        AxlePair? best = null;
        double bestD = 999.0;
        for (int i = 0; i < axles.Count; i++)
        for (int j = 0; j < axles.Count; j++)
        {
            if (i == j) continue;
            double dx = g[j].x - g[i].x, dz = g[j].z - g[i].z;
            double gap = Math.Sqrt(dx * dx + dz * dz);
            double d = Math.Abs(gap - Gap);
            if (d < bestD && d <= GapTol)
            {
                bestD = d;
                best = new AxlePair(axles[i], axles[j]);
            }
        }
        return best;
    }

    /// <summary>Снять текущую позицию локомотива по известной паре осей.</summary>
    public Snapshot Read(AxlePair pair)
    {
        var (gxf, gzf) = AxleGlobal(pair.Front);
        var (gxr, gzr) = AxleGlobal(pair.Rear);
        double gx = (gxf + gxr) / 2.0;
        double gz = (gzf + gzr) / 2.0;
        double gy = (_mem.ReadFloat(pair.Front + 0x4C) + _mem.ReadFloat(pair.Rear + 0x4C)) / 2.0;
        double sinH = (_mem.ReadFloat(pair.Front + 0x30) + _mem.ReadFloat(pair.Rear + 0x30)) / 2.0;
        double cosH = (_mem.ReadFloat(pair.Front + 0x38) + _mem.ReadFloat(pair.Rear + 0x38)) / 2.0;
        double rot = Math.Atan2(sinH, cosH) * 180.0 / Math.PI;
        return new Snapshot(gx, gz, gy, rot);
    }
}
