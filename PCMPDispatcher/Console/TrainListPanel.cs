using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PCMPDispatcher;

// ── Train data model ─────────────────────────────────────────────────

public record TrainStop(string Station, string Arrival, string Departure);

public record TrainInfo(
    string Id,          // "R 9087"
    string Type,        // "Regional" / "InterRegio" / "Marfă"
    string DriverFull,  // "Polikarp Kravcenko"
    string DriverShort, // "P.K."
    string DistanceKm,  // "12 km"
    string StatusLabel, // "En route" / "Stopped" / "Delayed"
    string StatusColor, // hex
    List<TrainStop> Schedule
);

public partial class ConsoleView : UserControl
{
    // ── Sample zone data (will come from server in future) ────────────
    private static readonly List<TrainInfo> ZoneTrains =
    [
        new("R 9087", "Regio", "Polikarp Kravcenko", "P.K.",
            "12 km", "En route", "#28A745",
        [
            new("Arad",    "—",     "07:14"),
            new("Ghioroc", "07:28", "07:30"),
            new("Paulis",  "07:41", "07:43"),
            new("Radna",   "07:55", "07:57"),
            new("Lipova",  "08:11", "—"),
        ]),

        new("R 1744", "Regio", "Alexandru Moldovan", "A.M.",
            "28 km", "En route", "#28A745",
        [
            new("Arad",    "—",     "07:55"),
            new("Paulis",  "08:19", "08:20"),
            new("Lipova",  "08:34", "08:36"),
            new("Deva",    "09:15", "—"),
        ]),

        new("R 3012", "Regio", "Vasile Ionescu", "V.I.",
            "3 km",  "Stopped", "#F5A623",
        [
            new("Radna",   "—",     "08:00"),
            new("Paulis",  "08:09", "08:12"),
            new("Ghioroc", "08:24", "08:25"),
            new("Arad",    "08:38", "—"),
        ]),

        new("R 0892", "Regio", "George Todea", "G.T.",
            "47 km", "En route", "#28A745",
        [
            new("Deva",    "—",     "05:30"),
            new("Lipova",  "06:10", "06:25"),
            new("Paulis",  "06:44", "06:50"),
            new("Arad",    "07:08", "—"),
        ]),

        new("R 2201", "Regio", "Mihai Stanescu", "M.S.",
            "61 km", "Delayed", "#CC0000",
        [
            new("Deva",    "—",     "07:20"),
            new("Lipova",  "08:01", "08:03"),
            new("Paulis",  "08:22", "08:23"),
            new("Arad",    "08:40", "—"),
        ]),

        new("R 4456", "Regio", "Cosmin Bîrlea", "C.B.",
            "19 km", "En route", "#28A745",
        [
            new("Arad",    "—",     "09:05"),
            new("Ghioroc", "09:19", "09:21"),
            new("Paulis",  "09:32", "09:34"),
            new("Radna",   "09:46", "—"),
        ]),
    ];

    // ── Build ────────────────────────────────────────────────────────

