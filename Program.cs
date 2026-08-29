namespace WarThunderUIDGuard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            SelfTest.Run();
            return;
        }

        if (args.Contains("--self-test-remote", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("--self-test-onedrive", StringComparer.OrdinalIgnoreCase))
        {
            RunRemoteSelfTest();
            return;
        }

        if (args.Contains("--self-test-update", StringComparer.OrdinalIgnoreCase))
        {
            var release = AutoUpdater.CheckAsync(default).GetAwaiter().GetResult();
            Console.WriteLine(release is null
                ? $"UPDATE SELF-TEST OK (current {AutoUpdater.CurrentVersion} is not older than latest)"
                : $"UPDATE SELF-TEST OK (new release {release.Tag})");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void RunRemoteSelfTest()
    {
        ApplicationConfiguration.Initialize();
        string? signedJson = null;
        string? json = null;
        Exception? error = null;
        using var testHost = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            Opacity = 0.01
        };
        testHost.Shown += async (_, _) =>
        {
            try
            {
                signedJson = await SignedBlacklistClient.FetchAndVerifyAsync(
                    new Uri(SignedBlacklistClient.DataUrl),
                    default);
                json = await PublicBlacklistDownloader.FetchJsonAsync(new Uri(DataStore.SharedBlacklistUrl), default);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                testHost.Close();
            }
        };
        Application.Run(testHost);
        if (error is not null) throw error;
        using var signedDocument = System.Text.Json.JsonDocument.Parse(
            signedJson ?? throw new InvalidDataException("No signed JSON was downloaded."));
        if (!signedDocument.RootElement.TryGetProperty("Players", out var signedPlayers) ||
            signedPlayers.ValueKind != System.Text.Json.JsonValueKind.Array ||
            signedPlayers.GetArrayLength() == 0)
            throw new InvalidDataException("The signed public blacklist contains no players.");
        using var document = System.Text.Json.JsonDocument.Parse(
            json ?? throw new InvalidDataException("No JSON was downloaded."));
        if (!document.RootElement.TryGetProperty("Players", out var players) ||
            players.ValueKind != System.Text.Json.JsonValueKind.Array ||
            players.GetArrayLength() == 0)
            throw new InvalidDataException("The public blacklist contains no players.");

        Console.WriteLine(
            $"REMOTE SELF-TEST OK ({players.GetArrayLength()} players, signed server verified, {json.Length} characters)");
    }
}
