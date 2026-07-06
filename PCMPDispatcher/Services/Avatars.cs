using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PCMPDispatcher.Services;

public static class Avatars
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static string Initials(string name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
        return (parts[0].Substring(0, 1) + parts[^1].Substring(0, 1)).ToUpperInvariant();
    }

    /// <summary>Show initials immediately, then replace with Steam avatar once downloaded.</summary>
    public static void Apply(Image img, TextBlock initials)
    {
        // Show initials right away as placeholder
        initials.Text = Initials(UserSession.VisibleName);
        initials.Visibility = Visibility.Visible;
        img.Visibility = Visibility.Collapsed;

        string url = UserSession.SteamAvatarUrl;
        if (!string.IsNullOrWhiteSpace(url))
            _ = LoadAvatarAsync(img, initials, url);
    }

    private static async Task LoadAvatarAsync(Image img, TextBlock initials, string url)
    {
        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();

            // Resume is on UI SynchronizationContext
            img.Source = bmp;
            img.Visibility = Visibility.Visible;
            initials.Visibility = Visibility.Collapsed;
        }
        catch { /* keep initials */ }
    }
}
