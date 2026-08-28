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
        ["App.Title"] = "War Thunder UID Guard  v0.2.0 Safe",
        ["App.SafetySubtitle"] = "安全模式 · 仅允许 127.0.0.1:8111 · 不读画面/进程/内存 · 不注入游戏",
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
        ["App.Title"] = "War Thunder UID Guard  v0.2.0 Safe",
        ["App.SafetySubtitle"] = "Safe mode · 127.0.0.1:8111 only · No screen/process/memory access · No injection",
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
