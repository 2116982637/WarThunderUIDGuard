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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string? _oneDriveRootOverride;
    private DateTime _lastCloudWriteUtc;
    private long _lastCloudLength = -1;

    public DataStore(string? dataDirectory = null, string? oneDriveRoot = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarThunderUIDGuard");
        _oneDriveRootOverride = oneDriveRoot;
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
            if (!data.OneDriveSyncEnabled)
            {
                OneDriveStatus = OneDriveSyncStatus.Disabled;
                return data;
            }

            return Synchronize(data).Data;
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

        if (!data.OneDriveSyncEnabled)
        {
            OneDriveStatus = OneDriveSyncStatus.Disabled;
            LastOneDriveError = null;
            return new OneDriveSyncResult(data, OneDriveStatus, false);
        }

        return Synchronize(data);
    }

    public OneDriveSyncResult Synchronize(AppData local)
    {
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
            WriteAtomic(DataFile, merged);
            WriteAtomic(cloudPath, merged);
            RememberCloudStamp(cloudPath);
            OneDriveStatus = OneDriveSyncStatus.Synced;
            LastOneDriveError = null;
            return new OneDriveSyncResult(merged, OneDriveStatus, changed);
        }
        catch (Exception ex)
        {
            return SetSyncFailure(local, OneDriveSyncStatus.Error, ex);
        }
    }

    public OneDriveSyncResult RefreshFromOneDrive(AppData local)
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
            if (!File.Exists(cloudPath)) return Synchronize(local);

            var info = new FileInfo(cloudPath);
            if (info.LastWriteTimeUtc == _lastCloudWriteUtc && info.Length == _lastCloudLength)
            {
                OneDriveStatus = OneDriveSyncStatus.Synced;
                return new OneDriveSyncResult(local, OneDriveStatus, false);
            }

            var merged = Merge(local, Read(cloudPath));
            var changed = !Equivalent(local, merged);
            WriteAtomic(DataFile, merged);
            if (changed) WriteAtomic(cloudPath, merged);
            RememberCloudStamp(cloudPath);
            OneDriveStatus = OneDriveSyncStatus.Synced;
            LastOneDriveError = null;
            return new OneDriveSyncResult(merged, OneDriveStatus, changed);
        }
        catch (Exception ex)
        {
            return SetSyncFailure(local, OneDriveSyncStatus.Error, ex);
        }
    }

    public OneDriveSyncResult PullFromOneDrive(AppData local)
    {
        if (!local.OneDriveSyncEnabled)
        {
            OneDriveStatus = OneDriveSyncStatus.Disabled;
            return new OneDriveSyncResult(local, OneDriveStatus, false);
        }

        var cloudPath = OneDriveDataFile;
        if (cloudPath is null || !File.Exists(cloudPath))
            return SetSyncFailure(local, OneDriveSyncStatus.Unavailable, null);

        try
        {
            var merged = Merge(local, Read(cloudPath));
            var changed = !Equivalent(local, merged);
            WriteAtomic(DataFile, merged);
            RememberCloudStamp(cloudPath);
            OneDriveStatus = OneDriveSyncStatus.Synced;
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

    private static BlockedPlayer MergePlayer(IReadOnlyCollection<BlockedPlayer> versions)
    {
        var newest = versions.OrderByDescending(player => player.UpdatedAt).First();
        return new BlockedPlayer
        {
            Uid = newest.Uid,
            Note = newest.Note,
            Aliases = versions.SelectMany(player => player.Aliases)
                .Select(Matcher.Normalize)
                .Where(alias => alias.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CreatedAt = versions.Min(player => player.CreatedAt),
            UpdatedAt = versions.Max(player => player.UpdatedAt)
        };
    }

    private static AppData Read(string path) =>
        JsonSerializer.Deserialize<AppData>(File.ReadAllText(path), JsonOptions) ?? new AppData();

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

    private void RememberCloudStamp(string cloudPath)
    {
        var info = new FileInfo(cloudPath);
        _lastCloudWriteUtc = info.LastWriteTimeUtc;
        _lastCloudLength = info.Length;
    }

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
    Synced,
    Unavailable,
    Error
}

public sealed record OneDriveSyncResult(AppData Data, OneDriveSyncStatus Status, bool Changed);
