using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using PD2Launcherv2.Utils;
using PD2Launcherv2.Utils.Gl;
using PD2Shared.Logging;
using static PD2Shared.Logging.LoggingStatic;
using PD2Shared.Utils;
using PD2Shared.Utils.PInvoke;

namespace PD2Launcherv2
{
    internal static class SanityChecks
    {
        private static bool FailurePrompt(string message)
        {
            if (MsgBox.Warn(
                message + "\n" +
                "\n" +
                "Continue regardless?",
                MessageBoxButton.YesNo,
                MessageBoxResult.No) == MessageBoxResult.No)
            {
                return false;
            }

            L.CallerWarning("User ignored sanity check failure.");
            return true;
        }

        private static bool FailurePromptEx(Exception exception, string message)
        {
            if (MsgBox.Exception(
                exception,
                message + "\n" +
                "\n" +
                "Continue regardless?",
                MessageBoxImage.Warning,
                MessageBoxButton.YesNo,
                MessageBoxResult.No) == MessageBoxResult.No)
            {
                return false;
            }

            L.CallerWarning("User ignored sanity check failure.");
            return true;
        }

        private static bool CheckGameDirPathEncoding()
        {
            string gameDirPath = Env.GetCwd();

            var encodingAltNames = new string[] { Env.AnsiEncoding.WebName, Env.AnsiEncoding.BodyName, Env.AnsiEncoding.HeaderName }
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .Select(n => $"'{n}'");
            string encodingDisplayName = $"'{Env.AnsiEncoding.EncodingName}' (aka {string.Join(", ", encodingAltNames)})";

            string roundTrip = Env.AnsiEncoding.GetString(Encoding.Convert(Encoding.Unicode, Env.AnsiEncoding, Encoding.Unicode.GetBytes(gameDirPath)));

            if (roundTrip != gameDirPath)
            {
                L.CallerWarning($"Game directory '{gameDirPath}' cannot be represented in system-default ANSI encoding: {encodingDisplayName}.");

                if (!FailurePrompt(
                    $"Game directory '{gameDirPath}' cannot be represented in system-default ANSI encoding: {encodingDisplayName}.\n" +
                    "\n" +
                    $"Problematic characters: '{new string(gameDirPath.Except(roundTrip).Distinct().ToArray())}'.\n" +
                    "\n" +
                    "This will likely cause PD2 to crash."))
                {
                    return false;
                }
            }
            else
            {
                L.CallerInformation($"Game directory '{gameDirPath}' can be represented in system-default ANSI encoding: {encodingDisplayName}.");
            }

            return true;
        }

        private static bool CheckIfDirectoriesWritable()
        {
            {
                string dirPath = Env.ProcessDirPath;

                var ex = Env.CheckIfDirectoryIsWritable(dirPath);

                if (ex != null)
                {
                    L.CallerWarning(ex, $"Launcher directory '{dirPath}' is not writable.");

                    if (!FailurePrompt(
                        $"Launcher directory '{dirPath}' is not writable.\n" +
                        "\n" +
                        "This can lead to unexpected issues."))
                    {
                        return false;
                    }
                }
                else
                {
                    L.CallerInformation($"Launcher directory '{dirPath}' is writable.");
                }
            }

            {
                string dirPath = Env.GetCwd();

                var ex = Env.CheckIfDirectoryIsWritable(dirPath);

                if (ex != null)
                {
                    L.CallerWarning(ex, $"Launcher working directory '{dirPath}' is not writable.");

                    if (!FailurePrompt(
                        $"Launcher working directory '{dirPath}' is not writable.\n" +
                        "\n" +
                        "This can lead to unexpected issues."))
                    {
                        return false;
                    }
                }
                else
                {
                    L.CallerInformation($"Launcher working directory '{dirPath}' is writable.");
                }
            }

            return true;
        }

