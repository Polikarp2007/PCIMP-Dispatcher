using System;
using System.Collections.Generic;

namespace PCMPDispatcher.Services.Game;

/// <summary>
/// Поезд ОДНОГО чужого игрока в нашем мире: его локомотив + вагоны (объекты
/// пула, выданные сервером), плюс вся логика плавного движения.
///
/// Плавность («конфетка») достигается двумя приёмами:
///   • Интерполяция сущности: входящие по сети позиции (≈30 Гц, неровно из-за
///     джиттера) складываем в буфер с метками времени и рисуем локомотив с
///     небольшой задержкой (RenderDelay), интерполируя между кадрами — движок
///     видит гладкое движение даже при рваной сети.
///   • След по дуге: отрисованную позицию локо добавляем в ConsistTrail, а
///     вагоны ставим позади по накопленному пути — они идут по рельсам следом.
///
/// Перед удалением поезда (игрок вышел / отключение) объекты ОБЯЗАТЕЛЬНО
/// возвращаются на исходные места (иначе движок крашится при выгрузке).
/// </summary>
public sealed class RemoteTrain
{
    // На сколько мс назад во времени рисуем — сглаживает сетевой джиттер.
    // 200 мс даёт запас ~6 пакетов (при 30 Гц), движение остаётся плавным даже
    // если часть пакетов пришла с опозданием.
    private const double RenderDelayMs = 200.0;

    // ── Крен в вираж (cant), считается локально из потока курса ──────────
    // Физика как в исходнике replay.py: φ = atan(v·ω/g), где v — скорость,
    // ω — угловая скорость курса. Меряем через ОКНО назад (не мгновенно) —
    // это давит резкие скачки на стрелках. Один крен на весь состав, чтобы
    // локомотив и вагоны заваливались одинаково и слитно.
    private const double RollMax      = 6.0;    // макс. завал, градусы (CFR ~6°)
    private const double RollGain     = -0.9;   // сила/сторона крена
    private const double RollSmooth   = 0.12;   // плавность входа/выхода (0..1)
    private const double RollWindowMs = 500.0;  // окно измерения ω (мс)
    private const double Gravity      = 9.81;

    private double _roll;   // текущий сглаженный крен, градусы

    public string Id { get; }
    public string Nickname { get; }
    private readonly string _locoGuid;
    private readonly string[] _wagonGuids;

    private ObjectWriter.GameObject? _loco;
    private ObjectWriter.GameObject[] _wagons = Array.Empty<ObjectWriter.GameObject>();
    private List<ObjectWriter.ObjPos> _originals = new();
    public bool Resolved { get; private set; }

    public bool LocoFound { get; private set; }
    public int WagonsFound { get; private set; }
    public int WagonsTotal => _wagonGuids.Length;

    private readonly ConsistTrail _trail = new();

    // Буфер интерполяции входящих позиций локомотива.
    private readonly object _bufLock = new();
    private readonly List<Sample> _buf = new();
    private readonly record struct Sample(double T, double X, double Z, double Y, double Rot);

    public RemoteTrain(string id, string nickname, string locoGuid, string[] wagonGuids)
    {
        Id = id; Nickname = nickname;
        _locoGuid = locoGuid; _wagonGuids = wagonGuids;
    }

    /// <summary>
    /// Найти в памяти объекты этого поезда (локо + вагоны) и запомнить их
    /// исходные позиции. Тяжёлый скан — вызывать в фоне, один раз при входе
    /// игрока. false — часть объектов не заспавнена.
    /// </summary>
    public bool Resolve(ObjectWriter writer)
    {
        var map = new Dictionary<string, string> { ["loco"] = _locoGuid };
        for (int i = 0; i < _wagonGuids.Length; i++) map[$"w{i}"] = _wagonGuids[i];

        var found = writer.FindAll(map);
        LocoFound = found.ContainsKey("loco");
        if (!found.TryGetValue("loco", out var loco)) return false;

        var wagons = new List<ObjectWriter.GameObject>();
        for (int i = 0; i < _wagonGuids.Length; i++)
            if (found.TryGetValue($"w{i}", out var w)) wagons.Add(w);
        WagonsFound = wagons.Count;

        _loco = loco;
        _wagons = wagons.ToArray();

        var all = new List<ObjectWriter.GameObject> { loco };
        all.AddRange(_wagons);
        _originals = writer.CaptureOriginals(all);

        Resolved = true;
        return true;
    }

    /// <summary>Принять новую позицию локомотива этого игрока (из сети).</summary>
    public void PushPosition(double x, double z, double y, double rot, double nowMs)
    {
        lock (_bufLock)
        {
            _buf.Add(new Sample(nowMs, x, z, y, rot));
            // держим небольшое окно (≈1.5с), старьё убираем
            while (_buf.Count > 2 && nowMs - _buf[0].T > 1500) _buf.RemoveAt(0);
        }
    }

