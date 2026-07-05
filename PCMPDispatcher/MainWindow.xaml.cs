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


    // High-priority clocks — use System.Threading.Timer so they fire from a background thread
    // and are dispatched at DispatcherPriority.Send (highest), bypassing all pending async work.

    private CancellationTokenSource? _navCts;

    public MainWindow()
    {
        InitializeComponent();

        // Disable Tab navigation everywhere in the app.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab) e.Handled = true;
        };

        // Repaint stale edges after returning from another virtual desktop / re-activation.
        Activated += (_, _) => ForceFullRedraw();

        // Role picker (UserControl) → shell navigates to the chosen dashboard.
        RolePickerView.RoleChosen += OnRoleChosen;

        // Dispatcher home (UserControl) → proceed to station select.
        DispatcherHomeView.GoStationRequested += () => _ = NavigateTo(() =>
        {
            DispatcherHomeView.Visibility = Visibility.Collapsed;
            StationSelectView.Open();
        });

        // Station select (UserControl) → back to dashboard / connect to a zone.
        StationSelectView.BackRequested += () => _ = NavigateTo(() =>
        {
            StationSelectView.Visibility = Visibility.Collapsed;
            DispatcherHomeView.Open();
        });
        StationSelectView.ConnectRequested += ShowConnecting;

        // Console (UserControl) → disconnect returns to the dispatcher dashboard.
        ConsoleView.DisconnectRequested += () => _ = NavigateTo(
            () => DispatcherHomeView.Open(), holdMs: 4000);

        // Driver home (UserControl) → proceed to Set-Up.
        DriverHomeView.GoSetupRequested += () => _ = NavigateTo(() =>
        {
            DriverHomeView.Visibility = Visibility.Collapsed;
            SetCaptionLight(false);
            DrvSetupView.Open();
        });

        // Driver Set-Up page (UserControl) → shell handles navigation.
        DrvSetupView.GoFinalRequested += cfg => _ = NavigateTo(() =>
        {
            DrvSetupView.Visibility = Visibility.Collapsed;
            SetCaptionLight(false);
            DrvFinalView.Open(cfg);
        });
        DrvSetupView.BackRequested += () => _ = NavigateTo(() =>
        {
            DrvSetupView.Visibility = Visibility.Collapsed;
            SetCaptionLight(false);
            DriverHomeView.Open();
        });

        // Final page (UserControl) → back to Set-Up.
        DrvFinalView.BackRequested += () => _ = NavigateTo(() =>
        {
            DrvFinalView.Visibility = Visibility.Collapsed;
            SetCaptionLight(false);
            DrvSetupView.Open();
        });

        // Splash intro → role picker.
        SplashView.Finished += () => _ = NavigateTo(() =>
        {
            SplashView.Visibility = Visibility.Collapsed;
            ShowRolePicker();
        });
        Loaded += (_, _) => SplashView.Start();
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


    private async void ShowConnecting(string station)
    {
        ConsoleView.PreloadStation(station); // preload the map panel during the animation
        ConnectingText.Text = $"Connecting to {station}…";
        ConnectingOverlay.Opacity = 0;
        ConnectingOverlay.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        ConnectingOverlay.BeginAnimation(OpacityProperty, fadeIn);
        AnimateWidth(ConnectProgress, 0, 300, 1.5);

        await Task.Delay(1700);

        ConnectingOverlay.Visibility = Visibility.Collapsed;
        StationSelectView.Visibility = Visibility.Collapsed;
        ConsoleView.Open(station);
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
