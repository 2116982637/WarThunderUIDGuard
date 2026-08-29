using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace WarThunderUIDGuard;

public sealed class DataStoreLoadException(string backupPath, Exception innerException)
    : Exception("Blacklist data could not be read.", innerException)
{
    public string BackupPath { get; } = backupPath;
}

public sealed class DataStore
{
    internal const string SharedBlacklistUrl =
        "https://raw.githubusercontent.com/elainasamae/WarThunderUIDGuard/main/data/blacklist.json";
    internal const string PublicBlacklistCdnUrl =
        "https://cdn.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json";
    internal const string OneDriveSharedBlacklistUrl =
        "https://1drv.ms/u/c/e49649a6a25c7af6/IQB7KQ5tKejhRJNs9D7QYLLQAdxJ7B5Ie9V5pfH1k8-Djnc?e=XXH1dv";
    internal const int MaxRemoteBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _oneDriveRootOverride;
    private readonly Func<Uri, CancellationToken, Task<string>> _remoteFetcher;
    private readonly string _sharedBlacklistUrl;

    public DataStore(
        string? dataDirectory = null,
        string? oneDriveRoot = null,
        Func<Uri, CancellationToken, Task<string>>? remoteFetcher = null,
        string? sharedBlacklistUrl = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarThunderUIDGuard");
        _oneDriveRootOverride = oneDriveRoot;
        _remoteFetcher = remoteFetcher ?? FetchRemoteJsonAsync;
        _sharedBlacklistUrl = sharedBlacklistUrl ?? SharedBlacklistUrl;
    }

    public string DataDirectory { get; }

    public string DataFile => Path.Combine(DataDirectory, "blacklist.json");
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");
    public OneDriveSyncStatus OneDriveStatus { get; private set; } = OneDriveSyncStatus.Disabled;
    public string? LastOneDriveError { get; private set; }
    public string? OneDriveDataFile
    {
        get
        {
            var root = _oneDriveRootOverride ?? FindOneDriveRoot();
            return string.IsNullOrWhiteSpace(root)
                ? null
                : Path.Combine(root, "WarThunderUIDGuard", "blacklist.json");
        }
    }

