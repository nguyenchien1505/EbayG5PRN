# F01 (docs/architecture/DEPLOYMENT.md): run the app image behind Nginx HTTPS on the user's local stack (project ebaygroup5-local).
# Needs .env (Initialize-LocalEnvironment.ps1), .env.database (Initialize-Database.ps1) and secrets/tls (New-LocalTlsCertificate.ps1).
# -Stop stops and removes only the web, nginx and migrate containers; SQL Server, Redis and every volume are kept.
param(
    [switch]$Stop,
    [ValidateRange(1024, 65535)][int]$HttpsPort = 8443,
    [ValidateRange(1024, 65535)][int]$HttpPort = 8080
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$previous = @{ HTTPS_PORT = $env:HTTPS_PORT; HTTP_PORT = $env:HTTP_PORT }
try {
    foreach ($file in '.env', '.env.database') {
        if (-not (Test-Path -LiteralPath $file)) { throw "$file is missing; see runbook LOCAL_INFRA (Initialize-LocalEnvironment.ps1, Initialize-Database.ps1)." }
    }
    $compose = @('compose', '-f', 'compose.yaml', '-f', 'compose.app.yaml', '--env-file', '.env', '--env-file', '.env.database')
    if ($Stop) {
        docker @compose rm --stop --force nginx web migrate
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop the app containers.' }
        Write-Output 'Stopped web/nginx; SQL Server, Redis and volumes are unchanged.'
        return
    }
    if (-not (Test-Path -LiteralPath 'secrets/tls/server.crt') -or -not (Test-Path -LiteralPath 'secrets/tls/server.key')) {
        throw 'secrets/tls is missing; run scripts/New-LocalTlsCertificate.ps1 first.'
    }
    # Shell variables take precedence over the env files for these two optional ports.
    $env:HTTPS_PORT = "$HttpsPort"
    $env:HTTP_PORT = "$HttpPort"
    docker @compose up -d --build
    if ($LASTEXITCODE -ne 0) { throw 'App stack failed to start; check "docker compose -f compose.yaml -f compose.app.yaml logs web nginx migrate".' }
    Write-Output "App stack ready: https://localhost:$HttpsPort (http://localhost:$HttpPort redirects). Trust secrets/tls/ca.crt to avoid browser warnings (runbook)."
} finally {
    $env:HTTPS_PORT = $previous.HTTPS_PORT
    $env:HTTP_PORT = $previous.HTTP_PORT
    Pop-Location
}
