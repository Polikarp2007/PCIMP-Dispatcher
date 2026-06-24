using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCMPDispatcher;

public enum ChatMsgType
{
    Player,      // green  — train/player message
    Dispatcher,  // blue   — dispatcher reply
    SysSwitch,   // gray   — switch action
    SysConsist,  // orange — consist applied
    Server       // red    — system alert
}

public partial class MainWindow
{
    private StackPanel?   _chatLog;
    private ScrollViewer? _chatScroll2;
    private TextBox?      _chatInputBox;
    private string        _currentStation = "Paulis";

    // ── Public API ───────────────────────────────────────────────────

    public void PostChatDispatcher(string text)
        => AppendChat(ChatMsgType.Dispatcher, $"[DSP {_currentStation}]", text);

    public void PostChatSwitch(string switchId, string direction)
        => AppendChat(ChatMsgType.SysSwitch, $"[DSP {_currentStation}]",
                      $"{switchId} pre-switched → {direction}");

    public void PostChatConsist(string trainNo, string route)
        => AppendChat(ChatMsgType.SysConsist, $"[DSP {_currentStation}]",
                      $"Applied consist for {trainNo}  {route}");

    public void PostChatPlayer(string trainId, string playerInitials, string text)
        => AppendChat(ChatMsgType.Player, $"[{trainId}] by {playerInitials}", text);

    // ── Build ────────────────────────────────────────────────────────

    private void BuildChatPanel(string station)
    {
        _currentStation = station;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── Header ─────────────────────────────────────────────────
        var header = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#E8E8E8"),
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding         = new Thickness(16, 10, 16, 10)
        };

        var hRow = new StackPanel { Orientation = Orientation.Horizontal };
        var liveDot = new Ellipse
        {
            Width = 9, Height = 9, Fill = Brush("#28A745"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        hRow.Children.Add(liveDot);
        hRow.Children.Add(new TextBlock
        {
            Text = "PC|MP", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        hRow.Children.Add(new TextBlock
        {
            Text = " Chat", FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Child = hRow;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // ── Log area ───────────────────────────────────────────────
        _chatLog = new StackPanel();
        _chatScroll2 = new ScrollViewer
        {
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White,
            Content    = _chatLog
        };
        Grid.SetRow(_chatScroll2, 1);
        root.Children.Add(_chatScroll2);

        // Demo seed
        PostChatPlayer("R 9087", "P.K.", "Hello, good morning dispatcher.");
        PostChatDispatcher("Hello. Welcome to Paulis zone.");

        // ── Input row ──────────────────────────────────────────────
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

        _chatInputBox = new TextBox
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
        _chatInputBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendDispatcherChat(); };
        _ = new RomanianT9(_chatInputBox); // attach T9 autocomplete
        Grid.SetColumn(_chatInputBox, 0);

        var sendBorder = new Border
        {
            Background      = Brush("#3458e1"),
            Padding         = new Thickness(16, 7, 16, 7),
            Cursor          = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        sendBorder.Child = new TextBlock
        {
            Text = "SEND", FontSize = 13, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        sendBorder.MouseEnter += (_, _) => sendBorder.Background = Brush("#2040c0");
        sendBorder.MouseLeave += (_, _) => sendBorder.Background = Brush("#3458e1");
        sendBorder.MouseLeftButtonUp += (_, _) => SendDispatcherChat();
        Grid.SetColumn(sendBorder, 1);

        inputRow.Children.Add(_chatInputBox);
        inputRow.Children.Add(sendBorder);
        inputBorder.Child = inputRow;
        Grid.SetRow(inputBorder, 2);
        root.Children.Add(inputBorder);

        ChatContainer.Child = root;
        ChatContainer.Background = Brushes.White;
    }

    private void SendDispatcherChat()
    {
        if (_chatInputBox == null) return;
        var msg = _chatInputBox.Text.Trim();
        if (string.IsNullOrEmpty(msg)) return;
        PostChatDispatcher(msg);
        _chatInputBox.Text = string.Empty;
        _chatInputBox.Focus();
    }

    // ── Append ───────────────────────────────────────────────────────
    private void AppendChat(ChatMsgType type, string tag, string text)
    {
        if (_chatLog == null) return;

        var (tagColor, textColor, bgColor, borderColor) = type switch
        {
            ChatMsgType.Player     => ("#1a7a35", "#1a1a2e", "#f5fff8", "#d0f0d8"),
            ChatMsgType.Dispatcher => ("#1a3a9f", "#1a1a2e", "#f5f7ff", "#c8d4f8"),
            ChatMsgType.SysSwitch  => ("#666677", "#444455", "#fafafa", "#e8e8ee"),
            ChatMsgType.SysConsist => ("#b35000", "#1a1a2e", "#fff8f0", "#ffd8a8"),
            ChatMsgType.Server     => ("#8f1a1a", "#1a1a2e", "#fff5f5", "#f8c8c8"),
            _                      => ("#555555", "#333333", "#ffffff", "#eeeeee")
        };

        var row = new Border
        {
            Background      = Brush(bgColor),
            BorderBrush     = Brush(borderColor),
            BorderThickness = new Thickness(3, 0, 0, 0), // left accent stripe
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = new Thickness(0, 0, 0, 1)
        };

        var sp = new WrapPanel { Orientation = Orientation.Horizontal };

        // Timestamp
        sp.Children.Add(new TextBlock
        {
            Text      = DateTime.Now.ToString("HH:mm") + "  ",
            FontSize  = 11, FontFamily = new FontFamily("Consolas"),
            Foreground = Brush("#AAAAAA"),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Tag (bold, colored)
        sp.Children.Add(new TextBlock
        {
            Text       = tag + "  ",
            FontSize   = 13, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI"),
            Foreground = Brush(tagColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        // Message
        sp.Children.Add(new TextBlock
        {
            Text         = text,
            FontSize     = 13, FontFamily = new FontFamily("Segoe UI"),
            Foreground   = Brush(textColor),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });

        row.Child = sp;
        _chatLog.Children.Add(row);
        _chatScroll2?.ScrollToEnd();
    }
}
