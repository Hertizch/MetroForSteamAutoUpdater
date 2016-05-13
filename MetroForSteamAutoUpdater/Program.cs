using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ionic.Zip;
using MetroForSteamAutoUpdater.Models;
using Microsoft.Win32;

namespace MetroForSteamAutoUpdater
{
    class Program
    {
        static void Main(string[] args)
        {
            // Events
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => Cleanup();
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;

            // Settings
            Console.Title = AppSetting.Name;

            // Main
            Execute();

            Console.ReadKey();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = null;

            if (e.ExceptionObject != null)
            {
                try
                {
                    File.WriteAllText(AppSetting.LogFilename, e.ExceptionObject.ToString());
                }
                catch (Exception ex)
                {
                    exception = ex;
                    WriteErrorToConsole($"ERROR: Could not write error details to file: {AppSetting.LogFilename} - Error message: {ex.Message}");
                }
                finally
                {
                    if (exception == null)
                        Console.WriteLine($"Error details written to file: {AppSetting.LogFilename}");
                }
            }

            WriteErrorToConsole($"FATAL: Something went wrong! - Error details written to file: {AppSetting.LogFilename}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(1);
        }

        /// <summary>
        /// Executes all methods
        /// </summary>
        private static async void Execute()
        {
            Console.Write("Attempting to find Steam skins path...");
            Steam.SkinsPath = GetSteamSkinsPath();

            if (Directory.Exists(Steam.SkinsPath))
                Console.Write($"\rFound Steam skins path at: {Steam.SkinsPath}\n");
            else
            {
                WriteErrorToConsole($"ERROR: Steam skins path does not exist at {Steam.SkinsPath}\nPlease double-check your steam path!");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }

            await GetPackageDetails();

            await GetPackage();

            Console.WriteLine("Extracting package...");
            ExtractPackage();
        }

        /// <summary>
        /// Gets the package details from metroforsteam.com source code.
        /// </summary>
        /// <returns></returns>
        private static async Task GetPackageDetails()
        {
            string source;
            Exception exception = null;

            // HTML source download client
            using (var webClient = new WebClient())
            {
                webClient.Proxy = null;
                webClient.DownloadProgressChanged += (sender, args) => Console.Write($"\rScraping metroforsteam.com... ({args.ProgressPercentage}%)");

                try
                {
                    source = await webClient.DownloadStringTaskAsync("http://metroforsteam.com/");
                }
                catch (Exception ex)
                {
                    exception = ex;
                    WriteErrorToConsole($"\nERROR: Failed to scrape metroforsteam.com - Error message: {ex.Message}");
                    return;
                }
                finally
                {
                    if (exception == null)
                        Console.Write("\rScraping of metroforsteam.com complete!\n");
                }
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                // HTML source search pattern string
                var match = Regex.Match(source,
                    "<a class=\"button\" href=\"http://metroforsteam.com/downloads/(.*).zip\">Download</a>");

                // If source pattern found
                if (match.Success)
                {
                    Package.Version = match.Groups[1].Value;
                    Package.DownloadUrl = $"http://metroforsteam.com/downloads/{Package.Version}.zip";
                    Package.DownloadPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(Package.DownloadUrl));

                    Console.WriteLine("Data recieved:");
                    Console.WriteLine($"Latest version: {Package.Version}");
                    Console.WriteLine($"Download url: {Package.DownloadUrl}");
                    Console.WriteLine($"Temporary download path: {Package.DownloadPath}");
                    Console.WriteLine($"Extract to: {Steam.SkinsPath}");
                }
                else
                {
                    WriteErrorToConsole("ERROR: GetPackageDetails() - match.Success is false.");
                }
            }
            else
            {
                WriteErrorToConsole("ERROR: GetPackageDetails() - (string) source is null.");
            }
        }

