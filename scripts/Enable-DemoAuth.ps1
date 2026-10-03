$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    . "$PSScriptRoot/Use-LocalDotnet.ps1"
    . "$PSScriptRoot/Use-LocalInfrastructure.ps1" -Migration
    $path = Join-Path $repoRoot '.env.auth'
    if (-not (Test-Path -LiteralPath $path)) {
        $lines = foreach ($role in @('Buyer','Seller','Admin')) {
            'Database__DemoPassword' + $role + '=Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
        }
        $stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $stream.Write([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")+"`n")) } finally { $stream.Dispose() }
    }
    $entries = Get-Content -LiteralPath $path
    if ($entries.Count -ne 3) { throw 'Invalid .env.auth format.' }
    $seen = @{}
    foreach ($entry in $entries) {
        if ($entry -cnotmatch '^Database__DemoPassword(Buyer|Seller|Admin)=(.{12,128})$') { throw 'Invalid .env.auth entry.' }
        if ($seen.ContainsKey($Matches[1])) { throw 'Duplicate role in .env.auth.' }
        $seen[$Matches[1]]=$true
        [Environment]::SetEnvironmentVariable('Database__DemoPassword'+$Matches[1],$Matches[2],'Process')
    }
    dotnet run --project src/EbayGroup5.Database -c Release --no-launch-profile -- --enable-demo-auth
    if ($LASTEXITCODE -ne 0) { throw 'Demo auth provisioning failed; credentials preserved. Check seed and identity changes.' }
    Write-Output 'Private demo credentials are in .env.auth; never commit or share this file.'
} finally {
    foreach ($role in @('Buyer','Seller','Admin')) { [Environment]::SetEnvironmentVariable('Database__DemoPassword'+$role,$null,'Process') }
    $env:ConnectionStrings__MigrationConnection=$null
    Pop-Location
}
