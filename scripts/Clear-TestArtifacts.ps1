# Lists, and with -Apply removes, ONLY test artifacts this repo creates (runbook LOCAL_INFRA, "Vòng đời Docker"):
#   - stopped/retained Test-CI containers named ebaygroup5-ci-<32 hex>[-redis]
#   - retained Test-AppStack compose projects ebaygroup5-stack-<32 hex>: their containers, network, named volumes and image tags (F01)
#   - test databases EbayGroup5_Test_<32 hex> in the local ebaygroup5-local SQL Server (preview DBs only with -IncludePreview)
# Never touches the EbayGroup5 database, named volumes, images or other Docker projects.
param(
    [switch]$Apply,
    [switch]$IncludePreview,
    [switch]$DatabasesOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $mode = if ($Apply) { 'APPLY' } else { 'LIST ONLY (pass -Apply to remove)' }
    Write-Output "Clear-TestArtifacts: $mode"

    if (-not $DatabasesOnly) {
        $containers = @(docker ps -a --filter 'name=^ebaygroup5-ci-' --format '{{.Names}}' | Where-Object { $_ -cmatch '^ebaygroup5-ci-[0-9a-f]{32}(-redis)?$' })
        if ($LASTEXITCODE -ne 0) { throw 'Cannot list Docker containers.' }
        Write-Output "Test-CI containers: $($containers.Count)"
        $containers | ForEach-Object { Write-Output "  $_" }
        if ($Apply -and $containers.Count -gt 0) {
            # -v removes only anonymous volumes attached to these containers.
            docker rm -f -v @containers | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not remove all Test-CI containers.' }
        }

        $stacks = @(docker ps -a --filter 'label=com.docker.compose.project' --format '{{.Label "com.docker.compose.project"}}' |
            Where-Object { $_ -cmatch '^ebaygroup5-stack-[0-9a-f]{32}$' } | Sort-Object -Unique)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot list Docker compose projects.' }
        Write-Output "Test-AppStack projects: $($stacks.Count)"
        $stacks | ForEach-Object { Write-Output "  $_" }
        if ($Apply) {
            foreach ($stack in $stacks) {
                # The name matched the strict pattern above; only that project's own resources are removed.
                docker compose -p $stack down --remove-orphans 2>$null | Out-Null
                $volumes = @(docker volume ls --quiet --filter "label=com.docker.compose.project=$stack")
                if ($volumes.Count -gt 0) { docker volume rm @volumes | Out-Null }
                $hex = $stack.Substring('ebaygroup5-stack-'.Length)
                docker image rm "ebaygroup5-web:stack-$hex" "ebaygroup5-migrate:stack-$hex" 2>$null | Out-Null
            }
        }
    }

    $sqlState = docker compose ps --status running --services 2>$null
    if (@($sqlState) -notcontains 'sqlserver') {
        Write-Output 'Local SQL (compose service sqlserver) is not running; skipped test databases.'
        return
    }
    function Invoke-LocalSql([string]$query) {
        # The SA password stays inside the container environment; nothing secret is passed on the host command line.
        $output = docker compose exec -T sqlserver /bin/sh -c "SQLCMDPASSWORD=`"`$MSSQL_SA_PASSWORD`" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -Q `"SET NOCOUNT ON; $query`""
        if ($LASTEXITCODE -ne 0) { throw 'Local SQL command failed.' }
        return $output
    }
    $pattern = if ($IncludePreview) { '^EbayGroup5_Test_(D02Visual)?[0-9a-f]{32}$' } else { '^EbayGroup5_Test_[0-9a-f]{32}$' }
    $databases = @(Invoke-LocalSql "SELECT name FROM sys.databases WHERE name LIKE 'EbayGroup5[_]Test[_]%' ORDER BY name" |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -cmatch $pattern })
    Write-Output "Test databases: $($databases.Count)"
    $databases | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" }
    if ($databases.Count -gt 20) { Write-Output "  … and $($databases.Count - 20) more" }
    if ($Apply) {
        foreach ($name in $databases) {
            # Name already matched the strict pattern above, so bracket quoting is safe.
            Invoke-LocalSql "ALTER DATABASE [$name] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$name];" | Out-Null
        }
        Write-Output "Removed $($databases.Count) test database(s)."
    }
} finally {
    Pop-Location
}
