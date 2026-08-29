using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WarThunderUIDGuard;

internal enum NicknameLookupStatus
{
    Found,
    NotFound,
    MultipleResults,
    TimedOut,
    Unavailable
}

internal sealed record NicknameLookupResult(NicknameLookupStatus Status, string? Nickname = null);

internal static class NicknameLookupService
{
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(45);

    internal static Uri BuildLookupUri(string uid) =>
        new($"https://warthunder.com/zh/community/searchplayers/?name={Uri.EscapeDataString(uid)}");

    internal static bool IsAllowedNavigation(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "warthunder.com", StringComparison.OrdinalIgnoreCase);

    internal static async Task<NicknameLookupResult> LookupAsync(
        string uid,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OverallTimeout);
        using var host = CreateOffscreenHost();
        using var webView = new WebView2 { Dock = DockStyle.Fill };
        host.Controls.Add(webView);
        host.Show();

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WarThunderUIDGuard",
                "NicknameLookupWebView2");
            var options = new CoreWebView2EnvironmentOptions("--inprivate");
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder,
                options: options);
            await webView.EnsureCoreWebView2Async(environment);
            ConfigureBrowser(webView.CoreWebView2);

            webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!IsAllowedTopLevelNavigation(args.Uri)) args.Cancel = true;
            };
            webView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            webView.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            webView.Source = BuildLookupUri(uid);

            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(1000, timeout.Token);

                try
                {
                    var json = await webView.CoreWebView2.ExecuteScriptAsync(ResultScript);
                    var page = JsonSerializer.Deserialize<LookupPageResult>(json);
                    if (page?.Names.Count == 1)
                    {
                        var nickname = Matcher.Normalize(page.Names[0]);
                        if (nickname.Length > 0)
                            return new NicknameLookupResult(NicknameLookupStatus.Found, nickname);
                    }

                    if (page?.Completed == true)
                    {
                        return new NicknameLookupResult(
                            page.Names.Count > 1
                                ? NicknameLookupStatus.MultipleResults
                                : NicknameLookupStatus.NotFound);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The official page may replace its document while loading.
                }
                catch (JsonException)
                {
                    // Keep polling until the official page returns a stable result.
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NicknameLookupResult(NicknameLookupStatus.TimedOut);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new NicknameLookupResult(NicknameLookupStatus.Unavailable);
        }
        finally
        {
            host.Hide();
        }
    }

    private static OffscreenBrowserHost CreateOffscreenHost() => new()
    {
        FormBorderStyle = FormBorderStyle.None,
        ShowInTaskbar = false,
        StartPosition = FormStartPosition.Manual,
        Location = new Point(-32000, -32000),
        Size = new Size(1024, 768),
        Opacity = 0.01
    };

    private static void ConfigureBrowser(CoreWebView2 browser)
    {
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreBrowserAcceleratorKeysEnabled = false;
        browser.Settings.IsStatusBarEnabled = false;
        browser.Settings.IsWebMessageEnabled = false;
        browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
    }

    private static bool IsAllowedTopLevelNavigation(string value)
    {
        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsAllowedNavigation(uri);
    }

    private const string ResultScript = """
        (() => {
            const names = [...document.querySelectorAll("table a[href*='/community/userinfo/?nick=']")]
                .map(link => {
                    try { return new URL(link.href, location.href).searchParams.get('nick') || link.textContent; }
                    catch { return link.textContent; }
                })
                .map(name => (name || '').trim())
                .filter(Boolean);
            const completed = [...document.querySelectorAll('h3')]
                .some(heading => (heading.textContent || '').includes('搜索结果'));
            return { Names: [...new Set(names)], Completed: completed };
        })();
        """;

    private sealed class LookupPageResult
    {
        public List<string> Names { get; set; } = [];
        public bool Completed { get; set; }
    }

    private sealed class OffscreenBrowserHost : Form
    {
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int wsExNoActivate = 0x08000000;
                var parameters = base.CreateParams;
                parameters.ExStyle |= wsExNoActivate;
                return parameters;
            }
        }
    }
}
