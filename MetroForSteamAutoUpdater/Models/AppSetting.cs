using System.Reflection;

namespace MetroForSteamAutoUpdater.Models
{
    public static class AppSetting
    {
        public static string Name { get; set; } = Assembly.GetExecutingAssembly().GetName().Name;

        public static string LogFilename { get; set; } = $"{Assembly.GetExecutingAssembly().GetName().Name}.log";
    }
}
