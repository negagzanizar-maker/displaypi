[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }

    foreach ($application in @(
        @{ Project = 'src/DisplayControl.Api'; Destination = 'artifacts/publish/api' },
        @{ Project = 'src/DisplayControl.DeviceAgent'; Destination = 'artifacts/publish/agent' }
    )) {
        & dotnet publish $application.Project --configuration Release --output $application.Destination
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($application.Project)" }
        $entryPoint = Join-Path $application.Destination 'wwwroot/index.html'
        if (!(Test-Path -LiteralPath $entryPoint) -or (Get-Item -LiteralPath $entryPoint).Length -eq 0) {
            throw "Published web entry point missing: $entryPoint"
        }
    }
}
finally {
    Pop-Location
}
