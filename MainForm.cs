namespace WarThunderUIDGuard;

public sealed class MainForm : Form
{
    private readonly DataStore _store;
    private readonly WarThunderClient _client = new();
    private AppData _data;
    private readonly DataGridView _grid = new();
    private readonly TextBox _uid = new();
    private readonly TextBox _aliases = new();
    private readonly TextBox _note = new();
    private readonly Label _status = new();
    private readonly Label _count = new();
    private readonly ListBox _log = new();
    private readonly Button _monitorButton = new();
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _connectionTimeoutTimer = new() { Interval = 10000 };
    private readonly ComboBox _languageSelector = new();
    private readonly Label _languageLabel = new();
    private readonly CheckBox _oneDriveSync = new();
    private readonly Button _uploadOneDriveButton = new();
    private readonly Button _pullOneDriveButton = new();
    private readonly Button _syncNicknameButton = new();
    private readonly List<Detection> _detections = [];
    private string _statusKey = "Status.NotStarted";
    private string _statusPrefix = "○";
    private bool _initializingLanguage;
    private bool _initializingOneDrive;
    private bool _oneDriveBusy;
    private bool _nicknameSyncBusy;
    private CancellationTokenSource? _nicknameSyncCancellation;
    private string? _nicknameSyncStatusKey;
    private object[] _nicknameSyncStatusArgs = [];

    public MainForm()
    {
        _store = new DataStore(remoteFetcher: PublicBlacklistDownloader.FetchJsonAsync);
        MinimumSize = new Size(980, 660);
        Size = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(246, 247, 250);

        DataStoreLoadException? loadError = null;
        try { _data = _store.Load(); }
        catch (DataStoreLoadException ex)
        {
            _data = new AppData();
            loadError = ex;
        }

        Localizer.Current = Localizer.Resolve(_data.Language);

        BuildUi();
        ApplyLocalization();
        RefreshGrid();
        if (loadError is not null)
            MessageBox.Show(
                Localizer.F("Error.DataRecovery", loadError.BackupPath),
                Localizer.T("Error.DataRecoveryTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

        _tray.Icon = SystemIcons.Shield;
        _tray.Text = "War Thunder UID Guard";
        _tray.Visible = true;
        _tray.BalloonTipClicked += (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };

        _client.PlayersProvider = () => _data.Players.ToList();
        _client.ConnectionChanged += (connected, text) => Ui(() =>
        {
            if (connected) _connectionTimeoutTimer.Stop();
            SetStatus(text, connected ? "●" : "○", connected ? Color.SeaGreen : Color.DarkOrange);
        });
        _client.IdentityObserved += (uid, alias, source, detail) => Ui(() => HandleDetection(uid, alias, source, detail));
        _connectionTimeoutTimer.Tick += (_, _) => HandleConnectionTimeout();
        FormClosed += (_, _) =>
        {
            _nicknameSyncCancellation?.Cancel();
            _nicknameSyncCancellation?.Dispose();
            _connectionTimeoutTimer.Stop();
            _connectionTimeoutTimer.Dispose();
            _client.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 67));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Text = "UID Guard",
            Font = new Font("Microsoft YaHei UI", 22, FontStyle.Bold),
            ForeColor = Color.FromArgb(35, 39, 48),
            AutoSize = true,
            Location = new Point(0, 4)
        });
        _status.Text = "";
        _status.AutoSize = true;
        _status.ForeColor = Color.DarkOrange;
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Location = new Point(760, 10);
        _monitorButton.Tag = "Button.StartMonitoring";
        _monitorButton.Size = new Size(142, 36);
        _monitorButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _monitorButton.Location = new Point(885, 34);
        _monitorButton.BackColor = Color.FromArgb(45, 108, 223);
        _monitorButton.ForeColor = Color.White;
        _monitorButton.FlatStyle = FlatStyle.Flat;
        _monitorButton.Click += (_, _) => ToggleMonitor();

