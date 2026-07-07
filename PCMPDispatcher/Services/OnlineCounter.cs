using System;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace PCMPDispatcher.Services;

/// <summary>
/// Периодически запрашивает /trains/online и публикует актуальное количество
/// игроков. Подписчики (сайдбары всех страниц) обновляют свой TextBlock.
/// Один таймер на всё приложение; всё на UI-потоке.
/// </summary>
public static class OnlineCounter
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly DispatcherTimer _timer = new()
        { Interval = TimeSpan.FromSeconds(30) };

    private static int _online = -1;
    private static int _max    = 40;

    /// <summary>Вызывается когда пришёл свежий ответ. Аргумент = "X / 40".</summary>
    public static event Action<string>? Updated;

    static OnlineCounter()
    {
        _timer.Tick += async (_, _) => await FetchAsync();
    }

    /// <summary>Запустить фоновый опрос и немедленно сделать первый запрос.</summary>
    public static async void Start()
    {
        if (!_timer.IsEnabled) _timer.Start();
        await FetchAsync();
    }

    /// <summary>Текущее отображаемое значение (кэш).</summary>
    public static string DisplayText
        => _online < 0 ? "… / 40" : $"{_online} / {_max}";

    private static async System.Threading.Tasks.Task FetchAsync()
    {
        try
        {
            var json  = await _http.GetStringAsync("https://auth.poli-co.com/trains/online");
            var doc   = JsonDocument.Parse(json).RootElement;
            if (doc.TryGetProperty("online", out var o) && doc.TryGetProperty("max", out var m))
            {
                _online = o.GetInt32();
                _max    = m.GetInt32();
                Updated?.Invoke(DisplayText);
            }
        }
        catch { /* нет сети или сервер временно недоступен — не страшно */ }
    }
}
