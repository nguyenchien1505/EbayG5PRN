# F01 (docs/architecture/DEPLOYMENT.md): isolated end-to-end smoke test of the Docker app stack through Nginx HTTPS/WSS.
# Creates its own compose project ebaygroup5-stack-<32 hex> with random passwords, free ports, its own subnet and a throw-away
# certificate. A passing run removes exactly that project (containers, network, its named volumes, its image tags); a failing run
# (or -KeepStack) keeps it and writes logs to artifacts/stack/<hex>/ for diagnosis. Remove kept stacks with Clear-TestArtifacts.ps1 -Apply.
# F03 (JOURNEYS.md): also seeds demo data, runs the Playwright browser journeys (installed Chrome) and checks log/DB correlation.
# -KeepRunning leaves a passing or failing stack running for local iteration (remove it later with Clear-TestArtifacts.ps1 -Apply).
param([switch]$KeepStack, [switch]$KeepRunning)
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

    # F03: demo data and private random credentials for the journeys (passed by name only, never on a command line).
    docker @compose run --rm --no-deps migrate --seed-demo
    if ($LASTEXITCODE -ne 0) { throw 'Demo seed failed.' }
    $journeyPasswords = @{ Buyer = New-Secret; Seller = New-Secret; Admin = New-Secret }
    foreach ($role in $journeyPasswords.Keys) {
        [Environment]::SetEnvironmentVariable("Database__DemoPassword$role", $journeyPasswords[$role], 'Process')
        [Environment]::SetEnvironmentVariable("EBAY_JOURNEY_$($role.ToUpperInvariant())_PASSWORD", $journeyPasswords[$role], 'Process')
    }
    docker @compose run --rm --no-deps -e Database__DemoPasswordBuyer -e Database__DemoPasswordSeller -e Database__DemoPasswordAdmin migrate --enable-demo-auth
    if ($LASTEXITCODE -ne 0) { throw 'Demo credentials failed.' }
    $server = [Security.Cryptography.X509Certificates.X509Certificate2]::CreateFromPem([IO.File]::ReadAllText((Join-Path $work 'tls/server.crt')))
    $spki = [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData($server.PublicKey.ExportSubjectPublicKeyInfo()))
    # Journeys run before the smoke: the smoke's spoofing check deliberately exhausts the host's auth window.
    dotnet run --project tests/EbayGroup5.Journeys -c Release --no-launch-profile -- `
        --base "https://localhost:$httpsPort" --spki $spki --out (Join-Path $work 'journeys')
    if ($LASTEXITCODE -ne 0) { throw 'Browser journeys failed; screenshots in artifacts/stack/<hex>/journeys, stack retained.' }

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
    # F04 (SECURITY_TESTS.md): the server itself offers only TLS 1.2/1.3 (the smoke's TLS 1.0/1.1 failure could also come from the client OS).
    $protocols = @(docker @compose exec -T nginx nginx -T 2>$null | Select-String '^\s*ssl_protocols\s+([^;]+);' | ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() } | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $protocols.Count -ne 1 -or $protocols[0] -ne 'TLSv1.2 TLSv1.3') { throw "Unexpected nginx ssl_protocols: $($protocols -join ' / ')" }
    Write-Output 'PASS nginx -T: ssl_protocols TLSv1.2 TLSv1.3 only'

    # F03 / G5-N05: the journey message links nginx request id = app trace = Message.TraceId, and no content reaches any log.
    $markers = @(Get-Content -LiteralPath (Join-Path $work 'journeys/markers.txt'))
    if ($markers.Count -ne 2 -or ($markers | Where-Object { $_ -notmatch '^journey (buyer note|seller reply) [0-9a-f]{10}$' })) { throw 'Unexpected journey markers.' }
    $sql = "SET NOCOUNT ON; SELECT CONCAT(Id,'|',TraceId) FROM dbo.Message WHERE Content = N'$($markers[0])'"
    # The SA password stays inside the SQL container environment.
    $row = docker @compose exec -T sqlserver /bin/sh -c "SQLCMDPASSWORD=`"`$MSSQL_SA_PASSWORD`" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -h -1 -W -d EbayGroup5 -Q `"$sql`""
    if ($LASTEXITCODE -ne 0 -or "$row".Trim() -notmatch '^(\d+)\|([0-9a-f]{32})$') { throw 'Journey message not found in SQL.' }
    $messageId = $Matches[1]; $trace = $Matches[2]
    $nginxLog = (docker @compose logs --no-color nginx 2>&1) -join "`n"
    $webLog = (docker @compose logs --no-color web 2>&1) -join "`n"
    if ($nginxLog -notmatch "rid=$trace") { throw 'The message trace does not match any nginx request id.' }
    if ($webLog -notmatch "message=$messageId\b[^`n]*trace=$trace") { throw 'The app log does not link the message id to its trace.' }
    foreach ($marker in $markers) { if ($nginxLog.Contains($marker) -or $webLog.Contains($marker)) { throw 'Chat content reached a log.' } }
    Write-Output "PASS Message $($messageId): nginx rid = app trace = Message.TraceId ($trace); no chat content in nginx or web logs"
    # Informational (round-robin does not guarantee it): how many replicas held the journey's chat WebSockets.
    $socketUpstreams = @($nginxLog -split "`n" | Select-String '"GET /hubs/chat" 101 .* upstream=(\S+)' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Sort-Object -Unique)
    Write-Output "INFO chat WebSockets were served by $($socketUpstreams.Count) web replica(s)"
    Write-Output 'PASS: app image + Nginx HTTPS/WSS stack smoke (isolated project).'
    $succeeded = $true
} finally {
    foreach ($role in 'Buyer', 'Seller', 'Admin') {
        [Environment]::SetEnvironmentVariable("Database__DemoPassword$role", $null, 'Process')
        [Environment]::SetEnvironmentVariable("EBAY_JOURNEY_$($role.ToUpperInvariant())_PASSWORD", $null, 'Process')
    }
    if ($started -and $KeepRunning) {
        Write-Warning "Left $project running (https://localhost:$httpsPort, CA $work/tls/ca.crt); remove it with scripts/Clear-TestArtifacts.ps1 -Apply."
    } elseif ($started) {
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
