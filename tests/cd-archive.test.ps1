# Cross-store IDs must stay bound to the correct image tag, never accepted as arbitrary digest aliases.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$work = Join-Path $repoRoot ('artifacts/cd-archive-test/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$archive = Join-Path $work 'images.tar'
$tag = 'ebaygroup5-web:' + ('1' * 40)
$configHash = 'a' * 64; $manifestHash = 'b' * 64
$saveManifest = @(@{ Config = "blobs/sha256/$configHash"; RepoTags = @($tag); Layers = @() })
$index = @{ schemaVersion = 2; manifests = @(@{ digest = "sha256:$manifestHash"; annotations = @{ 'io.containerd.image.name' = "docker.io/library/$tag" } }) }
function Write-Archive([bool]$includeIndex = $true) {
    $stream = [IO.File]::Create($archive)
    $writer = [System.Formats.Tar.TarWriter]::new($stream)
    try {
        $data = @{ 'manifest.json' = ConvertTo-Json -InputObject $saveManifest -Depth 6 }
        if ($includeIndex) { $data['index.json'] = $index | ConvertTo-Json -Depth 6 }
        foreach ($name in $data.Keys) {
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $name)
            $entry.DataStream = [IO.MemoryStream]::new([Text.Encoding]::UTF8.GetBytes($data[$name]))
            $writer.WriteEntry($entry)
            $entry.DataStream.Dispose()
        }
    } finally { $writer.Dispose(); $stream.Dispose() }
}
Write-Archive
$ids = & "$repoRoot/scripts/Get-CdArchiveIdentities.ps1" -Archive $archive
if ($ids.Count -ne 1 -or $ids[$tag].Count -ne 2 -or "sha256:$configHash" -notin $ids[$tag] -or "sha256:$manifestHash" -notin $ids[$tag]) { throw 'Classic/containerd digest mapping failed.' }
if (('sha256:' + ('c' * 64)) -in $ids[$tag]) { throw 'Unrelated image ID was accepted.' }
Write-Output 'PASS: config/manifest identities bound to exact tag; unrelated digest rejected.'
Write-Archive $false
$ids = & "$repoRoot/scripts/Get-CdArchiveIdentities.ps1" -Archive $archive
if ($ids[$tag].Count -ne 1 -or "sha256:$configHash" -notin $ids[$tag]) { throw 'Legacy config identity failed.' }
Write-Output 'PASS: legacy save archive uses config identity only.'
$saveManifest[0].Config = '../untrusted.json'; Write-Archive
try { $null = & "$repoRoot/scripts/Get-CdArchiveIdentities.ps1" -Archive $archive; throw 'Invalid config path was accepted.' }
catch { if ($_.Exception.Message -notlike '*Unsupported Docker config*') { throw }; Write-Output 'PASS: invalid config identity path rejected.' }
$parent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts/cd-archive-test'))
$resolved = [IO.Path]::GetFullPath($work)
if (-not $resolved.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive test cleanup path escaped its parent.' }
Remove-Item -LiteralPath $resolved -Recurse -Force
