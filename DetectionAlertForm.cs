namespace WarThunderUIDGuard;

internal sealed class DetectionAlertForm : Form
{
    private readonly System.Windows.Forms.Timer _closeTimer = new() { Interval = 10000 };

    public DetectionAlertForm(string title, string body)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(390, 160);
        BackColor = Color.FromArgb(255, 249, 230);
        Font = new Font("Microsoft YaHei UI", 9);

        var titleLabel = new Label
        {
            AutoSize = false,
            Text = title,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(183, 92, 0),
            Location = new Point(16, 14),
            Size = new Size(358, 28)
        };
        var bodyLabel = new Label
        {
            AutoSize = false,
            Text = body,
            ForeColor = Color.FromArgb(35, 39, 48),
            Location = new Point(16, 48),
            Size = new Size(358, 96)
        };
        Controls.Add(titleLabel);
        Controls.Add(bodyLabel);

        Shown += (_, _) =>
        {
            var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(
                Math.Max(workingArea.Left, workingArea.Right - Width - 16),
                Math.Max(workingArea.Top, workingArea.Bottom - Height - 16));
            _closeTimer.Start();
        };
        _closeTimer.Tick += (_, _) => Close();
        FormClosed += (_, _) =>
        {
            _closeTimer.Stop();
            _closeTimer.Dispose();
        };
    }

    protected override bool ShowWithoutActivation => true;
}