        _languageLabel.Tag = "Label.Language";
        _languageLabel.AutoSize = true;
        _languageLabel.ForeColor = Color.DimGray;
        _languageLabel.Top = 45;
        _languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageSelector.Size = new Size(108, 28);
        _languageSelector.Top = 39;
        _languageSelector.Items.AddRange(["中文", "English"]);
        _initializingLanguage = true;
        _languageSelector.SelectedIndex = Localizer.Current == AppLanguage.Chinese ? 0 : 1;
        _initializingLanguage = false;
        _languageSelector.SelectedIndexChanged += (_, _) => ChangeLanguage();

        _oneDriveSync.AutoSize = true;
        _oneDriveSync.Top = 43;
        _oneDriveSync.FlatStyle = FlatStyle.System;
        _initializingOneDrive = true;
        _oneDriveSync.Checked = _data.OneDriveSyncEnabled;
        _initializingOneDrive = false;
        _oneDriveSync.CheckedChanged += (_, _) => ChangeOneDriveSync();
        header.Resize += (_, _) =>
        {
            PositionHeaderControls();
        };
        header.Controls.AddRange([_status, _monitorButton, _languageLabel, _languageSelector, _oneDriveSync]);
        root.Controls.Add(header, 0, 0);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 8)
        };
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        AddField(form, "Label.Uid", _uid, 0);
        AddField(form, "Label.Nickname", _aliases, 2);
        AddField(form, "Label.Note", _note, 4);
        var add = MakeButton("Button.AddOrUpdate", Color.FromArgb(33, 150, 83));
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(8, 7, 0, 7);
        add.Click += (_, _) => AddOrUpdate();
        form.Controls.Add(add, 6, 0);
        var requestAdd = MakeButton("Button.RequestAdd", Color.FromArgb(220, 126, 34));
        requestAdd.Dock = DockStyle.Fill;
        requestAdd.Margin = new Padding(8, 7, 0, 7);
        requestAdd.Click += (_, _) => RequestAddition();
        form.Controls.Add(requestAdd, 6, 1);
        root.Controls.Add(form, 0, 1);

        var gridPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var gridToolbar = new Panel { Dock = DockStyle.Fill };
        _count.AutoSize = true;
        _count.Font = new Font(Font, FontStyle.Bold);
        _count.Location = new Point(0, 9);
        gridToolbar.Controls.Add(_count);

        var toolbarButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 522,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0)
        };
        var remove = MakeButton("Button.DeleteSelected", Color.FromArgb(180, 55, 55));
        remove.Size = new Size(96, 30);
        remove.Click += (_, _) => RemoveSelected();
        var simulate = MakeButton("Button.TestAlert", Color.FromArgb(94, 80, 180));
        simulate.Size = new Size(96, 30);
        simulate.Click += (_, _) => Simulate();
        ConfigureToolbarButton(_uploadOneDriveButton, "Button.UploadOneDrive", Color.FromArgb(33, 150, 83));
        _uploadOneDriveButton.Click += (_, _) => UploadToOneDrive();
        ConfigureToolbarButton(_pullOneDriveButton, "Button.PullOneDrive", Color.FromArgb(45, 108, 223));
        _pullOneDriveButton.Click += async (_, _) => await PullFromOneDriveAsync();
        ConfigureToolbarButton(_syncNicknameButton, "Button.SyncNickname", Color.FromArgb(29, 125, 140));
        _syncNicknameButton.Click += async (_, _) => await SyncSelectedNicknameAsync();
        toolbarButtons.Controls.AddRange([_syncNicknameButton, _uploadOneDriveButton, _pullOneDriveButton, simulate, remove]);
        gridToolbar.Controls.Add(toolbarButtons);
        gridPanel.Controls.Add(gridToolbar, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.Margin = new Padding(0, 0, 0, 8);
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.ColumnHeadersHeight = 34;
        _grid.RowTemplate.Height = 32;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.Columns.Add("uid", "");
        _grid.Columns.Add("aliases", "");
        _grid.Columns.Add("note", "");
        _grid.Columns.Add("updated", "");
        _grid.Columns[0].FillWeight = 18;
        _grid.Columns[1].FillWeight = 36;
        _grid.Columns[2].FillWeight = 28;
        _grid.Columns[3].FillWeight = 18;
        gridPanel.Controls.Add(_grid, 0, 1);
        root.Controls.Add(gridPanel, 0, 2);

        var logPanel = new GroupBox { Tag = "Group.DetectionLog", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _log.Dock = DockStyle.Fill;
        _log.HorizontalScrollbar = true;
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel, 0, 3);
    }

    private static void AddField(TableLayoutPanel panel, string textKey, Control control, int column)
    {
        panel.Controls.Add(new Label { Tag = textKey, AutoSize = true, Anchor = AnchorStyles.Left }, column, 0);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(3, 0, 8, 0);
        panel.Controls.Add(control, column + 1, 0);
    }

    private static Button MakeButton(string textKey, Color color) => new()
    {
        Tag = textKey,
        BackColor = color,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    private static void ConfigureToolbarButton(Button button, string textKey, Color color)
    {
        button.Tag = textKey;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.Size = new Size(96, 30);
    }

    private void ToggleMonitor()
    {
        if (_client.IsRunning)
        {
            _connectionTimeoutTimer.Stop();
            _client.Stop();
            RefreshMonitorButton();
            SetStatus("Status.Stopped", "○", Color.DarkOrange);
        }
        else
        {
            _client.Start();
            _connectionTimeoutTimer.Stop();
            _connectionTimeoutTimer.Start();
            RefreshMonitorButton();
            SetStatus("Status.Connecting", "○", Color.DarkOrange);
        }
    }

    private void HandleConnectionTimeout()
    {
        _connectionTimeoutTimer.Stop();
        if (!ShouldFailConnection(_client.IsRunning, _client.IsConnected)) return;

        _client.Stop();
        RefreshMonitorButton();
        SetStatus("Status.ConnectionFailed", "✕", Color.Firebrick);
    }

    internal static bool ShouldFailConnection(bool isRunning, bool isConnected) =>
        isRunning && !isConnected;

    private void AddOrUpdate()
    {
        var uid = _uid.Text.Trim();
        var aliases = _aliases.Text.Split([';', '；', ',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Matcher.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (uid.Length < 3 || !uid.All(char.IsDigit))
        {
            MessageBox.Show(
                Localizer.T("Error.InvalidUid"),
                Localizer.T("Error.InputTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        if (aliases.Count == 0)
        {
            MessageBox.Show(
                Localizer.T("Error.NicknameRequired"),
                Localizer.T("Error.NicknameRequiredTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var existing = _data.Players.FirstOrDefault(p => p.Uid == uid);
        if (existing is null)
        {
            existing = new BlockedPlayer { Uid = uid };
            _data.Players.Add(existing);
        }
        existing.Note = _note.Text.Trim();
        existing.Aliases = existing.Aliases.Concat(aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        existing.UpdatedAt = DateTimeOffset.Now;
        _data.DeletedPlayers.RemoveAll(item => item.Uid == uid);
        SaveData();
        _uid.Clear(); _aliases.Clear(); _note.Clear();
        RefreshGrid();
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var uid = _grid.SelectedRows[0].Cells[0].Value?.ToString();
        var player = _data.Players.FirstOrDefault(p => p.Uid == uid);
        if (player is null) return;
        if (MessageBox.Show(
                Localizer.F("Confirm.Delete", uid ?? ""),
                Localizer.T("Confirm.DeleteTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        _data.Players.Remove(player);
        _data.DeletedPlayers.RemoveAll(item => item.Uid == player.Uid);
        _data.DeletedPlayers.Add(new DeletedPlayer { Uid = player.Uid, DeletedAt = DateTimeOffset.Now });
        SaveData();
        RefreshGrid();
    }

    private void Simulate()
    {
        BlockedPlayer player;
        if (_grid.SelectedRows.Count > 0)
        {
            var uid = _grid.SelectedRows[0].Cells[0].Value?.ToString();
            player = _data.Players.FirstOrDefault(p => p.Uid == uid) ?? DemoPlayer();
        }
        else player = _data.Players.FirstOrDefault() ?? DemoPlayer();
        var detection = new Detection(
            player,
            player.Aliases.FirstOrDefault() ?? Localizer.T("Demo.Alias"),
            "Source.Test",
            Localizer.T("Demo.Detail"),
            DateTimeOffset.Now);
        ShowAlert(detection);
    }

    private static BlockedPlayer DemoPlayer() => new()
    {
        Uid = "123456789",
        Note = Localizer.T("Demo.Note"),
        Aliases = [Localizer.T("Demo.Alias")]
    };

    private void HandleDetection(string uid, string alias, string source, string detail)
    {
        var player = _data.Players.FirstOrDefault(p => p.Uid == uid);
        if (player is null) return;
        var detection = new Detection(player, alias, source, detail, DateTimeOffset.Now);
        _detections.Insert(0, detection);
        RenderDetectionLog();
        ShowAlert(detection);
    }

    private void ShowAlert(Detection detection)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var note = string.IsNullOrWhiteSpace(detection.Player.Note) ? Localizer.T("Value.None") : detection.Player.Note;
        _tray.ShowBalloonTip(
            10000,
            Localizer.T("Alert.Title"),
            Localizer.F(
                "Alert.Body",
                detection.Player.Uid,
                detection.MatchedAlias,
                Localizer.T(detection.Source),
                note),
            ToolTipIcon.Warning);
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var p in _data.Players.OrderByDescending(p => p.UpdatedAt))
            _grid.Rows.Add(p.Uid, p.AliasSummary, p.Note, p.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        RenderCount();
    }

    private void ChangeLanguage()
    {
        if (_initializingLanguage || _languageSelector.SelectedIndex < 0) return;
        Localizer.Current = _languageSelector.SelectedIndex == 0 ? AppLanguage.Chinese : AppLanguage.English;
        _data.Language = Localizer.Code(Localizer.Current);
        SaveData();
        ApplyLocalization();
    }

    private void ChangeOneDriveSync()
    {
        if (_initializingOneDrive) return;
        _data.OneDriveSyncEnabled = _oneDriveSync.Checked;
        var result = _store.Save(_data);
        _data = result.Data;
        RefreshGrid();
        UpdateOneDriveSyncUi();
    }

    private void UploadToOneDrive()
    {
        if (!_data.OneDriveSyncEnabled) return;
        var result = _store.UploadToOneDrive(_data);
        _data = result.Data;
        if (result.Changed) RefreshGrid();
        UpdateOneDriveSyncUi();
    }

    private async Task PullFromOneDriveAsync()
    {
        if (!_data.OneDriveSyncEnabled || _oneDriveBusy) return;
        _oneDriveBusy = true;
        try
        {
            var pullTask = _store.PullFromOneDriveAsync(_data);
            UpdateOneDriveSyncUi();
            var result = await pullTask;
            _data = result.Data;
            if (result.Changed) RefreshGrid();
        }
        finally
        {
            _oneDriveBusy = false;
            UpdateOneDriveSyncUi();
        }
    }

    private void RequestAddition()
    {
        var uid = _uid.Text.Trim();
        if (uid.Length < 3 || !uid.All(char.IsDigit))
        {
            SetNicknameSyncStatus("Error.InvalidUid");
            return;
        }

        var requestUri = BuildAdditionRequestUri(uid, _aliases.Text.Trim(), _note.Text.Trim());
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = requestUri.AbsoluteUri,
                UseShellExecute = true
            });
            SetNicknameSyncStatus("RequestAdd.Opened");
        }
        catch
        {
            SetNicknameSyncStatus("RequestAdd.NoMailClient");
        }
    }

    internal static Uri BuildAdditionRequestUri(string uid, string aliases, string note)
    {
        var subject = Localizer.F("RequestAdd.Subject", uid);
        var body = Localizer.F(
            "RequestAdd.Body",
            uid,
            string.IsNullOrWhiteSpace(aliases) ? Localizer.T("Value.None") : aliases,
            string.IsNullOrWhiteSpace(note) ? Localizer.T("Value.None") : note);
        return new Uri(
            $"mailto:elainasamae@outlook.com?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
    }

    private async Task SyncSelectedNicknameAsync()
    {
        if (_nicknameSyncBusy) return;
        if (_grid.SelectedRows.Count == 0)
        {
            SetNicknameSyncStatus("Error.SelectPlayer");
            return;
        }

        var uid = _grid.SelectedRows[0].Cells[0].Value?.ToString();
        var player = _data.Players.FirstOrDefault(item => item.Uid == uid);
        if (player is null) return;

        _nicknameSyncBusy = true;
        _syncNicknameButton.Enabled = false;
        _syncNicknameButton.Text = Localizer.T("Button.SyncingNickname");
        SetNicknameSyncStatus("NicknameSync.LookingUp", player.Uid);
        using var cancellation = new CancellationTokenSource();
        _nicknameSyncCancellation = cancellation;
        try
        {
            var result = await NicknameLookupService.LookupAsync(player.Uid, cancellation.Token);
            switch (result.Status)
            {
                case NicknameLookupStatus.Found when !string.IsNullOrWhiteSpace(result.Nickname):
                    if (ReplaceAliasesWithCurrentNickname(player, result.Nickname))
                    {
                        SaveData();
                        SetNicknameSyncStatus("NicknameSync.Found", result.Nickname);
                    }
                    else
                    {
                        SetNicknameSyncStatus("NicknameSync.Unchanged", result.Nickname);
                    }
                    RefreshGrid();
                    break;
                case NicknameLookupStatus.NotFound:
                    SetNicknameSyncStatus("NicknameSync.NoResult");
                    break;
                case NicknameLookupStatus.MultipleResults:
                    SetNicknameSyncStatus("NicknameSync.MultipleResults");
                    break;
                case NicknameLookupStatus.TimedOut:
                    SetNicknameSyncStatus("NicknameSync.TimedOut");
                    break;
                default:
                    SetNicknameSyncStatus("NicknameSync.WebViewUnavailable");
                    break;
            }
        }
        catch (OperationCanceledException) when (IsDisposed || Disposing)
        {
        }
        finally
        {
            if (ReferenceEquals(_nicknameSyncCancellation, cancellation))
                _nicknameSyncCancellation = null;
            _nicknameSyncBusy = false;
            if (!IsDisposed)
            {
                _syncNicknameButton.Enabled = true;
                _syncNicknameButton.Text = Localizer.T("Button.SyncNickname");
            }
        }
    }

    internal static bool ReplaceAliasesWithCurrentNickname(BlockedPlayer player, string nickname)
    {
        var currentNickname = nickname.Trim();
        if (currentNickname.Length == 0) return false;
        if (player.Aliases.Count == 1 &&
            string.Equals(player.Aliases[0], currentNickname, StringComparison.OrdinalIgnoreCase))
            return false;

        player.Aliases = [currentNickname];
        player.UpdatedAt = DateTimeOffset.Now;
        return true;
    }

    private void SetNicknameSyncStatus(string key, params object[] values)
    {
        _nicknameSyncStatusKey = key;
        _nicknameSyncStatusArgs = values;
        RenderCount();
    }

    private void RenderCount()
    {
        var count = Localizer.F("Count.Blacklist", _data.Players.Count);
        if (_nicknameSyncStatusKey is null)
        {
            _count.Text = count;
            return;
        }

        var status = _nicknameSyncStatusArgs.Length == 0
            ? Localizer.T(_nicknameSyncStatusKey)
            : Localizer.F(_nicknameSyncStatusKey, _nicknameSyncStatusArgs);
        _count.Text = $"{count}    ·    {status}";
    }

    private void SaveData()
    {
        var result = _store.Save(_data);
        _data = result.Data;
        UpdateOneDriveSyncUi();
    }

    private void ApplyLocalization()
    {
        Text = Localizer.T("App.Title");
        ApplyTaggedText(this);
        _uid.PlaceholderText = Localizer.T("Placeholder.Uid");
        _aliases.PlaceholderText = Localizer.T("Placeholder.Aliases");
        _note.PlaceholderText = Localizer.T("Placeholder.Note");
        _grid.Columns[0].HeaderText = Localizer.T("Grid.Uid");
        _grid.Columns[1].HeaderText = Localizer.T("Grid.AliasHistory");
        _grid.Columns[2].HeaderText = Localizer.T("Grid.Note");
        _grid.Columns[3].HeaderText = Localizer.T("Grid.UpdatedAt");
        RefreshMonitorButton();
        RenderStatus();
        RefreshGrid();
        RenderDetectionLog();
        UpdateOneDriveSyncUi();
        PositionHeaderControls();
    }

    private static void ApplyTaggedText(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.Tag is string key) control.Text = Localizer.T(key);
            ApplyTaggedText(control);
        }
    }

    private void RefreshMonitorButton() =>
        _monitorButton.Text = Localizer.T(_client.IsRunning ? "Button.StopMonitoring" : "Button.StartMonitoring");

    private void SetStatus(string key, string prefix, Color color)
    {
        _statusKey = key;
        _statusPrefix = prefix;
        _status.ForeColor = color;
        RenderStatus();
    }

    private void RenderStatus()
    {
        _status.Text = $"{_statusPrefix} {Localizer.T(_statusKey)}";
        if (_status.Parent is not null)
            _status.Left = _status.Parent.ClientSize.Width - _status.Width - 8;
    }

    private void RenderDetectionLog()
    {
        _log.BeginUpdate();
        _log.Items.Clear();
        foreach (var detection in _detections)
            _log.Items.Add(
                $"[{detection.DetectedAt:HH:mm:ss}] UID {detection.Player.Uid} / {detection.MatchedAlias} / " +
                $"{Localizer.T(detection.Source)} / {detection.Detail}");
        _log.EndUpdate();
    }

    private void PositionHeaderControls()
    {
        if (_monitorButton.Parent is not Control header) return;
        _status.Left = header.ClientSize.Width - _status.Width - 8;
        _monitorButton.Left = header.ClientSize.Width - _monitorButton.Width - 8;
        _languageSelector.Left = _monitorButton.Left - _languageSelector.Width - 12;
        _languageLabel.Left = _languageSelector.Left - _languageLabel.Width - 6;
        _oneDriveSync.Left = _languageLabel.Left - _oneDriveSync.Width - 18;
    }

    private void UpdateOneDriveSyncUi()
    {
        var status = _data.OneDriveSyncEnabled ? _store.OneDriveStatus : OneDriveSyncStatus.Disabled;
        _oneDriveSync.Text = Localizer.T(status switch
        {
            OneDriveSyncStatus.Ready => "OneDrive.Ready",
            OneDriveSyncStatus.Pulling => "OneDrive.Pulling",
            OneDriveSyncStatus.Uploaded => "OneDrive.Uploaded",
            OneDriveSyncStatus.Pulled => "OneDrive.Pulled",
            OneDriveSyncStatus.Unavailable => "OneDrive.Unavailable",
            OneDriveSyncStatus.Error => "OneDrive.Error",
            _ => "OneDrive.Disabled"
        });
        _oneDriveSync.ForeColor = status switch
        {
            OneDriveSyncStatus.Uploaded or OneDriveSyncStatus.Pulled => Color.SeaGreen,
            OneDriveSyncStatus.Pulling => Color.FromArgb(45, 108, 223),
            OneDriveSyncStatus.Unavailable => Color.DarkOrange,
            OneDriveSyncStatus.Error => Color.Firebrick,
            _ => Color.DimGray
        };
        _uploadOneDriveButton.Enabled = _data.OneDriveSyncEnabled && !_oneDriveBusy && _store.OneDriveDataFile is not null;
        _pullOneDriveButton.Enabled = _data.OneDriveSyncEnabled && !_oneDriveBusy;
        PositionHeaderControls();
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
}
