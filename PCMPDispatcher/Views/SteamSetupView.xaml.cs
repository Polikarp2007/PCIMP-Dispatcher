using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PCMPDispatcher;

public partial class SteamSetupView : UserControl
{
    public event Action<string, string, string>? SetupCompleted; // steamId, nickname, avatarUrl

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36" } }
    };

    private string? _steamId;
    private string? _avatarUrl;
    private CancellationTokenSource? _loadCts;

    public SteamSetupView()
    {
        InitializeComponent();
    }

    public void Open()
    {
        Visibility = Visibility.Visible;
        SteamInput.Text = "";
        _steamId = null;
        _avatarUrl = null;
        SetState("idle");
        SteamInput.Focus();
    }

    // ── States ────────────────────────────────────────────────────────────

    private void SetState(string state, string errorMsg = "")
    {
        StatusArea.Visibility  = state == "idle" ? Visibility.Collapsed : Visibility.Visible;
        LoadingRow.Visibility  = state == "loading" ? Visibility.Visible : Visibility.Collapsed;
        ProfileRow.Visibility  = state == "found"   ? Visibility.Visible : Visibility.Collapsed;
        ErrorRow.Visibility    = state == "error"   ? Visibility.Visible : Visibility.Collapsed;
        if (state == "error") ErrorText.Text = errorMsg;
    }

    // ── Input changed — auto-load after user stops typing ─────────────────

    private async void OnSteamInputChanged(object sender, TextChangedEventArgs e)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cts = _loadCts;

        _steamId = null;
        _avatarUrl = null;
        SaveBtn.IsEnabled = false;

        string input = SteamInput.Text.Trim();
        if (input.Length < 5) { SetState("idle"); return; }

        string? steamId = ExtractSteamId(input);
        if (steamId == null) { SetState("idle"); return; }

        SetState("loading");

        try
        {
            await Task.Delay(600, cts.Token); // debounce
            if (cts.IsCancellationRequested) return;

            var (nick, avatarUrl) = await LoadSteamProfile(steamId, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (nick == null)
            {
                SetState("error", "Could not load Steam profile. Make sure your profile is public.");
                return;
            }

            _steamId = steamId;
            _avatarUrl = avatarUrl;

            PreviewNickname.Text = nick;
            if (!string.IsNullOrEmpty(avatarUrl))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(avatarUrl, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                PreviewAvatar.Source = bmp;
            }

            SetState("found");
            SaveBtn.IsEnabled = true;
        }
        catch (OperationCanceledException) { }
        catch { SetState("error", "Network error. Check your connection and try again."); }
    }

    // ── Steam ID extraction ───────────────────────────────────────────────

    private static string? ExtractSteamId(string input)
    {
        // Direct 17-digit ID
        if (Regex.IsMatch(input, @"^\d{17}$")) return input;

        // URL: /profiles/76561198123456789
        var m = Regex.Match(input, @"/profiles/(\d{17})");
        if (m.Success) return m.Groups[1].Value;

        return null;
    }

    // ── Steam profile fetch ───────────────────────────────────────────────
    // Scrapes the public profile page — no API key needed.
    // Steam embeds og:image (avatar) and the nickname in <title>.

    private static async Task<(string? nick, string? avatar)> LoadSteamProfile(string steamId, CancellationToken ct)
    {
        try
        {
            string html = await Http.GetStringAsync(
                $"https://steamcommunity.com/profiles/{steamId}", ct);

            // <title>Steam Community :: NickName</title>
            string? nick = MatchGroup(html, @"<title>Steam Community :: (.+?)</title>");

            // <meta property="og:image" content="https://avatars...jpg"/>
            string? avatar = MatchGroup(html, @"property=""og:image""\s+content=""(https?://[^""]+)""");
            if (avatar == null)
                avatar = MatchGroup(html, @"content=""(https?://avatars\.[^""]+_full\.jpg)""");

            return (nick?.Trim(), avatar?.Trim());
        }
        catch { return (null, null); }
    }

    private static string? MatchGroup(string input, string pattern)
    {
        var m = Regex.Match(input, pattern, RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }

    // ── Buttons ───────────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_steamId))
        {
            SetState("error", "Please enter a valid Steam ID or profile URL first.");
            return;
        }

        Visibility = Visibility.Collapsed;
        SetupCompleted?.Invoke(_steamId!, PreviewNickname.Text, _avatarUrl ?? "");
    }
}
