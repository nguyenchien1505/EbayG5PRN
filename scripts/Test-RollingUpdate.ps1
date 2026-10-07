# F08: disposable kind only. HTTP probes are not retried; chat retries/recovery are measured separately.
param([ValidateSet('Short','Full')][string]$Profile = 'Short', [switch]$KeepCluster)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$hex = [Guid]::NewGuid().ToString('N').Substring(0,12)
$cluster = "ebaygroup5-rolling-$hex"
$context = "kind-$cluster"
$work = Join-Path $repoRoot "artifacts/rolling/$hex"
$settings = if ($Profile -eq 'Full') { @{ Scale=1.0; Workers=50; Pairs=20; Seconds=600 } } else { @{ Scale=0.1; Workers=10; Pairs=10; Seconds=120 } }
$oldEnvironment = @{}
foreach ($name in 'EBAY_LOAD_SQL','EBAY_LOAD_PASSWORD','EBAY_LOAD_WRITERS') { $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
$forward = $null; $load = $null; $tag = $null; $candidate = $null; $passed = $false
New-Item -ItemType Directory -Force -Path $work | Out-Null
function Port { $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop(); $port }
function Password { 'Roll!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(18)) }
function Kube { $result=kubectl --context $context -n ebaygroup5 @args; if ($LASTEXITCODE -ne 0) { throw 'Rolling test kubectl command failed.' }; $result }
function Start-Tool([string]$File, [string[]]$Arguments, [string]$Log) {
    $info=[Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$repoRoot
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process=[Diagnostics.Process]::Start($info)
    @{ Process=$process; Out=$process.StandardOutput.ReadToEndAsync(); Err=$process.StandardError.ReadToEndAsync(); Log=$Log }
}
function Finish-Tool($tool, [switch]$Kill) {
    if (-not $tool) { return }
    if ($Kill -and -not $tool.Process.HasExited) { $tool.Process.Kill($true) }
    $tool.Process.WaitForExit()
    ($tool.Out.GetAwaiter().GetResult() + $tool.Err.GetAwaiter().GetResult()) | Set-Content -LiteralPath $tool.Log
}
function Snapshot {
    $deployment=Kube get deployment web -o json | ConvertFrom-Json
    $pods=@((Kube get pods -l app.kubernetes.io/name=web -o json | ConvertFrom-Json).items | Where-Object { -not $_.metadata.deletionTimestamp })
    @{ revision=$deployment.metadata.annotations.'deployment.kubernetes.io/revision'; image=$deployment.spec.template.spec.containers[0].image
       ready=$deployment.status.readyReplicas; pods=@($pods | ForEach-Object { @{ uid=$_.metadata.uid; imageId=$_.status.containerStatuses[0].imageID } }) }
}
function Phase([string]$Name) { Set-Content -LiteralPath (Join-Path $work 'phase.txt') -Value $Name }
function MigrationCount {
    $count=Kube exec sqlserver-0 '--' /bin/sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -d EbayGroup5 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.SchemaVersions"'
    [int]("$count".Trim())
}
$kind = pwsh -NoProfile -File "$PSScriptRoot/Install-Kind.ps1"
if ($LASTEXITCODE -ne 0) { throw 'kind install failed.' }
Push-Location $repoRoot
try {
    $httpsPort=Port; $httpPort=Port; $sqlPort=Port
    pwsh -NoProfile -File "$PSScriptRoot/New-LocalTlsCertificate.ps1" -OutputDirectory (Join-Path $work 'tls') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Rolling test TLS setup failed.' }
    $secrets=@{ 'sa-password'=Password; 'app-password'=Password; 'redis-password'=Password }
    if ($env:GITHUB_ACTIONS -eq 'true') { foreach ($value in $secrets.Values) { Write-Output "::add-mask::$value" } }
    Write-Output "Isolated rolling cluster: $cluster ($Profile)"
    $deployed = & "$PSScriptRoot/Deploy-K8s.ps1" -Cluster $cluster -HttpsPort $httpsPort -HttpPort $httpPort -Secrets $secrets -TlsDir (Join-Path $work 'tls') -Work $work
    $deployed | Where-Object { $_ -is [string] } | Write-Output
    $tag=(@($deployed) | Where-Object { "$_" -like 'TAG *' } | Select-Object -Last 1).ToString().Substring(4)
    # Only this test deployment needs many sign-ins from one host. Business/chat rate limits remain unchanged.
    Kube set env deployment/web RateLimits__auth__PermitLimit=500 | Out-Null
    Kube rollout status deployment/web --timeout 300s | Out-Null
    $baseline=Snapshot
    $migrationCount=MigrationCount
    if ($baseline.ready -ne 2) { throw 'Rolling baseline needs two ready replicas.' }
    $candidate="ebaygroup5-web:rolling-$hex"
    "FROM ebaygroup5-web:$tag`nLABEL ebaygroup5.rolling.variant=candidate" | docker build -t $candidate -
    if ($LASTEXITCODE -ne 0) { throw 'Candidate image build failed.' }
    & $kind load docker-image $candidate --name $cluster
    if ($LASTEXITCODE -ne 0) { throw 'Candidate image load failed.' }
    $forward=Start-Tool (Get-Command kubectl).Source @('--context',$context,'-n','ebaygroup5','port-forward','--address','127.0.0.1','service/sqlserver',"${sqlPort}:1433") (Join-Path $work 'port-forward.log')
    $env:EBAY_LOAD_SQL="Server=127.0.0.1,$sqlPort;Database=EbayGroup5;User ID=sa;Password=$($secrets['sa-password']);Encrypt=True;TrustServerCertificate=True"
    $env:EBAY_LOAD_PASSWORD=Password; $env:EBAY_LOAD_WRITERS='8'
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$env:EBAY_LOAD_PASSWORD" }
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do { try { $socket=[Net.Sockets.TcpClient]::new('127.0.0.1',$sqlPort); $socket.Dispose(); break } catch { if ([DateTime]::UtcNow -gt $deadline) { throw 'SQL port-forward unavailable.' }; Start-Sleep 1 } } while ($true)
    dotnet build tests/EbayGroup5.Load -c Release --no-restore | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Rolling load tool build failed.' }
    $dotnet=(Get-Command dotnet).Source; $dll=Join-Path $repoRoot 'tests/EbayGroup5.Load/bin/Release/net8.0/EbayGroup5.Load.dll'
    & $dotnet $dll seed --scale $settings.Scale --out (Join-Path $work 'dataset')
    if ($LASTEXITCODE -ne 0) { throw 'Rolling load dataset seed failed.' }
    $loadReport=Join-Path $work 'load.json'
    Phase 'warmup'
    $load=Start-Tool $dotnet @($dll,'rolling','--base',"https://localhost:$httpsPort",'--ca',(Join-Path $work 'tls/ca.crt'),'--pairs',(Join-Path $work 'dataset/chat-pairs.csv'),
        '--count',"$($settings.Pairs)",'--workers',"$($settings.Workers)",'--seconds',"$($settings.Seconds)",'--work',$work,'--out',$loadReport) (Join-Path $work 'load.log')
    # Startup is outside the measured window; the 10-second recovery gate below is unchanged.
    $deadline=[DateTime]::UtcNow.AddSeconds(600)
    while (-not (Test-Path -LiteralPath (Join-Path $work 'ready.json'))) {
        if ($load.Process.HasExited) { Finish-Tool $load; throw 'Rolling load failed before ready (see load.log).' }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Rolling load ready timeout.' }
        Start-Sleep 1
    }
    Start-Sleep 10
    $timeline=@()
    Phase 'update'; $start=[DateTime]::UtcNow
    Kube set image deployment/web "web=$candidate" | Out-Null
    Kube rollout status deployment/web --timeout 300s | Out-Null
    $updated=Snapshot
    $timeline+=@{ phase='update'; startUtc=$start.ToString('o'); endUtc=[DateTime]::UtcNow.ToString('o'); snapshot=$updated }
    if ($updated.image -ne $candidate -or $updated.ready -ne 2) { throw 'Updated image/replicas mismatch.' }
    Start-Sleep 10
    Phase 'rollback'; $start=[DateTime]::UtcNow
    Kube rollout undo deployment/web "--to-revision=$($baseline.revision)" | Out-Null
    Kube rollout status deployment/web --timeout 300s | Out-Null
    $restored=Snapshot
    $timeline+=@{ phase='rollback'; startUtc=$start.ToString('o'); endUtc=[DateTime]::UtcNow.ToString('o'); snapshot=$restored }
    if ($restored.image -ne $baseline.image -or $restored.ready -ne 2) { throw 'Rollback image/replicas mismatch.' }
    if (@($updated.pods | Where-Object { $_.uid -in $baseline.pods.uid }).Count -or @($restored.pods | Where-Object { $_.uid -in $updated.pods.uid }).Count) { throw 'Update/rollback did not replace every pod.' }
    if ((MigrationCount) -ne $migrationCount) { throw 'Application rollback changed the migration journal.' }
    Start-Sleep 10
    Phase 'after-rollback'
    if (-not $load.Process.WaitForExit(($settings.Seconds+90)*1000)) { throw 'Rolling load process timeout.' }
    Finish-Tool $load
    $metrics=Get-Content -Raw -LiteralPath $loadReport | ConvertFrom-Json
    $facts=@{ profile=$Profile; utc=[DateTime]::UtcNow.ToString('o'); commit=(git rev-parse HEAD).Trim(); dataset=(Get-Content -Raw (Join-Path $work 'dataset/dataset.json') | ConvertFrom-Json)
        machine=@{ os=[Runtime.InteropServices.RuntimeInformation]::OSDescription; processors=[Environment]::ProcessorCount; docker=(docker info --format '{{.NCPU}} CPU {{.MemTotal}} bytes') }
        baseline=$baseline; migrationCount=$migrationCount; timeline=$timeline; metrics=$metrics; scope='single-node kind, compatible images with same business binary, no database rollback' }
    $facts | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $work 'report.json')
    foreach ($phase in 'warmup','update','rollback','after-rollback') {
        if (-not @($metrics.routes | Where-Object { $_.Phase -eq $phase }).Count) { throw "No actual HTTP samples during $phase." }
    }
    foreach ($phase in 'update','rollback') {
        if (-not @($metrics.recoveries | Where-Object { $_.Phase -eq $phase }).Count) { throw "No actual chat reconnection during $phase." }
    }
    if ($load.Process.ExitCode -ne 0) { throw 'Rolling HTTP/chat correctness or 10-second recovery gate failed (see report/load.log).' }
    if ($Profile -eq 'Full' -and (@($metrics.routes | Where-Object { $_.p95Ms -ge 1000 }).Count -or $metrics.chatP95Ms -ge 1000)) { throw 'Full rolling latency p95 gate failed.' }
    $passed=$true
    Write-Output "PASS rolling update/rollback: samples=$($metrics.httpSamples) HTTP5xx=$($metrics.http5xx), ACK=$($metrics.acked), missing=$($metrics.missing), reconnects=$($metrics.reconnects), maxRecovery=$($metrics.maxRecoveryMs)ms."
} finally {
    Finish-Tool $load -Kill; Finish-Tool $forward -Kill
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name,$oldEnvironment[$name]) }
    if ($passed -and -not $KeepCluster) {
        if ($cluster -cnotmatch '^ebaygroup5-rolling-[0-9a-f]{12}$') { throw 'Refusing to delete a non-test cluster.' }
        & $kind delete cluster --name $cluster | Out-Null
        if ($tag) { docker image rm "ebaygroup5-web:$tag" "ebaygroup5-migrate:$tag" $candidate 2>$null | Out-Null }
        # Keep aggregate evidence; credentials/TLS are never uploaded and can be cleaned with this exact run directory.
    } elseif (@(& $kind get clusters) -contains $cluster) {
        Kube get 'pods,jobs,pvc' -o wide | Out-File (Join-Path $work 'cluster.txt')
        Kube logs deployment/nginx --tail=2000 | Out-File (Join-Path $work 'nginx.log')
    }
    Pop-Location
}
