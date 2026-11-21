using System.Diagnostics;
using OpenCodePhoneProxy.Models;
using OpenCodePhoneProxy.Services;
using Timer = System.Windows.Forms.Timer;

namespace OpenCodePhoneProxy.UI;

public sealed class TrayApp : ApplicationContext
{
    private readonly SettingsStore _store;
    private readonly Logger _log;
    private readonly ProxyProbe _probe;
    private readonly NetworkRouteGuard _guard;
    private readonly OpenCodeLauncher _launcher;
    private readonly StatusForm _statusForm;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly Timer _pollTimer;

    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _launchItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _autostartItem;

    private AppSettings _settings;
    private PhoneLink? _link;
    private bool? _reachable;
    private DateTimeOffset _lastTcpCheck = DateTimeOffset.MinValue;
    private bool _busy;
    private bool _polling;
    private bool _openCodeRunning;
    private bool _exiting;
    private RouteSession? _session;

    public bool StartupCanceled { get; private set; }

    public TrayApp(bool startHidden)
    {
        _store = new SettingsStore();
        _log = new Logger(_store.DataDirectory);
        AppServices.Logger = _log;
        _settings = _store.Load();

        if (_store.LastLoadError is not null)
        {
            _log.Warn("Settings were unreadable and were reset: " + _store.LastLoadError);
        }

        _probe = new ProxyProbe(_log);
        _guard = new NetworkRouteGuard(_log);
        _launcher = new OpenCodeLauncher(_log);
        _launcher.Exited += (_, _) => OnOpenCodeExited();

        _statusForm = new StatusForm();
        _statusForm.LaunchRequested += async (_, _) => await LaunchAsync();
        _statusForm.TestRequested += async (_, _) => await TestConnectionAsync();
        _statusForm.SettingsRequested += (_, _) => ShowSettings();
        _statusForm.LogRequested += (_, _) => OpenLog();

        var icon = Environment.ProcessPath is { } path
            ? Icon.ExtractAssociatedIcon(path)
            : null;
        _tray = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "OpenCode Phone Proxy",
            Visible = true,
            BalloonTipTitle = "OpenCode Phone Proxy"
        };
        _tray.DoubleClick += (_, _) => ShowStatus();

