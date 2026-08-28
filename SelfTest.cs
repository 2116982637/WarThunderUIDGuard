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
        Console.WriteLine("SELF-TEST OK");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("SELF-TEST FAILED: " + message);
    }
}
