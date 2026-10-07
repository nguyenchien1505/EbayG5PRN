# F06 (docs/architecture/K8S.md): official kind release binary for this OS, verified against the SHA-256 pinned below, stored in the
# ignored artifacts/tools/ folder. Reuses a verified copy. Prints the path of the kind executable.
# Checksums: kind-<os>-amd64.sha256sum from github.com/kubernetes-sigs/kind/releases/tag/v0.33.0 (read 07/10/2026).
$ErrorActionPreference = 'Stop'
$version = 'v0.33.0'
$assets = @{
    Windows = @{ Name = 'kind-windows-amd64'; Sha256 = '4b22adaa135368c5a465d56bbd8e520cbea87272a06ca00b6078e7b81515c9fc'; File = 'kind.exe' }
    Linux   = @{ Name = 'kind-linux-amd64'; Sha256 = 'aee6151561422756b764a4ae28e7f44cda5af5a9eead3cc9985112b1de8d8e0d'; File = 'kind' }
}
$asset = if ($IsWindows) { $assets.Windows } elseif ($IsLinux) { $assets.Linux } else { throw 'kind is pinned for Windows and Linux (amd64) only.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $repoRoot "artifacts/tools/kind-$version"
$path = Join-Path $folder $asset.File
if ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -eq $asset.Sha256) {
    Write-Output $path
    return
}
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$download = "$path.download"
Invoke-WebRequest -Uri "https://github.com/kubernetes-sigs/kind/releases/download/$version/$($asset.Name)" -OutFile $download -TimeoutSec 300 -UseBasicParsing
$actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $asset.Sha256) {
    Remove-Item -LiteralPath $download -Force
    throw "kind checksum mismatch (got $actual); the file was deleted."
}
Move-Item -LiteralPath $download -Destination $path -Force
if (-not $IsWindows) { chmod +x $path }
Write-Output $path
