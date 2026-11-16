using System.Diagnostics;

namespace ShaderCacheCleaner.Helpers;

public static class ExternalLinks
{
    public const string SteamProfile = "https://steamcommunity.com/id/SyokosGaming/";
    public const string YouTubeChannel = "https://www.youtube.com/@Syokos?sub_confirmation=1";

    /// <summary>
    /// URL Discord configurable (%LocalAppData%\ShaderCacheCleaner\discord-url.txt).
    /// Par défaut : page Discord web (aucun ID inventé).
    /// </summary>
    public static string DiscordProfileUrl
    {
        get
        {
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShaderCacheCleaner",
                    "discord-url.txt");
                if (File.Exists(path))
                {
                    var url = File.ReadAllText(path).Trim();
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        return url;
                }
            }
            catch
            {
                // ignore
            }

            // Pas d'identifiant numérique inventé — page Discord accessible sans client.
            return "https://discord.com/";
        }
    }

    public static void OpenSteam() => OpenUrl(SteamProfile);
    public static void OpenYouTube() => OpenUrl(YouTubeChannel);
    public static void OpenDiscord() => OpenUrl(DiscordProfileUrl);

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
