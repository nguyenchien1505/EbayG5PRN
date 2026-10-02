# Windows x64, per-user SDK installation. No admin or persistent PATH change.
$ErrorActionPreference = 'Stop'
$version = '8.0.425'
$expectedHash = 'f0b6f15bf6f1a0507205c0cb102ab99e1dee875c4682c8ed94665be1d580186a06b21455e83b3a01a0ff7f4cd887b67420f2e2fe09ed985534a4cea488ae1af9'
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'This installer targets Windows x64. Install SDK 8.0.425 from Microsoft for your platform.'
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
