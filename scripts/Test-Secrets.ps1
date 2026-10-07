# F04 (docs/architecture/SECURITY_TESTS.md): secret scan without external tools. Checks tracked paths, the content of tracked files
# and every line ever added in the git history (all refs). Findings print file/commit and a masked value; exit 1 on any finding.
# CI checks out the full history (fetch-depth: 0) so the history pass covers every commit.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    $patterns = [ordered]@{
        'private key block'         = '-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY-----'
        'AWS access key id'         = '\b(?:AKIA|ASIA)[0-9A-Z]{16}\b'
        'GitHub token'              = '\bgh[pousr]_[A-Za-z0-9]{36,}\b|\bgithub_pat_[A-Za-z0-9_]{40,}'
        'Slack token'               = '\bxox[abposr]-[A-Za-z0-9-]{10,}'
        'Google API key'            = '\bAIza[0-9A-Za-z_-]{35}\b'
        'Atlassian API token'       = '\bATATT3[A-Za-z0-9_=-]{20,}'
        'JSON web token'            = '\beyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        # Literal passwords inside connection strings (no spaces around '=', unlike C# assignments); ${VAR}, $env:VAR and
        # <placeholder> are references. Env-file style NAME_PASSWORD=value lines; the .env.example marker is a placeholder.
        'connection string password' = '(?i)(?:^|[;"''\s])(?:password|pwd)=(?!\$|\{|<)[^;"''\s,}]{6,}'
        'env/yaml secret assignment' = '^\s*[A-Z0-9_]*(?:PASSWORD|SECRET|TOKEN|API_KEY)\s*[=:]\s*(?!\$|<|GENERATE_WITH_SETUP_SCRIPT\s*$)\S{6,}'
    }
    $findings = [System.Collections.Generic.List[string]]::new()
    function Test-Line([string]$where, [string]$line) {
        foreach ($name in $patterns.Keys) {
            $match = [regex]::Match($line, $patterns[$name])
            if ($match.Success) {
                $value = $match.Value.Trim()
                $findings.Add("$where — $name ($($value.Substring(0, [Math]::Min(6, $value.Length)))***)")
            }
        }
    }

    # 1. Paths that must never be tracked (now or in any commit): env files, key material, local artifacts and secrets.
    $forbidden = '(^|/)\.env($|\.(?!example$))|\.(pfx|p12|key|pem|jks|keystore)$|^(artifacts|secrets)/'
    $everTracked = @(git log --all --name-only --format= | Where-Object { $_ } | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'git log failed.' }
    foreach ($path in @(git ls-files) + $everTracked | Sort-Object -Unique) {
        if ($path -match $forbidden) { $findings.Add("$path — forbidden path (tracked now or in history)") }
    }

    # 2. Current tracked text files.
    $files = @(git ls-files)
    foreach ($path in $files) {
        # FileInfo, not Get-Item: on Linux Get-Item skips dotfiles (.dockerignore) unless -Force.
        $item = [IO.FileInfo]::new((Join-Path $repoRoot $path))
        if (-not $item.Exists) { continue }
        if ($item.Length -gt 2MB) { continue }
        $bytes = [IO.File]::ReadAllBytes($item.FullName)
        if ([Array]::IndexOf($bytes, [byte]0) -ge 0) { continue } # binary
        $number = 0
        foreach ($line in [Text.Encoding]::UTF8.GetString($bytes) -split "`n") { $number++; Test-Line "${path}:$number" $line }
    }

    # 3. Every line added in history (deleted secrets stay in clones until the history is rewritten).
    $commit = ''; $file = ''
    git log --all -p --no-color --no-ext-diff --format='commit %H' | ForEach-Object {
        if ($_ -match '^commit ([0-9a-f]{40})$') { $commit = $Matches[1].Substring(0, 10) }
        elseif ($_ -match '^\+\+\+ b/(.*)$') { $file = $Matches[1] }
        elseif ($_.StartsWith('+') -and -not $_.StartsWith('+++')) { Test-Line "history $commit $file" $_.Substring(1) }
    }
    if ($LASTEXITCODE -ne 0) { throw 'git log -p failed.' }
    $commits = @(git rev-list --all).Count

    if ($findings.Count -gt 0) {
        $findings | Sort-Object -Unique | ForEach-Object { Write-Output "FAIL $_" }
        throw "Secret scan found $($findings.Count) issue(s)."
    }
    Write-Output "PASS secret scan: $($files.Count) tracked files, $commits commits, $($patterns.Count) patterns, no forbidden paths."
} finally {
    Pop-Location
}
