using System.Drawing.Drawing2D;

namespace WarThunderUIDGuard;

internal sealed class UiBackdropPanel : TableLayoutPanel
{
    public UiBackdropPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        if (SystemInformation.HighContrast)
        {
            e.Graphics.Clear(SystemColors.Control);
            return;
        }
        using var gradient = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(229, 237, 253), Color.FromArgb(243, 245, 251), 45f);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }
}

internal sealed class UiInputFrame : Panel
{
    private readonly TextBox _editor;

    public UiInputFrame(TextBox editor)
    {
        _editor = editor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        TabStop = false;
        UiTheme.StyleTextBox(editor);
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = UiTheme.Surface;
        editor.Dock = DockStyle.None;
        editor.Margin = Padding.Empty;
        editor.TabIndex = 0;
        editor.Enter += EditorFocusChanged;
        editor.Leave += EditorFocusChanged;
        Controls.Add(editor);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_editor is null) return;
        var inset = Math.Max(8, (int)Math.Round(12 * DeviceDpi / 96d));
        var textHeight = _editor.PreferredHeight;
        _editor.SetBounds(inset, Math.Max(2, (Height - textHeight) / 2),
            Math.Max(1, Width - inset * 2), textHeight);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _editor.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 4 || Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        using var outline = UiTheme.CreateRoundedPath(bounds, (int)Math.Round(10 * scale));
        using var fill = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Window : UiTheme.Surface);
        e.Graphics.FillPath(fill, outline);
        var border = SystemInformation.HighContrast ? SystemColors.WindowText :
            _editor.Focused ? UiTheme.Primary : UiTheme.Border;
        using var pen = new Pen(border, _editor.Focused ? Math.Max(1.5f, 1.5f * scale) : 1f);
        e.Graphics.DrawPath(pen, outline);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _editor.Enter -= EditorFocusChanged;
            _editor.Leave -= EditorFocusChanged;
        }
        base.Dispose(disposing);
    }

    private void EditorFocusChanged(object? sender, EventArgs e) => Invalidate();
}

internal sealed class UiBrandMark : Control
{
    public UiBrandMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        AccessibleName = "UID Guard";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var size = Math.Min(Width - 4, Height - 4);
        if (size < 8) return;
        var bounds = new Rectangle(2, (Height - size) / 2, size, size);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiTheme.CreateRoundedPath(bounds, size / 3);
        using var fill = new LinearGradientBrush(bounds, Color.FromArgb(119, 153, 245), UiTheme.Primary, 65f);
        e.Graphics.FillPath(fill, path);
        using var highlight = new Pen(Color.FromArgb(150, Color.White), 1f);
        e.Graphics.DrawPath(highlight, path);
        PointF Point(float x, float y) => new(bounds.X + size * x, bounds.Y + size * y);
        using var shield = new GraphicsPath();
        shield.AddLines([Point(.5f, .23f), Point(.74f, .32f), Point(.71f, .58f),
            Point(.63f, .70f), Point(.5f, .79f), Point(.37f, .70f),
            Point(.29f, .58f), Point(.26f, .32f), Point(.5f, .23f)]);
        using var pen = new Pen(Color.White, Math.Max(1.6f, size * .035f))
        { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawPath(pen, shield);
        e.Graphics.DrawLines(pen, [Point(.39f, .49f), Point(.47f, .58f), Point(.62f, .42f)]);
    }
}
