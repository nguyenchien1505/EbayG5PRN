# F07: build on the CI host, export images tagged by the exact source commit. No secrets in this artifact.
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSha,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$actual = (git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actual -ne $ExpectedSha) { throw 'Checkout SHA does not match the requested build.' }
if (git -C $repoRoot status --porcelain --untracked-files=no) { throw 'CD images require a clean tracked source tree.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$images = @{}
foreach ($target in 'web', 'migrate') {
    $name = "ebaygroup5-${target}:$ExpectedSha"
    docker build --target $target --label "org.opencontainers.image.revision=$ExpectedSha" -t $name $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "CD build $target failed." }
    $image = @(docker image inspect $name | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0 -or $image.Os -ne 'linux' -or $image.Architecture -ne 'amd64') { throw 'Expected Linux amd64 images.' }
    $images[$target] = @{ name = $name; id = $image.Id }
}
$archive = Join-Path $OutputDirectory 'images.tar'
docker image save --output $archive $images.web.name $images.migrate.name
if ($LASTEXITCODE -ne 0) { throw 'Cannot export CD images.' }
@{ version = 1; revision = $ExpectedSha; archive = 'images.tar'
   sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); images = $images } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json') -Encoding utf8
Write-Output "PASS: CD artifact for $ExpectedSha (web + migrate, SHA-256 manifest)."
