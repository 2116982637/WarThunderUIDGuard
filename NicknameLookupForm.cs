using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WarThunderUIDGuard;

public sealed class NicknameLookupForm : Form
{
    private readonly string _uid;
    private readonly Label _status = new();
    private readonly WebView2 _webView = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 1500 };
    private bool _checking;
    private bool _completed;

    public NicknameLookupForm(string uid)
    {
        _uid = uid;
        Text = Localizer.T("NicknameSync.Title");
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 600);
        Size = new Size(1040, 760);
        Font = new Font("Microsoft YaHei UI", 9);

        _status.Dock = DockStyle.Top;
        _status.Height = 40;
        _status.Padding = new Padding(12, 10, 12, 0);
        _status.ForeColor = Color.DimGray;
        _status.Text = Localizer.F("NicknameSync.LookingUp", _uid);

        _webView.Dock = DockStyle.Fill;
        Controls.Add(_webView);
        Controls.Add(_status);

        _pollTimer.Tick += async (_, _) => await CheckResultAsync();
        Shown += async (_, _) => await InitializeBrowserAsync();
        FormClosed += (_, _) =>
        {
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _webView.Dispose();
        };
    }

    public string? Nickname { get; private set; }

    internal static Uri BuildLookupUri(string uid) =>
        new($"https://warthunder.com/zh/community/searchplayers/?name={Uri.EscapeDataString(uid)}");

    internal static bool IsAllowedNavigation(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "warthunder.com", StringComparison.OrdinalIgnoreCase);

    private async Task InitializeBrowserAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WarThunderUIDGuard",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webView.CoreWebView2.Settings.IsWebMessageEnabled = false;
            _webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !IsAllowedNavigation(uri))
                    args.Cancel = true;
            };
            _webView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            _webView.CoreWebView2.DownloadStarting += (_, args) => args.Cancel = true;
            _webView.CoreWebView2.NavigationCompleted += (_, _) =>
            {
                if (!_completed) _pollTimer.Start();
            };
            _webView.Source = BuildLookupUri(_uid);
        }
        catch
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = Localizer.T("NicknameSync.WebViewUnavailable");
        }
    }

    private async Task CheckResultAsync()
    {
        if (_checking || _completed || _webView.CoreWebView2 is null) return;
        _checking = true;
        try
        {
            const string script = """
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

            var json = await _webView.CoreWebView2.ExecuteScriptAsync(script);
            var result = JsonSerializer.Deserialize<LookupPageResult>(json);
            if (result?.Names.Count == 1)
            {
                var nickname = Matcher.Normalize(result.Names[0]);
                if (nickname.Length == 0) return;

                _completed = true;
                _pollTimer.Stop();
                Nickname = nickname;
                _status.ForeColor = Color.SeaGreen;
                _status.Text = Localizer.F("NicknameSync.Found", nickname);
                await Task.Delay(900);
                if (!IsDisposed)
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            }
            else if (result?.Completed == true)
            {
                _status.ForeColor = Color.DarkOrange;
                _status.Text = result.Names.Count > 1
                    ? Localizer.T("NicknameSync.MultipleResults")
                    : Localizer.T("NicknameSync.NoResult");
            }
            else
            {
                _status.ForeColor = Color.DimGray;
                _status.Text = Localizer.F("NicknameSync.LookingUp", _uid);
            }
        }
        catch
        {
            _status.ForeColor = Color.DarkOrange;
            _status.Text = Localizer.T("NicknameSync.WaitingForPage");
        }
        finally
        {
            _checking = false;
        }
    }

    private sealed class LookupPageResult
    {
        public List<string> Names { get; set; } = [];
        public bool Completed { get; set; }
    }
}
