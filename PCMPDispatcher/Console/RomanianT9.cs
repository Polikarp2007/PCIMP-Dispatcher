using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PCMPDispatcher;

/// <summary>
/// T9-style Romanian autocomplete.
/// Word list: downloaded from FrequencyWords repo (50k most common Romanian words),
/// cached at %LocalAppData%\PCMPDispatcher\ro_words.txt.
/// Falls back to built-in railway/dispatch phrases if offline.
/// </summary>
public class RomanianT9
{
    // ── Word list (loaded async) ─────────────────────────────────────
    private static string[] _words = _builtIn!;
    private static bool     _loaded = false;

    private static readonly string CacheFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCMPDispatcher", "ro_words.txt");

    // Remote sources (tried in order)
    private static readonly string[] Sources =
    [
        // FrequencyWords — 50k most common Romanian words, format: "word count"
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/ro/ro_50k.txt",
        // Backup: smaller 10k list
        "https://raw.githubusercontent.com/hermitdave/FrequencyWords/master/content/2018/ro/ro_10k.txt",
    ];

    // ── Built-in railway/dispatch fallback ───────────────────────────
    private static readonly string[] _builtIn =
    [
        "bună","bună ziua","bună dimineața","bună seara","salut","salutare",
        "noapte bună","la revedere","pe curând","confirmat","înțeles","da","nu",
        "afirmativ","negativ","roger","recepționat","OK","bine","perfect",
        "în regulă","te rog","vă rog","mulțumesc","cu plăcere","scuze",
        "îmi pare rău","tren","trenul","locomotivă","vagoane","vagon",
        "macaz","macazul","linie","linia","stație","stația","peron",
        "semnal","semnalul","deraiere","frână","viteză","oprire","stop",
        "pornire","manevră","garnitură","cale liberă","cale ocupată",
        "linia este liberă","linia este ocupată","puteți intra pe linie",
        "intrați pe linia","ieșiți de pe linia","treceți pe linia",
        "reduceți viteza","opriți la semnalul","așteptați pe linia",
        "continuați cu atenție","macazul este pozitionat",
        "semnalul este deschis","semnalul este închis",
        "disc verde","disc roșu","în regulă primiți",
        "ați primit confirmarea","dispecer","mecanic","conductor",
        "pasager","pasageri","personal","echipaj","urgent","imediat",
        "atenție","pericol","alertă","incident","defecțiune",
        "sosire","plecare","întârziere","la timp","raport","comunic",
        "stânga","dreapta","înainte","înapoi","nord","sud","est","vest",
    ];

    // ── Init: load words in background ───────────────────────────────

    public static void InitAsync()
    {
        if (_loaded) return;
        Task.Run(async () =>
        {
            try
            {
                string[] words = await LoadFromCacheOrDownload();
                // Merge with built-in phrases (full phrases won't be in freq list)
                var merged = words.Concat(_builtIn).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                _words  = merged;
                _loaded = true;
            }
            catch
            {
                _words  = _builtIn;
                _loaded = true;
            }
        });
    }

    private static async Task<string[]> LoadFromCacheOrDownload()
    {
        // 1. Try cache (valid for 30 days)
        if (File.Exists(CacheFile))
        {
            var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile);
            if (age.TotalDays < 30)
                return ParseWordList(await File.ReadAllTextAsync(CacheFile));
        }

        // 2. Download
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCMPDispatcher/1.0");

        foreach (var url in Sources)
        {
            try
            {
                var raw = await http.GetStringAsync(url);
                Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
                await File.WriteAllTextAsync(CacheFile, raw);
                return ParseWordList(raw);
            }
            catch { /* try next source */ }
        }

        // 3. Stale cache is better than nothing
        if (File.Exists(CacheFile))
            return ParseWordList(await File.ReadAllTextAsync(CacheFile));

