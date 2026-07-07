using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PCMPDispatcher;

public partial class DrvFinalView : UserControl
{
    private bool _connected;
    private bool _connecting;
    private DriverRunConfig? _cfg;
    private readonly System.Windows.Threading.DispatcherTimer _clock = new()
    { Interval = TimeSpan.FromMilliseconds(500) };
    private System.Windows.Threading.DispatcherTimer? _noticeTimer;

    /// <summary>Raised when the user presses Back — the host handles navigation.</summary>
    public event Action? BackRequested;

    public DrvFinalView()
    {
        InitializeComponent();
        _clock.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            FinalClockText.Text = now.ToString("HH:mm:ss");
            FinalDateText.Text  = now.ToString("dd.MM.yyyy");
        };
    }

    /// <summary>Build the run sheet from a config snapshot and fade the page in.</summary>
    public void Open(DriverRunConfig cfg)
    {
        _cfg = cfg;
        _connected = false;
        UpdateConnectUi();
        BuildFinalDocument(cfg);
        _clock.Start();
        SbOnlineCount.Text = Services.OnlineCounter.DisplayText;
        Services.OnlineCounter.Updated += OnOnlineUpdated;
        Services.OnlineCounter.Start();

        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void OnOnlineUpdated(string txt) => SbOnlineCount.Text = txt;

    private void OnVolumeGearClick(object sender, RoutedEventArgs e)
        => FinalVolumePopup.IsOpen = !FinalVolumePopup.IsOpen;

    private void OnVolumeChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        int pct = (int)FinalVolumeSlider.Value;
        Services.VoiceChat.Volume = pct / 100f;
        if (FinalVolumeLabel != null) FinalVolumeLabel.Text = $"{pct}%";
    }

    private void OnDrvFinalBack_Click(object sender, RoutedEventArgs e)
    {
        if (_connected)
        {
            ShowNotice("Disconnect from PC|MP first.");
            return;
        }
        _clock.Stop();
        BackRequested?.Invoke();
    }

    // Brief centered toast, e.g. when Back is blocked.
    private void ShowNotice(string text)
    {
        FinalNoticeText.Text = text;
        FinalNotice.Visibility = Visibility.Visible;
        FinalNotice.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        _noticeTimer?.Stop();
        _noticeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer?.Stop();
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280));
            fade.Completed += (_, _) => FinalNotice.Visibility = Visibility.Collapsed;
            FinalNotice.BeginAnimation(OpacityProperty, fade);
        };
        _noticeTimer.Start();
    }

    // ── Connect button state ──
    private async void OnConnectClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_connected || _connecting || _cfg == null) return;

        _connecting = true;
        DrvConnectText.Text = "Connecting…";
        DrvConnectBtn.Cursor = System.Windows.Input.Cursors.Wait;

        bool ok = await Services.MpSession.ConnectAsync(BuildRunPayload(_cfg), _cfg?.WagonCount ?? 0);

        _connecting = false;
        if (ok)
        {
            _connected = true;
            UpdateConnectUi();
        }
        else
        {
            UpdateConnectUi();
            ShowNotice("Could not connect to PC|MP. Try again.");
        }
    }

    private async void OnDisconnectClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _connected = false;
        UpdateConnectUi();
        await Services.MpSession.DisconnectAsync();
    }

    // Assemble the run sheet the HUD will display (train, route, full timetable).
    private object BuildRunPayload(DriverRunConfig cfg)
    {
        var stations = new System.Collections.Generic.List<string[]>();
        int t = cfg.DepartMinutes;
        for (int i = 0; i < cfg.Order.Length; i++)
        {
            bool origin = i == 0, terminus = i == cfg.Order.Length - 1;
            string arr, dep;
            if (origin) { arr = "—"; dep = Fmt(t); }
            else
            {
                t += cfg.Segments[i - 1];
                arr = Fmt(t);
                int d = cfg.Dwell.Length > i ? cfg.Dwell[i] : 0;
                if (!terminus) { t += d; dep = Fmt(t); }
                else dep = "—";
            }
            stations.Add(new[] { cfg.Order[i], arr, dep });
        }

        return new
        {
            train_num  = $"{cfg.TrainType} {cfg.TrainNumber}".Trim(),
            route_from = cfg.Order.Length > 0 ? cfg.Order[0] : "",
            route_to   = cfg.Order.Length > 0 ? cfg.Order[^1] : "",
            platform   = cfg.Platform,
            loco       = cfg.Loco,
            wagons     = $"{cfg.WagonCount} × {cfg.WagonType}",
            stations,
            options = new
            {
                radio    = cfg.OptRadio,
                textonly = cfg.OptTextOnly,
                priority = cfg.OptPriority
            }
        };
    }

    private void UpdateConnectUi()
    {
        if (_connected)
        {
            DrvConnectBtn.Background = Brush("#9AA1AD");
            DrvConnectBtn.Cursor = System.Windows.Input.Cursors.Arrow;
            DrvConnectText.Text = "Connected";
            DrvExitBtn.Visibility = Visibility.Visible;
        }
        else
        {
            DrvConnectBtn.Background = Brush("#111111");
            DrvConnectBtn.Cursor = System.Windows.Input.Cursors.Hand;
            DrvConnectText.Text = "Connect to PC|MP";
            DrvExitBtn.Visibility = Visibility.Collapsed;
        }
    }

    // ──────────────────────────────────────────────
    //  RUN SHEET DOCUMENT
    // ──────────────────────────────────────────────

    private void BuildFinalDocument(DriverRunConfig cfg)
    {
        var doc = DrvSheetContent;
        doc.Children.Clear();

        string trainId = $"{cfg.TrainType} {cfg.TrainNumber}";

        // ── Masthead ── (driver left · title center · issued date/time right)
        var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };

        var driver = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap
        };
        driver.Inlines.Add(new Run("Driver\n") { Foreground = Brush("#999999"), FontWeight = FontWeights.SemiBold });
        driver.Inlines.Add(new Run(cfg.DriverName) { Foreground = Brush("#222222"), FontWeight = FontWeights.Bold });
        head.Children.Add(driver);

        var issued = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Right, FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5
        };
        issued.Inlines.Add(new Run("Issued\n") { Foreground = Brush("#999999"), FontWeight = FontWeights.SemiBold });
        issued.Inlines.Add(new Run($"{DateTime.Now:dd.MM.yyyy}  ·  {DateTime.Now:HH:mm}")
        { Foreground = Brush("#222222"), FontWeight = FontWeights.Bold });
        head.Children.Add(issued);

        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock
        {
            Text = "PC|MP", HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 40, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1")
        };
        TextOptions.SetTextFormattingMode(title, TextFormattingMode.Ideal);
        center.Children.Add(title);
        center.Children.Add(new TextBlock
        {
            Text = "Official driver run document",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = Brush("#999999"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        head.Children.Add(center);

        doc.Children.Add(head);
        doc.Children.Add(Divider());

        doc.Children.Add(Para(
            "Please review and familiarise yourself with the information below. This is your schedule and " +
            "the arrival / departure times at each station. Try to follow the schedule as closely as possible.",
            14, "#333333", FontWeights.SemiBold, new Thickness(0, 16, 0, 22)));

        // ── 1. Schedule ──
        doc.Children.Add(Heading("1.", "Schedule"));
        doc.Children.Add(Para(
            $"Timetable for train {trainId} on route M200, running from {cfg.Origin} to {cfg.Destination}.",
            14, "#333333", FontWeights.SemiBold, new Thickness(0, 0, 0, 14)));

        doc.Children.Add(BuildScheduleTable(cfg));

        // Schedule summary
        int n = cfg.Order.Length;
        int totalSeg = 0; foreach (var s in cfg.Segments) totalSeg += s;
        int totalDwell = 0;
        for (int i = 1; i < n - 1; i++) totalDwell += cfg.Dwell.Length > i ? cfg.Dwell[i] : 0;
        int total = totalSeg + totalDwell;
        int stops = Math.Max(0, n - 1);
        string totalStr = total >= 60 ? $"{total / 60} h {total % 60:D2} min" : $"{total} min";

        var sum = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22)
        };
        sum.Inlines.Add(new Run("Summary:  ") { FontWeight = FontWeights.Black, Foreground = Brush("#111111") });
        sum.Inlines.Add(new Run($"{n} stations  ·  {stops} stops  ·  total time in transit ")
        { FontWeight = FontWeights.SemiBold, Foreground = Brush("#555555") });
        sum.Inlines.Add(new Run(totalStr) { FontWeight = FontWeights.Black, Foreground = Brush("#3458e1") });
        sum.Inlines.Add(new Run(".") { FontWeight = FontWeights.SemiBold, Foreground = Brush("#555555") });
        doc.Children.Add(sum);

        // ── 2. Consist ──
        doc.Children.Add(Heading("2.", "Consist"));
        doc.Children.Add(Para(
            $"One locomotive {cfg.Loco}, hauling {cfg.WagonCount} × {cfg.WagonType}.",
            14, "#333333", FontWeights.SemiBold, new Thickness(0, 0, 0, 12)));

        var strip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (cfg.LocoImg != null) strip.Children.Add(SheetVehicle(cfg.LocoImg));
        for (int i = 0; i < cfg.WagonCount && cfg.WagonImg != null; i++) strip.Children.Add(SheetVehicle(cfg.WagonImg));

        doc.Children.Add(new Border
        {
            BorderBrush = Brush("#222222"), BorderThickness = new Thickness(1),
            Background = Brush("#FCFCFE"), Height = 100,
            Margin = new Thickness(0, 0, 0, 24), Padding = new Thickness(18, 12, 18, 12),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = strip
            }
        });

        // ── 3. Additional options ──
        doc.Children.Add(Heading("3.", "Additional options"));
        bool any = false;
        if (cfg.OptRadio)    { doc.Children.Add(OptionLine("I speak on the radio")); any = true; }
        if (cfg.OptTextOnly) { doc.Children.Add(OptionLine("I'm text-chat only")); any = true; }
        if (cfg.OptPriority) { doc.Children.Add(OptionLine("I need priority departure")); any = true; }
        if (!any)
            doc.Children.Add(Para("No additional options selected.", 14, "#999999",
                FontWeights.SemiBold, new Thickness(0, 0, 0, 4)));

        doc.Children.Add(new Border { Height = 10 });
        doc.Children.Add(Divider());

        // ── Memo ──
        doc.Children.Add(new TextBlock
        {
            Text = "Before you connect",
            FontFamily = new FontFamily("Segoe UI"), FontSize = 18, FontWeight = FontWeights.Black,
            Foreground = Brush("#111111"), Margin = new Thickness(0, 18, 0, 12)
        });
        doc.Children.Add(Step(1, "Open the simulator — RailWorks (Train Simulator)."));
        doc.Children.Add(Step(2, "Select the route of your chosen publisher."));
        doc.Children.Add(Step(3, "Create a Free Roam scenario."));
        doc.Children.Add(Step(4, "Place your locomotive and your wagons on the track."));
        doc.Children.Add(Step(5, "Sit in your consist at the correct station and platform, then connect."));

        var important = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap,
            LineHeight = 21, Margin = new Thickness(0, 10, 0, 18)
        };
        important.Inlines.Add(new Run("Important! ") { FontWeight = FontWeights.Black, Foreground = Brush("#CC1122") });
        important.Inlines.Add(new Run("You must complete all of the steps above and make sure your consist matches this document before you press connect.")
        { FontWeight = FontWeights.SemiBold, Foreground = Brush("#555555") });
        doc.Children.Add(important);
    }

    // Classic sharp black-ruled table.
    private UIElement BuildScheduleTable(DriverRunConfig cfg)
    {
        var table = new Grid();
        double[] widths = { -1, 130, 96, 82, 82, 70 }; // -1 = star
        foreach (var w in widths)
            table.ColumnDefinitions.Add(new ColumnDefinition
            { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });

        int rows = cfg.Order.Length + 1;
        for (int r = 0; r < rows; r++)
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        string[] head = { "STATION", "TYPE", "PLATFORM", "ARRIVAL", "DEPART", "STOP" };
        for (int c = 0; c < head.Length; c++)
            table.Children.Add(Cell(head[c], 0, c, header: true, left: c <= 1, mono: false));

        int t = cfg.DepartMinutes;

        for (int i = 0; i < cfg.Order.Length; i++)
        {
            int r = i + 1;
            bool origin = i == 0, terminus = i == cfg.Order.Length - 1;
            string typeStr = origin ? "Origin" : terminus ? "Terminus" : "Passing";
            string plat = origin ? cfg.Platform : "—";
            string arr, depart, stop;

            if (origin) { arr = "—"; depart = Fmt(t); stop = "—"; }
            else
            {
                t += cfg.Segments[i - 1];
                arr = Fmt(t);
                int d = cfg.Dwell.Length > i ? cfg.Dwell[i] : 0;
                stop = d + " min";
                if (!terminus) { t += d; depart = Fmt(t); }
                else depart = "—";
            }

            table.Children.Add(Cell(cfg.Order[i], r, 0, false, left: true, mono: false, bold: true));
            table.Children.Add(Cell(typeStr,      r, 1, false, left: true, mono: false));
            table.Children.Add(Cell(plat,         r, 2, false, left: false, mono: false));
            table.Children.Add(Cell(arr,          r, 3, false, left: false, mono: true));
            table.Children.Add(Cell(depart,       r, 4, false, left: false, mono: true, accent: true));
            table.Children.Add(Cell(stop,         r, 5, false, left: false, mono: false));
        }

        return new Border
        {
            BorderBrush = Brush("#222222"), BorderThickness = new Thickness(1, 1, 0, 0),
            Margin = new Thickness(0, 0, 0, 24), Child = table
        };
    }

    private Border Cell(string text, int row, int col, bool header, bool left, bool mono,
                        bool bold = false, bool accent = false)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = header ? 12 : 14,
            FontWeight = header ? FontWeights.Black : (bold ? FontWeights.Bold : FontWeights.SemiBold),
            Foreground = Brush(header ? "#111111" : accent ? "#3458e1" : "#222222"),
            FontFamily = new FontFamily(mono ? "Bahnschrift SemiLight" : "Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Center,
            Margin = left ? new Thickness(12, 0, 0, 0) : new Thickness(0)
        };
        var b = new Border
        {
            BorderBrush = Brush("#222222"), BorderThickness = new Thickness(0, 0, 1, 1),
            Background = Brush(header ? "#EDEDED" : "#FFFFFF"),
            Child = tb
        };
        Grid.SetRow(b, row);
        Grid.SetColumn(b, col);
        return b;
    }

    private FrameworkElement OptionLine(string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        sp.Children.Add(new TextBlock
        {
            Text = "✓  ", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#28A745"), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI")
        });
        sp.Children.Add(new TextBlock
        {
            Text = text, FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brush("#1a1a2e"), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI")
        });
        return sp;
    }

    private FrameworkElement Step(int n, string text)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var num = new TextBlock
        {
            Text = n + ".", FontSize = 14, FontWeight = FontWeights.Black, Foreground = Brush("#3458e1"),
            VerticalAlignment = VerticalAlignment.Top, FontFamily = new FontFamily("Segoe UI")
        };
        Grid.SetColumn(num, 0);
        g.Children.Add(num);

        var body = new TextBlock
        {
            Text = text, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brush("#444444"),
            TextWrapping = TextWrapping.Wrap, LineHeight = 21, FontFamily = new FontFamily("Segoe UI")
        };
        Grid.SetColumn(body, 1);
        g.Children.Add(body);
        return g;
    }

    private static Image SheetVehicle(BitmapImage src) => new()
    {
        Source = src, Height = 60, Stretch = Stretch.Uniform,
        Margin = new Thickness(0, 0, -8, 0), VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true
    };

    private TextBlock Para(string text, double size, string hex, FontWeight weight, Thickness margin) => new()
    {
        Text = text, FontFamily = new FontFamily("Segoe UI"), FontSize = size,
        FontWeight = weight, Foreground = Brush(hex), TextWrapping = TextWrapping.Wrap,
        LineHeight = size + 9, Margin = margin
    };

    private FrameworkElement Heading(string number, string title)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI"), FontSize = 19, FontWeight = FontWeights.Black,
            Margin = new Thickness(0, 8, 0, 8)
        };
        TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
        tb.Inlines.Add(new Run(number) { Foreground = Brush("#3458e1") });
        tb.Inlines.Add(new Run("  " + title) { Foreground = Brush("#111111") });
        return tb;
    }

    private Border Divider() => new() { Height = 1, Background = Brush("#DDDDDD") };

    private static string Fmt(int minutes)
    {
        minutes = ((minutes % 1440) + 1440) % 1440;
        return $"{minutes / 60:D2}:{minutes % 60:D2}";
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));
}
