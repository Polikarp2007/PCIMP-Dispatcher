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
    private record StationInfo(
        string Name,
        string StationType,
        string Description,
        int Platforms,
        int ActivePlayers,
        string[] TrainClasses,
        int ArrivingTrains,
        int OnStationWaiting,
        int DepartingTrains,
        (string Name, string Duration)[] PreviousShifts,
        string CoatOfArms,
        string BgImage     = "",
        bool   IsAvailable = true
    );

    private static readonly StationInfo[] RouteStations =
    [
        new("Arad", "Terminal · Departure · Arrival",
            "The main terminal of the M200 route, situated in the heart of Arad city. It features six platforms — four for passenger services including two high-speed direct-line tracks supporting speeds of up to 160 km/h — and two dedicated freight lines. Arad is the busiest junction on the route, acting as a major interchange with intercity and international connections. Fully modernized with state-of-the-art ERTMS Level 2 signaling.",
            6, 8,
            ["Class R (Regio)"],
            12, 3, 7,
            [("Alex TOP",         "2 h 30 min"),
             ("Aleksei Gurlakov", "1 h 45 min"),
             ("Polikarp K.",      "3 h 00 min"),
             ("Ivan M.",          "1 h 20 min"),
             ("D. Petrov",        "2 h 10 min")],
            "/Assets/GERB%20ARAD.png",
            "/Assets/arad%20font.jpg"),

        new("Glogovat", "Passing station",
            "A compact passing station nestled in the Mureș valley between Arad and Ghioroc. Glogovat features three platforms: two main passing lines and one freight siding. Its strategic position makes it critical for regulating train spacing during peak hours. Recently renovated switches and modernized control systems allow smooth high-speed passage.",
            3, 3,
            ["Class R (Regio)"],
            6, 1, 6,
            [("Aleksei Gurlakov", "1 h 00 min"),
             ("Alex TOP",         "0 h 55 min"),
             ("D. Petrov",        "1 h 30 min"),
             ("Polikarp K.",      "2 h 05 min"),
             ("Ivan M.",          "0 h 40 min")],
            "/Assets/GERB%20GLOGOVAT.png",
            "/Assets/glogovat%20font.png"),

        new("Ghioroc", "Passing station",
            "Ghioroc is a mid-route passing station situated near the scenic vineyard village of Ghioroc. It hosts three tracks: two high-speed through lines and one dedicated to local freight. The station plays a key role in managing train meets between R class services on the single-track sections of the route.",
            3, 4,
            ["Class R (Regio)"],
            7, 2, 7,
            [("Alex TOP",         "0 h 50 min"),
             ("Polikarp K.",      "1 h 20 min"),
             ("Aleksei Gurlakov", "2 h 00 min"),
             ("Ivan M.",          "1 h 10 min"),
             ("D. Petrov",        "0 h 45 min")],
            "/Assets/GERB%20GHIOROC.png",
            "/Assets/ghioroc%20font.png"),

        new("Paulis hc.", "Halt · Not available to dispatch",
            "", 0, 0, [], 0, 0, 0, [], "", "", false),

        new("Paulis", "Passing station",
            "Paulis station serves the Păuliș commune along the Mureș River corridor. With three operational platforms — two for passenger traffic and one freight line — it acts as a key coordination point between Ghioroc and Radna. Equipped with modern interlocking systems and automated signals for safe high-speed operations.",
            3, 2,
            ["Class R (Regio)"],
            5, 1, 5,
            [("Aleksei Gurlakov", "0 h 45 min"),
             ("Alex TOP",         "1 h 00 min"),
             ("D. Petrov",        "1 h 15 min"),
             ("Polikarp K.",      "0 h 55 min"),
             ("Ivan M.",          "1 h 30 min")],
            "/Assets/GERB%20PAULIS.png",
            "/Assets/paulis%20font.png"),

        new("Radna", "Terminus · Arrival · Departure",
            "A small station located right on the banks of the historic Mureș River. It features five platforms: four for passengers — including two high-speed \"direct\" lines that support passing speeds of up to 100 km/h — and one line dedicated to freight train parking. The station has been fully modernized under the latest upgrade plans, featuring completely renewed infrastructure, signaling systems, and switches. It sees moderate daily traffic, with commuters traveling to Arad via R class trains.",
            5, 3,
            ["Class R (Regio)"],
            5, 2, 10,
            [("Alex TOP",         "1 h 15 min"),
             ("Aleksei Gurlakov", "2 h 15 min"),
             ("Ivan M.",          "1 h 05 min"),
             ("D. Petrov",        "1 h 50 min"),
             ("Polikarp K.",      "2 h 30 min")],
            "/Assets/GERB%20RADNA.png",
            "/Assets/radna%20font.png")
    ];

    private Border? _selectedStationRow;

    private SizeChangedEventHandler? _marchingHandler;

    private Window?  _overlay;
    private Canvas?  _overlayCanvas;
    private Polyline? _antBlue, _antWhite;
    private SizeChangedEventHandler? _antHandler;

    private void LoadSetupPage()
    {
        SetupStationList.RowDefinitions.Clear();
        SetupStationList.Children.Clear();
        _selectedStationRow = null;

        StationDetailPrompt.Visibility  = Visibility.Visible;
        StationDetailBg.Background      = Brushes.White;
        StationDetailDark.Background    = Brushes.Transparent;
        StopMarchingAnts();
        while (StationDetailPanel.Children.Count > 1)
            StationDetailPanel.Children.RemoveAt(StationDetailPanel.Children.Count - 1);

        for (int i = 0; i < RouteStations.Length; i++)
        {
            SetupStationList.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var row = BuildStationRow(RouteStations[i]);
            Grid.SetRow(row, i);
            SetupStationList.Children.Add(row);
        }
    }

    private Border BuildStationRow(StationInfo station)
    {
        bool isAvailable = station.IsAvailable;
        bool isTerminus  = isAvailable &&
                           (station.StationType.Contains("Terminal") ||
                            station.StationType.Contains("Terminus"));

        var outerBorder = new Border
        {
            CornerRadius      = new CornerRadius(8),
            Padding           = new Thickness(0, 0, 8, 0),
            Cursor            = isAvailable ? Cursors.Hand : Cursors.Arrow,
            Background        = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var rowGrid = new Grid();
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var circle = new Ellipse
        {
            Width  = isAvailable ? (isTerminus ? 20 : 14) : 10,
            Height = isAvailable ? (isTerminus ? 20 : 14) : 10,
            Fill   = isAvailable
                         ? Brush(isTerminus ? "#CC1122" : "#3458e1")
                         : Brush("#CCCCCC"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        Grid.SetColumn(circle, 0);

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        var nameTb = new TextBlock
        {
            Text       = station.Name,
            FontSize   = isAvailable ? 18 : 15,
            FontWeight = isAvailable ? FontWeights.Black : FontWeights.Bold,
            Foreground = isAvailable ? Brush("#1a1a2e") : Brush("#888888"),
            FontFamily = new FontFamily("Segoe UI")
        };
        TextOptions.SetTextFormattingMode(nameTb, TextFormattingMode.Ideal);
        RenderOptions.SetClearTypeHint(nameTb, ClearTypeHint.Enabled);
        textPanel.Children.Add(nameTb);

        var typeTb = new TextBlock
        {
            Text       = station.StationType,
            FontSize   = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = isAvailable
                             ? (isTerminus ? Brush("#CC1122") : Brush("#666666"))
                             : Brush("#AAAAAA"),
            FontFamily = new FontFamily("Segoe UI")
        };
        textPanel.Children.Add(typeTb);
        Grid.SetColumn(textPanel, 1);

        if (isAvailable)
        {
            var coatBorder = new Border
            {
                Width             = 40,
                Height            = 40,
                CornerRadius      = new CornerRadius(6),
                Background        = Brush("#EEF0FF"),
                BorderBrush       = Brush("#D8DCFF"),
                BorderThickness   = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var coatImg = new Image
            {
                Width  = 30,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(coatImg, BitmapScalingMode.HighQuality);
            coatImg.Source = TryLoadBitmap(station.CoatOfArms);
            coatBorder.Child = coatImg;
            Grid.SetColumn(coatBorder, 2);
            rowGrid.Children.Add(coatBorder);
        }

        rowGrid.Children.Add(circle);
        rowGrid.Children.Add(textPanel);
        outerBorder.Child = rowGrid;

        if (isAvailable)
        {
            string hoverBg    = isTerminus ? "#FFF0F0" : "#F2F4FF";
            string selectedBg = isTerminus ? "#FFE0E0" : "#E8EBFF";

            outerBorder.MouseEnter += (_, _) =>
            {
                if (outerBorder != _selectedStationRow)
                    outerBorder.Background = Brush(hoverBg);
            };
            outerBorder.MouseLeave += (_, _) =>
            {
                if (outerBorder != _selectedStationRow)
                    outerBorder.Background = Brushes.Transparent;
            };
            outerBorder.MouseLeftButtonUp += (_, _) =>
            {
                if (_selectedStationRow is not null)
                    _selectedStationRow.Background = Brushes.Transparent;
                outerBorder.Background = Brush(selectedBg);
                _selectedStationRow = outerBorder;
                ShowStationDetail(station);
            };
        }

        return outerBorder;
    }

    private void ShowStationDetail(StationInfo station)
    {
        StationDetailPrompt.Visibility = Visibility.Collapsed;
        while (StationDetailPanel.Children.Count > 1)
            StationDetailPanel.Children.RemoveAt(StationDetailPanel.Children.Count - 1);

        TextOptions.SetTextFormattingMode(StationDetailPanel, TextFormattingMode.Display);
        RenderOptions.SetClearTypeHint(StationDetailPanel, ClearTypeHint.Enabled);

        if (!string.IsNullOrEmpty(station.BgImage))
        {
            var bmp = TryLoadBitmap(station.BgImage);
            StationDetailBg.Background = bmp != null
                ? new ImageBrush(bmp)
                    { Stretch = Stretch.UniformToFill,
                      AlignmentX = AlignmentX.Center,
                      AlignmentY = AlignmentY.Center }
                : Brushes.White;
            StationDetailDark.Background = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255));
            StartMarchingAnts();
        }
        else
        {
            StationDetailBg.Background   = Brushes.White;
            StationDetailDark.Background = Brushes.Transparent;
            StopMarchingAnts();
        }

        bool isTerminus = station.StationType.Contains("Terminal") || station.StationType.Contains("Terminus");
        string accent = isTerminus ? "#CC1122" : "#3458e1";

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var coatBorder = new Border
        {
            Width             = 68,
            Height            = 68,
            CornerRadius      = new CornerRadius(10),
            Background        = Brush("#F0F2FF"),
            BorderBrush       = Brush("#D0D4FF"),
            BorderThickness   = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(0, 0, 18, 0)
        };
        var coatImg = new Image
        {
            Width  = 50, Height = 50,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Stretch = Stretch.Uniform
        };
        RenderOptions.SetBitmapScalingMode(coatImg, BitmapScalingMode.HighQuality);
        coatImg.Source = TryLoadBitmap(station.CoatOfArms);
        coatBorder.Child = coatImg;
        Grid.SetColumn(coatBorder, 0);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        var titleTb = new TextBlock
        {
            Text       = station.Name,
            FontSize   = 28,
            FontWeight = FontWeights.Black,
            Foreground = Brush("#000000"),
            FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        TextOptions.SetTextFormattingMode(titleTb, TextFormattingMode.Ideal);
        RenderOptions.SetClearTypeHint(titleTb, ClearTypeHint.Enabled);
        titleStack.Children.Add(titleTb);

        var typeBadge = new Border
        {
            Background          = Brush(accent),
            CornerRadius        = new CornerRadius(4),
            Padding             = new Thickness(9, 3, 9, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(0, 7, 0, 0)
        };
        typeBadge.Child = new TextBlock
        {
            Text = station.StationType, FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI")
        };
        titleStack.Children.Add(typeBadge);
        Grid.SetColumn(titleStack, 1);

        headerGrid.Children.Add(coatBorder);
        headerGrid.Children.Add(titleStack);
        StationDetailPanel.Children.Add(headerGrid);

        AddDivider(0, 12);
        var descTb = new TextBlock
        {
            Text         = station.Description,
            FontSize     = 14,
            FontWeight   = FontWeights.SemiBold,
            Foreground   = Brush("#1a1a2e"),
            FontFamily   = new FontFamily("Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight   = 24,
            Margin       = new Thickness(0, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth     = 680
        };
        TextOptions.SetTextFormattingMode(descTb, TextFormattingMode.Ideal);
        StationDetailPanel.Children.Add(descTb);

        var badgeRow = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin              = new Thickness(0, 0, 0, 0)
        };

        var platBadge = new Border
        {
            Background   = Brush(accent),
            CornerRadius = new CornerRadius(5),
            Padding      = new Thickness(12, 5, 12, 5),
            Margin       = new Thickness(0, 0, 8, 0),
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextRenderingMode(platBadge, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(platBadge, TextFormattingMode.Ideal);
        var platRow = new StackPanel { Orientation = Orientation.Horizontal };
        platRow.Children.Add(MakeBadgeNum(station.Platforms.ToString(), "White"));
        platRow.Children.Add(MakeBadgeLbl(" Platforms", "White"));
        platBadge.Child = platRow;
        badgeRow.Children.Add(platBadge);

        var playBadge = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#28A745"),
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(5),
            Padding         = new Thickness(12, 5, 12, 5),
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextRenderingMode(playBadge, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(playBadge, TextFormattingMode.Ideal);
        var playRow = new StackPanel { Orientation = Orientation.Horizontal };
        playRow.Children.Add(MakeBadgeNum(station.ActivePlayers.ToString(), "#28A745"));
        playRow.Children.Add(MakeBadgeLbl(" Players in zone", "#28A745"));
        playBadge.Child = playRow;
        badgeRow.Children.Add(playBadge);

        StationDetailPanel.Children.Add(badgeRow);

        AddDivider(14, 10);
        AddSectionLabel("AVAILABLE TRAIN CLASSES AT THIS STATION");

        foreach (var cls in station.TrainClasses)
        {
            var clsTb = new TextBlock
            {
                FontSize   = 15,
                FontFamily = new FontFamily("Segoe UI"),
                Margin     = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            TextOptions.SetTextFormattingMode(clsTb, TextFormattingMode.Ideal);

            var words = cls.Split(' ');
            if (words.Length >= 2 && words[0] == "Class")
            {
                clsTb.Inlines.Add(new Run("• Class ")
                    { Foreground = Brush("#333333"), FontWeight = FontWeights.SemiBold });
                clsTb.Inlines.Add(new Run(words[1])
                    { Foreground = Brush("#CC1122"), FontWeight = FontWeights.Black, FontSize = 17 });
                if (words.Length > 2)
                    clsTb.Inlines.Add(new Run(" " + string.Join(" ", words, 2, words.Length - 2))
                        { Foreground = Brush("#555555"), FontWeight = FontWeights.SemiBold });
            }
            else
            {
                clsTb.Inlines.Add(new Run("• " + cls)
                    { Foreground = Brush("#333333"), FontWeight = FontWeights.SemiBold });
            }

            StationDetailPanel.Children.Add(clsTb);
        }

        AddDivider(14, 10);
        AddSectionLabel("TRAFFIC OPERATIONS");
        AddInfoRow("Arriving trains:",            station.ArrivingTrains.ToString());
        AddInfoRow("On station / Wait for depart:", station.OnStationWaiting.ToString());
        AddInfoRow("Departing trains:",           station.DepartingTrains.ToString());

        AddDivider(14, 10);
        AddSectionLabel("PREVIOUS DISPATCHER SHIFTS");
        foreach (var shift in station.PreviousShifts)
            AddInfoRow(shift.Name + ":", shift.Duration);

        AddDivider(14, 10);
        AddSectionLabel("CURRENT DISPATCHER");

        var dispCard = new Border
        {
            BorderBrush     = Brush("#E0E0E0"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth        = 340
        };
        var dispGrid = new Grid();
        dispGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        dispGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentBar = new Border { Background = Brush("#E8A000") };
        Grid.SetColumn(accentBar, 0);

        var dispContent = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };

        var dispTitle = new TextBlock
        {
            Text = "No dispatcher on duty",
            FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#1a1a2e"), FontFamily = new FontFamily("Segoe UI")
        };
        TextOptions.SetTextFormattingMode(dispTitle, TextFormattingMode.Ideal);
        RenderOptions.SetClearTypeHint(dispTitle, ClearTypeHint.Enabled);

        var dispSub = new TextBlock
        {
            Text = "Shift available for takeover",
            FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#888888"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 3, 0, 0)
        };

        dispContent.Children.Add(dispTitle);
        dispContent.Children.Add(dispSub);
        Grid.SetColumn(dispContent, 1);

        dispGrid.Children.Add(accentBar);
        dispGrid.Children.Add(dispContent);
        dispCard.Child = dispGrid;
        StationDetailPanel.Children.Add(dispCard);

        var connectBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin  = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(22, 11, 22, 11),
            Cursor  = Cursors.Hand,
            Tag     = station.Name,
            Style   = (TryFindResource("OutlineButton") as Style) ?? (Style)FindResource("BlueButton")
        };
        connectBtn.Click += OnStationConnect_Click;

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(new TextBlock
        {
            Text = $"Take over dispatching at {station.Name}",
            FontSize = 14, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI")
        });
        btnRow.Children.Add(new TextBlock
            { Text = "  →", FontSize = 14, Opacity = 0.7 });
        connectBtn.Content = btnRow;
        StationDetailPanel.Children.Add(connectBtn);
    }

    private void ShowOverlay()
    {
        HideOverlay();
        _overlayCanvas = new Canvas();
        _overlay = new Window
        {
            WindowStyle        = WindowStyle.None,
            ResizeMode         = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background         = Brushes.Transparent,
            ShowInTaskbar      = false,
            Content            = _overlayCanvas,
            Owner              = this
        };
        _overlay.Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
            new Action(UpdateOverlayBounds));
        this.SizeChanged += OnMainWindowSizeChanged;
    }

    private void HideOverlay()
    {
        this.SizeChanged -= OnMainWindowSizeChanged;
        _antHandler = null; _antBlue = null; _antWhite = null;
        _overlay?.Close();
        _overlay = null; _overlayCanvas = null;
    }

    private void OnMainWindowSizeChanged(object s, SizeChangedEventArgs e) => UpdateOverlayBounds();

    private void UpdateOverlayBounds()
    {
        if (_overlay == null || !IsLoaded) return;
        const double tbH = 72;
        try
        {
            var pt  = this.PointToScreen(new Point(0, tbH));
            var src = PresentationSource.FromVisual(this);
            double sx = src?.CompositionTarget.TransformToDevice.M11 ?? 1;
            double sy = src?.CompositionTarget.TransformToDevice.M22 ?? 1;
            _overlay.Left   = pt.X / sx;
            _overlay.Top    = pt.Y / sy;
            _overlay.Width  = this.ActualWidth;
            _overlay.Height = this.ActualHeight - tbH;
        }
        catch { }
    }

    private void StartMapAnts()
    {
        StopMapAnts();
        if (_overlayCanvas == null) return;
        const double thick = 2.0, dashOn = 9.0, dashOff = 7.0, dur = 4.0;
        const double period = dashOn + dashOff;

        _antBlue  = MakeMarchingLine(Brush("#3458e1"), thick, dashOn, dashOff, 0,          dur);
        _antWhite = MakeMarchingLine(Brushes.White,    thick, dashOn, dashOff, period / 2, dur);
        _overlayCanvas.Children.Add(_antBlue);
        _overlayCanvas.Children.Add(_antWhite);

        void Update(object? s, SizeChangedEventArgs e)
        {
            ApplyMarchingPoints(_antBlue!,  _overlayCanvas, thick);
            ApplyMarchingPoints(_antWhite!, _overlayCanvas, thick);
        }
        _antHandler = Update;
        _overlayCanvas.SizeChanged += Update;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            ApplyMarchingPoints(_antBlue!,  _overlayCanvas, thick);
            ApplyMarchingPoints(_antWhite!, _overlayCanvas, thick);
        });
    }

    private void StopMapAnts()
    {
        if (_antHandler != null && _overlayCanvas != null)
            _overlayCanvas.SizeChanged -= _antHandler;
        _antHandler = null;
        _antBlue?.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        _antWhite?.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        if (_overlayCanvas != null)
        {
            _overlayCanvas.Children.Remove(_antBlue!);
            _overlayCanvas.Children.Remove(_antWhite!);
        }
        _antBlue = null; _antWhite = null;
    }

    private void StartMarchingAnts()
    {
        StopMarchingAnts();

        const double thick    = 2.0;
        const double dashOn   = 9.0;
        const double dashOff  = 7.0;
        const double period   = dashOn + dashOff;

        var blue  = MakeMarchingLine(Brush("#3458e1"), thick, dashOn, dashOff, 0);
        var white = MakeMarchingLine(Brushes.White,    thick, dashOn, dashOff, period / 2);

        StationDetailCanvas.Children.Add(blue);
        StationDetailCanvas.Children.Add(white);

        void Update(object? s, SizeChangedEventArgs e)
        {
            ApplyMarchingPoints(blue,  StationDetailCanvas, thick);
            ApplyMarchingPoints(white, StationDetailCanvas, thick);
        }
        _marchingHandler = Update;
        StationDetailCanvas.SizeChanged += Update;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            ApplyMarchingPoints(blue,  StationDetailCanvas, thick);
            ApplyMarchingPoints(white, StationDetailCanvas, thick);
        });
    }

    private void StopMarchingAnts()
    {
        if (_marchingHandler != null)
        {
            StationDetailCanvas.SizeChanged -= _marchingHandler;
            _marchingHandler = null;
        }
        foreach (UIElement el in StationDetailCanvas.Children)
            if (el is Polyline p)
                p.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
        StationDetailCanvas.Children.Clear();
    }

    private static Polyline MakeMarchingLine(
        Brush stroke, double thick, double dashOn, double dashOff, double startOffset,
        double durationSecs = 1.0)
    {
        double period = dashOn + dashOff;
        var p = new Polyline
        {
            Stroke           = stroke,
            StrokeThickness  = thick,
            StrokeDashArray  = new DoubleCollection { dashOn, dashOff },
            IsHitTestVisible = false
        };
        var anim = new DoubleAnimation(startOffset, startOffset + period,
                                       TimeSpan.FromSeconds(durationSecs))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = null
        };
        p.BeginAnimation(Shape.StrokeDashOffsetProperty, anim);
        return p;
    }

    private static void ApplyMarchingPoints(Polyline p, Canvas canvas, double thick)
    {
        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;
        if (w < 4 || h < 4) return;

        double m = thick / 2.0;
        p.Points = new PointCollection
        {
            new Point(w - m, h - m),
            new Point(m,     h - m),
            new Point(m,     m),
            new Point(w - m, m)
        };
    }

    private static TextBlock MakeBadgeNum(string text, string colorHex)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 18, FontWeight = FontWeights.Black,
            Foreground = Brush(colorHex), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment   = VerticalAlignment.Center,
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
        return tb;
    }

    private static TextBlock MakeBadgeLbl(string text, string colorHex)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 13, FontWeight = FontWeights.Bold,
            Foreground = Brush(colorHex), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment   = VerticalAlignment.Center,
            UseLayoutRounding   = true,
            SnapsToDevicePixels = true
        };
        TextOptions.SetTextRenderingMode(tb, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Ideal);
        return tb;
    }

    private void AddDivider(double top = 0, double bottom = 0)
        => StationDetailPanel.Children.Add(new Border
        {
            Height = 1, Background = Brush("#DDDDDD"),
            Margin = new Thickness(0, top, 0, bottom)
        });

    private void AddSectionLabel(string text)
        => StationDetailPanel.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        });

    private void AddInfoRow(string label, string value)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 5, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        row.Children.Add(new TextBlock
        {
            Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#555555"), FontFamily = new FontFamily("Segoe UI"),
            MinWidth = 200
        });
        row.Children.Add(new TextBlock
        {
            Text = value, FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#000000"), FontFamily = new FontFamily("Segoe UI")
        });
        StationDetailPanel.Children.Add(row);
    }

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
}

