using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace ZoomClipboard;

internal static class UiTheme
{
    internal static readonly Color Canvas = Color.FromArgb(244, 247, 252);
    internal static readonly Color Surface = Color.White;
    internal static readonly Color Primary = Color.FromArgb(45, 107, 255);
    internal static readonly Color PrimaryDark = Color.FromArgb(31, 77, 208);
    internal static readonly Color PrimarySoft = Color.FromArgb(232, 239, 255);
    internal static readonly Color Violet = Color.FromArgb(116, 82, 255);
    internal static readonly Color Text = Color.FromArgb(20, 28, 45);
    internal static readonly Color Muted = Color.FromArgb(99, 112, 135);
    internal static readonly Color Border = Color.FromArgb(224, 230, 240);
    internal static readonly Color Success = Color.FromArgb(22, 163, 74);
    internal static readonly Color SuccessSoft = Color.FromArgb(232, 248, 238);
    internal static readonly Color Warning = Color.FromArgb(217, 119, 6);
    internal static readonly Color Danger = Color.FromArgb(220, 38, 38);

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class GradientHeader : Panel
{
    internal GradientHeader() { DoubleBuffered = true; }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(27, 72, 185), Color.FromArgb(105, 74, 230), 12f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var glow = new SolidBrush(Color.FromArgb(24, Color.White));
        e.Graphics.FillEllipse(glow, Width - 260, -150, 420, 320);
        e.Graphics.FillEllipse(glow, Width - 510, 72, 260, 190);
    }
}

internal sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Radius { get; set; } = 16;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color BorderColor { get; set; } = UiTheme.Border;

    internal CardPanel()
    {
        DoubleBuffered = true;
        BackColor = UiTheme.Surface;
        Padding = new Padding(22);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (Width > 1 && Height > 1)
            Region = new Region(UiTheme.Rounded(new Rectangle(0, 0, Width, Height), Radius));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.Rounded(rect, Radius);
        using var fill = new SolidBrush(BackColor);
        using var border = new Pen(BorderColor);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class PremiumButton : Button
{
    private bool hovered;
    private bool pressed;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color FillColor { get; set; } = UiTheme.Primary;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color HoverColor { get; set; } = UiTheme.PrimaryDark;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color TextColor { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Radius { get; set; } = 10;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool AlignLeft { get; set; }

    internal PremiumButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 9.5f);
        UseVisualStyleBackColor = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        var color = !Enabled ? Color.FromArgb(205, 213, 226) : pressed ? Color.FromArgb(Math.Max(0, HoverColor.R - 18), Math.Max(0, HoverColor.G - 18), Math.Max(0, HoverColor.B - 18)) : hovered ? HoverColor : FillColor;
        var rect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = UiTheme.Rounded(rect, Radius);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);
        var textRect = AlignLeft ? new Rectangle(rect.Left + 16, rect.Top, rect.Width - 24, rect.Height) : rect;
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? TextColor : Color.FromArgb(115, 127, 147),
            (AlignLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(rect, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, TextColor, color);
        }
    }
}

internal sealed class StatusPill : Control
{
    private string stateText = "Signed out";
    private Color stateColor = UiTheme.Muted;
    private Color stateBackground = Color.FromArgb(240, 243, 248);

    internal StatusPill()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI Semibold", 9f);
        Size = new Size(126, 34);
    }

    internal void SetState(string text, Color color, Color background)
    {
        stateText = text;
        stateColor = color;
        stateBackground = background;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.Rounded(rect, 17);
        using var fill = new SolidBrush(stateBackground);
        e.Graphics.FillPath(fill, path);
        using var dot = new SolidBrush(stateColor);
        e.Graphics.FillEllipse(dot, 13, Height / 2 - 4, 8, 8);
        TextRenderer.DrawText(e.Graphics, stateText, Font, new Rectangle(28, 0, Width - 34, Height), stateColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class PremiumProgressBar : Control
{
    private int value;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    internal PremiumProgressBar()
    {
        Height = 10;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var trackPath = UiTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using var track = new SolidBrush(Color.FromArgb(226, 232, 242));
        e.Graphics.FillPath(track, trackPath);
        var filled = (int)((Width - 1) * value / 100d);
        if (filled <= 1) return;
        using var valuePath = UiTheme.Rounded(new Rectangle(0, 0, filled, Height - 1), Height / 2);
        using var gradient = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, filled), Height),
            UiTheme.Primary, UiTheme.Violet, 0f);
        e.Graphics.FillPath(gradient, valuePath);
    }
}
