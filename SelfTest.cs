namespace WarThunderUIDGuard;

internal static class SelfTest
{
    public static void Run()
    {
        var p = new BlockedPlayer { Uid = "123456", Aliases = ["Ace_Player", "旧昵称"] };
        Assert(Matcher.MatchExactSender([p], "ace_player")?.Player.Uid == "123456", "exact sender is case-insensitive");
        Assert(Matcher.MatchEventText([p], "Ace_Player destroyed a target")?.Alias == "Ace_Player", "event text matches token");
        Assert(Matcher.MatchEventText([p], "NotAce_PlayerX destroyed a target") is null, "event text avoids partial match");
        Assert(Matcher.MatchExactSender([p], "someone") is null, "unknown sender does not match");
        Assert(WarThunderClient.IsAllowedEndpoint(new Uri("http://127.0.0.1:8111/gamechat?lastId=1")), "local gamechat is allowed");
        Assert(WarThunderClient.IsAllowedEndpoint(new Uri("http://127.0.0.1:8111/hudmsg")), "local hudmsg is allowed");
        Assert(!WarThunderClient.IsAllowedEndpoint(new Uri("http://localhost:8111/gamechat")), "hostname indirection is rejected");
        Assert(!WarThunderClient.IsAllowedEndpoint(new Uri("https://127.0.0.1:8111/gamechat")), "https variant is rejected");
        Assert(!WarThunderClient.IsAllowedEndpoint(new Uri("http://127.0.0.1:8112/gamechat")), "other port is rejected");
        Assert(!WarThunderClient.IsAllowedEndpoint(new Uri("http://example.com/gamechat")), "external host is rejected");
        Assert(MainForm.ShouldFailConnection(true, false), "running disconnected client times out");
        Assert(!MainForm.ShouldFailConnection(true, true), "connected client does not time out");
        Assert(!MainForm.ShouldFailConnection(false, false), "stopped client does not time out");
        var now = DateTimeOffset.UtcNow;
        var localData = new AppData
        {
            Language = "zh-CN",
            OneDriveSyncEnabled = true,
            Players = [new BlockedPlayer { Uid = "42", Note = "new", Aliases = ["Alpha"], UpdatedAt = now }]
        };
        var cloudData = new AppData
        {
            Players = [new BlockedPlayer { Uid = "42", Note = "old", Aliases = ["Beta"], UpdatedAt = now.AddMinutes(-1) }]
        };
        var merged = DataStore.Merge(localData, cloudData);
        Assert(merged.Players.Single().Note == "new", "newest note wins during OneDrive merge");
        Assert(merged.Players.Single().Aliases.Count == 2, "nickname histories are combined during OneDrive merge");
        cloudData.DeletedPlayers = [new DeletedPlayer { Uid = "42", DeletedAt = now.AddMinutes(1) }];
        Assert(DataStore.Merge(localData, cloudData).Players.Count == 0, "newer OneDrive deletion is retained");
        localData.Players[0].UpdatedAt = now.AddMinutes(2);
        Assert(DataStore.Merge(localData, cloudData).Players.Count == 1, "newer re-add wins over an old deletion");
        var testDirectory = Path.Combine(Path.GetTempPath(), $"WTUIDGuard-selftest-{Guid.NewGuid():N}");
        try
        {
            var store = new DataStore(testDirectory, "");
            store.Save(new AppData());
            var fallbackData = new AppData { OneDriveSyncEnabled = true };
            var fallbackResult = store.Save(fallbackData);
            Assert(File.Exists(store.DataFile), "local data is saved before OneDrive sync");
            Assert(Directory.GetFiles(store.BackupDirectory, "blacklist-*.json").Length == 1, "local backup is created");
            Assert(fallbackResult.Status == OneDriveSyncStatus.Unavailable, "missing OneDrive falls back to local data");
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
        Assert(Localizer.TranslationSetsMatch(), "Chinese and English translation keys match");
        Assert(Localizer.HasTranslation("Status.ConnectionFailed"), "connection failure is translated");
        Assert(Localizer.HasTranslation("OneDrive.Synced"), "OneDrive status is translated");
        Assert(Localizer.Resolve("en") == AppLanguage.English, "saved English preference is restored");
        Assert(Localizer.Resolve("zh-CN") == AppLanguage.Chinese, "saved Chinese preference is restored");
        Localizer.Current = AppLanguage.English;
        Assert(Localizer.T("Button.StartMonitoring") == "Start monitoring", "English UI text is available");
        Localizer.Current = AppLanguage.Chinese;
        Assert(Localizer.T("Button.StartMonitoring") == "开始监控", "Chinese UI text is available");
        Console.WriteLine("SELF-TEST OK");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("SELF-TEST FAILED: " + message);
    }
}