    /// <summary>
    /// Обновить объекты в игре под текущий момент времени: интерполировать
    /// позицию локо, дописать след, поставить локо и вагоны. Вызывается из
    /// общего рендер-цикла ≈60 Гц.
    /// </summary>
    public void Update(ObjectWriter writer, double nowMs)
    {
        if (!Resolved || _loco == null) return;

        double renderT = nowMs - RenderDelayMs;
        var pose = InterpolateLoco(renderT);
        if (pose == null) return;

        // крен на вираже — единый на весь состав, плавный вход/выход
        UpdateRoll(pose.Value, renderT);

        // локомотив — голова следа
        _trail.AddHead(pose.Value.X, pose.Value.Z, pose.Value.Y, pose.Value.Rot);
        writer.Write(_loco.Value, pose.Value.X, pose.Value.Z, pose.Value.Y, pose.Value.Rot, _roll);

        // вагоны — позади по дуге на дистанциях сцепки (тот же крен)
        for (int i = 0; i < _wagons.Length; i++)
        {
            var p = _trail.SampleBehind(ConsistTrail.WagonOffset(i));
            writer.Write(_wagons[i], p.X, p.Z, p.Y, p.Rot, _roll);
        }
    }

    // Крен из физики виража: смотрим позицию/курс ОКНО назад по времени,
    // получаем скорость v и угловую скорость курса ω, затем φ = atan(v·ω/g).
    // Окно (а не мгновенная разность) гасит рывки на стрелках. Результат
    // плавно подтягиваем к цели (RollSmooth) — крен входит и выходит мягко.
    private void UpdateRoll(ConsistTrail.Pose now, double renderT)
    {
        double dt = RollWindowMs / 1000.0;
        var past = InterpolateLoco(renderT - RollWindowMs);
        double target = 0.0;
        if (past != null)
        {
            double dx = now.X - past.Value.X, dz = now.Z - past.Value.Z;
            double v = Math.Sqrt(dx * dx + dz * dz) / dt;                 // м/с
            double omega = Wrap(now.Rot - past.Value.Rot) * Math.PI / 180.0 / dt; // рад/с
            target = Math.Atan(v * omega / Gravity) * 180.0 / Math.PI * RollGain;
            if (target >  RollMax) target =  RollMax;
            if (target < -RollMax) target = -RollMax;
        }
        _roll += (target - _roll) * RollSmooth;
    }

    private ConsistTrail.Pose? InterpolateLoco(double renderT)
    {
        lock (_bufLock)
        {
            int n = _buf.Count;
            if (n == 0) return null;
            if (n == 1) return new ConsistTrail.Pose(_buf[0].X, _buf[0].Z, _buf[0].Y, _buf[0].Rot);

            if (renderT <= _buf[0].T)
                return new ConsistTrail.Pose(_buf[0].X, _buf[0].Z, _buf[0].Y, _buf[0].Rot);
            var last = _buf[^1];
            if (renderT >= last.T)
                return new ConsistTrail.Pose(last.X, last.Z, last.Y, last.Rot);

            // отрезок [i, i+1], содержащий renderT
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_buf[mid].T <= renderT) lo = mid; else hi = mid;
            }
            int i1 = lo, i2 = hi;
            // соседи для Катмулл-Рома (с зажимом на концах)
            var p0 = _buf[Math.Max(i1 - 1, 0)];
            var p1 = _buf[i1];
            var p2 = _buf[i2];
            var p3 = _buf[Math.Min(i2 + 1, n - 1)];

            double span = p2.T - p1.T;
            double t = span <= 0 ? 0 : (renderT - p1.T) / span;

            // углы разворачиваем относительно p1 (без скачков через ±180)
            double r1 = p1.Rot;
            double r0 = r1 + Wrap(p0.Rot - r1);
            double r2 = r1 + Wrap(p2.Rot - r1);
            double r3 = r1 + Wrap(p3.Rot - r1);

            return new ConsistTrail.Pose(
                Catmull(p0.X, p1.X, p2.X, p3.X, t),
                Catmull(p0.Z, p1.Z, p2.Z, p3.Z, t),
                Catmull(p0.Y, p1.Y, p2.Y, p3.Y, t),
                Catmull(r0,   r1,   r2,   r3,   t));
        }
    }

    // Сплайн Катмулла-Рома: гладкая кривая через p1..p2 (p0,p3 — соседи),
    // непрерывная скорость → нет «тиков» на стыках кадров.
    private static double Catmull(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return 0.5 * ((2.0 * p1)
                    + (-p0 + p2) * t
                    + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
                    + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    private static double Wrap(double d) => ((d + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

    /// <summary>Вернуть все объекты поезда на исходные места (перед удалением).</summary>
    public void Restore(ObjectWriter writer)
    {
        if (_originals.Count > 0) writer.RestoreOriginals(_originals);
    }
}
