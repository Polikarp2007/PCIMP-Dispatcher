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

public partial class ConsoleView : UserControl
{
    public event System.Action? DisconnectRequested;
    private const string DispatcherName = "Polikarp";
    private System.Threading.Timer? _consoleClockTimer;

    public ConsoleView()
    {
        InitializeComponent();
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    public void PreloadStation(string station) => MapControl.FocusStation(station);

    public void Open(string station)
    {
        ConsoleStationName.Text   = station.ToUpper();
        ConsoleSubtitleText.Text  = $"{DispatcherName}.K.";
        ConsoleTotalPlayers.Text  = "12";
        ConsoleZoneStatLabel.Text = station;
        ConsoleZonePlayers.Text   = "2";

        MapControl.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));

        _consoleClockTimer?.Dispose();
        var uiTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1), DispatcherPriority.Render,
            (_, _) =>
            {
                var now = DateTime.Now;
                ConsoleClockText.Text = now.ToString("HH:mm:ss");
                ConsoleDateText.Text  = now.ToString("dd.MM.yyyy");
            }, Dispatcher);
        uiTimer.Start();
        _consoleClockTimer = new System.Threading.Timer(_ => { }, null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        ConsoleClockText.Tag = uiTimer;

        _currentStation = station;
        MapControl.SwitchToggledFromHtml    += OnHtmlSwitchToggled;
        MapControl.SwitchSelectedForConsist += OnHtmlConsistSelected;
        BuildSwitchesWidget();
        BuildChatPanel(station);
        BuildTrainsPanel(station);
        BuildDispLogPanel(station);
        AddChatMessage("System", $"Dispatcher connected to zone: {station}", "#28A745");
    }

    private void OnDisconnect_Click(object sender, RoutedEventArgs e)
    {
        (ConsoleClockText.Tag as DispatcherTimer)?.Stop();
        ConsoleClockText.Tag = null;
        _consoleClockTimer?.Dispose();
        _consoleClockTimer = null;
        _chatMessages = null; _chatScroll = null; _chatInput = null;
        MapControl.SwitchToggledFromHtml    -= OnHtmlSwitchToggled;
        MapControl.SwitchSelectedForConsist -= OnHtmlConsistSelected;
        _consists.Clear(); _lockedSwitches.Clear();
        RightPanel.Child    = null;
        ChatContainer.Child = null;
        TrainsContainer.Child = null;
        _chatLog = null; _chatScroll2 = null; _chatInputBox = null;

        MapControl.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
        DisconnectRequested?.Invoke();
    }

    private Canvas? _overlayCanvas; // console floating-widget layer (legacy path)

    private static BitmapImage? TryLoadBitmap(string path)
    {
        try
        {
            var uri = path.StartsWith("pack://", StringComparison.Ordinal)
                ? new Uri(path, UriKind.Absolute)
                : new Uri("pack://application:,,," + path, UriKind.Absolute);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource    = uri;
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private readonly Dictionary<string, int>  _signalStatus = new();
    private readonly Dictionary<string, bool> _switchNormal = new();

    private StackPanel?    _chatMessages;
    private ScrollViewer?  _chatScroll;
    private TextBox?       _chatInput;

    private readonly List<(string Id, Border Widget)> _floatingWidgets = new();
    private record WidgetPos(double X, double Y, double W, double H);
    private static readonly string LayoutFile = SysIO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMPDispatcher", "widget_layout.json");

    private const double SNAP_SHOW = 22;
    private const double SNAP_LOCK = 10;

    private void BuildWidgets(string station)
    {
        if (_overlayCanvas == null) return;
        _overlayCanvas.Children.Clear();
        _floatingWidgets.Clear();
        _chatMessages = null; _chatScroll = null; _chatInput = null;
        _signalStatus.Clear();
        _switchNormal.Clear();
    }

    private void AddWidget(string id, string title, FrameworkElement content, double left, double top, double width)
    {
        if (_overlayCanvas == null) return;

        var normalShadow = new System.Windows.Media.Effects.DropShadowEffect
        { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 3, Opacity = 0.55, Direction = 270 };
        var dragShadow = new System.Windows.Media.Effects.DropShadowEffect
        { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 8, Opacity = 0.75, Direction = 270 };

        var header = new Border
        {
            Background = Brush("#10142e"), BorderBrush = Brush("#252840"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 8, 8, 8),
            Cursor  = Cursors.SizeAll
        };
        var hRow = new Grid();
        hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleTb = new TextBlock
        {
            Text = title, FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brush("#6a7a9a"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        TextOptions.SetTextFormattingMode(titleTb, TextFormattingMode.Ideal);
        Grid.SetColumn(titleTb, 0);

        bool minimized = false;
        Border? contentWrap = null;
        var minTb = new TextBlock
        {
            Text = "вЂ“", FontSize = 14, FontWeight = FontWeights.Bold,
            Foreground = Brush("#4a5a7a"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        var minBtn = new Border
        {
            Width = 22, Height = 22, Cursor = Cursors.Hand,
            Background = Brushes.Transparent, Child = minTb
        };
        minBtn.MouseEnter += (_, _) => minTb.Foreground = Brush("#6a95ff");
        minBtn.MouseLeave += (_, _) => minTb.Foreground = Brush("#4a5a7a");
        Grid.SetColumn(minBtn, 1);

        hRow.Children.Add(titleTb);
        hRow.Children.Add(minBtn);
        header.Child = hRow;

        contentWrap = new Border
        {
            Background = Brush("#080c1e"), Padding = new Thickness(10, 8, 10, 10),
            Child = content
        };

        double resizeStartX = 0, resizeStartY = 0, resizeStartW = 0, resizeStartH = 0;
        bool resizing = false;

        var grip = new Border
        {
            Width = 14, Height = 14,
            Background = Brush("#252840"),
            CornerRadius = new CornerRadius(2, 0, 3, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Bottom,
            Cursor  = Cursors.SizeNWSE,
            Margin  = new Thickness(0, 0, 0, 0),
            ToolTip = "Drag to resize"
        };
        grip.MouseEnter += (_, _) => grip.Background = Brush("#3458e1");
        grip.MouseLeave += (_, _) => { if (!resizing) grip.Background = Brush("#252840"); };

        var widgetGrid = new Grid();
        widgetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        widgetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(header,      0);
        Grid.SetRow(contentWrap, 1);
        widgetGrid.Children.Add(header);
        widgetGrid.Children.Add(contentWrap);

        var gripCanvas = new Canvas { IsHitTestVisible = true };
        Grid.SetRowSpan(gripCanvas, 2);
        widgetGrid.Children.Add(gripCanvas);
        widgetGrid.Loaded += (_, _) =>
        {
            gripCanvas.Width  = widgetGrid.ActualWidth;
            gripCanvas.Height = widgetGrid.ActualHeight;
            Canvas.SetLeft(grip, widgetGrid.ActualWidth  - 14);
            Canvas.SetTop( grip, widgetGrid.ActualHeight - 14);
            if (!gripCanvas.Children.Contains(grip))
                gripCanvas.Children.Add(grip);
        };
        widgetGrid.SizeChanged += (_, _) =>
        {
            gripCanvas.Width  = widgetGrid.ActualWidth;
            gripCanvas.Height = widgetGrid.ActualHeight;
            Canvas.SetLeft(grip, widgetGrid.ActualWidth  - 14);
            Canvas.SetTop( grip, widgetGrid.ActualHeight - 14);
        };

        var widget = new Border
        {
            Width = width,
            Background = Brush("#080c1e"),
            BorderBrush = Brush("#252840"), BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Effect = normalShadow,
            Child  = widgetGrid
        };

        Canvas.SetLeft(widget, left);
        Canvas.SetTop(widget, top);
        _overlayCanvas.Children.Add(widget);
        _floatingWidgets.Add((id, widget));

        minBtn.MouseLeftButtonUp += (_, _) =>
        {
            minimized = !minimized;
            contentWrap.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible;
            minTb.Text = minimized ? "+" : "вЂ“";
        };

        grip.MouseLeftButtonDown += (_, e) =>
        {
            resizing = true;
            resizeStartX = e.GetPosition(_overlayCanvas).X;
            resizeStartY = e.GetPosition(_overlayCanvas).Y;
            resizeStartW = widget.ActualWidth;
            resizeStartH = widget.ActualHeight;
            if (double.IsNaN(contentWrap.Height) || contentWrap.Height == 0)
                contentWrap.Height = contentWrap.ActualHeight;
            resizeStartH = contentWrap.Height;
            grip.CaptureMouse();
            e.Handled = true;
        };
        grip.MouseMove += (_, e) =>
        {
            if (!resizing) return;
            var p  = e.GetPosition(_overlayCanvas);
            double dw = p.X - resizeStartX;
            double dh = p.Y - resizeStartY;
            double nw = Math.Max(180, resizeStartW + dw);
            double nh = Math.Max(60,  resizeStartH + dh);
            widget.Width = nw;
            contentWrap.Height = nh;
        };
        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!resizing) return;
            resizing = false;
            grip.Background = Brush("#252840");
            grip.ReleaseMouseCapture();
            SaveWidgetPositions();
        };

        Point dragOffset = default;
        bool  dragging   = false;

        header.MouseLeftButtonDown += (_, e) =>
        {
            dragging = true;
            dragOffset = new Point(
                e.GetPosition(_overlayCanvas).X - Canvas.GetLeft(widget),
                e.GetPosition(_overlayCanvas).Y - Canvas.GetTop(widget));
            header.CaptureMouse();
            widget.Effect = dragShadow;
            Panel.SetZIndex(widget, 99);
        };

        header.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var mp = e.GetPosition(_overlayCanvas);
            double cx = mp.X - dragOffset.X;
            double cy = mp.Y - dragOffset.Y;

            double cw = _overlayCanvas.ActualWidth;
            double ch = _overlayCanvas.ActualHeight;
            cx = Math.Max(0, Math.Min(cx, cw - widget.ActualWidth));
            cy = Math.Max(0, Math.Min(cy, ch - widget.ActualHeight));

            (cx, cy) = ApplyWidgetSnap(widget, cx, cy);

            Canvas.SetLeft(widget, cx);
            Canvas.SetTop(widget,  cy);
        };

        header.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            header.ReleaseMouseCapture();
            widget.Effect = normalShadow;
            Panel.SetZIndex(widget, 1);
            ClearSnapIndicators(widget);
            SaveWidgetPositions();
        };
    }

    private (double x, double y) ApplyWidgetSnap(Border moving, double cx, double cy)
    {
        if (_overlayCanvas == null) return (cx, cy);
        double w  = moving.ActualWidth;
        double h  = moving.ActualHeight;
        double cw = _overlayCanvas.ActualWidth;
        double ch = _overlayCanvas.ActualHeight;

        var vx = new List<double> { 0, cw };
        var vy = new List<double> { 0, ch };

        foreach (var (_, wid) in _floatingWidgets)
        {
            if (wid == moving) continue;
            double ox = Canvas.GetLeft(wid);
            double oy = Canvas.GetTop(wid);
            double ow = wid.ActualWidth;
            double oh = wid.ActualHeight;
            vx.Add(ox); vx.Add(ox + ow);
            vy.Add(oy); vy.Add(oy + oh);
        }

        double sx = cx, sy = cy;
        bool sL = false, sR = false, sT = false, sB = false;

        foreach (var vline in vx)
        {
            if (Math.Abs(cx     - vline) < SNAP_SHOW) sL = true;
            if (Math.Abs(cx + w - vline) < SNAP_SHOW) sR = true;
            if (Math.Abs(cx     - vline) < SNAP_LOCK) sx = vline;
            if (Math.Abs(cx + w - vline) < SNAP_LOCK) sx = vline - w;
        }
        foreach (var vline in vy)
        {
            if (Math.Abs(cy     - vline) < SNAP_SHOW) sT = true;
            if (Math.Abs(cy + h - vline) < SNAP_SHOW) sB = true;
            if (Math.Abs(cy     - vline) < SNAP_LOCK) sy = vline;
            if (Math.Abs(cy + h - vline) < SNAP_LOCK) sy = vline - h;
        }

        moving.BorderBrush = Brush("#252840");
        if (sL || sR || sT || sB)
        {
            var b = new Thickness(
                sL ? 2.5 : 1.5,
                sT ? 2.5 : 1.5,
                sR ? 2.5 : 1.5,
                sB ? 2.5 : 1.5);
            moving.BorderThickness = b;
            moving.BorderBrush = (sL || sR || sT || sB) ? Brush("#e13458") : Brush("#252840");
        }
        else
        {
            moving.BorderBrush     = Brush("#252840");
            moving.BorderThickness = new Thickness(1.5);
        }

        return (sx, sy);
    }

    private void ClearSnapIndicators(Border widget)
    {
        widget.BorderBrush     = Brush("#252840");
        widget.BorderThickness = new Thickness(1.5);
    }

    private void DockWidgetsBottom()
    {
        if (_overlayCanvas == null || _floatingWidgets.Count == 0) return;

        var saved = LoadWidgetPositions();
        if (saved != null && saved.Count > 0)
        {
            foreach (var (id, widget) in _floatingWidgets)
            {
                if (!saved.TryGetValue(id, out var pos)) continue;
                Canvas.SetLeft(widget, pos.X);
                Canvas.SetTop( widget, pos.Y);
                widget.Width = pos.W;
                if (pos.H > 0)
                {
                    if (widget.Child is Grid g)
                    {
                        var cw = g.Children.OfType<Border>().LastOrDefault(b => b != g.Children[0]);
                        if (cw != null) cw.Height = pos.H;
                    }
                }
            }
        }
        else
        {
            double x = 12, y = 12;
            foreach (var (_, widget) in _floatingWidgets)
            {
                Canvas.SetLeft(widget, x);
                Canvas.SetTop( widget, y);
                y += widget.ActualHeight;
            }
        }
    }

    private void SaveWidgetPositions()
    {
        try
        {
            var dict = new Dictionary<string, WidgetPos>();
            foreach (var (id, widget) in _floatingWidgets)
            {
                double contentH = 0;
                if (widget.Child is Grid g)
                {
                    var cw = g.Children.OfType<Border>().Skip(1).FirstOrDefault();
                    if (cw != null && !double.IsNaN(cw.Height)) contentH = cw.Height;
                }
                dict[id] = new WidgetPos(Canvas.GetLeft(widget), Canvas.GetTop(widget),
                                          widget.ActualWidth, contentH);
            }
            SysIO.Directory.CreateDirectory(SysIO.Path.GetDirectoryName(LayoutFile)!);
            SysIO.File.WriteAllText(LayoutFile, JsonSerializer.Serialize(dict));
        }
        catch { }
    }

    private Dictionary<string, WidgetPos>? LoadWidgetPositions()
    {
        try
        {
            if (!SysIO.File.Exists(LayoutFile)) return null;
            return JsonSerializer.Deserialize<Dictionary<string, WidgetPos>>(SysIO.File.ReadAllText(LayoutFile));
        }
        catch { return null; }
    }

    private FrameworkElement MakeChatContent()
    {
        var outer = new StackPanel();

        _chatMessages = new StackPanel();
        _chatMessages.Children.Add(new TextBlock
        {
            Text = "[System] Connected to dispatch network.",
            FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Foreground = Brush("#6a7a9a"), Margin = new Thickness(0, 0, 0, 3)
        });
        _chatMessages.Children.Add(new TextBlock
        {
            Text = "[Dispatcher] Ready for duty.",
            FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Foreground = Brush("#6a95ff")
        });

        _chatScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            MaxHeight = 110, Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 0, 8),
            Content = _chatMessages
        };

        var inputGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _chatInput = new TextBox
        {
            Style = TryFindResource("LightTextBox") as Style,
            VerticalAlignment = VerticalAlignment.Center, Height = 34
        };
        _chatInput.KeyDown += ChatInput_KeyDown;
        Grid.SetColumn(_chatInput, 0);

        var sendBtn = new Button
        {
            Content = "Send", Padding = new Thickness(12, 0, 12, 0),
            Height = 34, Margin = new Thickness(6, 0, 0, 0),
            Style  = TryFindResource("BlueButton") as Style
        };
        sendBtn.Click += OnSendChat_Click;
        Grid.SetColumn(sendBtn, 1);

        inputGrid.Children.Add(_chatInput);
        inputGrid.Children.Add(sendBtn);

        var onlineRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6)
        };
        onlineRow.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Brush("#28A745"), VerticalAlignment = VerticalAlignment.Center });
        onlineRow.Children.Add(new TextBlock
        {
            Text = " Online", FontSize = 10, Foreground = Brush("#28A745"),
            FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center
        });

        outer.Children.Add(onlineRow);
        outer.Children.Add(_chatScroll);
        outer.Children.Add(inputGrid);
        return outer;
    }

    private static FrameworkElement MakeTrainListBox(string[] trains)
    {
        var scroll = new ScrollViewer
        {
            MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Background = Brushes.Transparent,
            Margin     = new Thickness(0, 0, 0, 0)
        };
        var sp = new StackPanel();
        foreach (var t in trains)
        {
            var card = new Border
            {
                Background = Brush("#0f1428"),
                Padding    = new Thickness(10, 7, 10, 7),
                Margin     = new Thickness(0, 0, 0, 3),
                CornerRadius = new CornerRadius(5)
            };
            card.MouseEnter += (_, _) => card.Background = Brush("#1a1d38");
            card.MouseLeave += (_, _) => card.Background = Brush("#0f1428");

            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            int close = t.IndexOf(']');
            if (close > 0)
            {
                string tag  = t[..(close + 1)] + " ";
                string name = t[(close + 1)..].Trim();
                inner.Children.Add(new TextBlock
                {
                    Text = tag, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold, Foreground = Brush("#6a95ff"),
                    VerticalAlignment = VerticalAlignment.Center
                });
                inner.Children.Add(new TextBlock
                {
                    Text = name, FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                    FontWeight = FontWeights.SemiBold, Foreground = Brush("#8a9ab8"),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                inner.Children.Add(new TextBlock
                {
                    Text = t, FontSize = 12, FontFamily = new FontFamily("Consolas"),
                    Foreground = Brush("#6a7a9a")
                });
            }
            card.Child = inner;
            sp.Children.Add(card);
        }
        scroll.Content = sp;
        return scroll;
    }

    private FrameworkElement MakeSignalRow(string signal)
    {
        int pendingStatus = _signalStatus.TryGetValue(signal, out int s) ? s : 0;
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 3) };

        var card = new Border
        {
            Background = Brush("#0f1428"),
            Padding    = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(5),
            Cursor     = Cursors.Hand
        };

        var rg = new Grid();
        rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 10, Height = 10, Fill = SignalBrush(0),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(dot, 0);

        var nameTb = new TextBlock
        {
            Text = signal, FontSize = 12, FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold, Foreground = Brush("#b8c4e8"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameTb, 1);

        var statusTb = new TextBlock
        {
            Text = SignalLabel(0), FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = SignalBrush(0), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 8, 0)
        };
        Grid.SetColumn(statusTb, 2);

        var arrowTb = new TextBlock
        {
            Text = "вЂє", FontSize = 16, FontWeight = FontWeights.Bold,
            Foreground = Brush("#BBBBBB"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrowTb, 3);

        rg.Children.Add(dot);
        rg.Children.Add(nameTb);
        rg.Children.Add(statusTb);
        rg.Children.Add(arrowTb);
        card.Child = rg;

        var selector = new Border
        {
            Background = Brush("#0a0e20"), Visibility = Visibility.Collapsed,
            Padding    = new Thickness(10, 8, 10, 8),
            Margin     = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0, 0, 5, 5)
        };

        var selRow = new Grid();
        selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var optRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var optRed    = MakeColorOption("в—Џ STOP",    "#e13458");
        var optYellow = MakeColorOption("в—Џ CAUTION", "#E8A000");
        var optGreen  = MakeColorOption("в—Џ CLEAR",   "#28A745");
        UpdateColorOptions(pendingStatus, optRed, optYellow, optGreen);

        optRed.MouseLeftButtonUp    += (_, _) => { pendingStatus = 0; UpdateColorOptions(0, optRed, optYellow, optGreen); };
        optYellow.MouseLeftButtonUp += (_, _) => { pendingStatus = 1; UpdateColorOptions(1, optRed, optYellow, optGreen); };
        optGreen.MouseLeftButtonUp  += (_, _) => { pendingStatus = 2; UpdateColorOptions(2, optRed, optYellow, optGreen); };

        optRow.Children.Add(optRed);
        optRow.Children.Add(optYellow);
        optRow.Children.Add(optGreen);
        Grid.SetColumn(optRow, 0);

        var confirmBtn = new Button
        {
            Content = "CONFIRM", Padding = new Thickness(10, 6, 10, 6),
            Cursor  = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
            Style   = TryFindResource("BlueButton") as Style
        };
        Grid.SetColumn(confirmBtn, 1);

        selRow.Children.Add(optRow);
        selRow.Children.Add(confirmBtn);
        selector.Child = selRow;

        bool expanded = false;
        card.MouseLeftButtonUp += (_, _) =>
        {
            expanded = !expanded;
            selector.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            arrowTb.Text = expanded ? "Л…" : "вЂє";
        };
        card.MouseEnter += (_, _) => card.Background = Brush("#1a1d38");
        card.MouseLeave += (_, _) => card.Background = Brush("#0f1428");

        confirmBtn.Click += (_, _) =>
        {
            _signalStatus[signal] = pendingStatus;
            dot.Fill          = SignalBrush(pendingStatus);
            statusTb.Text     = SignalLabel(pendingStatus);
            statusTb.Foreground = SignalBrush(pendingStatus);
            expanded = false;
            selector.Visibility = Visibility.Collapsed;
            arrowTb.Text = "вЂє";
            AddChatMessage("System", $"Signal {signal} в†’ {SignalLabel(pendingStatus)}", "#888888");
        };

        container.Children.Add(card);
        container.Children.Add(selector);
        return container;
    }

    private FrameworkElement MakeSwitchRow(string name)
    {
        var card = new Border
        {
            Background = Brush("#0f1428"),
            Padding    = new Thickness(10, 8, 10, 8),
            Margin     = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(5)
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameTb = new TextBlock
        {
            Text = name, FontSize = 14, FontWeight = FontWeights.Black,
            Foreground = Brush("#dce4ff"), FontFamily = new FontFamily("Segoe UI")
        };
        TextOptions.SetTextFormattingMode(nameTb, TextFormattingMode.Ideal);

        var statusTb = new TextBlock
        {
            Text = "Normal", FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#28A745"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        info.Children.Add(nameTb);
        info.Children.Add(statusTb);
        Grid.SetColumn(info, 0);

        var revBtn = new Border
        {
            BorderBrush     = Brush("#e13458"),
            BorderThickness = new Thickness(1.5),
            Background      = Brush("#0f1428"),
            Padding         = new Thickness(10, 5, 10, 5),
            Cursor          = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var revTb = new TextBlock
        {
            Text = "REVERSE", FontSize = 11, FontWeight = FontWeights.Black,
            Foreground = Brush("#e13458"), FontFamily = new FontFamily("Segoe UI")
        };
        revBtn.Child = revTb;
        Grid.SetColumn(revBtn, 1);

        revBtn.MouseEnter += (_, _) => { revBtn.Background = Brush("#e13458"); revTb.Foreground = Brushes.White; };
        revBtn.MouseLeave += (_, _) => { revBtn.Background = Brush("#0f1428"); revTb.Foreground = Brush("#e13458"); };
        card.MouseEnter   += (_, _) => card.Background = Brush("#1a1d38");
        card.MouseLeave   += (_, _) => card.Background = Brush("#0f1428");

        revBtn.MouseLeftButtonUp += (_, _) =>
        {
            _switchNormal[name] = !_switchNormal[name];
            bool normal = _switchNormal[name];
            statusTb.Text       = normal ? "Normal" : "Reversed";
            statusTb.Foreground = Brush(normal ? "#28A745" : "#e13458");
            AddChatMessage("System", $"Switch {name} в†’ {(normal ? "Normal" : "Reversed")}", "#888888");
        };

        g.Children.Add(info);
        g.Children.Add(revBtn);
        card.Child = g;
        return card;
    }

    private static Border MakeColorOption(string label, string color)
    {
        var bd = new Border
        {
            BorderBrush     = Brush(color),
            BorderThickness = new Thickness(1.5),
            Background      = Brush("#0a0e20"),
            Padding         = new Thickness(7, 4, 7, 4),
            Margin          = new Thickness(0, 0, 6, 0),
            Cursor          = Cursors.Hand
        };
        bd.Child = new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = Brush(color), FontFamily = new FontFamily("Segoe UI")
        };
        bd.MouseEnter += (_, _) => bd.Opacity = 0.75;
        bd.MouseLeave += (_, _) => bd.Opacity = 1.0;
        return bd;
    }

    private static void UpdateColorOptions(int selected, Border r, Border y, Border g)
    {
        (Border bd, string color)[] opts = [(r, "#e13458"), (y, "#E8A000"), (g, "#28A745")];
        for (int i = 0; i < opts.Length; i++)
        {
            bool sel = i == selected;
            opts[i].bd.Background = Brush(sel ? opts[i].color : "#0a0e20");
            if (opts[i].bd.Child is TextBlock tb)
                tb.Foreground = sel ? Brushes.White : Brush(opts[i].color);
        }
    }

    private static SolidColorBrush SignalBrush(int status) => status switch
    {
        1 => Brush("#E8A000"),
        2 => Brush("#28A745"),
        _ => Brush("#e13458")
    };

    private static string SignalLabel(int status) => status switch
    {
        1 => "CAUTION",
        2 => "CLEAR",
        _ => "STOP"
    };

    private void OnSendChat_Click(object sender, RoutedEventArgs e) => SendChat();

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SendChat();
    }

    private void SendChat()
    {
        if (_chatInput == null) return;
        var msg = _chatInput.Text.Trim();
        if (string.IsNullOrEmpty(msg)) return;
        AddChatMessage(DispatcherName, msg, "#3458e1");
        _chatInput.Text = string.Empty;
    }

    private void AddChatMessage(string sender, string text, string colorHex)
    {
        if (_chatMessages == null) return;
        _chatMessages.Children.Add(new TextBlock
        {
            Text = $"[{sender}] {text}",
            FontFamily = new FontFamily("Consolas"), FontSize = 11,
            Foreground = Brush(colorHex), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 3)
        });
        _chatScroll?.ScrollToEnd();
    }
}
