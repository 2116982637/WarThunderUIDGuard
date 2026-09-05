using System.Diagnostics;
using System.Net;

namespace WarThunderUIDGuard;

internal enum ServerLatencyStatus
{
    Online,
    TimedOut,
    Unavailable
}

internal readonly record struct ServerLatencyResult(ServerLatencyStatus Status, long Milliseconds)
{
    public static ServerLatencyResult Online(long milliseconds) =>
        new(ServerLatencyStatus.Online, Math.Max(1, milliseconds));

    public static ServerLatencyResult TimedOut() => new(ServerLatencyStatus.TimedOut, 0);
    public static ServerLatencyResult Unavailable() => new(ServerLatencyStatus.Unavailable, 0);
}

internal sealed class ServerLatencyProbe : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _client;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    public ServerLatencyProbe()
        : this(CreateHandler(), new Uri(SignedBlacklistClient.HealthUrl), DefaultTimeout)
    {
    }

    internal ServerLatencyProbe(HttpMessageHandler handler, Uri endpoint, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!SignedBlacklistClient.IsHealthUri(endpoint))
            throw new InvalidDataException("The latency endpoint is not allowed.");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        _endpoint = endpoint;
        _timeout = timeout;
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<ServerLatencyResult> MeasureAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Head, _endpoint);
        request.Headers.UserAgent.ParseAdd($"WarThunderUIDGuard/{AutoUpdater.CurrentVersion}");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            stopwatch.Stop();
            return response.IsSuccessStatusCode
                ? ServerLatencyResult.Online(stopwatch.ElapsedMilliseconds)
                : ServerLatencyResult.Unavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServerLatencyResult.TimedOut();
        }
        catch (HttpRequestException)
        {
            return ServerLatencyResult.Unavailable();
        }
    }

    public void Dispose() => _client.Dispose();

    private static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseProxy = false
    };
}
