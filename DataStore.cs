using System.Text.Json;

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

    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WarThunderUIDGuard");

    public string DataFile => Path.Combine(DataDirectory, "blacklist.json");

    public AppData Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(DataFile)) return new AppData();

        try
        {
            return JsonSerializer.Deserialize<AppData>(File.ReadAllText(DataFile), JsonOptions)
                   ?? new AppData();
        }
        catch (Exception ex)
        {
            var backup = Path.Combine(DataDirectory, $"blacklist.invalid-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(DataFile, backup, true);
            throw new DataStoreLoadException(backup, ex);
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = DataFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, JsonOptions));
        File.Move(temp, DataFile, true);
    }
}
