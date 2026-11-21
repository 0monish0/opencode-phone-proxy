using System.Text;
using System.Text.Json;
using OpenCodePhoneProxy.Models;
using OpenCodePhoneProxy.Services;
using OpenCodePhoneProxy.UI;

namespace OpenCodePhoneProxy;

internal static class Program
{
    public const string Version = "1.1.0";

    [STAThread]
    private static int Main(string[] args)
    {
        if (HasFlag(args, "--version"))
        {
            ConsoleBridge.Attach();
            Console.WriteLine(Version);
            return 0;
        }

        if (HasFlag(args, "--selftest"))
        {
            ConsoleBridge.Attach();
            return SelfTest.Run();
        }

        if (args.Length >= 1 && args[0].Equals("--elevated-route-apply", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedRouteCommand.RunApply(args);
        }

        if (args.Length >= 1 && args[0].Equals("--elevated-route-restore", StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedRouteCommand.RunRestore(args);
        }

        if (HasFlag(args, "--restore-route"))
        {
            ConsoleBridge.Attach();
            try
            {
                return RestoreRouteCommand.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not restore the route: " + ex.Message);
                return 1;
            }
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ReportFatal(e.Exception);
        };

        var startHidden = HasFlag(args, "--tray");
        using var context = new TrayApp(startHidden);
        if (!context.StartupCanceled)
        {
            Application.Run(context);
        }

        return 0;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static void ReportFatal(Exception? exception)
    {
        try
        {
            var logger = AppServices.Logger;
            logger?.Error("Unhandled exception: " + exception);
        }
        catch
        {
            // Never let the reporter crash the process.
        }

        MessageBox.Show(
            "OpenCode Phone Proxy hit an unexpected error.\n\n" +
            (exception?.Message ?? "Unknown error") +
            "\n\nSee the log from the tray menu for details.",
            "OpenCode Phone Proxy",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}

internal static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    public static void Attach()
    {
        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch
        {
            // No console to attach to; file logging still works.
        }
    }
}

internal static class ElevatedRouteCommand
{
    public static int RunApply(string[] args)
    {
        if (args.Length < 3)
        {
            return 2;
        }

        return Run(args, apply: true);
    }

    public static int RunRestore(string[] args)
    {
        if (args.Length < 3)
        {
            return 2;
        }

        return Run(args, apply: false);
    }

    private static int Run(string[] args, bool apply)
    {
        var resultPath = args[2];
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));
            var snapshot = JsonSerializer.Deserialize<RouteSnapshot>(json);
            if (snapshot is null)
            {
                WriteResult(resultPath, "FAILED: invalid route snapshot");
                return 2;
            }

            var logger = new Logger(Path.Combine(Path.GetTempPath(), "OpenCodePhoneProxy"));
            var guard = new NetworkRouteGuard(logger);
            var result = apply ? guard.ApplyDirect(snapshot) : guard.RestoreDirect(snapshot);
            WriteResult(resultPath, result == RouteChangeResult.Applied
                ? "APPLIED"
                : "FAILED: Windows rejected the interface metric change");
            return result == RouteChangeResult.Applied ? 0 : 1;
        }
        catch (Exception ex)
        {
            WriteResult(resultPath, "FAILED: " + ex.Message);
            return 1;
        }
    }

    private static void WriteResult(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, message);
        }
        catch
        {
            // The parent process will time out and report a failure.
        }
    }
}

internal static class RestoreRouteCommand
{
    public static int Run()
    {
        var store = new SettingsStore();
        var logger = new Logger(store.DataDirectory);
        var settings = store.Load();
        if (settings.PendingRoute is null)
        {
            Console.WriteLine("No pending route change.");
            return 0;
        }

        var guard = new NetworkRouteGuard(logger);
        var result = guard.Restore(settings.PendingRoute);
        if (result == RouteChangeResult.Applied)
        {
            settings.PendingRoute = null;
            store.Save(settings);
            Console.WriteLine("Route metric restored.");
            return 0;
        }

        if (result == RouteChangeResult.Declined)
        {
            Console.WriteLine("Administrator approval was declined; the route was not restored.");
        }
        else
        {
            Console.WriteLine("Could not restore the route metric.");
        }

        return 1;
    }
}
