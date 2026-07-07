using System;
using System.Collections.Generic;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// «След» состава — накопленный путь головы (локомотива) с длиной дуги, по
/// которому расставляются вагоны позади, друг за другом, держа дистанцию.
///
/// Голова добавляет точки по мере движения (AddHead). Любой вагон ставится на
/// точку, отстоящую на нужное число метров НАЗАД по фактически пройденному
/// пути (SampleBehind) — поэтому вагоны идут строго по рельсам, а на поворотах
/// не срезают. Старый хвост следа подрезается, чтобы память не росла.
///
/// Дистанции сцепки взяты из рабочего Python (замерены по маршруту):
///   локомотив → первый вагон = 23.18 м, дальше вагон → вагон = 26.27 м.
/// </summary>
public sealed class ConsistTrail
{
    public const double LocoToWagon  = 23.18; // локо → вагон1
    public const double WagonToWagon = 26.27; // вагон → вагон

    private const double MinStep = 0.05;  // добавляем точку не чаще, чем раз в 5 см
    private const double KeepArc = 600.0; // храним последние 600 м пути

    private readonly List<Point> _pts = new();
    private double _arc;

    private readonly record struct Point(double X, double Z, double Y, double Rot, double S);

    /// <summary>Смещение вагона №i (0-based) назад по дуге от головы.</summary>
    public static double WagonOffset(int i) => LocoToWagon + i * WagonToWagon;

    /// <summary>Текущая длина накопленного пути головы.</summary>
    public double HeadArc => _arc;

    public bool IsEmpty => _pts.Count == 0;

    public void Clear() { _pts.Clear(); _arc = 0; }

    /// <summary>Добавить новую позицию головы (локомотива).</summary>
    public void AddHead(double x, double z, double y, double rot)
    {
        if (_pts.Count == 0)
        {
            _pts.Add(new Point(x, z, y, rot, 0.0));
            return;
        }
        var last = _pts[^1];
        double d = Math.Sqrt((x - last.X) * (x - last.X) + (z - last.Z) * (z - last.Z));
        if (d < MinStep) return;

        _arc += d;
        _pts.Add(new Point(x, z, y, rot, _arc));
        while (_pts.Count > 2 && _arc - _pts[0].S > KeepArc) _pts.RemoveAt(0);
    }

    /// <summary>Позиция в мире.</summary>
    public readonly record struct Pose(double X, double Z, double Y, double Rot);

    /// <summary>Точка в metersBehind метрах позади головы по дуге.</summary>
    public Pose SampleBehind(double metersBehind) => SampleAt(_arc - metersBehind);

    /// <summary>Интерполированная точка на дуге targetS (с экстраполяцией за концы).</summary>
    public Pose SampleAt(double targetS)
    {
        if (_pts.Count == 0) return default;

        // Раньше начала следа: достраиваем ПРЯМУЮ линию назад по курсу первой
        // точки. Именно это даёт правильный спавн — вагоны стоят прямой цепочкой
        // ЗА локомотивом (как SPAWN_AT_START=False в исходном скрипте), а не
        // сваливаются в одну точку. Как только след накопится — пойдут по рельсам.
        if (targetS <= _pts[0].S)
        {
            var f = _pts[0];
            double back = f.S - targetS;                 // сколько метров за началом
            double rad = f.Rot * Math.PI / 180.0;
            double sdx = Math.Sin(rad), sdz = Math.Cos(rad);
            return new Pose(f.X - sdx * back, f.Z - sdz * back, f.Y, f.Rot);
        }
        if (targetS >= _pts[^1].S) { var l = _pts[^1]; return new Pose(l.X, l.Z, l.Y, l.Rot); }

        int lo = 0, hi = _pts.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (_pts[mid].S <= targetS) lo = mid; else hi = mid;
        }
        var a = _pts[lo]; var b = _pts[hi];
        double span = b.S - a.S;
        double t = span <= 0 ? 0 : (targetS - a.S) / span;
        double dr = ((b.Rot - a.Rot + 180.0) % 360.0) - 180.0; // угол без скачка через ±180
        return new Pose(
            a.X + (b.X - a.X) * t,
            a.Z + (b.Z - a.Z) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Rot + dr * t);
    }
}
