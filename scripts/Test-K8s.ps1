# F06 (docs/architecture/K8S.md): isolated kind cluster ebaygroup5-k8s-<12 hex> with random passwords, free host ports and a
# throw-away certificate; deploys with Deploy-K8s.ps1 (demo seed + random demo credentials), then checks journeys, replicas, probes,
# the migration Job, volumes (pod restarts keep data and sign-ins), NetworkPolicy and finally the F01-F04 smoke.
# A passing run deletes the cluster and its image tags; a failing run (or -KeepCluster) keeps it and writes logs to artifacts/k8s/<hex>/.
param([switch]$KeepCluster)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$hex = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$cluster = "ebaygroup5-k8s-$hex"
$context = "kind-$cluster"
$work = Join-Path $repoRoot "artifacts/k8s/$hex"
$succeeded = $false
$tag = $null
function Get-FreePort { $l = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $l.Start(); try { $l.LocalEndpoint.Port } finally { $l.Stop() } }
function New-Secret {
    $value = 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$value" | Out-Host }
    return $value
}
function Kube { $out = kubectl --context $context -n ebaygroup5 @args; if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." }; $out }
function Check([bool]$ok, [string]$name) { if (-not $ok) { throw "FAIL $name" }; Write-Output "PASS $name" }
function Sql([string]$query) {
    # The SA password stays in the pod environment.
    $out = kubectl --context $context -n ebaygroup5 exec sqlserver-0 -- /bin/sh -c "SQLCMDPASSWORD=`"`$MSSQL_SA_PASSWORD`" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -d EbayGroup5 -Q `"SET NOCOUNT ON; $query`""
    if ($LASTEXITCODE -ne 0) { throw 'SQL query in the cluster failed.' }
    "$out".Trim()
}

$kind = pwsh -NoProfile -File "$PSScriptRoot/Install-Kind.ps1"
if ($LASTEXITCODE -ne 0) { throw 'kind install failed.' }
Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    pwsh -NoProfile -File "$PSScriptRoot/New-LocalTlsCertificate.ps1" -OutputDirectory (Join-Path $work 'tls') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the test certificate.' }
    $httpsPort = Get-FreePort
    $httpPort = Get-FreePort
    $demo = @{ Buyer = New-Secret; Seller = New-Secret; Admin = New-Secret }
    $secrets = @{ 'sa-password' = New-Secret; 'app-password' = New-Secret; 'redis-password' = New-Secret
                  'demo-buyer-password' = $demo.Buyer; 'demo-seller-password' = $demo.Seller; 'demo-admin-password' = $demo.Admin }
    Write-Output "Isolated kind cluster: $cluster (https://localhost:$httpsPort)"
    $deploy = & "$PSScriptRoot/Deploy-K8s.ps1" -Cluster $cluster -HttpsPort $httpsPort -HttpPort $httpPort -Secrets $secrets `
        -TlsDir (Join-Path $work 'tls') -Work $work -HelperArgs '--seed-demo', '--enable-demo-auth'
    $deploy | Where-Object { $_ -is [string] } | Write-Output
    $tag = (@($deploy) | Where-Object { "$_" -like 'TAG *' } | Select-Object -Last 1).ToString().Substring(4)
    $base = "https://localhost:$httpsPort"

    # 1. Browser journeys J1-J7 against the cluster (WSS through nginx, two replicas, seeded demo data).
    $server = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem([IO.File]::ReadAllText((Join-Path $work 'tls/server.crt')))
    $spki = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData($server.PublicKey.ExportSubjectPublicKeyInfo()))
    foreach ($role in $demo.Keys) { [Environment]::SetEnvironmentVariable("EBAY_JOURNEY_$($role.ToUpperInvariant())_PASSWORD", $demo[$role], 'Process') }
    dotnet run --project tests/EbayGroup5.Journeys -c Release --no-launch-profile -- --base $base --spki $spki --out (Join-Path $work 'journeys')
    Check ($LASTEXITCODE -eq 0) 'Browser journeys J1-J7 on Kubernetes'

    # 2. Two web replicas, both serving (nginx upstream= has two pod addresses).
    $ready = Kube get deployment web -o 'jsonpath={.status.readyReplicas}'
    Check ($ready -eq '2') "Deployment web has 2 ready replicas ($ready)"
    for ($i = 0; $i -lt 20; $i++) { $null = Invoke-WebRequest -Uri "$base/Catalog" -SkipCertificateCheck -TimeoutSec 10 }
    $upstreams = @(Kube logs deployment/nginx --tail 2000 | Select-String 'upstream=(\d+\.\d+\.\d+\.\d+):8080' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
    $podIps = @((Kube get pods -l app.kubernetes.io/name=web -o 'jsonpath={.items[*].status.podIP}') -split ' ')
    Check (@($upstreams | Where-Object { $podIps -contains $_ }).Count -ge 2) "nginx spread requests over both web pods ($($upstreams -join ', '))"

    # 3. Probes as declared in K8S.md.
    $web = Kube get deployment web -o json | ConvertFrom-Json
    $c = $web.spec.template.spec.containers[0]
    Check ($c.startupProbe.httpGet.path -eq '/health/live' -and $c.livenessProbe.httpGet.path -eq '/health/live' -and $c.readinessProbe.httpGet.path -eq '/health/ready') 'web startup/liveness /health/live, readiness /health/ready'
    foreach ($set in 'sqlserver', 'redis') {
        $s = (Kube get statefulset $set -o json | ConvertFrom-Json).spec
        Check ($null -ne $s.template.spec.containers[0].readinessProbe -and $null -ne $s.template.spec.containers[0].livenessProbe -and $s.volumeClaimTemplates.Count -eq 1) "$set has readiness/liveness probes and a volume claim"
    }
    $pvcs = @((Kube get pvc -o 'jsonpath={.items[*].status.phase}') -split ' ')
    Check ($pvcs.Count -eq 2 -and @($pvcs | Where-Object { $_ -ne 'Bound' }).Count -eq 0) 'SQL and Redis volume claims are Bound'

    # 4. Migration Job completed; running it again applies nothing new.
    Check ((Kube get job migrate -o 'jsonpath={.status.succeeded}') -eq '1') 'migrate Job completed'
    $migrateLog = (Kube logs job/migrate) -join ' '
    Check ($migrateLog -match 'applied (\d+) migration') "first migration run: $($Matches[0])"
    $rerun = & "$PSScriptRoot/Deploy-K8s.ps1" -Cluster $cluster -HttpsPort $httpsPort -HttpPort $httpPort -Secrets $secrets -TlsDir (Join-Path $work 'tls') -Work $work
    Check (((Kube logs job/migrate) -join ' ') -match 'applied 0 migration') 'redeploy re-runs the migration Job with 0 new migrations'

    # 5. Volumes and shared sign-in: one session survives SQL, Redis and web pod restarts.
    # Plain HttpClient: Invoke-WebRequest cannot stop at a 302. Certificate checks are skipped only here (smoke/journeys verify TLS).
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.CookieContainer = [Net.CookieContainer]::new()
    $handler.ServerCertificateCustomValidationCallback = [Net.Http.HttpClientHandler]::DangerousAcceptAnyServerCertificateValidator
    $client = [Net.Http.HttpClient]::new($handler)
    $client.BaseAddress = [Uri]"$base/"
    function PostForm([string]$path, [hashtable]$fields) {
        $page = $client.GetStringAsync($path).GetAwaiter().GetResult()
        $body = [Collections.Generic.Dictionary[string, string]]::new()
        foreach ($key in $fields.Keys) { $body[$key] = $fields[$key] }
        $body['__RequestVerificationToken'] = [Net.WebUtility]::HtmlDecode([regex]::Match($page, 'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value)
        [int]$client.PostAsync($path, [Net.Http.FormUrlEncodedContent]::new($body)).GetAwaiter().GetResult().StatusCode
    }
    # A fresh account: the session under test belongs to this run only.
    $email = "k8s-$hex@example.invalid"; $password = New-Secret
    Check ((PostForm 'Account/Register' @{ Name = "K8s $hex"; Email = $email; Password = $password }) -eq 302) 'register a test account'
    Check ((PostForm 'Account/Login' @{ Email = $email; Password = $password }) -eq 302) 'test account signs in'
    function SignedIn { try { [int]$client.GetAsync('Account/Me').GetAwaiter().GetResult().StatusCode -eq 200 } catch { $false } }
    function WaitWeb { Kube rollout status deployment/web --timeout 300s | Out-Null; $d = [DateTime]::UtcNow.AddSeconds(120); while (-not (SignedIn) -and [DateTime]::UtcNow -lt $d) { Start-Sleep 2 } }
    $orders = Sql 'SELECT COUNT(*) FROM dbo.OrderTable'
    Kube delete pod sqlserver-0 --wait=true | Out-Null
    Kube rollout status statefulset/sqlserver --timeout 600s | Out-Null
    WaitWeb
    Check ((Sql 'SELECT COUNT(*) FROM dbo.OrderTable') -eq $orders) "SQL data survives a pod restart ($orders orders incl. the journey's)"
    Check (SignedIn) 'session still valid after the SQL pod restart'
    Kube delete pod redis-0 --wait=true | Out-Null
    Kube rollout status statefulset/redis --timeout 300s | Out-Null
    WaitWeb
    Check (SignedIn) 'session still valid after the Redis pod restart (key ring kept on the volume)'
    $victim = @((Kube get pods -l app.kubernetes.io/name=web -o 'jsonpath={.items[*].metadata.name}') -split ' ')[0]
    Kube delete pod $victim --wait=true | Out-Null
    WaitWeb
    Check ((Kube get deployment web -o 'jsonpath={.status.readyReplicas}') -eq '2') 'web is back to 2 ready replicas after a pod is deleted'
    Check (SignedIn) 'session still valid after a web pod is replaced'

    # 6. NetworkPolicy: a stray pod cannot reach SQL or web directly; it can reach nginx (control).
    $nginxImage = (Kube get deployment nginx -o 'jsonpath={.spec.template.spec.containers[0].image}')
    $probe = kubectl --context $context -n ebaygroup5 run "netcheck-$hex" --image=$nginxImage --restart=Never --rm -i --quiet --command -- /bin/sh -c `
        'for t in sqlserver:1433 web:8080 redis:6379 nginx:8443; do h=${t%:*}; p=${t#*:}; if nc -z -w 3 $h.ebaygroup5.svc.cluster.local $p; then echo "$t OPEN"; else echo "$t BLOCKED"; fi; done'
    $probe = ($probe | Out-String)
    Check ($probe -match 'sqlserver:1433 BLOCKED' -and $probe -match 'web:8080 BLOCKED' -and $probe -match 'redis:6379 BLOCKED' -and $probe -match 'nginx:8443 OPEN') "NetworkPolicy: $(($probe -split "?
" | Where-Object { $_ }) -join '; ')"

    # 7. F01-F04 smoke last (its spoofing check deliberately exhausts this host's auth window).
    dotnet run --project tests/EbayGroup5.StackSmoke -c Release --no-launch-profile -- --https-port $httpsPort --http-port $httpPort --ca (Join-Path $work 'tls/ca.crt') --wait-seconds 120
    Check ($LASTEXITCODE -eq 0) 'Stack smoke (HTTPS/WSS, headers, TLS, sessions) on Kubernetes'
    Write-Output "PASS: Kubernetes (kind) deployment $tag"
    $succeeded = $true
} finally {
    foreach ($role in 'BUYER', 'SELLER', 'ADMIN') { [Environment]::SetEnvironmentVariable("EBAY_JOURNEY_${role}_PASSWORD", $null, 'Process') }
    if ($succeeded -and -not $KeepCluster) {
        & $kind delete cluster --name $cluster 2>$null | Out-Null
        if ($tag) { docker image rm "ebaygroup5-web:$tag" "ebaygroup5-migrate:$tag" 2>$null | Out-Null }
        Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
    } elseif (@(& $kind get clusters) -contains $cluster) {
        $logs = Join-Path $work 'logs.txt'
        kubectl --context $context -n ebaygroup5 get all,pvc,networkpolicy -o wide 2>&1 | Out-File -Encoding utf8 $logs
        kubectl --context $context -n ebaygroup5 describe pods 2>&1 | Out-File -Append -Encoding utf8 $logs
        # Scheduling/volume problems only show up in events and node conditions (and the host disk on CI runners).
        "==== events" | Out-File -Append -Encoding utf8 $logs
        kubectl --context $context get events -A --sort-by=.lastTimestamp 2>&1 | Out-File -Append -Encoding utf8 $logs
        "==== node" | Out-File -Append -Encoding utf8 $logs
        kubectl --context $context describe nodes 2>&1 | Out-File -Append -Encoding utf8 $logs
        "==== host disk" | Out-File -Append -Encoding utf8 $logs
        if ($IsLinux) { df -h 2>&1 | Out-File -Append -Encoding utf8 $logs }
        docker system df 2>&1 | Out-File -Append -Encoding utf8 $logs
        foreach ($target in 'deployment/web', 'deployment/nginx', 'statefulset/sqlserver', 'statefulset/redis', 'job/migrate') {
            "==== $target" | Out-File -Append -Encoding utf8 $logs
            kubectl --context $context -n ebaygroup5 logs $target --all-containers --tail 300 2>&1 | Out-File -Append -Encoding utf8 $logs
        }
        Write-Warning "Kept $cluster (logs in artifacts/k8s/$hex/logs.txt); remove it with scripts/Clear-TestArtifacts.ps1 -Apply."
    }
    Pop-Location
}
