using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace PCMPDispatcher;

public partial class DrvSetupView : UserControl
{
    // Canonical line order Radna → Arad and running times (minutes) between stations.
    private static readonly string[] _lineRA =
        { "Radna", "Paulis", "Paulis hc.", "Ghioroc", "Glogovat", "Arad" };
    private static readonly int[] _segRA = { 5, 3, 3, 9, 7 };

    // Departure platforms per origin station.
    private static readonly string[] _platRadna =
        { "Linia 1", "Linia 2 [D]", "Linia 3 [D]", "Linia 4", "Linia 5 [M]" };
    private static readonly string[] _platArad =
        { "Linia 1", "Linia 2", "Linia 3 [D]", "Linia 4 [D]", "Linia 5", "Linia 6", "Linia 7" };

    private readonly List<TextBox?> _dwellBoxes = new();
    private string[] _currentOrder = Array.Empty<string>();
    private int[] _currentSegs = Array.Empty<int>();

    /// <summary>Raised with a config snapshot when the user proceeds to the Final page.</summary>
    public event Action<DriverRunConfig>? GoFinalRequested;
    /// <summary>Raised when the user presses Back.</summary>
    public event Action? BackRequested;

    public DrvSetupView()
    {
        InitializeComponent();
    }

    /// <summary>Reset the form to sensible defaults and fade the page in.</summary>
    public void Open()
    {
        // Suggest departure = current PC time + 10 minutes.
        var suggested = DateTime.Now.AddMinutes(10);
        DrvHH.Text = suggested.ToString("HH");
        DrvMM.Text = suggested.ToString("mm");

        if (DrvDirection.SelectedIndex < 0) DrvDirection.SelectedIndex = 0; // triggers rebuild
        else RebuildForDirection();

        InitConsist();

        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void OnDrvGoFinal(object sender, RoutedEventArgs e)
    {
        // Номер поезда обязателен
        if (string.IsNullOrWhiteSpace(DrvTrainNum.Text))
        {
            DrvTrainNum.BorderBrush = Brush("#E81123");
            DrvTrainNum.Focus();
            return;
        }
        // Время отправления: часы 0-23, минуты 0-59
        int hh = ParseInt(DrvHH.Text), mm = ParseInt(DrvMM.Text);
        bool timeOk = DrvHH.Text.Length > 0 && DrvMM.Text.Length > 0
                   && hh >= 0 && hh <= 23 && mm >= 0 && mm <= 59;
        if (!timeOk)
        {
            DrvHH.BorderBrush = Brush("#E81123");
            DrvMM.BorderBrush = Brush("#E81123");
            DrvHH.Focus();
            return;
        }
        GoFinalRequested?.Invoke(CaptureRunConfig());
    }

    private void OnDrvSetupBack_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke();

    // Snapshot all Set-Up selections into a self-contained config for the Final page.
    private DriverRunConfig CaptureRunConfig()
    {
        var dwell = new int[_currentOrder.Length];
        for (int i = 0; i < _dwellBoxes.Count && i < dwell.Length; i++)
            dwell[i] = ParseInt(_dwellBoxes[i]?.Text ?? "0");

        return new DriverRunConfig
        {
            DriverName  = Services.UserSession.VisibleName,
            TrainType   = (DrvTrainType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—",
            TrainNumber = string.IsNullOrWhiteSpace(DrvTrainNum.Text) ? "—" : DrvTrainNum.Text,
            Platform    = (DrvPlatform.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—",
            Loco        = (DrvLocoCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—",
            WagonType   = (DrvWagonType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—",
            Order       = (string[])_currentOrder.Clone(),
            Segments    = (int[])_currentSegs.Clone(),
            Dwell       = dwell,
            DepartMinutes = Math.Clamp(ParseInt(DrvHH.Text), 0, 23) * 60 + Math.Clamp(ParseInt(DrvMM.Text), 0, 59),
            WagonCount  = _wagonCount,
            OptRadio    = DrvOptRadio.IsChecked == true,
            OptTextOnly = DrvOptTextOnly.IsChecked == true,
            OptPriority = DrvOptPriority.IsChecked == true,
            LocoImg     = _locoImg,
            WagonImg    = _wagonImg
        };
    }

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e)
        => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");

    // Сброс красной рамки при вводе — пользователь начал исправлять поле.
    private void OnValidFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.ClearValue(TextBox.BorderBrushProperty);
    }

    // Highlight the stage (1 = left half, 2 = right half) the cursor is over.
    private void OnSetupStageHover(object sender, MouseEventArgs e)
        => SetActiveStage(e.GetPosition(this).X < ActualWidth / 2 ? 1 : 2);

    private void SetActiveStage(int stage)
    {
        bool one = stage == 1;
        DrvStep1Num.Background = one ? Brush("#3458e1") : Brush("#F0F0F0");
        DrvStep1Num.Foreground = one ? Brushes.White   : Brush("#BBBBBB");
        DrvStep1Lbl.Foreground = one ? Brush("#111111") : Brush("#BBBBBB");

        DrvStep2Num.Background = one ? Brush("#F0F0F0") : Brush("#3458e1");
        DrvStep2Num.Foreground = one ? Brush("#BBBBBB") : Brushes.White;
        DrvStep2Lbl.Foreground = one ? Brush("#BBBBBB") : Brush("#111111");
    }

    private void OnDrvDirectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DrvDwellPanel == null) return;
        RebuildForDirection();
    }

    private void RebuildForDirection()
    {
        BuildPlatforms();
        BuildDwellRows();
    }

    private void BuildPlatforms()
    {
        if (DrvPlatform == null) return;

        bool reverse = DrvDirection.SelectedIndex == 1; // Arad → Radna
        string origin = reverse ? "Arad" : "Radna";
        var list = reverse ? _platArad : _platRadna;

        DrvPlatform.Items.Clear();
        foreach (var p in list)
            DrvPlatform.Items.Add(new ComboBoxItem { Content = p });
        DrvPlatform.SelectedIndex = 0;

        DrvPlatformDesc.Text = $"Choose the platform you'll depart from at {origin}.";
    }

    private void BuildDwellRows()
    {
        if (DrvDwellPanel == null) return;

        bool reverse = DrvDirection.SelectedIndex == 1; // Arad → Radna
        _currentOrder = reverse ? _lineRA.Reverse().ToArray() : (string[])_lineRA.Clone();
        _currentSegs  = reverse ? _segRA.Reverse().ToArray()  : (int[])_segRA.Clone();

        DrvDwellPanel.Children.Clear();
        _dwellBoxes.Clear();

        for (int i = 0; i < _currentOrder.Length; i++)
        {
            bool isOrigin = i == 0;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var num = new TextBlock
            {
                Text = (i + 1) + ".",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("#3458e1"),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Grid.SetColumn(num, 0);
            row.Children.Add(num);

            var name = new TextBlock
            {
                Text = _currentOrder[i],
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#1a1a2e"),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            bool isTerminus = i == _currentOrder.Length - 1;
            if (isOrigin || isTerminus)
            {
                _dwellBoxes.Add(null); // origin/terminus — no stop stepper, show dashes
                var ph = BuildOriginPlaceholder();
                Grid.SetColumn(ph, 2);
                row.Children.Add(ph);
            }
            else
            {
                var stepper = BuildStepper(out var box, 2);
                _dwellBoxes.Add(box);
                Grid.SetColumn(stepper, 2);
                row.Children.Add(stepper);
            }

            DrvDwellPanel.Children.Add(row);
        }
    }

    // Rectangular minute stepper:  [ − | value | + ]  min   — no fill, thin borders.
    private FrameworkElement BuildStepper(out TextBox box, int defaultMinutes)
    {
        var outer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var shell = new Border
        {
            BorderBrush = Brush("#D5D9E2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Background = Brushes.White,
            SnapsToDevicePixels = true,
            VerticalAlignment = VerticalAlignment.Center
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var minus = MakeStepButton("−");
        Grid.SetColumn(minus, 0);
        var sep1 = new Border { Width = 1, Background = Brush("#E6E9F1") };
        Grid.SetColumn(sep1, 1);

        var value = new TextBox
        {
            Text = defaultMinutes.ToString(),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brush("#111111")
        };
        value.PreviewTextInput += OnDigitsOnly;
        Grid.SetColumn(value, 2);

        var sep2 = new Border { Width = 1, Background = Brush("#E6E9F1") };
        Grid.SetColumn(sep2, 3);
        var plus = MakeStepButton("+");
        Grid.SetColumn(plus, 4);

        minus.Click += (_, _) => value.Text = Math.Max(0, ParseInt(value.Text) - 1).ToString();
        plus.Click  += (_, _) => value.Text = Math.Min(59, ParseInt(value.Text) + 1).ToString();

        g.Children.Add(minus);
        g.Children.Add(sep1);
        g.Children.Add(value);
        g.Children.Add(sep2);
        g.Children.Add(plus);
        shell.Child = g;

        var unit = new TextBlock
        {
            Text = "min",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#999999"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Segoe UI")
        };

        outer.Children.Add(shell);
        outer.Children.Add(unit);
        box = value;
        return outer;
    }

    // Same footprint as the stepper, showing a centered "– | –" (origin / no stop).
    private FrameworkElement BuildOriginPlaceholder()
    {
        var outer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        var shell = new Border
        {
            Width = 106,
            Height = 30,
            BorderBrush = Brush("#D5D9E2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Background = Brushes.White,
            SnapsToDevicePixels = true
        };
        shell.Child = new TextBlock
        {
            Text = "–  |  –",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#C2C7D2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI")
        };

        var unit = new TextBlock
        {
            Text = "min",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#CCCCCC"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontFamily = new FontFamily("Segoe UI")
        };

        outer.Children.Add(shell);
        outer.Children.Add(unit);
        return outer;
    }

    // Flat rectangular +/- cell.
    private Button MakeStepButton(string glyph)
    {
        var b = new Button { Width = 30, Height = 30, Content = glyph, Cursor = Cursors.Hand,
                             FontSize = 15, FontWeight = FontWeights.Bold };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "bd";
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#EEF2FF"), "bd"));
        tpl.Triggers.Add(hover);

        b.Template = tpl;
        b.Foreground = Brush("#3458e1");
        return b;
    }

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;

    // ──────────────────────────────────────────────
    //  CONSIST (step 2)
    // ──────────────────────────────────────────────

    private int _wagonCount = 3;

    private static readonly BitmapImage _locoImg =
        new(new Uri("pack://application:,,,/Assets/Loco.png"));
    private static readonly BitmapImage _wagonImg =
        new(new Uri("pack://application:,,,/Assets/Vagon.png"));

    private void InitConsist()
    {
        if (DrvLocoCombo.SelectedIndex < 0) DrvLocoCombo.SelectedIndex = 0;
        if (DrvWagonType.SelectedIndex < 0) DrvWagonType.SelectedIndex = 0;
        DrvWagonCount.Text = _wagonCount.ToString();
        BuildConsistStrip();
    }

    private void OnConsistChanged(object sender, SelectionChangedEventArgs e) => BuildConsistStrip();

    private void OnWagonMinus(object sender, RoutedEventArgs e)
    {
        _wagonCount = Math.Max(1, _wagonCount - 1);
        DrvWagonCount.Text = _wagonCount.ToString();
        BuildConsistStrip();
    }

    private void OnWagonPlus(object sender, RoutedEventArgs e)
    {
        _wagonCount = Math.Min(6, _wagonCount + 1);
        DrvWagonCount.Text = _wagonCount.ToString();
        BuildConsistStrip();
    }

    private void BuildConsistStrip()
    {
        if (DrvConsistStrip == null) return;

        DrvConsistStrip.Children.Clear();
        DrvConsistStrip.Children.Add(MakeVehicle(_locoImg));
        for (int i = 0; i < _wagonCount; i++)
            DrvConsistStrip.Children.Add(MakeVehicle(_wagonImg));

        var loco = (DrvLocoCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—";
        var wagon = (DrvWagonType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "—";
        DrvConsistSummary.Text = $"{loco}   +   {_wagonCount} × {wagon}";
    }

    private static Image MakeVehicle(BitmapImage src) => new()
    {
        Source = src,
        Height = 66,
        Stretch = Stretch.Uniform,
        Margin = new Thickness(0, 0, -10, 0),
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true
    };

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));
}
