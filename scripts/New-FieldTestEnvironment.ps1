param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $ServerHost,

    [ValidateRange(1, 65535)]
    [int] $HttpsPort = 7443,

    [ValidateRange(1, 65535)]
    [int] $SqlServerPort = 14333,

    [string] $OutputDirectory = (Join-Path (Get-Location) '.data/field-test')
)

$ErrorActionPreference = 'Stop'

$parsedAddress = $null
if ([System.Net.IPAddress]::TryParse($ServerHost, [ref] $parsedAddress) -and
    [System.Net.IPAddress]::IsLoopback($parsedAddress)) {
    throw 'ServerHost must be the laptop LAN address visible from the Raspberry Pi, not a loopback address.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Refusing to overwrite the existing field-test directory: $outputRoot"
}

$databaseName = 'display_control'
$databaseOwnerUser = 'sa'
$databaseRuntimeUser = 'display_control_runtime_login'
$composeProjectName = 'display-control-field-test'
$databaseOwnerPasswordBytes = New-Object byte[] 32
$databaseRuntimePasswordBytes = New-Object byte[] 32
$databaseRandom = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $databaseRandom.GetBytes($databaseOwnerPasswordBytes)
    $databaseRandom.GetBytes($databaseRuntimePasswordBytes)
}
finally {
    $databaseRandom.Dispose()
}
$databaseOwnerPassword = 'Sql1!' + (-join ($databaseOwnerPasswordBytes | ForEach-Object { $_.ToString('x2') }))
$databaseRuntimePassword = 'Sql1!' + (-join ($databaseRuntimePasswordBytes | ForEach-Object { $_.ToString('x2') }))
$databaseOwnerConnectionString = "Server=127.0.0.1,$SqlServerPort;Database=$databaseName;User Id=$databaseOwnerUser;Password=$databaseOwnerPassword;TrustServerCertificate=True"
$databaseRuntimeConnectionString = "Server=127.0.0.1,$SqlServerPort;Database=$databaseName;User Id=$databaseRuntimeUser;Password=$databaseRuntimePassword;TrustServerCertificate=True"

& (Join-Path $PSScriptRoot 'New-DevelopmentSecurityMaterial.ps1') `
    -DatabaseConnectionString $databaseRuntimeConnectionString `
    -OutputDirectory $outputRoot `
    -HttpsHost $ServerHost `
    -HttpsPort $HttpsPort

$composeEnvironmentPath = Join-Path $outputRoot 'compose.env'
$composeEnvironment = @"
MSSQL_PID=Developer
MSSQL_SA_PASSWORD=$databaseOwnerPassword
SQLSERVER_PORT=$SqlServerPort
CLAMAV_PORT=3310
"@
[System.IO.File]::WriteAllText($composeEnvironmentPath, $composeEnvironment)

function Quote-PowerShellSingle([string] $value) {
    return "'" + $value.Replace("'", "''") + "'"
}

$runtimeLoginSqlPath = Join-Path $outputRoot 'provision-runtime-login.sql'
$runtimeLoginSql = @"
IF SUSER_ID(N'$databaseRuntimeUser') IS NULL
    CREATE LOGIN [$databaseRuntimeUser] WITH PASSWORD = '$databaseRuntimePassword', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;
ELSE
    ALTER LOGIN [$databaseRuntimeUser] WITH PASSWORD = '$databaseRuntimePassword';
"@
[System.IO.File]::WriteAllText($runtimeLoginSqlPath, $runtimeLoginSql)

$launcherPath = Join-Path $outputRoot 'start-field-test-server.ps1'
$apiLauncherPath = Join-Path $outputRoot 'run-api.local.ps1'
$runtimeUserTemplatePath = Join-Path $repositoryRoot 'deploy/sqlserver/runtime-user.template.sql'
$runtimeVerificationPath = Join-Path $repositoryRoot 'deploy/sqlserver/verify-runtime-security.sql'
$launcher = @"
`$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $(Quote-PowerShellSingle $repositoryRoot)

docker info *> `$null
if (`$LASTEXITCODE -ne 0) { throw 'Docker Desktop is not running.' }

docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) up -d sqlserver clamav
if (`$LASTEXITCODE -ne 0) { throw 'Docker Compose failed to start SQL Server and ClamAV.' }

