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
        Assert(merged.Players.Single().Aliases.SequenceEqual(["Alpha"]),
            "newest nickname list wins during OneDrive merge");
        cloudData.DeletedPlayers = [new DeletedPlayer { Uid = "42", DeletedAt = now.AddMinutes(1) }];
        Assert(DataStore.Merge(localData, cloudData).Players.Count == 0, "newer OneDrive deletion is retained");
        localData.Players[0].UpdatedAt = now.AddMinutes(2);
        Assert(DataStore.Merge(localData, cloudData).Players.Count == 1, "newer re-add wins over an old deletion");
        var locallyDeletedData = new AppData
        {
            Language = "zh-CN",
            OneDriveSyncEnabled = true,
            DeletedPlayers = [new DeletedPlayer { Uid = "42", DeletedAt = now.AddMinutes(3) }]
        };
        var remotelyActiveData = new AppData
        {
            Players = [new BlockedPlayer { Uid = "42", Aliases = ["ServerPlayer"], UpdatedAt = now }]
        };
        var serverAuthoritativeMerge = DataStore.MergeRemoteAuthoritative(locallyDeletedData, remotelyActiveData);
        Assert(serverAuthoritativeMerge.Players.Single().Uid == "42",
            "remote pull restores a server player hidden by a local deletion");
        Assert(serverAuthoritativeMerge.DeletedPlayers.All(deletion => deletion.Uid != "42"),
            "remote pull removes the conflicting local deletion marker");
        var remotelyDeletedData = new AppData
        {
            DeletedPlayers = [new DeletedPlayer { Uid = "42", DeletedAt = now.AddMinutes(4) }]
        };
        Assert(DataStore.MergeRemoteAuthoritative(localData, remotelyDeletedData).Players.Count == 0,
            "remote pull still honors a server deletion");
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
            var pullResult = receiver.PullFromOneDriveAsync(new AppData
                {
                    OneDriveSyncEnabled = true,
                    DeletedPlayers = [new DeletedPlayer { Uid = "99", DeletedAt = now.AddMinutes(5) }]
                })
                .GetAwaiter().GetResult();
            Assert(pullResult.Status == OneDriveSyncStatus.Pulled, "manual pull reports a remote download");
            Assert(pullResult.Data.Players.Single().Uid == "99", "manual pull loads public remote data");
            Assert(pullResult.Data.DeletedPlayers.All(deletion => deletion.Uid != "99"),
                "manual pull treats the server as authoritative over a local deletion");
            Assert(File.Exists(receiver.RemoteCacheFile), "a successful remote pull creates an offline cache");
            Assert(requestedUri?.AbsoluteUri == DataStore.SharedBlacklistUrl,
                "public pull opens the configured read-only mirror");
            var cachedReceiver = new DataStore(
                Path.Combine(testDirectory, "receiver"),
                "",
                (_, _) => Task.FromException<string>(new HttpRequestException("offline")));
            var cachedResult = cachedReceiver.PullFromOneDriveAsync(pullResult.Data).GetAwaiter().GetResult();
            Assert(cachedResult.Status == OneDriveSyncStatus.Cached,
                "the last valid remote cache is used when every network source fails");
        }
        finally
        {
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
        }
        Assert(Localizer.TranslationSetsMatch(), "Chinese and English translation keys match");
        Assert(Localizer.HasTranslation("Status.ConnectionFailed"), "connection failure is translated");
        Assert(Localizer.HasTranslation("OneDrive.Pulled"), "OneDrive pull status is translated");
        Assert(Localizer.HasTranslation("OneDrive.Uploaded"), "OneDrive upload status is translated");
        Assert(Localizer.HasTranslation("OneDrive.Cached"), "offline cache status is translated");
        Assert(Localizer.HasTranslation("Button.UploadOneDrive"), "OneDrive upload button is translated");
        Assert(Localizer.HasTranslation("Button.PullOneDrive"), "OneDrive pull button is translated");
        Assert(Localizer.HasTranslation("Button.SyncNickname"), "nickname sync button is translated");
        Assert(Localizer.HasTranslation("Button.RequestAdd"), "addition request button is translated");
        Assert(Localizer.HasTranslation("Button.CheckUpdate"), "update button is translated");
        Assert(Localizer.HasTranslation("Update.InstallFailed"), "update rollback status is translated");
        Assert(Localizer.HasTranslation("RequestAdd.Subject"), "addition request subject is translated");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://1drv.ms/u/example")), "OneDrive short links are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://public.bl.files.1drv.com/file")), "OneDrive download hosts are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://storage.live.com/file")), "OneDrive storage hosts are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri("https://my.microsoftpersonalcontent.com/file")), "OneDrive personal-content downloads are allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri(DataStore.SharedBlacklistUrl)), "the exact GitHub data mirror is allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri(DataStore.PublicBlacklistCdnUrl)), "the exact CDN data mirror is allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri(DataStore.PublicBlacklistGcoreUrl)), "the exact Gcore data mirror is allowed");
        Assert(DataStore.IsAllowedRemoteUri(new Uri(DataStore.PublicBlacklistFastlyUrl)), "the exact Fastly data mirror is allowed");
        Assert(SignedBlacklistClient.IsDataUri(new Uri(SignedBlacklistClient.DataUrl)),
            "the exact signed server data endpoint is allowed");
        Assert(!SignedBlacklistClient.IsDataUri(new Uri("http://39.105.200.142:8443/other.json")),
            "other paths on the signed server are blocked");
        Assert(!SignedBlacklistClient.IsDataUri(new Uri("http://39.105.200.142:8080/blacklist.json")),
            "other ports on the signed server are blocked");
        Assert(SignedBlacklistClient.ComputePinnedPublicKeyHash() == SignedBlacklistClient.PublicKeySha256,
            "the embedded signed-server public key matches its pinned hash");
        Assert(SignedBlacklistClient.IsUpdateMetadataUri(new Uri(SignedBlacklistClient.UpdateMetadataUrl)),
            "the exact signed update metadata endpoint is allowed");
        Assert(SignedBlacklistClient.IsUpdateSignatureUri(new Uri(SignedBlacklistClient.UpdateSignatureUrl)),
            "the exact signed update signature endpoint is allowed");
        Assert(SignedBlacklistClient.IsUpdateArchiveUri(
                new Uri("http://39.105.200.142:8443/updates/WarThunderUIDGuard-v1.2.3-win-x64.zip")),
            "a versioned server update archive is allowed");
        Assert(!SignedBlacklistClient.IsUpdateArchiveUri(
                new Uri("http://39.105.200.142:8443/updates/other.zip")),
            "unapproved files on the update server are blocked");
        using (var testRsa = System.Security.Cryptography.RSA.Create(2048))
        {
            var signedPayload = System.Text.Encoding.UTF8.GetBytes("signed blacklist test");
            var signature = testRsa.SignData(
                signedPayload,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            Assert(SignedBlacklistClient.VerifySignature(signedPayload, signature, testRsa.ToXmlString(false)),
                "a valid RSA blacklist signature is accepted");
            signedPayload[0] ^= 1;
            Assert(!SignedBlacklistClient.VerifySignature(signedPayload, signature, testRsa.ToXmlString(false)),
                "tampered blacklist data is rejected");
        }
        Assert(!DataStore.IsAllowedRemoteUri(new Uri("https://raw.githubusercontent.com/other/repo/main/data/blacklist.json")),
            "unapproved GitHub data mirrors are blocked");
        var remoteAttempts = new System.Collections.Concurrent.ConcurrentQueue<Uri>();
        var mirrorJson = """{"SchemaVersion":2,"Players":[],"DeletedPlayers":[]}""";
        var fallbackJson = PublicBlacklistDownloader.FetchJsonAsync(
                new Uri(DataStore.SharedBlacklistUrl),
                (uri, _) =>
                {
                    remoteAttempts.Enqueue(uri);
                    return uri.AbsoluteUri == DataStore.SharedBlacklistUrl ||
                           uri.AbsoluteUri == SignedBlacklistClient.DataUrl
                        ? Task.FromException<string>(new HttpRequestException("primary unavailable"))
                        : Task.FromResult(mirrorJson);
                },
                default)
            .GetAwaiter().GetResult();
        Assert(fallbackJson == mirrorJson, "CDN fallback returns valid JSON");
        Assert(remoteAttempts.Select(uri => uri.AbsoluteUri).Contains(DataStore.SharedBlacklistUrl),
            "the fresh GitHub source is attempted");
        Assert(remoteAttempts.Select(uri => uri.AbsoluteUri).Contains(SignedBlacklistClient.DataUrl),
            "the signed server source is attempted first");
        Assert(remoteAttempts.Select(uri => uri.AbsoluteUri).Contains(DataStore.PublicBlacklistGcoreUrl),
            "a fast independent CDN fallback is attempted");
        Assert(!DataStore.IsAllowedRemoteUri(new Uri("https://example.com/file")), "non-Microsoft remote hosts are blocked");
        Assert(NicknameLookupService.BuildLookupUri("28384455").Query == "?name=28384455", "official nickname lookup URL uses UID");
        Assert(NicknameLookupService.IsAllowedNavigation(new Uri("https://warthunder.com/zh/community/searchplayers/?name=1")), "official website navigation is allowed");
        Assert(!NicknameLookupService.IsAllowedNavigation(new Uri("https://example.com/")), "external website navigation is blocked");
        Assert(AutoUpdater.IsAllowedUpdateUri(AutoUpdater.LatestReleaseUri), "the exact GitHub release API is allowed");
        Assert(AutoUpdater.IsAllowedUpdateUri(
                new Uri("http://39.105.200.142:8443/updates/WarThunderUIDGuard-v1.2.3-win-x64.zip")),
            "the exact signed-server update archive pattern is allowed");
        Assert(!AutoUpdater.IsAllowedUpdateUri(
                new Uri("http://39.105.200.142:8443/updates/WarThunderUIDGuard-v1.2.3-win-x64.zip.exe")),
            "other server update extensions are blocked");
        Assert(AutoUpdater.IsAllowedUpdateUri(new Uri("https://github.com/elainasamae/WarThunderUIDGuard/releases/download/v1.2.3/file.zip")),
            "official repository release assets are allowed");
        Assert(!AutoUpdater.IsAllowedUpdateUri(new Uri("https://github.com/other/repository/releases/download/v1.2.3/file.zip")),
            "other repository release assets are blocked");
        Assert(!AutoUpdater.IsAllowedUpdateUri(new Uri("http://github.com/elainasamae/WarThunderUIDGuard/releases/download/v1.2.3/file.zip")),
            "non-HTTPS update downloads are blocked");
        const string releaseJson = """
            {
              "tag_name": "v1.2.3",
              "draft": false,
              "prerelease": false,
              "assets": [
                { "name": "WarThunderUIDGuard-v1.2.3-win-x64.zip", "browser_download_url": "https://github.com/elainasamae/WarThunderUIDGuard/releases/download/v1.2.3/WarThunderUIDGuard-v1.2.3-win-x64.zip" },
                { "name": "WarThunderUIDGuard-v1.2.3-win-x64.zip.sha256.txt", "browser_download_url": "https://github.com/elainasamae/WarThunderUIDGuard/releases/download/v1.2.3/WarThunderUIDGuard-v1.2.3-win-x64.zip.sha256.txt" }
              ]
            }
            """;
        var parsedRelease = AutoUpdater.ParseReleaseJson(releaseJson, new Version(1, 0, 0));
        Assert(parsedRelease?.Version == new Version(1, 2, 3), "newer GitHub releases are parsed");
        Assert(AutoUpdater.ParseReleaseJson(releaseJson, new Version(1, 2, 3)) is null,
            "the current release is not installed again");
        var signedReleaseJson = $$"""
            {
              "schemaVersion": 1,
              "tag": "v1.2.3",
              "archive": "WarThunderUIDGuard-v1.2.3-win-x64.zip",
              "sha256": "{{new string('B', 64)}}",
              "size": 123456
            }
            """;
        var signedRelease = AutoUpdater.ParseSignedReleaseJson(signedReleaseJson, new Version(1, 0, 0));
        Assert(signedRelease?.Version == new Version(1, 2, 3), "newer signed server releases are parsed");
        Assert(signedRelease?.Sources[0].ArchiveUri.AbsoluteUri ==
               "http://39.105.200.142:8443/updates/WarThunderUIDGuard-v1.2.3-win-x64.zip",
            "signed server releases use the exact server archive path");
        Assert(signedRelease?.Sources[0].ExpectedSha256 == new string('B', 64),
            "signed server releases carry a trusted archive checksum");
        Assert(signedRelease?.Sources.Count == 2 &&
               signedRelease.Sources[1].ArchiveUri.AbsoluteUri ==
               "https://github.com/elainasamae/WarThunderUIDGuard/releases/download/v1.2.3/WarThunderUIDGuard-v1.2.3-win-x64.zip",
            "signed server releases retain GitHub only as the second download source");
        Assert(signedRelease?.Sources[1].ExpectedSha256 == new string('B', 64),
            "the GitHub fallback is bound to the server-signed checksum");
        Assert(AutoUpdater.ParseSignedReleaseJson(signedReleaseJson, new Version(1, 2, 3)) is null,
            "the current signed server release is not installed again");
        Assert(AutoUpdater.ParseSha256(
                $"{new string('A', 64)}  WarThunderUIDGuard-v1.2.3-win-x64.zip",
                "WarThunderUIDGuard-v1.2.3-win-x64.zip") == new string('A', 64),
            "published update checksums are parsed");
        var archiveRoot = Path.Combine(Path.GetTempPath(), "WTUIDGuard-update-root");
        Assert(AutoUpdater.ValidateArchiveEntryPath(archiveRoot, "WarThunderUIDGuard.exe") ==
               Path.Combine(archiveRoot, "WarThunderUIDGuard.exe"), "safe archive paths remain inside staging");
        var traversalBlocked = false;
        try { AutoUpdater.ValidateArchiveEntryPath(archiveRoot, "../outside.exe"); }
        catch (InvalidDataException) { traversalBlocked = true; }
        Assert(traversalBlocked, "archive path traversal is blocked");
        var renamedPlayer = new BlockedPlayer { Uid = "7", Aliases = ["OldName", "OlderName"] };
        Assert(MainForm.ReplaceAliasesWithCurrentNickname(renamedPlayer, "NewName"),
            "nickname sync replaces old aliases");
        Assert(renamedPlayer.Aliases.SequenceEqual(["NewName"]),
            "nickname sync keeps only the current nickname");
        Assert(!MainForm.ReplaceAliasesWithCurrentNickname(renamedPlayer, "newname"),
            "same current nickname is unchanged");
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
