using System.Drawing.Drawing2D;

namespace WarThunderUIDGuard;

internal static class UiTheme
{
    public static Color Background { get; } = Color.FromArgb(244, 247, 251);
    public static Color Surface { get; } = Color.White;
    public static Color Primary { get; } = Color.FromArgb(37, 99, 235);
    public static Color Success { get; } = Color.FromArgb(22, 163, 74);
    public static Color Warning { get; } = Color.FromArgb(217, 119, 6);
    public static Color Danger { get; } = Color.FromArgb(220, 38, 38);
    public static Color Purple { get; } = Color.FromArgb(124, 58, 237);
    public static Color Teal { get; } = Color.FromArgb(15, 118, 110);
    public static Color TextPrimary { get; } = Color.FromArgb(23, 32, 51);
    public static Color TextSecondary { get; } = Color.FromArgb(100, 116, 139);
    public static Color Border { get; } = Color.FromArgb(216, 225, 236);
    public static Color Header { get; } = Color.FromArgb(234, 241, 248);
    public static Color Selection { get; } = Color.FromArgb(219, 234, 254);

    public static void StyleButton(Button button, Color accent, bool compact = false)
    {
        button.AutoEllipsis = true;
        button.BackColor = accent;
        button.Cursor = Cursors.Hand;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Darken(accent, 10);
        button.FlatAppearance.MouseDownBackColor = Darken(accent, 20);
        button.Font = new Font("Microsoft YaHei UI", compact ? 8.5f : 9f, FontStyle.Bold);
        button.ForeColor = Color.White;
        button.MinimumSize = new Size(compact ? 72 : 88, compact ? 30 : 36);
        button.Padding = new Padding(compact ? 4 : 6, 0, compact ? 4 : 6, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        button.UseVisualStyleBackColor = false;
    }

    public static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Surface;
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.Font = new Font("Microsoft YaHei UI", 9f);
        textBox.ForeColor = TextPrimary;
    }

    public static void StyleComboBox(ComboBox comboBox)
    {
        comboBox.BackColor = Surface;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.Font = new Font("Microsoft YaHei UI", 9f);
        comboBox.ForeColor = TextPrimary;
    }

    public static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.AutoSize = true;
        checkBox.BackColor = Color.Transparent;
        checkBox.Font = new Font("Microsoft YaHei UI", 9f);
        checkBox.ForeColor = TextPrimary;
        checkBox.UseVisualStyleBackColor = false;
    }

    public static void StyleDataGridView(DataGridView grid)
    {
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.GridColor = Border;
        grid.RowHeadersVisible = false;

        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            BackColor = Header,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            ForeColor = TextPrimary,
            Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Header,
            SelectionForeColor = TextPrimary,
            WrapMode = DataGridViewTriState.False
        };
        grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 36);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            BackColor = Surface,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = TextPrimary,
            Padding = new Padding(8, 0, 8, 0),
            SelectionBackColor = Selection,
            SelectionForeColor = TextPrimary,
            WrapMode = DataGridViewTriState.False
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(249, 251, 253),
            ForeColor = TextPrimary,
            SelectionBackColor = Selection,
            SelectionForeColor = TextPrimary
        };
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 34);
    }

    public static Color Darken(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Max(0, color.R - Math.Max(0, amount)),
        Math.Max(0, color.G - Math.Max(0, amount)),
        Math.Max(0, color.B - Math.Max(0, amount)));

    internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Min(Math.Max(2, radius * 2), Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class UiCardPanel : Panel
{
    private int _cornerRadius = 14;
    private Color _borderColor = UiTheme.Border;

    public UiCardPanel()
    {
        BackColor = UiTheme.Surface;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
            UpdateRoundedRegion();
            Invalidate();
        }
    }

    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        UpdateRoundedRegion();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateRoundedRegion();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 1 || Height <= 1)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiTheme.CreateRoundedPath(bounds, ScaledCornerRadius());
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var oldRegion = Region;
            Region = null;
            oldRegion?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateRoundedRegion()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = UiTheme.CreateRoundedPath(ClientRectangle, ScaledCornerRadius());
        var replacement = new Region(path);
        var oldRegion = Region;
        Region = replacement;
        oldRegion?.Dispose();
    }

    private int ScaledCornerRadius() => CornerRadius == 0
        ? 0
        : Math.Max(1, (int)Math.Round(CornerRadius * DeviceDpi / 96d));
}