        throw new Exception("No word list available");
    }

    /// Parse FrequencyWords format: each line is "word count"
    private static string[] ParseWordList(string raw)
        => raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(line => line.Split(' ')[0].Trim().ToLowerInvariant())
              .Where(w => w.Length >= 2)
              .ToArray();

    // ── Instance (one per TextBox) ───────────────────────────────────

    private readonly TextBox    _input;
    private readonly Popup      _popup;
    private readonly StackPanel _panel;
    private int      _selIdx = -1;
    private string[] _cur    = [];

    public RomanianT9(TextBox input)
    {
        _input = input;

        _panel = new StackPanel { Background = Brushes.White };

        var border = new Border
        {
            Child           = _panel,
            Background      = Brushes.White,
            BorderBrush     = Brush("#3458e1"),
            BorderThickness = new Thickness(1.5),
            Effect          = Shadow()
        };

        _popup = new Popup
        {
            Child              = border,
            PlacementTarget    = _input,
            Placement          = PlacementMode.Top,
            AllowsTransparency = true,
            StaysOpen          = false,
            PopupAnimation     = PopupAnimation.Fade
        };

        _input.TextChanged    += OnTextChanged;
        _input.PreviewKeyDown += OnKeyDown;
        _input.LostFocus      += (_, _) => _popup.IsOpen = false;
    }

    // ── Events ───────────────────────────────────────────────────────

    private void OnTextChanged(object s, TextChangedEventArgs e)
    {
        var word = CurrentWord();
        if (word.Length < 2) { _popup.IsOpen = false; return; }

        _cur    = Suggest(word);
        _selIdx = -1;

        if (_cur.Length == 0) { _popup.IsOpen = false; return; }
        Rebuild(word);
        _popup.IsOpen = true;
    }

    private void OnKeyDown(object s, KeyEventArgs e)
    {
        if (!_popup.IsOpen) return;
        switch (e.Key)
        {
            case Key.Tab:
                Accept(_selIdx >= 0 ? _cur[_selIdx] : _cur[0]);
                e.Handled = true; break;
            case Key.Down:
                _selIdx = Math.Min(_selIdx + 1, _cur.Length - 1);
                Highlight(_selIdx); e.Handled = true; break;
            case Key.Up:
                _selIdx = Math.Max(_selIdx - 1, 0);
                Highlight(_selIdx); e.Handled = true; break;
            case Key.Escape:
                _popup.IsOpen = false; e.Handled = true; break;
        }
    }

    // ── Logic ────────────────────────────────────────────────────────

    private static string[] Suggest(string prefix)
    {
        prefix = prefix.ToLowerInvariant();
        return _words
            .Where(w => w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                     && !w.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.Length)
            .Take(6)
            .ToArray();
    }

    private string CurrentWord()
    {
        var text  = _input.Text;
        int caret = _input.CaretIndex;
        if (caret == 0 || string.IsNullOrEmpty(text)) return "";
        int start = text.LastIndexOf(' ', Math.Min(caret - 1, text.Length - 1));
        start = start < 0 ? 0 : start + 1;
        return caret > start ? text[start..caret] : "";
    }

    private void Accept(string word)
    {
        var text  = _input.Text;
        int caret = _input.CaretIndex;
        int start = text.LastIndexOf(' ', Math.Min(caret - 1, text.Length - 1));
        start = start < 0 ? 0 : start + 1;
        var before = text[..start];
        var after  = caret < text.Length ? text[caret..] : "";
        _input.Text       = before + word + " " + after;
        _input.CaretIndex = before.Length + word.Length + 1;
        _popup.IsOpen     = false;
    }

    // ── UI ───────────────────────────────────────────────────────────

    private void Rebuild(string typed)
    {
        _panel.Children.Clear();
        for (int i = 0; i < _cur.Length; i++)
        {
            var idx  = i;
            var word = _cur[i];

            var row = new Border
            {
                Padding    = new Thickness(12, 7, 18, 7),
                Background = Brushes.White,
                Cursor     = Cursors.Hand,
                MinWidth   = 180
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };

            // Matched prefix — bold blue
            var matchLen = Math.Min(typed.Length, word.Length);
            sp.Children.Add(new TextBlock
            {
                Text = word[..matchLen], FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = Brush("#3458e1"), FontFamily = new FontFamily("Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            });
            // Remainder — gray
            if (word.Length > matchLen)
                sp.Children.Add(new TextBlock
                {
                    Text = word[matchLen..], FontSize = 13,
                    Foreground = Brush("#999999"), FontFamily = new FontFamily("Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center
                });

            row.Child = sp;
            row.MouseEnter       += (_, _) => { _selIdx = idx; Highlight(idx); };
            row.MouseLeave       += (_, _) => Highlight(_selIdx);
            row.MouseLeftButtonUp += (_, _) => Accept(word);
            _panel.Children.Add(row);

            if (i < _cur.Length - 1)
                _panel.Children.Add(new Border
                {
                    Height = 1, Background = Brush("#F0F0F4"),
                    Margin = new Thickness(10, 0, 10, 0)
                });
        }
    }

    private void Highlight(int idx)
    {
        int row = 0;
        foreach (var child in _panel.Children)
        {
            if (child is Border b && b.MinWidth > 0)
            {
                b.Background = (row == idx) ? Brush("#EEF2FF") : Brushes.White;
                row++;
            }
        }
    }

    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    private static System.Windows.Media.Effects.DropShadowEffect Shadow() =>
        new() { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.15, Direction = 270 };
}
