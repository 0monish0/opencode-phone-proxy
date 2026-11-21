using System.Diagnostics;
using OpenCodePhoneProxy.Models;
using OpenCodePhoneProxy.Services;

namespace OpenCodePhoneProxy.UI;

public sealed class SetupForm : Form
{
    private readonly SettingsStore _store;
    private readonly Logger _log;
    private readonly ProxyProbe _probe;
    private readonly AppSettings _settings;

    private readonly ComboBox _adapterBox = new();
    private readonly Button _refreshButton;
    private readonly CheckBox _autoHost = new();
    private readonly TextBox _hostBox = new();
    private readonly NumericUpDown _portBox = new();
    private readonly ComboBox _schemeBox = new();
    private readonly Button _testButton;
    private readonly Label _testResult = new();

    private readonly TextBox _openCodePath = new();
    private readonly ComboBox _candidateBox = new();
    private readonly Label _openCodeStatus = new();
    private readonly Button _browseButton;
    private readonly Button _detectButton;

    private readonly CheckBox _protectRoute = new();
    private readonly CheckBox _testBeforeLaunch = new();
    private readonly CheckBox _closeExisting = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _minimizeToTray = new();

    private readonly Button _saveButton;
    private readonly Button _cancelButton;
    private bool _testing;

    public SetupForm(AppSettings settings, SettingsStore store, Logger log)
    {
        _settings = settings;
        _store = store;
        _log = log;
        _probe = new ProxyProbe(log);

        _refreshButton = UiTheme.SecondaryButton("Refresh", 84);
        _testButton = UiTheme.SecondaryButton("Test connection", 140);
        _browseButton = UiTheme.SecondaryButton("Browse", 88);
        _detectButton = UiTheme.SecondaryButton("Auto-detect", 110);
        _saveButton = UiTheme.PrimaryButton("Save", 110);
        _cancelButton = UiTheme.SecondaryButton("Cancel", 100);

        BuildUi();
        LoadValues();
        RefreshAdapters();
        DetectOpenCode(silent: true);
    }

