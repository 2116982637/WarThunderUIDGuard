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
    private readonly System.Windows.Forms.Timer _connectionTimeoutTimer = new() { Interval = 10000 };
    private readonly ComboBox _languageSelector = new();
    private readonly Label _languageLabel = new();
    private readonly CheckBox _oneDriveSync = new();
    private readonly Button _uploadOneDriveButton = new();
    private readonly Button _pullOneDriveButton = new();
    private readonly Button _syncNicknameButton = new();
    private readonly Button _updateButton = new();
    private readonly Label _subtitle = new();
    private readonly Label _remoteSyncStatus = new();
    private readonly Label _activityStatus = new();
    private readonly List<Detection> _detections = [];
    private string _statusKey = "Status.NotStarted";
    private string _statusPrefix = "○";
    private bool _initializingLanguage;
    private bool _initializingOneDrive;
    private bool _oneDriveBusy;
    private bool _nicknameSyncBusy;
    private bool _updateBusy;
    private bool _updateExitPending;
    private CancellationTokenSource? _nicknameSyncCancellation;
    private string? _nicknameSyncStatusKey;
    private object[] _nicknameSyncStatusArgs = [];

    public MainForm()
    {
        _store = new DataStore(remoteFetcher: PublicBlacklistDownloader.FetchJsonAsync);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.25f);
        BackColor = UiTheme.Background;

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
        if (AutoUpdater.TakeInstallerFailure() is not null)
            SetNicknameSyncStatus("Update.InstallFailed");
        if (loadError is not null)
            MessageBox.Show(
                Localizer.F("Error.DataRecovery", loadError.BackupPath),
                Localizer.T("Error.DataRecoveryTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

        _client.PlayersProvider = () => _data.Players.ToList();
        _client.ConnectionChanged += (connected, text) => Ui(() =>
        {
            if (connected) _connectionTimeoutTimer.Stop();
            SetStatus(text, connected ? "●" : "○", connected ? UiTheme.Success : UiTheme.Warning);
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
        };
    }

    private void BuildUi()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            RowCount = 4,
            ColumnCount = 1,
            BackColor = UiTheme.Background,
            AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 67));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        Controls.Add(root);

        var header = CreateCard(new Padding(20, 12, 20, 12), new Padding(0, 0, 0, 12));
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(headerLayout);

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        brand.Controls.Add(new Label
        {
            Text = "UID Guard",
            Font = new Font("Microsoft YaHei UI", 20.5f, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0)
        }, 0, 0);
        _subtitle.Tag = "App.Subtitle";
        _subtitle.ForeColor = UiTheme.TextSecondary;
        _subtitle.AutoSize = false;
        _subtitle.Dock = DockStyle.Fill;
        _subtitle.TextAlign = ContentAlignment.TopLeft;
        _subtitle.Margin = new Padding(1, 2, 0, 0);
        brand.Controls.Add(_subtitle, 0, 1);
        headerLayout.Controls.Add(brand, 0, 0);

        var headerActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        headerActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        headerActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statusRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _remoteSyncStatus.AutoSize = false;
        _remoteSyncStatus.Dock = DockStyle.Fill;
        _remoteSyncStatus.TextAlign = ContentAlignment.MiddleLeft;
        _remoteSyncStatus.AutoEllipsis = true;
        _remoteSyncStatus.ForeColor = UiTheme.TextSecondary;
        _remoteSyncStatus.Margin = new Padding(0);
        statusRow.Controls.Add(_remoteSyncStatus, 0, 0);

        _status.Text = "";
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.ForeColor = UiTheme.Warning;
        _status.Font = new Font(Font, FontStyle.Bold);
        _status.Margin = new Padding(0);
        statusRow.Controls.Add(_status, 1, 0);
        headerActions.Controls.Add(statusRow, 0, 0);

        var quickControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };
        _monitorButton.Tag = "Button.StartMonitoring";
        _monitorButton.Size = new Size(170, 38);
        _monitorButton.Margin = new Padding(12, 0, 0, 0);
        UiTheme.StyleButton(_monitorButton, UiTheme.Primary);
        _monitorButton.Click += (_, _) => ToggleMonitor();

        _languageLabel.Tag = "Label.Language";
        _languageLabel.AutoSize = true;
        _languageLabel.ForeColor = UiTheme.TextSecondary;
        _languageLabel.Margin = new Padding(8, 9, 6, 0);
        _languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageSelector.Size = new Size(112, 34);
        _languageSelector.Margin = new Padding(0, 2, 0, 0);
        UiTheme.StyleComboBox(_languageSelector);
        _languageSelector.Items.AddRange(["中文", "English"]);
        _initializingLanguage = true;
        _languageSelector.SelectedIndex = Localizer.Current == AppLanguage.Chinese ? 0 : 1;
        _initializingLanguage = false;
        _languageSelector.SelectedIndexChanged += (_, _) => ChangeLanguage();

        _oneDriveSync.AutoSize = true;
        _oneDriveSync.Tag = "Label.RemoteSync";
        _oneDriveSync.Margin = new Padding(18, 9, 0, 0);
        UiTheme.StyleCheckBox(_oneDriveSync);
        _initializingOneDrive = true;
        _oneDriveSync.Checked = _data.OneDriveSyncEnabled;
        _initializingOneDrive = false;
        _oneDriveSync.CheckedChanged += (_, _) => ChangeOneDriveSync();
        quickControls.Controls.AddRange([_monitorButton, _languageSelector, _languageLabel, _oneDriveSync]);
        headerActions.Controls.Add(quickControls, 0, 1);
        headerLayout.Controls.Add(headerActions, 1, 0);
        root.Controls.Add(header, 0, 0);

        var inputCard = CreateCard(new Padding(16, 12, 16, 14), new Padding(0, 0, 0, 12));
        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        form.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        AddField(form, "Label.Uid", _uid, 0);
        AddField(form, "Label.Nickname", _aliases, 1);
        AddField(form, "Label.Note", _note, 2);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 0, 0)
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var add = MakeButton("Button.AddOrUpdate", UiTheme.Success);
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(0, 0, 0, 4);
        add.Padding = new Padding(0);
        add.Click += (_, _) => AddOrUpdate();
        var requestAdd = MakeButton("Button.RequestAdd", UiTheme.Warning);
        requestAdd.Dock = DockStyle.Fill;
        requestAdd.Margin = new Padding(0, 4, 0, 0);
        requestAdd.Padding = new Padding(0);
        requestAdd.Click += (_, _) => RequestAddition();
        actions.Controls.Add(add, 0, 0);
        actions.Controls.Add(requestAdd, 0, 1);
        form.Controls.Add(actions, 3, 0);
        form.SetRowSpan(actions, 2);
        inputCard.Controls.Add(form);
        root.Controls.Add(inputCard, 0, 1);

        var gridCard = CreateCard(new Padding(14, 10, 14, 14), new Padding(0, 0, 0, 12));
        var gridPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        gridPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var gridStatusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        gridStatusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        gridStatusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        gridStatusRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _count.AutoSize = false;
        _count.Dock = DockStyle.Fill;
        _count.TextAlign = ContentAlignment.MiddleLeft;
        _count.Font = new Font(Font, FontStyle.Bold);
        _count.ForeColor = UiTheme.TextPrimary;
        _count.Margin = new Padding(4, 0, 0, 0);
        _activityStatus.AutoSize = false;
        _activityStatus.Dock = DockStyle.Fill;
        _activityStatus.TextAlign = ContentAlignment.MiddleRight;
        _activityStatus.AutoEllipsis = true;
        _activityStatus.ForeColor = UiTheme.TextSecondary;
        _activityStatus.Margin = new Padding(8, 0, 4, 0);
        gridStatusRow.Controls.Add(_count, 0, 0);
        gridStatusRow.Controls.Add(_activityStatus, 1, 0);
        gridPanel.Controls.Add(gridStatusRow, 0, 0);

        var toolbarButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 8)
        };
        foreach (var width in new[] { 16f, 16f, 15f, 16f, 15f, 18f })
            toolbarButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        toolbarButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var remove = MakeButton("Button.DeleteSelected", UiTheme.Danger);
        remove.Click += (_, _) => RemoveSelected();
        var simulate = MakeButton("Button.TestAlert", UiTheme.Purple);
        simulate.Click += (_, _) => Simulate();
        ConfigureToolbarButton(_uploadOneDriveButton, "Button.UploadOneDrive", UiTheme.Success);
        _uploadOneDriveButton.Click += async (_, _) => await UploadToServerAsync();
        ConfigureToolbarButton(_pullOneDriveButton, "Button.PullOneDrive", UiTheme.Primary);
        _pullOneDriveButton.Click += async (_, _) => await PullFromOneDriveAsync();
        ConfigureToolbarButton(_syncNicknameButton, "Button.SyncNickname", UiTheme.Teal);
        _syncNicknameButton.Click += async (_, _) => await SyncSelectedNicknameAsync();
        ConfigureToolbarButton(_updateButton, "Button.CheckUpdate", UiTheme.Purple);
        _updateButton.Click += async (_, _) => await UpdateApplicationAsync();
        var toolbarItems = new[] { _syncNicknameButton, _uploadOneDriveButton, _pullOneDriveButton, _updateButton, simulate, remove };
        for (var i = 0; i < toolbarItems.Length; i++)
        {
            toolbarItems[i].Dock = DockStyle.Fill;
            toolbarItems[i].Margin = new Padding(i == 0 ? 4 : 5, 0, i == toolbarItems.Length - 1 ? 4 : 5, 0);
            toolbarButtons.Controls.Add(toolbarItems[i], i, 0);
        }
        gridPanel.Controls.Add(toolbarButtons, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.Margin = new Padding(4, 0, 4, 0);
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.ColumnHeadersHeight = 36;
        _grid.RowTemplate.Height = 34;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        UiTheme.StyleDataGridView(_grid);
        _grid.Columns.Add("uid", "");
        _grid.Columns.Add("aliases", "");
        _grid.Columns.Add("note", "");
        _grid.Columns.Add("updated", "");
        _grid.Columns[0].FillWeight = 18;
        _grid.Columns[1].FillWeight = 36;
        _grid.Columns[2].FillWeight = 28;
        _grid.Columns[3].FillWeight = 18;
        gridPanel.Controls.Add(_grid, 0, 2);
        gridCard.Controls.Add(gridPanel);
        root.Controls.Add(gridCard, 0, 2);

        var logCard = CreateCard(new Padding(14, 10, 14, 14), new Padding(0));
        var logPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        logPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logPanel.Controls.Add(new Label
        {
            Tag = "Group.DetectionLog",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = UiTheme.TextPrimary,
            Margin = new Padding(4, 0, 0, 0)
        }, 0, 0);
        _log.Dock = DockStyle.Fill;
        _log.HorizontalScrollbar = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = UiTheme.Surface;
        _log.ForeColor = UiTheme.TextSecondary;
        _log.IntegralHeight = false;
        _log.Margin = new Padding(4, 0, 4, 0);
        logPanel.Controls.Add(_log, 0, 1);
        logCard.Controls.Add(logPanel);
        root.Controls.Add(logCard, 0, 3);

        ResumeLayout(performLayout: true);
    }

    private static void AddField(TableLayoutPanel panel, string textKey, Control control, int column)
    {
        panel.Controls.Add(new Label
        {
            Tag = textKey,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.TextSecondary,
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
            Margin = new Padding(4, 0, 8, 0)
        }, column, 0);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(4, 2, 8, 4);
        if (control is TextBox textBox) UiTheme.StyleTextBox(textBox);
        panel.Controls.Add(control, column, 1);
    }

    private static Panel CreateCard(Padding padding, Padding margin) => new UiCardPanel
    {
        Dock = DockStyle.Fill,
        Padding = padding,
        Margin = margin,
        BackColor = UiTheme.Surface
    };

    private static Button MakeButton(string textKey, Color color)
    {
        var button = new Button { Tag = textKey };
        UiTheme.StyleButton(button, color);
        return button;
    }

    private static void ConfigureToolbarButton(Button button, string textKey, Color color)
    {
        button.Tag = textKey;
        UiTheme.StyleButton(button, color, compact: true);
    }

    private void ToggleMonitor()
    {
        if (_client.IsRunning)
        {
            _connectionTimeoutTimer.Stop();
            _client.Stop();
            RefreshMonitorButton();
            SetStatus("Status.Stopped", "○", UiTheme.Warning);
        }
        else
        {
            _client.Start();
            _connectionTimeoutTimer.Stop();
            _connectionTimeoutTimer.Start();
            RefreshMonitorButton();
            SetStatus("Status.Connecting", "○", UiTheme.Warning);
        }
    }

    private void HandleConnectionTimeout()
    {
        _connectionTimeoutTimer.Stop();
        if (!ShouldFailConnection(_client.IsRunning, _client.IsConnected)) return;

        _client.Stop();
        RefreshMonitorButton();
        SetStatus("Status.ConnectionFailed", "✕", UiTheme.Danger);
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
        var alert = new DetectionAlertForm(
            Localizer.T("Alert.Title"),
            Localizer.F(
                "Alert.Body",
                detection.Player.Uid,
                detection.MatchedAlias,
                Localizer.T(detection.Source),
                note));
        alert.Show();
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

    private async Task UploadToServerAsync()
    {
        if (!_data.OneDriveSyncEnabled || _oneDriveBusy) return;
        var password = UploadPasswordDialog.Request(this);
        if (password is null) return;

        _oneDriveBusy = true;
        try
        {
            var uploadTask = _store.UploadToServerAsync(_data, password);
            password = "";
            UpdateOneDriveSyncUi();
            var result = await uploadTask;
            _data = result.Data;
            if (result.Changed) RefreshGrid();
        }
        finally
        {
            password = "";
            _oneDriveBusy = false;
            UpdateOneDriveSyncUi();
        }
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

    private async Task UpdateApplicationAsync()
    {
        if (_updateBusy) return;
        _updateBusy = true;
        _updateButton.Enabled = false;
        _updateButton.Text = Localizer.T("Button.Updating");
        SetNicknameSyncStatus("Update.Checking");
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var release = await AutoUpdater.CheckAsync(cancellation.Token);
            if (release is null)
            {
                SetNicknameSyncStatus("Update.UpToDate");
                return;
            }

            SetNicknameSyncStatus("Update.Downloading", release.Tag);
            await AutoUpdater.PrepareAndLaunchAsync(release, Environment.ProcessId, cancellation.Token);
            _updateExitPending = true;
            SetNicknameSyncStatus("Update.Restarting", release.Tag);
            Application.Exit();
        }
        catch (OperationCanceledException)
        {
            SetNicknameSyncStatus("Update.Failed");
        }
        catch
        {
            SetNicknameSyncStatus("Update.Failed");
        }
        finally
        {
            _updateBusy = false;
            if (!_updateExitPending && !IsDisposed)
            {
                _updateButton.Enabled = true;
                _updateButton.Text = Localizer.T("Button.CheckUpdate");
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
        _count.Text = Localizer.F("Count.Blacklist", _data.Players.Count);
        if (_nicknameSyncStatusKey is null)
        {
            _activityStatus.Text = "";
            return;
        }

        _activityStatus.Text = _nicknameSyncStatusArgs.Length == 0
            ? Localizer.T(_nicknameSyncStatusKey)
            : Localizer.F(_nicknameSyncStatusKey, _nicknameSyncStatusArgs);
        _activityStatus.ForeColor = _nicknameSyncStatusKey.StartsWith("Error.", StringComparison.Ordinal) ||
                                    _nicknameSyncStatusKey.EndsWith("Failed", StringComparison.Ordinal)
            ? UiTheme.Danger
            : UiTheme.TextSecondary;
    }

    private void SaveData()
    {
        var result = _store.Save(_data);
        _data = result.Data;
        UpdateOneDriveSyncUi();
    }

    private void ApplyLocalization()
    {
        Text = Localizer.F("App.Title", AutoUpdater.CurrentVersion);
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

    private void UpdateOneDriveSyncUi()
    {
        var status = _data.OneDriveSyncEnabled ? _store.OneDriveStatus : OneDriveSyncStatus.Disabled;
        _remoteSyncStatus.Text = Localizer.T(status switch
        {
            OneDriveSyncStatus.Ready => "OneDrive.Ready",
            OneDriveSyncStatus.Uploading => "OneDrive.Uploading",
            OneDriveSyncStatus.Pulling => "OneDrive.Pulling",
            OneDriveSyncStatus.Uploaded => "OneDrive.Uploaded",
            OneDriveSyncStatus.UploadUnauthorized => "OneDrive.UploadUnauthorized",
            OneDriveSyncStatus.UploadConflict => "OneDrive.UploadConflict",
            OneDriveSyncStatus.UploadRateLimited => "OneDrive.UploadRateLimited",
            OneDriveSyncStatus.Pulled => "OneDrive.Pulled",
            OneDriveSyncStatus.Cached => "OneDrive.Cached",
            OneDriveSyncStatus.Unavailable => "OneDrive.Unavailable",
            OneDriveSyncStatus.Error => "OneDrive.Error",
            _ => "OneDrive.Disabled"
        });
        _remoteSyncStatus.ForeColor = status switch
        {
            OneDriveSyncStatus.Uploaded or OneDriveSyncStatus.Pulled => UiTheme.Success,
            OneDriveSyncStatus.Uploading or OneDriveSyncStatus.Pulling => UiTheme.Primary,
            OneDriveSyncStatus.Cached or OneDriveSyncStatus.Unavailable => UiTheme.Warning,
            OneDriveSyncStatus.UploadConflict or OneDriveSyncStatus.UploadRateLimited => UiTheme.Warning,
            OneDriveSyncStatus.UploadUnauthorized or OneDriveSyncStatus.Error => UiTheme.Danger,
            _ => UiTheme.TextSecondary
        };
        _oneDriveSync.ForeColor = UiTheme.TextSecondary;
        _uploadOneDriveButton.Enabled = _data.OneDriveSyncEnabled && !_oneDriveBusy;
        _pullOneDriveButton.Enabled = _data.OneDriveSyncEnabled && !_oneDriveBusy;
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
}
