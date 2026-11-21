using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
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

    /// <summary>
    /// Start one elevated helper that applies the metric and stays alive until
    /// the tray app asks it to restore. This keeps a normal run to one UAC
    /// prompt. Falls back to the one-shot apply path when the helper cannot be
    /// started.
    /// </summary>
    public async Task<(RouteChangeResult Result, RouteSession? Session)> BeginSessionAsync(
        RouteSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (Elevation.IsAdministrator)
        {
            var direct = ApplyDirect(snapshot);
            return (direct, direct == RouteChangeResult.Applied
                ? RouteSession.CreateDirect(this, snapshot, _log)
                : null);
        }

        NamedPipeServerStream? pipe = null;
        Process? helper = null;

        try
        {
            var pipeName = "ocpp-route-" + Guid.NewGuid().ToString("N");
            pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            var json = JsonSerializer.Serialize(snapshot);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            helper = Elevation.StartElevated(
                $"--elevated-route-session {base64} \"{pipeName}\"",
                out var declined);

            if (helper is null)
            {
                pipe.Dispose();
                return (declined ? RouteChangeResult.Declined : RouteChangeResult.Failed, null);
            }

            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(120));

            try
            {
                await pipe.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _log.Warn("The elevated route helper did not connect in time.");
                await AbandonHelperAsync(pipe, helper).ConfigureAwait(false);
                return (RouteChangeResult.Failed, null);
            }

            var status = await ReadByteAsync(pipe, TimeSpan.FromSeconds(45), cancellationToken)
                .ConfigureAwait(false);

            if (status == (byte)'A')
            {
                _log.Info("The elevated route helper applied the metric and is holding the session.");
                return (RouteChangeResult.Applied, RouteSession.CreateHelper(snapshot, pipe, helper, _log));
            }

            _log.Warn("The elevated route helper did not apply the metric.");
            await AbandonHelperAsync(pipe, helper).ConfigureAwait(false);
            return (RouteChangeResult.Failed, null);
        }
        catch (Exception ex)
        {
            _log.Warn("Could not start the elevated route session: " + ex.Message);
            await AbandonHelperAsync(pipe, helper).ConfigureAwait(false);
            return (RouteChangeResult.Failed, null);
        }
    }

    /// <summary>
    /// Read-only check used to clear a stale pending snapshot without asking for
    /// administrator approval again.
    /// </summary>
    public bool IsRestored(RouteSnapshot snapshot)
    {
        var v4 = snapshot.V4Metric.HasValue ? ReadMetric("IPv4", snapshot.InterfaceIndex) : null;
        if (snapshot.V4Metric.HasValue)
        {
            if (v4 is null)
            {
                return false;
            }

            if (snapshot.V4Automatic != v4.Value.Automatic)
            {
                return false;
            }

            if (!snapshot.V4Automatic && snapshot.V4Metric.Value != v4.Value.Metric)
            {
                return false;
            }
        }

        var v6 = snapshot.V6Metric.HasValue ? ReadMetric("IPv6", snapshot.InterfaceIndex) : null;
        if (snapshot.V6Metric.HasValue)
        {
            if (v6 is null)
            {
                return false;
            }

            if (snapshot.V6Automatic != v6.Value.Automatic)
            {
                return false;
            }

            if (!snapshot.V6Automatic && snapshot.V6Metric.Value != v6.Value.Metric)
            {
                return false;
            }
        }

        return true;
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

    private static async Task<byte?> ReadByteAsync(
        Stream stream,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var buffer = new byte[1];

        try
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, 1), timeoutSource.Token)
                .ConfigureAwait(false);
            return read == 1 ? buffer[0] : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    private static async Task AbandonHelperAsync(NamedPipeServerStream? pipe, Process? helper)
    {
        try
        {
            pipe?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        if (helper is null)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // Timed out or already gone.
        }

        try
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            helper.Dispose();
        }
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

    public static Process? StartElevated(string arguments, out bool declined)
    {
        declined = false;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return null;
        }

        var info = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            return Process.Start(info);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            declined = true;
            return null;
        }
        catch
        {
            return null;
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
