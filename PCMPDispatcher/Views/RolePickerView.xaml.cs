using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCMPDispatcher;

public partial class RolePickerView : UserControl
{
    private double _glowTargetX = -1000, _glowTargetY = -1000;
    private bool _frameHooked;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private CancellationTokenSource? _comingSoonCts;

    /// <summary>Raised with the chosen role ("Dispatcher" / "Driver"). The host navigates.</summary>
    public event Action<string>? RoleChosen;

    public RolePickerView()
    {
        InitializeComponent();
    }

    /// <summary>Fade the page in and start the cursor-glow / watermark render loop.</summary>
    public void Open()
    {
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

        if (!_frameHooked)
        {
            _frameClock.Restart();
            CompositionTarget.Rendering += OnGlowFrame;
            _frameHooked = true;
        }
    }

    private void OnRolePickerMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        _glowTargetX = p.X - CursorGlow.Width / 2;
        _glowTargetY = p.Y - CursorGlow.Height / 2;
    }

    // Per-frame loop: smooth glow trailing + sine-based watermark breathing.
    private void OnGlowFrame(object? sender, EventArgs e)
    {
        if (Visibility != Visibility.Visible)
        {
            CompositionTarget.Rendering -= OnGlowFrame;
            _frameHooked = false;
            return;
        }

        GlowT.X += (_glowTargetX - GlowT.X) * 0.15;
        GlowT.Y += (_glowTargetY - GlowT.Y) * 0.15;

        double t = _frameClock.Elapsed.TotalSeconds;
        Breathe(Wm1, t, period: 7.0, phase: 0.00, min: 0.03, max: 0.14);
        Breathe(Wm2, t, period: 6.0, phase: 0.35, min: 0.03, max: 0.12);
        Breathe(Wm3, t, period: 8.0, phase: 0.60, min: 0.02, max: 0.13);
        Breathe(Wm4, t, period: 6.5, phase: 0.15, min: 0.03, max: 0.11);
        Breathe(Wm5, t, period: 7.5, phase: 0.80, min: 0.02, max: 0.11);
        Breathe(Wm6, t, period: 5.5, phase: 0.50, min: 0.03, max: 0.12);
    }

    private static void Breathe(UIElement el, double t, double period, double phase, double min, double max)
    {
        double s = 0.5 - 0.5 * Math.Cos((t / period + phase) * 2 * Math.PI);
        el.Opacity = min + (max - min) * s;
    }

    private void OnRoleSelected(object sender, MouseButtonEventArgs e)
    {
        var role = (sender as FrameworkElement)?.Tag?.ToString() ?? "";

        if (role is "Dispatcher" or "Driver")
        {
            RoleChosen?.Invoke(role);
            return;
        }

        // Observator dashboard is not built yet — flash a notice.
        ShowComingSoon($"{role} mode is in development — coming soon.");
    }

    private async void ShowComingSoon(string message)
    {
        _comingSoonCts?.Cancel();
        _comingSoonCts = new CancellationTokenSource();
        var token = _comingSoonCts.Token;

        RoleComingSoon.Text = message;
        RoleComingSoon.Visibility = Visibility.Visible;
        RoleComingSoon.Opacity = 0;
        RoleComingSoon.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));

        try
        {
            await Task.Delay(2400, token);
            RoleComingSoon.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)));
        }
        catch (OperationCanceledException) { }
    }
}
