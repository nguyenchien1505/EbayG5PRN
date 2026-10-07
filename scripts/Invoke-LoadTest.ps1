# F05 (docs/architecture/LOAD.md): runs inside scripts/Test-AppStack.ps1 -Load against its isolated stack. Seeds the load dataset,
# runs JMeter (HTTP) and the SignalR client (realtime) at the same time, reads the replica logs, gates by profile and writes the report
# to artifacts/load/<utc>-<profile>/ (kept after the stack is removed). The SA password arrives in EBAY_LOAD_SA_PASSWORD only.
param(
    [Parameter(Mandatory)][ValidateSet('Short', 'Full', 'Stress')][string]$Profile,
    [Parameter(Mandatory)][string[]]$Compose,
    [Parameter(Mandatory)][string]$Work,
    [Parameter(Mandatory)][int]$HttpsPort,
    [Parameter(Mandatory)][int]$SqlPort
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$settings = @{
    Short  = @{ Scale = 0.1; Steps = @(10);              Ramp = 10; Seconds = 60;  Pairs = 10; GateLatency = $false }
    Full   = @{ Scale = 1.0; Steps = @(50);              Ramp = 30; Seconds = 600; Pairs = 20; GateLatency = $true }
    Stress = @{ Scale = 1.0; Steps = @(50, 100, 200, 400, 800, 1600); Ramp = 30; Seconds = 120; Pairs = 20; GateLatency = $false }
}[$Profile]
$base = "https://localhost:$HttpsPort"
$loadDir = Join-Path $Work 'load'
$report = Join-Path $repoRoot ("artifacts/load/{0:yyyyMMdd-HHmmss}-{1}" -f [DateTime]::UtcNow, $Profile.ToLowerInvariant())
New-Item -ItemType Directory -Force -Path $loadDir, $report | Out-Null

function Get-Percentile([double[]]$values, [double]$p) {
    if ($values.Count -eq 0) { return [double]::NaN }
    $sorted = $values | Sort-Object
    return $sorted[[Math]::Min($sorted.Count - 1, [Math]::Ceiling($p * $sorted.Count) - 1)]
}

# 1. Wait for HTTPS readiness through nginx (certificate check skipped only for this poll; the load tools verify the CA).
$deadline = [DateTime]::UtcNow.AddSeconds(240)
function Test-Ready { try { (Invoke-WebRequest -Uri "$base/health/ready" -SkipCertificateCheck -TimeoutSec 5).StatusCode -eq 200 } catch { $false } }
while (-not (Test-Ready)) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'Stack not ready for the load test.' }
    Start-Sleep -Seconds 2
}

# 2. Load dataset through the real services (password random per run, by environment only).
$env:EBAY_LOAD_PASSWORD = 'Load!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(18))
if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$env:EBAY_LOAD_PASSWORD" }
$env:EBAY_LOAD_SQL = "Server=127.0.0.1,$SqlPort;Database=EbayGroup5;User ID=sa;Password=$env:EBAY_LOAD_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
try {
    dotnet run --project tests/EbayGroup5.Load -c Release --no-launch-profile -- seed --scale $settings.Scale --out $loadDir
    if ($LASTEXITCODE -ne 0) { throw 'Load seed failed.' }
} finally { $env:EBAY_LOAD_SQL = $null }
dotnet build tests/EbayGroup5.Load -c Release --nologo -v q | Out-Null