`$sqlContainerId = docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) ps -q sqlserver
`$sqlReady = `$false
foreach (`$attempt in 1..90) {
    `$health = docker inspect --format '{{.State.Health.Status}}' `$sqlContainerId 2>`$null
    if (`$health -eq 'healthy') { `$sqlReady = `$true; break }
    Start-Sleep -Seconds 2
}
if (-not `$sqlReady) { throw 'SQL Server did not become ready within three minutes.' }

docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) exec --env 'SQLCMDPASSWORD=$databaseOwnerPassword' -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q "IF DB_ID(N'$databaseName') IS NULL CREATE DATABASE [$databaseName];"
if (`$LASTEXITCODE -ne 0) { throw 'Database creation failed.' }

`$env:DISPLAYCONTROL_MIGRATION_CONNECTION=$(Quote-PowerShellSingle $databaseOwnerConnectionString)
dotnet tool restore
if (`$LASTEXITCODE -ne 0) { throw 'The pinned dotnet-ef tool could not be restored.' }
dotnet build src/DisplayControl.Api/DisplayControl.Api.csproj --configuration Release
if (`$LASTEXITCODE -ne 0) { throw 'The Release build required for migration failed.' }
dotnet ef database update --project src/DisplayControl.SqlServerMigrations --startup-project src/DisplayControl.Api --configuration Release --no-build
if (`$LASTEXITCODE -ne 0) { throw 'SQL Server migration failed.' }

Get-Content -Raw -LiteralPath $(Quote-PowerShellSingle $runtimeLoginSqlPath) | docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) exec --env 'SQLCMDPASSWORD=$databaseOwnerPassword' -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d master
if (`$LASTEXITCODE -ne 0) { throw 'Database runtime login creation failed.' }
Get-Content -Raw -LiteralPath $(Quote-PowerShellSingle $runtimeUserTemplatePath) | docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) exec --env 'SQLCMDPASSWORD=$databaseOwnerPassword' -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -d '$databaseName' -v RuntimeUser='$databaseRuntimeUser'
if (`$LASTEXITCODE -ne 0) { throw 'Database runtime grants failed.' }
Get-Content -Raw -LiteralPath $(Quote-PowerShellSingle $runtimeVerificationPath) | docker compose --project-name '$composeProjectName' --env-file $(Quote-PowerShellSingle $composeEnvironmentPath) exec --env 'SQLCMDPASSWORD=$databaseRuntimePassword' -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U '$databaseRuntimeUser' -C -b -d '$databaseName'
if (`$LASTEXITCODE -ne 0) { throw 'Database runtime security verification failed.' }

npm.cmd run build --workspace admin-web
if (`$LASTEXITCODE -ne 0) { throw 'The administration application build failed.' }

Write-Host 'Field-test server starting at https://${ServerHost}:$HttpsPort'
Write-Host 'Keep this window open during the Raspberry Pi test.'
& $(Quote-PowerShellSingle $apiLauncherPath)
"@
[System.IO.File]::WriteAllText($launcherPath, $launcher)

$serverCaPath = Join-Path $outputRoot 'field-test-server-ca.crt'
Write-Host ''
Write-Host 'SQL Server field-test environment created.'
Write-Host "1. Trust the local CA for the current Windows user (one time):"
Write-Host "   Import-Certificate -FilePath '$serverCaPath' -CertStoreLocation Cert:\CurrentUser\Root"
Write-Host '2. Start Docker Desktop.'
Write-Host "3. Start the complete server: & '$launcherPath'"
Write-Host "4. Open https://${ServerHost}:$HttpsPort"
Write-Host "5. Give the Pi installer this public CA: $serverCaPath"
