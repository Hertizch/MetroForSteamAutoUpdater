using System;

namespace MetroForSteamAutoUpdater.Helpers
{
    public static class ConsoleHelper
    {
        /// <summary>
        /// Write errors to console
        /// </summary>
        /// <param name="value">Value to write</param>
        public static void WriteError(string value)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{value}");
            Console.ResetColor();
        }
    }
}
