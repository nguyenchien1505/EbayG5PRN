$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    . "$PSScriptRoot/Use-LocalDotnet.ps1"
    . "$PSScriptRoot/Use-LocalInfrastructure.ps1" -Migration
    $appEnvPath = Join-Path $repoRoot '.env.database'
    if (-not (Test-Path -LiteralPath $appEnvPath)) {
        $password = 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        $stream = [IO.File]::Open($appEnvPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $stream.Write([Text.Encoding]::UTF8.GetBytes("SQL_APP_PASSWORD=$password`n")) }
        finally { $stream.Dispose() }
    }
    $entry = [IO.File]::ReadAllText($appEnvPath).Trim()
    if ($entry -cnotmatch '^SQL_APP_PASSWORD=([A-Za-z0-9!_-]{24,128})$') { throw 'Invalid .env.database format.' }
    $env:Database__AppPassword = $Matches[1]
    dotnet run --project src/EbayGroup5.Database -c Release --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Database setup failed. Credentials/volumes were preserved.' }
} finally {
    $env:Database__AppPassword = $null
    $env:ConnectionStrings__MigrationConnection = $null
    Pop-Location
}
