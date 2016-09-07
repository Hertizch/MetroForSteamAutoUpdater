using System.IO;

namespace MetroForSteamAutoUpdater.Models
{
    public static class Package
    {
        public static string DownloadUrl { get; set; } = "http://metroforsteam.com/downloads/latest.zip";

        public static string DownloadPath { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetFileName("http://metroforsteam.com/downloads/latest.zip"));

        public static string ThemeName { get; set; } = "Metro for Steam";
    }
}