    public AppData Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(DataFile)) return new AppData();

        try
        {
            var data = Read(DataFile);
            OneDriveStatus = data.OneDriveSyncEnabled ? OneDriveSyncStatus.Ready : OneDriveSyncStatus.Disabled;
            return data;
        }
        catch (Exception ex)
        {
            var backup = Path.Combine(DataDirectory, $"blacklist.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(DataFile, backup, true);
            throw new DataStoreLoadException(backup, ex);
        }
    }

    public OneDriveSyncResult Save(AppData data)
    {
        data.SchemaVersion = 2;
        BackupLocalFile();
        WriteAtomic(DataFile, data);

        OneDriveStatus = data.OneDriveSyncEnabled ? OneDriveSyncStatus.Ready : OneDriveSyncStatus.Disabled;
        LastOneDriveError = null;
        return new OneDriveSyncResult(data, OneDriveStatus, false);
    }

    public OneDriveSyncResult UploadToOneDrive(AppData local)
    {
        if (!local.OneDriveSyncEnabled)
        {
            OneDriveStatus = OneDriveSyncStatus.Disabled;
            return new OneDriveSyncResult(local, OneDriveStatus, false);
        }

        var cloudPath = OneDriveDataFile;
        if (cloudPath is null)
            return SetSyncFailure(local, OneDriveSyncStatus.Unavailable, null);

        try
        {
            AppData merged;
            if (File.Exists(cloudPath))
            {
                var cloud = Read(cloudPath);
                merged = Merge(local, cloud);
            }
            else
            {
                merged = Clone(local);
            }

            var changed = !Equivalent(local, merged);
            BackupLocalFile();
            WriteAtomic(DataFile, merged);
            WriteAtomic(cloudPath, merged);
            OneDriveStatus = OneDriveSyncStatus.Uploaded;
            LastOneDriveError = null;
            return new OneDriveSyncResult(merged, OneDriveStatus, changed);
        }
        catch (Exception ex)
        {
            return SetSyncFailure(local, OneDriveSyncStatus.Error, ex);
        }
    }

    public async Task<OneDriveSyncResult> PullFromOneDriveAsync(
        AppData local,
        CancellationToken cancellationToken = default)
    {
        if (!local.OneDriveSyncEnabled)
        {
            OneDriveStatus = OneDriveSyncStatus.Disabled;
            return new OneDriveSyncResult(local, OneDriveStatus, false);
        }

        if (!Uri.TryCreate(_sharedBlacklistUrl, UriKind.Absolute, out var sharedUri) ||
            !IsAllowedRemoteUri(sharedUri))
            return SetSyncFailure(local, OneDriveSyncStatus.Unavailable, null);

        OneDriveStatus = OneDriveSyncStatus.Pulling;
        LastOneDriveError = null;
        try
        {
            var cloud = ReadJson(await _remoteFetcher(sharedUri, cancellationToken));
            var merged = Merge(local, cloud);
            var changed = !Equivalent(local, merged);
            BackupLocalFile();
            WriteAtomic(DataFile, merged);
            OneDriveStatus = OneDriveSyncStatus.Pulled;
            LastOneDriveError = null;
            return new OneDriveSyncResult(merged, OneDriveStatus, changed);
        }
        catch (Exception ex)
        {
            return SetSyncFailure(local, OneDriveSyncStatus.Error, ex);
        }
    }

    internal static AppData Merge(AppData local, AppData cloud)
    {
        var deletions = local.DeletedPlayers.Concat(cloud.DeletedPlayers)
            .Where(item => !string.IsNullOrWhiteSpace(item.Uid))
            .GroupBy(item => item.Uid, StringComparer.Ordinal)
            .Select(group => new DeletedPlayer
            {
                Uid = group.Key,
                DeletedAt = group.Max(item => item.DeletedAt)
            })
            .ToDictionary(item => item.Uid, StringComparer.Ordinal);

        var players = local.Players.Concat(cloud.Players)
            .Where(player => !string.IsNullOrWhiteSpace(player.Uid))
            .GroupBy(player => player.Uid, StringComparer.Ordinal)
            .Select(group => MergePlayer(group.ToList()))
            .Where(player => !deletions.TryGetValue(player.Uid, out var deletion) || player.UpdatedAt > deletion.DeletedAt)
            .OrderByDescending(player => player.UpdatedAt)
            .ToList();

        var activeUids = players.Select(player => player.Uid).ToHashSet(StringComparer.Ordinal);
        return new AppData
        {
            SchemaVersion = 2,
            Language = local.Language,
            OneDriveSyncEnabled = local.OneDriveSyncEnabled,
            Players = players,
            DeletedPlayers = deletions.Values
                .Where(deletion => !activeUids.Contains(deletion.Uid))
                .OrderByDescending(deletion => deletion.DeletedAt)
                .ToList()
        };
    }

    internal static string? FindOneDriveRoot()
    {
        foreach (var variable in new[] { "OneDriveConsumer", "OneDriveCommercial", "OneDrive" })
        {
            var path = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        }

        using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
        if (accounts is null) return null;
        foreach (var accountName in accounts.GetSubKeyNames().OrderBy(name => name.StartsWith("Personal") ? 0 : 1))
        {
            using var account = accounts.OpenSubKey(accountName);
            var path = account?.GetValue("UserFolder") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        }

        return null;
    }

    internal static bool IsAllowedRemoteUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort) return false;
        var host = uri.Host;
        var path = uri.AbsolutePath;
        var isGitHubMirror = host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) &&
                             path.Equals(
                                 "/elainasamae/WarThunderUIDGuard/main/data/blacklist.json",
                                 StringComparison.OrdinalIgnoreCase);
        var isCdnMirror = host.Equals("cdn.jsdelivr.net", StringComparison.OrdinalIgnoreCase) &&
                          path.Equals(
                              "/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json",
                              StringComparison.OrdinalIgnoreCase);
        return isGitHubMirror ||
               isCdnMirror ||
               host.Equals("1drv.ms", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("onedrive.live.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".onedrive.live.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("1drv.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".1drv.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("onedrive.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".onedrive.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("storage.live.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".storage.live.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".livefilestore.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("storage.msn.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".storage.msn.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("my.microsoftpersonalcontent.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".microsoftpersonalcontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static BlockedPlayer MergePlayer(IReadOnlyCollection<BlockedPlayer> versions)
    {
        var newest = versions.OrderByDescending(player => player.UpdatedAt).First();
        return new BlockedPlayer
        {
            Uid = newest.Uid,
            Note = newest.Note,
            Aliases = newest.Aliases
                .Select(Matcher.Normalize)
                .Where(alias => alias.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CreatedAt = versions.Min(player => player.CreatedAt),
            UpdatedAt = versions.Max(player => player.UpdatedAt)
        };
    }

    private static AppData Read(string path) =>
        ReadJson(File.ReadAllText(path));

    private static AppData ReadJson(string json)
    {
        var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions)
                   ?? throw new InvalidDataException("Blacklist JSON is empty.");
        data.Players ??= [];
        data.DeletedPlayers ??= [];
        foreach (var player in data.Players) player.Aliases ??= [];
        return data;
    }

    private static AppData Clone(AppData data) =>
        JsonSerializer.Deserialize<AppData>(JsonSerializer.Serialize(data, JsonOptions), JsonOptions) ?? new AppData();

    private static bool Equivalent(AppData left, AppData right) =>
        JsonSerializer.Serialize(left, JsonOptions) == JsonSerializer.Serialize(right, JsonOptions);

    private static void WriteAtomic(string path, AppData data)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Data path has no directory.");
        Directory.CreateDirectory(directory);
        var temp = path + $".tmp-{Environment.ProcessId}";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temp, path, true);
    }

    private void BackupLocalFile()
    {
        if (!File.Exists(DataFile)) return;
        Directory.CreateDirectory(BackupDirectory);
        var backup = Path.Combine(BackupDirectory, $"blacklist-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
        File.Copy(DataFile, backup, true);
        foreach (var oldFile in Directory.GetFiles(BackupDirectory, "blacklist-*.json")
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(10))
            File.Delete(oldFile);
    }

    internal static async Task<string> FetchRemoteJsonAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            CheckCertificateRevocationList = true
        };
        using var client = new HttpClient(handler);
        var current = initialUri;

        for (var redirect = 0; redirect <= 6; redirect++)
        {
            if (!IsAllowedRemoteUri(current))
                throw new InvalidDataException("The remote source redirected outside the allowed domains.");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("WarThunderUIDGuard/0.4.4");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true
            };
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location
                               ?? throw new HttpRequestException("The remote source returned a redirect without a location.");
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxRemoteBytes)
                throw new InvalidDataException("The remote blacklist is larger than 1 MB.");

            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await input.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                if (output.Length + read > MaxRemoteBytes)
                    throw new InvalidDataException("The remote blacklist is larger than 1 MB.");
                output.Write(buffer, 0, read);
            }

            return Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
        }

        throw new HttpRequestException("The remote source returned too many redirects.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private OneDriveSyncResult SetSyncFailure(AppData data, OneDriveSyncStatus status, Exception? error)
    {
        OneDriveStatus = status;
        LastOneDriveError = error?.Message;
        return new OneDriveSyncResult(data, status, false);
    }
}

public enum OneDriveSyncStatus
{
    Disabled,
    Ready,
    Pulling,
    Uploaded,
    Pulled,
    Unavailable,
    Error
}

public sealed record OneDriveSyncResult(AppData Data, OneDriveSyncStatus Status, bool Changed);
