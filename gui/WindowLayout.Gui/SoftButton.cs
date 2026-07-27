using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WindowLayout.Gui;

/// <summary>Owner-drawn button with rounded corners, soft shadow, and hover/press states.</summary>
internal sealed class SoftButton : Button
{
    private bool _hot;
    private bool _pressed;
    private bool _emphasized;
    private int _cornerRadius = 12;

    public SoftButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        ForeColor = Color.White;
        BackColor = Color.FromArgb(51, 65, 85);
        Font = new Font("Segoe UI", 9.5f);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(4, value);
            Invalidate();
        }
    }

    /// <summary>Draw a soft accent ring (used for the suggested next step).</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Emphasized
    {
        get => _emphasized;
        set
        {
            if (_emphasized == value) return;
            _emphasized = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hot = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Parent?.BackColor ?? Color.FromArgb(11, 18, 32));

        // Keep a thin bottom shadow band; avoid shrinking body/text so glyphs (esp. on
        // short buttons like the theme toggle) are not clipped by VerticalCenter.
        var shadowDepth = _pressed ? 1 : 2;
        var bodyOffset = _pressed ? 1 : 0;
        var bodyRect = new Rectangle(1, bodyOffset, Width - 4, Height - shadowDepth - 1 - bodyOffset);
        var shadowRect = new Rectangle(2, bodyRect.Y + shadowDepth, Width - 6, bodyRect.Height);

        using (var shadowPath = RoundedRect(shadowRect, _cornerRadius))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(Enabled ? 55 : 25, 0, 0, 0)))
        {
            g.FillPath(shadowBrush, shadowPath);
        }

        var fill = BaseFillColor();
        if (!Enabled)
            fill = SoftBlend(fill, Color.FromArgb(30, 41, 59), 0.45f);
        else if (_pressed)
            fill = SoftBlend(fill, Color.Black, 0.12f);
        else if (_hot)
            fill = SoftBlend(fill, Color.White, 0.10f);

        using (var bodyPath = RoundedRect(bodyRect, _cornerRadius))
        using (var bodyBrush = new SolidBrush(fill))
        {
            g.FillPath(bodyBrush, bodyPath);

            // Soft top highlight edge
            if (Enabled && !_pressed)
            {
                using var hi = new Pen(Color.FromArgb(40, 255, 255, 255), 1.2f);
                var top = new Rectangle(bodyRect.X + 2, bodyRect.Y + 1, bodyRect.Width - 4, Math.Max(8, bodyRect.Height / 2));
                using var clip = RoundedRect(bodyRect, _cornerRadius);
                g.SetClip(clip);
                g.DrawLine(hi, top.Left, top.Top, top.Right, top.Top);
                g.ResetClip();
            }

            if (_emphasized && Enabled)
            {
                using var ring = new Pen(Color.FromArgb(200, 125, 211, 252), 2f);
                var ringRect = Rectangle.Inflate(bodyRect, -1, -1);
                using var ringPath = RoundedRect(ringRect, Math.Max(4, _cornerRadius - 1));
                g.DrawPath(ring, ringPath);
            }
        }

        var padX = Math.Min(14, Math.Max(6, bodyRect.Width / 10));
        var padY = bodyRect.Height >= 44 ? 6 : (bodyRect.Height >= 28 ? 2 : 1);
        var textRect = Rectangle.Inflate(bodyRect, -padX, -padY);
        if (textRect.Height < 1) textRect.Height = Math.Max(1, bodyRect.Height);
        if (textRect.Width < 1) textRect.Width = Math.Max(1, bodyRect.Width);
        var textColor = Enabled ? ForeColor : Color.FromArgb(148, 163, 184);
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        flags |= TextAlign is ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
            ? TextFormatFlags.Left
            : TextFormatFlags.HorizontalCenter;
        TextRenderer.DrawText(g, Text, Font, textRect, textColor, flags);
    }

    private Color BaseFillColor() => BackColor;

    private static Color SoftBlend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * amount),
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d > bounds.Width) d = bounds.Width;
        if (d > bounds.Height) d = bounds.Height;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
