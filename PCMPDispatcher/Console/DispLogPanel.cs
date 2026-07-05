using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCMPDispatcher;

public enum DispLogType
{
    Approach,    // 🟡 train approaching zone
    Departed,    // 🟢 train departed
    Stopped,     // 🟠 train stopped at station
    SwitchPass,  // 🔵 train passed a switch
    DispMsg,     // ⚪ /disp message sent/received
    PlayerMsg,   // ⚪ /player message
    Alert,       // 🔴 system alert
    Info         // gray — general info
}

public partial class ConsoleView : UserControl
{
    private StackPanel?   _dispLog;
    private ScrollViewer? _dispLogScroll;
    private TextBox?      _dispCmdBox;

    // ── Public API ────────────────────────────────────────────────────────────

    /// Train approaching dispatcher zone (< 1 km)
    public void LogTrainApproaching(string trainNo, string player, double distKm)
        => AppendDispLog(DispLogType.Approach,
            $"APPROACHING  {trainNo}",
            $"{player}  ·  {distKm:0.0} km from zone  ·  build route now");

    /// Train departed station
    public void LogTrainDeparted(string trainNo, string player, string from)
        => AppendDispLog(DispLogType.Departed,
            $"DEPARTED  {trainNo}",
            $"{player}  departed {from}");

    /// Train stopped / arrived at station
    public void LogTrainStopped(string trainNo, string player, string where)
        => AppendDispLog(DispLogType.Stopped,
            $"ARRIVED  {trainNo}",
            $"{player}  stopped at {where}");

    /// Train passed a switch in dispatcher zone
    public void LogSwitchPassed(string trainNo, string switchId)
        => AppendDispLog(DispLogType.SwitchPass,
            $"SWITCH  {switchId}",
            $"{trainNo}  cleared switch {switchId}");

    /// System info line
    public void LogInfo(string text)
        => AppendDispLog(DispLogType.Info, "INFO", text);

    /// Alert line
    public void LogAlert(string text)
        => AppendDispLog(DispLogType.Alert, "ALERT", text);

    // ── Build ─────────────────────────────────────────────────────────────────
    private void BuildDispLogPanel(string station)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // log
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // cmd input

