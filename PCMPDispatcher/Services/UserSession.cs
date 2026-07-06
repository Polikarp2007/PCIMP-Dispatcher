namespace PCMPDispatcher.Services;

/// <summary>
/// Holds the signed-in user's identity for the current app run, so any
/// dashboard page can show "Welcome, &lt;name&gt;" without re-fetching.
/// Populated by LoginView once the server confirms the session.
/// </summary>
public static class UserSession
{
    /// <summary>Full name shown in the UI (e.g. "Polikarp Kravchenko"). Falls back to "Player".</summary>
    public static string DisplayName    { get; private set; } = "Player";
    public static string Username       { get; private set; } = "";
    public static string Email          { get; private set; } = "";
    public static string SteamNickname  { get; private set; } = "";
    public static string SteamAvatarUrl { get; private set; } = "";
    public static string Token          { get; private set; } = "";
    public static string Hwid           { get; private set; } = "";

    /// <summary>Name shown in UI — Steam nickname if available, else WP display name.</summary>
    public static string VisibleName => !string.IsNullOrWhiteSpace(SteamNickname) ? SteamNickname : DisplayName;

    public static void Set(string? display, string? username = null, string? email = null)
    {
        if (!string.IsNullOrWhiteSpace(display)) DisplayName = display.Trim();
        if (!string.IsNullOrWhiteSpace(username)) Username = username.Trim();
        if (!string.IsNullOrWhiteSpace(email))    Email    = email.Trim();
    }

    public static void SetSteam(string? nickname, string? avatarUrl)
    {
        if (!string.IsNullOrWhiteSpace(nickname))  SteamNickname  = nickname.Trim();
        if (!string.IsNullOrWhiteSpace(avatarUrl)) SteamAvatarUrl = avatarUrl.Trim();
    }

    /// <summary>Stores the signed token + machine id so the launcher can call /mp/* on the user's behalf.</summary>
    public static void SetAuth(string? token, string? hwid)
    {
        if (!string.IsNullOrWhiteSpace(token)) Token = token.Trim();
        if (!string.IsNullOrWhiteSpace(hwid))  Hwid  = hwid.Trim();
    }
}
