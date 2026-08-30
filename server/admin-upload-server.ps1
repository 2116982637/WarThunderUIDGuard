$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$root = 'C:\ProgramData\WarThunderUIDGuardSync'
$www = Join-Path $root 'www'
$dataPath = Join-Path $www 'blacklist.json'
$signaturePath = Join-Path $www 'blacklist.sig'
$backupDirectory = Join-Path $root 'upload-backups'
$logPath = Join-Path $root 'admin-upload.log'
$maximumBytes = 1048576
$maximumAttempts = 10
$attemptWindowSeconds = 600
$clockSkewSeconds = 120
$utf8 = New-Object Text.UTF8Encoding($false, $true)
$ascii = New-Object Text.ASCIIEncoding
$attempts = @{}
$nonces = @{}

function Write-Audit {
    param([string]$Message)
    Add-Content -LiteralPath $logPath -Encoding ASCII -Value ("{0:o} {1}" -f [DateTime]::UtcNow, $Message)
}

function Send-Response {
    param($Context, [int]$StatusCode, [string]$Text)
    $bytes = $ascii.GetBytes($Text)
    $Context.Response.StatusCode = $StatusCode
    $Context.Response.ContentType = 'text/plain; charset=us-ascii'
    $Context.Response.ContentLength64 = $bytes.Length
    $Context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $Context.Response.OutputStream.Close()
}

function Test-FixedTimeEquals {
    param([byte[]]$Left, [byte[]]$Right)
    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) { return $false }
    $difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ($Left[$index] -bxor $Right[$index])
    }
    return $difference -eq 0
}

