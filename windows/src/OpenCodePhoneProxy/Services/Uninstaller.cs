using System.Diagnostics;
using Microsoft.Win32;

namespace OpenCodePhoneProxy.Services;

public static class Uninstaller
{
    public static bool Run(Logger log, string installDirectory, bool keepSettings)
    {
        var script = Path.Combine(installDirectory, "uninstall.ps1");
        if (File.Exists(script))
        {
            try
            {
                var arguments =
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                    "\" -InstallDir \"" + installDirectory + "\"";
                if (keepSettings)
                {
                    arguments += " -KeepSettings";
                }

                var info = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = true,
                    WorkingDirectory = installDirectory,
                    Arguments = arguments
                };

                if (!CanWrite(installDirectory))
                {
                    info.Verb = "runas";
                }

                Process.Start(info)?.Dispose();
                log.Info("Started uninstall.ps1.");
                return true;
            }
            catch (Exception ex)
            {
                log.Warn("Could not start uninstall.ps1: " + ex.Message);
            }
        }

        log.Info("uninstall.ps1 was not found; using the built-in fallback.");
        AutostartManager.Set(false, "");
        RemoveShortcuts();
        RemoveRunKey();

        if (!keepSettings)
        {
            try
            {
                var data = new SettingsStore().DataDirectory;
                if (Directory.Exists(data))
                {
                    Directory.Delete(data, recursive: true);
                }
            }
            catch (Exception ex)
            {
                log.Warn("Could not delete app data: " + ex.Message);
            }
        }

        ScheduleSelfDelete(installDirectory);
        return true;
    }

    public static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            key?.DeleteValue("OpenCodePhoneProxy", throwOnMissingValue: false);
        }
        catch
        {
            // Best effort.
        }
    }

    public static void RemoveShortcuts()
    {
        var shortcuts = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs",
                "OpenCode Phone Proxy.lnk"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "OpenCode Phone Proxy.lnk")
        };

        foreach (var shortcut in shortcuts)
        {
            try
            {
                if (File.Exists(shortcut))
                {
                    File.Delete(shortcut);
                }
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ScheduleSelfDelete(string installDirectory)
    {
        try
        {
            var batch = Path.Combine(
                Path.GetTempPath(),
                "ocpp-uninstall-" + Guid.NewGuid().ToString("N") + ".cmd");

            var contents =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                "rmdir /s /q \"" + installDirectory + "\"\r\n" +
                "del /f /q \"%~f0\" >nul 2>&1\r\n";

            File.WriteAllText(batch, contents);

            var info = new ProcessStartInfo("cmd.exe")
            {
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "/c \"" + batch + "\""
            };
            Process.Start(info)?.Dispose();
        }
        catch
        {
            // The user can delete the folder manually.
        }
    }
}
