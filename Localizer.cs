using System.Globalization;

namespace WarThunderUIDGuard;

public enum AppLanguage
{
    Chinese,
    English
}

public static class Localizer
{
    private static readonly IReadOnlyDictionary<string, string> Chinese = new Dictionary<string, string>
    {
        ["App.Title"] = "War Thunder UID Guard  v0.6.1 Safe",
        ["Label.Language"] = "语言",
        ["Label.Uid"] = "UID",
        ["Label.Nickname"] = "昵称",
        ["Label.Note"] = "备注",
        ["Placeholder.Uid"] = "数字账号 UID",
        ["Placeholder.Aliases"] = "当前昵称；旧昵称（用分号分隔）",
        ["Placeholder.Note"] = "可选",
        ["Button.StartMonitoring"] = "开始监控",
        ["Button.StopMonitoring"] = "停止监控",
        ["Button.AddOrUpdate"] = "添加 / 更新",
        ["Button.RequestAdd"] = "申请添加",
        ["Button.DeleteSelected"] = "删除选中",
        ["Button.TestAlert"] = "测试提醒",
        ["Button.UploadOneDrive"] = "管理员上传",
        ["Button.PullOneDrive"] = "拉取同步",
        ["Button.SyncNickname"] = "同步昵称",
        ["Button.SyncingNickname"] = "正在同步…",
        ["Button.CheckUpdate"] = "检查更新",
        ["Button.Updating"] = "正在更新…",
        ["Update.Checking"] = "正在检查更新…",
        ["Update.UpToDate"] = "已是最新版本",
        ["Update.Downloading"] = "正在下载并校验更新 {0}…",
        ["Update.Restarting"] = "已安装更新 {0}，正在重启…",
        ["Update.Failed"] = "自动更新失败，当前版本未修改",
        ["Update.InstallFailed"] = "更新安装失败，已恢复原版本",
        ["OneDrive.Disabled"] = "远程手动同步",
        ["OneDrive.Ready"] = "远程手动同步",
        ["OneDrive.Pulling"] = "正在拉取远程数据…",
        ["OneDrive.Uploaded"] = "管理员数据已上传",
        ["OneDrive.Pulled"] = "远程数据已拉取",
        ["OneDrive.Cached"] = "远程线路暂不可用，已使用上次缓存",
        ["OneDrive.Unavailable"] = "同步源不可用",
        ["OneDrive.Error"] = "远程同步失败",
        ["Grid.Uid"] = "UID",
        ["Grid.AliasHistory"] = "昵称历史",
        ["Grid.Note"] = "备注",
        ["Grid.UpdatedAt"] = "更新时间",
        ["Group.DetectionLog"] = "检测记录（仅本次运行）",
        ["Count.Blacklist"] = "黑名单：{0} 人",
        ["Status.NotStarted"] = "尚未开始监控",
        ["Status.Connecting"] = "正在连接…",
        ["Status.Connected"] = "已连接 War Thunder 8111",
        ["Status.Stopped"] = "监控已停止",
        ["Status.WaitingForBattle"] = "等待游戏进入对局…",
        ["Status.ConnectionFailed"] = "连接失败",
        ["Error.InputTitle"] = "输入错误",
        ["Error.InvalidUid"] = "UID 只能包含数字。",
        ["Error.NicknameRequiredTitle"] = "需要昵称",
        ["Error.NicknameRequired"] = "实时接口不返回 UID，请至少填写一个当前昵称。",
        ["Error.DataRecoveryTitle"] = "数据恢复",
        ["Error.DataRecovery"] = "黑名单文件无法读取，已备份到：{0}",
        ["Error.SelectPlayerTitle"] = "请选择玩家",
        ["Error.SelectPlayer"] = "请先在黑名单表格中选择一名玩家。",
        ["NicknameSync.Title"] = "从战争雷霆官网同步昵称",
        ["NicknameSync.LookingUp"] = "正在后台查询 UID {0}…",
        ["NicknameSync.Found"] = "昵称已更新：{0}",
        ["NicknameSync.Unchanged"] = "当前昵称没有变化：{0}",
        ["NicknameSync.NoResult"] = "官网未找到该 UID。",
        ["NicknameSync.MultipleResults"] = "官网返回多个结果，未自动采用。",
        ["NicknameSync.TimedOut"] = "官网查询超时，请稍后重试。",
        ["NicknameSync.WebViewUnavailable"] = "无法启动网页组件，请安装或修复 Microsoft Edge WebView2 Runtime。",
        ["RequestAdd.Opened"] = "已打开邮件，请确认后发送。",
        ["RequestAdd.NoMailClient"] = "未找到默认邮件程序。",
        ["RequestAdd.Subject"] = "War Thunder UID Guard 黑名单添加申请 - UID {0}",
        ["RequestAdd.Body"] = "申请添加以下玩家：\n\nUID：{0}\n昵称：{1}\n备注：{2}\n\n请管理员审核后决定是否加入公共黑名单。",
        ["Confirm.DeleteTitle"] = "确认删除",
        ["Confirm.Delete"] = "删除 UID {0}？",
        ["Alert.Title"] = "发现黑名单玩家",
        ["Alert.Body"] = "UID：{0}\n昵称：{1}\n来源：{2}\n备注：{3}",
        ["Value.None"] = "无",
        ["Source.Chat"] = "聊天",
        ["Source.CombatEvent"] = "战斗事件",
        ["Source.HudEvent"] = "HUD 事件",
        ["Source.Test"] = "测试",
        ["Demo.Alias"] = "示例昵称",
        ["Demo.Note"] = "测试记录",
        ["Demo.Detail"] = "测试提醒"
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["App.Title"] = "War Thunder UID Guard  v0.6.1 Safe",
        ["Label.Language"] = "Language",
        ["Label.Uid"] = "UID",
        ["Label.Nickname"] = "Nickname",
        ["Label.Note"] = "Note",
        ["Placeholder.Uid"] = "Numeric account UID",
        ["Placeholder.Aliases"] = "Current; previous names (semicolon-separated)",
        ["Placeholder.Note"] = "Optional",
        ["Button.StartMonitoring"] = "Start monitoring",
        ["Button.StopMonitoring"] = "Stop monitoring",
        ["Button.AddOrUpdate"] = "Add / Update",
        ["Button.RequestAdd"] = "Request addition",
        ["Button.DeleteSelected"] = "Delete selected",
        ["Button.TestAlert"] = "Test alert",
        ["Button.UploadOneDrive"] = "Admin upload",
        ["Button.PullOneDrive"] = "Pull sync",
        ["Button.SyncNickname"] = "Sync nickname",
        ["Button.SyncingNickname"] = "Syncing…",
        ["Button.CheckUpdate"] = "Check update",
        ["Button.Updating"] = "Updating…",
        ["Update.Checking"] = "Checking for updates…",
        ["Update.UpToDate"] = "This is the latest version",
        ["Update.Downloading"] = "Downloading and verifying update {0}…",
        ["Update.Restarting"] = "Update {0} installed; restarting…",
        ["Update.Failed"] = "Automatic update failed; the current version was not changed",
        ["Update.InstallFailed"] = "Update installation failed; the previous version was restored",
        ["OneDrive.Disabled"] = "Manual remote sync",
        ["OneDrive.Ready"] = "Manual remote sync",
        ["OneDrive.Pulling"] = "Pulling remote data…",
        ["OneDrive.Uploaded"] = "Admin data uploaded",
        ["OneDrive.Pulled"] = "Remote data pulled",
        ["OneDrive.Cached"] = "Remote sources unavailable; using the last cache",
        ["OneDrive.Unavailable"] = "Sync source unavailable",
        ["OneDrive.Error"] = "Remote sync failed",
        ["Grid.Uid"] = "UID",
        ["Grid.AliasHistory"] = "Nickname history",
        ["Grid.Note"] = "Note",
        ["Grid.UpdatedAt"] = "Updated",
        ["Group.DetectionLog"] = "Detection log (this run only)",
        ["Count.Blacklist"] = "Blacklist: {0}",
        ["Status.NotStarted"] = "Monitoring has not started",
        ["Status.Connecting"] = "Connecting…",
        ["Status.Connected"] = "Connected to War Thunder 8111",
        ["Status.Stopped"] = "Monitoring stopped",
        ["Status.WaitingForBattle"] = "Waiting for the game to enter a battle…",
        ["Status.ConnectionFailed"] = "Connection failed",
        ["Error.InputTitle"] = "Invalid input",
        ["Error.InvalidUid"] = "UID must contain digits only.",
        ["Error.NicknameRequiredTitle"] = "Nickname required",
        ["Error.NicknameRequired"] = "The live API does not return UIDs. Add at least one current nickname.",
        ["Error.DataRecoveryTitle"] = "Data recovery",
        ["Error.DataRecovery"] = "The blacklist file could not be read and was backed up to: {0}",
        ["Error.SelectPlayerTitle"] = "Select a player",
        ["Error.SelectPlayer"] = "Select a player in the blacklist table first.",
        ["NicknameSync.Title"] = "Sync nickname from the War Thunder website",
        ["NicknameSync.LookingUp"] = "Looking up UID {0} in the background…",
        ["NicknameSync.Found"] = "Nickname updated: {0}",
        ["NicknameSync.Unchanged"] = "The current nickname is unchanged: {0}",
        ["NicknameSync.NoResult"] = "The official website did not find this UID.",
        ["NicknameSync.MultipleResults"] = "The official website returned multiple results; none was applied.",
        ["NicknameSync.TimedOut"] = "The official lookup timed out. Try again later.",
        ["NicknameSync.WebViewUnavailable"] = "The web component could not start. Install or repair Microsoft Edge WebView2 Runtime.",
        ["RequestAdd.Opened"] = "The email draft is open. Review it and send when ready.",
        ["RequestAdd.NoMailClient"] = "No default email application was found.",
        ["RequestAdd.Subject"] = "War Thunder UID Guard blacklist request - UID {0}",
        ["RequestAdd.Body"] = "Please review this player for addition:\n\nUID: {0}\nNickname: {1}\nNote: {2}\n\nThe administrator will decide whether to add this player to the public blacklist.",
        ["Confirm.DeleteTitle"] = "Confirm deletion",
        ["Confirm.Delete"] = "Delete UID {0}?",
        ["Alert.Title"] = "Blacklisted player detected",
        ["Alert.Body"] = "UID: {0}\nNickname: {1}\nSource: {2}\nNote: {3}",
        ["Value.None"] = "None",
        ["Source.Chat"] = "Chat",
        ["Source.CombatEvent"] = "Combat event",
        ["Source.HudEvent"] = "HUD event",
        ["Source.Test"] = "Test",
        ["Demo.Alias"] = "ExamplePlayer",
        ["Demo.Note"] = "Test record",
        ["Demo.Detail"] = "Test alert"
    };

    public static AppLanguage Current { get; set; } = Resolve(null);

    public static string T(string key)
    {
        var dictionary = Current == AppLanguage.Chinese ? Chinese : English;
        return dictionary.TryGetValue(key, out var text) ? text : key;
    }

    public static string F(string key, params object[] values) =>
        string.Format(CultureInfo.CurrentCulture, T(key), values);

    public static AppLanguage Resolve(string? savedCode)
    {
        if (string.Equals(savedCode, "zh-CN", StringComparison.OrdinalIgnoreCase)) return AppLanguage.Chinese;
        if (string.Equals(savedCode, "en", StringComparison.OrdinalIgnoreCase)) return AppLanguage.English;
        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Chinese
            : AppLanguage.English;
    }

    public static string Code(AppLanguage language) => language == AppLanguage.Chinese ? "zh-CN" : "en";

    internal static bool HasTranslation(string key) => Chinese.ContainsKey(key) && English.ContainsKey(key);
    internal static bool TranslationSetsMatch() =>
        Chinese.Keys.OrderBy(key => key).SequenceEqual(English.Keys.OrderBy(key => key));
}
