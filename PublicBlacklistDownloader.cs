using System.Text.Json;

namespace WarThunderUIDGuard;

internal static class PublicBlacklistDownloader
{
    internal static async Task<string> FetchJsonAsync(Uri primaryUri, CancellationToken cancellationToken)
    {
        return await FetchJsonAsync(
            primaryUri,
            DataStore.FetchRemoteJsonAsync,
            OneDriveWebDownloader.FetchJsonAsync,
            cancellationToken);
    }

    internal static async Task<string> FetchJsonAsync(
        Uri primaryUri,
        Func<Uri, CancellationToken, Task<string>> httpFetcher,
        Func<Uri, CancellationToken, Task<string>> oneDriveFetcher,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var httpSources = new[]
        {
            primaryUri,
            new Uri(DataStore.PublicBlacklistCdnUrl)
        }.DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase);

        foreach (var source in httpSources)
        {
            try
            {
                var json = await httpFetcher(source, cancellationToken);
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
        }

        try
        {
            var json = await oneDriveFetcher(
                new Uri(DataStore.OneDriveSharedBlacklistUrl),
                cancellationToken);
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

        throw new AggregateException("All public blacklist sources are unavailable.", failures);
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