        _statusItem = new ToolStripMenuItem("Phone: checking...") { Enabled = false };
        _launchItem = new ToolStripMenuItem("Launch OpenCode");
        _stopItem = new ToolStripMenuItem("Stop OpenCode") { Enabled = false };
        _autostartItem = new ToolStripMenuItem("Start with Windows");

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_launchItem);
        _menu.Items.Add(_stopItem);
        _menu.Items.Add(new ToolStripMenuItem("Test connection", null, async (_, _) => await TestConnectionAsync()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings()));
        _menu.Items.Add(new ToolStripMenuItem("View log", null, (_, _) => OpenLog()));
        _menu.Items.Add(_autostartItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Uninstall...", null, (_, _) => Uninstall()));
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
        _menu.Opening += (_, _) => UpdateMenu();
        _tray.ContextMenuStrip = _menu;

        _launchItem.Click += async (_, _) => await LaunchAsync();
        _stopItem.Click += (_, _) => StopOpenCode();
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _pollTimer = new Timer { Interval = 5000 };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _pollTimer.Start();

        if (!_settings.FirstRunCompleted)
        {
            if (!ShowSetup())
            {
                _log.Info("First-run setup canceled; exiting.");
                PrepareStartupCancel();
                return;
            }
        }

        if (_settings.PendingRoute is not null)
        {
            _log.Warn("A previous session left a route change pending. Restoring it.");
            RestorePendingRoute(showWarning: true);
        }

        UpdateStatus();

        if (!startHidden)
        {
            ShowStatus();
        }
    }

    public void ShowStatus()
    {
        if (_exiting || _statusForm.IsDisposed)
        {
            return;
        }

        _statusForm.Show();
        if (_statusForm.WindowState == FormWindowState.Minimized)
        {
            _statusForm.WindowState = FormWindowState.Normal;
        }

        _statusForm.Activate();
    }

    private bool ShowSetup()
    {
        using var form = new SetupForm(_settings, _store, _log);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        _settings = _store.Load();
        return true;
    }

    private void ShowSettings()
    {
        if (_busy || _exiting)
        {
            return;
        }

        using var form = new SetupForm(_settings, _store, _log);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings = _store.Load();
        UpdateStatus();
    }

    private async Task LaunchAsync()
    {
        if (_busy || _exiting)
        {
            return;
        }

        _busy = true;
        UpdateStatus();

        try
        {
            var link = FindSelectedLink();
            var config = BuildConfig(link);
            var validation = config.Validate();
            if (validation is not null)
            {
                ShowMessage(validation, MessageBoxIcon.Warning);
                ShowSettings();
                return;
            }

            if (_settings.AutoDetectProxyHost && link is null && string.IsNullOrWhiteSpace(config.Host))
            {
                ShowMessage(
                    "Phone not detected.\n\n" +
                    "Connect the USB cable, enable USB tethering on the phone, start Every Proxy, then try again. " +
                    "You can also set the phone address manually in Settings.",
                    MessageBoxIcon.Warning);
                return;
            }

            var candidate = ResolveCandidate();
            if (candidate is null)
            {
                ShowMessage(
                    "OpenCode was not found.\n\n" +
                    "Use Settings > OpenCode > Browse to select OpenCode.exe once.",
                    MessageBoxIcon.Warning);
                ShowSettings();
                return;
            }

            if (_settings.TestProxyBeforeLaunch)
            {
                _tray.Text = "OpenCode Phone Proxy - testing...";
                var test = await _probe.TestAsync(config);
                _log.Info("Pre-launch proxy test: " + test.Summary);
                if (!test.Success || test.SameEgress)
                {
                    var proceed = ShowQuestion(
                        test.Summary + "\n\nLaunch OpenCode anyway?",
                        "Proxy test failed");
                    if (!proceed)
                    {
                        return;
                    }
                }
                else
                {
                    _reachable = true;
                }
            }

            RouteSnapshot? snapshot = null;
            if (_settings.ProtectDefaultRoute)
            {
                if (link is null)
                {
                    ShowMessage(
                        "Route protection needs the USB tether adapter, but none was detected.\n\n" +
                        "OpenCode will start without changing the interface metric.",
                        MessageBoxIcon.Warning);
                }
                else
                {
                    var pending = _settings.PendingRoute;
                    if (pending is not null && pending.InterfaceIndex == link.InterfaceIndex)
                    {
                        // A previous restore may have been declined. That snapshot is
                        // the only copy of the original metric; capturing now would
                        // save the already-guarded 9999 metric as the baseline.
                        snapshot = pending;
                        _log.Info("Reusing the pending route baseline for the USB adapter.");
                    }
                    else
                    {
                        if (pending is not null)
                        {
                            _log.Warn(
                                $"Replacing the pending route baseline for interface {pending.InterfaceIndex} " +
                                $"with the connected adapter {link.InterfaceIndex}.");
                        }

                        snapshot = _guard.Capture(link.InterfaceIndex, link.AdapterName);
                    }

                    if (snapshot is null)
                    {
                        ShowMessage(
                            "Could not read the USB adapter's route metric.\n\n" +
                            "OpenCode will start without route protection.",
                            MessageBoxIcon.Warning);
                    }
                    else
                    {
                        _settings.PendingRoute = snapshot;
                        _store.Save(_settings);

                        var (change, session) = await _guard.BeginSessionAsync(snapshot);
                        if (change == RouteChangeResult.Applied && session is not null)
                        {
                            _session?.Dispose();
                            _session = session;
                            _log.Info("Route protection is active for this OpenCode session.");
                        }
                        else
                        {
                            var reason = change == RouteChangeResult.Declined
                                ? "Administrator approval was declined."
                                : "Windows rejected the interface metric change.";

                            if (_guard.IsRestored(snapshot))
                            {
                                _settings.PendingRoute = null;
                                _store.Save(_settings);
                            }
                            else
                            {
                                _log.Warn(
                                    "Keeping the pending route baseline because the adapter metric is still changed.");
                            }

                            ShowMessage(
                                reason + "\n\nOpenCode will start, but USB tethering may become the default route.",
                                MessageBoxIcon.Warning);
                            snapshot = null;
                        }
                    }
                }
            }

            // Stop the background service first so the stop is graceful and can be
            // reversed with a clean service when OpenCode exits.
            _launcher.StopBundledService();

            var closed = _launcher.CloseExisting(_settings);
            if (closed > 0)
            {
                _log.Info($"Closed {closed} running OpenCode process(es).");
            }

            var (_, result) = _launcher.Start(candidate, config, _settings);
            if (!result.Started)
            {
                _log.Error("OpenCode launch failed: " + result.Error);
                RestorePendingRoute(showWarning: false);
                ShowMessage("Could not start OpenCode:\n\n" + result.Error, MessageBoxIcon.Error);
                return;
            }

            _openCodeRunning = true;
            _reachable = true;
            _tray.ShowBalloonTip(
                3000,
                "OpenCode started",
                "Traffic is going through your phone.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _log.Error("Launch failed: " + ex);
            RestorePendingRoute(showWarning: false);
            ShowMessage("Launch failed:\n\n" + ex.Message, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private void StopOpenCode()
    {
        if (_busy || _exiting)
        {
            return;
        }

        _busy = true;
        UpdateStatus();
        try
        {
            var count = _launcher.StopAll();
            _launcher.StartCleanService();
            RestorePendingRoute(showWarning: false);
            _openCodeRunning = false;
            _reachable = null;
            if (count == 0)
            {
                ShowMessage("OpenCode was not running.", MessageBoxIcon.Information);
            }
        }
        finally
        {
            _busy = false;
            UpdateStatus();
        }
    }

    private async Task TestConnectionAsync()
    {
        if (_busy || _exiting)
        {
            return;
        }

        var link = FindSelectedLink();
        var config = BuildConfig(link);
        var validation = config.Validate();
        if (validation is not null)
        {
            ShowMessage(validation, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _tray.Text = "OpenCode Phone Proxy - testing...";
            var result = await _probe.TestAsync(config);
            _reachable = result.Success && !result.SameEgress;
            var details = result.Summary;
            if (!string.IsNullOrWhiteSpace(result.DirectIp))
            {
                details += $"\n\nThis PC's direct IP: {result.DirectIp}.";
            }

            ShowMessage(
                details,
                result.Success && !result.SameEgress ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _log.Error("Connection test failed: " + ex);
            ShowMessage("Connection test failed:\n\n" + ex.Message, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateStatus();
        }
    }

    private async Task PollAsync()
    {
        if (_polling || _busy || _exiting)
        {
            return;
        }

        _polling = true;
        try
        {
            UpdateStatus();

            if (_link is not null &&
                DateTimeOffset.UtcNow - _lastTcpCheck > TimeSpan.FromSeconds(12))
            {
                _lastTcpCheck = DateTimeOffset.UtcNow;
                _reachable = await _probe.TcpCheckAsync(BuildConfig(_link));
                UpdateStatus();
            }
            else if (_link is null)
            {
                _reachable = null;
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void OnOpenCodeExited()
    {
        if (_exiting || _statusForm.IsDisposed)
        {
            return;
        }

        if (_statusForm.InvokeRequired)
        {
            _statusForm.BeginInvoke(new Action(OnOpenCodeExited));
            return;
        }

        _log.Info("OpenCode exited.");
        _openCodeRunning = false;
        _launcher.StartCleanService();
        RestorePendingRoute(showWarning: false);
        _reachable = null;
        UpdateStatus();
        _tray.ShowBalloonTip(
            3000,
            "OpenCode closed",
            "Network settings restored.",
            ToolTipIcon.Info);
    }

    private void UpdateStatus()
    {
        if (_exiting)
        {
            return;
        }

        _link = FindSelectedLink();
        _openCodeRunning = _launcher.IsRunning;
        var config = BuildConfig(_link);
        _statusForm.UpdateStatus(_link, _reachable, _openCodeRunning, config);
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        if (_exiting)
        {
            return;
        }

        _statusItem.Text = _link is null
            ? "Phone: not detected"
            : $"Phone: {_link.GatewayAddress}:{_settings.ProxyPort}";
        _launchItem.Enabled = !_busy && !_openCodeRunning;
        _stopItem.Enabled = !_busy && _openCodeRunning;
        _autostartItem.Checked = AutostartManager.IsEnabled;
        _tray.Text = _link is null
            ? "OpenCode Phone Proxy - phone not detected"
            : "OpenCode Phone Proxy - phone connected";
    }

    private PhoneLink? FindSelectedLink()
    {
        var links = PhoneLinkDetector.Detect();
        return links.FirstOrDefault(link => link.AdapterId == _settings.LastAdapterId)
               ?? links.FirstOrDefault(link => link.GatewayAddress == _settings.ProxyHost)
               ?? PhoneLinkDetector.Best();
    }

    private ProxyConfig BuildConfig(PhoneLink? link)
    {
        var host = _settings.AutoDetectProxyHost && link is not null
            ? link.GatewayAddress
            : _settings.ProxyHost ?? "";
        return new ProxyConfig(
            host,
            _settings.ProxyPort,
            ProxyConfig.NormalizeScheme(_settings.ProxyScheme));
    }

    private OpenCodeCandidate? ResolveCandidate()
    {
        if (!string.IsNullOrWhiteSpace(_settings.OpenCodePath) && File.Exists(_settings.OpenCodePath))
        {
            return new OpenCodeCandidate(_settings.OpenCodePath, "", "settings");
        }

        var found = OpenCodeLocator.FindAll().FirstOrDefault();
        if (found is null)
        {
            return null;
        }

        _settings.OpenCodePath = found.Path;
        _store.Save(_settings);
        return found;
    }

    private void RestorePendingRoute(bool showWarning)
    {
        var session = _session;
        _session = null;

        if (session is not null)
        {
            var liveResult = session.Restore();
            session.Dispose();
            if (liveResult == RouteChangeResult.Applied)
            {
                if (_settings.PendingRoute is not null &&
                    _settings.PendingRoute.InterfaceIndex == session.Snapshot.InterfaceIndex)
                {
                    _settings.PendingRoute = null;
                    _store.Save(_settings);
                }

                _log.Info("Route metric restored through the live helper session.");
                return;
            }

            var liveMessage =
                "The route helper could not confirm the restore. The app will try again next time.";
            _log.Warn(liveMessage);
            if (showWarning)
            {
                ShowMessage(liveMessage, MessageBoxIcon.Warning);
            }

            return;
        }

        if (_settings.PendingRoute is null)
        {
            return;
        }

        var snapshot = _settings.PendingRoute;
        if (_guard.IsRestored(snapshot))
        {
            _settings.PendingRoute = null;
            _store.Save(_settings);
            _log.Info("The adapter metric already matches the saved baseline; cleared the pending route.");
            return;
        }

        var result = _guard.Restore(snapshot);
        if (result == RouteChangeResult.Applied)
        {
            _settings.PendingRoute = null;
            _store.Save(_settings);
            _log.Info("Route metric restored.");
            return;
        }

        var message = result == RouteChangeResult.Declined
            ? "Administrator approval was declined, so the USB adapter metric was not restored. The app will try again next time."
            : "Windows did not restore the USB adapter metric. The app will try again next time.";
        _log.Warn(message);
        if (showWarning)
        {
            ShowMessage(message, MessageBoxIcon.Warning);
        }
    }

    private void ToggleAutostart()
    {
        _settings.StartWithWindows = !AutostartManager.IsEnabled;
        AutostartManager.Set(_settings.StartWithWindows, Environment.ProcessPath ?? "");
        _store.Save(_settings);
        UpdateMenu();
    }

    private void OpenLog()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_log.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowMessage("Could not open the log:\n\n" + ex.Message, MessageBoxIcon.Warning);
        }
    }

    private void Uninstall()
    {
        if (_busy || _exiting)
        {
            return;
        }

        var answer = ShowQuestion(
            "Remove OpenCode Phone Proxy?\n\n" +
            "This deletes the app, settings, logs, and autostart entry, and restores any network setting it changed.\n\n" +
            "OpenCode itself is not touched.",
            "Uninstall");
        if (!answer)
        {
            return;
        }

        RestorePendingRoute(showWarning: false);
        var directory = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? AppContext.BaseDirectory;
        Uninstaller.Run(_log, directory, keepSettings: false);
        // The uninstaller restores the route itself; don't show the
        // "route protection kept" exit path just because OpenCode is open.
        _openCodeRunning = false;
        ExitApp();
    }

    private void ExitApp()
    {
        if (_exiting)
        {
            return;
        }

        if (_openCodeRunning)
        {
            var answer = ShowQuestion(
                "OpenCode is still running.\n\n" +
                "Exit the tray app anyway? OpenCode keeps using the phone proxy, and the USB adapter " +
                "metric stays guarded until you open this app again.",
                "Exit");
            if (!answer)
            {
                return;
            }
        }

        _exiting = true;
        _pollTimer.Stop();

        if (_openCodeRunning)
        {
            // Restoring now could make USB tethering the default route while OpenCode
            // is still working. Tell the helper to leave the metric and exit; the
            // persisted snapshot is restored on the next app start.
            _log.Info("Tray app exiting while OpenCode is running; route restore is deferred.");
            _session?.Keep();
            _session?.Dispose();
            _session = null;
            _tray.ShowBalloonTip(
                4000,
                "Network settings kept",
                "USB route protection will be restored the next time you open OpenCode Phone Proxy.",
                ToolTipIcon.Info);
        }
        else
        {
            RestorePendingRoute(showWarning: false);
        }

        _statusForm.AllowClose = true;
        _statusForm.Close();
        _tray.Visible = false;
        _tray.Dispose();
        ExitThread();
    }

    private void PrepareStartupCancel()
    {
        StartupCanceled = true;
        _exiting = true;
        _pollTimer.Stop();
        _statusForm.AllowClose = true;
        _statusForm.Close();
        _tray.Visible = false;
    }

    private void ShowMessage(string text, MessageBoxIcon icon)
    {
        var owner = _statusForm.Visible ? _statusForm : null;
        if (owner is null)
        {
            MessageBox.Show(text, "OpenCode Phone Proxy", MessageBoxButtons.OK, icon);
        }
        else
        {
            MessageBox.Show(owner, text, "OpenCode Phone Proxy", MessageBoxButtons.OK, icon);
        }
    }

    private bool ShowQuestion(string text, string title)
    {
        var owner = _statusForm.Visible ? _statusForm : null;
        var result = owner is null
            ? MessageBox.Show(text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            : MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return result == DialogResult.Yes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session?.Dispose();
            _session = null;
            _pollTimer.Dispose();
            _tray.Dispose();
            _statusForm.Dispose();
        }

        base.Dispose(disposing);
    }
}
