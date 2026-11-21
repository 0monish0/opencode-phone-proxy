using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public sealed class NetworkRouteGuard
{
    private readonly Logger _log;

    public NetworkRouteGuard(Logger log)
    {
        _log = log;
    }

    public RouteSnapshot? Capture(int interfaceIndex, string adapterName)
    {
        var v4 = ReadMetric("IPv4", interfaceIndex);
        var v6 = ReadMetric("IPv6", interfaceIndex);
        if (v4 is null && v6 is null)
        {
            _log.Warn($"Could not read interface metrics for index {interfaceIndex}.");
            return null;
        }

        return new RouteSnapshot
        {
            InterfaceIndex = interfaceIndex,
            AdapterName = adapterName,
            V4Metric = v4?.Metric,
            V4Automatic = v4?.Automatic ?? false,
            V6Metric = v6?.Metric,
            V6Automatic = v6?.Automatic ?? false
        };
    }

    public RouteChangeResult ApplyHighMetric(RouteSnapshot snapshot)
    {
        if (Elevation.IsAdministrator)
        {
            return ApplyDirect(snapshot);
        }

        return ViaElevated("--elevated-route-apply", snapshot);
    }

    public RouteChangeResult Restore(RouteSnapshot snapshot)
    {
        if (Elevation.IsAdministrator)
        {
            return RestoreDirect(snapshot);
        }

        return ViaElevated("--elevated-route-restore", snapshot);
    }

    public RouteChangeResult ApplyDirect(RouteSnapshot snapshot)
    {
        var v4 = SetMetric("IPv4", snapshot.InterfaceIndex, 9999);
        var v6 = !snapshot.V6Metric.HasValue ||
                 SetMetric("IPv6", snapshot.InterfaceIndex, 9999);

        if (v4 && v6)
        {
            _log.Info($"Set interface {snapshot.InterfaceIndex} metric to 9999 (IPv4={v4}, IPv6={v6}).");
            return RouteChangeResult.Applied;
        }

        _log.Warn($"Failed to set interface {snapshot.InterfaceIndex} metric (IPv4={v4}, IPv6={v6}).");
        return RouteChangeResult.Failed;
    }

    public RouteChangeResult RestoreDirect(RouteSnapshot snapshot)
    {
        var v4 = !snapshot.V4Metric.HasValue ||
                 RestoreMetric("IPv4", snapshot.InterfaceIndex, snapshot.V4Metric.Value, snapshot.V4Automatic);
        var v6 = !snapshot.V6Metric.HasValue ||
                 RestoreMetric("IPv6", snapshot.InterfaceIndex, snapshot.V6Metric.Value, snapshot.V6Automatic);

        if (v4 && v6)
        {
            _log.Info($"Restored interface {snapshot.InterfaceIndex} metrics.");
            return RouteChangeResult.Applied;
        }

        _log.Warn($"Failed to restore interface {snapshot.InterfaceIndex} metrics (IPv4={v4}, IPv6={v6}).");
        return RouteChangeResult.Failed;
    }

    private RouteChangeResult ViaElevated(string verb, RouteSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var resultDirectory = Path.Combine(Path.GetTempPath(), "OpenCodePhoneProxy");
        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, $"route-{Guid.NewGuid():N}.txt");

        var arguments = $"{verb} {base64} \"{resultPath}\"";
        var exitCode = Elevation.RunElevated(arguments);

        if (exitCode is null)
        {
            _log.Warn("Administrator approval was declined; route protection was not changed.");
            return RouteChangeResult.Declined;
        }

        try
        {
            if (exitCode == 0 &&
                File.Exists(resultPath) &&
                File.ReadAllText(resultPath).StartsWith("APPLIED", StringComparison.Ordinal))
            {
                return RouteChangeResult.Applied;
            }

            var detail = File.Exists(resultPath) ? File.ReadAllText(resultPath) : $"helper exit code {exitCode}";
            _log.Error("Elevated route change failed: " + detail);
        }
        finally
        {
            try
            {
                File.Delete(resultPath);
            }
            catch
            {
                // Best effort.
            }
        }

        return RouteChangeResult.Failed;
    }

    private (int Metric, bool Automatic)? ReadMetric(string family, int interfaceIndex)
    {
        var command =
            "$i = Get-NetIPInterface -InterfaceIndex " + interfaceIndex +
            " -AddressFamily " + family +
            " -ErrorAction Stop; Write-Output ('{0}|{1}' -f $i.InterfaceMetric, $i.AutomaticMetric)";

        var result = RunPowerShell(command);
        if (result.ExitCode != 0)
        {
            _log.Warn($"Get-NetIPInterface {family} failed: {result.Error.Trim()}");
            return null;
        }

        var line = result.Output
            .Split('\n', '\r')
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();

        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var parts = line.Split('|');
        if (parts.Length < 2 || !int.TryParse(parts[0].Trim(), out var metric))
        {
            return null;
        }

        return (metric, parts[1].Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase));
    }

    private bool SetMetric(string family, int interfaceIndex, int metric)
    {
        var command =
            "Set-NetIPInterface -InterfaceIndex " + interfaceIndex +
            " -AddressFamily " + family +
            " -InterfaceMetric " + metric +
            " -ErrorAction Stop";
        var result = RunPowerShell(command);
        if (result.ExitCode != 0)
        {
            _log.Warn($"Set-NetIPInterface {family} metric failed: {result.Error.Trim()}");
            return false;
        }

        return true;
    }

    private bool RestoreMetric(string family, int interfaceIndex, int metric, bool automatic)
    {
        var command = automatic
            ? "Set-NetIPInterface -InterfaceIndex " + interfaceIndex +
              " -AddressFamily " + family +
              " -AutomaticMetric Enabled -ErrorAction Stop"
            : "Set-NetIPInterface -InterfaceIndex " + interfaceIndex +
              " -AddressFamily " + family +
              " -InterfaceMetric " + metric +
              " -ErrorAction Stop";

        var result = RunPowerShell(command);
        if (result.ExitCode != 0)
        {
            _log.Warn($"Restore-NetIPInterface {family} failed: {result.Error.Trim()}");
            return false;
        }

        return true;
    }

    private static PowerShellResult RunPowerShell(string command)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return new PowerShellResult(-1, "", "Could not start powershell.exe.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(20_000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone.
                }

                return new PowerShellResult(-1, output, "PowerShell timed out.");
            }

            return new PowerShellResult(process.ExitCode, output, error);
        }
        catch (Exception ex)
        {
            return new PowerShellResult(-1, "", ex.Message);
        }
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);
}

public static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public static int? RunElevated(string arguments)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return -1;
        }

        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null;
        }
        catch
        {
            return -1;
        }
    }
}
