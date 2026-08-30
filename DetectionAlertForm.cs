using System.Drawing.Drawing2D;

namespace WarThunderUIDGuard;

internal sealed class DetectionAlertForm : Form
{
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 10000 };

    public DetectionAlertForm(string title, string body)
    {
        Text = title;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = UiTheme.Surface;
        Font = new Font("Microsoft YaHei UI", 9);
        Padding = new Padding(1);

        var bodyFont = new Font("Microsoft YaHei UI", 9.5f);
        ClientSize = CalculateClientSize(body, bodyFont);

        var accent = new Panel
        {
            BackColor = UiTheme.Warning,
            Dock = DockStyle.Left,
            Width = 6
        };
        var titleLabel = new Label
        {
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Margin = new Padding(0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var close = new Button
        {
            AccessibleName = title,
            BackColor = UiTheme.Surface,
            Cursor = Cursors.Hand,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 13f),
            ForeColor = UiTheme.TextSecondary,
            Margin = new Padding(2, 0, 0, 0),
            Padding = new Padding(0),
            TabStop = false,
            Text = "×",
            UseVisualStyleBackColor = false
        };
        close.FlatAppearance.BorderSize = 0;
        close.FlatAppearance.MouseOverBackColor = UiTheme.Header;
        close.FlatAppearance.MouseDownBackColor = UiTheme.Selection;
        close.Click += (_, _) => Close();

        var bodyLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = body,
            AutoEllipsis = true,
            Font = bodyFont,
            ForeColor = UiTheme.TextSecondary,
            Margin = new Padding(0, 6, 0, 0),
            TextAlign = ContentAlignment.TopLeft
        };

        var content = new TableLayoutPanel
        {
            BackColor = UiTheme.Surface,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(18, 12, 14, 14),
            RowCount = 2
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(titleLabel, 0, 0);
        content.Controls.Add(close, 1, 0);
        content.Controls.Add(bodyLabel, 0, 1);
        content.SetColumnSpan(bodyLabel, 2);

        Controls.Add(content);
        Controls.Add(accent);

        Shown += (_, _) =>
        {
            PositionInWorkingArea(Screen.FromPoint(Cursor.Position).WorkingArea);
            _closeTimer.Start();
        };
        _closeTimer.Tick += (_, _) => Close();
        FormClosed += (_, _) =>
        {
            _closeTimer.Stop();
            _closeTimer.Dispose();
        };
        Resize += (_, _) => UpdateRoundedRegion();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int csDropShadow = 0x00020000;
            const int wsExNoActivate = 0x08000000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= csDropShadow;
            parameters.ExStyle |= wsExNoActivate;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateRoundedRegion();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateRoundedRegion();
        PositionInWorkingArea(Screen.FromRectangle(e.SuggestedRectangle).WorkingArea);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width <= 1 || Height <= 1)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiTheme.CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), ScaledRadius());
        using var pen = new Pen(UiTheme.Border);
        e.Graphics.DrawPath(pen, path);
    }

    private void UpdateRoundedRegion()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = UiTheme.CreateRoundedPath(ClientRectangle, ScaledRadius());
        var replacement = new Region(path);
        var oldRegion = Region;
        Region = replacement;
        oldRegion?.Dispose();
        Invalidate();
    }

    private void PositionInWorkingArea(Rectangle workingArea)
    {
        var margin = Math.Max(8, (int)Math.Round(16 * DeviceDpi / 96d));
        Location = new Point(
            Math.Max(workingArea.Left, workingArea.Right - Width - margin),
            Math.Max(workingArea.Top, workingArea.Bottom - Height - margin));
    }

    private static Size CalculateClientSize(string body, Font font)
    {
        const int minimumWidth = 420;
        const int maximumWidth = 520;
        const int minimumHeight = 176;
        const int maximumHeight = 320;
        const TextFormatFlags singleLineFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        const TextFormatFlags wrappedFlags = TextFormatFlags.NoPadding |
                                             TextFormatFlags.TextBoxControl |
                                             TextFormatFlags.WordBreak;

        var longestLine = body
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .DefaultIfEmpty(string.Empty)
            .Max(line => TextRenderer.MeasureText(line, font, Size.Empty, singleLineFlags).Width);
        var width = Math.Clamp(longestLine + 64, minimumWidth, maximumWidth);
        var bodyWidth = Math.Max(1, width - 42);
        var measuredBody = TextRenderer.MeasureText(
            body,
            font,
            new Size(bodyWidth, maximumHeight),
            wrappedFlags);
        var height = Math.Clamp(measuredBody.Height + 76, minimumHeight, maximumHeight);
        return new Size(width, height);
    }

    private int ScaledRadius() => Math.Max(1, (int)Math.Round(14 * DeviceDpi / 96d));
}
