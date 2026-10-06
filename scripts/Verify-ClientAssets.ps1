$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'client-assets.json') -Raw | ConvertFrom-Json
foreach ($asset in $manifest.files) {
    $path = Join-Path $repoRoot $asset.path
    if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.sha256) {
        throw 'Pinned client asset checksum mismatch.'
    }
}
Write-Output "PASS: $($manifest.package) $($manifest.version) local browser assets/license match checksums."
