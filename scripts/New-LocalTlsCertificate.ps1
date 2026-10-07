# F01 (docs/architecture/DEPLOYMENT.md): local CA (400 days) + server certificate (397 days) for https://localhost, written as PEM files.
# The CA private key is never written to disk, so the CA cannot sign anything else. Trusting ca.crt is the user's choice (runbook).
param(
    [string]$OutputDirectory = 'secrets/tls',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$target = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $repoRoot $OutputDirectory }
$files = 'ca.crt', 'server.crt', 'server.key' | ForEach-Object { Join-Path $target $_ }
if (-not $Force -and ($files | Where-Object { Test-Path -LiteralPath $_ })) {
    Write-Output "TLS files already exist in $OutputDirectory; pass -Force to replace them."
    return
}
New-Item -ItemType Directory -Force -Path $target | Out-Null

Add-Type -AssemblyName System.Security.Cryptography
$X509 = [System.Security.Cryptography.X509Certificates.X509Certificate2]
$now = [DateTimeOffset]::UtcNow

$caKey = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$caRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=EbayGroup5 Local Development CA', $caKey, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($true, $true, 0, $true))
$caRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]'KeyCertSign, CrlSign', $true))
$caSki = [System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($caRequest.PublicKey, $false)
$caRequest.CertificateExtensions.Add($caSki)
$ca = $caRequest.CreateSelfSigned($now.AddDays(-1), $now.AddDays(400))

$serverKey = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
$serverRequest = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $serverKey, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$san = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
$san.AddDnsName('localhost')
$san.AddIpAddress([System.Net.IPAddress]::Parse('127.0.0.1'))
$san.AddIpAddress([System.Net.IPAddress]::IPv6Loopback)
$serverRequest.CertificateExtensions.Add($san.Build($true))
$serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
$serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]'DigitalSignature', $true))
$eku = [System.Security.Cryptography.OidCollection]::new()
$eku.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.1')) | Out-Null # serverAuth
$serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku, $false))
$serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($serverRequest.PublicKey, $false))
$serverRequest.CertificateExtensions.Add([System.Security.Cryptography.X509Certificates.X509AuthorityKeyIdentifierExtension]::CreateFromSubjectKeyIdentifier($caSki))
$serial = [byte[]]::new(16)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($serial)
$serial[0] = $serial[0] -band 0x7F
# 397 days: within the browser limit for TLS server certificates.
$server = $serverRequest.Create($ca, $now.AddDays(-1), $now.AddDays(397), $serial)

[IO.File]::WriteAllText($files[0], $ca.ExportCertificatePem() + "`n")
[IO.File]::WriteAllText($files[1], $server.ExportCertificatePem() + "`n")
[IO.File]::WriteAllText($files[2], $serverKey.ExportPkcs8PrivateKeyPem() + "`n")
$caKey.Dispose(); $serverKey.Dispose()
Write-Output "Created $OutputDirectory/ca.crt, server.crt, server.key (CA key discarded). Server certificate valid until $($server.NotAfter.ToUniversalTime().ToString('yyyy-MM-dd')) UTC."
