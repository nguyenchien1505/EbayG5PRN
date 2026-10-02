# PowerShell 7. No overwrite: an existing SQL volume retains its original SA password.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$envPath = Join-Path $repoRoot '.env'
if (Test-Path -LiteralPath $envPath) {
    Write-Output '.env already exists; preserved without changes.'
    exit 0
}
function New-LocalPassword {
    # Hex avoids Compose interpolation and connection string/config delimiters.
    'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
}
$lines = @(
    'SQLSERVER_PORT=14335'
    'REDIS_PORT=16379'
    "MSSQL_SA_PASSWORD=$(New-LocalPassword)"
    "REDIS_PASSWORD=$(New-LocalPassword)"
)
# CreateNew also prevents overwriting if another setup process wins the race.
$stream = [IO.File]::Open($envPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
    $stream.Write($bytes)
} finally { $stream.Dispose() }
Write-Output '.env created with random local credentials. No credentials printed.'
