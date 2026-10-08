# Requires PowerShell 7, Docker Linux engine and SDK from global.json on PATH.
# Isolated SQL/Redis containers are removed after a passing run; a failing run (or -KeepContainers) keeps them for diagnosis.
# Remove kept ones later with scripts/Clear-TestArtifacts.ps1 -Apply (runbook LOCAL_INFRA).
param([switch]$KeepContainers)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$containerName = 'ebaygroup5-ci-' + [Guid]::NewGuid().ToString('N')
$created = $false
$redisContainer = $containerName + '-redis'
$redisCreated = $false
$previousPassword = $env:MSSQL_SA_PASSWORD
$previousTestSql = $env:EBAY_TEST_SQL
$previousRedisPassword = $env:REDIS_PASSWORD
$previousTestRedis = $env:EBAY_TEST_REDIS
$previousRedisConnection = $env:ConnectionStrings__Redis
$succeeded = $false
Push-Location $repoRoot
try {
    pwsh -NoProfile -File "$PSScriptRoot/Verify-Planning.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Planning validation failed.' }
    pwsh -NoProfile -File "$PSScriptRoot/Verify-Migrations.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Migration checksum validation failed.' }
    pwsh -NoProfile -File "$PSScriptRoot/Verify-ClientAssets.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Pinned browser assets failed.' }
    # F04 (SECURITY_TESTS.md): no secrets in tracked files or history; no NuGet package with a known vulnerability.
    pwsh -NoProfile -File "$PSScriptRoot/Test-Secrets.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Secret scan failed.' }
    node --test tests/chat-client.test.mjs tests/dispute-draft.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Chat client state tests failed; Node20+ is required.' }
    pwsh -NoProfile -File "$repoRoot/tests/cd-artifact.test.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'CD artifact rejection tests failed.' }
    pwsh -NoProfile -File "$repoRoot/tests/cd-archive.test.ps1"
    if ($LASTEXITCODE -ne 0) { throw 'Docker archive identity mapping tests failed.' }
    dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    $audit = @(dotnet list EbayGroup5.sln package --vulnerable --include-transitive 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($audit -match 'has the following vulnerable packages')) { $audit | Write-Output; throw 'Vulnerable NuGet packages (or the audit could not run).' }
    Write-Output 'PASS NuGet audit: no known vulnerable packages (direct and transitive).'
    dotnet build --no-restore -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    # Every SQL test creates its own database and keeps it until the container is removed; ~220 databases need ~2 GB (H01).
    $env:MSSQL_SA_PASSWORD = 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$env:MSSQL_SA_PASSWORD" }
    $health = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 5 -Q "SELECT 1" > /dev/null'
    docker run --detach --name $containerName --publish '127.0.0.1::1433' `
        --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD `
        --env MSSQL_MEMORY_LIMIT_MB=3072 --memory 4g `
        --health-cmd $health --health-interval 3s --health-timeout 8s --health-retries 40 `
        'mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated CI SQL container.' }
    $created = $true
    Write-Output "Isolated SQL container: $containerName"
    $env:REDIS_PASSWORD = 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$env:REDIS_PASSWORD" }
    $redisHealth = 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping | grep -qx PONG'
    $redisCommand = 'umask 077; printf ''requirepass %s\n'' "$REDIS_PASSWORD" > /tmp/redis-ci.conf; exec redis-server /tmp/redis-ci.conf'
    docker run --detach --name $redisContainer --publish '127.0.0.1::6379' --env REDIS_PASSWORD --memory 256m `
        --health-cmd $redisHealth --health-interval 2s --health-timeout 3s --health-retries 30 `
        'redis:7.4.11-alpine3.21@sha256:858f009f9709ce576febc734aa78b8f6d624b82571f9ddb6bda4377c833b3499' /bin/sh -ec $redisCommand | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create isolated CI Redis container.' }
    $redisCreated = $true
    Write-Output "Isolated Redis container: $redisContainer"
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    do {
        $healthStatus = docker inspect --format '{{.State.Health.Status}}' $containerName
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect CI SQL health.' }
        if ($healthStatus -eq 'healthy') { break }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'CI SQL readiness timed out; container retained for diagnosis.' }
        Start-Sleep -Seconds 3
    } while ($true)
    $binding = docker port $containerName 1433/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected CI SQL port binding.' }
    $env:EBAY_TEST_SQL = "Server=127.0.0.1,$($Matches[1]);Database=master;User ID=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
    $redisDeadline = [DateTime]::UtcNow.AddSeconds(90)
    while ((docker inspect --format '{{.State.Health.Status}}' $redisContainer) -ne 'healthy') {
        if ($LASTEXITCODE -ne 0 -or [DateTime]::UtcNow -gt $redisDeadline) { throw 'CI Redis readiness failed; container retained.' }
        Start-Sleep -Seconds 2
    }
    $redisBinding = docker port $redisContainer 6379/tcp
    if ($LASTEXITCODE -ne 0 -or $redisBinding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Unexpected CI Redis port binding.' }
    $env:EBAY_TEST_REDIS = "127.0.0.1:$($Matches[1]),password=$env:REDIS_PASSWORD,abortConnect=false"
    $env:ConnectionStrings__Redis = $env:EBAY_TEST_REDIS
    dotnet test tests/EbayGroup5.Database.Tests -c Release --no-build --no-restore `
        --logger 'trx;LogFileName=database.trx' --results-directory artifacts/ci/test-results
    if ($LASTEXITCODE -ne 0) { throw 'SQL integration tests failed.' }
    Write-Output 'PASS: local CI SQL/HTTP/WebSocket/Redis checks (not evidence of a GitHub Actions run).'
    $succeeded = $true
} finally {
    $env:MSSQL_SA_PASSWORD = $previousPassword
    $env:EBAY_TEST_SQL = $previousTestSql
    $env:REDIS_PASSWORD = $previousRedisPassword
    $env:EBAY_TEST_REDIS = $previousTestRedis
    $env:ConnectionStrings__Redis = $previousRedisConnection
    $remove = $succeeded -and -not $KeepContainers
    foreach ($item in @(@{ Made = $redisCreated; Name = $redisContainer }, @{ Made = $created; Name = $containerName })) {
        if (-not $item.Made) { continue }
        if ($remove) {
            # Each container holds every test database of this run; removing it stops disk growth (see runbook LOCAL_INFRA).
            docker rm -f -v $item.Name | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove $($item.Name); run scripts/Clear-TestArtifacts.ps1 -Apply." }
        } else {
            docker stop --time 20 $item.Name | Out-Null
            Write-Warning "Kept $($item.Name) for diagnosis; remove it with scripts/Clear-TestArtifacts.ps1 -Apply."
        }
    }
    Pop-Location
}
