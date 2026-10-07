# F01 (docs/architecture/DEPLOYMENT.md): isolated end-to-end smoke test of the Docker app stack through Nginx HTTPS/WSS.
# Creates its own compose project ebaygroup5-stack-<32 hex> with random passwords, free ports, its own subnet and a throw-away
# certificate. A passing run removes exactly that project (containers, network, its named volumes, its image tags); a failing run
# (or -KeepStack) keeps it and writes logs to artifacts/stack/<hex>/ for diagnosis. Remove kept stacks with Clear-TestArtifacts.ps1 -Apply.
param([switch]$KeepStack)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$hex = [Guid]::NewGuid().ToString('N')
$project = "ebaygroup5-stack-$hex"
$work = Join-Path $repoRoot "artifacts/stack/$hex"
$tag = "stack-$hex"
$started = $false
$succeeded = $false

function Get-FreePort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    try { return $listener.LocalEndpoint.Port } finally { $listener.Stop() }
}
function New-Secret {
    $value = 'Eg5!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$value" | Out-Host }
    return $value
}

Push-Location $repoRoot
try {
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    pwsh -NoProfile -File "$PSScriptRoot/New-LocalTlsCertificate.ps1" -OutputDirectory (Join-Path $work 'tls') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the test certificate.' }
    $httpsPort = Get-FreePort
    $httpPort = Get-FreePort
    $octet = Get-Random -Minimum 200 -Maximum 250
    $envFile = Join-Path $work 'stack.env'
    # Compose reads this file only for interpolation; it never enters an image (artifacts/ is ignored by git and .dockerignore).
    [IO.File]::WriteAllLines($envFile, [string[]]@(
        "SQLSERVER_PORT=$(Get-FreePort)", "REDIS_PORT=$(Get-FreePort)",
        "MSSQL_SA_PASSWORD=$(New-Secret)", "REDIS_PASSWORD=$(New-Secret)", "SQL_APP_PASSWORD=$(New-Secret)",
        "HTTP_PORT=$httpPort", "HTTPS_PORT=$httpsPort", "APP_IMAGE_TAG=$tag",
        "APP_SUBNET=172.30.$octet.0/24", "APP_GATEWAY=172.30.$octet.1", "APP_PROXY_ADDRESS=172.30.$octet.10",
        "TLS_DIR=$((Join-Path $work 'tls').Replace('\', '/'))"))
    $compose = @('compose', '-p', $project, '-f', 'compose.yaml', '-f', 'compose.app.yaml', '--env-file', $envFile)
    Write-Output "Isolated app stack: $project (https://localhost:$httpsPort)"

    docker @compose build
    if ($LASTEXITCODE -ne 0) { throw 'Image build failed.' }
    $started = $true
    docker @compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'Stack start failed (migrate, web health or nginx); stack retained for diagnosis.' }
    $migrateExit = docker inspect --format '{{.State.ExitCode}}' "$project-migrate-1"
    if ($LASTEXITCODE -ne 0 -or $migrateExit -ne '0') { throw 'Migration container did not exit cleanly.' }

    dotnet run --project tests/EbayGroup5.StackSmoke -c Release --no-launch-profile -- `
        --https-port $httpsPort --http-port $httpPort --ca (Join-Path $work 'tls/ca.crt') --wait-seconds 240
    if ($LASTEXITCODE -ne 0) { throw 'Stack smoke checks failed; stack retained for diagnosis.' }
    # F02 (SCALE_OUT.md): at least two running web replicas actually served the signed-in requests.
    $replicas = @(docker @compose ps --status running --format '{{.Name}}' web)
    if ($LASTEXITCODE -ne 0 -or $replicas.Count -lt 2) { throw "Expected at least two running web replicas, found $($replicas.Count)." }
    $upstreams = @(docker @compose logs --no-color nginx 2>&1 | Select-String '"GET /Account/Me" 200 .* upstream=(\S+)' |
        ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
    if ($upstreams.Count -lt 2) { throw "Signed-in requests reached $($upstreams.Count) upstream(s); expected two or more." }
    Write-Output "PASS $($replicas.Count) web replicas served signed-in requests ($($upstreams.Count) distinct upstreams; login kept across instances)"
    Write-Output 'PASS: app image + Nginx HTTPS/WSS stack smoke (isolated project).'
    $succeeded = $true
} finally {
    if ($started) {
        if ($succeeded -and -not $KeepStack) {
            docker @compose down --remove-orphans 2>$null | Out-Null
            # Only this project's named volumes and image tags; nothing shared with other stacks.
            foreach ($volume in 'sqlserver-data', 'redis-data', 'dataprotection-keys') { docker volume rm "${project}_$volume" 2>$null | Out-Null }
            docker image rm "ebaygroup5-web:$tag" "ebaygroup5-migrate:$tag" 2>$null | Out-Null
            Remove-Item -Recurse -Force -LiteralPath $work -ErrorAction SilentlyContinue
            if (docker ps -a --filter "label=com.docker.compose.project=$project" --format '{{.Names}}') {
                Write-Warning "Could not remove all of $project; run scripts/Clear-TestArtifacts.ps1 -Apply."
            }
        } else {
            docker @compose logs --no-color --timestamps 2>&1 | Out-File -Encoding utf8 (Join-Path $work 'logs.txt')
            docker @compose stop 2>$null | Out-Null
            Write-Warning "Kept $project (logs in artifacts/stack/$hex/logs.txt); remove it with scripts/Clear-TestArtifacts.ps1 -Apply."
        }
    } elseif (-not $succeeded) {
        Write-Warning "Stack $project was not started; nothing to clean up beyond artifacts/stack/$hex."
    }
    Pop-Location
}
