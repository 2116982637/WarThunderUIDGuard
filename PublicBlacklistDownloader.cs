using System.Text.Json;

namespace WarThunderUIDGuard;

internal static class PublicBlacklistDownloader
{
    internal static async Task<string> FetchJsonAsync(Uri primaryUri, CancellationToken cancellationToken)
    {
        return await FetchJsonAsync(
            primaryUri,
            FetchProductionSourceAsync,
            cancellationToken);
    }

    internal static async Task<string> FetchJsonAsync(
        Uri primaryUri,
        Func<Uri, CancellationToken, Task<string>> httpFetcher,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var httpSources = new (Uri Uri, TimeSpan StartDelay)[]
        {
            (new Uri(SignedBlacklistClient.DataUrl), TimeSpan.Zero),
            (primaryUri, TimeSpan.FromMilliseconds(150)),
            (new Uri(DataStore.PublicBlacklistGcoreUrl), TimeSpan.FromMilliseconds(300)),
            (new Uri(DataStore.PublicBlacklistFastlyUrl), TimeSpan.FromMilliseconds(450)),
            (new Uri(DataStore.PublicBlacklistCdnUrl), TimeSpan.FromMilliseconds(600))
        };
        var uniqueSources = httpSources
            .DistinctBy(source => source.Uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using var raceCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pending = uniqueSources
            .Select(source => FetchWithRetryAsync(
                source.Uri,
                source.StartDelay,
                httpFetcher,
                raceCancellation.Token))
            .ToList();
        while (pending.Count > 0)
        {
            try
            {
                var completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                var json = await completed;
                raceCancellation.Cancel();
                await DrainAsync(pending);
                return json;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        throw new AggregateException("All public blacklist sources are unavailable.", failures);
    }

    private static Task<string> FetchProductionSourceAsync(Uri source, CancellationToken cancellationToken) =>
        SignedBlacklistClient.IsDataUri(source)
            ? SignedBlacklistClient.FetchAndVerifyAsync(source, cancellationToken)
            : DataStore.FetchRemoteJsonAsync(source, cancellationToken);

    private static async Task<string> FetchWithRetryAsync(
        Uri source,
        TimeSpan startDelay,
        Func<Uri, CancellationToken, Task<string>> fetcher,
        CancellationToken cancellationToken)
    {
        if (startDelay > TimeSpan.Zero)
            await Task.Delay(startDelay, cancellationToken);

        var failures = new List<Exception>();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(attempt == 0 ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(8));
            try
            {
                var json = await fetcher(source, timeout.Token);
                ValidateJson(json);
                return json;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            if (attempt == 0) await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new AggregateException($"Remote source {source.Host} failed after retries.", failures);
    }

    private static async Task DrainAsync(IEnumerable<Task<string>> tasks)
    {
        try { await Task.WhenAll(tasks); }
        catch { }
    }

    private static void ValidateJson(string json)
    {
        if (json.Length > DataStore.MaxRemoteBytes)
            throw new InvalidDataException("The remote blacklist is larger than 1 MB.");

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The remote blacklist is not a JSON object.");
    }
}