    private void BuildTrainsPanel(string station)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header
        var hdr = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#E8E8E8"),
            BorderThickness = new Thickness(0, 0, 0, 1.5),
            Padding         = new Thickness(14, 10, 14, 10)
        };
        var hStack = new StackPanel { Orientation = Orientation.Horizontal };
        hStack.Children.Add(new Ellipse
        {
            Width = 9, Height = 9, Fill = Brush("#3458e1"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        hStack.Children.Add(new TextBlock
        {
            Text = $"ZONE {station.ToUpper()}",
            FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        hStack.Children.Add(new TextBlock
        {
            Text = "  TRAINS",
            FontSize = 15, FontWeight = FontWeights.Black,
            Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        hdr.Child = hStack;
        Grid.SetRow(hdr, 0);
        root.Children.Add(hdr);

        // Train list
        var list = new StackPanel { Margin = new Thickness(0) };
        foreach (var train in ZoneTrains)
            list.Children.Add(MakeTrainRow(train, station));

        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.White
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        TrainsContainer.Child = root;
    }

    private UIElement MakeTrainRow(TrainInfo t, string zone)
    {
        var row = new Border
        {
            Padding         = new Thickness(12, 9, 12, 9),
            Background      = Brushes.White,
            BorderBrush     = Brush("#F0F0F0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor          = Cursors.Hand
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: id + driver
        var left = new StackPanel();
        var idTb = new TextBlock
        {
            Text = $"[{t.Id}]",
            FontSize = 13, FontWeight = FontWeights.Black,
            Foreground = Brush("#1a1a2e"), FontFamily = new FontFamily("Consolas")
        };
        var driverTb = new TextBlock
        {
            Text = $"by {t.DriverShort}",
            FontSize = 11, Foreground = Brush("#888888"),
            FontFamily = new FontFamily("Segoe UI"), Margin = new Thickness(0, 1, 0, 0)
        };
        left.Children.Add(idTb);
        left.Children.Add(driverTb);
        Grid.SetColumn(left, 0);

        // Right: status dot + distance
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(new Ellipse
        {
            Width = 8, Height = 8, Fill = Brush(t.StatusColor),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 3)
        });
        right.Children.Add(new TextBlock
        {
            Text = t.DistanceKm, FontSize = 10,
            Foreground = Brush("#AAAAAA"), FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(right, 1);

        grid.Children.Add(left);
        grid.Children.Add(right);
        row.Child = grid;

        // Hover highlight
        row.MouseEnter += (_, _) => row.Background = Brush("#F5F7FF");
        row.MouseLeave += (_, _) => row.Background = Brushes.White;

        // ToolTip — WPF manages show/hide automatically, no popup bugs
        var tt = new ToolTip
        {
            Content          = BuildInfoCard(t, zone),
            Background       = Brushes.Transparent,
            BorderThickness  = new Thickness(0),
            HasDropShadow    = false,
            Placement        = System.Windows.Controls.Primitives.PlacementMode.Right,
            HorizontalOffset = 8,
            Padding          = new Thickness(0)
        };
        ToolTipService.SetInitialShowDelay(row, 200);
        ToolTipService.SetShowDuration(row, 60000);
        ToolTipService.SetBetweenShowDelay(row, 0);
        row.ToolTip = tt;

        return row;
    }

    // ── Info card (used by ToolTip) ──────────────────────────────────

    private static UIElement BuildInfoCard(TrainInfo t, string zone)
    {
        var card = new Border
        {
            Background      = Brushes.White,
            BorderBrush     = Brush("#3458e1"),
            BorderThickness = new Thickness(1.5),
            CornerRadius    = new CornerRadius(0),
            Padding         = new Thickness(16, 14, 16, 14),
            MinWidth        = 300,
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 20,
                ShadowDepth = 4, Opacity = 0.18, Direction = 270
            }
        };

        var panel = new StackPanel { MinWidth = 280 };

        // ── Train header ──
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,4) };
        titleRow.Children.Add(new TextBlock
        {
            Text = $"[{t.Id}]", FontSize = 16, FontWeight = FontWeights.Black,
            Foreground = Brush("#1a1a2e"), FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0)
        });
        var typeBadge = new Border
        {
            Background = Brush("#3458e1"), Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        typeBadge.Child = new TextBlock
        {
            Text = t.Type, FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI")
        };
        titleRow.Children.Add(typeBadge);
        panel.Children.Add(titleRow);

        // Status
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,12) };
        statusRow.Children.Add(new Ellipse
        {
            Width = 8, Height = 8, Fill = Brush(t.StatusColor),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0)
        });
        statusRow.Children.Add(new TextBlock
        {
            Text = t.StatusLabel, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = Brush(t.StatusColor), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,14,0)
        });
        statusRow.Children.Add(new TextBlock
        {
            Text = $"Distance to {zone}: {t.DistanceKm}",
            FontSize = 11, Foreground = Brush("#888888"),
            FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(statusRow);

        // Divider
        panel.Children.Add(new Border { Height = 1, Background = Brush("#EEEEEE"), Margin = new Thickness(0,0,0,10) });

        // Driver
        var driverRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,10) };
        driverRow.Children.Add(new TextBlock
        {
            Text = "Driver: ", FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = Brush("#555555"), FontFamily = new FontFamily("Segoe UI")
        });
        driverRow.Children.Add(new TextBlock
        {
            Text = t.DriverFull, FontSize = 11,
            Foreground = Brush("#1a1a2e"), FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(driverRow);

        // Schedule header
        panel.Children.Add(new TextBlock
        {
            Text = "SCHEDULE", FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brush("#AAAAAA"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 6)
        });

        // Schedule rows
        foreach (var stop in t.Schedule)
        {
            bool isHere = stop.Station.Equals(zone, StringComparison.OrdinalIgnoreCase);
            var stopRow = new Border
            {
                Background      = isHere ? Brush("#EEF2FF") : Brushes.Transparent,
                BorderBrush     = isHere ? Brush("#3458e1") : Brushes.Transparent,
                BorderThickness = new Thickness(2.5, 0, 0, 0),
                Padding         = new Thickness(8, 3, 0, 3),
                Margin          = new Thickness(0, 0, 0, 2)
            };

            var sg = new Grid();
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            sg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            sg.Children.Add(MakeSchedTb(stop.Station,
                isHere ? FontWeights.Black : FontWeights.Normal,
                isHere ? "#3458e1" : "#333333", 0));
            sg.Children.Add(MakeSchedTb(stop.Arrival,   FontWeights.Normal, "#555555", 1));
            sg.Children.Add(MakeSchedTb(stop.Departure, FontWeights.Normal, "#555555", 2));

            stopRow.Child = sg;
            panel.Children.Add(stopRow);
        }

        // Legend
        var legRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,8,0,0) };
        legRow.Children.Add(MakeLegTb("ARR", "#888888"));
        legRow.Children.Add(MakeLegTb("  /  DEP", "#888888"));
        panel.Children.Add(legRow);

        card.Child = panel;
        return card;
    }

    private static TextBlock MakeSchedTb(string text, FontWeight w, string hex, int col)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 11, FontWeight = w,
            Foreground = Brush(hex), FontFamily = new FontFamily("Consolas")
        };
        Grid.SetColumn(tb, col);
        return tb;
    }

    private static TextBlock MakeLegTb(string t, string hex)
        => new() { Text = t, FontSize = 9, Foreground = Brush(hex), FontFamily = new FontFamily("Segoe UI") };

}
