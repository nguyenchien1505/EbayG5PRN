$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content (Join-Path $PSScriptRoot 'migration-checksums.json') -Raw | ConvertFrom-Json -AsHashtable
$paths = @('docs/requirements/sources/clone_ebay_sqlserver_schema.sql')
$paths += Get-ChildItem (Join-Path $repoRoot 'src/EbayGroup5.Database/Scripts') -Filter '*.sql' | ForEach-Object { 'src/EbayGroup5.Database/Scripts/' + $_.Name }
$paths += Get-ChildItem (Join-Path $repoRoot 'src/EbayGroup5.Database/Seed') -Filter '*.sql' | ForEach-Object { 'src/EbayGroup5.Database/Seed/' + $_.Name }
if (@(Compare-Object @($manifest.Keys) $paths).Count -ne 0) { throw 'Migration/seed file set differs from manifest; append new versions explicitly.' }
foreach ($path in $paths) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $repoRoot $path) -Algorithm SHA256).Hash
    if ($hash -cne $manifest[$path]) { throw "Immutable SQL checksum mismatch: $path" }
}
Write-Output "PASS: $($paths.Count) immutable SQL assets match manifest."
