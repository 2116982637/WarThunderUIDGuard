using System.Text.Json;

namespace WarThunderUIDGuard;

public sealed class WarThunderClient : IDisposable
{
    private static readonly Uri AllowedBaseAddress = new("http://127.0.0.1:8111/");
    private readonly HttpClient _http = CreateLocalOnlyClient();
    private readonly HashSet<string> _seen = [];
    private CancellationTokenSource? _cts;
    private int _lastChatId = -1;
    private int _lastEventId = -1;
    private int _lastDamageId = -1;
    private volatile bool _connected;

    public event Action<bool, string>? ConnectionChanged;
    public event Action<string, string, string, string>? IdentityObserved;
    public Func<IReadOnlyList<BlockedPlayer>> PlayersProvider { get; set; } = () => [];

    public bool IsRunning => _cts is not null;
    public bool IsConnected => _connected;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = PollLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        SetConnection(false, "Status.Stopped");
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await PollChatAsync(token);
                await PollHudAsync(token);
                SetConnection(true, "Status.Connected");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetConnection(false, "Status.WaitingForBattle");
            }

            try { await Task.Delay(900, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollChatAsync(CancellationToken token)
    {
        using var doc = await GetJsonAsync($"gamechat?lastId={_lastChatId}", token);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = GetInt(item, "id", -1);
            _lastChatId = Math.Max(_lastChatId, id);
            var sender = GetString(item, "sender");
            var message = GetString(item, "msg");
            Observe($"chat:{id}", sender, message, "Source.Chat");
        }
    }

    private async Task PollHudAsync(CancellationToken token)
    {
        using var doc = await GetJsonAsync(
            $"hudmsg?lastEvt={_lastEventId}&lastDmg={_lastDamageId}", token);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        if (doc.RootElement.TryGetProperty("damage", out var damage) && damage.ValueKind == JsonValueKind.Array)
        foreach (var item in damage.EnumerateArray())
        {
            var id = GetInt(item, "id", -1);
            _lastDamageId = Math.Max(_lastDamageId, id);
            Observe($"damage:{id}", GetString(item, "sender"), GetString(item, "msg"), "Source.CombatEvent");
        }

        if (doc.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var msg = item.GetString() ?? "";
                Observe($"event:{_lastEventId + 1}:{msg}", "", msg, "Source.HudEvent");
                _lastEventId++;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var id = GetInt(item, "id", _lastEventId + 1);
                _lastEventId = Math.Max(_lastEventId, id);
                Observe($"event:{id}", GetString(item, "sender"), GetString(item, "msg"), "Source.HudEvent");
            }
        }
    }

    private void Observe(string key, string sender, string message, string source)
    {
        if (!_seen.Add(key)) return;
        if (_seen.Count > 4000) _seen.Clear();

        var players = PlayersProvider();
        var exact = Matcher.MatchExactSender(players, sender);
        var textMatch = exact ?? Matcher.MatchEventText(players, message);
        if (textMatch is null) return;

        var detail = string.IsNullOrWhiteSpace(message) ? sender : message;
        IdentityObserved?.Invoke(textMatch.Value.Player.Uid, textMatch.Value.Alias, source, detail);
    }

    private async Task<JsonDocument> GetJsonAsync(string relativeUrl, CancellationToken token)
    {
        var uri = new Uri(AllowedBaseAddress, relativeUrl);
        if (!IsAllowedEndpoint(uri))
            throw new InvalidOperationException("Security policy rejected a non-local War Thunder endpoint.");

        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private static HttpClient CreateLocalOnlyClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false
        };
        return new HttpClient(handler)
        {
            BaseAddress = AllowedBaseAddress,
            Timeout = TimeSpan.FromSeconds(2),
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    internal static bool IsAllowedEndpoint(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.Host == "127.0.0.1" &&
        uri.Port == 8111 &&
        (uri.AbsolutePath == "/gamechat" || uri.AbsolutePath == "/hudmsg");

    private void SetConnection(bool connected, string text)
    {
        if (_connected == connected) return;
        _connected = connected;
        ConnectionChanged?.Invoke(connected, text);
    }

    private static string GetString(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int GetInt(JsonElement e, string property, int fallback) =>
        e.TryGetProperty(property, out var v) && v.TryGetInt32(out var result) ? result : fallback;

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
