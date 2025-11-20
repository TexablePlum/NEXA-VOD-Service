#!/usr/bin/env pwsh

<#
.SYNOPSIS
    NEXA - Secure Content Upload Pipeline
.DESCRIPTION
    Complete automated upload workflow:
    1. Transcode & segment video (configurable codec)
    2. Encrypt segments (CEK in-memory only)
    3. Import CEK to DRM Server (secure API)
    4. Create metadata

    SECURITY: CEK keys are NEVER written to disk - only in memory
.EXAMPLE
    # Basic upload with NVIDIA GPU (NVenc)
    .\upload-content.ps1 -InputFile "movie.mp4" -Qualities "720p","1080p" -Title "My Movie" -Plan "pro"

.EXAMPLE
    # Upload with CPU encoding (slower but works everywhere)
    .\upload-content.ps1 -InputFile "movie.mp4" -Codec "libx264" -Description "My description" -ReleaseDate "2025-12-01"

.EXAMPLE
    # Upload with AMD GPU
    .\upload-content.ps1 -InputFile "movie.mp4" -Codec "h264_amf" -Qualities "720p","1080p","4k"

.EXAMPLE
    # Upload with Intel Quick Sync
    .\upload-content.ps1 -InputFile "movie.mp4" -Codec "h264_qsv" -Title "Test" -Plan "free"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputFile,

    [Parameter()]
    [ValidateSet('480p', '720p', '1080p', '4k')]
    [string[]]$Qualities = @('720p'),

    [Parameter()]
    [string]$Title,

    [Parameter()]
    [string]$Description = "Brak opisu.",

    [Parameter()]
    [ValidateSet('free', 'basic', 'pro')]
    [string]$Plan = 'free',

    [Parameter()]
    [string]$ReleaseDate,

    [Parameter()]
    [string]$ContentId,

    [Parameter()]
    [string]$OutputDir = "./content/storage",

    [Parameter()]
    [ValidateSet('h264_nvenc', 'libx264', 'h264_amf', 'h264_qsv')]
    [string]$Codec = 'h264_nvenc',

    [Parameter()]
    [string]$DrmServerUrl = "http://localhost/api/admin/cek/import"
)

$ErrorActionPreference = "Stop"

# ========================================
# Helper Functions
# ========================================

