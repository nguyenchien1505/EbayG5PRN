# Windows x64, per-user SDK installation. No admin or persistent PATH change.
$ErrorActionPreference = 'Stop'
$version = '8.0.427'
$expectedHash = 'a4dd6eb2bf2a2f6fb10997d3e044a229b23ad96a5c86d20494687e34ee73b537d7bd14488ce14cded6917f6778da4d66360e2734020b7ef487d0f29ab144489d'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'This installer targets Windows x64. Install SDK 8.0.427 from Microsoft for your platform.'
}
$sdkRoot = Join-Path $env:LOCALAPPDATA 'EbayGroup5/dotnet'
if (Test-Path -LiteralPath (Join-Path $sdkRoot "sdk/$version")) {
    Write-Output "SDK $version already present at $sdkRoot"
    exit 0
}
$archive = Join-Path ([IO.Path]::GetTempPath()) "ebaygroup5-dotnet-sdk-$version-win-x64.zip"
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expectedHash) {
    Invoke-WebRequest "https://builds.dotnet.microsoft.com/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expectedHash) {
    throw 'SDK archive SHA512 mismatch; installation stopped.'
}
New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $sdkRoot -Force
& (Join-Path $sdkRoot 'dotnet.exe') --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'SDK verification failed.' }