# Deadlocks SQL Server recorded during the seed (system_health files): count, locked objects and statements (parameters, no values).
$deadlockSql = Join-Path $loadDir 'deadlocks.sql'
Set-Content -LiteralPath $deadlockSql -Value @'
SET QUOTED_IDENTIFIER ON; SET NOCOUNT ON;
SELECT CAST(event_data AS XML) AS x INTO #d FROM sys.fn_xe_file_target_read_file(N'/var/opt/mssql/log/system_health*.xel', NULL, NULL, NULL)
WHERE object_name = N'xml_deadlock_report';
SELECT CONCAT('deadlocks=', COUNT(*)) FROM #d;
SELECT TOP 12 CONCAT(COUNT(*), ' x ', obj, ' | ', idx, ' | ', mode) FROM (
  SELECT r.value('@objectname','nvarchar(200)') obj, r.value('@indexname','nvarchar(200)') idx, r.value('@mode','nvarchar(10)') mode
  FROM #d CROSS APPLY x.nodes('//resource-list/*') n(r)) t GROUP BY obj, idx, mode ORDER BY COUNT(*) DESC;
SELECT TOP 8 CONCAT(COUNT(*), ' x ', b) FROM (
  SELECT LEFT(LTRIM(REPLACE(REPLACE(p.value('(inputbuf)[1]','nvarchar(max)'), CHAR(13), ' '), CHAR(10), ' ')), 260) b
  FROM #d CROSS APPLY x.nodes('//process-list/process') n(p)) t GROUP BY b ORDER BY COUNT(*) DESC;
'@
$sqlContainer = docker @Compose ps -q sqlserver
docker cp $deadlockSql "${sqlContainer}:/tmp/deadlocks.sql" | Out-Null
$deadlocks = docker @Compose exec -T sqlserver /bin/sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -h -1 -W -d EbayGroup5 -i /tmp/deadlocks.sql'
$deadlocks | Set-Content -LiteralPath (Join-Path $report 'seed-deadlocks.txt')
Write-Output "Seed deadlocks (SQL Server): $(@($deadlocks)[0])"
# Q-PERF-001: the seed writes orders 8 at a time; any deadlock is a regression of the lock fix.
if ("$(@($deadlocks)[0])" -notmatch '^deadlocks=0$') { throw "SQL Server recorded deadlocks while seeding: $(@($deadlocks)[0]) (see $report/seed-deadlocks.txt)." }

# 3. JMeter and the realtime client together, per step.
$jmeterBin = pwsh -NoProfile -File "$PSScriptRoot/Install-JMeter.ps1"
if ($LASTEXITCODE -ne 0) { throw 'JMeter install failed.' }
$jmeter = if ($IsWindows) { Join-Path $jmeterBin 'jmeter.bat' } else { Join-Path $jmeterBin 'jmeter' }
$steps = @()
try {
    foreach ($threads in $settings.Steps) {
        $stepDir = Join-Path $loadDir "step-$threads"
        New-Item -ItemType Directory -Force -Path $stepDir | Out-Null
        $jtl = Join-Path $stepDir 'results.jtl'
        $jmeterArgs = @('-n', '-t', (Join-Path $repoRoot 'tests/load/ebaygroup5.jmx'), "-Jhost=localhost", "-Jport=$HttpsPort", "-Jthreads=$threads",
            "-Jramp=$($settings.Ramp)", "-Jduration=$($settings.Seconds)", "-Jusers=$(Join-Path $loadDir 'jmeter-users.csv')",
            '-l', $jtl, '-j', (Join-Path $stepDir 'jmeter.log'), '-e', '-o', (Join-Path $stepDir 'dashboard'))
        Write-Output "Load step: $threads JMeter threads + $($settings.Pairs) chat pairs for $($settings.Seconds)s"
        $process = Start-Process -FilePath $jmeter -ArgumentList $jmeterArgs -NoNewWindow -PassThru -RedirectStandardOutput (Join-Path $stepDir 'jmeter.out')
        $chatJson = Join-Path $stepDir 'chat.json'
        dotnet run --project tests/EbayGroup5.Load -c Release --no-build --no-launch-profile -- chat --base $base --ca (Join-Path $Work 'tls/ca.crt') `
            --pairs (Join-Path $loadDir 'chat-pairs.csv') --count $settings.Pairs --seconds $settings.Seconds --out $chatJson
        $chatExit = $LASTEXITCODE
        if (-not $process.WaitForExit(($settings.Seconds + 300) * 1000)) { $process.Kill($true); throw 'JMeter did not finish.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $jtl)) { throw "JMeter failed (exit $($process.ExitCode)); see $stepDir/jmeter.log." }
        if ($chatExit -ne 0) { throw 'Realtime chat client failed.' }

        $rows = @(Import-Csv -LiteralPath $jtl)
        $labels = foreach ($group in ($rows | Group-Object label | Sort-Object Name)) {
            $elapsed = [double[]]@($group.Group | ForEach-Object { [double]$_.elapsed })
            $errors = @($group.Group | Where-Object { $_.success -ne 'true' }).Count
            [ordered]@{ label = $group.Name; samples = $group.Count; errors = $errors; errorPct = [Math]::Round(100.0 * $errors / $group.Count, 2)
                p50 = Get-Percentile $elapsed 0.50; p95 = Get-Percentile $elapsed 0.95; p99 = Get-Percentile $elapsed 0.99; max = ($elapsed | Measure-Object -Maximum).Maximum }
        }
        $errorCount = @($rows | Where-Object { $_.success -ne 'true' }).Count
        # 429 = the per-user chat/auth limits doing their job (threads beyond 50 share the 50 load accounts); reported apart.
        $limited = @($rows | Where-Object { $_.responseCode -eq '429' }).Count
        $codes = $rows | Where-Object { $_.success -ne 'true' } | Group-Object responseCode | ForEach-Object { "$($_.Name)x$($_.Count)" }
        $steps += [ordered]@{
            threads = $threads; seconds = $settings.Seconds; samples = $rows.Count; errors = $errorCount; rateLimited429 = $limited
            errorPct = if ($rows.Count) { [Math]::Round(100.0 * $errorCount / $rows.Count, 3) } else { 100 }
            throughputPerSecond = [Math]::Round($rows.Count / $settings.Seconds, 1); errorCodes = @($codes); labels = @($labels)
            chat = Get-Content -LiteralPath $chatJson -Raw | ConvertFrom-Json
        }
        Copy-Item -Recurse -LiteralPath (Join-Path $stepDir 'dashboard') -Destination (Join-Path $report "dashboard-$threads")
        Copy-Item -LiteralPath $chatJson -Destination (Join-Path $report "chat-$threads.json")
    }
} finally { $env:EBAY_LOAD_PASSWORD = $null }

# 4. Which replica held each chat socket (app log "Chat connection opened user=<id>" per container).
$owners = @{}
foreach ($line in (docker @Compose logs --no-color web 2>&1)) {
    if ("$line" -match '^(\S+)\s+\|.*Chat connection opened user=(\d+)') { $owners[$Matches[2]] = $Matches[1] }
}
$last = $steps[-1]
$pairUsers = @($last.chat.pairUsers)
$crossPairs = @($pairUsers | Where-Object { $owners["$($_[0])"] -and $owners["$($_[1])"] -and $owners["$($_[0])"] -ne $owners["$($_[1])"] }).Count
$replicas = @($owners.Values | Sort-Object -Unique).Count

# 5. Machine and run facts.
$cpu = if ($IsWindows) { (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() } else { ((Get-Content /proc/cpuinfo | Select-String '^model name') | Select-Object -First 1).Line.Split(':', 2)[1].Trim() }
$ramGb = if ($IsWindows) { [Math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1) } else { [Math]::Round(([double]((Get-Content /proc/meminfo | Select-String '^MemTotal').Line -replace '\D', '')) / 1MB, 1) }
$dockerInfo = docker info --format '{{.NCPU}} CPU, {{.MemTotal}} bytes, {{.OperatingSystem}}'
$facts = [ordered]@{
    profile = $Profile; commit = (git rev-parse --short HEAD) + $(if (git status --porcelain --untracked-files=no) { '+dirty' } else { '' }); utc = [DateTime]::UtcNow.ToString('o')
    machine = [ordered]@{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; cpu = $cpu; logicalCores = [Environment]::ProcessorCount; ramGb = $ramGb; docker = "$dockerInfo" }
    java = ((java -version 2>&1) | Select-Object -First 1).ToString(); jmeter = '5.6.3'
    dataset = Get-Content -LiteralPath (Join-Path $loadDir 'dataset.json') -Raw | ConvertFrom-Json
    webReplicas = 2; chatSocketReplicas = $replicas; crossReplicaPairs = $crossPairs; steps = $steps
}
$facts | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $report 'report.json')

# 6. Gates (LOAD.md). Stress only reports where the limits are crossed.
$failures = @()
foreach ($step in $steps) {
    $chat = $step.chat
    $line = "threads=$($step.threads) samples=$($step.samples) errors=$($step.errorPct)% (429: $($step.rateLimited429)) throughput=$($step.throughputPerSecond)/s chat sent=$($chat.sent) received=$($chat.received) missing=$($chat.missing) duplicates=$($chat.duplicates) p95=$($chat.p95Ms)ms"
    Write-Output "STEP $line"
    foreach ($label in $step.labels) { Write-Output ("  {0,-26} n={1,-6} err={2,5}% p50={3,5} p95={4,5} p99={5,5} max={6}" -f $label.label, $label.samples, $label.errorPct, $label.p50, $label.p95, $label.p99, $label.max) }
    if ($Profile -eq 'Stress') { continue }
    if ($step.errorPct -ge 1) { $failures += "JMeter errors $($step.errorPct)% >= 1% ($($step.errorCodes -join ', '))" }
    if ($chat.missing -gt 0 -or $chat.duplicates -gt 0 -or $chat.sendErrors -gt 0 -or $chat.received -eq 0) { $failures += "Realtime delivery: missing=$($chat.missing) duplicates=$($chat.duplicates) sendErrors=$($chat.sendErrors)" }
    if ($settings.GateLatency) {
        foreach ($label in $step.labels) { if ($label.p95 -ge 1000) { $failures += "p95 $($label.label) = $($label.p95) ms >= 1000 ms" } }
        if ($chat.p95Ms -ge 1000) { $failures += "Realtime p95 $($chat.p95Ms) ms >= 1000 ms" }
    }
}
if ($Profile -eq 'Stress') {
    $knee = $steps | Where-Object { $_.errorPct -ge 1 -or @($_.labels | Where-Object { $_.p95 -ge 1000 }).Count -gt 0 -or $_.chat.p95Ms -ge 1000 } | Select-Object -First 1
    Write-Output ($knee ? "STRESS first step over a limit: $($knee.threads) threads" : "STRESS no step crossed a limit up to $($settings.Steps[-1]) threads")
} else {
    if ($replicas -lt 2) { $failures += "Chat sockets on $replicas replica(s); expected 2" }
    if ($crossPairs -lt 1) { $failures += 'No chat pair had its two sides on different replicas' }
}
Write-Output "Chat sockets on $replicas replica(s); $crossPairs/$($pairUsers.Count) pairs had their two sides on different replicas"
Write-Output "Report: $report"
if ($failures.Count -gt 0) { $failures | ForEach-Object { Write-Output "FAIL $_" }; throw "Load gates failed ($Profile)." }
Write-Output "PASS load test ($Profile)"
