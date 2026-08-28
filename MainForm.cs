namespace WarThunderUIDGuard;

public sealed class MainForm : Form
{
    private readonly DataStore _store = new();
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

    public MainForm()
    {
        Text = "War Thunder UID Guard  v0.1.2 Safe";
        MinimumSize = new Size(980, 660);
        Size = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Color.FromArgb(246, 247, 250);

        try { _data = _store.Load(); }
        catch (InvalidDataException ex)
        {
            _data = new AppData();
            MessageBox.Show(ex.Message, "数据恢复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        BuildUi();
        RefreshGrid();

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
            _status.Text = connected ? "● " + text : "○ " + text;
            _status.ForeColor = connected ? Color.SeaGreen : Color.DarkOrange;
        });
        _client.IdentityObserved += (uid, alias, source, detail) => Ui(() => HandleDetection(uid, alias, source, detail));
        FormClosed += (_, _) =>
        {
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
        header.Controls.Add(new Label
        {
            Text = "安全模式 · 仅允许 127.0.0.1:8111 · 不读画面/进程/内存 · 不注入游戏",
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(3, 48)
        });
        _status.Text = "○ 尚未开始监控";
        _status.AutoSize = true;
        _status.ForeColor = Color.DarkOrange;
        _status.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _status.Location = new Point(760, 10);
        _monitorButton.Text = "开始监控";
        _monitorButton.Size = new Size(118, 36);
        _monitorButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _monitorButton.Location = new Point(885, 34);
        _monitorButton.BackColor = Color.FromArgb(45, 108, 223);
        _monitorButton.ForeColor = Color.White;
        _monitorButton.FlatStyle = FlatStyle.Flat;
        _monitorButton.Click += (_, _) => ToggleMonitor();
        header.Resize += (_, _) =>
        {
            _status.Left = header.ClientSize.Width - _status.Width - 8;
            _monitorButton.Left = header.ClientSize.Width - _monitorButton.Width - 8;
        };
        header.Controls.AddRange([_status, _monitorButton]);
        root.Controls.Add(header, 0, 0);

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, Padding = new Padding(0, 8, 0, 8) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        AddField(form, "UID", _uid, 0);
        AddField(form, "昵称", _aliases, 2);
        AddField(form, "备注", _note, 4);
        _aliases.PlaceholderText = "当前昵称；旧昵称（用分号分隔）";
        _uid.PlaceholderText = "数字账号 UID";
        _note.PlaceholderText = "可选";
        var add = MakeButton("添加 / 更新", Color.FromArgb(33, 150, 83));
        add.Dock = DockStyle.Fill;
        add.Margin = new Padding(8, 0, 0, 32);
        add.Click += (_, _) => AddOrUpdate();
        form.Controls.Add(add, 6, 0);
        var hint = new Label
        {
            Text = "UID 是永久主键；实时接口只返回昵称，因此至少填写一个当前昵称。对方改名后需补充新昵称。",
            ForeColor = Color.FromArgb(160, 88, 0),
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        form.SetColumnSpan(hint, 6);
        form.Controls.Add(hint, 0, 1);
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
            Width = 210,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 0)
        };
        var remove = MakeButton("删除选中", Color.FromArgb(180, 55, 55));
        remove.Size = new Size(96, 30);
        remove.Click += (_, _) => RemoveSelected();
        var simulate = MakeButton("测试提醒", Color.FromArgb(94, 80, 180));
        simulate.Size = new Size(96, 30);
        simulate.Click += (_, _) => Simulate();
        toolbarButtons.Controls.AddRange([simulate, remove]);
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
        _grid.Columns.Add("uid", "UID");
        _grid.Columns.Add("aliases", "昵称历史");
        _grid.Columns.Add("note", "备注");
        _grid.Columns.Add("updated", "更新时间");
        _grid.Columns[0].FillWeight = 18;
        _grid.Columns[1].FillWeight = 36;
        _grid.Columns[2].FillWeight = 28;
        _grid.Columns[3].FillWeight = 18;
        gridPanel.Controls.Add(_grid, 0, 1);
        root.Controls.Add(gridPanel, 0, 2);

        var logPanel = new GroupBox { Text = "检测记录（仅本次运行）", Dock = DockStyle.Fill, Padding = new Padding(8) };
        _log.Dock = DockStyle.Fill;
        _log.HorizontalScrollbar = true;
        logPanel.Controls.Add(_log);
        root.Controls.Add(logPanel, 0, 3);
    }

    private static void AddField(TableLayoutPanel panel, string label, Control control, int column)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, column, 0);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 0, 8, 32);
        panel.Controls.Add(control, column + 1, 0);
    }

    private static Button MakeButton(string text, Color color) => new()
    {
        Text = text,
        BackColor = color,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat
    };

    private void ToggleMonitor()
    {
        if (_client.IsRunning)
        {
            _client.Stop();
            _monitorButton.Text = "开始监控";
        }
        else
        {
            _client.Start();
            _monitorButton.Text = "停止监控";
            _status.Text = "○ 正在连接…";
        }
    }

    private void AddOrUpdate()
    {
        var uid = _uid.Text.Trim();
        var aliases = _aliases.Text.Split([';', '；', ',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Matcher.Normalize).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (uid.Length < 3 || !uid.All(char.IsDigit))
        {
            MessageBox.Show("UID 只能包含数字。", "输入错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (aliases.Count == 0)
        {
            MessageBox.Show("实时接口不返回 UID，请至少填写一个当前昵称。", "需要昵称", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        _store.Save(_data);
        _uid.Clear(); _aliases.Clear(); _note.Clear();
        RefreshGrid();
    }

    private void RemoveSelected()
    {
        if (_grid.SelectedRows.Count == 0) return;
        var uid = _grid.SelectedRows[0].Cells[0].Value?.ToString();
        var player = _data.Players.FirstOrDefault(p => p.Uid == uid);
        if (player is null) return;
        if (MessageBox.Show($"删除 UID {uid}？", "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _data.Players.Remove(player);
        _store.Save(_data);
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
        var detection = new Detection(player, player.Aliases.FirstOrDefault() ?? "示例昵称", "测试", "测试提醒", DateTimeOffset.Now);
        ShowAlert(detection);
    }

    private static BlockedPlayer DemoPlayer() => new() { Uid = "123456789", Note = "测试记录", Aliases = ["示例昵称"] };

    private void HandleDetection(string uid, string alias, string source, string detail)
    {
        var player = _data.Players.FirstOrDefault(p => p.Uid == uid);
        if (player is null) return;
        var detection = new Detection(player, alias, source, detail, DateTimeOffset.Now);
        _log.Items.Insert(0, $"[{detection.DetectedAt:HH:mm:ss}] UID {uid} / {alias} / {source} / {detail}");
        ShowAlert(detection);
    }

    private void ShowAlert(Detection detection)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var note = string.IsNullOrWhiteSpace(detection.Player.Note) ? "无" : detection.Player.Note;
        _tray.ShowBalloonTip(
            10000,
            "发现黑名单玩家",
            $"UID：{detection.Player.Uid}\n昵称：{detection.MatchedAlias}\n来源：{detection.Source}\n备注：{note}",
            ToolTipIcon.Warning);
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();
        foreach (var p in _data.Players.OrderByDescending(p => p.UpdatedAt))
            _grid.Rows.Add(p.Uid, p.AliasSummary, p.Note, p.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        _count.Text = $"黑名单：{_data.Players.Count} 人";
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(action); else action();
    }
}
