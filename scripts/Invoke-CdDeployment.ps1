# F07: import exactly the CI-built images, retain existing state, deploy and run read-only smoke.
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSha,
    [string]$StateRoot = $env:EBAY_CD_STATE_ROOT,
    [string]$Cluster = 'ebaygroup5',
    [int]$HttpsPort = 9443,
    [int]$HttpPort = 9080,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if ((git -C $repoRoot rev-parse HEAD).Trim() -ne $ExpectedSha -or $LASTEXITCODE -ne 0) { throw 'Deployment checkout SHA mismatch.' }
$manifest = Get-Content -Raw -LiteralPath (Join-Path $ArtifactDirectory 'manifest.json') | ConvertFrom-Json
if ($manifest.version -ne 1 -or $manifest.revision -ne $ExpectedSha -or $manifest.archive -ne 'images.tar' -or
    $manifest.sha256 -notmatch '^[0-9a-f]{64}$') { throw 'Invalid CD artifact manifest/revision.' }
$archive = Join-Path $ArtifactDirectory 'images.tar'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'CD artifact checksum mismatch.' }
foreach ($target in 'web', 'migrate') {
    $image = $manifest.images.$target
    if ($image.name -ne "ebaygroup5-${target}:$ExpectedSha" -or $image.id -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Invalid CD artifact image identity.' }
}
if ($ValidateOnly) { Write-Output 'PASS: CD artifact manifest, revision, checksum and image identities.'; return }
if ([string]::IsNullOrWhiteSpace($StateRoot)) { throw 'EBAY_CD_STATE_ROOT must identify the existing local secrets directory.' }
$secretsFile = Join-Path $StateRoot 'k8s/secrets.json'
$tls = Join-Path $StateRoot 'tls'
foreach ($path in $secretsFile, (Join-Path $tls 'server.crt'), (Join-Path $tls 'server.key'), (Join-Path $tls 'ca.crt')) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Existing local state/TLS is missing; no secrets were replaced.' }
}
$secrets = @{}
(Get-Content -Raw -LiteralPath $secretsFile | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $secrets[$_.Name] = $_.Value }
foreach ($key in 'sa-password', 'app-password', 'redis-password') {
    if ([string]::IsNullOrWhiteSpace($secrets[$key])) { throw 'Existing cluster credentials are incomplete.' }
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$($secrets[$key])" }
}
docker image load --input $archive
if ($LASTEXITCODE -ne 0) { throw 'Cannot import CI images.' }
foreach ($target in 'web', 'migrate') {
    $expected = $manifest.images.$target
    $image = @(docker image inspect $expected.name | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0 -or $image.Id -ne $expected.id -or $image.Os -ne 'linux' -or $image.Architecture -ne 'amd64' -or
        $image.Config.Labels.'org.opencontainers.image.revision' -ne $ExpectedSha) { throw 'Imported image does not match the CI manifest/revision.' }
}
$work = Join-Path $repoRoot "artifacts/cd/$ExpectedSha"
& "$PSScriptRoot/Deploy-K8s.ps1" -Cluster $Cluster -HttpsPort $HttpsPort -HttpPort $HttpPort -Secrets $secrets -TlsDir $tls -Work $work -ImageTag $ExpectedSha
function Kube { $output = kubectl --context "kind-$Cluster" -n ebaygroup5 @args; if ($LASTEXITCODE -ne 0) { throw 'CD kubectl verification failed.' }; $output }
$web = Kube get deployment web -o json | ConvertFrom-Json
$migration = Kube get job migrate -o json | ConvertFrom-Json
if ($web.status.readyReplicas -ne 2 -or $web.spec.template.spec.containers[0].image -ne $manifest.images.web.name -or
    $migration.status.succeeded -ne 1 -or $migration.spec.template.spec.containers[0].image -ne $manifest.images.migrate.name) {
    throw 'Deployment replicas, image SHA or migration completion mismatch.'
}
node "$PSScriptRoot/cd-smoke.mjs" (Join-Path $tls 'ca.crt') $HttpsPort $HttpPort
if ($LASTEXITCODE -ne 0) { throw 'Read-only CD smoke failed.' }
@{ revision = $ExpectedSha; cluster = $Cluster; webImage = $manifest.images.web.name; migrateImage = $manifest.images.migrate.name
   readyReplicas = $web.status.readyReplicas; migrationSucceeded = $migration.status.succeeded; smoke = 'PASS'; utc = [DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'deployment-report.json') -Encoding utf8
Write-Output "PASS: CD deployed $ExpectedSha, migration Complete, 2 ready web replicas, verified HTTPS."
