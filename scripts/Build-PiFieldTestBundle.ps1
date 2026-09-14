param(
    [ValidatePattern('^[0-9A-Za-z._-]{1,64}$')]
    [string] $Version = '0.1.8-final',

    [string] $ServerCaCertificatePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts/pi-field-test'
[System.IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
$artifactPath = Join-Path $artifactsRoot "display-control-pi-linux-arm64-$Version.tar.gz"
if (Test-Path -LiteralPath $artifactPath) {
    throw "Refusing to overwrite the existing release archive: $artifactPath"
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("display-control-pi-" + [Guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $stagingRoot 'publish'
[System.IO.Directory]::CreateDirectory($publishDirectory) | Out-Null
try {
    Set-Location -LiteralPath $repositoryRoot
    npm.cmd run build --workspace player-web
    if ($LASTEXITCODE -ne 0) { throw 'The player build failed.' }

    $runtimeLockTemplate = Join-Path $stagingRoot '$(MSBuildProjectName).packages.lock.json'
    dotnet restore src/DisplayControl.DeviceAgent/DisplayControl.DeviceAgent.csproj `
        --runtime linux-arm64 `
        --force-evaluate `
        "-p:NuGetLockFilePath=$runtimeLockTemplate"
    if ($LASTEXITCODE -ne 0) { throw 'The isolated linux-arm64 runtime restore failed.' }

    dotnet publish src/DisplayControl.DeviceAgent/DisplayControl.DeviceAgent.csproj `
        --configuration Release `
        --runtime linux-arm64 `
        --self-contained true `
        --no-restore `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'The self-contained linux-arm64 publication failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'DisplayControl.DeviceAgent'))) {
        throw 'The publication did not produce the expected Raspberry Pi executable.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'wwwroot/index.html'))) {
        throw 'The publication does not contain the embedded player application.'
    }

    tar.exe -C $publishDirectory -czf $artifactPath .
    if ($LASTEXITCODE -ne 0) { throw 'The Raspberry Pi release archive could not be created.' }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

$deployCopy = Join-Path $artifactsRoot 'deploy-pi'
[System.IO.Directory]::CreateDirectory($deployCopy) | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'deploy/pi') -File |
    Copy-Item -Destination $deployCopy -Force
if (-not [string]::IsNullOrWhiteSpace($ServerCaCertificatePath)) {
    $resolvedCa = (Resolve-Path -LiteralPath $ServerCaCertificatePath).Path
    $destinationCa = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'field-test-server-ca.crt'))
    if (-not [System.StringComparer]::OrdinalIgnoreCase.Equals($resolvedCa, $destinationCa)) {
        Copy-Item -LiteralPath $resolvedCa -Destination $destinationCa -Force
    }
}

$digest = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$artifactPath.sha256", "$digest  $([System.IO.Path]::GetFileName($artifactPath))`n")
Write-Host "Pi archive: $artifactPath"
Write-Host "SHA-256:   $digest"
Write-Host "Installer:  $deployCopy"
