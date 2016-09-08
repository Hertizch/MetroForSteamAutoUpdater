using System.IO;
using Microsoft.Win32;

namespace MetroForSteamAutoUpdater.Helpers
{
    public static class RegistryHelper
    {
        /// <summary>
        /// Gets the Steam skins path from the registry
        /// </summary>
        /// <returns></returns>
        public static string GetSteamSkinsPath()
        {
            string path = null;

            using (var registryKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Valve\\Steam"))
            {
                var value = registryKey?.GetValue("SteamPath");
                if (value != null)
                    path = Path.Combine(value.ToString().Replace(@"/", @"\"), "skins");
            }

            return path;
        }
    }
}
