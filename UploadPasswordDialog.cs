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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 138);

        var label = new Label
        {
            Text = Localizer.T("AdminUpload.Password"),
            AutoSize = true,
            Location = new Point(20, 18)
        };
        _password.Location = new Point(20, 43);

        var ok = new Button
        {
            Text = Localizer.T("Common.OK"),
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Location = new Point(158, 92)
        };
        var cancel = new Button
        {
            Text = Localizer.T("Common.Cancel"),
            DialogResult = DialogResult.Cancel,
            Size = new Size(90, 30),
            Location = new Point(252, 92)
        };

        Controls.AddRange([label, _password, ok, cancel]);
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