        private static bool CheckMsvc2019Runtime()
        {
            const string DllFileName = "msvcp140.dll";
            Version DllMinVersion = new(14, 20); // Visual C++ 14.20 released alongside first Visual Studio 2019 (version 16.0.0)

            IntPtr hModule = IntPtr.Zero;
            Version? dllVersion = null;

            try
            {
                hModule = Kernel32.LoadLibrary(DllFileName);

                if (hModule == IntPtr.Zero)
                {
                    L.CallerWarning(Win32.GetLastErrorMessage(nameof(Kernel32.LoadLibrary), DllFileName));
                }
                else
                {
                    string? modulePath = Kernel32.GetModuleFileName(hModule);

                    if (modulePath == null)
                    {
                        L.CallerWarning(Win32.GetLastErrorMessage(nameof(Kernel32.GetModuleFileName)));
                    }
                    else
                    {
                        try
                        {
                            var vi = FileVersionInfo.GetVersionInfo(modulePath);

                            if (vi.ProductVersion == null)
                            {
                                L.CallerWarning($"{modulePath} contains no version information");
                            }
                            else
                            {
                                dllVersion = new Version(vi.ProductMajorPart, vi.ProductMinorPart, vi.ProductBuildPart, vi.ProductPrivatePart);
                            }
                        }
                        catch (FileNotFoundException ex)
                        {
                            L.CallerWarning(ex, $"{nameof(FileVersionInfo.GetVersionInfo)}() failed for '{modulePath}'.");
                        }
                    }
                }

                L.CallerWrite(
                    dllVersion == null ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Information,
                    $"{DllFileName} version: {(dllVersion == null ? "Unknown" : dllVersion)}"
                );
            }
            finally
            {
                if (hModule != IntPtr.Zero)
                {
                    if (!Kernel32.FreeLibrary(hModule))
                    {
                        L.CallerWarning(Win32.GetLastErrorMessage(nameof(Kernel32.FreeLibrary)));
                    }
                }
            }

            if (dllVersion >= DllMinVersion)
            {
                L.CallerInformation($"MSVC runtime meets version requirement of >= {DllMinVersion}");
                return true;
            }
            else
            {
                L.CallerWarning($"MSVC runtime does not meet version requirement of >= {DllMinVersion}");
            }

            return FailurePrompt(
                $"x86 MSVC 2019 runtime does not seem to be present.\n" +
                "\n" +
                "This will render PD2 unable to run.");
        }

        private static bool CheckD2RegistrySettings()
        {
            // Check whether D2's 'InstallPath' registry value is correct, i.e. it resolves to the expected base D2 directory path.
            // Incorrect 'InstallPath', yet pointing to a valid (existing) directory, will lead PD2 astray resulting in failure to load base MPQs.
            // Offer to remove the registry value if it's deemed incorrect as D2 will always recreate it if it's invalid.

            const string HkcuD2Path = @"Software\Blizzard Entertainment\Diablo II";
            const string InstallPathValueName = "InstallPath";

            L.CallerInformation($"Opening registry key: '{Registry.CurrentUser}\\{HkcuD2Path}'...");

            RegistryKey registryKey;

            try
            {
                registryKey = Registry.CurrentUser.CreateSubKey(HkcuD2Path, writable: true);
            }
            catch (Exception ex)
            {
                return FailurePromptEx(
                    ex,
                    $"Unable to access Diablo II registry key: '{Registry.CurrentUser}\\{HkcuD2Path}'.\n" +
                    "\n" +
                    "If unchecked, PD2 might fail to load base MPQs.");
            }

            using (registryKey)
            {
                if (registryKey == null)
                {
                    L.CallerWarning($"Unable to create registry key: '{Registry.CurrentUser}\\{HkcuD2Path}'.");

                    // The key could not be created, which is acceptable as long as it doesn't contain incorrect values.
                    return true;
                }

                var installPath = (string?)registryKey.GetValue(InstallPathValueName);

                if (installPath == null)
                {
                    L.CallerWarning($"Registry value '{InstallPathValueName}' not found.");

                    // The value does not exist, which is acceptable.
                    return true;
                }

                string installPathNormalized;

                try
                {
                    installPathNormalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installPath));
                }
                catch (Exception ex)
                {
                    L.CallerError(ex, "Path.GetFullPath() threw.");

                    installPathNormalized = installPath;
                }