function Get-RequestBytes {
    param($Request)
    if ($Request.ContentLength64 -gt $maximumBytes) { throw 'UploadTooLarge' }
    $output = New-Object IO.MemoryStream
    try {
        $buffer = New-Object byte[] 8192
        while (($read = $Request.InputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($output.Length + $read -gt $maximumBytes) { throw 'UploadTooLarge' }
            $output.Write($buffer, 0, $read)
        }
        return $output.ToArray()
    }
    finally { $output.Dispose() }
}

function Get-ClientAddress {
    param($Request)
    $forwarded = [string]$Request.Headers['X-Forwarded-For']
    if (-not [string]::IsNullOrWhiteSpace($forwarded)) {
        return ($forwarded -split ',')[-1].Trim()
    }
    return [string]$Request.RemoteEndPoint.Address
}

function Test-RateLimit {
    param([string]$Address)
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $cutoff = $now - $attemptWindowSeconds
    $recent = @($attempts[$Address] | Where-Object { [long]$_ -ge $cutoff })
    if ($recent.Count -ge $maximumAttempts) {
        $attempts[$Address] = $recent
        return $false
    }
    $attempts[$Address] = @($recent + $now)
    return $true
}

function Test-Blacklist {
    param($Data)
    if ($null -eq $Data -or [int]$Data.SchemaVersion -ne 2) { throw 'InvalidSchema' }
    $players = @($Data.Players)
    $deleted = @($Data.DeletedPlayers)
    if ($players.Count -gt 50000 -or $deleted.Count -gt 50000) { throw 'TooManyRecords' }
    $uids = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($player in $players) {
        $uid = [string]$player.Uid
        if ($uid -notmatch '^\d{1,20}$' -or -not $uids.Add($uid)) { throw 'InvalidPlayerUid' }
        if ([string]$player.Note -and ([string]$player.Note).Length -gt 500) { throw 'InvalidNote' }
        $aliases = @($player.Aliases)
        if ($aliases.Count -gt 50) { throw 'TooManyAliases' }
        foreach ($alias in $aliases) {
            if ([string]::IsNullOrWhiteSpace([string]$alias) -or ([string]$alias).Length -gt 100) {
                throw 'InvalidAlias'
            }
        }
        [DateTimeOffset]::Parse([string]$player.CreatedAt) | Out-Null
        [DateTimeOffset]::Parse([string]$player.UpdatedAt) | Out-Null
    }
    foreach ($item in $deleted) {
        if ([string]$item.Uid -notmatch '^\d{1,20}$') { throw 'InvalidDeletedUid' }
        [DateTimeOffset]::Parse([string]$item.DeletedAt) | Out-Null
    }
}

function Write-SignedBlacklist {
    param([byte[]]$Body)
    $encrypted = [IO.File]::ReadAllBytes((Join-Path $root 'signing-key.protected'))
    $privateBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.PersistKeyInCsp = $false
        $rsa.ImportCspBlob($privateBytes)
        $signature = $rsa.SignData(
            $Body,
            [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
        $signatureBytes = $ascii.GetBytes([Convert]::ToBase64String($signature))
        $temporaryData = $dataPath + '.upload-' + [Guid]::NewGuid().ToString('N')
        $temporarySignature = $signaturePath + '.upload-' + [Guid]::NewGuid().ToString('N')
        [IO.File]::WriteAllBytes($temporaryData, $Body)
        [IO.File]::WriteAllBytes($temporarySignature, $signatureBytes)
        Move-Item -LiteralPath $temporaryData -Destination $dataPath -Force
        Move-Item -LiteralPath $temporarySignature -Destination $signaturePath -Force
        [Array]::Clear($signature, 0, $signature.Length)
        [Array]::Clear($signatureBytes, 0, $signatureBytes.Length)
    }
    finally {
        $rsa.Dispose()
        [Array]::Clear($privateBytes, 0, $privateBytes.Length)
        [Array]::Clear($encrypted, 0, $encrypted.Length)
    }
}

$protectedKey = [IO.File]::ReadAllBytes((Join-Path $root 'admin-upload-key.protected'))
$authenticationKey = [Security.Cryptography.ProtectedData]::Unprotect(
    $protectedKey,
    $null,
    [Security.Cryptography.DataProtectionScope]::LocalMachine)
[Array]::Clear($protectedKey, 0, $protectedKey.Length)
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$listener = New-Object Net.HttpListener
$listener.Prefixes.Add('http://127.0.0.1:8090/')
$listener.Start()
Write-Audit 'service-started'

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $address = Get-ClientAddress $context.Request
        try {
            if ($context.Request.HttpMethod -cne 'POST' -or $context.Request.Url.AbsolutePath -cne '/admin/upload') {
                Send-Response $context 404 'Not found.'
                continue
            }
            if (-not (Test-RateLimit $address)) {
                Write-Audit ("rate-limited address={0}" -f $address)
                Send-Response $context 429 'Too many requests.'
                continue
            }

            $timestampText = [string]$context.Request.Headers['X-WT-Timestamp']
            $nonce = [string]$context.Request.Headers['X-WT-Nonce']
            $baseHash = (([string]$context.Request.Headers['X-WT-Base-SHA256']) + '').ToUpperInvariant()
            $authorization = [string]$context.Request.Headers['Authorization']
            $timestamp = 0L
            if ([string]::IsNullOrWhiteSpace($timestampText) -or
                [string]::IsNullOrWhiteSpace($authorization) -or
                -not $authorization.StartsWith('WT-HMAC ') -or
                $nonce -notmatch '^[0-9A-F]{32}$' -or
                $baseHash -notmatch '^[0-9A-F]{64}$') {
                Write-Audit ("upload-rejected address={0} reason=unauthorized" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }
            try { $timestamp = [Convert]::ToInt64($timestampText) }
            catch {
                Write-Audit ("upload-rejected address={0} reason=unauthorized" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }
            if ([Math]::Abs([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - $timestamp) -gt $clockSkewSeconds) {
                Write-Audit ("upload-rejected address={0} reason=unauthorized" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }

            $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            foreach ($oldNonce in @($nonces.Keys)) {
                if ($nonces[$oldNonce] -lt $now) { $nonces.Remove($oldNonce) }
            }
            if ($nonces.ContainsKey($nonce)) {
                Write-Audit ("upload-rejected address={0} reason=replay" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }

            $body = Get-RequestBytes $context.Request
            $sha256 = New-Object Security.Cryptography.SHA256Managed
            try { $bodyHash = $sha256.ComputeHash($body) } finally { $sha256.Dispose() }
            $bodyHashText = [BitConverter]::ToString($bodyHash).Replace('-', '')
            [Array]::Clear($bodyHash, 0, $bodyHash.Length)
            $canonical = "POST`n/admin/upload`n$timestampText`n$nonce`n$baseHash`n$bodyHashText`n"
            $hmac = New-Object Security.Cryptography.HMACSHA256
            try {
                $hmac.Key = $authenticationKey
                $expectedMac = $hmac.ComputeHash($utf8.GetBytes($canonical))
            }
            finally { $hmac.Dispose() }
            try { $providedMac = [Convert]::FromBase64String($authorization.Substring(8)) }
            catch {
                [Array]::Clear($expectedMac, 0, $expectedMac.Length)
                Write-Audit ("upload-rejected address={0} reason=unauthorized" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }
            $authenticated = Test-FixedTimeEquals $expectedMac $providedMac
            [Array]::Clear($expectedMac, 0, $expectedMac.Length)
            [Array]::Clear($providedMac, 0, $providedMac.Length)
            if (-not $authenticated) {
                Write-Audit ("upload-rejected address={0} reason=unauthorized" -f $address)
                Send-Response $context 401 'Unauthorized.'
                continue
            }
            $nonces[$nonce] = $now + 300

            $dataMutex = New-Object Threading.Mutex($false, 'Global\WTUIDGuardDataWrite')
            $lockHeld = $false
            try {
                $lockHeld = $dataMutex.WaitOne([TimeSpan]::FromSeconds(30))
                if (-not $lockHeld) { throw 'ServerBusy' }

                $actualBaseHash = (Get-FileHash -LiteralPath $dataPath -Algorithm SHA256).Hash
                if ($actualBaseHash -ne $baseHash) {
                    Write-Audit ("upload-rejected address={0} reason=conflict" -f $address)
                    Send-Response $context 409 'Server data changed.'
                    continue
                }

                $json = $utf8.GetString($body)
                $data = $json | ConvertFrom-Json
                Test-Blacklist $data

                $backup = Join-Path $backupDirectory ("blacklist-{0:yyyyMMdd-HHmmssfff}.json" -f [DateTime]::UtcNow)
                Copy-Item -LiteralPath $dataPath -Destination $backup -Force
                Get-ChildItem -LiteralPath $backupDirectory -Filter 'blacklist-*.json' |
                    Sort-Object LastWriteTimeUtc -Descending |
                    Select-Object -Skip 30 |
                    Remove-Item -Force

                Write-SignedBlacklist $body
            }
            finally {
                if ($lockHeld) { $dataMutex.ReleaseMutex() }
                $dataMutex.Dispose()
            }
            Write-Audit ("upload-ok address={0} players={1} deleted={2} sha256={3}" -f
                $address, @($data.Players).Count, @($data.DeletedPlayers).Count, $bodyHashText)
            Send-Response $context 200 'OK'
        }
        catch {
            $reason = [string]$_.Exception.Message
            if ($reason -eq 'UploadTooLarge') {
                Write-Audit ("upload-rejected address={0} reason=too-large" -f $address)
                Send-Response $context 413 'Upload too large.'
            }
            else {
                $safeMessage = ([string]$_.Exception.Message) -replace '[\r\n]+', ' '
                Write-Audit ("upload-rejected address={0} reason=invalid type={1} line={2} message={3}" -f
                    $address, $_.Exception.GetType().FullName, $_.InvocationInfo.ScriptLineNumber, $safeMessage)
                Send-Response $context 400 'Invalid blacklist.'
            }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
    [Array]::Clear($authenticationKey, 0, $authenticationKey.Length)
    Write-Audit 'service-stopped'
}
