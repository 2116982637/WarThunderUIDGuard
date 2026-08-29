$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Security

$root = 'C:\ProgramData\WarThunderUIDGuardSync'
$www = Join-Path $root 'www'
$updates = Join-Path $www 'updates'
$utf8 = New-Object Text.UTF8Encoding($false)
New-Item -ItemType Directory -Path $updates -Force | Out-Null

function Get-RemoteFile {
    param([string[]]$Urls, [string]$Destination, [long]$MaximumBytes)
    $download = $Destination + '.download'
    Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue
    foreach ($url in $Urls) {
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $url -Headers @{ 'User-Agent' = 'WarThunderUIDGuardSync/2' } -OutFile $download
            $length = (Get-Item -LiteralPath $download).Length
            if ($length -le 0 -or $length -gt $MaximumBytes) { throw 'Downloaded file size is invalid.' }
            Move-Item -LiteralPath $download -Destination $Destination -Force
            return
        }
        catch {
            Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue
        }
    }
    throw "All download sources failed for $Destination"
}

function Write-Utf8NoBom {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

$encrypted = [IO.File]::ReadAllBytes((Join-Path $root 'signing-key.protected'))
$privateBytes = [Security.Cryptography.ProtectedData]::Unprotect(
    $encrypted,
    $null,
    [Security.Cryptography.DataProtectionScope]::LocalMachine)
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp = $false
$rsa.ImportCspBlob($privateBytes)
[Array]::Clear($privateBytes, 0, $privateBytes.Length)

function Write-Signature {
    param([string]$Source, [string]$Destination)
    $bytes = [IO.File]::ReadAllBytes($Source)
    $signature = $rsa.SignData(
        $bytes,
        [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    Write-Utf8NoBom -Path $Destination -Text ([Convert]::ToBase64String($signature))
}

try {
    $blacklistPath = Join-Path $www 'blacklist.json'
    Get-RemoteFile -Urls @(
        'https://gcore.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json',
        'https://fastly.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json',
        'https://cdn.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json',
        'https://raw.githubusercontent.com/elainasamae/WarThunderUIDGuard/main/data/blacklist.json'
    ) -Destination $blacklistPath -MaximumBytes 1048576
    $blacklist = Get-Content -LiteralPath $blacklistPath -Raw | ConvertFrom-Json
    if ($null -eq $blacklist.Players) { throw 'Blacklist JSON has no Players array.' }
    Write-Signature -Source $blacklistPath -Destination (Join-Path $www 'blacklist.sig')

    $release = Invoke-RestMethod -UseBasicParsing `
        -Uri 'https://api.github.com/repos/elainasamae/WarThunderUIDGuard/releases/latest' `
        -Headers @{ 'User-Agent' = 'WarThunderUIDGuardSync/2'; 'Accept' = 'application/vnd.github+json' }
    if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v\d+\.\d+\.\d+$') {
        throw 'GitHub latest release is not a stable semantic version.'
    }

    $tag = [string]$release.tag_name
    $archiveName = "WarThunderUIDGuard-$tag-win-x64.zip"
    $checksumName = $archiveName + '.sha256.txt'
    $archiveAsset = $release.assets | Where-Object { $_.name -ceq $archiveName } | Select-Object -First 1
    $checksumAsset = $release.assets | Where-Object { $_.name -ceq $checksumName } | Select-Object -First 1
    if ($null -eq $archiveAsset -or $null -eq $checksumAsset) { throw 'GitHub release assets are incomplete.' }

    $checksumPath = Join-Path $updates $checksumName
    Get-RemoteFile -Urls @([string]$checksumAsset.browser_download_url) -Destination $checksumPath -MaximumBytes 16384
    $checksumText = Get-Content -LiteralPath $checksumPath -Raw
    $match = [regex]::Match($checksumText, '(?im)^\s*([0-9a-f]{64})\s+\*?WarThunderUIDGuard-v\d+\.\d+\.\d+-win-x64\.zip\s*$')
    if (-not $match.Success) { throw 'GitHub release checksum is invalid.' }
    $expectedHash = $match.Groups[1].Value.ToUpperInvariant()

    $archivePath = Join-Path $updates $archiveName
    $archiveValid = (Test-Path -LiteralPath $archivePath) -and
        ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -eq $expectedHash)
    if (-not $archiveValid) {
        Get-RemoteFile -Urls @([string]$archiveAsset.browser_download_url) -Destination $archivePath -MaximumBytes 314572800
        if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
            Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
            throw 'Downloaded release archive checksum does not match.'
        }
    }

    $metadata = [ordered]@{
        schemaVersion = 1
        tag = $tag
        archive = $archiveName
        sha256 = $expectedHash
        size = (Get-Item -LiteralPath $archivePath).Length
        publishedAt = [string]$release.published_at
    } | ConvertTo-Json
    $metadataPath = Join-Path $updates 'latest.json'
    Write-Utf8NoBom -Path $metadataPath -Text $metadata
    Write-Signature -Source $metadataPath -Destination (Join-Path $updates 'latest.sig')
}
finally {
    $rsa.Dispose()
    [Array]::Clear($encrypted, 0, $encrypted.Length)
}
