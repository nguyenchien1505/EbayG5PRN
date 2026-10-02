$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    . "$PSScriptRoot/Use-LocalDotnet.ps1"
    . "$PSScriptRoot/Use-LocalInfrastructure.ps1" -Migration
    # Tests replace the catalog with unique test names; never mutate EbayGroup5.
    $env:EBAY_TEST_SQL = $env:ConnectionStrings__MigrationConnection
    dotnet test tests/EbayGroup5.Database.Tests -c Release --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Database tests failed; isolated test databases retained for diagnosis.' }
} finally {
    $env:EBAY_TEST_SQL = $null
    $env:ConnectionStrings__MigrationConnection = $null
    Pop-Location
}
