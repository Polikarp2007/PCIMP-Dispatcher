using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SysIO = System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private class ConsistDef
    {
        public readonly List<string> Switches = new();
        public bool   Recording;
        public bool   Visible  = true;
        public bool   Applied  = false;
        public string Color    = "#00dd55"; // assigned once at slot creation
        public Border?           Box;
        public TextBlock?        SeqText;
        public StackPanel?       Btns;
        public Border?           RecBtn;
        public Border?           VisBtn;
        public Border?           ApplyBtn;
        public Border?           DeleteBtn;
        public FrameworkElement? TrainInput;   // custom dropdown control
        public string            TrainNo  = "";
        public Action<bool>?     SetTrainDropdownEnabled;
        public Action?           ResetTrainDropdown;
    }

    private readonly List<ConsistDef>  _consists       = new();
    private readonly HashSet<string>   _lockedSwitches = new();
    private readonly Dictionary<string, Border> _switchCircleBorders = new();
    private StackPanel? _consistSection;
    private const int   MaxConsists = 2;

    private FrameworkElement BuildConsistSection()
    {
        _consists.Clear();
        _lockedSwitches.Clear();

        var section = new StackPanel();
        section.Children.Add(new TextBlock
        {
            Text = "CONSIST", FontSize = 11, FontWeight = FontWeights.Black,
            Foreground = Brush("#222222"), FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        _consistSection = new StackPanel();
        _consistSection.SetValue(StackPanel.MarginProperty, new Thickness(0));
        section.Children.Add(_consistSection);

        AddConsistSlot();
        return section;
    }

    private void AddConsistSlot()
    {
        if (_consists.Count >= MaxConsists) return;
        var def = new ConsistDef();
        def.Color = ConsistColors.FirstOrDefault(c => _consists.All(x => x.Color != c)) ?? ConsistColors[0];
        _consists.Add(def);
        string dotColor = def.Color;

        def.Box = new Border
        {
            Background = Brush("#FAFAFA"), BorderBrush = Brush("#DDDDDD"),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(0),
            Margin = new Thickness(0, 0, 0, 6)
        };

        // ── Vertical card layout ──────────────────────────────────────────
        var card = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8, 7, 8, 7) };
        card.Tag = "card";

        // Row 1: dot + label/status + action buttons (right-aligned)
        var topRow = new Grid();
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });         // dot
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // status
        topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });         // buttons

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9, Height = 9, Fill = Brush(dotColor),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0)
        };
        Grid.SetColumn(dot, 0);
        topRow.Children.Add(dot);

        def.SeqText = new TextBlock
        {
            Text = "no consist", FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#BBBBBB"), FontFamily = new FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(def.SeqText, 1);
        topRow.Children.Add(def.SeqText);

        // Right button group: rec/save/clear/vis/del
        var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rightBtns, 2);

        def.Btns = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
        var saveBtn = MakeConsistBtn("✓", "#28A745");
        saveBtn.Margin = new Thickness(3, 0, 0, 0);
        saveBtn.MouseLeftButtonUp += (_, _) => SaveConsist(def);
        var clearBtn = MakeConsistBtn("✕", "#888888");
        clearBtn.Margin = new Thickness(3, 0, 0, 0);
        clearBtn.MouseLeftButtonUp += (_, _) => ClearConsist(def);
        def.Btns.Children.Add(saveBtn);
        def.Btns.Children.Add(clearBtn);

        def.VisBtn = MakeConsistBtn("HIDE", "#3458e1");
        def.VisBtn.Margin = new Thickness(3, 0, 0, 0);
        def.VisBtn.Visibility = Visibility.Collapsed;
        def.VisBtn.MouseLeftButtonUp += (_, _) =>
        {
            if (def.Switches.Count == 0 || def.Recording) return;
            def.Visible = !def.Visible;
            if (def.Box != null) def.Box.Opacity = def.Visible ? 1.0 : 0.55;
            if (def.VisBtn.Child is TextBlock vt) vt.Text = def.Visible ? "HIDE" : "SHOW";
            RedrawAllVisibleConsists();
        };

        def.RecBtn = MakeConsistBtn("NEW", "#CC0000");
        def.RecBtn.Margin = new Thickness(3, 0, 0, 0);
        def.RecBtn.MouseLeftButtonUp += (_, _) => StartRecording(def);

        def.DeleteBtn = MakeConsistBtn("✕ DEL", "#CC0000");
        def.DeleteBtn.Margin = new Thickness(3, 0, 0, 0);
        def.DeleteBtn.Visibility = Visibility.Collapsed;
        def.DeleteBtn.MouseLeftButtonUp += (_, _) => DeleteAppliedConsist(def);

        rightBtns.Children.Add(def.Btns);
        rightBtns.Children.Add(def.VisBtn);
        rightBtns.Children.Add(def.RecBtn);
        rightBtns.Children.Add(def.DeleteBtn);
        topRow.Children.Add(rightBtns);

        card.Children.Add(topRow);

        // Row 2: chips area (hidden when empty, shown on recording/saved)
        // populated dynamically by RefreshConsistChips

        // Row 3: train dropdown + APPLY (hidden until saved)
        var bottomRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
            Tag = "bottomRow"
        };

        def.TrainInput = BuildTrainDropdown(def);
        def.TrainInput.Margin = new Thickness(0, 0, 4, 0);

        def.ApplyBtn = MakeConsistBtn("APPLY", "#3458e1");
        def.ApplyBtn.MouseLeftButtonUp += (_, _) => ApplyConsist(def);

        bottomRow.Children.Add(def.TrainInput);
        bottomRow.Children.Add(def.ApplyBtn);
        card.Children.Add(bottomRow);

        def.Box.Child = card;
        _consistSection!.Children.Add(def.Box);
    }

    private FrameworkElement BuildTrainDropdown(ConsistDef def)
    {
        string[] trains = ["IR 1833", "R 9087", "R 4521", "IC 527", "IR 1745",
                           "CFR 2031", "R 8812", "IC 631", "M 401", "R 5512",
                           "IR 347",  "Acc 7723"];

        // Header row: label + arrow
        var labelTb = new TextBlock
        {
            Text = "select train…", FontSize = 13, FontWeight = FontWeights.Black,
            FontFamily = new FontFamily("Segoe UI"), Foreground = Brush("#AAAAAA"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        var arrowTb = new TextBlock
        {
            Text = "▾", FontSize = 11, Foreground = Brush("#3458e1"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        var hdrContent = new Grid();
        hdrContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(labelTb, 0);
        Grid.SetColumn(arrowTb, 1);
        hdrContent.Children.Add(labelTb);
        hdrContent.Children.Add(arrowTb);

        var hdr = new Border
        {
            Width = 130, Height = 28,
            CornerRadius = new CornerRadius(5),
            Background = Brushes.White,
            BorderBrush = Brush("#3458e1"), BorderThickness = new Thickness(1.5),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 0, 6, 0),
            Child = hdrContent
        };

        // Declare popup early so item lambdas can capture it
        var popup = new Popup
        {
            StaysOpen = false, AllowsTransparency = true,
            PlacementTarget = hdr, Placement = PlacementMode.Bottom,
            VerticalOffset = 3, MinWidth = 124
        };

        // Popup list
        var listSp = new StackPanel { Background = Brushes.Transparent };
        foreach (var train in trains)
        {
            var tb = new TextBlock
            {
                Text = train, FontSize = 12, FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI"), Foreground = Brush("#1a1a2e"),
                Padding = new Thickness(12, 7, 12, 7)
            };
            var row = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = tb };
            row.MouseEnter += (_, _) => row.Background = Brush("#F0F4FF");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;

            var captured = train;
            row.MouseLeftButtonUp += (_, _) =>
            {
                def.TrainNo        = captured;
                labelTb.Text       = captured;
                labelTb.Foreground = Brush("#111111");
                popup.IsOpen       = false;
            };
            listSp.Children.Add(row);
        }

        popup.Child = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("#DDDDDD"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 12, Opacity = 0.18, ShadowDepth = 3, Direction = 270 },
            Child = listSp
        };

        hdr.MouseLeftButtonUp += (_, _) => { if (hdr.IsEnabled) popup.IsOpen = !popup.IsOpen; };
        hdr.MouseEnter += (_, _) => { if (hdr.IsEnabled) hdr.BorderBrush = Brush("#1e3bc4"); };
        hdr.MouseLeave += (_, _) => hdr.BorderBrush = Brush("#3458e1");

        def.SetTrainDropdownEnabled = enabled =>
        {
            hdr.IsEnabled = enabled;
            hdr.Opacity   = enabled ? 1.0 : 0.55;
            hdr.Cursor    = enabled ? Cursors.Hand : Cursors.Arrow;
            arrowTb.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        };
        def.ResetTrainDropdown = () =>
        {
            labelTb.Text       = "select train…";
            labelTb.Foreground = Brush("#AAAAAA");
            def.TrainNo        = "";
        };

        return hdr;
    }

    private static Border MakeConsistBtn(string label, string color)
    {
        var bd = new Border
        {
            Background = Brushes.White, BorderBrush = Brush(color),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(0),
            Padding = new Thickness(9, 4, 9, 4), Cursor = Cursors.Hand
        };
        bd.Child = new TextBlock
        {
            Text = label, FontSize = 11, FontWeight = FontWeights.Black,
            Foreground = Brush(color), FontFamily = new FontFamily("Segoe UI")
        };
        bd.MouseEnter += (_, _) => { bd.Background = Brush(color); ((TextBlock)bd.Child).Foreground = Brushes.White; };
        bd.MouseLeave += (_, _) => { bd.Background = Brushes.White; ((TextBlock)bd.Child).Foreground = Brush(color); };
        return bd;
    }

    private void StartRecording(ConsistDef def)
    {
        def.Recording = true;
        MapControl.SetConsistMode(true);
        if (def.Box     != null) { def.Box.BorderBrush = Brush("#CC0000"); def.Box.Background = Brush("#FFF8F8"); }
        if (def.SeqText != null) { def.SeqText.Text = "click switches…"; def.SeqText.Foreground = Brush("#AAAAAA"); }
        if (def.Btns    != null) def.Btns.Visibility = Visibility.Visible;
        if (def.RecBtn  != null) def.RecBtn.Visibility = Visibility.Collapsed;
    }

    private void SaveConsist(ConsistDef def)
    {
        if (def.Switches.Count < 2) { ClearConsist(def); return; }
        def.Recording = false;
        MapControl.SetConsistMode(false);

        foreach (var id in def.Switches)
        {
            _lockedSwitches.Add(id);
            MapControl.LockSwitch(id, true);
            if (_switchCircleBorders.TryGetValue(id, out var b))
                { b.BorderBrush = Brush("#AAAAAA"); b.Cursor = Cursors.Arrow; }
        }

        if (def.Box     != null) { def.Box.BorderBrush = Brush("#AAAAAA"); def.Box.Background = Brush("#F5F5F5"); }
        if (def.SeqText != null) def.SeqText.Foreground = Brush("#333333");
        if (def.Btns    != null)
        {
            var editBtn = MakeConsistBtn("✎", "#3458e1");
            editBtn.Margin = new Thickness(4, 0, 0, 0);
            editBtn.MouseLeftButtonUp += (_, _) => EditConsist(def);
            def.Btns.Children.RemoveAt(0);
            def.Btns.Children.Insert(0, editBtn);
        }

        if (def.VisBtn    != null) def.VisBtn.Visibility    = Visibility.Visible;
        // Show bottom row (train dropdown + APPLY)
        if (def.Box?.Child is StackPanel sc)
        {
            var br = sc.Children.OfType<StackPanel>().FirstOrDefault(e => e.Tag as string == "bottomRow");
            if (br != null) br.Visibility = Visibility.Visible;
        }
        RedrawAllVisibleConsists();
        if (_consists.Count < MaxConsists) AddConsistSlot();
    }

    private void EditConsist(ConsistDef def)
    {
        if (def.Applied) return;
        foreach (var id in def.Switches)
        {
            _lockedSwitches.Remove(id);
            MapControl.LockSwitch(id, false);
            MapControl.HighlightSwitch(id, false);
            if (_switchCircleBorders.TryGetValue(id, out var b))
                { b.BorderBrush = Brush("#CC0000"); b.Cursor = Cursors.Hand; }
        }
        var saved = new List<string>(def.Switches);
        def.Switches.Clear();

        if (def.Btns?.Children[0] is Border)
        {
            var sb = MakeConsistBtn("✓", "#28A745");
            sb.Margin = new Thickness(4, 0, 0, 0);
            sb.MouseLeftButtonUp += (_, _) => SaveConsist(def);
            def.Btns.Children.RemoveAt(0);
            def.Btns.Children.Insert(0, sb);
        }

        if (_consists.Count == 2 && _consists[1].Switches.Count == 0 && _consists[1] != def)
        {
            _consists.RemoveAt(1);
            _consistSection!.Children.RemoveAt(1);
        }

        StartRecording(def);
        foreach (var id in saved) AddToConsist(def, id);
    }

    private void ClearConsist(ConsistDef def)
    {
        if (def.Applied) return;
        def.Recording = false;
        MapControl.SetConsistMode(false);

        foreach (var id in def.Switches)
        {
            _lockedSwitches.Remove(id);
            MapControl.LockSwitch(id, false);
            MapControl.HighlightSwitch(id, false);
            if (_switchCircleBorders.TryGetValue(id, out var b))
                { b.BorderBrush = Brush("#CC0000"); b.Cursor = Cursors.Hand; }
        }
        def.Switches.Clear();
        MapControl.ClearRoute();

        if (def.Box     != null) { def.Box.BorderBrush = Brush("#DDDDDD"); def.Box.Background = Brush("#FAFAFA"); }
        if (def.Box?.Child is StackPanel clCard2)
        {
            var ec = clCard2.Children.OfType<FrameworkElement>().FirstOrDefault(e => e.Tag as string == "chips");
            if (ec != null) clCard2.Children.Remove(ec);
        }
        if (def.SeqText != null) { def.SeqText.Text = "no consist"; def.SeqText.Foreground = Brush("#BBBBBB"); def.SeqText.Visibility = Visibility.Visible; }
        if (def.Btns    != null)
        {
            def.Btns.Visibility = Visibility.Collapsed;
            if (def.Btns.Children[0] is Border eb && eb.Child is TextBlock ebt && ebt.Text == "✎")
            {
                var sb = MakeConsistBtn("✓", "#28A745");
                sb.Margin = new Thickness(4, 0, 0, 0);
                sb.MouseLeftButtonUp += (_, _) => SaveConsist(def);
                def.Btns.Children.RemoveAt(0);
                def.Btns.Children.Insert(0, sb);
            }
        }
        if (def.RecBtn != null) def.RecBtn.Visibility = Visibility.Visible;
        if (def.VisBtn   != null) { def.VisBtn.Visibility = Visibility.Collapsed; if (def.VisBtn.Child is TextBlock vt) vt.Text = "HIDE"; }
        def.TrainNo = ""; def.ResetTrainDropdown?.Invoke();
        if (def.DeleteBtn  != null) def.DeleteBtn.Visibility  = Visibility.Collapsed;
        // Hide bottom row
        if (def.Box?.Child is StackPanel clCard)
        {
            var br = clCard.Children.OfType<StackPanel>().FirstOrDefault(e => e.Tag as string == "bottomRow");
            if (br != null) br.Visibility = Visibility.Collapsed;
        }
        // Reset box style
        if (def.Box != null) { def.Box.BorderBrush = Brush("#DDDDDD"); def.Box.Background = Brush("#FAFAFA"); def.Box.BorderThickness = new Thickness(1.5); }
        def.Visible = true;
        if (def.Box != null) def.Box.Opacity = 1.0;

        int idx = _consists.IndexOf(def);
        if (idx > 0)
        {
            _consists.RemoveAt(idx);
            _consistSection!.Children.RemoveAt(idx);
        }
        // Restore other visible consists and ensure empty slot exists
        RedrawAllVisibleConsists();
        if (_consists.Count < MaxConsists && _consists.All(c => c.Switches.Count > 0 && !c.Recording))
            AddConsistSlot();
    }

    private void AddToConsist(ConsistDef def, string id)
    {
        if (!def.Recording || _lockedSwitches.Contains(id)) return;
        if (def.Switches.Count > 0 && def.Switches.Last() == id)
        {
            def.Switches.RemoveAt(def.Switches.Count - 1);
            MapControl.HighlightSwitch(id, false);
            if (_switchCircleBorders.TryGetValue(id, out var b)) { b.BorderBrush = Brush("#CC0000"); b.Cursor = Cursors.Hand; }
        }
        else if (!def.Switches.Contains(id))
        {
            def.Switches.Add(id);
            MapControl.HighlightSwitch(id, true);
            if (_switchCircleBorders.TryGetValue(id, out var b)) { b.BorderBrush = Brush("#AAAAAA"); b.Cursor = Cursors.Arrow; }
        }

        RefreshConsistChips(def);
        MapControl.ClearRoute();
        foreach (var c in _consists)
        {
            if (c == def) continue;
            if (c.Switches.Count >= 2 && c.Visible && !c.Recording)
                MapControl.AddRoute(c.Switches.ToArray(), c.Color);
        }
        if (def.Switches.Count >= 2)
            MapControl.AddRoute(def.Switches.ToArray(), def.Color);
    }

    private static WrapPanel BuildChipsPanel(IEnumerable<string> ids, string chipColor, string textColor)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var id in ids)
        {
            var chip = new Border
            {
                Background = Brush(chipColor),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                Margin = new Thickness(0, 2, 3, 2)
            };
            chip.Child = new TextBlock
            {
                Text = id, FontSize = 11, FontWeight = FontWeights.Black,
                Foreground = Brush(textColor), FontFamily = new FontFamily("Segoe UI")
            };
            wrap.Children.Add(chip);
        }
        return wrap;
    }

    private void RefreshConsistChips(ConsistDef def)
    {
        if (def.Box?.Child is not StackPanel card) return;

        // Remove any existing chips row (tagged "chips")
        var existing = card.Children.OfType<FrameworkElement>().FirstOrDefault(e => e.Tag as string == "chips");
        if (existing != null) card.Children.Remove(existing);

        if (def.Switches.Count == 0)
        {
            if (def.SeqText != null) { def.SeqText.Text = "click switches…"; def.SeqText.Foreground = Brush("#AAAAAA"); def.SeqText.Visibility = Visibility.Visible; }
            return;
        }

        if (def.SeqText != null) { def.SeqText.Text = $"{def.Switches.Count} switches"; def.SeqText.Foreground = Brush("#555555"); def.SeqText.Visibility = Visibility.Visible; }

        var chips = BuildChipsPanel(def.Switches, "#E8EDFF", "#1a1a2e");
        chips.Tag = "chips";
        chips.Margin = new Thickness(0, 5, 0, 0);

        // Insert chips before bottomRow
        var bottomIdx = card.Children.OfType<FrameworkElement>().ToList().FindIndex(e => e.Tag as string == "bottomRow");
        if (bottomIdx >= 0) card.Children.Insert(bottomIdx, chips);
        else card.Children.Add(chips);
    }

    private void ApplyConsist(ConsistDef def)
    {
        if (def.Switches.Count < 2 || def.Applied) return;
        def.Applied = true;

        // Visual: brand-blue border, darker bg, "APPLIED" badge in seq text
        if (def.Box != null)
        {
            def.Box.BorderBrush  = Brush("#3458e1");
            def.Box.Background   = Brush("#F0F4FF");
            def.Box.BorderThickness = new Thickness(2);
        }
        if (def.SeqText != null)
        {
            def.SeqText.Foreground = Brush("#1a1a2e");
        }

        // Replace recording chips with applied (blue) chips
        if (def.Box?.Child is StackPanel applyCard)
        {
            var oldChips = applyCard.Children.OfType<FrameworkElement>().FirstOrDefault(e => e.Tag as string == "chips");
            if (oldChips != null) applyCard.Children.Remove(oldChips);

            if (def.SeqText != null)
            {
                def.SeqText.Text = $"APPLIED · {def.Switches.Count} switches";
                def.SeqText.Foreground = Brush("#3458e1");
                def.SeqText.Visibility = Visibility.Visible;
            }

            var appliedChips = BuildChipsPanel(def.Switches, "#3458e1", "#ffffff");
            appliedChips.Tag = "chips";
            appliedChips.Margin = new Thickness(0, 5, 0, 0);

            var bottomIdx = applyCard.Children.OfType<FrameworkElement>().ToList().FindIndex(e => e.Tag as string == "bottomRow");
            if (bottomIdx >= 0) applyCard.Children.Insert(bottomIdx, appliedChips);
            else applyCard.Children.Add(appliedChips);
        }

        // Lock train dropdown — no more editing after apply
        def.SetTrainDropdownEnabled?.Invoke(false);

        // Hide EDIT/SAVE, VIS, APPLY — show only DELETE
        if (def.Btns    != null) def.Btns.Visibility    = Visibility.Collapsed;
        if (def.VisBtn  != null) def.VisBtn.Visibility  = Visibility.Collapsed;
        if (def.ApplyBtn!= null) def.ApplyBtn.Visibility= Visibility.Collapsed;
        if (def.RecBtn  != null) def.RecBtn.Visibility  = Visibility.Collapsed;
        if (def.DeleteBtn!= null) def.DeleteBtn.Visibility = Visibility.Visible;

        // Post to chat
        string trainNo = !string.IsNullOrWhiteSpace(def.TrainNo) ? def.TrainNo.Trim() : "???";
        string route   = string.Join(">", def.Switches);
        PostChatConsist(trainNo, route);
        // TODO: send switch configuration to server
        // ServerApi.SendConsist(def.Switches, _paulisSwitches, trainNo);
    }

    private void DeleteAppliedConsist(ConsistDef def)
    {
        def.Applied = false;
        // Unlock switches
        foreach (var id in def.Switches)
        {
            _lockedSwitches.Remove(id);
            MapControl.LockSwitch(id, false);
            MapControl.HighlightSwitch(id, false);
            if (_switchCircleBorders.TryGetValue(id, out var b))
                { b.BorderBrush = Brush("#CC0000"); b.Cursor = Cursors.Hand; }
        }
        def.Switches.Clear();
        RedrawAllVisibleConsists();

        int idx = _consists.IndexOf(def);
        if (idx >= 0)
        {
            _consists.RemoveAt(idx);
            _consistSection!.Children.RemoveAt(idx);
        }
        if (_consists.Count < MaxConsists && _consists.All(c => c.Switches.Count > 0 && !c.Recording))
            AddConsistSlot();
        else if (_consists.Count == 0)
            AddConsistSlot();
    }

    private static readonly string[] ConsistColors = ["#00dd55", "#ff9900"]; // palette, order = preference

    private void RedrawAllVisibleConsists()
    {
        MapControl.ClearRoute();
        foreach (var c in _consists)
            if (c.Switches.Count >= 2 && c.Visible && !c.Recording)
                MapControl.AddRoute(c.Switches.ToArray(), c.Color);
    }

    private void OnHtmlSwitchToggled(string id, bool state)
    {
        if (_lockedSwitches.Contains(id)) return;
        _paulisSwitches[id] = state;
        if (_switchStatusTbs.TryGetValue(id, out var tb))
        {
            var dir = GetSwitchDir(id);
            tb.Text = dir;
            // Skip post if change was initiated from our UI button (already posted there)
            if (!_uiInitiatedSwitches.Remove(id))
                PostChatSwitch(id, dir);
        }
    }

    private void OnHtmlConsistSelected(string id)
    {
        var rec = _consists.FirstOrDefault(c => c.Recording);
        if (rec != null) AddToConsist(rec, id);
    }
}
