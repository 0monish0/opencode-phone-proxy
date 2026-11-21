using System.Drawing.Drawing2D;

namespace OpenCodePhoneProxy.UI;

public static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(246, 248, 252);
    public static readonly Color Surface = Color.White;
    public static readonly Color Accent = Color.FromArgb(11, 107, 203);
    public static readonly Color AccentHover = Color.FromArgb(9, 92, 175);
    public static readonly Color AccentPressed = Color.FromArgb(7, 78, 148);
    public static readonly Color Success = Color.FromArgb(26, 127, 55);
    public static readonly Color Warning = Color.FromArgb(178, 106, 0);
    public static readonly Color Danger = Color.FromArgb(198, 40, 40);
    public static readonly Color Text = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(91, 101, 115);
    public static readonly Color Border = Color.FromArgb(214, 221, 232);
    public static readonly Color Subtle = Color.FromArgb(238, 242, 248);

    public static readonly Font TitleFont = new("Segoe UI", 18f, FontStyle.Bold);
    public static readonly Font SectionFont = new("Segoe UI", 11.5f, FontStyle.Bold);
    public static readonly Font BodyFont = new("Segoe UI", 9.75f, FontStyle.Regular);
    public static readonly Font BodyBoldFont = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font SmallFont = new("Segoe UI", 8.75f, FontStyle.Regular);

    public static Label Label(string text, Font? font = null, Color? color = null) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = font ?? BodyFont,
            ForeColor = color ?? Text,
            Margin = new Padding(0, 6, 0, 6)
        };

    public static Label FieldLabel(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Font = BodyBoldFont,
            ForeColor = Text,
            Margin = new Padding(0, 9, 12, 9)
        };

    public static Label Note(string text, int maxWidth = 540) =>
        new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(maxWidth, 0),
            Font = SmallFont,
            ForeColor = Muted,
            Margin = new Padding(0, 4, 0, 8)
        };

    public static Button PrimaryButton(string text, int width = 140)
    {
        var button = BaseButton(text, width);
        button.BackColor = Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = AccentHover;
        button.FlatAppearance.MouseDownBackColor = AccentPressed;
        button.Font = BodyBoldFont;
        return button;
    }

    public static Button SecondaryButton(string text, int width = 130)
    {
        var button = BaseButton(text, width);
        button.BackColor = Surface;
        button.ForeColor = Text;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Subtle;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 233, 243);
        return button;
    }

    private static Button BaseButton(string text, int width) =>
        new()
        {
            Text = text,
            Width = width,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = BodyFont,
            UseVisualStyleBackColor = false,
            TabStop = true,
            Margin = new Padding(6, 0, 0, 0)
        };
}

public sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
        Padding = new Padding(20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

public sealed class StatusDot : Control
{
    private Color _color = UiTheme.Muted;

    public StatusDot()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        Size = new Size(16, 16);
        Margin = new Padding(0, 4, 10, 0);
    }

    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var halo = new SolidBrush(Color.FromArgb(38, _color));
        using var fill = new SolidBrush(_color);
        e.Graphics.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
        e.Graphics.FillEllipse(fill, 3, 3, Width - 7, Height - 7);
    }
}
