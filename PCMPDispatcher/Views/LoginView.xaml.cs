using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PCMPDispatcher.Services;

namespace PCMPDispatcher;

public partial class LoginView : UserControl
{
    public event Action<string, string, string>? LoginSucceeded; // token, hwid, steam_id

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string AuthBase = "https://auth.poli-co.com";
    private const string SiteAuth = "https://poli-co.com/?pcimp_auth=1&rid=";

    // Token cached locally, encrypted with Windows DPAPI (per-user, per-machine).
    private static string TokenPath =>
        Path.Combine(AppContext.BaseDirectory, "pcimp.session");

    private readonly string _hwid;
    private CancellationTokenSource? _pollCts;

    public LoginView()
    {
        InitializeComponent();
        _hwid = HardwareInfo.Hwid();
    }

    // ── entry ────────────────────────────────────────────────────────────

    public enum SessionState { NotBound, Valid, Banned }

    /// <summary>
    /// Silent background check used during the splash screen. No UI is touched.
    /// Returns Valid (skip to role picker), Banned (+until date), or NotBound.
    /// </summary>
    public async Task<(SessionState state, DateTime? until)> HasValidSessionAsync()
    {
        string? saved = LoadToken();
        if (saved == null) return (SessionState.NotBound, null);

        try
        {
            var payload = new StringContent(
                JsonSerializer.Serialize(new { token = saved, hwid = _hwid }),
                Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{AuthBase}/verify", payload);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                UserSession.Set(
                    root.TryGetProperty("display", out var d) ? d.GetString() : null,
                    root.TryGetProperty("username", out var un) ? un.GetString() : null,
                    root.TryGetProperty("email", out var em) ? em.GetString() : null);
                UserSession.SetAuth(saved, _hwid);
                UserSession.SetSteam(
                    root.TryGetProperty("steam_nickname", out var sn) ? sn.GetString() : null,
                    root.TryGetProperty("steam_avatar_url", out var sa) ? sa.GetString() : null);
                return (SessionState.Valid, null);
            }

            string status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            if (status == "banned")
            {
                DateTime? until = null;
                if (root.TryGetProperty("until", out var u) && u.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(u.GetString(), out var dt))
                    until = dt.ToLocalTime();
                return (SessionState.Banned, until);
            }

            // invalid / expired / revoked → force a fresh login next time
            ClearToken();
            return (SessionState.NotBound, null);
        }
        catch
        {
            // offline — can't verify; keep the token, just show the login form
            return (SessionState.NotBound, null);
        }
    }

    /// <summary>Show the login form (no valid session).</summary>
    public void Open()
    {
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
        SignInPanel.Visibility = Visibility.Visible;
        BanPanel.Visibility    = Visibility.Collapsed;
    }

    /// <summary>Show the "account suspended" card instead of the login form.</summary>
    public void ShowBanned(DateTime? until)
    {
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));

        SignInPanel.Visibility = Visibility.Collapsed;
        BanPanel.Visibility    = Visibility.Visible;

        int days = until.HasValue
            ? Math.Max(1, (int)Math.Ceiling((until.Value - DateTime.Now).TotalDays))
            : 0;
        string word = days == 1 ? "day" : "days";

        BanMainText.Text = until.HasValue
            ? $"You have been banned by the Poli&Co team for {days} {word}."
            : "You have been banned by the Poli&Co team.";
    }

    // ── button ───────────────────────────────────────────────────────────

    private async void OnLoginClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetBusy(true, "Starting secure sign-in…");

        // 1) Ask the server to open a login request bound to this machine's HWID.
        string? rid = await StartRequestAsync();
        if (rid == null)
        {
            SetBusy(false, "Could not reach the server. Check your connection and try again.");
            return;
        }

        // 2) Open the browser with the request id (HWID never travels through the URL).
        try
        {
            Process.Start(new ProcessStartInfo { FileName = SiteAuth + rid, UseShellExecute = true });
        }
        catch
        {
            SetBusy(false, "Could not open the browser. Please try again.");
            return;
        }

        // 3) Poll the server until the site confirms the login (or time out).
        SetBusy(true, "Waiting for confirmation in your browser…");
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            while (!_pollCts.Token.IsCancellationRequested)
            {
                await Task.Delay(2500, _pollCts.Token);
                var (state, token, display, reason, steamId, steamNick, steamAvatar) = await PollAsync(rid);
                if (state == PollState.Done && token != null)
                {
                    SaveToken(token);              // encrypted at rest
                    UserSession.Set(display);
                    UserSession.SetSteam(steamNick, steamAvatar);
                    UserSession.SetAuth(token, _hwid);
                    SetBusy(false);
                    LoginSucceeded?.Invoke(token, _hwid, steamId ?? "");
                    return;
                }
                if (state == PollState.Error)
                {
                    SetBusy(false, FriendlyReason(reason));
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }

        SetBusy(false, "Sign-in timed out. Click the button to try again.");
    }

    // ── server calls ─────────────────────────────────────────────────────

    private async Task<string?> StartRequestAsync()
    {
        try
        {
            var body = new { hwid = _hwid, fingerprint = HardwareInfo.Collect() };
            var payload = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{AuthBase}/auth/start", payload);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return root.GetProperty("request_id").GetString();
        }
        catch { }
        return null;
    }

    /// <summary>Best-effort logout ping when the app closes (logs the session end).</summary>
    public static void EndSession()
    {
        try
        {
            string? token = LoadToken();
            if (token == null) return;
            var body = new { token, hwid = HardwareInfo.Hwid() };
            var payload = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            // fire-and-forget with a short timeout so shutdown isn't blocked
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            c.PostAsync($"{AuthBase}/session/end", payload).Wait(2500);
        }
        catch { }
    }

    private enum PollState { Pending, Done, Error }

    private async Task<(PollState state, string? token, string? display, string? reason, string? steamId, string? steamNick, string? steamAvatar)> PollAsync(string rid)
    {
        try
        {
            var resp = await Http.GetStringAsync($"{AuthBase}/auth/poll?request_id={rid}");
            using var doc = JsonDocument.Parse(resp);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                return (PollState.Done,
                    root.GetProperty("token").GetString(),
                    root.TryGetProperty("display_name", out var d) ? d.GetString() : null,
                    null,
                    root.TryGetProperty("steam_id", out var st) ? st.GetString() : null,
                    root.TryGetProperty("steam_nickname", out var sn) ? sn.GetString() : null,
                    root.TryGetProperty("steam_avatar_url", out var sa) ? sa.GetString() : null);

            string status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            if (status == "error")
                return (PollState.Error, null, null,
                    root.TryGetProperty("reason", out var r) ? r.GetString() : null, null, null, null);
        }
        catch { }
        return (PollState.Pending, null, null, null, null, null, null);
    }

    private static string FriendlyReason(string? reason) => reason switch
    {
        "already_linked" => "This account is already linked to another PC. Contact support to reset it.",
        "banned"         => "This account is temporarily blocked. Please try again later or contact support.",
        _                => "Sign-in failed. Please try again."
    };

    // ── token storage (DPAPI) ────────────────────────────────────────────

    private static void SaveToken(string token)
    {
        try
        {
            byte[] enc = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenPath, enc);
        }
        catch { }
    }

    private static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            byte[] dec = ProtectedData.Unprotect(
                File.ReadAllBytes(TokenPath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
        catch { return null; } // copied to another PC → DPAPI throws → treated as no token
    }

    private static void ClearToken()
    {
        try { if (File.Exists(TokenPath)) File.Delete(TokenPath); } catch { }
    }

    // ── UI ───────────────────────────────────────────────────────────────

    private void SetBusy(bool busy, string status = "")
    {
        Dispatcher.Invoke(() =>
        {
            LoginBtn.IsHitTestVisible = !busy;
            BtnLabel.Visibility    = busy ? Visibility.Collapsed : Visibility.Visible;
            SpinnerWrap.Visibility = busy ? Visibility.Visible   : Visibility.Collapsed;
            StatusText.Text        = status;
        });
    }
}
