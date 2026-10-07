# F07: one ephemeral Windows runner, one explicitly dispatched owner/main CD, no service.
param([string]$StateRoot)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This bootstrap is pinned to Windows x64.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
if (-not $StateRoot) { $StateRoot = Join-Path $repoRoot 'secrets' }
$StateRoot = [IO.Path]::GetFullPath($StateRoot)
if (-not (Test-Path -LiteralPath (Join-Path $StateRoot 'k8s/secrets.json'))) { throw 'Run Start-K8s first to initialize persistent local state.' }
foreach ($tool in 'git', 'gh', 'docker', 'kubectl', 'node', 'pwsh') { $null = Get-Command $tool -ErrorAction Stop }
docker info --format '{{.OSType}}' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker Linux must be running.' }
$repository = 'Twiash/EbayGroup5'
$api = "repos/$repository"
function Api([string]$path, [string]$method = 'GET') {
    $json = gh api --method $method $path
    if ($LASTEXITCODE -ne 0) { throw "GitHub API $method failed (no credentials printed)." }
    if ($json) { $json | ConvertFrom-Json }
}
$main = (Api "$api/git/ref/heads/main").object.sha
$ci = (Api "$api/actions/workflows/ci.yml/runs?head_sha=$main&status=completed&per_page=100").workflow_runs
if (-not @($ci | Where-Object { $_.head_sha -eq $main -and $_.conclusion -eq 'success' }).Count) { throw 'main needs successful CI on its exact SHA.' }
$workflow = Api "$api/actions/workflows/cd.yml"
if ($workflow.state -ne 'active') { throw 'CD workflow must be active on main.' }
$hex = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$label = "ebaygroup5-cd-$hex"
$runnerParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/cd-runners'))
$runnerDir = [IO.Path]::GetFullPath((Join-Path $runnerParent $hex))
if (-not $runnerDir.StartsWith($runnerParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runner path escaped its intended directory.' }
$version = '2.337.0'
$checksum = '1150692afa94e71f872017e254ea55b6eece1eece3fe7e3a6d4c93d0a1b85cfc'
$zip = Join-Path $repoRoot "artifacts/tools/actions-runner-win-x64-$version.zip"
New-Item -ItemType Directory -Force -Path (Split-Path $zip), $runnerDir | Out-Null
if (-not (Test-Path -LiteralPath $zip) -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum) {
    Invoke-WebRequest -Uri "https://github.com/actions/runner/releases/download/v$version/actions-runner-win-x64-$version.zip" -OutFile "$zip.download" -TimeoutSec 300
    if ((Get-FileHash -LiteralPath "$zip.download" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $checksum) { throw 'Runner archive checksum mismatch.' }
    Move-Item -LiteralPath "$zip.download" -Destination $zip -Force
}
Expand-Archive -LiteralPath $zip -DestinationPath $runnerDir
$listener = Join-Path $runnerDir 'bin/Runner.Listener.exe'
$oldState = $env:EBAY_CD_STATE_ROOT
$oldInputToken = $env:ACTIONS_RUNNER_INPUT_TOKEN
$oldPath = $env:PATH
$process = $null
$runnerId = $null
$runId = $null
$conclusion = $null
$deregistered = $false
try {
    # Pass the short-lived registration token only through the runner's supported environment input.
    $env:ACTIONS_RUNNER_INPUT_TOKEN = (Api "$api/actions/runners/registration-token" 'POST').token
    & $listener configure --url "https://github.com/$repository" --name $label --work _work --unattended --ephemeral --no-default-labels --labels $label
    if ($LASTEXITCODE -ne 0) { throw 'Ephemeral runner registration failed.' }
    $env:ACTIONS_RUNNER_INPUT_TOKEN = $oldInputToken
    $registered = @((Api "$api/actions/runners").runners | Where-Object { $_.name -eq $label })
    if ($registered.Count -ne 1) { throw 'Could not identify this exact ephemeral runner.' }
    $runnerId = $registered[0].id
    $env:EBAY_CD_STATE_ROOT = $StateRoot
    # Jobs inherit the process PATH, including portable PowerShell on this machine.
    $toolDirs = @('pwsh', 'docker', 'kubectl', 'node', 'git' | ForEach-Object { Split-Path (Get-Command $_).Source }) | Sort-Object -Unique
    $env:PATH = ($toolDirs -join [IO.Path]::PathSeparator) + [IO.Path]::PathSeparator + $oldPath
    $process = Start-Process -FilePath $listener -ArgumentList 'run' -WorkingDirectory $runnerDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $runnerDir 'stdout.log') -RedirectStandardError (Join-Path $runnerDir 'stderr.log')
    gh workflow run cd.yml --repo $repository --ref main -f "runner_label=$label"
    if ($LASTEXITCODE -ne 0) { throw 'Could not dispatch the CD workflow.' }
    Write-Output "Ephemeral runner $label (ID $runnerId), requested CD on main $main."
    $deadline = [DateTime]::UtcNow.AddMinutes(65)
    $lastStatus = ''
    do {
        $runs = @((Api "$api/actions/workflows/cd.yml/runs?event=workflow_dispatch&branch=main&per_page=30").workflow_runs |
            Where-Object { $_.display_title -eq $label })
        if ($runs.Count -gt 0) {
            $run = $runs[0]; $runId = $run.id
            if ($run.head_sha -ne $main) { throw 'main changed before CD dispatch; refusing an unexpected commit.' }
            if ($run.status -ne $lastStatus) { Write-Output "CD $runId $($run.status): $($run.html_url)"; $lastStatus = $run.status }
            if ($run.status -eq 'completed') { $conclusion = $run.conclusion; break }
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'CD timed out; this run will be cancelled and runner removed.' }
        Start-Sleep -Seconds 10
    } while ($true)
    if ($conclusion -ne 'success') { throw "CD run $runId ended with $conclusion. See its GitHub logs." }
    Write-Output "PASS: CD $runId success."
} finally {
    $env:ACTIONS_RUNNER_INPUT_TOKEN = $oldInputToken
    $env:EBAY_CD_STATE_ROOT = $oldState
    $env:PATH = $oldPath
    if ($runId -and -not $conclusion) {
        try { $null = Api "$api/actions/runs/$runId/cancel" 'POST' } catch { Write-Warning 'Could not cancel this CD run; inspect it on GitHub.' }
    }
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            $null = $process.WaitForExit(10000)
            $process.Refresh()
            if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        }
    }
    # The service deregisters ephemeral runners after one job; remove only this ID if configuration/dispatch/job failed.
    $remaining = @((Api "$api/actions/runners").runners | Where-Object { $_.name -eq $label })
    foreach ($item in $remaining) { $null = Api "$api/actions/runners/$($item.id)" 'DELETE' }
    $deregistered = -not @((Api "$api/actions/runners").runners | Where-Object { $_.name -eq $label }).Count
    foreach ($name in '.credentials', '.credentials_rsaparams', '.runner', '.service') {
        $path = Join-Path $runnerDir $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    $reportDir = Join-Path $repoRoot 'artifacts/cd'
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    @{ runner = $label; runnerId = $runnerId; runId = $runId; revision = $main; conclusion = $conclusion
       deregistered = $deregistered; serviceInstalled = $false; utc = [DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportDir "runner-$hex.json") -Encoding utf8
    Write-Output "Runner $label deregistered: $deregistered (no service installed)."
}
if (-not $deregistered) { throw 'Runner deregistration could not be verified.' }
