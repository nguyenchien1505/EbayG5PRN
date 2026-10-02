# Dot-source to configure the current terminal; .NET does not load Compose .env files.
param([switch]$Migration)
$repoRoot = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) { throw 'Run Initialize-LocalEnvironment.ps1 first.' }
$localInfra = @{}
foreach ($line in [IO.File]::ReadAllLines($envPath)) {
    if ($line -match '^\s*(#|$)') { continue }
    if ($line -notmatch '^(SQLSERVER_PORT|REDIS_PORT|MSSQL_SA_PASSWORD|REDIS_PASSWORD)=(.+)$') {
        throw 'Invalid local .env entry; use the format in .env.example.'
    }
    $localInfra[$Matches[1]] = $Matches[2]
}
foreach ($key in @('SQLSERVER_PORT', 'REDIS_PORT')) {
    $port = 0
    if (-not [int]::TryParse($localInfra[$key], [ref]$port) -or $port -lt 1024 -or $port -gt 65535) { throw "Invalid $key." }
}
foreach ($key in @('MSSQL_SA_PASSWORD', 'REDIS_PASSWORD')) {
    # Constrain syntax so a password is safe in Redis config and both connection strings.
    if ($localInfra[$key] -cnotmatch '^[A-Za-z0-9!_-]{24,128}$' -or $localInfra[$key] -eq 'GENERATE_WITH_SETUP_SCRIPT') {
        throw "Invalid $key. Use the setup-generated value, or 24-128 characters A-Z/a-z/0-9/!/_/-."
    }
}
$env:ConnectionStrings__MigrationConnection = $null
if ($Migration) {
    $env:ConnectionStrings__MigrationConnection = "Server=127.0.0.1,$($localInfra.SQLSERVER_PORT);Database=EbayGroup5;User ID=sa;Password=$($localInfra.MSSQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True"
}
$appEnvPath = Join-Path $repoRoot '.env.database'
$env:ConnectionStrings__DefaultConnection = $null
if (Test-Path -LiteralPath $appEnvPath) {
    $appEntry = [IO.File]::ReadAllText($appEnvPath).Trim()
    if ($appEntry -cnotmatch '^SQL_APP_PASSWORD=([A-Za-z0-9!_-]{24,128})$') { throw 'Invalid .env.database format.' }
    $env:ConnectionStrings__DefaultConnection = "Server=127.0.0.1,$($localInfra.SQLSERVER_PORT);Database=EbayGroup5;User ID=EbayGroup5App;Password=$($Matches[1]);Encrypt=True;TrustServerCertificate=True"
    Remove-Variable appEntry
}
$env:ConnectionStrings__Redis = "127.0.0.1:$($localInfra.REDIS_PORT),password=$($localInfra.REDIS_PASSWORD),abortConnect=false"
Remove-Variable localInfra, line, key, port -ErrorAction SilentlyContinue
Write-Output 'Local configuration loaded (values hidden). Admin connection is exported only with -Migration.'
