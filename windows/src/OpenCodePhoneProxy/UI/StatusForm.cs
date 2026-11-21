using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.UI;

public sealed class StatusForm : Form
{
    private readonly StatusDot _dot = new();
    private readonly Label _statusLabel = new();
    private readonly Label _detailsLabel = new();
    private readonly Label _openCodeLabel = new();
    private readonly Button _launchButton;
    private readonly Button _testButton;
    private readonly Button _settingsButton;
    private readonly LinkLabel _logLink = new();

    public StatusForm()
    {
        _launchButton = UiTheme.PrimaryButton("Launch OpenCode", 170);
        _testButton = UiTheme.SecondaryButton("Test connection", 140);
        _settingsButton = UiTheme.SecondaryButton("Settings", 100);

        BuildUi();
        UpdateStatus(null, null, false, new ProxyConfig("", 8080, "http"));
    }

    public bool AllowClose { get; set; }

    public event EventHandler? LaunchRequested;
    public event EventHandler? TestRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? LogRequested;

    public void UpdateStatus(PhoneLink? link, bool? reachable, bool openCodeRunning, ProxyConfig config)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateStatus(link, reachable, openCodeRunning, config)));
            return;
        }

        if (link is null)
        {
            _dot.Color = UiTheme.Danger;
            _statusLabel.Text = "Phone not detected";
            _detailsLabel.Text = string.IsNullOrWhiteSpace(config.Host)
                ? "Connect the USB cable, enable USB tethering, and start Every Proxy."
                : $"Using manual address {config.Host}:{config.Port}. Connect USB tethering for automatic detection.";
        }
        else if (reachable == false)
        {
            _dot.Color = UiTheme.Warning;
            _statusLabel.Text = "Phone link found, proxy not answering";
            _detailsLabel.Text =
                $"{link.AdapterName}  |  {link.GatewayAddress}:{config.Port}  |  local {link.LocalAddress}\n" +
                "Check that mobile data is on and Every Proxy is running.";
        }
        else
        {
            _dot.Color = reachable == true ? UiTheme.Success : UiTheme.Accent;
            _statusLabel.Text = reachable == true ? "Phone connected" : "Phone link found";
            _detailsLabel.Text =
                $"{link.AdapterName}  |  {link.GatewayAddress}:{config.Port}  |  local {link.LocalAddress}";
        }

        _openCodeLabel.Text = openCodeRunning
            ? "OpenCode is running through the phone."
            : "OpenCode is not running.";
        _launchButton.Enabled = !openCodeRunning;
    }

    private void BuildUi()
    {
        Text = "OpenCode Phone Proxy";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(610, 430);
        MinimumSize = new Size(650, 470);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
            Text = "Only OpenCode uses the phone. Everything else stays on your normal network.",
            Font = UiTheme.SmallFont,
            ForeColor = UiTheme.Muted,
            AutoSize = true,
            Location = new Point(26, 54)
        });

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 16),
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = true,
            BackColor = UiTheme.Background
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var card = new CardPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        var cardGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Surface
        };
        var statusFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Margin = new Padding(0)
        };
        _statusLabel.AutoSize = true;
        _statusLabel.Font = new Font("Segoe UI", 13f, FontStyle.Bold);
        _statusLabel.ForeColor = UiTheme.Text;
        _statusLabel.Margin = new Padding(0, 1, 0, 0);
        statusFlow.Controls.Add(_dot);
        statusFlow.Controls.Add(_statusLabel);

        _detailsLabel.AutoSize = true;
        _detailsLabel.MaximumSize = new Size(510, 0);
        _detailsLabel.Font = UiTheme.SmallFont;
        _detailsLabel.ForeColor = UiTheme.Muted;
        _detailsLabel.Margin = new Padding(0, 8, 0, 2);

        _openCodeLabel.AutoSize = true;
        _openCodeLabel.Font = UiTheme.BodyFont;
        _openCodeLabel.ForeColor = UiTheme.Text;
        _openCodeLabel.Margin = new Padding(0, 8, 0, 0);

        cardGrid.Controls.Add(statusFlow, 0, 0);
        cardGrid.Controls.Add(_detailsLabel, 0, 1);
        cardGrid.Controls.Add(_openCodeLabel, 0, 2);
        card.Controls.Add(cardGrid);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            BackColor = UiTheme.Background,
            Margin = new Padding(0, 16, 0, 0)
        };
        actions.Controls.Add(_launchButton);
        actions.Controls.Add(_testButton);
        actions.Controls.Add(_settingsButton);

        _logLink.Text = "View log";
        _logLink.AutoSize = true;
        _logLink.Font = UiTheme.SmallFont;
        _logLink.LinkColor = UiTheme.Accent;
        _logLink.ActiveLinkColor = UiTheme.AccentPressed;
        _logLink.Margin = new Padding(12, 12, 0, 0);
        actions.Controls.Add(_logLink);

        var footer = UiTheme.Note(
            "Phone proxy is used only by the OpenCode process. Windows system proxy and your other apps are not changed. " +
            "Version " + Program.Version + "  ·  Developed by Monish.",
            540);
        footer.Margin = new Padding(0, 18, 0, 0);

        content.Controls.Add(card, 0, 0);
        content.Controls.Add(actions, 0, 1);
        content.Controls.Add(footer, 0, 2);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        root.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Background }, 0, 2);
        Controls.Add(root);

        _launchButton.Click += (_, _) => LaunchRequested?.Invoke(this, EventArgs.Empty);
        _testButton.Click += (_, _) => TestRequested?.Invoke(this, EventArgs.Empty);
        _settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _logLink.LinkClicked += (_, _) => LogRequested?.Invoke(this, EventArgs.Empty);

        FormClosing += (_, e) =>
        {
            if (AllowClose)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        };
    }
}
