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

        info.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1";
        info.Environment["no_proxy"] = "localhost,127.0.0.1,::1";
        info.Environment["NODE_USE_ENV_PROXY"] = "1";

        // Chromium's flag helps the desktop app. Passing it to the CLI could make
        // the CLI reject an unknown option, so only add it for the Electron build.
        if (OpenCodeLocator.LooksLikeDesktop(candidate.Path))
        {
            info.ArgumentList.Add("--proxy-server=" + proxy.Url);
        }

        foreach (var argument in SplitArguments(settings.OpenCodeArguments))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
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
