param(
    [Security.SecureString]$Password
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$root = 'C:\ProgramData\WarThunderUIDGuardSync'
$serverScript = Join-Path $root 'admin-upload-server.ps1'
$keyPath = Join-Path $root 'admin-upload-key.protected'
$salt = [Convert]::FromBase64String('uK5nuzmRHwBibjAmAz/vQXHsaYa4AtryZAjrWGNWU5A=')
$iterations = 300000
New-Item -ItemType Directory -Path $root -Force | Out-Null
if (-not (Test-Path -LiteralPath $serverScript)) {
    throw 'admin-upload-server.ps1 must be installed before configuring the password.'
}
if ($null -eq $Password) {
    $Password = Read-Host 'Enter the administrator upload password' -AsSecureString
}

$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
$plain = $null
$derived = $null
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($plain) -or $plain.Length -lt 20) {
        throw 'The administrator upload password must contain at least 20 characters.'
    }

    $pbkdf2 = [Security.Cryptography.Rfc2898DeriveBytes]::new(
        $plain,
        $salt,
        $iterations,
        [Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $derived = $pbkdf2.GetBytes(32) } finally { $pbkdf2.Dispose() }
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        $derived,
        $null,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    $temporaryKey = $keyPath + '.new'
    [IO.File]::WriteAllBytes($temporaryKey, $protected)
    Move-Item -LiteralPath $temporaryKey -Destination $keyPath -Force
    & icacls.exe $keyPath '/inheritance:r' '/grant:r' 'SYSTEM:(F)' 'Administrators:(F)' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Failed to restrict the upload credential file permissions.' }
    [Array]::Clear($protected, 0, $protected.Length)
}
finally {
    if ($null -ne $derived) { [Array]::Clear($derived, 0, $derived.Length) }
    [Array]::Clear($salt, 0, $salt.Length)
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    $plain = $null
}

$powerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$action = New-ScheduledTaskAction -Execute $powerShell `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $serverScript)
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) `
    -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'WTUIDGuardAdminUpload' -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null
Stop-ScheduledTask -TaskName 'WTUIDGuardAdminUpload' -ErrorAction SilentlyContinue
Start-ScheduledTask -TaskName 'WTUIDGuardAdminUpload'

Write-Output 'Administrator upload credential configured.'
