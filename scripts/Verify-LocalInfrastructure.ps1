param([switch]$CheckPersistence)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    function Invoke-ComposeChecked {
        param([string[]]$Arguments)
        $output = & docker compose @Arguments
        if ($LASTEXITCODE -ne 0) { throw 'Docker Compose verification command failed.' }
        return $output
    }
    function Assert-Services {
        $rows = @(Invoke-ComposeChecked @('ps', '--format', 'json') | ForEach-Object { $_ | ConvertFrom-Json })
        foreach ($name in @('sqlserver', 'redis')) {
            $service = @($rows | Where-Object Service -eq $name)
            if ($service.Count -ne 1 -or $service[0].Health -ne 'healthy') { throw "$name is not healthy. Run docker compose up -d --wait first." }
        }
        $sql = Invoke-ComposeChecked @('exec', '-T', 'sqlserver', 'sh', '-ec', 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 5 -h -1 -W -Q "SET NOCOUNT ON; SELECT 1"')
        if (($sql -join '').Trim() -ne '1') { throw 'SQL SELECT 1 failed.' }
        $pong = Invoke-ComposeChecked @('exec', '-T', 'redis', 'sh', '-ec', 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping')
        if (($pong -join '').Trim() -ne 'PONG') { throw 'Redis authenticated PING failed.' }
        $unauth = Invoke-ComposeChecked @('exec', '-T', 'redis', 'redis-cli', 'ping')
        if (($unauth -join '') -notmatch 'NOAUTH') { throw 'Redis accepted an unauthenticated connection.' }
        Write-Output 'PASS: both services healthy; SQL SELECT 1; Redis authenticated PING and anonymous denial.'
    }
    Invoke-ComposeChecked @('config', '--quiet')
    Assert-Services
    if ($CheckPersistence) {
        # Only project containers are recreated. No volume or database is deleted.
        $probe = [Guid]::NewGuid().ToString('N')
        $marker = "/var/opt/mssql/.a04-probe-$probe"
        $key = "ebaygroup5:a04:probe:$probe"
        Invoke-ComposeChecked @('exec', '-T', 'sqlserver', 'sh', '-ec', "printf '%s' '$probe' > '$marker'") | Out-Null
        Invoke-ComposeChecked @('exec', '-T', 'redis', 'sh', '-ec', "REDISCLI_AUTH=`"`$REDIS_PASSWORD`" redis-cli SET '$key' '$probe' EX 600") | Out-Null
        # Graceful container recreation; this does not simulate a crash or power loss.
        Invoke-ComposeChecked @('up', '-d', '--force-recreate', '--wait', '--wait-timeout', '180', 'sqlserver', 'redis') | Out-Null
        Assert-Services
        $storedSql = Invoke-ComposeChecked @('exec', '-T', 'sqlserver', 'cat', $marker)
        $storedRedis = Invoke-ComposeChecked @('exec', '-T', 'redis', 'sh', '-ec', "REDISCLI_AUTH=`"`$REDIS_PASSWORD`" redis-cli GET '$key'")
        if (($storedSql -join '').Trim() -ne $probe -or ($storedRedis -join '').Trim() -ne $probe) { throw 'Volume persistence check failed.' }
        Invoke-ComposeChecked @('exec', '-T', 'sqlserver', 'rm', '--', $marker) | Out-Null
        Write-Output 'PASS: SQL volume file and Redis AOF key survived container recreation. Probe file removed; Redis probe expires in 10 minutes.'
    }
} finally { Pop-Location }
