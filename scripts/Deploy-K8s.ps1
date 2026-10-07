# F06 (docs/architecture/K8S.md): deploy the app to a kind cluster (created if missing). Shared by Start-K8s.ps1 (user cluster) and
# Test-K8s.ps1 (isolated). Order: images -> Secrets/ConfigMap -> SQL/Redis ready -> migrate Job complete -> web/nginx rollout.
# Passwords arrive in the hashtable $Secrets and reach kubectl through stdin only (never a command line). Prints the image tag.
param(
    [Parameter(Mandatory)][string]$Cluster,
    [Parameter(Mandatory)][int]$HttpsPort,
    [Parameter(Mandatory)][int]$HttpPort,
    [Parameter(Mandatory)][hashtable]$Secrets,   # sa-password, app-password, redis-password [+ demo-buyer/seller/admin-password]
    [Parameter(Mandatory)][string]$TlsDir,       # server.crt + server.key (New-LocalTlsCertificate.ps1)
    [Parameter(Mandatory)][string]$Work,
    [string[]]$HelperArgs = @(),                 # extra migrate-image runs after the migration, e.g. --seed-demo, --enable-demo-auth
    [ValidatePattern('^[0-9a-f]{40}$')][string]$ImageTag # F07: verified, preloaded CI images; no build on the deployment host
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$nodeImage = 'kindest/node:v1.37.0@sha256:a1ed56cfb0e7b93589bdf97c8cd566405a265939e3620fc4f5de89adff580ae5'
$podCidr = '10.244.0.0/16'
New-Item -ItemType Directory -Force -Path $Work | Out-Null
$kind = pwsh -NoProfile -File "$PSScriptRoot/Install-Kind.ps1"
if ($LASTEXITCODE -ne 0) { throw 'kind install failed.' }
$context = "kind-$Cluster"
function Kube { kubectl --context $context @args; if ($LASTEXITCODE -ne 0) { throw "kubectl $($args -join ' ') failed." } }
function Apply-Json([object]$manifest) { $manifest | ConvertTo-Json -Depth 10 | kubectl --context $context apply -f - | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'kubectl apply failed.' } }
function B64([string]$text) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($text)) }

# 1. Cluster (one node; host ports on 127.0.0.1 only).
if (@(& $kind get clusters) -notcontains $Cluster) {
    $config = Join-Path $Work 'kind-cluster.yaml'
    Set-Content -LiteralPath $config -Value @"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
networking:
  podSubnet: $podCidr
nodes:
  - role: control-plane
    image: $nodeImage
    extraPortMappings:
      - { containerPort: 30443, hostPort: $HttpsPort, listenAddress: 127.0.0.1, protocol: TCP }
      - { containerPort: 30080, hostPort: $HttpPort, listenAddress: 127.0.0.1, protocol: TCP }
"@
    & $kind create cluster --name $Cluster --config $config --wait 180s
    if ($LASTEXITCODE -ne 0) { throw "kind create cluster $Cluster failed." }
}
elseif ((docker inspect --format '{{.State.Running}}' "$Cluster-control-plane") -ne 'true') {
    docker start "$Cluster-control-plane" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the existing kind node.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while ((kubectl --context $context get --raw /readyz 2>$null) -ne 'ok') {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The restarted cluster did not become ready.' }
        Start-Sleep -Seconds 3
    }
}

# 2. Images tagged by commit (dirty trees get a unique suffix), loaded into the node.
$tag = $ImageTag
if ($ImageTag) {
    foreach ($target in 'web', 'migrate') {
        docker image inspect "ebaygroup5-${target}:$tag" --format '{{.Id}}' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Verified CI image $target is missing; refusing to build on the deploy host." }
    }
} else {
    $tag = (git -C $repoRoot rev-parse --short HEAD).Trim()
    if (git -C $repoRoot status --porcelain --untracked-files=no) { $tag += '-dirty-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) }
    foreach ($target in 'web', 'migrate') {
        docker build --target $target -t "ebaygroup5-${target}:$tag" $repoRoot
        if ($LASTEXITCODE -ne 0) { throw "docker build $target failed." }
    }
}
& $kind load docker-image "ebaygroup5-web:$tag" "ebaygroup5-migrate:$tag" --name $Cluster
if ($LASTEXITCODE -ne 0) { throw 'kind load docker-image failed.' }

# 3. Namespace, Secrets (stdin), per-cluster ConfigMap.
Apply-Json @{ apiVersion = 'v1'; kind = 'Namespace'; metadata = @{ name = 'ebaygroup5' } }
$secretData = @{}
foreach ($key in $Secrets.Keys) { $secretData[$key] = B64 $Secrets[$key] }
Apply-Json @{ apiVersion = 'v1'; kind = 'Secret'; type = 'Opaque'; metadata = @{ name = 'ebaygroup5-secrets'; namespace = 'ebaygroup5' }; data = $secretData }
Apply-Json @{ apiVersion = 'v1'; kind = 'Secret'; type = 'Opaque'; metadata = @{ name = 'ebaygroup5-tls'; namespace = 'ebaygroup5' }
    data = @{ 'server.crt' = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $TlsDir 'server.crt')))
              'server.key' = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $TlsDir 'server.key'))) } }
