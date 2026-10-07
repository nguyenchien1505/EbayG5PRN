# Docker classic reports config digest as .Id; the containerd store can report manifest digest instead.
# Read both identities for each exact tag from the checksum-verified save archive, without extracting any file.
param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
$stream = [IO.File]::OpenRead($Archive)
$reader = [System.Formats.Tar.TarReader]::new($stream)
$metadata = @{}
try {
    while ($entry = $reader.GetNextEntry()) {
        if ($entry.Name -notin 'manifest.json', 'index.json') { continue }
        if ($metadata.ContainsKey($entry.Name) -or $entry.Length -gt 1MB -or -not $entry.DataStream) { throw 'Invalid Docker archive metadata.' }
        $text = [IO.StreamReader]::new($entry.DataStream, [Text.Encoding]::UTF8, $true, 4096, $true)
        $metadata[$entry.Name] = $text.ReadToEnd() | ConvertFrom-Json
        $text.Dispose()
    }
} finally { $reader.Dispose(); $stream.Dispose() }
if (-not $metadata.ContainsKey('manifest.json')) { throw 'Docker save manifest is missing.' }
$identities = @{}
foreach ($image in $metadata['manifest.json']) {
    if ($image.Config -notmatch '^(?:blobs/sha256/([0-9a-f]{64})|([0-9a-f]{64})\.json)$') { throw 'Unsupported Docker config digest path.' }
    $configDigest = 'sha256:' + $Matches[1] + $Matches[2]
    foreach ($tag in $image.RepoTags) {
        if ($identities.ContainsKey($tag)) { throw 'Duplicate Docker archive tag.' }
        $identities[$tag] = @($configDigest)
    }
}
if ($metadata.ContainsKey('index.json')) {
    foreach ($descriptor in $metadata['index.json'].manifests) {
        $name = $descriptor.annotations.'io.containerd.image.name'
        if ($name -like 'docker.io/library/*') { $name = $name.Substring('docker.io/library/'.Length) }
        if (-not $name -or -not $identities.ContainsKey($name)) { continue }
        if ($descriptor.digest -notmatch '^sha256:[0-9a-f]{64}$') { throw 'Invalid Docker manifest digest.' }
        $identities[$name] += $descriptor.digest
    }
}
return $identities
