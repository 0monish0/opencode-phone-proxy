using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public sealed class ProxyProbe
{
    private readonly Logger _log;

    public ProxyProbe(Logger log)
    {
        _log = log;
    }

    public async Task<ProxyTestResult> TestAsync(ProxyConfig config, CancellationToken cancellationToken = default)
    {
        var validation = config.Validate();
        if (validation is not null)
        {
            return new ProxyTestResult { TcpReachable = false, Success = false, Error = validation };
        }

        var reachable = await TcpCheckAsync(config, cancellationToken).ConfigureAwait(false);
        if (!reachable)
        {
            return new ProxyTestResult
            {
                TcpReachable = false,
                Success = false,
                Error = $"Cannot reach {config.Host}:{config.Port}. Check USB tethering and that the proxy app is running."
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var proxy = await RunCurlAsync(
            new[]
            {
                "-x", config.Url,
                "--noproxy", "",
                "--max-time", "15",
                "-fsS",
                "https://api.ipify.org"
            },
            cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (!proxy.Ok)
        {
            return new ProxyTestResult
            {
                TcpReachable = true,
                Success = false,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Error = string.IsNullOrWhiteSpace(proxy.Error)
                    ? "The proxy did not answer the test request."
                    : proxy.Error.Trim()
            };
        }

        var directIp = await DirectIpAsync(cancellationToken).ConfigureAwait(false);
        return new ProxyTestResult
        {
            TcpReachable = true,
            Success = true,
            PublicIp = proxy.Output,
            DirectIp = directIp,
            LatencyMs = stopwatch.ElapsedMilliseconds
        };
    }

    public async Task<bool> TcpCheckAsync(ProxyConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(config.Host, config.Port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> DirectIpAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunCurlAsync(
            new[]
            {
                "--noproxy", "*",
                "--max-time", "10",
                "-fsS",
                "https://api.ipify.org"
            },
            cancellationToken).ConfigureAwait(false);
        return result.Ok ? result.Output : null;
    }

    private async Task<CurlResult> RunCurlAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
        if (!File.Exists(curl))
        {
            curl = "curl.exe";
        }

        var info = new ProcessStartInfo(curl)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return new CurlResult(false, "", "Could not start curl.exe.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already gone.
                }

                return new CurlResult(false, "", "The proxy test timed out.");
            }

            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();

            if (process.ExitCode != 0)
            {
                _log.Warn($"curl failed with exit code {process.ExitCode}: {error}");
                return new CurlResult(false, output, error);
            }

            // Keep only the first line and require something that looks like an address.
            var firstLine = output.Split('\n', '\r')
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?.Trim() ?? "";

            if (!IPAddress.TryParse(firstLine, out _))
            {
                return new CurlResult(false, firstLine, "The proxy answered, but the test response was not an IP address.");
            }

            return new CurlResult(true, firstLine, "");
        }
        catch (Exception ex)
        {
            _log.Warn("curl test failed: " + ex.Message);
            return new CurlResult(false, "", ex.Message);
        }
    }

    private sealed record CurlResult(bool Ok, string Output, string Error);
}
