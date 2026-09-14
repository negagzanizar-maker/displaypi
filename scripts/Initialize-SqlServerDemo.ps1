param(
    [ValidatePattern('^[A-Za-z0-9_]+$')]
    [string] $DatabaseName = 'display_control',

    [ValidatePattern('^[A-Za-z0-9_]+$')]
    [string] $ApplicationLogin = 'display_app'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$environmentPath = Join-Path $repositoryRoot '.env'

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "Missing $environmentPath. Copy .env.example to .env and set local-only SQL Server passwords."
}

function Get-EnvironmentFileValue([string] $name) {
    $entry = Get-Content -LiteralPath $environmentPath |
        Where-Object { $_ -match ('^' + [regex]::Escape($name) + '=') } |
        Select-Object -Last 1
    if ([string]::IsNullOrWhiteSpace($entry)) {
        return $null
    }

    return ($entry -split '=', 2)[1]
}

$saPassword = Get-EnvironmentFileValue 'MSSQL_SA_PASSWORD'
$applicationPassword = Get-EnvironmentFileValue 'SQLSERVER_APP_PASSWORD'
if ([string]::IsNullOrWhiteSpace($saPassword)) {
    throw 'MSSQL_SA_PASSWORD is required in .env.'
}
if ([string]::IsNullOrWhiteSpace($applicationPassword)) {
    throw 'SQLSERVER_APP_PASSWORD is required in .env.'
}

Set-Location -LiteralPath $repositoryRoot
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Desktop is not running.'
}

docker compose up -d sqlserver
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose failed to start SQL Server.'
}

$clamAvAvailable = $false
$clamAvProbe = [System.Net.Sockets.TcpClient]::new()
try {
    $connectTask = $clamAvProbe.ConnectAsync('127.0.0.1', 3310)
    $clamAvAvailable = $connectTask.Wait(2000) -and $clamAvProbe.Connected
}
catch {
    $clamAvAvailable = $false
}
finally {
    $clamAvProbe.Dispose()
}
if (-not $clamAvAvailable) {
    docker compose up -d clamav
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Compose failed to start ClamAV.'
    }
}

$sqlContainer = docker compose ps -q sqlserver
if ([string]::IsNullOrWhiteSpace($sqlContainer)) {
    throw 'Docker Compose did not return the SQL Server container identifier.'
}

$sqlReady = $false
foreach ($attempt in 1..90) {
    $health = docker inspect --format '{{.State.Health.Status}}' $sqlContainer 2>$null
    if ($health -eq 'healthy') {
        $sqlReady = $true
        break
    }
    Start-Sleep -Seconds 2
}
if (-not $sqlReady) {
    throw 'SQL Server did not become healthy within three minutes.'
}

$escapedDatabaseName = $DatabaseName.Replace(']', ']]')
$createDatabaseSql = "IF DB_ID(N'$DatabaseName') IS NULL CREATE DATABASE [$escapedDatabaseName];"
docker exec --env "SQLCMDPASSWORD=$saPassword" $sqlContainer /opt/mssql-tools18/bin/sqlcmd `
    -S localhost -U sa -C -b -Q $createDatabaseSql
if ($LASTEXITCODE -ne 0) {
    throw 'SQL Server database creation failed.'
}

$env:DISPLAYCONTROL_MIGRATION_CONNECTION =
    "Server=127.0.0.1,14333;Database=$DatabaseName;User Id=sa;Password=$saPassword;TrustServerCertificate=True"
dotnet tool restore
if ($LASTEXITCODE -ne 0) {
    throw 'The pinned dotnet-ef tool could not be restored.'
}
dotnet build src/DisplayControl.Api/DisplayControl.Api.csproj --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'The Release build required for migration failed.'
}
dotnet ef database update `
    --project src/DisplayControl.SqlServerMigrations `
    --startup-project src/DisplayControl.Api `
    --configuration Release `
    --no-build
if ($LASTEXITCODE -ne 0) {
    throw 'SQL Server migration failed.'
}

$escapedLogin = $ApplicationLogin.Replace(']', ']]')
$escapedPassword = $applicationPassword.Replace("'", "''")
$loginSql = @"
IF SUSER_ID(N'$ApplicationLogin') IS NULL
    EXEC(N'CREATE LOGIN [$escapedLogin] WITH PASSWORD = ''$escapedPassword'', CHECK_POLICY = OFF, CHECK_EXPIRATION = OFF;');
ELSE
    EXEC(N'ALTER LOGIN [$escapedLogin] WITH PASSWORD = ''$escapedPassword'';');
"@
docker exec --env "SQLCMDPASSWORD=$saPassword" $sqlContainer /opt/mssql-tools18/bin/sqlcmd `
    -S localhost -U sa -C -b -d master -Q $loginSql
if ($LASTEXITCODE -ne 0) {
    throw 'SQL Server application login provisioning failed.'
}

$userSql = @"
IF DATABASE_PRINCIPAL_ID(N'$ApplicationLogin') IS NULL
    CREATE USER [$escapedLogin] FOR LOGIN [$escapedLogin];
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id
    WHERE r.name = N'db_datareader' AND m.name = N'$ApplicationLogin')
    ALTER ROLE [db_datareader] ADD MEMBER [$escapedLogin];
IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals r ON r.principal_id = drm.role_principal_id
    JOIN sys.database_principals m ON m.principal_id = drm.member_principal_id
    WHERE r.name = N'db_datawriter' AND m.name = N'$ApplicationLogin')
    ALTER ROLE [db_datawriter] ADD MEMBER [$escapedLogin];
GRANT EXECUTE ON SCHEMA::app TO [$escapedLogin];
"@
docker exec --env "SQLCMDPASSWORD=$saPassword" $sqlContainer /opt/mssql-tools18/bin/sqlcmd `
    -S localhost -U sa -C -b -d $DatabaseName -Q $userSql
if ($LASTEXITCODE -ne 0) {
    throw 'SQL Server application user grants failed.'
}

Write-Host 'SQL Server, schema, restricted application login, and ClamAV are ready.'
Write-Host 'Start the API with: .\scripts\Start-SqlServerDemo.ps1'
