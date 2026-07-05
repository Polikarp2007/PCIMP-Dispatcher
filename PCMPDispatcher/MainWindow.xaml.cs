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

public partial class MainWindow : Window
{
    private const string DispatcherName = "Polikarp";

    private readonly DispatcherTimer _dotChaser = new() { Interval = TimeSpan.FromMilliseconds(380) };
    private int _chaserPos = 0;

    // High-priority clocks — use System.Threading.Timer so they fire from a background thread
    // and are dispatched at DispatcherPriority.Send (highest), bypassing all pending async work.
    private System.Threading.Timer? _topClockTimer;
    private System.Threading.Timer? _consoleClockTimer;

    private CancellationTokenSource? _navCts;

    public MainWindow()
    {
        InitializeComponent();

        HomeUserName.Text = DispatcherName;
        DrvUserName.Text  = DispatcherName;

        // Top-bar clock — starts immediately, fires every 500ms
        _topClockTimer = new System.Threading.Timer(_ =>
        {
            var now = DateTime.Now;
            Dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                TopClockText.Text = now.ToString("HH:mm:ss");
                TopDateText.Text  = now.ToString("dd.MM.yyyy");
                DrvClockText.Text = now.ToString("HH:mm:ss");
                DrvDateText.Text  = now.ToString("dd.MM.yyyy");
            });
        }, null, 0, 500);

        _dotChaser.Tick += ChaserTick;
        _dotChaser.Start();

        // Disable Tab navigation everywhere in the app.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) e.Handled = true;
        };

        // Repaint stale edges after returning from another virtual desktop / re-activation.
        Activated += (_, _) => ForceFullRedraw();

        Loaded += (_, _) => RunSplash();
        RomanianT9.InitAsync(); // start downloading word list in background
    }

    // UpdateUiClock kept as no-op for any leftover references
    private void UpdateUiClock(object? s, EventArgs e) { }

    // ──────────────────────────────────────────────
    //  BRAND TRANSITION OVERLAY
    // ──────────────────────────────────────────────

    private async Task NavigateTo(Action switchPage, int holdMs = 1600)
    {
        _navCts?.Cancel();
        _navCts = new CancellationTokenSource();
        var token = _navCts.Token;

        try
        {
            BrandScale.ScaleX = 0;
            BrandScale.ScaleY = 0;
            TransitionOverlay.Opacity = 0;
            TransitionOverlay.Visibility = Visibility.Visible;
            await Task.Yield();
            token.ThrowIfCancellationRequested();

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            TransitionOverlay.BeginAnimation(OpacityProperty, fadeIn);

            var springX = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            var springY = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            BrandScale.BeginAnimation(ScaleTransform.ScaleXProperty, springX);
            BrandScale.BeginAnimation(ScaleTransform.ScaleYProperty, springY);

            await Task.Delay(holdMs, token);
            switchPage();

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(340))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            fadeOut.Completed += (_, _) => TransitionOverlay.Visibility = Visibility.Collapsed;
            TransitionOverlay.BeginAnimation(OpacityProperty, fadeOut);

            await Task.Delay(340, token);
        }
        catch (OperationCanceledException)
        {
            TransitionOverlay.Visibility = Visibility.Collapsed;
            TransitionOverlay.Opacity = 0;
            switchPage();
        }
    }

    // ──────────────────────────────────────────────
    //  NAVIGATION
    // ──────────────────────────────────────────────

    private bool _routeSelected = false;

    private void OnRouteCardToggle(object sender, MouseButtonEventArgs e)
    {
        _routeSelected = !_routeSelected;
        RouteDescPanel.Visibility  = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        ProceedBtn.Visibility      = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        ComingSoonCard.Visibility  = _routeSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnProceedClick(object sender, RoutedEventArgs e)
    {
        _ = NavigateTo(() =>
        {
            MainPage.Visibility    = Visibility.Collapsed;
            LoadSetupPage();
            StationPage.Visibility = Visibility.Visible;
            StationPage.Opacity    = 1;
        });
    }

    private void OnRouteCardClick(object sender, RoutedEventArgs e) => OnProceedClick(sender, e);

    private void OnStationBack_Click(object sender, RoutedEventArgs e)
    {
        _ = NavigateTo(() =>
        {
            StationPage.Visibility = Visibility.Collapsed;
            MainPage.Visibility = Visibility.Visible;
            MainPage.Opacity = 1;
        });
    }

    private void OnStationConnect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var station = btn.Tag?.ToString() ?? "Unknown";
        ShowConnecting(station);
    }

    private async void ShowConnecting(string station)
    {
        MapControl.FocusStation(station); // start loading the correct panel during the animation
        ConnectingText.Text = $"Connecting to {station}…";
        ConnectingOverlay.Opacity = 0;
        ConnectingOverlay.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        ConnectingOverlay.BeginAnimation(OpacityProperty, fadeIn);
        AnimateWidth(ConnectProgress, 0, 300, 1.5);

        await Task.Delay(1700);

        ConnectingOverlay.Visibility = Visibility.Collapsed;
        OpenConsole(station);
    }

    private void OpenConsole(string station)
    {
        ConsoleStationName.Text   = station.ToUpper();
        ConsoleSubtitleText.Text  = $"{DispatcherName}.K.";
        ConsoleTotalPlayers.Text  = "12";
        ConsoleZoneStatLabel.Text = station;
        ConsoleZonePlayers.Text   = "2";

        StationPage.Visibility = Visibility.Collapsed;
        ConsolePage.Visibility = Visibility.Visible;
        ConsolePage.Opacity    = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400));
        ConsolePage.BeginAnimation(OpacityProperty, fadeIn);

        // Console clock — DispatcherTimer runs natively on UI thread, no marshal overhead
        _consoleClockTimer?.Dispose();
        var uiTimer = new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Render,
            (_, _) =>
            {
                var now = DateTime.Now;
                ConsoleClockText.Text = now.ToString("HH:mm:ss");
                ConsoleDateText.Text  = now.ToString("dd.MM.yyyy");
            },
            Dispatcher);
        uiTimer.Start();
        // Wrap in IDisposable shim so existing _consoleClockTimer?.Dispose() still works
        _consoleClockTimer = new System.Threading.Timer(_ => { }, null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        // Store stop action via tag — stop uiTimer on disconnect
        ConsoleClockText.Tag = uiTimer;

        _currentStation = station;
        MapControl.SwitchToggledFromHtml   += OnHtmlSwitchToggled;
        MapControl.SwitchSelectedForConsist += OnHtmlConsistSelected;
        BuildSwitchesWidget();
        BuildChatPanel(station);
        BuildTrainsPanel(station);
        BuildDispLogPanel(station);
        AddChatMessage("System", $"Dispatcher connected to zone: {station}", "#28A745");
    }

    private void OnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        (ConsoleClockText.Tag as System.Windows.Threading.DispatcherTimer)?.Stop();
        ConsoleClockText.Tag = null;
        _consoleClockTimer?.Dispose();
        _consoleClockTimer = null;
        _chatMessages = null; _chatScroll = null; _chatInput = null;
        MapControl.SwitchToggledFromHtml   -= OnHtmlSwitchToggled;
        MapControl.SwitchSelectedForConsist -= OnHtmlConsistSelected;
        _consists.Clear(); _lockedSwitches.Clear();
        RightPanel.Child    = null;
        ChatContainer.Child = null;
        TrainsContainer.Child = null;
        _chatLog = null; _chatScroll2 = null; _chatInputBox = null;

        // WebView2 и весь ConsolePage скрываем сразу — до анимации
        MapControl.Visibility  = Visibility.Collapsed;
        ConsolePage.Visibility = Visibility.Collapsed;

        _ = NavigateTo(() =>
        {
            MainPage.Visibility   = Visibility.Visible;
            MainPage.Opacity      = 1;
            MapControl.Visibility = Visibility.Visible;
        }, holdMs: 4000);
    }

    // ──────────────────────────────────────────────
    //  UTILS
    // ──────────────────────────────────────────────

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    private static void AnimateWidth(FrameworkElement el, double from, double to, double seconds)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        el.BeginAnimation(FrameworkElement.WidthProperty, a);
    }

    // ──────────────────────────────────────────────
    //  CHASER ANIMATION (station dots on route card)
    // ──────────────────────────────────────────────

    private readonly Color _dotBlue  = (Color)ColorConverter.ConvertFromString("#3458e1");
    private readonly Color _dotWhite = Colors.White;

    private void ChaserTick(object? s, EventArgs e)
    {
        SolidColorBrush[][] allSets =
        [
            [Dot1Brush, Dot2Brush, Dot3Brush, Dot4Brush, Dot5Brush, Dot6Brush],
            [MiniDot1Brush, MiniDot2Brush, MiniDot3Brush, MiniDot4Brush, MiniDot5Brush, MiniDot6Brush],
            [DrvDot1Brush, DrvDot2Brush, DrvDot3Brush, DrvDot4Brush, DrvDot5Brush, DrvDot6Brush]
        ];

        foreach (var brushes in allSets)
        {
            for (int i = 0; i < brushes.Length; i++)
            {
                int dist = Math.Abs(i - _chaserPos);
                if (dist > 3) dist = 6 - dist;

                var target = dist switch
                {
                    0 => _dotWhite,
                    1 => Lerp(_dotBlue, _dotWhite, 0.55),
                    _ => _dotBlue
                };

                AnimateBrush(brushes[i], target);
            }
        }

        _chaserPos = (_chaserPos + 1) % 6;
    }

    private static void AnimateBrush(SolidColorBrush brush, Color to)
    {
        var anim = new ColorAnimation(to, TimeSpan.FromMilliseconds(320))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private void ExitApp_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    // ──────────────────────────────────────────────
    //  WINDOW CAPTION CONTROLS
    // ──────────────────────────────────────────────

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { OnMaxRestore_Click(sender, e); return; }
        if (WindowState == WindowState.Maximized) return; // drag only when windowed
        DragMove();
    }

    private void OnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaxRestore_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        ApplyWindowStateUi();
    }

    private void ApplyWindowStateUi()
    {
        bool windowed = WindowState == WindowState.Normal;

        // Glyph: "maximize" when windowed, "restore-down" when maximized.
        MaxBtn.Content = ((char)(windowed ? 0xE922 : 0xE923)).ToString();
        MaxBtn.ToolTip = windowed ? "Maximize" : "Restore down";

        // Soft gray outline only when floating in a window over other apps.
        WindowBorder.BorderThickness = new Thickness(windowed ? 1 : 0);

        if (windowed)
        {
            // Proper custom chrome in windowed mode: no white non-client line,
            // clean DWM (Alt-Tab) thumbnail, and working edge/grip resize.
            System.Windows.Shell.WindowChrome.SetWindowChrome(this,
                new System.Windows.Shell.WindowChrome
                {
                    CaptionHeight = 0,
                    ResizeBorderThickness = new Thickness(6),
                    GlassFrameThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(0),
                    UseAeroCaptionButtons = false
                });
            ResizeMode = ResizeMode.CanResizeWithGrip;
        }
        else
        {
            // Borderless fullscreen: drop chrome and resizing → flush, no gaps.
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, null);
            ResizeMode = ResizeMode.NoResize;
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(this);
        src?.AddHook(WindowProc);
        ApplyWindowStateUi(); // starts maximized → set glyph/border now
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Pin the maximized window exactly to the monitor (0,0 + full size). Without
        // this, maximizing after the window has had a resizable frame overflows ~8px
        // off every edge and pushes the caption buttons into/past the corner.
        if (msg == WM_GETMINMAXINFO)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref mi))
                {
                    var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
                    RECT area = mi.rcMonitor;
                    mmi.ptMaxPosition.x = 0;
                    mmi.ptMaxPosition.y = 0;
                    mmi.ptMaxSize.x     = area.right - area.left;
                    mmi.ptMaxSize.y     = area.bottom - area.top;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    // ── Force a full repaint (cures transparent edges after DWM/desktop switches) ──
    private const uint RDW_INVALIDATE = 0x0001, RDW_ERASE = 0x0004,
                       RDW_FRAME = 0x0400, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100;

    private void ForceFullRedraw()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
                RDW_INVALIDATE | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }
}
