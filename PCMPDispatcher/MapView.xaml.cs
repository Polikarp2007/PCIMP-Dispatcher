using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PCMPDispatcher;

public partial class MapView : UserControl
{
    public event Action<string, bool>? SwitchToggledFromHtml;
    public event Action<string>?       SwitchSelectedForConsist;

    private readonly Dictionary<string, int> _lastStates = new();
    private readonly DispatcherTimer _pollTimer = new(DispatcherPriority.ApplicationIdle)
        { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _polling = false;
    private string? _pendingMapPath = null;

    public MapView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                null,
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PCMPDispatcher_WV2")
            );
            await WebMap.EnsureCoreWebView2Async(env);

            // Disable F5/Ctrl+R reload, right-click context menu, browser shortcuts
            WebMap.CoreWebView2.Settings.AreDefaultContextMenusEnabled    = false;
            WebMap.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            WebMap.CoreWebView2.Settings.IsStatusBarEnabled               = false;

            var defaultPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "paulis_panel.html");
            var mapPath = _pendingMapPath ?? defaultPath;
            _pendingMapPath = null;
            WebMap.Source = new Uri(mapPath);

            // Start polling after page loads
            WebMap.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                _lastStates.Clear();
                if (!_pollTimer.IsEnabled) _pollTimer.Start();
            };

            _pollTimer.Tick += async (_, _) => await PollAsync();
        }
        catch
        {
            WebMap.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Call this to start/stop polling (e.g. when console opens/closes)</summary>
    public void StartPolling()  { _lastStates.Clear(); _pollTimer.Start(); }
    public void StopPolling()   { _pollTimer.Stop(); }

    private async Task PollAsync()
    {
        if (_polling || WebMap.CoreWebView2 == null) return;
        _polling = true;
        try
        {
            // ── 1. consist click ──────────────────────────────────────────────
            var cr = await WebMap.CoreWebView2.ExecuteScriptAsync(
                "window.getAndClearConsistClick ? (window.getAndClearConsistClick() || 'null') : 'null'");
            // ExecuteScriptAsync wraps a JS string in extra JSON quotes → "\"P7\""
            if (cr != "null" && cr != "\"null\"")
            {
                var cid = cr.Trim('"');
                if (!string.IsNullOrEmpty(cid))
                    SwitchSelectedForConsist?.Invoke(cid);
            }

            // ── 2. switch states ──────────────────────────────────────────────
            var raw = await WebMap.CoreWebView2.ExecuteScriptAsync(
                "JSON.stringify(window.getAllSwitches ? window.getAllSwitches() : {})");
            // raw is a JSON-encoded string:  "{\"P1\":0,\"P2\":1,...}"
            var jsonStr = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrEmpty(jsonStr)) return;

            using var doc = JsonDocument.Parse(jsonStr);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var id    = prop.Name;
                var state = prop.Value.GetInt32();
                var prev  = _lastStates.TryGetValue(id, out int p) ? p : -1; // -1 = first run
                if (state != prev)
                {
                    _lastStates[id] = state;
                    if (prev != -1) // skip initial snapshot, only real changes
                    {
                        var cid = id; var cst = state;
                        SwitchToggledFromHtml?.Invoke(cid, cst == 1);
                    }
                }
            }
        }
        catch { }
        finally { _polling = false; }
    }

    public void FocusStation(string stationName)
    {
        var fileName = stationName.ToLowerInvariant() switch
        {
            "arad"     => "arad_panel.html",
            "ghioroc"  => "ghioroc_panel.html",
            "glogovat" => "glogovat_panel.html",
            "radna"    => "radna_panel.html",
            _          => "paulis_panel.html"
        };
        var path = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
        if (!System.IO.File.Exists(path)) return;
        if (WebMap.CoreWebView2 != null)
            WebMap.Source = new Uri(path);
        else
            _pendingMapPath = path; // WebView2 not ready yet — OnLoaded will use this
    }

    public void SetSwitch(string switchId, bool reversed)
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync(
            $"window.setSwitch('{switchId}', {(reversed ? "true" : "false")})"); }
        catch { }
    }

    public void BuildRoute(string[] ids, string color = "#00ff55")
    {
        try
        {
            var arr = "[" + string.Join(",", ids.Select(id => $"'{id}'")) + "]";
            WebMap.CoreWebView2?.ExecuteScriptAsync($"window.buildRoute({arr}, '{color}')");
        }
        catch { }
    }

    public void ClearRoute()
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync("window.clearRoute()"); }
        catch { }
    }

    public void AddRoute(string[] ids, string color = "#00dd55")
    {
        try
        {
            var arr = "[" + string.Join(",", ids.Select(id => $"'{id}'")) + "]";
            WebMap.CoreWebView2?.ExecuteScriptAsync($"window.addRoute({arr}, '{color}')");
        }
        catch { }
    }

    public void HighlightSwitch(string id, bool on)
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync(
            $"window.highlightSwitch('{id}', {(on ? "true" : "false")})"); }
        catch { }
    }

    public void SetConsistMode(bool on)
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync(
            $"window.setConsistMode({(on ? "true" : "false")})"); }
        catch { }
    }

    public void SetSignal(string id, int state)
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync(
            $"window.setSignal('{id}', {state})"); }
        catch { }
    }

    public void LockSwitch(string id, bool locked)
    {
        try { WebMap.CoreWebView2?.ExecuteScriptAsync(
            $"window.lockSwitch('{id}', {(locked ? "true" : "false")})"); }
        catch { }
    }
}
