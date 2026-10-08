# G02: actual GitHub clone with no copied state/reference checkout. Generated test stacks own their data.
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedSha,
    [string]$Ref = 'main'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$hex = [Guid]::NewGuid().ToString('N')
$work = Join-Path $repoRoot "artifacts/handover/clean-$hex"
$clone = Join-Path $work 'repo'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$remote = 'https://github.com/Twiash/EbayGroup5.git'
git clone --no-checkout --single-branch --branch $Ref $remote $clone
if ($LASTEXITCODE -ne 0) { throw 'Private GitHub clone failed; authenticate without putting a token in the URL.' }
git -C $clone checkout --detach $ExpectedSha
if ($LASTEXITCODE -ne 0 -or (git -C $clone rev-parse HEAD).Trim() -ne $ExpectedSha) { throw 'Clean clone revision mismatch.' }
foreach ($path in '.env','.env.database','.env.auth','secrets','artifacts','src/EbayGroup5.Web/Properties/launchSettings.json') {
    if (Test-Path -LiteralPath (Join-Path $clone $path)) { throw "Unexpected local state in fresh clone: $path" }
}
$success = $false
Push-Location $clone
try {
    if ($IsWindows) { . ./scripts/Use-LocalDotnet.ps1 }
    foreach ($script in 'Verify-Planning.ps1','Test-CI.ps1') {
        pwsh -NoProfile -File "scripts/$script" *> (Join-Path $work "$script.log")
        if ($LASTEXITCODE -ne 0) { throw "Clean clone $script failed; see $work." }
        Write-Output "PASS clean GitHub clone: $script"
    }
    pwsh -NoProfile -File scripts/Test-AppStack.ps1 -DemoClockSeconds 10 *> (Join-Path $work 'demo.log')
    if ($LASTEXITCODE -ne 0) { throw "Clean clone demo/TLS/WSS failed; see $work/demo.log." }
    if (git status --porcelain --untracked-files=no) { throw 'Verification changed the tracked source in the clean clone.' }
    $success = $true
    Write-Output 'PASS: clean GitHub clone, locked restore/Release build/test, new-order demo and HTTPS/WSS smoke.'
} finally {
    @{ revision=$ExpectedSha; source=$remote; clone=$clone; utc=[DateTime]::UtcNow.ToString('o'); passed=$success
       copiedSecrets=$false; needsReferenceProjects=$false } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'report.json')
    Pop-Location
}
Write-Output "Handover report: $work/report.json"