        // ── Header ──────────────────────────────────────────────────────────
        var header = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#E8E8E8"),
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding         = new Thickness(16, 10, 16, 10)
        };

        var hRow = new StackPanel { Orientation = Orientation.Horizontal };

        // Pulsing blue dot — matches brand
        var pulseDot = new Ellipse
        {
            Width = 9, Height = 9, Fill = Brush("#3458e1"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AnimatePulse(pulseDot, "#3458e1");

        hRow.Children.Add(pulseDot);
        hRow.Children.Add(new TextBlock
        {
            Text = "PC|MP", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        hRow.Children.Add(new TextBlock
        {
            Text = " D-LOG", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Commands hint — right aligned
        var hint = new TextBlock
        {
            Text = "/disp  /player_…",
            FontSize = 10, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#CCCCCC"), FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var hGrid = new Grid();
        hGrid.Children.Add(hRow);
        hGrid.Children.Add(hint);
        header.Child = hGrid;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── Log area ─────────────────────────────────────────────────────────
        _dispLog = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        _dispLogScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White,
            Content    = _dispLog
        };
        Grid.SetRow(_dispLogScroll, 1);
        root.Children.Add(_dispLogScroll);

        // Seed with demo events
        LogInfo($"Dispatcher connected · zone {station}");
        LogTrainApproaching("IR 1833", "Stefan.M", 0.8);
        LogSwitchPassed("IC 527", "P11");
        LogTrainStopped("R 9087", "Poli.K", station);

        // ── Command input ────────────────────────────────────────────────────
        var inputBorder = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#E8E8E8"),
            BorderThickness = new Thickness(0, 1.5, 0, 0),
            Padding         = new Thickness(12, 8, 12, 8)
        };

        var inputRow = new Grid();
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _dispCmdBox = new TextBox
        {
            FontSize         = 13,
            FontFamily       = new FontFamily("Segoe UI"),
            Foreground       = Brush("#111111"),
            Background       = Brush("#F5F7FF"),
            BorderBrush      = Brush("#C8D0F0"),
            BorderThickness  = new Thickness(1.5),
            Padding          = new Thickness(10, 7, 10, 7),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin           = new Thickness(0, 0, 8, 0),
            CaretBrush       = Brush("#3458e1")
        };
        SetPlaceholder(_dispCmdBox, "/disp msg  ·  /player_Stefan msg  ·  or just type");

        _dispCmdBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendDispCmd(); };
        Grid.SetColumn(_dispCmdBox, 0);

        var sendBtn = new Border
        {
            Background        = Brush("#3458e1"),
            Padding           = new Thickness(16, 7, 16, 7),
            Cursor            = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        sendBtn.Child = new TextBlock
        {
            Text = "SEND", FontSize = 13, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        sendBtn.MouseEnter += (_, _) => sendBtn.Background = Brush("#2040c0");
        sendBtn.MouseLeave += (_, _) => sendBtn.Background = Brush("#3458e1");
        sendBtn.MouseLeftButtonUp += (_, _) => SendDispCmd();
        Grid.SetColumn(sendBtn, 1);

        inputRow.Children.Add(_dispCmdBox);
        inputRow.Children.Add(sendBtn);
        inputBorder.Child = inputRow;
        Grid.SetRow(inputBorder, 2);
        root.Children.Add(inputBorder);

        BottomRightPanel.Child = root;
        BottomRightPanel.Background = Brushes.White;
    }

    // ── Send command ─────────────────────────────────────────────────────────
    private void SendDispCmd()
    {
        if (_dispCmdBox == null) return;
        var raw = _dispCmdBox.Text.Trim();
        if (string.IsNullOrEmpty(raw) || raw == (string)_dispCmdBox.Tag) return;

        if (raw.StartsWith("/disp ", StringComparison.OrdinalIgnoreCase))
        {
            var msg = raw[6..].Trim();
            AppendDispLog(DispLogType.DispMsg, $"[D-DSP {_currentStation}]", msg);
            PostChatDispatcher($"[D-DSP {_currentStation}] {msg}");
        }
        else if (raw.StartsWith("/player_", StringComparison.OrdinalIgnoreCase))
        {
            var rest  = raw[8..];
            var space = rest.IndexOf(' ');
            var nick  = space > 0 ? rest[..space] : rest;
            var msg   = space > 0 ? rest[(space + 1)..].Trim() : "";
            AppendDispLog(DispLogType.PlayerMsg, $"[D-DSP {_currentStation}] → {nick}", msg);
        }
        else
        {
            // Plain message — post as dispatcher log entry
            AppendDispLog(DispLogType.DispMsg, $"[D-DSP {_currentStation}]", raw);
            PostChatDispatcher($"[D-DSP {_currentStation}] {raw}");
        }

        _dispCmdBox.Text = string.Empty;
        _dispCmdBox.Focus();
    }

    // ── Append entry — same style as ChatPanel.AppendChat ────────────────────
    private void AppendDispLog(DispLogType type, string tag, string text)
    {
        if (_dispLog == null) return;

        // tagColor, textColor, bgColor, stripeColor (left 3px border)
        var (tagColor, textColor, bgColor, stripeColor) = type switch
        {
            DispLogType.Approach   => ("#b35000", "#1a1a2e", "#fff8f0", "#FF8C00"), // 🟠 orange stripe
            DispLogType.Departed   => ("#1a7a35", "#1a1a2e", "#f5fff8", "#28A745"), // 🟢 green stripe
            DispLogType.Stopped    => ("#7a6800", "#1a1a2e", "#fffdf0", "#F5C518"), // 🟡 yellow stripe
            DispLogType.SwitchPass => ("#1a3a9f", "#1a1a2e", "#f5f7ff", "#3458e1"), // 🔵 blue stripe
            DispLogType.DispMsg    => ("#5a1a8f", "#1a1a2e", "#faf5ff", "#8844cc"), // 🟣 purple stripe
            DispLogType.PlayerMsg  => ("#0a6b70", "#1a1a2e", "#f0fbfc", "#0097A7"), // 🩵 teal stripe
            DispLogType.Alert      => ("#8f1a1a", "#1a1a2e", "#fff5f5", "#CC1122"), // 🔴 red stripe
            _                     => ("#555577", "#333344", "#fafafa", "#AAAACC"), // ⚪ gray-blue stripe
        };

        var row = new Border
        {
            Background      = Brush(bgColor),
            BorderBrush     = Brush(stripeColor),
            BorderThickness = new Thickness(3, 0, 0, 0),  // left accent stripe — same as chat
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = new Thickness(0, 0, 0, 1)
        };

        var sp = new WrapPanel { Orientation = Orientation.Horizontal };

        // Timestamp ��� Consolas 11px gray
        sp.Children.Add(new TextBlock
        {
            Text      = DateTime.Now.ToString("HH:mm") + "  ",
            FontSize  = 11, FontFamily = new FontFamily("Consolas"),
            Foreground = Brush("#AAAAAA"),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Tag — Segoe UI Black 13px colored
        sp.Children.Add(new TextBlock
        {
            Text       = tag + "  ",
            FontSize   = 13, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brush(tagColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Message — Segoe UI 13px
        sp.Children.Add(new TextBlock
        {
            Text         = text,
            FontSize     = 13, FontFamily = new FontFamily("Segoe UI"),
            Foreground   = Brush(textColor),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Child = sp;
        _dispLog.Children.Add(row);
        _dispLogScroll?.ScrollToEnd();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SetPlaceholder(TextBox tb, string placeholder)
    {
        tb.Tag = placeholder;
        tb.Text = placeholder;
        tb.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        tb.GotFocus  += (_, _) =>
        {
            if (tb.Text == (string)tb.Tag) { tb.Text = ""; tb.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)); }
        };
        tb.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(tb.Text)) { tb.Text = (string)tb.Tag; tb.Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); }
        };
    }

    private static void AnimatePulse(Ellipse el, string hexColor)
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0, To = 0.2,
            Duration = new Duration(TimeSpan.FromSeconds(1.1)),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        el.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}
