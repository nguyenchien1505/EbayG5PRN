# F07: tampering/revision/name rejection must happen before Docker/cluster/state is accessed.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$sha = (git -C $repoRoot rev-parse HEAD).Trim()
$work = Join-Path $repoRoot ('artifacts/cd-artifact-test/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work 'images.tar'
[IO.File]::WriteAllBytes($archive, [Text.Encoding]::UTF8.GetBytes('non-secret fixture; validation does not import Docker archives'))
$manifest = @{ version = 1; revision = $sha; archive = 'images.tar'
    sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    images = @{ web = @{ name = "ebaygroup5-web:$sha"; id = 'sha256:' + ('a' * 64) }
                migrate = @{ name = "ebaygroup5-migrate:$sha"; id = 'sha256:' + ('b' * 64) } } }
function Write-Manifest { $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'manifest.json') }
function Reject([string]$expected) {
    Write-Manifest
    try {
        & "$repoRoot/scripts/Invoke-CdDeployment.ps1" -ArtifactDirectory $work -ExpectedSha $sha -StateRoot (Join-Path $work 'missing-state')
    } catch {
        if ($_.Exception.Message -like "*$expected*") { Write-Output "PASS: $expected rejected before state/Docker."; return }
        throw
    }
    throw "Artifact was accepted unexpectedly: $expected"
}
Write-Manifest
& "$repoRoot/scripts/Invoke-CdDeployment.ps1" -ArtifactDirectory $work -ExpectedSha $sha -ValidateOnly
$manifest.revision = '0' * 40; Reject 'manifest/revision'; $manifest.revision = $sha
$manifest.archive = '../images.tar'; Reject 'manifest/revision'; $manifest.archive = 'images.tar'
$manifest.sha256 = '0' * 64; Reject 'checksum mismatch'
$manifest.sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest.images.web.name = "unrelated-web:$sha"; Reject 'image identity'; $manifest.images.web.name = "ebaygroup5-web:$sha"
$manifest.images.migrate.id = 'invalid'; Reject 'image identity'
# Bounded native PowerShell cleanup of this exact generated fixture directory.
$parent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/cd-artifact-test'))
$resolved = [IO.Path]::GetFullPath($work)
if (-not $resolved.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup path escaped its parent.' }
Remove-Item -LiteralPath $resolved -Recurse -Force
Write-Output 'PASS: CD artifact validation and five rejection cases.'
