# Dot-source: . ./scripts/Use-LocalDotnet.ps1
# Sets PATH for this PowerShell session only; leaves the machine SDK intact.
$sdkRoot = Join-Path $env:LOCALAPPDATA 'EbayGroup5/dotnet'
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'dotnet.exe'))) {
    throw 'Local SDK missing. Run scripts/Install-Dotnet8.ps1 or use a system-installed compatible .NET 8 SDK.'
}
$env:DOTNET_ROOT = $sdkRoot
if (($env:PATH -split [IO.Path]::PathSeparator) -notcontains $sdkRoot) {
    $env:PATH = $sdkRoot + [IO.Path]::PathSeparator + $env:PATH
}
