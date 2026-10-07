# F06 (docs/architecture/K8S.md): the user's local Kubernetes demo on a kind cluster named "ebaygroup5" (https://localhost:9443).
# Passwords are generated once into secrets/k8s/secrets.json (ignored) so the SQL/Redis volumes stay usable across runs; TLS reuses
# secrets/tls from New-LocalTlsCertificate.ps1. Running it again redeploys the current commit (migration Job first).
#   -Stop    stops the cluster node container (data kept; start again with this script)
#   -Delete  deletes the cluster and its volumes (all K8s demo data); asks for confirmation unless -Force
param([switch]$Stop, [switch]$Delete, [switch]$Force, [int]$HttpsPort = 9443, [int]$HttpPort = 9080)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$cluster = 'ebaygroup5'
$node = "$cluster-control-plane"
$kind = pwsh -NoProfile -File "$PSScriptRoot/Install-Kind.ps1"
if ($LASTEXITCODE -ne 0) { throw 'kind install failed.' }
$exists = @(& $kind get clusters) -contains $cluster

if ($Delete) {
    if (-not $exists) { Write-Output "No kind cluster '$cluster'."; return }
    if (-not $Force -and (Read-Host "Delete kind cluster '$cluster' and ALL its data? Type the cluster name to confirm") -ne $cluster) { Write-Output 'Cancelled.'; return }
    & $kind delete cluster --name $cluster
    return
}
if ($Stop) {
    if ($exists) { docker stop $node | Out-Null; Write-Output "Stopped $node (data kept)." } else { Write-Output "No kind cluster '$cluster'." }
    return
}
if ($exists -and (docker inspect --format '{{.State.Running}}' $node) -ne 'true') {
    docker start $node | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(180)
    while ((kubectl --context "kind-$cluster" get --raw /readyz 2>$null) -ne 'ok') {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The restarted cluster did not become ready.' }
        Start-Sleep -Seconds 3
    }
}

$secretsFile = Join-Path $repoRoot 'secrets/k8s/secrets.json'
if (-not (Test-Path -LiteralPath $secretsFile)) {
    New-Item -ItemType Directory -Force -Path (Split-Path $secretsFile) | Out-Null
    $new = { 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24)) }
    @{ 'sa-password' = & $new; 'app-password' = & $new; 'redis-password' = & $new } | ConvertTo-Json | Set-Content -LiteralPath $secretsFile
}
$secrets = @{}
(Get-Content -Raw -LiteralPath $secretsFile | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $secrets[$_.Name] = $_.Value }
$tls = Join-Path $repoRoot 'secrets/tls'
if (-not (Test-Path -LiteralPath (Join-Path $tls 'server.crt'))) {
    pwsh -NoProfile -File "$PSScriptRoot/New-LocalTlsCertificate.ps1" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the TLS certificate.' }
}
& "$PSScriptRoot/Deploy-K8s.ps1" -Cluster $cluster -HttpsPort $HttpsPort -HttpPort $HttpPort -Secrets $secrets -TlsDir $tls -Work (Join-Path $repoRoot 'artifacts/k8s/user')
Write-Output "Kubernetes demo: https://localhost:$HttpsPort (CA: secrets/tls/ca.crt). kubectl --context kind-$cluster -n ebaygroup5 get pods"
