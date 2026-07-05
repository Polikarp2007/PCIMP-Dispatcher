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
    private readonly Dictionary<string, bool>      _paulisSwitches      = new();
    private readonly Dictionary<string, TextBlock> _switchStatusTbs     = new();
    private readonly HashSet<string>               _uiInitiatedSwitches = new(); // prevents double chat post

    private static readonly Dictionary<string, (double sx, double dx)> SwData = new()
    {
        {"P17",(160,146)},{"P16",(120,134)},{"P15",(300,286)},{"P14",(260,274)},
        {"P13",(430,421)},{"P12",(430,421)},{"P11",(500,486)},{"P10",(460,474)},
        {"P9", (855,843)},{"P8", (820,832)},{"P7", (945,936)},{"P5", (920,929)},
        {"P6", (920,929)},{"P4",(1030,1018)},{"P3",(995,1007)},{"P2",(1125,1113)},
        {"P1",(1090,1102)}
    };
    private static readonly HashSet<string> SwInverted =
        ["P16","P15","P12","P11","P8","P6","P7","P4","P1"];

    private static readonly Dictionary<string, (double sx, double dx)> GhiorocSwData = new()
    {
        {"GH12",(160,146)},{"GH11",(120,134)},{"GH10",(300,286)},{"GH9",(260,274)},
        {"GH7",(430,421)},{"GH8",(430,421)},{"GH5",(920,929)},
        {"GH6",(920,929)},{"GH4",(1030,1018)},{"GH3",(995,1007)},{"GH2",(1125,1113)},
        {"GH1",(1090,1102)}
    };
    private static readonly HashSet<string> GhiorocSwInverted =
        ["GH11","GH10","GH8","GH6","GH4","GH1"];

    // Arad — switch x-coords derived from arad_panel.html SWP geometry (L=35)
    // SCH I  (A12–A31): local coords; SCH II (A1–A11): local coords inside second SVG
    private static readonly Dictionary<string, (double sx, double dx)> AradSwData = new()
    {
        // SCH I (горловина приёма, левая часть схемы)
        {"A31",(479,  485.4)}, {"A30",(543,  548.3)}, {"A29",(659,  664.3)},
        {"A28",(799,  793.4)}, {"A27",(791,  796.6)}, {"A26",(713,  718.1)},
        {"A25",(717,  711.9)}, {"A24",(729,  734.1)}, {"A23",(917,  911.2)},
        {"A22",(908,  913.8)}, {"A21",(1011, 1016.5)},{"A20",(1018, 1012.6)},
        {"A19",(1018, 1012.6)},{"A18",(1011, 1016.5)},{"A17",(1125, 1119.8)},
        {"A16",(1120, 1125.2)},{"A15",(1148, 1142.4)},{"A14",(1292, 1297.3)},
        {"A13",(1140, 1145.6)},{"A12",(1297, 1291.1)},
        // SCH II (горловина отправления, правая часть схемы)
        {"A7", (722,  716.9)}, {"A6", (718,  723.1)}, {"A4", (870,  864.9)},
        {"A11",(473,  466.9)}, {"A5", (718,  723.1)}, {"A3", (866,  871.1)},
        {"A2", (983,  979.9)}, {"A1", (1056, 1052.9)},
        {"A9", (468,  473.2)}, {"A8", (722,  716.9)}, {"A10",(473,  467.8)},
    };
    private static readonly HashSet<string> AradSwInverted =
        ["A4","A5","A6","A7","A9","A12","A13","A14","A15","A16","A18","A20","A21","A22","A24","A26","A27","A28","A29","A30","A31"];

    // Radna — switch x-coords from radna_panel.html JS geometry
    private static readonly Dictionary<string, (double sx, double dx)> RadnaSwData = new()
    {
        {"R11",(185,175.9)},{"R10",(159,168.1)},{"R9",(325,334.1)},{"R8",(465,455.9)},
        {"R7",(439,448.1)},{"R6",(595,585.9)},{"R5",(569,578.1)},{"R4",(735,725.9)},
        {"R3",(735,725.9)},{"R2",(709,718.1)},{"R1",(955,945.9)},
    };
    private static readonly HashSet<string> RadnaSwInverted =
        ["R1","R3","R5","R7","R9","R11"];

    // Glogovat — switch x-coords from glogovat_panel.html JS (SW_ARAD + SW_GHIO)
    private static readonly Dictionary<string, (double sx, double dx)> GlogatSwData = new()
    {
        // SCH II — ARAD side
        {"G26",(266,261.9)},{"G25",(271,275.1)},{"G24",(399,391.9)},{"G23",(466,458.5)},
        {"G22",(447,454.5)},{"G21",(572,564.5)},{"G20",(553,560.5)},{"G17",(731,726.1)},
        {"G18",(732,727.9)},{"G19",(737,741.1)},{"G16",(896,889.3)},{"G15",(961,955.4)},
        {"G13",(953,958.6)},{"G12",(1015,1025.3)},{"G11",(1139,1131.3)},{"G10",(1119,1126.7)},
        {"G14",(975,983.3)},
        // SCH I — GHIOROC side
        {"G8",(646,655)},{"G7",(757,766)},{"G6",(757,766)},{"G9",(535,544)},
        {"G5",(894,885)},{"G4",(868,877)},{"G2",(1069,1055)},{"G1",(1029,1043)},
        {"G3",(1003.5,993.7)}
    };
    private static readonly HashSet<string> GlogatSwInverted =
        ["G1","G3","G5","G6","G9","G10","G12","G14","G15","G18","G20","G22","G24","G26"];

    private TextBlock? _infoNameTb, _infoDirTb;

    private string GetSwitchDir(string id)
    {
        bool isArad     = _currentStation.Equals("Arad",     StringComparison.OrdinalIgnoreCase);
        bool isGhioroc  = _currentStation.Equals("Ghioroc",  StringComparison.OrdinalIgnoreCase);
        bool isGlogovat = _currentStation.Equals("Glogovat", StringComparison.OrdinalIgnoreCase);
        bool isRadna    = _currentStation.Equals("Radna",    StringComparison.OrdinalIgnoreCase);
        var data     = isArad ? AradSwData : isGlogovat ? GlogatSwData : isGhioroc ? GhiorocSwData : isRadna ? RadnaSwData : SwData;
        var inverted = isArad ? AradSwInverted : isGlogovat ? GlogatSwInverted : isGhioroc ? GhiorocSwInverted : isRadna ? RadnaSwInverted : SwInverted;
        if (!data.TryGetValue(id, out var d)) return "–";
        bool rev = _paulisSwitches.TryGetValue(id, out bool v) && v;
        bool right = d.sx > d.dx ? !rev : rev;
        if (inverted.Contains(id)) right = !right;
        return right ? "RIGHT" : "LEFT";
    }

    private void BuildSwitchesWidget()
    {
        bool isArad     = _currentStation.Equals("Arad",     StringComparison.OrdinalIgnoreCase);
        bool isGhioroc  = _currentStation.Equals("Ghioroc",  StringComparison.OrdinalIgnoreCase);
        bool isGlogovat = _currentStation.Equals("Glogovat", StringComparison.OrdinalIgnoreCase);
        bool isRadna    = _currentStation.Equals("Radna",    StringComparison.OrdinalIgnoreCase);
        string[][] rows = isArad
            ? [
                ["A12", "A13", "A14", "A15", "A16"],
                ["A17", "A18", "A19", "A20", "A21"],
                ["A22", "A23", "A24", "A25", "A26"],
                ["A27", "A28", "A29", "A30", "A31"],
                ["A1",  "A2",  "A3",  "A4",  "A5",  "A6"],
                ["A7",  "A8",  "A9",  "A10", "A11"],
              ]
            : isGlogovat
            ? [
                ["G26", "G25", "G24", "G23", "G22", "G21"],
                ["G20", "G19", "G18", "G17", "G16", "G15"],
                ["G14", "G13", "G12", "G11", "G10"],
                ["G9",  "G8",  "G7",  "G6",  "G5",  "G4"],
                ["G3",  "G2",  "G1"]
              ]
            : isGhioroc
            ? [
                ["GH1",  "GH2",  "GH3",  "GH4",  "GH5",  "GH6"],
                ["GH7",  "GH8",  "GH9",  "GH10", "GH11", "GH12"]
              ]
            : isRadna
            ? [
                ["R11", "R10", "R9",  "R8",  "R7"],
                ["R6",  "R5",  "R4",  "R3",  "R2",  "R1"]
              ]
            : [
                ["P1",  "P2",  "P3",  "P4",  "P5",  "P6"],
                ["P7",  "P8",  "P9",  "P10", "P11"],
                ["P12", "P13", "P14", "P15", "P16", "P17"]
              ];

        foreach (var row in rows)
            foreach (var id in row)
                _paulisSwitches[id] = false;
        _switchStatusTbs.Clear();

        RightPanel.Background = Brushes.White;
        RightPanel.BorderBrush = Brush("#F0F0F0");

        var root = new Grid();

        var bgImg = new Image
        {
            Source              = TryLoadBitmap("/Assets/font P block R.png"),
            Stretch             = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment   = VerticalAlignment.Stretch,
            Opacity             = 0.22,
            IsHitTestVisible    = false
        };
        root.Children.Add(bgImg);

        var content = new StackPanel { Margin = new Thickness(16, 16, 16, 16) };

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin      = new Thickness(0, 0, 0, 12)
        };
        titleRow.Children.Add(new TextBlock
        {
            Text       = "SWITCHES",
            FontSize   = 11, FontWeight = FontWeights.Black,
            Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI")
        });
        titleRow.Children.Add(new TextBlock
        {
            Text       = " · " + _currentStation.ToUpper(),
            FontSize   = 11, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI")
        });
        content.Children.Add(titleRow);

        // ── twoCol: switch grid | simple info panel ───────────────────────
        var twoCol = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        twoCol.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: switch grid (no dashed border)
        var circlesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
        foreach (var row in rows)
        {
            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var id in row)
                rowPanel.Children.Add(BuildSwitchCircle(id));
            circlesPanel.Children.Add(rowPanel);
        }
        Grid.SetColumn(circlesPanel, 0);
        twoCol.Children.Add(circlesPanel);

        // Right: simple hover-info panel
        _infoNameTb = new TextBlock
        {
            Text = "—", FontSize = 24, FontWeight = FontWeights.Black,
            Foreground = Brush("#CC0000"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 4)
        };
        TextOptions.SetTextFormattingMode(_infoNameTb, TextFormattingMode.Ideal);

        _infoDirTb = new TextBlock
        {
            Text = "—", FontSize = 13, FontWeight = FontWeights.Black,
            Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI")
        };

        var infoSp = new StackPanel
        {
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4)
        };
        infoSp.Children.Add(new TextBlock
        {
            Text = "SWITCH", FontSize = 9, FontWeight = FontWeights.Black,
            Foreground = Brush("#AAAAAA"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        infoSp.Children.Add(_infoNameTb);
        infoSp.Children.Add(_infoDirTb);

        Grid.SetColumn(infoSp, 2);
        twoCol.Children.Add(infoSp);

        content.Children.Add(twoCol);

        // ── CONSIST stays with switches ────────────────────────────────────
        content.Children.Add(new Border { Height = 1, Background = Brush("#EEEEEE"), Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(BuildConsistSection());

        // ── SIGNALS at the bottom ──────────────────────────────────────────
        content.Children.Add(new Border { Height = 1, Background = Brush("#EEEEEE"), Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(BuildSignalsSection());

        root.Children.Add(content);
        RightPanel.Child = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private Border BuildSwitchCircle(string id)
    {
        var statusTb = new TextBlock
        {
            Text                = GetSwitchDir(id),
            FontSize            = 8,
            FontWeight          = FontWeights.Bold,
            Foreground          = Brush("#777777"),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily          = new FontFamily("Segoe UI"),
            Margin              = new Thickness(0, 1, 0, 0)
        };
        _switchStatusTbs[id] = statusTb;

        var numTb = new TextBlock
        {
            Text                = id,
            FontSize            = id.Length > 4 ? 10 : 13,
            FontWeight          = FontWeights.Black,
            Foreground          = Brush("#111111"),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontFamily          = new FontFamily("Segoe UI")
        };
        TextOptions.SetTextFormattingMode(numTb, TextFormattingMode.Ideal);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(numTb);
        textStack.Children.Add(statusTb);

        var swIcon = MakeSwitchIcon(14, "#BB0000");
        if (swIcon is FrameworkElement fe) { fe.VerticalAlignment = VerticalAlignment.Center; fe.Margin = new Thickness(0, 0, 2, 0); }

        var inner = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        inner.Children.Add(swIcon);
        inner.Children.Add(textStack);

        var btn = new Border
        {
            Width           = 54,
            Height          = 38,
            CornerRadius    = new CornerRadius(4),
            Background      = Brushes.White,
            BorderBrush     = Brush("#CC0000"),
            BorderThickness = new Thickness(2),
            Margin          = new Thickness(0, 0, 6, 6),
            Cursor          = Cursors.Hand,
            Child           = inner
        };

        _switchCircleBorders[id] = btn;

        btn.MouseEnter += (_, _) =>
        {
            if (!_lockedSwitches.Contains(id))
                btn.Background = new SolidColorBrush(Color.FromArgb(18, 204, 0, 0));
            ShowSwitchInfo(id);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = Brushes.White;
            ClearSwitchInfo();
        };

        btn.MouseLeftButtonUp += (_, _) =>
        {
            var rec = _consists.FirstOrDefault(c => c.Recording);
            if (rec != null) { AddToConsist(rec, id); return; }
            if (_lockedSwitches.Contains(id)) return;
            _paulisSwitches[id] = !_paulisSwitches[id];
            var newDir = GetSwitchDir(id);
            statusTb.Text = newDir;
            _uiInitiatedSwitches.Add(id);   // mark as UI-initiated → skip duplicate HTML poll post
            MapControl.SetSwitch(id, _paulisSwitches[id]);
            ShowSwitchInfo(id);
            PostChatSwitch(id, newDir);
        };

        return btn;
    }

    private readonly Dictionary<string, Ellipse> _signalDots = new();

    private static readonly string[] SigColors = ["#FF2020", "#FFCC00", "#00CC44"];

    // ── Signals section ────────────────────────────────────────────────────
    private FrameworkElement BuildSignalsSection()
    {
        _signalDots.Clear();
        var section = new StackPanel();

        // Header
        var hdr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        hdr.Children.Add(new TextBlock { Text = "SIGNALS", FontSize = 13, FontWeight = FontWeights.Black, Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI") });
        hdr.Children.Add(new TextBlock { Text = " · " + _currentStation.ToUpper(), FontSize = 13, FontWeight = FontWeights.Black, Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI") });
        section.Children.Add(hdr);

        // ── ENTRY — same colW/gapX grid as EXIT for perfect alignment ─────
        section.Children.Add(new TextBlock
        {
            Text = "ENTRY", FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brush("#999999"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        const double colW = 200, gapX = 44, entryGapX = 100;

        var entryGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Width = colW * 2 + entryGapX
        };
        entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
        entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(entryGapX) });
        entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });

        bool arad     = _currentStation.Equals("Arad",     StringComparison.OrdinalIgnoreCase);
        bool glogovat = _currentStation.Equals("Glogovat", StringComparison.OrdinalIgnoreCase);
        bool ghioroc  = _currentStation.Equals("Ghioroc",  StringComparison.OrdinalIgnoreCase);
        bool radna    = _currentStation.Equals("Radna",    StringComparison.OrdinalIgnoreCase);

        void AddEntry(string id, string lbl, int col, int row)
        {
            var it = col == 0 ? BuildSigItemLeft(id, lbl) : BuildSigItem(id, lbl);
            Grid.SetColumn(it, col); Grid.SetRow(it, row);
            entryGrid.Children.Add(it);
        }

        if (arad)
        {
            // Arad ENTRY: left = none (terminal), right = XG / XGF (from Glogovat)
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 0
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); // row 1
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 2
            var ediv = new Border { Width = 1.5, Background = Brush("#222222"), Opacity = 0.18,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(ediv, 1); Grid.SetRowSpan(ediv, 3);
            entryGrid.Children.Add(ediv);
            AddEntry("XG",  "XG",  2, 0);
            AddEntry("XGF", "XGF", 2, 2);
        }
        else if (glogovat)
        {
            // Glogovat ENTRY: left=YAF/YA/Y (3 items), right=X1/X2 (2 items)
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 0
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); // row 1
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 2
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); // row 3
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 4
            var ediv = new Border { Width = 1.5, Background = Brush("#222222"), Opacity = 0.18,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(ediv, 1); Grid.SetRowSpan(ediv, 5);
            entryGrid.Children.Add(ediv);
            AddEntry("YAF","YAF",0,0); AddEntry("YA","YA",0,2); AddEntry("Y","Y",0,4);
            AddEntry("X","X",2,0);     AddEntry("XF","XF",2,2);
        }
        else if (radna)
        {
            // Radna ENTRY: inline dashed-arrow rows (YF --→ YPF, Y --→ YP), no grid divider
            Canvas MakeDashArrow()
            {
                var ac = new Canvas { Width = 80, Height = 24 };
                var ln = new Line { X1=2, Y1=12, X2=66, Y2=12, Stroke=Brush("#BBBBBB"), StrokeThickness=1.5 };
                ln.StrokeDashArray = new DoubleCollection([4, 3]);
                var arr = new Polygon { Fill=Brush("#BBBBBB"),
                    Points=new PointCollection([new Point(66,7), new Point(76,12), new Point(66,17)]) };
                ac.Children.Add(ln); ac.Children.Add(arr);
                return ac;
            }
            var radnaEntry = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,0,0,10) };
            var er1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,4) };
            er1.Children.Add(BuildSigItemLeft("YF", "YF")); er1.Children.Add(MakeDashArrow()); er1.Children.Add(BuildSigItem("YPF","YPF"));
            var er2 = new StackPanel { Orientation = Orientation.Horizontal };
            er2.Children.Add(BuildSigItemLeft("Y", "Y")); er2.Children.Add(MakeDashArrow()); er2.Children.Add(BuildSigItem("YP","YP"));
            radnaEntry.Children.Add(er1); radnaEntry.Children.Add(er2);
            section.Children.Add(radnaEntry);
        }
        else
        {
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 0
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) }); // row 1
            entryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // row 2
            var ediv2 = new Border { Width = 1.5, Background = Brush("#222222"), Opacity = 0.18,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(ediv2, 1); Grid.SetRowSpan(ediv2, 3);
            entryGrid.Children.Add(ediv2);
            AddEntry("YF","YF",0,0); AddEntry("Y","Y",0,2);
            AddEntry("X","X",2,0);   AddEntry("XF","XF",2,2);
        }
        if (!radna) section.Children.Add(entryGrid);

        // ── EXIT label ────────────────────────────────────────────────────────
        section.Children.Add(new TextBlock
        {
            Text = "EXIT", FontSize = 10, FontWeight = FontWeights.Black,
            Foreground = Brush("#999999"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // Canvas proportional to map cy values (149-309)
        // colW=200 per column, gapX=44 declared above — center line at 200+22=222
        const double mapY0 = 146, scl = 1.08;
        double CY(double my) => (my - mapY0) * scl + 14;
        double totalW = colW * 2 + gapX;

        // left = mirrored (EMG→INIT→name→dot→icon), right = normal (icon→dot→name→INIT→EMG)
        void AddTrack(Canvas cav, string lId, string lLbl, string rId, string rLbl, double mapY)
        {
            double cy = CY(mapY);
            var l = BuildSigItemLeft(lId, lLbl);
            var r = BuildSigItem(rId, rLbl);
            Canvas.SetLeft(l, 0);           Canvas.SetTop(l, cy - 13);
            Canvas.SetLeft(r, colW + gapX); Canvas.SetTop(r, cy - 13);
            cav.Children.Add(l); cav.Children.Add(r);
        }

        double canvasH;
        Canvas cv;
        if (arad)
        {
            // Arad EXIT: 7 left (XP1–XP7, terminal/platform side) + 7 right (Y1–Y7, gorlovina side)
            const double rowH = 30;
            canvasH = rowH * 7 + 4;
            cv = new Canvas { Height = canvasH, Width = totalW };
            double lx = colW + gapX / 2.0;
            var div = new Border { Width = 1.5, Height = canvasH, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(div, lx - 0.75); Canvas.SetTop(div, 0);
            cv.Children.Add(div);
            string[] lIds = ["XP7",  "XP6",  "XP5",  "XPIV",  "XPIII", "XP2",  "XP1"];
            string[] rIds = ["Y7",   "Y6",   "Y5",   "YIV",   "YIII",  "Y2",   "Y1"];
            for (int i = 0; i < 7; i++)
            {
                double top = i * rowH;
                var l = BuildSigItemLeft(lIds[i], lIds[i]);
                var r = BuildSigItem(rIds[i], rIds[i]);
                Canvas.SetLeft(l, 0);           Canvas.SetTop(l, top);
                Canvas.SetLeft(r, colW + gapX); Canvas.SetTop(r, top);
                cv.Children.Add(l); cv.Children.Add(r);
            }
        }
        else if (glogovat)
        {
            // Glogovat EXIT: 6 left (SCH II/ARAD) + 6 right (SCH I/GHIOROC)
            // Left:  X6, X5, XIV, XIII, X2, X1
            // Right: Y6, Y5, YIV, YIII, Y2, Y1
            const double rowH = 30;
            canvasH = rowH * 6 + 4;
            cv = new Canvas { Height = canvasH, Width = totalW };
            double lx = colW + gapX / 2.0;
            var div = new Border { Width = 1.5, Height = canvasH, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(div, lx - 0.75); Canvas.SetTop(div, 0);
            cv.Children.Add(div);
            string[] lIds  = ["X6",  "X5",  "XIV", "XIII", "X2",  "X1"];
            string[] rIds  = ["Y6",  "Y5",  "YIV", "YIII", "Y2",  "Y1"];
            for (int i = 0; i < 6; i++)
            {
                double top = i * rowH;
                var l = BuildSigItemLeft(lIds[i], lIds[i]);
                var r = BuildSigItem(rIds[i], rIds[i]);
                Canvas.SetLeft(l, 0);           Canvas.SetTop(l, top);
                Canvas.SetLeft(r, colW + gapX); Canvas.SetTop(r, top);
                cv.Children.Add(l); cv.Children.Add(r);
            }
        }
        else if (ghioroc)
        {
            canvasH = CY(309) + 18;
            cv = new Canvas { Height = canvasH, Width = totalW };
            double lx = colW + gapX / 2.0;
            var div = new Border { Width = 1.5, Height = canvasH, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(div, lx - 0.75); Canvas.SetTop(div, 0);
            cv.Children.Add(div);
            AddTrack(cv, "X1",  "X1",  "Y1",   "Y1",   149);
            AddTrack(cv, "XII", "XII", "YII",  "YII",  189);
            AddTrack(cv, "XIII","XIII","YIII", "YIII", 229);
            AddTrack(cv, "X4",  "X4",  "Y4",   "Y4",   269);
        }
        else if (radna)
        {
            // Radna EXIT: left col = XA, XAF, [sep], X1, XII, XIII, X4, X5
            //             right col = YP1–YP5 aligned with X1–X5
            const double rowH = 28, sepGap = 14;
            canvasH = rowH * 7 + sepGap + 4;
            cv = new Canvas { Height = canvasH, Width = totalW };
            double lxRn = colW + gapX / 2.0;
            var divRn = new Border { Width = 1.5, Height = canvasH, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(divRn, lxRn - 0.75); Canvas.SetTop(divRn, 0);
            cv.Children.Add(divRn);
            // Separator at midpoint of the gap between XAF and X1
            double sepY = 2 * rowH + sepGap / 2.0;
            var sepL = new Border { Width = lxRn, Height = 1.5, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(sepL, 0); Canvas.SetTop(sepL, sepY - 0.75);
            cv.Children.Add(sepL);
            // XA, XAF — above separator
            string[] topIds = ["XA", "XAF"];
            for (int i = 0; i < topIds.Length; i++)
            {
                var it = BuildSigItemLeft(topIds[i], topIds[i]);
                Canvas.SetLeft(it, 0); Canvas.SetTop(it, i * rowH);
                cv.Children.Add(it);
            }
            // X1–X5 — below separator (offset by 2*rowH + sepGap)
            string[] lIds5 = ["X1", "XII", "XIII", "X4", "X5"];
            string[] rIds5 = ["YP1", "YPII", "YPIII", "YP4", "YP5"];
            for (int i = 0; i < lIds5.Length; i++)
            {
                double y = 2 * rowH + sepGap + i * rowH;
                var lIt = BuildSigItemLeft(lIds5[i], lIds5[i]);
                var rIt = BuildSigItem(rIds5[i], rIds5[i]);
                Canvas.SetLeft(lIt, 0);           Canvas.SetTop(lIt, y);
                Canvas.SetLeft(rIt, colW + gapX); Canvas.SetTop(rIt, y);
                cv.Children.Add(lIt); cv.Children.Add(rIt);
            }
        }
        else
        {
            canvasH = CY(309) + 18;
            cv = new Canvas { Height = canvasH, Width = totalW };
            double lx = colW + gapX / 2.0;
            var div = new Border { Width = 1.5, Height = canvasH, Background = Brush("#222222"), Opacity = 0.18 };
            Canvas.SetLeft(div, lx - 0.75); Canvas.SetTop(div, 0);
            cv.Children.Add(div);
            AddTrack(cv, "X1",  "X1",  "RX1",   "RX1",   149);
            AddTrack(cv, "XII", "XII", "RXII",  "RXII",  189);
            AddTrack(cv, "XIII","XIII","RXIII", "RXIII", 229);
            AddTrack(cv, "X4",  "X4",  "RX4",   "RX4",   269);
            AddTrack(cv, "X5",  "X5",  "RX5",   "RX5",   309);
        }

        // Center canvas in section
        var cvWrapper = new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = cv };
        section.Children.Add(cvWrapper);
        return section;
    }

    // ── Signal item — right column: icon → dot → name → INIT → EMG ──────────
    private Border BuildSigItem(string id, string label)
    {
        var dot = new Ellipse { Width = 13, Height = 13, Fill = Brush("#FF2020"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _signalDots[id] = dot;

        var icon = MakeSemaphoreIcon(11, 24);
        if (icon is FrameworkElement fi) { fi.VerticalAlignment = VerticalAlignment.Center; fi.Margin = new Thickness(0, 0, 5, 0); }

        var nameTb = new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI"), Foreground = Brush("#111111"),
            VerticalAlignment = VerticalAlignment.Center, Width = 38, Margin = new Thickness(0, 0, 5, 0)
        };

        var initBd = MakeSigRoundBtn("INIT", "#1a7a34", "#1a7a34");
        var emgBd  = MakeSigRoundBtn("EMG",  "#CC0000", "#CC0000");
        initBd.Margin = new Thickness(0, 0, 4, 0);

        string[] sc = ["#FF2020", "#FFCC00", "#00CC44"];
        initBd.MouseLeftButtonUp += (_, _) => { int st = Random.Shared.Next(0, 3); dot.Fill = Brush(sc[st]); MapControl.SetSignal(id, st); };
        emgBd .MouseLeftButtonUp += (_, _) => { dot.Fill = Brush(sc[0]); MapControl.SetSignal(id, 0); };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(dot);
        row.Children.Add(nameTb);
        row.Children.Add(initBd);
        row.Children.Add(emgBd);

        return new Border { Width = 200, Height = 24, Background = Brushes.Transparent, Child = row };
    }

    // ── Signal item — left column (mirrored): EMG → INIT → name → dot → icon ─
    private Border BuildSigItemLeft(string id, string label)
    {
        var dot = new Ellipse { Width = 13, Height = 13, Fill = Brush("#FF2020"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _signalDots[id] = dot;

        var icon = MakeSemaphoreIcon(11, 24);
        if (icon is FrameworkElement fi) { fi.VerticalAlignment = VerticalAlignment.Center; fi.Margin = new Thickness(5, 0, 0, 0); }

        var nameTb = new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI"), Foreground = Brush("#111111"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 38, TextAlignment = TextAlignment.Right,
            Margin = new Thickness(5, 0, 0, 0)
        };

        var initBd = MakeSigRoundBtn("INIT", "#1a7a34", "#1a7a34");
        var emgBd  = MakeSigRoundBtn("EMG",  "#CC0000", "#CC0000");
        emgBd.Margin  = new Thickness(0, 0, 4, 0);
        initBd.Margin = new Thickness(0, 0, 0, 0);

        string[] sc = ["#FF2020", "#FFCC00", "#00CC44"];
        initBd.MouseLeftButtonUp += (_, _) => { int st = Random.Shared.Next(0, 3); dot.Fill = Brush(sc[st]); MapControl.SetSignal(id, st); };
        emgBd .MouseLeftButtonUp += (_, _) => { dot.Fill = Brush(sc[0]); MapControl.SetSignal(id, 0); };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right  // push icon to right edge → symmetric with right column
        };
        row.Children.Add(emgBd);   // ← leftmost
        row.Children.Add(initBd);
        row.Children.Add(nameTb);
        row.Children.Add(dot);
        row.Children.Add(icon);    // ← rightmost, flush with center gap

        return new Border { Width = 200, Height = 24, Background = Brushes.Transparent, Child = row };
    }

    // ── Pill signal button ──────────────────────────────────────────────────
    private static Border MakeSigRoundBtn(string text, string textColor, string borderColor)
    {
        const double w = 42, h = 20;
        var bd = new Border
        {
            Width = w, Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = Brushes.White,
            BorderBrush = Brush("#111111"),
            BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand
        };
        var tb = new TextBlock
        {
            Text = text, FontSize = 9, FontWeight = FontWeights.Black,
            Foreground = Brush(textColor), FontFamily = new FontFamily("Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        bd.Child = tb;
        bd.MouseEnter += (_, _) => { bd.Background = Brush(borderColor); tb.Foreground = Brushes.White; };
        bd.MouseLeave += (_, _) => { bd.Background = Brushes.White;       tb.Foreground = Brush(textColor); };
        return bd;
    }

    private void ShowSwitchInfo(string id)
    {
        if (_infoNameTb == null) return;
        string dir = GetSwitchDir(id);
        _infoNameTb.Text       = id;
        _infoNameTb.Foreground = Brush("#3458e1");
        _infoDirTb!.Text       = dir;
        _infoDirTb.Foreground  = Brush("#3458e1");
    }

    private void ClearSwitchInfo()
    {
        if (_infoNameTb == null) return;
        _infoNameTb.Text       = "—";
        _infoNameTb.Foreground = Brush("#BBBBBB");
        _infoDirTb!.Text       = "—";
        _infoDirTb.Foreground  = Brush("#BBBBBB");
    }

    // ── SVG icon helpers ────────────────────────────────────────────────────

    private static UIElement MakeSwitchIcon(double size = 12, string color = "#CC0000")
    {
        const string p1 = "m298,178.248h-59.517c-3.323-9.592-12.442-16.5-23.151-16.5-13.51,0-24.5,10.99-24.5,24.5 0,13.51 10.99,24.5 24.5,24.5 10.709,0 19.828-6.908 23.151-16.5h59.517v-16zm-74.168,8c0,4.687-3.814,8.5-8.5,8.5-4.687,0-8.5-3.813-8.5-8.5 0-4.686 3.813-8.5 8.5-8.5 4.686,0 8.5,3.813 8.5,8.5z";
        const string p2 = "m171.014,87.252l-76.967,76.967c-3.238-1.582-6.873-2.472-10.713-2.472-10.709,0-19.828,6.908-23.151,16.5h-60.183v16h60.183c3.323,9.592 12.442,16.5 23.151,16.5 13.51,0 24.5-10.99 24.5-24.5 0-3.841-0.891-7.476-2.472-10.715l76.966-76.966-11.314-11.314zm-96.18,98.996c0-4.686 3.813-8.5 8.5-8.5s8.5,3.814 8.5,8.5c0,4.687-3.813,8.5-8.5,8.5s-8.5-3.814-8.5-8.5z";
        try
        {
            var cv = new Canvas { Width = 298, Height = 298 };
            cv.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(p1), Fill = Brush(color) });
            cv.Children.Add(new System.Windows.Shapes.Path { Data = Geometry.Parse(p2), Fill = Brush(color) });
            return new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = cv };
        }
        catch
        {
            // Fallback: simple switch lines
            var cv = new Canvas { Width = size, Height = size };
            double m = size / 2;
            cv.Children.Add(new Line { X1=0, Y1=m, X2=size, Y2=m, Stroke=Brush(color), StrokeThickness=1.5 });
            cv.Children.Add(new Line { X1=m, Y1=m, X2=size, Y2=1, Stroke=Brush(color), StrokeThickness=1.5 });
            return cv;
        }
    }

    // Semaphore icon from semafores.svg paths, rotated 90° to vertical orientation
    private static UIElement MakeSemaphoreIcon(double w = 11, double h = 24)
    {
        // SVG paths from Assets/semafores.svg (512×512 viewBox, horizontal capsule)
        const string pOutline =
            "M396.719,140.719H115.281C51.719,140.719,0,192.438,0,256s51.719,115.281,115.281,115.281h281.438" +
            "C460.266,371.281,512,319.563,512,256S460.266,140.719,396.719,140.719z " +
            "M396.719,344.156H115.281c-48.594,0-88.156-39.547-88.156-88.156s39.563-88.156,88.156-88.156h281.438" +
            "c48.594,0,88.156,39.547,88.156,88.156S445.313,344.156,396.719,344.156z";
        const string pDotR =
            "M397.984,198.328c-31.859,0-57.672,25.828-57.672,57.672s25.813,57.688,57.672,57.688" +
            "s57.672-25.844,57.672-57.688S429.844,198.328,397.984,198.328z";
        const string pDotL =
            "M114.016,198.328c-31.859,0-57.688,25.828-57.688,57.672s25.828,57.688,57.688,57.688" +
            "c31.844,0,57.672-25.844,57.672-57.688S145.859,198.328,114.016,198.328z";
        const string pDotC =
            "M256,198.328c-31.859,0-57.688,25.828-57.688,57.672s25.828,57.688,57.688,57.688" +
            "c31.844,0,57.672-25.844,57.672-57.688S287.844,198.328,256,198.328z";

        // Capsule spans y=140.719–371.281 in the 512×512 SVG (height≈231, full width=512).
        // After rotating 90° around center (256,256) and translating, it fits in a 231×512 canvas.
        var cv = new Canvas { Width = 231, Height = 512 };
        foreach (var pd in new[] { pOutline, pDotR, pDotL, pDotC })
        {
            var tg = new TransformGroup();
            tg.Children.Add(new RotateTransform(90, 256, 256));   // rotate in-place
            tg.Children.Add(new TranslateTransform(-140, 0));      // shift to canvas origin
            var path = new System.Windows.Shapes.Path
            {
                Data            = Geometry.Parse(pd),
                Fill            = Brush("#222222"),
                RenderTransform = tg
            };
            cv.Children.Add(path);
        }

        return new Viewbox { Width = w, Height = h, Stretch = Stretch.Uniform, Child = cv };
    }
}
