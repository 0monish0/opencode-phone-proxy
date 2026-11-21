using System.Diagnostics;
using System.Text;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public sealed class OpenCodeLauncher
{
    private static readonly string[] ProxyKeys =
    {
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
        "http_proxy", "https_proxy", "all_proxy"
    };

    private static readonly string[] ProcessNames =
    {
        "opencode-desktop", "OpenCode", "opencode", "ai.opencode", "opencode-cli"
    };

    private readonly Logger _log;
    private Process? _process;
    private string? _cliPath;
    private bool _serviceStopped;

    public OpenCodeLauncher(Logger log)
    {
        _log = log;
    }

    public event EventHandler? Exited;

    public bool IsRunning =>
        _process is { HasExited: false } ||
        ProcessNames.Any(name => Process.GetProcessesByName(name).Any());

    public int CloseExisting(AppSettings settings)
    {
        if (!settings.CloseExistingOpenCode)
        {
            return 0;
        }

        var count = 0;
        foreach (var name in ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    _log.Info($"Closing {name} process {process.Id}.");
                    if (process.CloseMainWindow())
                    {
                        if (!process.WaitForExit(5000))
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    else
                    {
                        process.Kill(entireProcessTree: true);
                    }

                    count++;
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not close {name} process {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return count;
    }

    public string? StopBundledService()
    {
        _cliPath = OpenCodeLocator.FindBundledCli();
        if (_cliPath is null)
        {
            return null;
        }

        var result = RunTool(_cliPath, new[] { "service", "stop" });
        if (result)
        {
            _serviceStopped = true;
            _log.Info("Stopped the OpenCode background service.");
        }

        return _cliPath;
    }

    public (Process? Process, LaunchResult Result) Start(
        OpenCodeCandidate candidate,
        ProxyConfig proxy,
        AppSettings settings)
    {
        if (!File.Exists(candidate.Path))
        {
            return (null, new LaunchResult(false, null, "OpenCode was not found at: " + candidate.Path));
        }

        try
        {
            var info = BuildStartInfo(candidate, proxy, settings);
            var process = Process.Start(info);
            if (process is null)
            {
                return (null, new LaunchResult(false, null, "Windows did not start OpenCode."));
            }

            _process = process;
            _log.Info($"Started OpenCode (PID {process.Id}) with proxy {proxy.Url}.");
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
            return (process, new LaunchResult(true, process.Id, null));
        }
        catch (Exception ex)
        {
            _log.Error("Could not start OpenCode: " + ex);
            return (null, new LaunchResult(false, null, ex.Message));
        }
    }

    public int StopAll()
    {
        var count = 0;
        foreach (var name in ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    count++;
                }
                catch
                {
                    // Already gone or protected.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        _process = null;
        return count;
    }

    public void StartCleanService()
    {
        if (!_serviceStopped || string.IsNullOrWhiteSpace(_cliPath))
        {
            return;
        }

        try
        {
            var info = new ProcessStartInfo(_cliPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var key in ProxyKeys)
            {
                info.Environment.Remove(key);
            }

            info.Environment.Remove("NO_PROXY");
            info.Environment.Remove("no_proxy");
            info.Environment.Remove("NODE_USE_ENV_PROXY");
            info.ArgumentList.Add("service");
            info.ArgumentList.Add("start");
            Process.Start(info)?.Dispose();
            _serviceStopped = false;
            _log.Info("Started a clean OpenCode background service.");
        }
        catch (Exception ex)
        {
            _log.Warn("Could not restart the OpenCode background service: " + ex.Message);
        }
    }

    public static ProcessStartInfo BuildStartInfo(
        OpenCodeCandidate candidate,
        ProxyConfig proxy,
        AppSettings settings)
    {
        var info = new ProcessStartInfo(candidate.Path)
        {
            UseShellExecute = false,
            CreateNoWindow = false
        };

        var directory = Path.GetDirectoryName(candidate.Path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            info.WorkingDirectory = directory;
        }

        foreach (var key in ProxyKeys)
        {
            info.Environment[key] = proxy.Url;
        }

        // OpenCode Desktop must reach its own sidecar on loopback. Never send
        // 127.0.0.1 / localhost through the phone proxy, or the UI reports
        // "Can't reach this server". Do not pass Chromium's --proxy-server:
        // the env vars already cover OpenCode's traffic, and the flag can pull
        // the desktop's local control connection into the proxy.
        var noProxy = BuildNoProxy();
        info.Environment["NO_PROXY"] = noProxy;
        info.Environment["no_proxy"] = noProxy;

        foreach (var argument in SplitArguments(settings.OpenCodeArguments))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal static string BuildNoProxy()
    {
        var entries = new List<string>
        {
            "localhost",
            "127.0.0.1",
            "0.0.0.0",
            "::1",
            "[::1]",
            "*.localhost"
        };

        var machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machine))
        {
            entries.Add(machine);
            entries.Add(machine + ".local");
        }

        return string.Join(",", entries.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> SplitArguments(string? input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return result;
        }

        var current = new StringBuilder();
        var quoted = false;
        var escaping = false;

        foreach (var character in input)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && quoted)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private bool RunTool(string executable, IReadOnlyList<string> arguments)
    {
        try
        {
            var info = new ProcessStartInfo(executable)
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

            using var process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(8000) && process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not run {executable}: {ex.Message}");
            return false;
        }
    }
}
