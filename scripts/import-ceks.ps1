#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Import CEK-ów do Redis dla DRM Server
.DESCRIPTION
    Skanuje content/storage/{contentId}/encryption.json i metadata.json,
    importuje CEK-i i metadane contentu do Redis.
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ContentStoragePath = "./content/storage",

    [Parameter()]
    [string]$RedisHost = "localhost:6379",

    [Parameter()]
    [string]$RedisPassword = $null
)

$ErrorActionPreference = "Stop"

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

# Sprawdź czy StackExchange.Redis jest dostępny
try {
    Add-Type -Path "$PSScriptRoot/../tools/StackExchange.Redis.dll" -ErrorAction Stop
}
catch {
    Write-Failure "StackExchange.Redis.dll not found. Please install Redis client library."
    Write-Info "You can download it from NuGet: https://www.nuget.org/packages/StackExchange.Redis/"
    exit 1
}

function Import-ContentCeks {
    param(
        [string]$ContentDir,
        [Parameter(Mandatory)]
        $RedisDb
    )

    $contentId = Split-Path $ContentDir -Leaf

    $encryptionFile = Join-Path $ContentDir "encryption.json"
    $metadataFile = Join-Path $ContentDir "metadata.json"

    if (-not (Test-Path $encryptionFile)) {
        Write-Info "Skipping $contentId - no encryption.json (unencrypted content)"
        return
    }

    if (-not (Test-Path $metadataFile)) {
        Write-Failure "Skipping $contentId - no metadata.json"
        return
    }

    # Wczytaj encryption.json
    $encryption = Get-Content $encryptionFile | ConvertFrom-Json

    # Wczytaj metadata.json
    $metadata = Get-Content $metadataFile | ConvertFrom-Json

    Write-Info "Importing CEKs for content: $contentId (plan: $($metadata.RequiredPlan))"

    # Importuj metadane contentu (RequiredPlan)
    $contentMetaKey = "content:meta:$contentId"
    $contentMetaValue = @{
        RequiredPlan = $metadata.RequiredPlan
    } | ConvertTo-Json -Compress

    $RedisDb.StringSet($contentMetaKey, $contentMetaValue) | Out-Null

    # Importuj CEK-i dla każdej jakości
    $importedCount = 0

    foreach ($quality in $encryption.Qualities.PSObject.Properties.Name) {
        $qualityData = $encryption.Qualities.$quality

        $cekKey = "cek:${contentId}:${quality}"
        $cekValue = @{
            Key = (Get-Content (Join-Path $ContentDir "$quality/$quality.key") -Raw).Trim()
            KeyId = $qualityData.KeyId
        } | ConvertTo-Json -Compress

        $RedisDb.StringSet($cekKey, $cekValue) | Out-Null

        $importedCount++
        Write-Info "  - $quality : KID=$($qualityData.KeyId)"
    }

    Write-Success "Imported $importedCount CEKs for $contentId"
}

function Main {
    Write-Host @"
========================================
NEXA - CEK Import to Redis
========================================
"@ -ForegroundColor Cyan

    Write-Info "Content storage path: $ContentStoragePath"
    Write-Info "Redis host: $RedisHost"

    if (-not (Test-Path $ContentStoragePath)) {
        Write-Failure "Content storage path does not exist: $ContentStoragePath"
        exit 1
    }

    # Połącz z Redis
    Write-Info "Connecting to Redis..."

    try {
        $redisConfig = [StackExchange.Redis.ConfigurationOptions]::Parse($RedisHost)

        if ($RedisPassword) {
            $redisConfig.Password = $RedisPassword
        }

        $redisConfig.ConnectTimeout = 5000
        $redisConfig.SyncTimeout = 5000

        $redis = [StackExchange.Redis.ConnectionMultiplexer]::Connect($redisConfig)
        $db = $redis.GetDatabase()

        Write-Success "Connected to Redis"
    }
    catch {
        Write-Failure "Failed to connect to Redis: $_"
        exit 1
    }

    # Skanuj content directories
    $contentDirs = Get-ChildItem -Path $ContentStoragePath -Directory

    Write-Info "Found $($contentDirs.Count) content directories"

    $totalImported = 0

    foreach ($contentDir in $contentDirs) {
        try {
            Import-ContentCeks -ContentDir $contentDir.FullName -RedisDb $db
            $totalImported++
        }
        catch {
            Write-Failure "Error importing $($contentDir.Name): $_"
        }
    }

    $redis.Close()

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Import completed successfully" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Total contents imported: $totalImported" -ForegroundColor Yellow
}

Main