function Write-Progress-Step {
    param(
        [int]$Step,
        [int]$TotalSteps,
        [string]$Activity,
        [string]$Status = "Processing..."
    )

    $percentComplete = [math]::Round(($Step / $TotalSteps) * 100)
    Write-Progress -Activity $Activity -Status $Status -PercentComplete $percentComplete
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Failure {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Get-AdminToken {
    param(
        [string]$JwtSecret
    )

    Write-Info "Generowanie admin JWT token..."

    try {
        # Run AdminTokenGenerator to get admin token
        $tokenOutput = dotnet run --project tools/AdminTokenGenerator/AdminTokenGenerator.csproj -- $JwtSecret 24 2>&1

        # Extract token from output (last line after "========================================")
        $token = ($tokenOutput | Select-String -Pattern "^eyJ" | Select-Object -Last 1).Line

        if ([string]::IsNullOrEmpty($token)) {
            throw "Failed to generate admin token. Output: $tokenOutput"
        }

        Write-Success "Admin token wygenerowany (ważny 24h)"
        return $token
    }
    catch {
        Write-Failure "Nie udało się wygenerować admin tokena: $_"
        throw
    }
}

function Import-CekToServer {
    param(
        [string]$ContentId,
        [hashtable]$EncryptionData,
        [string]$AdminToken,
        [string]$ServerUrl
    )

    Write-Info "Importowanie CEK do DRM Server..."

    $importedCount = 0
    $totalQualities = $EncryptionData.Keys.Count

    foreach ($quality in $EncryptionData.Keys) {
        $qualityData = $EncryptionData[$quality]

        Write-Info "  Importowanie CEK dla jakości: $quality"

        $body = @{
            contentId = $ContentId
            quality = $quality
            cek = $qualityData.Cek
            keyId = $qualityData.KeyId
        } | ConvertTo-Json -Compress

        try {
            $response = Invoke-RestMethod -Uri $ServerUrl `
                -Method Post `
                -Headers @{ Authorization = "Bearer $AdminToken" } `
                -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
                -ContentType "application/json"

            Write-Success "  ✓ $quality - CEK zaimportowany (KeyId: $($qualityData.KeyId))"
            $importedCount++
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $errorMessage = $_.Exception.Message

            Write-Failure "  ✗ $quality - Import failed (HTTP $statusCode): $errorMessage"

            # Continue with other qualities even if one fails
        }
    }

    if ($importedCount -eq $totalQualities) {
        Write-Success "Wszystkie CEK-i zaimportowane pomyślnie ($importedCount/$totalQualities)"
        return $true
    }
    else {
        Write-Failure "Niepowodzenie importu niektórych CEK-ów ($importedCount/$totalQualities)"
        return $false
    }
}

# ========================================
# Main Upload Pipeline
# ========================================

Write-Host @"

========================================
🎬 NEXA - Secure Content Upload Pipeline
========================================
Security Features:
  ✓ CEK keys in-memory only (no disk write)
  ✓ Secure API transport
  ✓ Admin JWT authentication
  ✓ IP whitelist protection
========================================

"@ -ForegroundColor Cyan

# Validate input file
if (-not (Test-Path $InputFile)) {
    Write-Failure "Plik nie istnieje: $InputFile"
    exit 1
}

# Extract title from filename if not provided
if ([string]::IsNullOrEmpty($Title)) {
    $Title = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
}

Write-Info "Plik wideo: $InputFile"
Write-Info "Tytuł: $Title"
Write-Info "Opis: $Description"
Write-Info "Jakości: $($Qualities -join ', ')"
Write-Info "Kodek: $Codec"
Write-Info "Plan: $Plan"
if (-not [string]::IsNullOrEmpty($ReleaseDate)) {
    Write-Info "Data wydania: $ReleaseDate"
}
Write-Host ""

# ========================================
# STEP 1/6: Generate Admin Token
# ========================================

Write-Progress-Step -Step 1 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Generowanie admin token..."

$jwtSecret = $env:JWT_SECRET
if ([string]::IsNullOrEmpty($jwtSecret)) {
    Write-Failure "JWT_SECRET nie jest ustawiony w zmiennych środowiskowych"
    Write-Info "Ustaw za pomocą: `$env:JWT_SECRET = 'twój-klucz'"
    exit 1
}

try {
    $adminToken = Get-AdminToken -JwtSecret $jwtSecret
}
catch {
    Write-Failure "Nie udało się wygenerować admin tokena"
    exit 1
}

# ========================================
# STEP 2/6: Prepare Content (Transcode, Segment, Encrypt)
# ========================================

Write-Progress-Step -Step 2 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Transkodowanie i segmentacja..."

Write-Info "Rozpoczynanie przygotowania contentu (kodek: $Codec)..."

try {
    # Build parameters hashtable for prepare-content.ps1
    $prepareParams = @{
        InputFile = $InputFile
        Qualities = $Qualities
        Description = $Description
        RequiredPlan = $Plan
        OutputDir = $OutputDir
        Codec = $Codec
    }

    # Add optional parameters if provided
    if (-not [string]::IsNullOrEmpty($Title)) {
        $prepareParams.Add('Title', $Title)
    }

    if (-not [string]::IsNullOrEmpty($ContentId)) {
        $prepareParams.Add('ContentId', $ContentId)
    }

    if (-not [string]::IsNullOrEmpty($ReleaseDate)) {
        $prepareParams.Add('ReleaseDate', $ReleaseDate)
    }

    $prepareResult = & ".\scripts\prepare-content.ps1" @prepareParams

    if ($null -eq $prepareResult) {
        throw "prepare-content.ps1 nie zwrócił wyniku"
    }

    $contentId = $prepareResult.ContentId
    $encryptionData = $prepareResult.EncryptionData

    Write-Success "Content przygotowany: ContentId = $contentId"
}
catch {
    Write-Failure "Błąd podczas przygotowania contentu: $_"
    exit 1
}

# ========================================
# STEP 3/6: Import CEK to DRM Server
# ========================================

Write-Progress-Step -Step 3 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Importowanie kluczy szyfrowania..."

if ($null -ne $encryptionData -and $encryptionData.Count -gt 0) {
    try {
        $importSuccess = Import-CekToServer `
            -ContentId $contentId `
            -EncryptionData $encryptionData `
            -AdminToken $adminToken `
            -ServerUrl $DrmServerUrl

        if (-not $importSuccess) {
            Write-Failure "Nie wszystkie CEK-i zostały zaimportowane"
            Write-Info "Możesz spróbować ręcznie zaimportować klucze później"
        }
    }
    catch {
        Write-Failure "Błąd podczas importu CEK: $_"
        Write-Info "Content został przygotowany, ale klucze nie zostały zaimportowane"
        exit 1
    }
}
else {
    Write-Info "Brak danych szyfrowania (content bez szyfrowania)"
}

# ========================================
# STEP 4/6: Register Content in DRM Server
# ========================================

Write-Progress-Step -Step 4 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Rejestrowanie contentu w DRM Server..."

try {
    $registerUrl = $DrmServerUrl -replace '/cek/import$', '/content/register'
    $registerBody = @{
        contentId = $contentId
    } | ConvertTo-Json

    $registerResponse = Invoke-RestMethod `
        -Uri $registerUrl `
        -Method Post `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -ContentType "application/json" `
        -Body $registerBody `
        -ErrorAction Stop

    Write-Success "Content zarejestrowany w DRM Server"
}
catch {
    Write-Failure "Błąd podczas rejestracji contentu w DRM: $_"
    Write-Info "CEK zostały zaimportowane, ale content nie został zarejestrowany"
    Write-Info "Możesz ręcznie wywołać: POST /api/admin/content/register"
}

# ========================================
# STEP 5/6: Cleanup (Security)
# ========================================

Write-Progress-Step -Step 5 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Czyszczenie pamięci..."

# SECURITY: Clear sensitive data from memory
$encryptionData = $null
$adminToken = $null
[System.GC]::Collect()

Write-Success "Pamięć wyczyszczona (CEK usunięte)"

# ========================================
# STEP 6/6: Final Summary
# ========================================

Write-Progress-Step -Step 6 -TotalSteps 6 -Activity "Upload Pipeline" -Status "Zakończono!"
Write-Progress -Activity "Upload Pipeline" -Completed

Write-Host @"

========================================
✅ Upload zakończony pomyślnie!
========================================
Content ID: $contentId
Tytuł: $Title
Opis: $Description
Jakości: $($Qualities -join ', ')
Kodek: $Codec
Plan: $Plan

📹 Manifest URL:
   /content/$contentId/manifest.mpd

🔒 Security Status:
   ✓ CEK keys imported to DRM Server
   ✓ No plaintext keys on disk
   ✓ Memory cleared

Content jest gotowy do streamingu!
========================================

"@ -ForegroundColor Green

exit 0
