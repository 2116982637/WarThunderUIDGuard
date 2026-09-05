using System.Drawing.Drawing2D;

namespace WarThunderUIDGuard;

internal static class UiTheme
{
    public static Color Background { get; } = Color.FromArgb(237, 242, 250);
    public static Color Surface { get; } = Color.White;
    public static Color Primary { get; } = Color.FromArgb(57, 89, 211);
    public static Color Success { get; } = Color.FromArgb(24, 128, 100);
    public static Color Warning { get; } = Color.FromArgb(217, 119, 6);
    public static Color Danger { get; } = Color.FromArgb(192, 55, 80);
    public static Color Purple { get; } = Color.FromArgb(111, 82, 189);
    public static Color Teal { get; } = Color.FromArgb(15, 118, 110);
    public static Color TextPrimary { get; } = Color.FromArgb(30, 42, 65);
    public static Color TextSecondary { get; } = Color.FromArgb(99, 115, 139);
    public static Color Border { get; } = Color.FromArgb(222, 230, 243);
    public static Color Header { get; } = Color.FromArgb(239, 244, 252);
    public static Color Selection { get; } = Color.FromArgb(226, 235, 255);

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
        if (button is GlassButton glass)
        {
            glass.AccentColor = accent;
            glass.BackColor = Surface;
        }
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
        grid.GridColor = Color.FromArgb(237, 241, 248);
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
        grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 40);
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
        grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 38);
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
    private int _cornerRadius = 18;
    private Color _borderColor = UiTheme.Border;

    public UiCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
    }

    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(0, value);
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

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
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
        var scale = DeviceDpi / 96f;
        var bounds = new Rectangle(1, 1, Width - 3, Height - 1 - (int)Math.Ceiling(4 * scale));
        if (bounds.Width < 1 || bounds.Height < 1) return;
        for (var i = 3; i > 0; i--)
        {
            var shadowBounds = bounds;
            shadowBounds.Offset(0, (int)Math.Ceiling(i * scale));
            using var shadow = UiTheme.CreateRoundedPath(shadowBounds, ScaledCornerRadius());
            using var shadowBrush = new SolidBrush(Color.FromArgb(4, 45, 69, 117));
            e.Graphics.FillPath(shadowBrush, shadow);
        }
        using var path = UiTheme.CreateRoundedPath(bounds, ScaledCornerRadius());
        using var surface = new LinearGradientBrush(bounds, Color.White, Color.FromArgb(250, 252, 255), 90f);
        e.Graphics.FillPath(surface, path);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    private int ScaledCornerRadius() => CornerRadius == 0
        ? 0
        : Math.Max(1, (int)Math.Round(CornerRadius * DeviceDpi / 96d));
}