    private void BuildUi()
    {
        Text = "OpenCode Phone Proxy - Setup";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 650);
        MinimumSize = new Size(760, 700);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = UiTheme.Background;
        Font = UiTheme.BodyFont;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(24, 14, 24, 10)
        };
        header.Controls.Add(new Label
        {
            Text = "OpenCode Phone Proxy",
            Font = UiTheme.TitleFont,
            ForeColor = UiTheme.Accent,
            AutoSize = true,
            Location = new Point(24, 14)
        });
        header.Controls.Add(new Label
        {
            Text = "Route only OpenCode through your phone. Everything else stays on your normal network.",
            Font = UiTheme.SmallFont,
            ForeColor = UiTheme.Muted,
            AutoSize = true,
            Location = new Point(26, 54)
        });

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = UiTheme.BodyFont,
            Padding = new Point(18, 8)
        };
        tabs.TabPages.Add(BuildPhoneTab());
        tabs.TabPages.Add(BuildOpenCodeTab());
        tabs.TabPages.Add(BuildOptionsTab());

        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(20, 14, 20, 14)
        };
        var footerNote = new Label
        {
            Text = "Developed by Monish  ·  Saved on this PC only.",
            AutoSize = true,
            Font = UiTheme.SmallFont,
            ForeColor = UiTheme.Muted,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var footerButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            BackColor = UiTheme.Surface
        };
        footerButtons.Controls.Add(_saveButton);
        footerButtons.Controls.Add(_cancelButton);
        footer.Controls.Add(footerButtons);
        footer.Controls.Add(footerNote);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);

        _saveButton.Click += (_, _) => Save();
        _cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        _refreshButton.Click += (_, _) => RefreshAdapters();
        _testButton.Click += async (_, _) => await TestConnectionAsync();
        _browseButton.Click += (_, _) => BrowseOpenCode();
        _detectButton.Click += (_, _) => DetectOpenCode(silent: false);
        _adapterBox.SelectedIndexChanged += (_, _) => AdapterChanged();
        _candidateBox.SelectedIndexChanged += (_, _) => CandidateChanged();
        _autoHost.CheckedChanged += (_, _) => _hostBox.Enabled = !_autoHost.Checked;
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private TabPage BuildPhoneTab()
    {
        var tab = new TabPage("Phone")
        {
            BackColor = UiTheme.Background,
            AutoScroll = true
        };

        var grid = NewGrid(7);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _adapterBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _adapterBox.Width = 360;
        _adapterBox.Font = UiTheme.BodyFont;
        var adapterFlow = NewFlow();
        adapterFlow.Controls.Add(_adapterBox);
        adapterFlow.Controls.Add(_refreshButton);
        grid.Controls.Add(UiTheme.FieldLabel("USB tether adapter"), 0, 0);
        grid.Controls.Add(adapterFlow, 1, 0);

        _hostBox.Width = 220;
        _hostBox.Font = UiTheme.BodyFont;
        _autoHost.Text = "Detect automatically";
        _autoHost.AutoSize = true;
        _autoHost.Font = UiTheme.BodyFont;
        _autoHost.ForeColor = UiTheme.Muted;
        _autoHost.Margin = new Padding(12, 9, 0, 0);
        var hostFlow = NewFlow();
        hostFlow.Controls.Add(_hostBox);
        hostFlow.Controls.Add(_autoHost);
        grid.Controls.Add(UiTheme.FieldLabel("Phone address"), 0, 1);
        grid.Controls.Add(hostFlow, 1, 1);

        _portBox.Minimum = 1;
        _portBox.Maximum = 65535;
        _portBox.Value = 8080;
        _portBox.Width = 110;
        _portBox.Font = UiTheme.BodyFont;
        var portFlow = NewFlow();
        portFlow.Controls.Add(_portBox);
        portFlow.Controls.Add(UiTheme.Label("Default: 8080", UiTheme.SmallFont, UiTheme.Muted));
        grid.Controls.Add(UiTheme.FieldLabel("Port"), 0, 2);
        grid.Controls.Add(portFlow, 1, 2);

        _schemeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _schemeBox.Width = 140;
        _schemeBox.Font = UiTheme.BodyFont;
        _schemeBox.Items.AddRange(new object[] { "http", "https", "socks5", "socks5h" });
        var schemeFlow = NewFlow();
        schemeFlow.Controls.Add(_schemeBox);
        schemeFlow.Controls.Add(UiTheme.Label("Every Proxy HTTP mode uses http.", UiTheme.SmallFont, UiTheme.Muted));
        grid.Controls.Add(UiTheme.FieldLabel("Proxy type"), 0, 3);
        grid.Controls.Add(schemeFlow, 1, 3);

        var testPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 6, 0, 0)
        };
        _testResult.AutoSize = true;
        _testResult.MaximumSize = new Size(440, 0);
        _testResult.Font = UiTheme.SmallFont;
        _testResult.ForeColor = UiTheme.Muted;
        _testResult.Margin = new Padding(0, 8, 0, 0);
        testPanel.Controls.Add(_testButton, 0, 0);
        testPanel.Controls.Add(_testResult, 0, 1);
        grid.Controls.Add(UiTheme.FieldLabel("Check"), 0, 4);
        grid.Controls.Add(testPanel, 1, 4);

        var note = UiTheme.Note(
            "On the phone: mobile data on, USB cable connected, USB tethering on, and Every Proxy running in Server -> HTTP Proxy mode. " +
            "The phone address is usually the adapter's gateway.",
            470);
        grid.Controls.Add(note, 0, 5);
        grid.SetColumnSpan(note, 2);

        tab.Controls.Add(grid);
        return tab;
    }

    private TabPage BuildOpenCodeTab()
    {
        var tab = new TabPage("OpenCode")
        {
            BackColor = UiTheme.Background,
            AutoScroll = true
        };

        var grid = NewGrid(6);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _openCodePath.Width = 330;
        _openCodePath.Font = UiTheme.BodyFont;
        var pathFlow = NewFlow();
        pathFlow.Controls.Add(_openCodePath);
        pathFlow.Controls.Add(_browseButton);
        grid.Controls.Add(UiTheme.FieldLabel("OpenCode executable"), 0, 0);
        grid.Controls.Add(pathFlow, 1, 0);

        _candidateBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _candidateBox.Width = 330;
        _candidateBox.Font = UiTheme.BodyFont;
        var candidateFlow = NewFlow();
        candidateFlow.Controls.Add(_candidateBox);
        candidateFlow.Controls.Add(_detectButton);
        grid.Controls.Add(UiTheme.FieldLabel("Detected installs"), 0, 1);
        grid.Controls.Add(candidateFlow, 1, 1);

        _openCodeStatus.AutoSize = true;
        _openCodeStatus.MaximumSize = new Size(440, 0);
        _openCodeStatus.Font = UiTheme.SmallFont;
        _openCodeStatus.ForeColor = UiTheme.Muted;
        _openCodeStatus.Margin = new Padding(0, 6, 0, 8);
        grid.Controls.Add(_openCodeStatus, 1, 2);

        _closeExisting.Text = "Close a running OpenCode instance before launching";
        _closeExisting.AutoSize = true;
        _closeExisting.Font = UiTheme.BodyFont;
        var closeFlow = NewFlow();
        closeFlow.Controls.Add(_closeExisting);
        grid.Controls.Add(closeFlow, 1, 3);

        var note = UiTheme.Note(
            "Auto-detect checks the usual Windows install folders. If it does not find your copy, click Browse and select " +
            "OpenCode.exe (or opencode.exe for the CLI). The app re-checks this path every launch.",
            470);
        grid.Controls.Add(note, 0, 4);
        grid.SetColumnSpan(note, 2);

        tab.Controls.Add(grid);
        return tab;
    }

    private TabPage BuildOptionsTab()
    {
        var tab = new TabPage("Options")
        {
            BackColor = UiTheme.Background,
            AutoScroll = true
        };

        var grid = NewGrid(7, columns: 1);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _protectRoute.Text = "Keep USB tethering from becoming the default route";
        _protectRoute.AutoSize = true;
        _protectRoute.Font = UiTheme.BodyBoldFont;
        grid.Controls.Add(_protectRoute, 0, 0);

        var routeNote = UiTheme.Note(
            "Recommended. Windows will ask for administrator approval briefly while the USB adapter metric changes. " +
            "OpenCode itself keeps running as your normal user.",
            560);
        routeNote.Margin = new Padding(22, 0, 0, 12);
        grid.Controls.Add(routeNote, 0, 1);

        _testBeforeLaunch.Text = "Test the proxy before launching OpenCode";
        _testBeforeLaunch.AutoSize = true;
        _testBeforeLaunch.Font = UiTheme.BodyFont;
        grid.Controls.Add(_testBeforeLaunch, 0, 2);

        _startWithWindows.Text = "Start OpenCode Phone Proxy with Windows";
        _startWithWindows.AutoSize = true;
        _startWithWindows.Font = UiTheme.BodyFont;
        grid.Controls.Add(_startWithWindows, 0, 3);

        _minimizeToTray.Text = "Minimize to the tray when the window is closed";
        _minimizeToTray.AutoSize = true;
        _minimizeToTray.Font = UiTheme.BodyFont;
        grid.Controls.Add(_minimizeToTray, 0, 4);

        var note = UiTheme.Note(
            "Uninstall from the tray menu. It removes the app, settings, logs, and autostart entry, and restores any " +
            "network setting the app changed. It never touches OpenCode.",
            560);
        note.Margin = new Padding(0, 18, 0, 0);
        grid.Controls.Add(note, 0, 5);

        tab.Controls.Add(grid);
        return tab;
    }

    private static TableLayoutPanel NewGrid(int rows, int columns = 2)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 18),
            ColumnCount = columns,
            RowCount = rows,
            BackColor = UiTheme.Background,
            AutoSize = false
        };

        for (var index = 0; index < rows - 1; index++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return grid;
    }

    private static FlowLayoutPanel NewFlow() =>
        new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 4, 0, 4)
        };

    private void LoadValues()
    {
        _hostBox.Text = _settings.ProxyHost ?? "";
        _portBox.Value = Math.Clamp(_settings.ProxyPort, 1, 65535);
        _schemeBox.SelectedItem = ProxyConfig.NormalizeScheme(_settings.ProxyScheme);
        _autoHost.Checked = _settings.AutoDetectProxyHost;
        _hostBox.Enabled = !_autoHost.Checked;
        _openCodePath.Text = _settings.OpenCodePath ?? "";
        _protectRoute.Checked = _settings.ProtectDefaultRoute;
        _testBeforeLaunch.Checked = _settings.TestProxyBeforeLaunch;
        _closeExisting.Checked = _settings.CloseExistingOpenCode;
        _startWithWindows.Checked = _settings.StartWithWindows;
        _minimizeToTray.Checked = _settings.MinimizeToTray;

        if (!string.IsNullOrWhiteSpace(_openCodePath.Text) && File.Exists(_openCodePath.Text))
        {
            SetOpenCodeStatus(_openCodePath.Text);
        }
    }

    private void RefreshAdapters()
    {
        _adapterBox.Items.Clear();
        var links = PhoneLinkDetector.Detect();
        foreach (var link in links)
        {
            _adapterBox.Items.Add(new AdapterItem(link));
        }

        if (links.Count == 0)
        {
            _testResult.Text =
                "No USB tether adapter detected. Connect the phone, enable USB tethering, then click Refresh. " +
                "You can also turn off automatic detection and type the phone address.";
            _testResult.ForeColor = UiTheme.Warning;
            if (string.IsNullOrWhiteSpace(_hostBox.Text))
            {
                _hostBox.Text = _settings.ProxyHost ?? "";
            }

            return;
        }

        var selected = PhoneLinkDetector.Best() ?? links[0];
        for (var index = 0; index < links.Count; index++)
        {
            if (links[index].AdapterId == _settings.LastAdapterId)
            {
                selected = links[index];
                break;
            }
        }

        for (var index = 0; index < links.Count; index++)
        {
            if (links[index].AdapterId == selected.AdapterId)
            {
                _adapterBox.SelectedIndex = index;
                break;
            }
        }

        if (_autoHost.Checked)
        {
            _hostBox.Text = selected.GatewayAddress;
        }
    }

    private void AdapterChanged()
    {
        if (_autoHost.Checked && _adapterBox.SelectedItem is AdapterItem item)
        {
            _hostBox.Text = item.Link.GatewayAddress;
        }
    }

    private async Task TestConnectionAsync()
    {
        if (_testing)
        {
            return;
        }

        var config = ReadProxyConfig();
        var validation = config.Validate();
        if (validation is not null)
        {
            SetTestResult(validation, UiTheme.Danger);
            return;
        }

        _testing = true;
        _testButton.Enabled = false;
        _testButton.Text = "Testing...";
        SetTestResult("Testing through " + config.Url + " ...", UiTheme.Muted);

        try
        {
            var result = await _probe.TestAsync(config);
            var color = result.Success && !result.SameEgress
                ? UiTheme.Success
                : result.TcpReachable
                    ? UiTheme.Warning
                    : UiTheme.Danger;
            SetTestResult(result.Summary, color);
            _log.Info("Setup proxy test: " + result.Summary);
        }
        catch (Exception ex)
        {
            SetTestResult("Test failed: " + ex.Message, UiTheme.Danger);
        }
        finally
        {
            _testing = false;
            _testButton.Enabled = true;
            _testButton.Text = "Test connection";
        }
    }

    private void SetTestResult(string text, Color color)
    {
        _testResult.Text = text;
        _testResult.ForeColor = color;
    }

    private void BrowseOpenCode()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select the OpenCode executable",
            Filter = "OpenCode (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _openCodePath.Text = dialog.FileName;
            SetOpenCodeStatus(dialog.FileName);
        }
    }

    private void DetectOpenCode(bool silent)
    {
        var candidates = OpenCodeLocator.FindAll();
        _candidateBox.Items.Clear();
        foreach (var candidate in candidates)
        {
            _candidateBox.Items.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            if (!silent)
            {
                _openCodeStatus.Text = "OpenCode was not found automatically. Click Browse and select OpenCode.exe.";
                _openCodeStatus.ForeColor = UiTheme.Warning;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(_openCodePath.Text) && File.Exists(_openCodePath.Text))
        {
            var existing = candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Path, _openCodePath.Text, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _candidateBox.SelectedItem = existing;
            }

            return;
        }

        var preferred = candidates.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, _settings.OpenCodePath, StringComparison.OrdinalIgnoreCase)) ?? candidates[0];
        _candidateBox.SelectedItem = preferred;
        _openCodePath.Text = preferred.Path;
        SetOpenCodeStatus(preferred.Path);
        _openCodeStatus.Text = $"Found {candidates.Count} install(s). Version {preferred.Version}.";
        _openCodeStatus.ForeColor = UiTheme.Muted;
    }

    private void CandidateChanged()
    {
        if (_candidateBox.SelectedItem is OpenCodeCandidate candidate)
        {
            _openCodePath.Text = candidate.Path;
            SetOpenCodeStatus(candidate.Path);
        }
    }

    private void SetOpenCodeStatus(string path)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
            _openCodeStatus.Text = version.Length > 0
                ? $"Selected: {Path.GetFileName(path)}  -  version {version}"
                : $"Selected: {Path.GetFileName(path)}";
            _openCodeStatus.ForeColor = UiTheme.Muted;
        }
        catch (Exception ex)
        {
            _openCodeStatus.Text = "Could not read the OpenCode version: " + ex.Message;
            _openCodeStatus.ForeColor = UiTheme.Warning;
        }
    }

    private ProxyConfig ReadProxyConfig()
    {
        var host = _hostBox.Text.Trim();
        if (_autoHost.Checked && _adapterBox.SelectedItem is AdapterItem item)
        {
            host = item.Link.GatewayAddress;
        }

        var scheme = _schemeBox.SelectedItem as string ?? "http";
        return new ProxyConfig(host, (int)_portBox.Value, scheme);
    }

    private void Save()
    {
        var config = ReadProxyConfig();
        var validation = config.Validate();
        if (validation is not null)
        {
            ShowError(validation);
            return;
        }

        // A disconnected phone must not block saving unrelated settings. If automatic
        // detection is on but no adapter is present, keep the last known/manual host.

        var openCodePath = _openCodePath.Text.Trim();
        if (openCodePath.Length > 0 && !File.Exists(openCodePath))
        {
            ShowError("The OpenCode executable does not exist at:\n\n" + openCodePath + "\n\nUse Browse to select it again.");
            return;
        }

        if (openCodePath.Length == 0)
        {
            var answer = MessageBox.Show(
                this,
                "OpenCode was not selected. You can set it later, but Launch will ask you to choose it first.\n\nSave anyway?",
                "OpenCode not selected",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        _settings.ProxyHost = config.Host;
        _settings.ProxyPort = config.Port;
        _settings.ProxyScheme = config.Scheme;
        _settings.AutoDetectProxyHost = _autoHost.Checked;
        _settings.OpenCodePath = openCodePath;
        _settings.ProtectDefaultRoute = _protectRoute.Checked;
        _settings.TestProxyBeforeLaunch = _testBeforeLaunch.Checked;
        _settings.CloseExistingOpenCode = _closeExisting.Checked;
        _settings.StartWithWindows = _startWithWindows.Checked;
        _settings.MinimizeToTray = _minimizeToTray.Checked;
        _settings.FirstRunCompleted = true;
        _settings.LastAdapterId = (_adapterBox.SelectedItem as AdapterItem)?.Link.AdapterId;

        try
        {
            _store.Save(_settings);
            AutostartManager.Set(_settings.StartWithWindows, Environment.ProcessPath ?? "");
        }
        catch (Exception ex)
        {
            ShowError("Could not save settings: " + ex.Message);
            return;
        }

        _log.Info("Settings saved.");
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "Check your setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed class AdapterItem
    {
        public AdapterItem(PhoneLink link)
        {
            Link = link;
        }

        public PhoneLink Link { get; }

        public override string ToString() =>
            $"{Link.AdapterName} - {Link.Description} ({Link.GatewayAddress})" +
            (Link.LooksLikeUsbTether ? "" : "  [not USB tether]");
    }
}
