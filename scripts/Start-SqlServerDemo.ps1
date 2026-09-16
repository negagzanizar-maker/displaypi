$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dataRoot = Join-Path $repositoryRoot '.data\simulation-2026-09-09'
$environmentPath = Join-Path $repositoryRoot '.env'
$applicationPasswordEntry = Get-Content -LiteralPath $environmentPath |
    Where-Object { $_ -like 'SQLSERVER_APP_PASSWORD=*' } |
    Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($applicationPasswordEntry)) {
    throw 'SQLSERVER_APP_PASSWORD is required in .env. Run scripts\Initialize-SqlServerDemo.ps1 first.'
}
$applicationPassword = ($applicationPasswordEntry -split '=', 2)[1]

$env:ConnectionStrings__Database = "Server=127.0.0.1,14333;Database=display_control;User Id=display_app;Password=$applicationPassword;TrustServerCertificate=True"
$env:Database__Provider = 'SqlServer'
$env:Security__DataProtectionKeyDirectory = Join-Path $dataRoot 'data-protection'
$env:Security__TokenDigestPepperBase64 = 'yWfn0dbHgNsAtXpNxflHtgrtTcicENuLCmy7/6vk8o4='
$env:Security__HumanAuthentication__RequireMfa = 'false'
$env:Security__PlatformBootstrapTokenSha256Base64 = 'Fcng7rnTj6YNC2T19R65yEF1RmK8lXGUyo26aS59mZo='
$env:Security__DeviceCertificateAuthority__PfxPath = Join-Path $dataRoot 'device-ca.pfx'
$env:Security__DeviceCertificateAuthority__PfxPassword = '8HRJEvIr/fK/nxL1NsGaKIL1IuDZipEAHkvXgLMRgPg='
$env:Security__DeviceCertificateAuthority__IssuedLifetimeDays = '90'
$env:Security__LicenseSigningKey__PrivateKeyPath = Join-Path $dataRoot 'license-signing-key.pem'
$env:Security__LicenseSigningKey__Password = 'VzMnVFnyHFqsxs7xss+Bl3Srgz+Z6RsKWi7L2QQgzYc='
$env:ContentStorage__RootDirectory = 'C:\DisplayControl\Content'
$env:ContentScanning__ClamAv__Host = '127.0.0.1'
$env:ContentScanning__ClamAv__Port = '3310'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://0.0.0.0:7443'
$env:Kestrel__Certificates__Default__Path = Join-Path $dataRoot 'field-test-server.pfx'
$env:Kestrel__Certificates__Default__Password = 'clzGOYKEWvHSK/2kt0njjGxz/RZdLg8TP9FD9plHeOg='

dotnet run --no-launch-profile --project (Join-Path $repositoryRoot 'src\DisplayControl.Api')
