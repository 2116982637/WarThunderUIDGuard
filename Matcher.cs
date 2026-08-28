using System.Text.RegularExpressions;

namespace WarThunderUIDGuard;

public static class Matcher
{
    public static (BlockedPlayer Player, string Alias)? MatchExactSender(
        IEnumerable<BlockedPlayer> players,
        string? sender)
    {
        var normalized = Normalize(sender);
        if (normalized.Length == 0) return null;

        foreach (var player in players)
        foreach (var alias in player.Aliases)
            if (string.Equals(Normalize(alias), normalized, StringComparison.OrdinalIgnoreCase))
                return (player, alias);

        return null;
    }

    public static (BlockedPlayer Player, string Alias)? MatchEventText(
        IEnumerable<BlockedPlayer> players,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var player in players)
        foreach (var alias in player.Aliases.OrderByDescending(a => a.Length))
        {
            var clean = Normalize(alias);
            if (clean.Length < 3) continue;
            var pattern = $@"(?<![\p{{L}}\p{{N}}_@-]){Regex.Escape(clean)}(?![\p{{L}}\p{{N}}_@-])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return (player, alias);
        }

        return null;
    }

    public static string Normalize(string? value) => (value ?? "").Trim().Normalize();
}
