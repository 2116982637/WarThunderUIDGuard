using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarThunderUIDGuard;

internal static class AdminUploadClient
{
    internal const string UploadUrl = "http://39.105.200.142:8443/admin/upload";
    internal const int PasswordIterations = 300_000;
    private const string PasswordSaltBase64 = "uK5nuzmRHwBibjAmAz/vQXHsaYa4AtryZAjrWGNWU5A=";
    private const int MaxUploadBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static async Task<AppData> UploadAsync(
        AppData local,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (password.Length < 20)
            throw new AdminUploadException(AdminUploadFailure.Unauthorized);

        var key = DeriveKey(password);
        password = string.Empty;
        try
        {
            var remoteJson = await SignedBlacklistClient.FetchAndVerifyAsync(
                new Uri(SignedBlacklistClient.DataUrl),
                cancellationToken);
            var remote = ReadData(remoteJson);
            var merged = DataStore.Merge(local, remote);
            var body = JsonSerializer.SerializeToUtf8Bytes(merged, JsonOptions);
            if (body.Length > MaxUploadBytes)
                throw new InvalidDataException("The blacklist is larger than 1 MB.");

            var baseHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(remoteJson)));
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var nonceBytes = RandomNumberGenerator.GetBytes(16);
            var nonce = Convert.ToHexString(nonceBytes);
            CryptographicOperations.ZeroMemory(nonceBytes);
            var authorization = CreateAuthorization(body, baseHash, timestamp, nonce, key);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseProxy = false
            };
            using var client = new HttpClient(handler);
            using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl);
            request.Headers.UserAgent.ParseAdd($"WarThunderUIDGuard/{AutoUpdater.CurrentVersion}");
            request.Headers.TryAddWithoutValidation("X-WT-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-WT-Nonce", nonce);
            request.Headers.TryAddWithoutValidation("X-WT-Base-SHA256", baseHash);
            request.Headers.TryAddWithoutValidation("Authorization", "WT-HMAC " + authorization);
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AdminUploadException(AdminUploadFailure.Unauthorized);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new AdminUploadException(AdminUploadFailure.Conflict);
            if ((int)response.StatusCode == 429)
                throw new AdminUploadException(AdminUploadFailure.RateLimited);
            response.EnsureSuccessStatusCode();

            var confirmedJson = await FetchConfirmedJsonAsync(timeout.Token);
            var confirmed = ReadData(confirmedJson);
            if (!EquivalentBlacklist(merged, confirmed))
                throw new InvalidDataException("The signed server data does not match the uploaded blacklist.");

            confirmed.Language = local.Language;
            confirmed.OneDriveSyncEnabled = local.OneDriveSyncEnabled;
            return confirmed;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static bool IsUploadUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp &&
        uri.Host.Equals("39.105.200.142", StringComparison.OrdinalIgnoreCase) &&
        uri.Port == 8443 &&
        uri.AbsolutePath.Equals("/admin/upload", StringComparison.Ordinal) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.IsNullOrEmpty(uri.UserInfo);

    internal static byte[] DeriveKey(string password)
    {
        var salt = Convert.FromBase64String(PasswordSaltBase64);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                PasswordIterations,
                HashAlgorithmName.SHA256,
                32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    internal static string CreateAuthorization(
        byte[] body,
        string baseHash,
        string timestamp,
        string nonce,
        byte[] key)
    {
        var bodyHash = Convert.ToHexString(SHA256.HashData(body));
        var canonical = $"POST\n/admin/upload\n{timestamp}\n{nonce}\n{baseHash}\n{bodyHash}\n";
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static AppData ReadData(string json)
    {
        var data = JsonSerializer.Deserialize<AppData>(json, JsonOptions)
                   ?? throw new InvalidDataException("The server blacklist is empty.");
        data.Players ??= [];
        data.DeletedPlayers ??= [];
        foreach (var player in data.Players) player.Aliases ??= [];
        return data;
    }

    private static bool EquivalentBlacklist(AppData left, AppData right) =>
        JsonSerializer.Serialize(left.Players, JsonOptions) == JsonSerializer.Serialize(right.Players, JsonOptions) &&
        JsonSerializer.Serialize(left.DeletedPlayers, JsonOptions) == JsonSerializer.Serialize(right.DeletedPlayers, JsonOptions);

    private static async Task<string> FetchConfirmedJsonAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await SignedBlacklistClient.FetchAndVerifyAsync(
                    new Uri(SignedBlacklistClient.DataUrl),
                    cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                lastError = ex;
                await Task.Delay(200, cancellationToken);
            }
        }
        throw lastError ?? new InvalidDataException("The signed server response could not be verified.");
    }
}

internal enum AdminUploadFailure
{
    Unauthorized,
    Conflict,
    RateLimited
}

internal sealed class AdminUploadException(AdminUploadFailure failure) : Exception
{
    internal AdminUploadFailure Failure { get; } = failure;
}
