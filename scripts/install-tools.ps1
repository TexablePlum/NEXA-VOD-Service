$ErrorActionPreference = 'Stop'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NEXA - Instalator Narzędzi (FFmpeg)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Ścieżka relatywna do lokalizacji skryptu (NEXA/scripts -> NEXA/tools/ffmpeg)
$projectRoot = Split-Path -Parent $PSCommandPath
$targetPath = Join-Path $projectRoot "..\tools\ffmpeg"
$targetPath = [System.IO.Path]::GetFullPath($targetPath)

if (-not (Test-Path $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}

Write-Host "`n[1/3] Pobieranie FFmpeg (BtbN Builds)..." -ForegroundColor Cyan
$ffmpegUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
$zipPath = "$env:TEMP\ffmpeg.zip"
$extractPath = "$env:TEMP\ffmpeg-extract"

Invoke-WebRequest -Uri $ffmpegUrl -OutFile $zipPath

Write-Host "[2/3] Rozpakowywanie archiwum..." -ForegroundColor Cyan
if (Test-Path $extractPath) { Remove-Item $extractPath -Recurse -Force }
Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

Write-Host "[3/3] Instalacja w folderze tools..." -ForegroundColor Cyan
$ffmpegDir = Get-ChildItem -Path $extractPath -Directory | Select-Object -First 1
Copy-Item -Path "$($ffmpegDir.FullName)\bin\*" -Destination $targetPath -Recurse -Force

# Sprzątanie
Remove-Item $zipPath -Force
Remove-Item $extractPath -Recurse -Force

Write-Host "`n[OK] FFmpeg został pomyślnie zainstalowany w: $targetPath" -ForegroundColor Green
Write-Host "Skrypt prepare-content.ps1 automatycznie wykryje to narzędzie!" -ForegroundColor Yellow
