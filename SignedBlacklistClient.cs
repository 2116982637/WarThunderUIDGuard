using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WarThunderUIDGuard;

internal static class SignedBlacklistClient
{
    internal const string DataUrl = "http://39.105.200.142:8443/blacklist.json";
    internal const string SignatureUrl = "http://39.105.200.142:8443/blacklist.sig";
    internal const string UpdateMetadataUrl = "http://39.105.200.142:8443/updates/latest.json";
    internal const string UpdateSignatureUrl = "http://39.105.200.142:8443/updates/latest.sig";
    internal const string UpdateBaseUrl = "http://39.105.200.142:8443/updates/";
    internal const string PublicKeySha256 = "63E5E8CFD6D43224FC9452597ECA490754E3054AFBE9ECADCD53DF9B3DB063E9";

    private const int MaxSignatureBytes = 16 * 1024;
    private const string PublicKeyXml =
        "<RSAKeyValue><Modulus>wMJWy3q2nB1gpAx+C95yPnwjttJQAIun6hkYww8OK5736wbzT7bJjspvZVounTeL0bgh1A0awp6kRmxsYJ4W4bjIuELeAmXk06C5ZjVzNAwVyJ2dMNIDXA4PIk96+KByQDQK1c5GrtQTO60Sq4QGtxikBe/gzx6fsMVHOSMdaRl9UH6cBrHTEWz5KuQP11e4eO8aOffSuhoG97gdxeXEVVOsZyvLGSHSrAAI3mh/eHJwb800cQAonKe9r2C1Xf+fp0Zc77NLolLF9qYtnoYOHl8atFFZPpVozE6siNxsYKg+Msv/Xv3xG8z+JfUsRO5HmZVO0U4Wpsm2CsIozPRGk5s6B5BxZDqzSNIvF8/7VQdSUuWlgHYsA9AmN/rNjXKmA8770N6rV8nBg08lH9AIaVDqcLZYR6U2ARsNeA3SyQiHqxlWPfGoHZfbU4aV2C0NnjHW2IUFiCsbXOQ/c19xr1P+ESM+xUdzm7Ye8/F3lHgl6FktCV5oPoneBqnDztzp</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

    internal static bool IsDataUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.Host.Equals("39.105.200.142", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 8443 &&
        uri.AbsolutePath.Equals("/blacklist.json", StringComparison.Ordinal) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo);

    internal static async Task<string> FetchAndVerifyAsync(Uri dataUri, CancellationToken cancellationToken)
    {
        if (!IsDataUri(dataUri))
            throw new InvalidDataException("The signed blacklist URI is not allowed.");

        return await FetchAndVerifyDocumentAsync(
            dataUri,
            new Uri(SignatureUrl),
            DataStore.MaxRemoteBytes,
            cancellationToken);
    }

    internal static async Task<string> FetchAndVerifyUpdateMetadataAsync(CancellationToken cancellationToken)
    {
        var metadataUri = new Uri(UpdateMetadataUrl);
        var signatureUri = new Uri(UpdateSignatureUrl);
        if (!IsUpdateMetadataUri(metadataUri) || !IsUpdateSignatureUri(signatureUri))
            throw new InvalidDataException("The signed update metadata URI is not allowed.");

        return await FetchAndVerifyDocumentAsync(
            metadataUri,
            signatureUri,
            64 * 1024,
            cancellationToken);
    }

    internal static bool IsUpdateMetadataUri(Uri uri) =>
        IsExactServerUri(uri, "/updates/latest.json");

    internal static bool IsUpdateSignatureUri(Uri uri) =>
        IsExactServerUri(uri, "/updates/latest.sig");

    internal static bool IsUpdateArchiveUri(Uri uri)
    {
        const string prefix = "WarThunderUIDGuard-v";
        const string suffix = "-win-x64.zip";
        if (!IsServerUri(uri) || !uri.AbsolutePath.StartsWith("/updates/", StringComparison.Ordinal))
            return false;
        var fileName = uri.AbsolutePath["/updates/".Length..];
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal) ||
            fileName.Contains('/'))
            return false;
        var versionText = fileName[prefix.Length..^suffix.Length];
        return Version.TryParse(versionText, out var version) && version is not null;
    }

    private static async Task<string> FetchAndVerifyDocumentAsync(
        Uri dataUri,
        Uri signatureUri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            // These endpoints are a pinned IP and signature-verified. A desktop
            // proxy can otherwise intercept or black-hole the private server path.
            UseProxy = false
        };
        using var client = new HttpClient(handler);
        var dataTask = FetchBytesAsync(client, dataUri, maximumBytes, timeout.Token);
        var signatureTask = FetchBytesAsync(client, signatureUri, MaxSignatureBytes, timeout.Token);
        await Task.WhenAll(dataTask, signatureTask);

        var payload = await dataTask;
        var signatureText = Encoding.ASCII.GetString(await signatureTask).Trim();
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureText); }
        catch (FormatException ex) { throw new InvalidDataException("The blacklist signature is malformed.", ex); }

        if (!VerifySignature(payload, signature, PublicKeyXml))
            throw new InvalidDataException("The blacklist signature is invalid.");

        return Encoding.UTF8.GetString(payload).TrimStart('\uFEFF');
    }

    internal static bool VerifySignature(byte[] payload, byte[] signature, string publicKeyXml)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(publicKeyXml);
        return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    internal static string ComputePinnedPublicKeyHash() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PublicKeyXml)));

    private static bool IsExactServerUri(Uri uri, string path) =>
        IsServerUri(uri) && uri.AbsolutePath.Equals(path, StringComparison.Ordinal);

    private static bool IsServerUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.Host.Equals("39.105.200.142", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 8443 &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static async Task<byte[]> FetchBytesAsync(
        HttpClient client,
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd($"WarThunderUIDGuard/{AutoUpdater.CurrentVersion}");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("The signed blacklist response is too large.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new InvalidDataException("The signed blacklist response is too large.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
