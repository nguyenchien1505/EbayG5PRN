$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$previousTestRedis = $env:EBAY_TEST_REDIS
Push-Location $repoRoot
try {
    . "$PSScriptRoot/Use-LocalDotnet.ps1"
    . "$PSScriptRoot/Use-LocalInfrastructure.ps1" -Migration
    # Tests replace the catalog with unique test names; never mutate EbayGroup5.
    $env:EBAY_TEST_SQL = $env:ConnectionStrings__MigrationConnection
    $env:EBAY_TEST_REDIS = $env:ConnectionStrings__Redis
    dotnet test tests/EbayGroup5.Database.Tests -c Release --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Database tests failed; isolated test databases retained for diagnosis.' }
} finally {
    $env:EBAY_TEST_SQL = $null
    $env:EBAY_TEST_REDIS = $previousTestRedis
    $env:ConnectionStrings__MigrationConnection = $null
    Pop-Location
}
