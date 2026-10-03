$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    . "$PSScriptRoot/Use-LocalDotnet.ps1"
    . "$PSScriptRoot/Use-LocalInfrastructure.ps1" -Migration
    dotnet run --project src/EbayGroup5.Database -c Release --no-launch-profile -- --seed-demo
    if ($LASTEXITCODE -ne 0) { throw 'Demo seed failed; check initialization/email collisions and runbook. No data was reset.' }
} finally {
    $env:ConnectionStrings__MigrationConnection = $null
    Pop-Location
}
