param(
    [Parameter(Mandatory = $true)]
    [string] $DatabaseConnectionString,

    [string] $OutputDirectory = (Join-Path (Get-Location) '.data/development'),

    [string] $HttpsHost,

    [ValidateRange(1, 65535)]
    [int] $HttpsPort = 7443
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$arguments = @(
    'run', '--project', (Join-Path $repositoryRoot 'tools/DisplayControl.Setup'), '--',
    '--database-connection', $DatabaseConnectionString,
    '--output', ([System.IO.Path]::GetFullPath($OutputDirectory)),
    '--https-port', [string] $HttpsPort
)
if (-not [string]::IsNullOrWhiteSpace($HttpsHost)) {
    $arguments += @('--https-host', $HttpsHost)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw 'Development security material generation failed.'
}
