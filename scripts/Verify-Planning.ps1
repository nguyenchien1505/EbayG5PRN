# Read-only validation of the documentation checkpoint; not application tests.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = [System.Collections.Generic.List[string]]::new()
function Assert-Planning([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $failures.Add($Message) }
}

$sqlPath = Join-Path $root 'docs/requirements/sources/clone_ebay_sqlserver_schema.sql'
$expectedHash = 'D56EC9C0FFCEA721E2F7BA3453249794F03AFEB1F17CC4A2F9EBD0ADA38E0754'
Assert-Planning ((Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash -eq $expectedHash) 'SQL source checksum changed.'
$sql = Get-Content -LiteralPath $sqlPath -Raw -Encoding utf8
$tables = @([regex]::Matches($sql, '(?im)^CREATE TABLE \[([^\]]+)\]') | ForEach-Object { $_.Groups[1].Value })
$expectedTables = @('User','Address','Category','Product','OrderTable','OrderItem','Payment','ShippingInfo','ReturnRequest','Bid','Review','Message','Coupon','Inventory','Feedback','Dispute','Store')
Assert-Planning ($tables.Count -eq 17 -and @(Compare-Object $expectedTables $tables).Count -eq 0) 'Expected 17 original tables.'

$plan = Get-Content -LiteralPath (Join-Path $root 'docs/PLAN.md') -Raw -Encoding utf8
$planIds = @([regex]::Matches($plan, '(?m)^\| ([A-G]\d{2}) \|') | ForEach-Object { $_.Groups[1].Value })
$index = Get-Content -LiteralPath (Join-Path $root 'docs/tasks/INDEX.md') -Raw -Encoding utf8
$indexRows = [regex]::Matches($index, '(?m)^\| \[([A-G]\d{2})\]\([^)]*\) \|[^\r\n]+\| (Planned|Ready|InProgress|Blocked|Done) \|')
$indexIds = @($indexRows | ForEach-Object { $_.Groups[1].Value })
Assert-Planning ($planIds.Count -eq 39 -and @($planIds | Select-Object -Unique).Count -eq 39) 'Plan must contain 39 unique tasks.'
Assert-Planning ($indexIds.Count -eq 39 -and @(Compare-Object $planIds $indexIds).Count -eq 0) 'Task index does not match plan.'
foreach ($row in $indexRows) {
    $id = $row.Groups[1].Value
    $status = $row.Groups[2].Value
    $path = Join-Path $root "docs/tasks/$id.md"
    Assert-Planning (Test-Path -LiteralPath $path) "Missing task card: $id"
    if (Test-Path -LiteralPath $path) {
        $card = Get-Content -LiteralPath $path -Raw -Encoding utf8
        Assert-Planning ($card -match "(?m)^Status: $status\s*$") "Status mismatch: $id"
        Assert-Planning ($card -match '(?m)^Dependency:' -and $card -match '(?m)^Requirement IDs:') "Missing task inputs: $id"
    }
}

$matrix = Get-Content -LiteralPath (Join-Path $root 'docs/requirements/MATRIX.md') -Raw -Encoding utf8
foreach ($family in @(@('G5-',8), @('G5-N',5), @('OPS-',6), @('SUP-',5))) {
    for ($i=1; $i -le $family[1]; $i++) {
        $id = $family[0] + $i.ToString('00')
        Assert-Planning ($matrix.Contains("| $id |")) "Missing requirement: $id"
    }
}

# Validate repository documents, excluding ignored tool downloads/build artifacts.
$markdownPaths = @(git -C $root ls-files --cached --others --exclude-standard -- '*.md')
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate repository documentation.' }
$markdownFiles = @($markdownPaths | ForEach-Object { Get-Item -LiteralPath (Join-Path $root $_) })
foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
    Assert-Planning (-not $content.Contains([char]0xFFFD)) "Invalid Unicode replacement in $($file.Name)"
    foreach ($link in [regex]::Matches($content, '\]\(([^)]+)\)')) {
        $target = $link.Groups[1].Value
        if ($target -match '^(https?://|mailto:|#)') { continue }
        $target = ($target -split '#',2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $target))
        Assert-Planning (Test-Path -LiteralPath $resolved) "Broken link in $($file.Name): $target"
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAIL: $_" }
    exit 1
}
Write-Output "PASS: SQL checksum and 17 tables; 39 task cards/index statuses; required requirement IDs; $($markdownFiles.Count) Markdown files and local link targets."
Write-Output 'Scope: documentation/source integrity only. No runtime, SQL migration, UI or performance test has run.'
