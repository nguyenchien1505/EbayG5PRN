# F05 (docs/architecture/LOAD.md): official Apache JMeter binary, verified against the SHA-512 pinned below, unpacked into the
# ignored artifacts/tools/ folder. Reuses an existing verified copy. Prints the path of jmeter's bin folder.
# Checksum: apache-jmeter-5.6.3.zip.sha512 published by Apache (dlcdn.apache.org, read 07/10/2026).
$ErrorActionPreference = 'Stop'
$version = '5.6.3'
$sha512 = '387fadca903ee0aa30e3f2115fdfedb3898b102e6b9fe7cc3942703094bd2e65b235df2b0c6d0d3248e74c9a7950a36e42625fd74425368342c12e40b0163076'
$repoRoot = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $repoRoot 'artifacts/tools'
$home_ = Join-Path $tools "apache-jmeter-$version"
$marker = Join-Path $home_ '.verified-sha512'
# Expand-Archive drops the execute bit on Linux/macOS (also for a copy restored from the CI cache).
function Set-Executable { if (-not $IsWindows) { chmod +x (Join-Path $home_ 'bin/jmeter') } }
if ((Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw).Trim() -eq $sha512) {
    Set-Executable
    Write-Output (Join-Path $home_ 'bin')
    return
}
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$zip = Join-Path $tools "apache-jmeter-$version.zip"
$sources = @("https://dlcdn.apache.org/jmeter/binaries/apache-jmeter-$version.zip", "https://archive.apache.org/dist/jmeter/binaries/apache-jmeter-$version.zip")
$downloaded = $false
foreach ($source in $sources) {
    try {
        Invoke-WebRequest -Uri $source -OutFile $zip -TimeoutSec 600 -UseBasicParsing
        $downloaded = $true
        break
    } catch { Write-Warning "Download failed from $source ($($_.Exception.GetType().Name)); trying the next source." }
}
if (-not $downloaded) { throw 'Cannot download Apache JMeter from any official source.' }
$actual = (Get-FileHash -LiteralPath $zip -Algorithm SHA512).Hash.ToLowerInvariant()
if ($actual -ne $sha512) {
    Remove-Item -LiteralPath $zip -Force
    throw "JMeter archive checksum mismatch (got $actual); the file was deleted."
}
if (Test-Path -LiteralPath $home_) { Remove-Item -Recurse -Force -LiteralPath $home_ }
Expand-Archive -LiteralPath $zip -DestinationPath $tools
Remove-Item -LiteralPath $zip -Force
Set-Content -LiteralPath $marker -Value $sha512 -NoNewline
Set-Executable
Write-Output (Join-Path $home_ 'bin')