                string ExpectedPath = Env.GetBaseGameDirPath();

                if (installPathNormalized != ExpectedPath)
                {
                    // The path must be valid (pointing to an existing directory), yet incorrect, to trip up PD2.
                    if (Directory.Exists(installPathNormalized))
                    {
                        L.CallerWarning($"Registry value '{InstallPathValueName}' does not match D2's expected path: '{ExpectedPath}' != '{installPath}' (actual).");
                        L.CallerDebug("Prompting the user...");

                        if (MsgBox.Warn(
                            $"Diablo II's registry value '{InstallPathValueName}' does not match Diablo II's actual path.\n" +
                            "This can lead to PD2 failing to load base MPQs.\n" +
                            "\n" +
                            "Attempt to remove the offending value?",
                            MessageBoxButton.YesNo,
                            MessageBoxResult.Yes) == MessageBoxResult.No)
                        {
                            L.CallerWarning("User declined to remove the offending value.");

                            return true;
                        }

                        L.CallerInformation($"Removing '{InstallPathValueName}'...");
                        registryKey.DeleteValue(InstallPathValueName);

                        return true;
                    }
                    else
                    {
                        L.CallerWarning($"Registry value '{InstallPathValueName}' is an invalid path and will be ignored: '{installPath}'.");

                        return true;
                    }
                }
                else
                {
                    L.CallerInformation($"Registry value '{InstallPathValueName}' matches D2's expected path.");

                    return true;
                }
            }
        }

        private static bool TestGlContext()
        {
            using LoggedRoutine loggedRoutine = new();

            const string FlagFileName = "flag.SanityCheck.GL";
            string FlagFilePath = Path.Combine(Env.GetLauncherFilesRootDirPath(), FlagFileName);

            // Check for an existing flag file
            if (File.Exists(FlagFilePath))
            {
                L.CallerWarning($"Found an existing flag file: '{FlagFilePath}'.");

                if (MsgBox.Warn(
                    "Launcher has detected to have previously crashed while attempting to test GL context creation.\n" +
                    "\n" +
                    "Would you like to rerun the test as normal or skip running it this time?",
                    MessageBoxButton.YesNo,
                    MessageBoxResult.Yes) == MessageBoxResult.Yes)
                {
                    L.CallerDebug("User agreed to rerun the test -- attempting to remove the flag file...");

                    // Let this throw on failure
                    File.Delete(FlagFilePath);
                }
                else
                {
                    L.CallerWarning("User declined to rerun the test -- leaving the flags file intact.");

                    return true;
                }
            }

            // Create a flag file. (Let these throw on failure).
            {
                L.CallerDebug($"Attempting to create a flag file: '{FlagFilePath}'...");

                Env.EnsureDirectoryExists(FlagFilePath);

                using var stream = new FileStream(
                    FlagFilePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 0,
                    FileOptions.WriteThrough);
            }

            GlTest.CreateBestCtx();

            // ...and remove the flag file in the end. (This should not fail).
            File.Delete(FlagFilePath);

            // This is too early to complain about any issues with GL.
            // Evaluate GlTest.BestCtx in light of current launcher options in MainWindow.
            return true;
        }

        public static bool Run()
        {
            List<Func<bool>> sanityChecks = new()
            {
                () => CheckGameDirPathEncoding(),
                () => CheckIfDirectoriesWritable(),
                () => CheckMsvc2019Runtime(),
                () => CheckD2RegistrySettings(),
                () => TestGlContext()
            };

            using LoggedScope loggedScope = new($"Running {sanityChecks.Count} sanity check(s)...");

            for (int i = 0; i < sanityChecks.Count; ++i)
            {
                var check = sanityChecks[i];

                L.CallerInformation($"> {i + 1}/{sanityChecks.Count}");

                if (!check())
                {
                    return false;
                }
            }

            return true;
        }
    }
}
