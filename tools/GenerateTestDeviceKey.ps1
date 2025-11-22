#!/usr/bin/env pwsh
# GenerateTestDeviceKey.ps1
# Generuje parę kluczy RSA-2048 do testowania rejestracji urządzenia

Write-Host "=== Test Device Key Generator ===" -ForegroundColor Cyan
Write-Host "Generowanie pary kluczy RSA-2048..." -ForegroundColor Yellow

# Generuje klucz RSA-2048
$rsa = [System.Security.Cryptography.RSA]::Create(2048)

# Eksportuje klucz publiczny w formacie PEM
$publicKeyBytes = $rsa.ExportSubjectPublicKeyInfo()
$publicKeyBase64 = [Convert]::ToBase64String($publicKeyBytes)

# Formatuje jako PEM (64 znaki na linię)
$pemLines = @()
for ($i = 0; $i -lt $publicKeyBase64.Length; $i += 64) {
    $length = [Math]::Min(64, $publicKeyBase64.Length - $i)
    $pemLines += $publicKeyBase64.Substring($i, $length)
}

$publicKeyPem = "-----BEGIN PUBLIC KEY-----`n" + ($pemLines -join "`n") + "`n-----END PUBLIC KEY-----"

Write-Host "`n✓ Wygenerowano klucz RSA-2048" -ForegroundColor Green

Write-Host "`n=== KLUCZ PUBLICZNY (do Swagger) ===" -ForegroundColor Cyan
Write-Host $publicKeyPem -ForegroundColor White

Write-Host "`n=== JSON dla Swagger POST /api/device/register ===" -ForegroundColor Cyan

# Zamiana prawdziwych znaków końca linii na ich dosłowne odpowiedniki "\n" do użycia w JSON
$publicKeyPemEscaped = $publicKeyPem -replace "`r`n", "\n" -replace "`n", "\n"

# Ręczne złożenie JSON-a z poprawnie escapowanymi stringami
$deviceId = "test-device-$(Get-Random -Minimum 1000 -Maximum 9999)"
$jsonPayload = @"
{
  "deviceId": "$deviceId",
  "deviceName": "Test Device - PowerShell Generated",
  "publicKeyPem": "$publicKeyPemEscaped",
  "tpmAttestation": null
}
"@

Write-Host $jsonPayload -ForegroundColor White

# Zapisje klucze do plików
$publicKeyPem | Out-File -FilePath "test_device_public.pem" -Encoding utf8 -NoNewline

# Zapisuje klucz prywatny w formacie PEM
$privateKeyBytes = $rsa.ExportPkcs8PrivateKey()
$privateKeyBase64 = [Convert]::ToBase64String($privateKeyBytes)

# Formatuje jako PEM (64 znaki na linię)
$privatePemLines = @()
for ($i = 0; $i -lt $privateKeyBase64.Length; $i += 64) {
    $length = [Math]::Min(64, $privateKeyBase64.Length - $i)
    $privatePemLines += $privateKeyBase64.Substring($i, $length)
}

$privateKeyPem = "-----BEGIN PRIVATE KEY-----`n" + ($privatePemLines -join "`n") + "`n-----END PRIVATE KEY-----"
$privateKeyPem | Out-File -FilePath "test_device_private.pem" -Encoding utf8 -NoNewline

Write-Host "`n✓ Zapisano klucze:" -ForegroundColor Green
Write-Host "  - test_device_public.pem (klucz publiczny)" -ForegroundColor Gray
Write-Host "  - test_device_private.pem (klucz prywatny - do późniejszych testów)" -ForegroundColor Gray

Write-Host "`n📋 Skopiuj JSON powyżej i wklej do Swagger UI w POST /api/device/register" -ForegroundColor Yellow

$rsa.Dispose()
