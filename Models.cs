using System.Text.Json.Serialization;

namespace WarThunderUIDGuard;

public sealed class BlockedPlayer
{
    public string Uid { get; set; } = "";
    public string Note { get; set; } = "";
    public List<string> Aliases { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string AliasSummary => string.Join("、", Aliases);
}

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "";
    public List<BlockedPlayer> Players { get; set; } = [];
}

public sealed record Detection(
    BlockedPlayer Player,
    string MatchedAlias,
    string Source,
    string Detail,
    DateTimeOffset DetectedAt);
