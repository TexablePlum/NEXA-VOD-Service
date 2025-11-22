#!/usr/bin/env pwsh

<#
.SYNOPSIS
    NEXA - Secure Content Upload Pipeline
.DESCRIPTION
    Complete automated upload workflow:
    1. Generate admin JWT token
    2. Transcode, segment & encrypt video (CEK in-memory only)
    3. Register content + import CEKs to DRM Server (atomic operation)
    4. Cleanup memory
    5. Summary

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

.EXAMPLE
    # Upload with full quality range (mobile to 8K)
    .\upload-content.ps1 -InputFile "movie.mp4" -Qualities "144p","240p","480p","720p","1080p","1440p","2160p" -Plan "pro"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputFile,

    [Parameter()]
    [ValidateSet('144p', '240p', '360p', '480p', '720p', '1080p', '1440p', '2160p', '4320p', '4k', '8k')]
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
    [string]$OutputDir = "./temp-upload",

    [Parameter()]
    [ValidateSet('h264_nvenc', 'libx264', 'h264_amf', 'h264_qsv')]
    [string]$Codec = 'h264_nvenc',

    [Parameter()]
    [string]$DrmServerUrl = "http://localhost/api/admin/cek/import",

    [Parameter()]
    [string]$ContentServerContainer = "nexa-content-server"
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

function Register-ContentWithCeks {
    param(
        [string]$ContentId,
        [hashtable]$EncryptionData,
        [string]$AdminToken,
        [string]$ServerUrl
    )

    Write-Info "Rejestrowanie contentu i importowanie CEK-ów w DRM Server..."

    # Przygotuj array CEKs dla nowego API
    $ceksArray = @()
    foreach ($quality in $EncryptionData.Keys) {
        $qualityData = $EncryptionData[$quality]

        $ceksArray += @{
            quality = $quality
            cek = $qualityData.Cek
            keyId = $qualityData.KeyId
        }

        Write-Info "  - Przygotowano CEK dla jakości: $quality (KeyId: $($qualityData.KeyId))"
    }

    # Nowy endpoint - atomic registration with CEKs
    $registerUrl = $ServerUrl -replace '/cek/import$', '/content/register'

    $body = @{
        contentId = $ContentId
        ceks = $ceksArray
    } | ConvertTo-Json -Depth 5

    try {
        $response = Invoke-RestMethod `
            -Uri $registerUrl `
            -Method Post `
            -Headers @{ Authorization = "Bearer $AdminToken" } `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType "application/json" `
            -ErrorAction Stop

        Write-Success "Content zarejestrowany pomyślnie!"
        Write-Success "  ✓ Zaimportowano $($response.totalCeksImported) CEK(s): $($response.importedQualities -join ', ')"
        Write-Info "  Required Plan: $($response.requiredPlan)"

        if ($null -ne $response.releaseDate) {
            Write-Info "  Release Date: $($response.releaseDate)"
        }

        return $true
    }
    catch {
        $errorMessage = $_.Exception.Message
        Write-Failure "Błąd podczas rejestracji contentu w DRM: $errorMessage"

        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__
            Write-Failure "  HTTP Status: $statusCode"
        }

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
# STEP 1/7: Generate Admin Token
# ========================================

Write-Progress-Step -Step 1 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Generowanie admin token..."

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
# STEP 2/7: Prepare Content (Transcode, Segment, Encrypt)
# ========================================

Write-Progress-Step -Step 2 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Transkodowanie i segmentacja..."

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
# STEP 3/7: Copy Content to Docker Volume
# ========================================

Write-Progress-Step -Step 3 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Kopiowanie do Docker volume..."

Write-Info "Kopiowanie contentu do Docker volume..."

$localContentPath = Join-Path $OutputDir $contentId
$dockerDestPath = "/app/content/"

try {
    # Verify Docker container is running
    $containerStatus = docker inspect -f '{{.State.Running}}' $ContentServerContainer 2>$null
    if ($containerStatus -ne 'true') {
        throw "Container '$ContentServerContainer' nie jest uruchomiony. Uruchom: docker compose up -d"
    }

    # Copy content to Docker volume
    $dockerCpCommand = "docker cp `"$localContentPath`" ${ContentServerContainer}:${dockerDestPath}"
    Write-Info "Wykonuję: $dockerCpCommand"

    docker cp $localContentPath "${ContentServerContainer}:${dockerDestPath}"

    if ($LASTEXITCODE -ne 0) {
        throw "docker cp zakończył się błędem (exit code: $LASTEXITCODE)"
    }

    Write-Success "Content skopiowany do Docker volume: ${ContentServerContainer}:${dockerDestPath}${contentId}"

    # Cleanup temp directory
    Write-Info "Usuwanie tymczasowego folderu: $localContentPath"
    Remove-Item -Path $localContentPath -Recurse -Force
    Write-Success "Tymczasowy folder usunięty"
}
catch {
    Write-Failure "Błąd podczas kopiowania do Docker: $_"
    Write-Info "Content pozostał w lokalizacji: $localContentPath"
    exit 1
}

# ========================================
# STEP 4/7: Register Content + Import CEKs (Atomic Operation)
# ========================================

Write-Progress-Step -Step 4 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Rejestrowanie contentu i importowanie CEK-ów..."

if ($null -ne $encryptionData -and $encryptionData.Count -gt 0) {
    try {
        $registrationSuccess = Register-ContentWithCeks `
            -ContentId $contentId `
            -EncryptionData $encryptionData `
            -AdminToken $adminToken `
            -ServerUrl $DrmServerUrl

        if (-not $registrationSuccess) {
            Write-Failure "Nie udało się zarejestrować contentu w DRM Server"
            exit 1
        }
    }
    catch {
        Write-Failure "Błąd podczas rejestracji contentu: $_"
        Write-Info "Content został przygotowany w storage, ale nie został zarejestrowany w DRM"
        exit 1
    }
}
else {
    Write-Info "Brak danych szyfrowania (content bez szyfrowania) - pomijanie rejestracji w DRM"
}

# ========================================
# STEP 5/7: Cleanup (Security)
# ========================================

Write-Progress-Step -Step 5 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Czyszczenie pamięci..."

# SECURITY: Clear sensitive data from memory
$encryptionData = $null
$adminToken = $null
[System.GC]::Collect()

Write-Success "Pamięć wyczyszczona (CEK usunięte)"

# ========================================
# STEP 6/7: Final Summary
# ========================================

Write-Progress-Step -Step 6 -TotalSteps 7 -Activity "Upload Pipeline" -Status "Zakończono!"
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

📦 Storage:
   ✓ Content stored in Docker volume (${ContentServerContainer}:/app/content/${contentId})
   ✓ Temp files cleaned up

Content jest gotowy do streamingu!
========================================

"@ -ForegroundColor Green

exit 0
