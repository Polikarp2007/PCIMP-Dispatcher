using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SysIO = System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PCMPDispatcher;

public partial class MainWindow
{
    private async void RunSplash()
    {
        AnimateWidth(SplashProgress, 0, 300, 6.0);
        await Task.Delay(3000);
        await FlipOut(WelcomeScale, 20, 15);
        SplashWelcome.Visibility = Visibility.Collapsed;
        SplashTagline.Visibility = Visibility.Visible;
        await FlipIn(TaglineScale, 20, 15);
        await Task.Delay(3000);
        await NavigateTo(() =>
        {
            SplashPage.Visibility = Visibility.Collapsed;
            ShowRolePicker();
        });
    }

    private void ShowRolePicker()
    {
        RolePicker.Visibility = Visibility.Visible;
        RolePicker.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        RolePicker.BeginAnimation(OpacityProperty, fadeIn);
        SetCaptionLight(true); // dark page → white window buttons

        // One render loop drives BOTH the cursor glow and the watermark breathing.
        if (!_frameHooked)
        {
            _frameClock.Restart();
            CompositionTarget.Rendering += OnGlowFrame;
            _frameHooked = true;
        }
    }

    private double _glowTargetX = -1000, _glowTargetY = -1000;
    private bool _frameHooked;
    private readonly System.Diagnostics.Stopwatch _frameClock = new();

    private void OnRolePickerMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(RolePicker);
        _glowTargetX = p.X - CursorGlow.Width / 2;
        _glowTargetY = p.Y - CursorGlow.Height / 2;
    }

    // Per-frame loop: smooth glow trailing + sine-based watermark breathing.
    // No storyboards, no per-event animations — guaranteed steady 60 fps.
    private void OnGlowFrame(object? sender, EventArgs e)
    {
        if (RolePicker.Visibility != Visibility.Visible)
        {
            CompositionTarget.Rendering -= OnGlowFrame;
            _frameHooked = false;
            return;
        }

        // Glow eases toward the cursor (~15% of the gap per frame).
        GlowT.X += (_glowTargetX - GlowT.X) * 0.15;
        GlowT.Y += (_glowTargetY - GlowT.Y) * 0.15;

        // Watermarks breathe with independent periods/phases for a calm shimmer.
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
        // 0..1 sine wave → smooth in/out, no seams.
        double s = 0.5 - 0.5 * Math.Cos((t / period + phase) * 2 * Math.PI);
        el.Opacity = min + (max - min) * s;
    }

    private void SetCaptionLight(bool light)
    {
        var brush = light ? Brushes.White : Brush("#444444");
        MinBtn.Foreground = brush;
        MaxBtn.Foreground = brush;
        CloseBtn2.Foreground = brush;
    }

    private CancellationTokenSource? _comingSoonCts;

    private void OnRoleSelected(object sender, MouseButtonEventArgs e)
    {
        var role = (sender as FrameworkElement)?.Tag?.ToString() ?? "";

        if (role == "Dispatcher")
        {
            _ = NavigateTo(() =>
            {
                RolePicker.Visibility = Visibility.Collapsed;
                ShowMainPage();
            });
            return;
        }

        if (role == "Driver")
        {
            _ = NavigateTo(() =>
            {
                RolePicker.Visibility = Visibility.Collapsed;
                ShowDriverPage();
            });
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

    private bool _drvRouteSelected = false;

    private void OnDrvRouteCardToggle(object sender, MouseButtonEventArgs e)
    {
        _drvRouteSelected = !_drvRouteSelected;
        DrvRouteDescPanel.Visibility  = _drvRouteSelected ? Visibility.Visible   : Visibility.Collapsed;
        DrvProceedBtn.Visibility      = _drvRouteSelected ? Visibility.Visible   : Visibility.Collapsed;
        DrvComingSoonCard.Visibility  = _drvRouteSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDrvProceedClick(object sender, RoutedEventArgs e)
    {
        _ = NavigateTo(() =>
        {
            DriverPage.Visibility = Visibility.Collapsed;
            ShowDrvSetupPage();
        });
    }

    private void ShowDriverPage()
    {
        SetCaptionLight(false); // white page → dark window buttons
        DriverPage.Visibility = Visibility.Visible;
        DriverPage.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        DriverPage.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void ShowMainPage()
    {
        SetCaptionLight(false); // white page → dark window buttons
        MainPage.Visibility = Visibility.Visible;
        MainPage.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        MainPage.BeginAnimation(OpacityProperty, fadeIn);
    }

    private static async Task FlipOut(ScaleTransform st, int steps, int delayMs)
    {
        for (int i = steps; i >= 0; i--)
        {
            st.ScaleY = (double)i / steps;
            await Task.Delay(delayMs);
        }
    }

    private static async Task FlipIn(ScaleTransform st, int steps, int delayMs)
    {
        for (int i = 0; i <= steps; i++)
        {
            st.ScaleY = (double)i / steps;
            await Task.Delay(delayMs);
        }
    }
}
