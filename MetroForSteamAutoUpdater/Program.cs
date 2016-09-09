using System;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using MetroForSteamAutoUpdater.Helpers;
using MetroForSteamAutoUpdater.Models;
using MetroForSteamAutoUpdater.Properties;
using Octokit;

namespace MetroForSteamAutoUpdater
{
    class Program
    {
        static void Main()
        {
            // Events
            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => Cleanup();
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;

            // Settings
            Console.Title = AppSetting.Name;

            // Package details
            Package.DownloadUrl = "http://metroforsteam.com/downloads/latest.zip";
            Package.DownloadPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(Package.DownloadUrl));
            Package.ThemeName = "Metro for Steam";

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
                    File.AppendAllText(AppSetting.LogFilename, e.ExceptionObject.ToString());
                }
                catch (Exception ex)
                {
                    exception = ex;
                    ConsoleHelper.WriteError($"ERROR: Could not write error details to file: {AppSetting.LogFilename} - Error message: {ex.Message}");
                }
                finally
                {
                    if (exception == null)
                        Console.WriteLine($"Error details written to file: {AppSetting.LogFilename}");
                }
            }

            ConsoleHelper.WriteError($"FATAL: Something went wrong! - Error details written to file: {AppSetting.LogFilename}");
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
            Steam.SkinsPath = RegistryHelper.GetSteamSkinsPath();

            if (Directory.Exists(Steam.SkinsPath))
                Console.Write($"\rFound Steam skins path at: {Steam.SkinsPath}\n");
            else
            {
                ConsoleHelper.WriteError($"ERROR: Steam skins path does not exist at {Steam.SkinsPath}\nPlease double-check your steam path!");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                Environment.Exit(1);
            }

            await CheckForAppUpdate();

            await GetPackage();

            ExtractPackage();
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
                webClient.DownloadProgressChanged += (sender, args) => Console.Write($"\rDownloading package... ({args.ProgressPercentage}%)");

                try
                {
                    await webClient.DownloadFileTaskAsync(Package.DownloadUrl, Package.DownloadPath);
                }
                catch (Exception ex)
                {
                    ConsoleHelper.WriteError($"\nERROR: Failed to download package - Error message: {ex.Message}");
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

                    Console.WriteLine($"Extracting package to: {Path.Combine(Steam.SkinsPath, Package.ThemeName)}");

                    var rootFolder = Path.GetDirectoryName(zip.Entries.First(x => x.FileName.Contains(Package.ThemeName)).FileName);

                    foreach (var entry in selection.ToList())
                    {
                        if (rootFolder != null)
                        {
                            var newName = entry.FileName.Replace(rootFolder, Package.ThemeName);
                            entry.FileName = newName;
                        }

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
                ConsoleHelper.WriteError($"ERROR: Failed to extract package - Error message: {ex.Message}");
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

        private static GitHubClient _gitHubClient;

        private static async Task CheckForAppUpdate()
        {
            Console.WriteLine($"Communicating with GitHub to check if the application needs to be updated...");

            var apiKey = ConfigurationManager.AppSettings["APIKey"];

            _gitHubClient = new GitHubClient(new ProductHeaderValue("MetroForSteamAutoUpdater"))
            {
                Credentials = new Credentials(apiKey),
            };

            // Check rate limits
            var rateLimits = await _gitHubClient.Miscellaneous.GetRateLimits();

            // Gets the TimeSpan when the rate limit resets
            var resetTime = DateTime.Parse(rateLimits.Resources.Core.Reset.ToString()) - DateTime.Now;

            // If the rate limit has exceeded, return
            if (rateLimits.Resources.Core.Remaining < 1)
            {
                Console.WriteLine($"Unable to check for application updates -- GitHub API request rate limit exceeded, will reset in {resetTime.Minutes} minutes");
                return;
            }

            // Get releases
            var releases = await _gitHubClient.Repository.Release.GetAll("Hertizch", "MetroForSteamAutoUpdater");

            // Get latest release
            if (releases != null)
            {
                var latestRelease = releases.First();

                // Get current version
                var assembly = Assembly.GetExecutingAssembly();
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                var currentVersion = Version.Parse(fileVersionInfo.ProductVersion);

                // Get latest version
                Version latestVersion;
                Version.TryParse(latestRelease.TagName.Replace("v", ""), out latestVersion);

                // Compare version to determine if update is needed
                if (latestVersion > currentVersion)
                {
                    Console.WriteLine($"A newer version ({latestVersion}) of the application has been found -- starting update...");

                    var browserDownloadUrl = latestRelease.Assets.First().BrowserDownloadUrl;

                    if (browserDownloadUrl != null)
                        await DownloadAppPackage(browserDownloadUrl, latestVersion.ToString());

                    if (browserDownloadUrl != null)
                        RunUpdateScript(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(browserDownloadUrl)));
                }
                else
                {
                    Console.WriteLine($"No application updates found -- continuing...");
                }
            }
            else
            {
                Console.WriteLine($"No releases found");
            }
        }

        private static async Task DownloadAppPackage(string browserDownloadUrl, string latestVersion)
        {
            // Create the webclient
            var webClient = new WebClient
            {
                Proxy = null
            };

            // Add fake user agent (reqired by github api)
            webClient.Headers.Add("user-agent", AppSetting.Name);

            double progressPercentage;

            // Progress changed event
            webClient.DownloadProgressChanged += (sender, args) =>
            {
                progressPercentage = args.ProgressPercentage;

                if (progressPercentage.ToString(CultureInfo.InvariantCulture).Contains("-") || Math.Abs(progressPercentage) < 0.001)
                    progressPercentage = ((double)args.BytesReceived / args.TotalBytesToReceive) * 100;

                Console.Write($"\rDownloading: {progressPercentage}% ({args.BytesReceived} bytes of {args.TotalBytesToReceive} bytes)");
            };

            // Download complete event
            webClient.DownloadFileCompleted += (sender, args) =>
            {
                Console.WriteLine(args.Error == null
                    ? $"\nDownload complete without error!"
                    : $"\nDownload failed: {args.Error.Message}");
            };

            // Execute download
            if (browserDownloadUrl != null)
                await webClient.DownloadFileTaskAsync(browserDownloadUrl, $"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(browserDownloadUrl))}");
        }

        private static void RunUpdateScript(string newFilename)
        {
            var scriptFilename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Path.GetFileName(Path.GetTempFileName() + ".bat"));

            var sb = new StringBuilder();

            sb.AppendLine("timeout /t 2 /nobreak");

            if (AppSetting.Name != null)
                sb.AppendLine($"del \"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSetting.Name + ".exe")}\"");

            sb.AppendLine("timeout /t 2 /nobreak");
            sb.AppendLine($"ren \"{newFilename}\" \"{AppSetting.Name + ".exe"}\"");
            sb.AppendLine("timeout /t 2 /nobreak");
            sb.AppendLine($"start \"\" \"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppSetting.Name + ".exe")}\"");
            sb.AppendLine("exit");

            File.WriteAllText(scriptFilename, sb.ToString());

            Process.Start(scriptFilename);

            Environment.Exit(2);
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
                ConsoleHelper.WriteError($"ERROR: Failed to delete file: {Package.DownloadPath} - Error message: {ex.Message}");
            }
            finally
            {
                if (exception == null)
                    Console.WriteLine($"Deleted temporary file: {Package.DownloadPath}");
            }
        }
    }
}
