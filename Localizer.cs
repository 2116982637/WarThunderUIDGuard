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
        ["App.Title"] = "War Thunder UID Guard  v0.4.0 Safe",
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
        ["Button.DeleteSelected"] = "删除选中",
        ["Button.TestAlert"] = "测试提醒",
        ["Button.UploadOneDrive"] = "上传本地",
        ["Button.PullOneDrive"] = "拉取同步",
        ["Button.SyncNickname"] = "同步昵称",
        ["OneDrive.Disabled"] = "OneDrive 同步",
        ["OneDrive.Synced"] = "OneDrive 文件已更新",
        ["OneDrive.Unavailable"] = "OneDrive 不可用",
        ["OneDrive.Error"] = "OneDrive 同步失败",
        ["Hint.UidAliases"] = "UID 是永久主键；实时接口只返回昵称，因此至少填写一个当前昵称。对方改名后需补充新昵称。",
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
        ["NicknameSync.LookingUp"] = "正在通过官网查询 UID {0}…",
        ["NicknameSync.Found"] = "已找到当前昵称：{0}，正在自动保存…",
        ["NicknameSync.NoResult"] = "官网未找到该 UID。你可以关闭此窗口。",
        ["NicknameSync.MultipleResults"] = "官网返回多个结果，未自动采用。",
        ["NicknameSync.WaitingForPage"] = "等待官网页面或人工完成验证…",
        ["NicknameSync.WebViewUnavailable"] = "无法启动网页组件，请安装或修复 Microsoft Edge WebView2 Runtime。",
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
        ["App.Title"] = "War Thunder UID Guard  v0.4.0 Safe",
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
        ["Button.DeleteSelected"] = "Delete selected",
        ["Button.TestAlert"] = "Test alert",
        ["Button.UploadOneDrive"] = "Upload local",
        ["Button.PullOneDrive"] = "Pull sync",
        ["Button.SyncNickname"] = "Sync nickname",
        ["OneDrive.Disabled"] = "OneDrive sync",
        ["OneDrive.Synced"] = "OneDrive file updated",
        ["OneDrive.Unavailable"] = "OneDrive unavailable",
        ["OneDrive.Error"] = "OneDrive sync failed",
        ["Hint.UidAliases"] = "UID is the permanent key. The live API returns nicknames only, so add at least one current name and append new names after a rename.",
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
        ["NicknameSync.LookingUp"] = "Looking up UID {0} on the official website…",
        ["NicknameSync.Found"] = "Current nickname found: {0}. Saving automatically…",
        ["NicknameSync.NoResult"] = "The official website did not find this UID. You may close this window.",
        ["NicknameSync.MultipleResults"] = "The official website returned multiple results; none was applied.",
        ["NicknameSync.WaitingForPage"] = "Waiting for the official page or manual verification…",
        ["NicknameSync.WebViewUnavailable"] = "The web component could not start. Install or repair Microsoft Edge WebView2 Runtime.",
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