$existing = kubectl --context $context -n ebaygroup5 get configmap ebaygroup5-config -o jsonpath='{.data.admin-address}' 2>$null
# Until the host address is known, an unroutable documentation address (TEST-NET-1) keeps admin closed.
$admin = if ($existing) { $existing } else { '192.0.2.1' }
function Set-Config([string]$adminAddress) {
    Apply-Json @{ apiVersion = 'v1'; kind = 'ConfigMap'; metadata = @{ name = 'ebaygroup5-config'; namespace = 'ebaygroup5' }
        data = @{ 'pod-cidr' = $podCidr; 'origin-localhost' = "https://localhost:$HttpsPort"; 'origin-loopback' = "https://127.0.0.1:$HttpsPort"
                  'public-https-port' = "$HttpsPort"; 'admin-address' = $adminAddress } }
}
Set-Config $admin

# 4. Workloads (image tag "local" -> commit tag).
$rendered = (kubectl kustomize --load-restrictor LoadRestrictionsNone (Join-Path $repoRoot 'deploy/k8s/base')) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'kustomize render failed.' }
$rendered = $rendered.Replace('ebaygroup5-web:local', "ebaygroup5-web:$tag")
Set-Content -LiteralPath (Join-Path $Work 'rendered.yaml') -Value $rendered
# Keep the existing app serving until migrations finish; first deploy infrastructure/services/policies only.
$documents = @($rendered -split '(?m)^---\s*$' | Where-Object { $_.Trim() })
($documents | Where-Object { $_ -notmatch '(?m)^kind: Deployment\s*$' }) -join "`n---`n" |
    kubectl --context $context apply -f - | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (infrastructure) failed.' }
Kube -n ebaygroup5 rollout status statefulset/sqlserver --timeout 600s
Kube -n ebaygroup5 rollout status statefulset/redis --timeout 300s

# 5. Migration Job (and optional helper Jobs) before web serves.
function Invoke-MigrateJob([string]$name, [string[]]$jobArgs) {
    $job = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot 'deploy/k8s/jobs/migrate.yaml')).
        Replace('ebaygroup5-migrate:local', "ebaygroup5-migrate:$tag").
        Replace('args: []', 'args: [' + (($jobArgs | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ']')
    $job = [regex]::Replace($job, '(?m)^  name: migrate\r?$', "  name: $name")
    kubectl --context $context -n ebaygroup5 delete job $name --ignore-not-found --wait=true | Out-Host
    $job | kubectl --context $context apply -f - | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Job $name could not be created." }
    kubectl --context $context -n ebaygroup5 wait --for=condition=complete "job/$name" --timeout=600s | Out-Host
    $ok = $LASTEXITCODE -eq 0
    kubectl --context $context -n ebaygroup5 logs "job/$name" | Out-Host
    if (-not $ok) { throw "Job $name did not complete." }
}
Invoke-MigrateJob 'migrate' @()
foreach ($helper in $HelperArgs) { Invoke-MigrateJob ('migrate-' + $helper.TrimStart('-')) @($helper) }
($documents | Where-Object { $_ -match '(?m)^kind: Deployment\s*$' }) -join "`n---`n" |
    kubectl --context $context apply -f - | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'kubectl apply (app deployments) failed.' }
Kube -n ebaygroup5 rollout status deployment/web --timeout 600s
Kube -n ebaygroup5 rollout status deployment/nginx --timeout 300s

# 6. Admin network: the address host traffic has inside the cluster (NodePort keeps the client IP). Measured, then pinned.
$deadline = [DateTime]::UtcNow.AddSeconds(120)
do {
    try { $null = Invoke-WebRequest -Uri "https://localhost:$HttpsPort/health/live" -SkipCertificateCheck -TimeoutSec 5 } catch { Start-Sleep -Seconds 2 }
    $seen = @(kubectl --context $context -n ebaygroup5 logs deployment/nginx --tail 50 2>$null | Select-String '^(\S+) \[.*"GET /health/live" 200' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }) | Select-Object -Last 1
} while (-not $seen -and [DateTime]::UtcNow -lt $deadline)
if (-not $seen) { throw 'Could not reach nginx through the host port.' }
if ($seen -ne $admin) {
    Set-Config $seen
    Kube -n ebaygroup5 rollout restart deployment/web
    Kube -n ebaygroup5 rollout status deployment/web --timeout 600s
}
Write-Output "TAG $tag"
Write-Output "ADMIN $seen"
