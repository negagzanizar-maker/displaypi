param(
    [Parameter(Mandatory = $true)]
    [uri] $ApiBaseUri,

    [ValidatePattern('^[A-Za-z0-9._-]{1,32}$')]
    [string] $SerialNumber = 'VIRTUALPI0001',

    [string] $StateDirectory = (Join-Path (Get-Location) '.data/virtual-device'),

    [securestring] $EnrollmentCode,

    [switch] $DisableServerCertificateRevocationCheck
)

$ErrorActionPreference = 'Stop'
if (-not $ApiBaseUri.IsAbsoluteUri -or $ApiBaseUri.Scheme -ne 'https') {
    throw 'ApiBaseUri must be an absolute HTTPS address.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateRoot = [System.IO.Path]::GetFullPath($StateDirectory)
[System.IO.Directory]::CreateDirectory($stateRoot) | Out-Null
$statePath = Join-Path $stateRoot 'agent-state.json'
$enrollmentPath = Join-Path $stateRoot 'enrollment-code.txt'

if (-not (Test-Path -LiteralPath $statePath)) {
    if ($null -eq $EnrollmentCode) {
        $EnrollmentCode = Read-Host 'One-use device enrollment code' -AsSecureString
    }

    $plainEnrollmentCode = [System.Net.NetworkCredential]::new('', $EnrollmentCode).Password
    try {
        if ($plainEnrollmentCode.Length -lt 32 -or $plainEnrollmentCode.Length -gt 160) {
            throw 'The enrollment code has an invalid length.'
        }
        [System.IO.File]::WriteAllText($enrollmentPath, $plainEnrollmentCode)
    }
    finally {
        $plainEnrollmentCode = $null
    }
}

Set-Location -LiteralPath $repositoryRoot
& npm.cmd run build --workspace player-web
if ($LASTEXITCODE -ne 0) {
    throw 'The player build failed.'
}

$env:DOTNET_ENVIRONMENT = 'Development'
$env:Agent__ServerBaseAddress = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
$env:Agent__StateDirectory = $stateRoot
$env:Agent__DevelopmentSerialNumber = $SerialNumber
$env:Agent__CheckServerCertificateRevocation = if ($DisableServerCertificateRevocationCheck) { 'false' } else { 'true' }
if (Test-Path -LiteralPath $enrollmentPath) {
    $env:Agent__EnrollmentCodeFile = $enrollmentPath
}

Write-Host "Virtual device player: http://localhost:8787"
Write-Host 'Keep this terminal open. Press Ctrl+C to stop the virtual device.'
& dotnet run --no-launch-profile --project src/DisplayControl.DeviceAgent
