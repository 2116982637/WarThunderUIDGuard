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
            Assert(fallbackResult.Status == OneDriveSyncStatus.Ready, "enabling manual sync does not upload automatically");

            var oneDriveRoot = Path.Combine(testDirectory, "OneDrive");
            Directory.CreateDirectory(oneDriveRoot);
            var uploader = new DataStore(Path.Combine(testDirectory, "uploader"), oneDriveRoot);
            var uploadData = new AppData
            {
                OneDriveSyncEnabled = true,
                Players = [new BlockedPlayer { Uid = "99", Aliases = ["CloudPlayer"], UpdatedAt = now }]
            };
            Assert(uploader.UploadToOneDrive(uploadData).Status == OneDriveSyncStatus.Uploaded, "manual upload writes OneDrive data");
            var remoteJson = File.ReadAllText(uploader.OneDriveDataFile!);
            Uri? requestedUri = null;
            var receiver = new DataStore(
                Path.Combine(testDirectory, "receiver"),
                "",
                (uri, _) =>
                {
                    requestedUri = uri;
                    return Task.FromResult(remoteJson);
                });
            var pullResult = receiver.PullFromOneDriveAsync(new AppData { OneDriveSyncEnabled = true })
                .GetAwaiter().GetResult();
            Assert(pullResult.Status == OneDriveSyncStatus.Pulled, "manual pull reports a remote download");
            Assert(pullResult.Data.Players.Single().Uid == "99", "manual pull loads public OneDrive data");
            Assert(requestedUri?.AbsoluteUri == DataStore.SharedBlacklistUrl,
                "public OneDrive pull opens the configured read-only share link");
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
        Assert(Localizer.TranslationSetsMatch(), "Chinese and English translation keys match");
        Assert(Localizer.HasTranslation("Status.ConnectionFailed"), "connection failure is translated");
        Assert(Localizer.HasTranslation("OneDrive.Pulled"), "OneDrive pull status is translated");
        Assert(Localizer.HasTranslation("OneDrive.Uploaded"), "OneDrive upload status is translated");
        Assert(Localizer.HasTranslation("Button.UploadOneDrive"), "OneDrive upload button is translated");
        Assert(Localizer.HasTranslation("Button.PullOneDrive"), "OneDrive pull button is translated");
        Assert(Localizer.HasTranslation("Button.SyncNickname"), "nickname sync button is translated");
        Assert(Localizer.HasTranslation("Button.RequestAdd"), "addition request button is translated");
        Assert(Localizer.HasTranslation("RequestAdd.Subject"), "addition request subject is translated");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://1drv.ms/u/example")), "OneDrive short links are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://public.bl.files.1drv.com/file")), "OneDrive download hosts are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://storage.live.com/file")), "OneDrive storage hosts are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://my.microsoftpersonalcontent.com/file")), "OneDrive personal-content downloads are allowed");
        Assert(!DataStore.IsAllowedRemoteUri(new Uri("https://example.com/file")), "non-Microsoft remote hosts are blocked");
        Assert(NicknameLookupService.BuildLookupUri("28384455").Query == "?name=28384455", "official nickname lookup URL uses UID");
        Assert(NicknameLookupService.IsAllowedNavigation(new Uri("https://warthunder.com/zh/community/searchplayers/?name=1")), "official website navigation is allowed");
        Assert(!NicknameLookupService.IsAllowedNavigation(new Uri("https://example.com/")), "external website navigation is blocked");
        var requestUri = MainForm.BuildAdditionRequestUri("28384455", "Player Name", "test note");
        Assert(requestUri.Scheme == Uri.UriSchemeMailto, "addition request uses the local email client");
        Assert(requestUri.AbsoluteUri.StartsWith("mailto:elainasamae@outlook.com", StringComparison.OrdinalIgnoreCase),
            "addition request is addressed to the administrator");
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
