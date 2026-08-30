using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarThunderUIDGuard;

internal sealed record UpdateRelease(
    Version Version,
    string Tag,
    string ArchiveName,
    IReadOnlyList<UpdateDownloadSource> Sources);

internal sealed record UpdateDownloadSource(
    Uri ArchiveUri,
    Uri? ChecksumUri,
    string? ExpectedSha256);

internal static class AutoUpdater
{
    internal const string Repository = "elainasamae/WarThunderUIDGuard";
    internal static readonly Uri LatestReleaseUri =
        new($"https://api.github.com/repos/{Repository}/releases/latest");

    private const long MetadataLimit = 1024 * 1024;
    private const long ChecksumLimit = 16 * 1024;
    private const long ArchiveLimit = 300L * 1024 * 1024;
    private const long ExtractedLimit = 700L * 1024 * 1024;
    private const int EntryLimit = 200;
    private const int RedirectLimit = 5;

    public static Version CurrentVersion
    {
        get
        {
            var value = typeof(AutoUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(value.Major, value.Minor, Math.Max(value.Build, 0));
        }
    }

    public static async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        UpdateRelease? serverRelease = null;
        var serverReached = false;

        try
        {
            serverRelease = await FetchSignedServerReleaseAsync(CurrentVersion, cancellationToken);
            serverReached = true;
            if (serverRelease is not null) return serverRelease;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { failures.Add(ex); }

        UpdateRelease? gitHubRelease = null;
        var gitHubReached = false;
        try
        {
            gitHubRelease = await FetchGitHubReleaseAsync(CurrentVersion, cancellationToken);
            gitHubReached = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { failures.Add(ex); }

        if (!serverReached && !gitHubReached)
            throw new AggregateException("All update metadata sources are unavailable.", failures);
        return gitHubRelease;
    }

    internal static async Task<UpdateRelease?> FetchSignedServerReleaseAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        var json = await SignedBlacklistClient.FetchAndVerifyUpdateMetadataAsync(cancellationToken);
        return ParseSignedReleaseJson(json, currentVersion);
    }

    private static async Task<UpdateRelease?> FetchGitHubReleaseAsync(
        Version currentVersion,
        CancellationToken cancellationToken)
    {
        var json = Encoding.UTF8.GetString(
            await DownloadBytesAsync(LatestReleaseUri, MetadataLimit, cancellationToken));
        return ParseReleaseJson(json, currentVersion);
    }

    public static async Task PrepareAndLaunchAsync(
        UpdateRelease release,
        int parentProcessId,
        CancellationToken cancellationToken)
    {
        var installDirectory = ResolveInstallDirectory(Environment.ProcessPath);
        EnsureDirectoryIsWritable(installDirectory);

        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"WarThunderUIDGuard-Update-{Guid.NewGuid():N}");
        var stagingDirectory = Path.Combine(workDirectory, "staging");
        var archivePath = Path.Combine(workDirectory, release.ArchiveName);
        var checksumPath = archivePath + ".sha256.txt";
        var scriptPath = Path.Combine(workDirectory, "install-update.ps1");

        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var failures = new List<Exception>();
            var downloaded = false;
            foreach (var source in release.Sources)
            {
                try
                {
                    TryDeleteFile(archivePath);
                    TryDeleteFile(checksumPath);
                    await DownloadFileAsync(source.ArchiveUri, archivePath, ArchiveLimit, cancellationToken);
                    var checksumText = source.ExpectedSha256 is string expected
                        ? $"{expected}  {release.ArchiveName}"
                        : source.ChecksumUri is Uri checksumUri
                            ? Encoding.UTF8.GetString(
                                await DownloadBytesAsync(checksumUri, ChecksumLimit, cancellationToken))
                            : throw new InvalidDataException("The update source has no trusted checksum.");
                    await File.WriteAllTextAsync(
                        checksumPath,
                        checksumText,
                        new UTF8Encoding(false),
                        cancellationToken);

                    var expectedHash = ParseSha256(checksumText, release.ArchiveName);
                    var actualHash = await ComputeSha256Async(archivePath, cancellationToken);
                    if (!CryptographicOperations.FixedTimeEquals(
                            Convert.FromHexString(expectedHash),
                            Convert.FromHexString(actualHash)))
                        throw new InvalidDataException(
                            "The update archive checksum does not match the trusted checksum.");
                    downloaded = true;
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    failures.Add(ex);
                    TryDeleteFile(archivePath);
                    TryDeleteFile(checksumPath);
                }
            }

            if (!downloaded)
                throw new AggregateException("All update download sources are unavailable.", failures);

            ExtractArchiveSafely(archivePath, stagingDirectory);
            var executableName = Path.GetFileName(Environment.ProcessPath) ?? "WarThunderUIDGuard.exe";
            var stagedExecutable = Path.Combine(stagingDirectory, executableName);
            if (!File.Exists(stagedExecutable))
                throw new InvalidDataException("The update archive does not contain the application executable.");
            ValidateExecutableVersion(stagedExecutable, release.Version);

            var failureLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WarThunderUIDGuard",
                "update-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(failureLog)!);
            File.WriteAllText(scriptPath, BuildInstallerScript(), new UTF8Encoding(true));

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(stagingDirectory);
            startInfo.ArgumentList.Add(installDirectory);
            startInfo.ArgumentList.Add(executableName);
            startInfo.ArgumentList.Add(NormalizeVersion(release.Version).ToString(4));
            startInfo.ArgumentList.Add(workDirectory);
            startInfo.ArgumentList.Add(failureLog);

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The update installer could not be started.");
        }
        catch
        {
            TryDeleteDirectory(workDirectory);
            throw;
        }
    }

    public static bool TryLaunchVersionDirectoryRename(int parentProcessId)
    {
        // Framework-dependent development builds must never rename their bin directory.
        if (!IsSingleFileDeployment()) return false;

        string? workDirectory = null;
        try
        {
            var currentDirectory = ResolveInstallDirectory(Environment.ProcessPath);
            var versionMarker = Path.Combine(currentDirectory, "VERSION.txt");
            if (!File.Exists(versionMarker) ||
                !string.Equals(
                    File.ReadAllText(versionMarker).Trim(),
                    CurrentVersion.ToString(),
                    StringComparison.Ordinal))
                return false;

            var expectedDirectoryName = GetVersionedDirectoryName(CurrentVersion);
            if (string.Equals(
                    Path.GetFileName(currentDirectory),
                    expectedDirectoryName,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            var parentDirectory = Directory.GetParent(currentDirectory)?.FullName
                ?? throw new InvalidOperationException("The application directory cannot be renamed safely.");
            var targetDirectory = Path.Combine(parentDirectory, expectedDirectoryName);
            if (Directory.Exists(targetDirectory))
                throw new IOException($"The target update directory already exists: {targetDirectory}");

            var executableName = Path.GetFileName(Environment.ProcessPath) ?? "WarThunderUIDGuard.exe";
            var failureLog = GetFailureLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(failureLog)!);
            workDirectory = Path.Combine(
                Path.GetTempPath(),
                $"WarThunderUIDGuard-Rename-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workDirectory);
            var scriptPath = Path.Combine(workDirectory, "rename-version-directory.ps1");
            File.WriteAllText(scriptPath, BuildDirectoryRenameScript(), new UTF8Encoding(true));

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32",
                    "WindowsPowerShell",
                    "v1.0",
                    "powershell.exe"),
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(currentDirectory);
            startInfo.ArgumentList.Add(targetDirectory);
            startInfo.ArgumentList.Add(executableName);
            startInfo.ArgumentList.Add(workDirectory);
            startInfo.ArgumentList.Add(failureLog);

            if (Process.Start(startInfo) is null)
                throw new InvalidOperationException("The version directory renamer could not be started.");
            return true;
        }
        catch (Exception ex)
        {
            if (workDirectory is not null) TryDeleteDirectory(workDirectory);
            WriteFailureLog(ex);
            return false;
        }
    }

    internal static UpdateRelease? ParseReleaseJson(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            throw new InvalidDataException("Draft releases cannot be installed.");
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            throw new InvalidDataException("Prereleases cannot be installed.");

        var tag = root.GetProperty("tag_name").GetString()?.Trim()
            ?? throw new InvalidDataException("The release has no tag.");
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version is null)
            throw new InvalidDataException("The release tag is not a valid version.");

        if (version <= currentVersion) return null;

        var archiveName = $"WarThunderUIDGuard-{tag}-win-x64.zip";
        var checksumName = archiveName + ".sha256.txt";
        Uri? archiveUri = null;
        Uri? checksumUri = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (!string.Equals(name, archiveName, StringComparison.Ordinal) &&
                !string.Equals(name, checksumName, StringComparison.Ordinal))
                continue;

            var uriText = asset.GetProperty("browser_download_url").GetString();
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || !IsAllowedUpdateUri(uri))
                throw new InvalidDataException("The release contains an unapproved download URL.");
            if (!uri.AbsolutePath.StartsWith(
                    $"/{Repository}/releases/download/{tag}/",
                    StringComparison.Ordinal))
                throw new InvalidDataException("The release asset is outside the expected tag path.");

            if (string.Equals(name, archiveName, StringComparison.Ordinal)) archiveUri = uri;
            else checksumUri = uri;
        }

        if (archiveUri is null || checksumUri is null)
            throw new InvalidDataException("The release is missing its portable archive or checksum.");
        return new UpdateRelease(
            version,
            tag,
            archiveName,
            [new UpdateDownloadSource(archiveUri, checksumUri, null)]);
    }

    internal static UpdateRelease? ParseSignedReleaseJson(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("The signed update metadata schema is unsupported.");

        var tag = root.GetProperty("tag").GetString()?.Trim()
            ?? throw new InvalidDataException("The signed update metadata has no tag.");
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || version is null)
            throw new InvalidDataException("The signed update tag is invalid.");
        if (version <= currentVersion) return null;

        var archiveName = root.GetProperty("archive").GetString()?.Trim()
            ?? throw new InvalidDataException("The signed update metadata has no archive.");
        var expectedArchiveName = $"WarThunderUIDGuard-{tag}-win-x64.zip";
        if (!string.Equals(archiveName, expectedArchiveName, StringComparison.Ordinal))
            throw new InvalidDataException("The signed update archive name is invalid.");

        var expectedHash = root.GetProperty("sha256").GetString()?.Trim().ToUpperInvariant()
            ?? throw new InvalidDataException("The signed update metadata has no checksum.");
        if (expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit))
            throw new InvalidDataException("The signed update checksum is invalid.");
        var size = root.GetProperty("size").GetInt64();
        if (size <= 0 || size > ArchiveLimit)
            throw new InvalidDataException("The signed update archive size is invalid.");

        var archiveUri = new Uri(SignedBlacklistClient.UpdateBaseUrl + archiveName);
        if (!SignedBlacklistClient.IsUpdateArchiveUri(archiveUri))
            throw new InvalidDataException("The signed update archive URI is not allowed.");
        var gitHubArchiveUri = new Uri(
            $"https://github.com/{Repository}/releases/download/{tag}/{archiveName}");
        if (!IsAllowedUpdateUri(gitHubArchiveUri))
            throw new InvalidDataException("The GitHub fallback archive URI is not allowed.");
        return new UpdateRelease(
            version,
            tag,
            archiveName,
            [
                new UpdateDownloadSource(archiveUri, null, expectedHash),
                new UpdateDownloadSource(gitHubArchiveUri, null, expectedHash)
            ]);
    }

    internal static string ParseSha256(string text, string expectedFileName)
    {
        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var fileName = parts[^1].TrimStart('*');
            if (!string.Equals(fileName, expectedFileName, StringComparison.Ordinal)) continue;
            if (parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                return parts[0].ToUpperInvariant();
        }

        throw new InvalidDataException("The published checksum file is invalid.");
    }

    internal static bool IsAllowedUpdateUri(Uri uri)
    {
        if (SignedBlacklistClient.IsUpdateArchiveUri(uri)) return true;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Equals(
                $"/repos/{Repository}/releases/latest",
                StringComparison.Ordinal);
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.StartsWith(
                $"/{Repository}/releases/download/",
                StringComparison.Ordinal);

        return uri.Host.Equals("release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("github-releases.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ValidateArchiveEntryPath(string extractionRoot, string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
            throw new InvalidDataException("The update archive contains an invalid path.");
        var normalizedRoot = Path.GetFullPath(extractionRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(normalizedRoot, entryName));
        if (!destination.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update archive attempted to write outside its staging directory.");
        return destination;
    }

    internal static string ResolveInstallDirectory(string? processPath)
    {
        var executablePath = string.IsNullOrWhiteSpace(processPath)
            ? throw new InvalidOperationException("The running executable path is unavailable.")
            : Path.GetFullPath(processPath);
        return Path.GetDirectoryName(executablePath)?.TrimEnd(Path.DirectorySeparatorChar)
            ?? throw new InvalidOperationException("The application directory is unavailable.");
    }

    internal static string GetVersionedDirectoryName(Version version) =>
        $"WarThunderUIDGuard-v{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}-win-x64";

    internal static void ValidateExecutableVersion(string executablePath, Version expectedVersion)
    {
        var versionText = FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
        if (!Version.TryParse(versionText, out var actualVersion) || actualVersion is null ||
            NormalizeVersion(actualVersion) != NormalizeVersion(expectedVersion))
            throw new InvalidDataException("The update executable version does not match its release metadata.");
    }

    private static Version NormalizeVersion(Version version) => new(
        version.Major,
        version.Minor,
        Math.Max(version.Build, 0),
        Math.Max(version.Revision, 0));

    private static bool IsSingleFileDeployment()
    {
#pragma warning disable IL3000 // Empty Assembly.Location is the documented single-file signal used here.
        return string.IsNullOrEmpty(typeof(AutoUpdater).Assembly.Location);
#pragma warning restore IL3000
    }

    internal static string? TakeInstallerFailure()
    {
        var path = GetFailureLogPath();
        try
        {
            if (!File.Exists(path)) return null;
            var message = File.ReadAllText(path);
            File.Delete(path);
            return message;
        }
        catch
        {
            return null;
        }
    }

    private static string GetFailureLogPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WarThunderUIDGuard",
        "update-error.log");

    private static void WriteFailureLog(Exception exception)
    {
        try
        {
            var path = GetFailureLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exception.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }

    private static async Task<byte[]> DownloadBytesAsync(
        Uri uri,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await DownloadAsync(uri, memory, maximumBytes, cancellationToken);
        return memory.ToArray();
    }

    private static async Task DownloadFileAsync(
        Uri uri,
        string path,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await DownloadAsync(uri, output, maximumBytes, cancellationToken);
    }

    private static async Task DownloadAsync(
        Uri initialUri,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        var uri = initialUri;
        for (var redirect = 0; redirect <= RedirectLimit; redirect++)
        {
            if (!IsAllowedUpdateUri(uri))
                throw new InvalidDataException("The update request was redirected to an unapproved address.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd($"WarThunderUIDGuard/{CurrentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or
                HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
                HttpStatusCode.PermanentRedirect)
            {
                if (redirect == RedirectLimit || response.Headers.Location is null)
                    throw new HttpRequestException("The update download used too many redirects.");
                uri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
                throw new InvalidDataException("The update download exceeds the size limit.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("The update download exceeds the size limit.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return;
        }

        throw new HttpRequestException("The update download failed.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = SHA256.Create();
        var value = await hash.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(value);
    }

    private static void ExtractArchiveSafely(string archivePath, string extractionRoot)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > EntryLimit)
            throw new InvalidDataException("The update archive contains too many entries.");

        long extractedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Symbolic links are not allowed in update archives.");
            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > ExtractedLimit)
                throw new InvalidDataException("The extracted update exceeds the size limit.");

            var destination = ValidateArchiveEntryPath(extractionRoot, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static void EnsureDirectoryIsWritable(string directory)
    {
        var path = Path.Combine(directory, $".update-write-test-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(path, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException("The application directory is not writable.", ex);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch { }
    }

    private static string BuildInstallerScript() =>
        """
        param(
            [Parameter(Mandatory=$true)][int]$ParentProcessId,
            [Parameter(Mandatory=$true)][string]$StagingDirectory,
            [Parameter(Mandatory=$true)][string]$InstallDirectory,
            [Parameter(Mandatory=$true)][string]$ExecutableName,
            [Parameter(Mandatory=$true)][version]$ExpectedVersion,
            [Parameter(Mandatory=$true)][string]$WorkDirectory,
            [Parameter(Mandatory=$true)][string]$FailureLog
        )
        $ErrorActionPreference = 'Stop'
        $items = @()
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(120)
            while (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) {
                if ([DateTime]::UtcNow -gt $deadline) { throw 'The running application did not close in time.' }
                Start-Sleep -Milliseconds 250
            }

            # Resolve 8.3 short paths before calculating relative names.
            $root = (Get-Item -LiteralPath $StagingDirectory).FullName.TrimEnd('\')
            foreach ($source in Get-ChildItem -LiteralPath $StagingDirectory -File -Recurse) {
                $relative = $source.FullName.Substring($root.Length).TrimStart('\')
                $destination = Join-Path $InstallDirectory $relative
                $parent = Split-Path -Parent $destination
                if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
                $newPath = $destination + '.wtuid-update-new'
                $oldPath = $destination + '.wtuid-update-old'
                Remove-Item -LiteralPath $newPath -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $oldPath -Force -ErrorAction SilentlyContinue
                Copy-Item -LiteralPath $source.FullName -Destination $newPath -Force
                $items += [pscustomobject]@{
                    Destination = $destination
                    NewPath = $newPath
                    OldPath = $oldPath
                    HadOld = $false
                    Installed = $false
                }
            }

            foreach ($item in $items) {
                if (Test-Path -LiteralPath $item.Destination) {
                    Move-Item -LiteralPath $item.Destination -Destination $item.OldPath -Force
                    $item.HadOld = $true
                }
                Move-Item -LiteralPath $item.NewPath -Destination $item.Destination -Force
                $item.Installed = $true
            }

            $installedExecutable = Join-Path $InstallDirectory $ExecutableName
            $installedVersion = [version][Diagnostics.FileVersionInfo]::GetVersionInfo($installedExecutable).FileVersion
            if ($installedVersion -ne $ExpectedVersion) {
                throw "The installed executable version $installedVersion does not match $ExpectedVersion."
            }

            Remove-Item -LiteralPath $FailureLog -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath $installedExecutable -WorkingDirectory $InstallDirectory
            Start-Sleep -Seconds 2
            foreach ($item in $items) {
                Remove-Item -LiteralPath $item.OldPath -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            $message = ($_ | Out-String)
            New-Item -ItemType Directory -Path (Split-Path -Parent $FailureLog) -Force | Out-Null
            Set-Content -LiteralPath $FailureLog -Value $message -Encoding UTF8
            for ($index = $items.Count - 1; $index -ge 0; $index--) {
                $item = $items[$index]
                if ($item.Installed) {
                    Remove-Item -LiteralPath $item.Destination -Force -ErrorAction SilentlyContinue
                }
                if ($item.HadOld -and (Test-Path -LiteralPath $item.OldPath)) {
                    Move-Item -LiteralPath $item.OldPath -Destination $item.Destination -Force
                }
                Remove-Item -LiteralPath $item.NewPath -Force -ErrorAction SilentlyContinue
            }
            $oldExecutable = Join-Path $InstallDirectory $ExecutableName
            if (Test-Path -LiteralPath $oldExecutable) {
                Start-Process -FilePath $oldExecutable -WorkingDirectory $InstallDirectory
            }
        }
        finally {
            Start-Sleep -Seconds 1
            Remove-Item -LiteralPath $WorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
        """;

    private static string BuildDirectoryRenameScript() =>
        """
        param(
            [Parameter(Mandatory=$true)][int]$ParentProcessId,
            [Parameter(Mandatory=$true)][string]$CurrentDirectory,
            [Parameter(Mandatory=$true)][string]$TargetDirectory,
            [Parameter(Mandatory=$true)][string]$ExecutableName,
            [Parameter(Mandatory=$true)][string]$WorkDirectory,
            [Parameter(Mandatory=$true)][string]$FailureLog
        )
        $ErrorActionPreference = 'Stop'
        $moved = $false
        try {
            $deadline = [DateTime]::UtcNow.AddSeconds(120)
            while (Get-Process -Id $ParentProcessId -ErrorAction SilentlyContinue) {
                if ([DateTime]::UtcNow -gt $deadline) { throw 'The running application did not close in time.' }
                Start-Sleep -Milliseconds 250
            }
            if (Test-Path -LiteralPath $TargetDirectory) {
                throw "The target update directory already exists: $TargetDirectory"
            }

            Move-Item -LiteralPath $CurrentDirectory -Destination $TargetDirectory
            $moved = $true
            $newExecutable = Join-Path $TargetDirectory $ExecutableName
            if (-not (Test-Path -LiteralPath $newExecutable)) {
                throw 'The executable is missing after renaming the update directory.'
            }

            Remove-Item -LiteralPath $FailureLog -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath $newExecutable -WorkingDirectory $TargetDirectory
        }
        catch {
            $message = ($_ | Out-String)
            if ($moved -and (Test-Path -LiteralPath $TargetDirectory) -and
                -not (Test-Path -LiteralPath $CurrentDirectory)) {
                Move-Item -LiteralPath $TargetDirectory -Destination $CurrentDirectory -ErrorAction SilentlyContinue
                $moved = $false
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $FailureLog) -Force | Out-Null
            Set-Content -LiteralPath $FailureLog -Value $message -Encoding UTF8
            $restartDirectory = if (Test-Path -LiteralPath $CurrentDirectory) {
                $CurrentDirectory
            } else {
                $TargetDirectory
            }
            $restartExecutable = Join-Path $restartDirectory $ExecutableName
            if (Test-Path -LiteralPath $restartExecutable) {
                Start-Process -FilePath $restartExecutable -WorkingDirectory $restartDirectory
            }
        }
        finally {
            Start-Sleep -Seconds 1
            Remove-Item -LiteralPath $WorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
        """;
}
