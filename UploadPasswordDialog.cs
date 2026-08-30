namespace WarThunderUIDGuard;

internal sealed class UploadPasswordDialog : Form
{
    private readonly TextBox _password = new()
    {
        UseSystemPasswordChar = true,
        Width = 300,
        MaxLength = 256
    };

    private UploadPasswordDialog()
    {
        Text = Localizer.T("AdminUpload.Title");
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Background;
        Font = new Font("Microsoft YaHei UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 202);
        MinimumSize = new Size(476, 241);

        var label = new Label
        {
            Text = Localizer.T("AdminUpload.Password"),
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = UiTheme.TextSecondary,
            Margin = new Padding(0, 0, 0, 8),
            TextAlign = ContentAlignment.BottomLeft
        };
        _password.Dock = DockStyle.Fill;
        _password.Margin = new Padding(0);
        UiTheme.StyleTextBox(_password);

        var ok = new Button
        {
            Text = Localizer.T("Common.OK"),
            DialogResult = DialogResult.OK,
            Size = new Size(104, 36),
            Margin = new Padding(8, 0, 0, 0)
        };
        var cancel = new Button
        {
            Text = Localizer.T("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(104, 36),
            Margin = new Padding(8, 0, 0, 0)
        };
        UiTheme.StyleButton(ok, UiTheme.Primary);
        UiTheme.StyleButton(cancel, UiTheme.TextSecondary);

        var actions = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = UiTheme.Surface,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0),
            Padding = new Padding(0),
            WrapContents = false
        };
        actions.Controls.Add(cancel);
        actions.Controls.Add(ok);

        var card = new UiCardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(20),
            Padding = new Padding(22)
        };
        var layout = new TableLayoutPanel
        {
            BackColor = UiTheme.Surface,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_password, 0, 1);
        layout.Controls.Add(actions, 0, 3);
        card.Controls.Add(layout);
        Controls.Add(card);

        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _password.Focus();
    }

    internal static string? Request(IWin32Window owner)
    {
        using var dialog = new UploadPasswordDialog();
        if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
        var value = dialog._password.Text;
        dialog._password.Clear();
        return value;
    }
}