        /// <summary>
        /// Downloads the update package from metroforsteam.com
        /// </summary>
        /// <returns></returns>
        private static async Task GetPackage()
        {
            if (Package.DownloadUrl == null)
                return;

            // Package download client
            using (var webClient = new WebClient())
            {
                webClient.Proxy = null;
                webClient.DownloadProgressChanged += (sender, args) => Console.Write($"\rDownloading package - {args.ProgressPercentage}% ...");
                webClient.DownloadFileCompleted += (sender, args) =>
                {
                    if (!args.Cancelled && args.Error == null)
                        Console.Write("\rDownload of package complete!  ");
                };

                try
                {
                    await webClient.DownloadFileTaskAsync(Package.DownloadUrl, Package.DownloadPath);
                }
                catch (Exception ex)
                {
                    WriteErrorToConsole($"\nERROR: Failed to download package - Error message: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine("");
                }
            }
        }

        /// <summary>
        /// Extracts the downloaded package to Steam skins folder
        /// </summary>
        private static void ExtractPackage()
        {
            var filesExtracted = 0;
            var foldersExtracted = 0;
            var skippedEntries = 0;
            Exception exception = null;

            try
            {
                using (var zip = ZipFile.Read(Package.DownloadPath))
                {
                    // Get only the files we need
                    var selection = (from entry in zip.Entries where (entry.FileName).StartsWith(Package.ThemeName) select entry);

                    // Create base directory if it does not exist
                    if (!Directory.Exists(Path.Combine(Steam.SkinsPath, Package.ThemeName)))
                    {
                        Directory.CreateDirectory(Path.Combine(Steam.SkinsPath, Package.ThemeName));
                        Console.WriteLine($"Created directory: {Path.Combine(Steam.SkinsPath, Package.ThemeName)}");
                    }

                    foreach (var entry in selection)
                    {
                        // Do not overwrite custom.styles (if it exists) unless package contains newer version
                        if (entry.FileName.Contains("custom.styles") &&
                            File.Exists(Path.Combine(Steam.SkinsPath, entry.FileName)) &&
                            entry.LastModified <= File.GetLastWriteTime(Path.Combine(Steam.SkinsPath, entry.FileName)))
                        {
                            Console.WriteLine($"Skipped file: {entry.FileName} - Newer version already exists.");
                            skippedEntries++;
                            continue;
                        }

                        // Extract contents
                        entry.Extract(Steam.SkinsPath, ExtractExistingFileAction.OverwriteSilently);
                        Console.WriteLine($"Extracted: {entry.FileName}");

                        // Count stuff...
                        if (entry.IsDirectory)
                            foldersExtracted++;
                        else
                            filesExtracted++;
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
                WriteErrorToConsole($"ERROR: Failed to extract package - Error message: {ex.Message}");
            }
            finally
            {
                if (exception == null)
                {
                    Console.WriteLine($"Package extraced - ({filesExtracted} file(s) - {foldersExtracted} folder(s) - {skippedEntries} skipped file(s)).");

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("\nCOMPLETE! - You need to restart Steam for the skin to update!");
                    Console.ResetColor();

                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
        }

        /// <summary>
        /// Gets the Steam skins path from the registry
        /// </summary>
        /// <returns></returns>
        private static string GetSteamSkinsPath()
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

        /// <summary>
        /// Cleans up the temporary downloaded package zip file
        /// </summary>
        private static void Cleanup()
        {
            if (!File.Exists(Package.DownloadPath)) return;

            Exception exception = null;

            try
            {
                File.Delete(Package.DownloadPath);
            }
            catch (Exception ex)
            {
                exception = ex;
                WriteErrorToConsole($"ERROR: Failed to delete file: {Package.DownloadPath} - Error message: {ex.Message}");
            }
            finally
            {
                if (exception == null)
                    Console.WriteLine($"Deleted temporary file: {Package.DownloadPath}");
            }
        }

        /// <summary>
        /// Write errors to console
        /// </summary>
        /// <param name="value">Value to write</param>
        private static void WriteErrorToConsole(string value)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{value}");
            Console.ResetColor();
        }
    }
}
