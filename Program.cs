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

        if (args.Contains("--self-test-onedrive", StringComparer.OrdinalIgnoreCase))
        {
            RunOneDriveSelfTest();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void RunOneDriveSelfTest()
    {
        ApplicationConfiguration.Initialize();
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
                json = await OneDriveWebDownloader.FetchJsonAsync(new Uri(DataStore.SharedBlacklistUrl), default);
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
        _ = System.Text.Json.JsonDocument.Parse(json ?? throw new InvalidDataException("No JSON was downloaded."));
        Console.WriteLine($"ONEDRIVE SELF-TEST OK ({json.Length} characters)");
    }
}
