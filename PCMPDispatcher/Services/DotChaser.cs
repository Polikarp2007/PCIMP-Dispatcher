using System;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PCMPDispatcher;

/// <summary>
/// Drives the "running dot" animation over a single set of six brushes
/// (the station timeline on a route card). Each page owns its own instance.
/// </summary>
public sealed class DotChaser
{
    private static readonly Color Blue  = (Color)ColorConverter.ConvertFromString("#3458e1");
    private static readonly Color White = Colors.White;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private readonly SolidColorBrush[] _brushes;
    private int _pos;

    public DotChaser(params SolidColorBrush[] brushes)
    {
        _brushes = brushes;
        _timer.Tick += Tick;
    }

    public void Start() => _timer.Start();
    public void Stop()  => _timer.Stop();

    private void Tick(object? sender, EventArgs e)
    {
        int n = _brushes.Length;
        for (int i = 0; i < n; i++)
        {
            int dist = Math.Abs(i - _pos);
            if (dist > n / 2) dist = n - dist;

            var target = dist switch
            {
                0 => White,
                1 => Lerp(Blue, White, 0.55),
                _ => Blue
            };
            Animate(_brushes[i], target);
        }
        _pos = (_pos + 1) % n;
    }

    private static void Animate(SolidColorBrush brush, Color to)
        => brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, TimeSpan.FromMilliseconds(320))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
