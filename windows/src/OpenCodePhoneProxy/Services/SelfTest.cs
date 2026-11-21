using System.IO.Pipes;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

internal static class AppServices
{
    public static Logger? Logger { get; set; }
}

public static class SelfTest
{
    public static int Run()
    {
        var lines = new List<string>();
        var failures = 0;
        var root = Path.Combine(
            Path.GetTempPath(),
            "OpenCodePhoneProxy-selftest-" + Guid.NewGuid().ToString("N"));

        void Check(string name, Func<bool> test)
        {
            try
            {
                var ok = test();
                lines.Add($"{name}: {(ok ? "PASS" : "FAIL")}");
                if (!ok)
                {
                    failures++;
                }
            }
            catch (Exception ex)
            {
                lines.Add($"{name}: FAIL {ex.GetType().Name}: {ex.Message}");
                failures++;
            }
        }

        try
        {
            Directory.CreateDirectory(root);
            var logger = new Logger(root);

            Check("settings-roundtrip", () =>
            {
                var store = new SettingsStore(Path.Combine(root, "roundtrip"));
                var settings = new AppSettings
                {
                    ProxyHost = "192.168.42.129",
                    ProxyPort = 8080,
                    ProxyScheme = "http",
                    OpenCodePath = @"C:\Program Files\OpenCode\OpenCode.exe",
                    FirstRunCompleted = true
                };
                store.Save(settings);
                var loaded = store.Load();
                return loaded.ProxyHost == settings.ProxyHost &&
                       loaded.ProxyPort == settings.ProxyPort &&
                       loaded.ProxyScheme == settings.ProxyScheme &&
                       loaded.OpenCodePath == settings.OpenCodePath &&
                       loaded.FirstRunCompleted;
            });

            Check("settings-corrupt", () =>
            {
                var corruptRoot = Path.Combine(root, "corrupt");
                Directory.CreateDirectory(corruptRoot);
                File.WriteAllText(Path.Combine(corruptRoot, "settings.json"), "{not valid json");
                var store = new SettingsStore(corruptRoot);
                var loaded = store.Load();
                var backups = Directory.GetFiles(corruptRoot, "settings.bad.*.json");
                return loaded.ProxyPort == 8080 &&
                       store.LastLoadError is not null &&
                       backups.Length == 1;
            });

            Check("proxy-url", () =>
                new ProxyConfig("192.168.42.129", 8080, "http").Url == "http://192.168.42.129:8080" &&
                new ProxyConfig("10.0.0.2", 1080, "socks5").Url == "socks5://10.0.0.2:1080" &&
                new ProxyConfig("bad host", 8080, "http").Validate() is not null &&
                new ProxyConfig("192.168.42.129", 0, "http").Validate() is not null);

            Check("proxy-result", () =>
            {
                var same = new ProxyTestResult
                {
                    TcpReachable = true,
                    Success = true,
                    PublicIp = "1.2.3.4",
                    DirectIp = "1.2.3.4"
                };
                var skipped = new ProxyTestResult
                {
                    TcpReachable = true,
                    Success = true,
                    PublicIp = "5.6.7.8",
                    LatencyMs = 42
                };
                var unreadable = new ProxyTestResult
                {
                    TcpReachable = true,
                    Success = true,
                    PublicIp = "5.6.7.8",
                    DirectSourceAddress = "192.168.1.20",
                    LatencyMs = 42
                };
                var failed = new ProxyTestResult { TcpReachable = false, Success = false, Error = "offline" };

                return same.SameEgress &&
                       !skipped.SameEgress &&
                       skipped.DirectComparisonSkipped &&
                       skipped.Summary.Contains("No separate Wi-Fi/Ethernet", StringComparison.Ordinal) &&
                       unreadable.DirectComparisonSkipped &&
                       unreadable.Summary.Contains("Could not read", StringComparison.Ordinal) &&
                       !failed.DirectComparisonSkipped;
            });

            Check("argument-split", () =>
            {
                var arguments = OpenCodeLauncher.SplitArguments("--foo \"bar baz\" --x");
                return arguments.Count == 3 &&
                       arguments[0] == "--foo" &&
                       arguments[1] == "bar baz" &&
                       arguments[2] == "--x";
            });

            Check("launch-env", () =>
            {
                var info = OpenCodeLauncher.BuildStartInfo(
                    new OpenCodeCandidate(Path.Combine(root, "OpenCode.exe"), "", "selftest"),
                    new ProxyConfig("10.0.0.2", 8080, "http"),
                    new AppSettings());

                var noProxy = info.Environment["NO_PROXY"] ?? "";
                var arguments = string.Join(" ", info.ArgumentList);
                return info.Environment["HTTP_PROXY"] == "http://10.0.0.2:8080" &&
                       info.Environment["HTTPS_PROXY"] == "http://10.0.0.2:8080" &&
                       noProxy.Contains("127.0.0.1", StringComparison.Ordinal) &&
                       noProxy.Contains("::1", StringComparison.Ordinal) &&
                       noProxy.Contains("0.0.0.0", StringComparison.Ordinal) &&
                       !arguments.Contains("--proxy-server", StringComparison.Ordinal);
            });

            Check("route-session", () =>
            {
                var snapshot = new RouteSnapshot
                {
                    InterfaceIndex = 1,
                    AdapterName = "selftest",
                    V4Metric = 25,
                    V4Automatic = false
                };

                var allOk = true;

                NamedPipeServerStream NewServer(string pipeName) =>
                    new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                var pipeName = "ocpp-selftest-" + Guid.NewGuid().ToString("N");
                using (var server = NewServer(pipeName))
                {
                    var restoreCalls = 0;
                    var helper = Task.Run(() => RouteSessionProtocol.Run(
                        snapshot,
                        pipeName,
                        _ => RouteChangeResult.Applied,
                        _ =>
                        {
                            Interlocked.Increment(ref restoreCalls);
                            return RouteChangeResult.Applied;
                        },
                        logger,
                        5_000));
                    server.WaitForConnection();
                    var ok = server.ReadByte() == RouteSessionProtocol.Applied;
                    server.WriteByte(RouteSessionProtocol.RestoreRequested);
                    server.Flush();
                    ok &= server.ReadByte() == RouteSessionProtocol.RestoreDone;
                    ok &= helper.Wait(15_000) && helper.Result == 0 && restoreCalls == 1;
                    lines.Add("  route-session-restore: " + (ok ? "PASS" : "FAIL"));
                    allOk &= ok;
                }

                pipeName = "ocpp-selftest-" + Guid.NewGuid().ToString("N");
                using (var server = NewServer(pipeName))
                {
                    var restoreCalls = 0;
                    var helper = Task.Run(() => RouteSessionProtocol.Run(
                        snapshot,
                        pipeName,
                        _ => RouteChangeResult.Applied,
                        _ =>
                        {
                            Interlocked.Increment(ref restoreCalls);
                            return RouteChangeResult.Applied;
                        },
                        logger,
                        5_000));
                    server.WaitForConnection();
                    var ok = server.ReadByte() == RouteSessionProtocol.Applied;
                    server.WriteByte(RouteSessionProtocol.KeepRequested);
                    server.Flush();
                    ok &= server.ReadByte() == RouteSessionProtocol.KeepRequested;
                    ok &= helper.Wait(15_000) && helper.Result == 0 && restoreCalls == 0;
                    lines.Add("  route-session-keep: " + (ok ? "PASS" : "FAIL"));
                    allOk &= ok;
                }

                pipeName = "ocpp-selftest-" + Guid.NewGuid().ToString("N");
                var crashServer = NewServer(pipeName);
                var crashRestoreCalls = 0;
                var crashHelper = Task.Run(() => RouteSessionProtocol.Run(
                    snapshot,
                    pipeName,
                    _ => RouteChangeResult.Applied,
                    _ =>
                    {
                        Interlocked.Increment(ref crashRestoreCalls);
                        return RouteChangeResult.Applied;
                    },
                    logger,
                    5_000));
                crashServer.WaitForConnection();
                var crashOk = crashServer.ReadByte() == RouteSessionProtocol.Applied;
                crashServer.Dispose();
                crashOk &= crashHelper.Wait(15_000) &&
                            crashHelper.Result == 0 &&
                            crashRestoreCalls == 1;
                lines.Add("  route-session-crash: " + (crashOk ? "PASS" : "FAIL"));
                allOk &= crashOk;

                pipeName = "ocpp-selftest-" + Guid.NewGuid().ToString("N");
                using (var server = NewServer(pipeName))
                {
                    var restoreCalls = 0;
                    var helper = Task.Run(() => RouteSessionProtocol.Run(
                        snapshot,
                        pipeName,
                        _ => RouteChangeResult.Failed,
                        _ =>
                        {
                            Interlocked.Increment(ref restoreCalls);
                            return RouteChangeResult.Applied;
                        },
                        logger,
                        5_000));
                    server.WaitForConnection();
                    var ok = server.ReadByte() == RouteSessionProtocol.Failed;
                    ok &= helper.Wait(15_000) && helper.Result == 1 && restoreCalls == 0;
                    lines.Add("  route-session-fail: " + (ok ? "PASS" : "FAIL"));
                    allOk &= ok;
                }

                return allOk;
            });

            Check("logger", () =>
            {
                logger.Info("selftest entry");
                return logger.Tail(10).Contains("selftest entry", StringComparison.Ordinal);
            });

            Check("detect", () =>
            {
                var links = PhoneLinkDetector.Detect();
                lines.Add("  adapters: " + (links.Count == 0
                    ? "(none)"
                    : string.Join("; ", links.Select(link =>
                        $"{link.AdapterName} [{link.GatewayAddress}] score={link.Score} usb={link.LooksLikeUsbTether}"))));
                return true;
            });

            Check("locator", () =>
            {
                var candidates = OpenCodeLocator.FindAll();
                lines.Add("  opencode: " + (candidates.Count == 0
                    ? "(not installed)"
                    : string.Join("; ", candidates.Select(candidate => $"{candidate.Path} ({candidate.Version})"))));
                return true;
            });

            Check("curl", () =>
            {
                var curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
                lines.Add("  curl: " + curl);
                return File.Exists(curl);
            });

            Check("powershell", () =>
            {
                var powershell = Path.Combine(
                    Environment.SystemDirectory,
                    "WindowsPowerShell", "v1.0", "powershell.exe");
                lines.Add("  powershell: " + powershell);
                return File.Exists(powershell);
            });

            Check("route-capture", () =>
            {
                var link = PhoneLinkDetector.Detect()
                    .FirstOrDefault(candidate => candidate.LooksLikeUsbTether) ??
                    PhoneLinkDetector.Detect().FirstOrDefault();
                if (link is null)
                {
                    lines.Add("  route-capture: no adapter to inspect (ok)");
                    return true;
                }

                var snapshot = new NetworkRouteGuard(logger)
                    .Capture(link.InterfaceIndex, link.AdapterName);
                if (snapshot is null)
                {
                    lines.Add("  route-capture: no metrics returned for " + link.AdapterName);
                    return false;
                }

                lines.Add($"  route-capture: {snapshot.AdapterName} v4={snapshot.V4Metric}/{snapshot.V4Automatic} v6={snapshot.V6Metric}/{snapshot.V6Automatic}");
                return true;
            });
        }
        catch (Exception ex)
        {
            lines.Add("selftest harness: FAIL " + ex);
            failures++;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch
            {
                // Temp cleanup is best effort.
            }
        }

        lines.Add(failures == 0 ? "RESULT: PASS" : $"RESULT: FAIL ({failures})");

        try
        {
            File.WriteAllLines(
                Path.Combine(Path.GetTempPath(), "OpenCodePhoneProxy-selftest.log"),
                lines);
        }
        catch
        {
            // Console output is enough for CI.
        }

        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }

        return failures == 0 ? 0 : 1;
    }
}
