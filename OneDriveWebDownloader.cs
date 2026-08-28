using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WarThunderUIDGuard;

internal static class OneDriveWebDownloader
{
    private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(35);

    internal static async Task<string> FetchJsonAsync(Uri shareUri, CancellationToken cancellationToken)
    {
        if (!DataStore.IsAllowedRemoteUri(shareUri))
            throw new InvalidDataException("The OneDrive share URL is not on an allowed Microsoft domain.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OverallTimeout);

        var tempPath = Path.Combine(Path.GetTempPath(), $"WTUIDGuard-OneDrive-{Guid.NewGuid():N}.json");
        using var host = CreateOffscreenHost();
        using var webView = new WebView2 { Dock = DockStyle.Fill };
        host.Controls.Add(webView);
        host.Show();

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WarThunderUIDGuard",
                "OneDriveWebView2");
            var options = new CoreWebView2EnvironmentOptions("--inprivate");
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder,
                options: options);
            await webView.EnsureCoreWebView2Async(environment);

            ConfigureBrowser(webView.CoreWebView2);
            var downloadCompletion = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var downloadStarted = false;

            webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!IsAllowedTopLevelNavigation(args.Uri)) args.Cancel = true;
            };
            webView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            webView.CoreWebView2.DownloadStarting += (_, args) =>
            {
                if (downloadStarted ||
                    !Uri.TryCreate(args.DownloadOperation.Uri, UriKind.Absolute, out var downloadUri) ||
                    !DataStore.IsAllowedRemoteUri(downloadUri))
                {
                    args.Cancel = true;
                    downloadCompletion.TrySetException(
                        new InvalidDataException("OneDrive attempted a download from an unapproved domain."));
                    return;
                }

                downloadStarted = true;
                if (File.Exists(tempPath)) File.Delete(tempPath);
                args.ResultFilePath = tempPath;
                args.Handled = true;
                args.DownloadOperation.StateChanged += (_, _) =>
                {
                    switch (args.DownloadOperation.State)
                    {
                        case CoreWebView2DownloadState.Completed:
                            downloadCompletion.TrySetResult(tempPath);
                            break;
                        case CoreWebView2DownloadState.Interrupted:
                            downloadCompletion.TrySetException(new IOException(
                                $"OneDrive download was interrupted: {args.DownloadOperation.InterruptReason}."));
                            break;
                    }
                };
            };

            webView.Source = shareUri;
            await TriggerDownloadAsync(webView, () => downloadStarted, timeout.Token);

            var completedPath = await downloadCompletion.Task.WaitAsync(DownloadTimeout, timeout.Token);
            var info = new FileInfo(completedPath);
            if (!info.Exists || info.Length == 0)
                throw new InvalidDataException("OneDrive downloaded an empty file.");
            if (info.Length > DataStore.MaxRemoteBytes)
                throw new InvalidDataException("The remote blacklist is larger than 1 MB.");

            return (await File.ReadAllTextAsync(completedPath, timeout.Token)).TrimStart('\uFEFF');
        }
        finally
        {
            host.Hide();
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static Form CreateOffscreenHost() => new()
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
    }

    private static bool IsAllowedTopLevelNavigation(string value)
    {
        if (string.Equals(value, "about:blank", StringComparison.OrdinalIgnoreCase)) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && DataStore.IsAllowedRemoteUri(uri);
    }

    private static async Task TriggerDownloadAsync(
        WebView2 webView,
        Func<bool> downloadStarted,
        CancellationToken cancellationToken)
    {
        const string script = """
            (() => {
                if (window.__wtuidGuardDownloadTriggered) return true;
                const buttons = [...document.querySelectorAll('button')];
                const button = buttons.find(item => {
                    const label = (item.getAttribute('aria-label') || '').trim().toLowerCase();
                    const title = (item.getAttribute('title') || '').trim().toLowerCase();
                    return label === '下载' || label === 'download' || title === '下载' || title === 'download';
                });
                if (button && !button.disabled && button.getAttribute('aria-disabled') !== 'true') {
                    window.__wtuidGuardDownloadTriggered = true;
                    button.click();
                    return true;
                }

                const now = Date.now();
                if (!window.__wtuidGuardMoreMenuOpenedAt || now - window.__wtuidGuardMoreMenuOpenedAt > 2500) {
                    const more = buttons.find(item => {
                        const text = [
                            item.getAttribute('aria-label'),
                            item.getAttribute('title'),
                            item.textContent
                        ].filter(Boolean).join(' ').trim().toLowerCase();
                        return text.includes('其他操作') ||
                            text.includes('所选项目') ||
                            text.includes('other actions') ||
                            text.includes('selected item');
                    });
                    if (more && !more.disabled && more.getAttribute('aria-disabled') !== 'true') {
                        window.__wtuidGuardMoreMenuOpenedAt = now;
                        more.click();
                    }
                }

                return false;
            })();
            """;

        for (var attempt = 0; attempt < 55; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (downloadStarted()) return;

            string result;
            try
            {
                result = await webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (InvalidOperationException) when (webView.CoreWebView2 is not null)
            {
                // The page can briefly replace its document while opening the file preview.
                await Task.Delay(750, cancellationToken);
                continue;
            }

            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
            {
                for (var wait = 0; wait < 30 && !downloadStarted(); wait++)
                    await Task.Delay(250, cancellationToken);
                if (downloadStarted()) return;
                throw new InvalidOperationException("OneDrive did not start the file download.");
            }

            await Task.Delay(750, cancellationToken);
        }

        string diagnostics;
        try
        {
            diagnostics = await webView.CoreWebView2.ExecuteScriptAsync("""
                (() => ({
                    title: document.title,
                    host: location.host,
                    state: document.readyState,
                    buttons: [...document.querySelectorAll('button')]
                        .map(item => ({
                            ariaLabel: item.getAttribute('aria-label') || '',
                            title: item.getAttribute('title') || '',
                            popup: item.getAttribute('aria-haspopup') || '',
                            role: item.getAttribute('role') || '',
                            automationId: item.getAttribute('data-automationid') || '',
                            text: (item.textContent || '').trim()
                        }))
                        .slice(0, 20)
                }))();
                """);
        }
        catch
        {
            diagnostics = "unavailable";
        }

        throw new TimeoutException($"The OneDrive download button did not become available. Page: {diagnostics}");
    }
}
