#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Przygotowanie contentu wideo dla NEXA
.DESCRIPTION
    Transkodowanie, segmentacja i szyfrowanie wideo
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InputFile,
    
    [Parameter()]
    [string]$OutputDir = "./content/storage",
    
    [Parameter()]
    [string]$ContentId,
    
    [Parameter()]
    [ValidateSet('480p', '720p', '1080p', '4k', 'all')]
    [string[]]$Qualities = @('480p', '720p'),
    
    [Parameter()]
    [switch]$SkipEncryption
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

function Test-Dependencies {
    $required = @('ffmpeg', 'ffprobe')
    $missing = @()
    
    foreach ($tool in $required) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            $missing += $tool
        }
    }
    
    if ($missing.Count -gt 0) {
        Write-Failure "Brakujace narzedzia: $($missing -join ', ')"
        Write-Info "Uruchom: .\install-tools.ps1"
        exit 1
    }
    
    $shakaPath = "./tools/shaka-packager/packager.exe"
    if (-not (Test-Path $shakaPath) -and -not $SkipEncryption) {
        Write-Failure "Shaka Packager nie znaleziony: $shakaPath"
        Write-Info "Uruchom: .\install-tools.ps1"
        exit 1
    }
    
    return $shakaPath
}

function Get-VideoInfo {
    param([string]$FilePath)
    
    Write-Info "Pobieranie informacji o wideo..."
    
    $info = ffprobe -v quiet -print_format json -show_format -show_streams $FilePath | ConvertFrom-Json
    
    $videoStream = $info.streams | Where-Object { $_.codec_type -eq 'video' } | Select-Object -First 1
    
    $fpsRational = $videoStream.r_frame_rate -split '/'
    $fps = [math]::Round([double]$fpsRational[0] / [double]$fpsRational[1], 2)
    
    return @{
        Duration = [math]::Round($info.format.duration, 2)
        Width = $videoStream.width
        Height = $videoStream.height
        Bitrate = [math]::Round($info.format.bit_rate / 1000000, 2)
        Codec = $videoStream.codec_name
        Fps = $fps  # DODANE
    }
}

function New-ContentId {
    return [System.Guid]::NewGuid().ToString()
}

function Get-QualitySettings {
    param([string]$Quality)
    
    $settings = @{
        '480p' = @{
            Width = 854
            Height = 480
            VideoBitrate = '1M'
            AudioBitrate = '128k'
            Profile = 'baseline'
        }
        '720p' = @{
            Width = 1280
            Height = 720
            VideoBitrate = '3M'
            AudioBitrate = '192k'
            Profile = 'main'
        }
        '1080p' = @{
            Width = 1920
            Height = 1080
            VideoBitrate = '5M'
            AudioBitrate = '256k'
            Profile = 'high'
        }
        '4k' = @{
            Width = 3840
            Height = 2160
            VideoBitrate = '15M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
    }
    
    return $settings[$Quality]
}

function Invoke-Transcode {
    param(
        [string]$InputFile,
        [string]$OutputFile,
        [hashtable]$Settings,
        [double]$Fps
    )
    
    Write-Info "Transkodowanie do $($Settings.Height)p..."
    
    $segmentDuration = 4
    $gopSize = [math]::Round($Fps * $segmentDuration)
    
    Write-Info "FPS: $Fps, GOP size: $gopSize (keyframe co ${segmentDuration}s)"
    
    $ffmpegArgs = @(
        '-i', $InputFile,
        '-c:v', 'h264_nvenc', # libx264 - CPU, h264_nvenc - NVIDIA GPU, h264_amf - AMD GPU, h264_qsv - Intel Quick Sync Video
        '-profile:v', $Settings.Profile,
        '-b:v', $Settings.VideoBitrate,
        '-maxrate', $Settings.VideoBitrate,
        '-bufsize', "$(([int]($Settings.VideoBitrate.TrimEnd('M')) * 2))M",
        '-vf', "scale=$($Settings.Width):$($Settings.Height)",
        '-g', $gopSize.ToString(),           
        '-keyint_min', $gopSize.ToString(),  
        '-sc_threshold', '0',                
        '-c:a', 'aac',
        '-b:a', $Settings.AudioBitrate,
        '-movflags', '+faststart',
        '-y',
        $OutputFile
    )
    
    & ffmpeg $ffmpegArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Transkodowanie zakonczone: $(Split-Path $OutputFile -Leaf)"
    } else {
        Write-Failure "Blad transkodowania"
        exit 1
    }
}

function Invoke-Segmentation {
    param(
        [Parameter(Mandatory)]
        [hashtable]$InputFiles,
        
        [Parameter(Mandatory)]
        [string]$OutputDir,
        
        [Parameter(Mandatory)]
        [string]$ContentId,
        
        [Parameter(Mandatory)]
        [string]$ShakaPath,
        
        [Parameter()]
        [switch]$SkipEncryption
    )
    
    Write-Info "Rozpoczynanie segmentacji dla wszystkich jakosci..."
    
    $inputStreams = @()
    $encryptionKeys = @()
    $encryptionMetadata = @{}
    
    foreach ($quality in $InputFiles.Keys) {
        $qualityDir = Join-Path $OutputDir $quality
        New-Item -ItemType Directory -Path $qualityDir -Force | Out-Null
        
        $filePath = $InputFiles[$quality]
        
        if (-not $SkipEncryption) {
            $cek = -join ((1..16) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
            $kid = (New-Guid).ToString('N')
            
            $keyFile = Join-Path $qualityDir "$quality.key"
            Set-Content -Path $keyFile -Value $cek -NoNewline
            
            Write-Info "[$quality] CEK: $cek"
            Write-Info "[$quality] KID: $kid"
            Write-Host "[$quality] Key file: $keyFile" -ForegroundColor Yellow
            
            $encryptionKeys += "label=$quality`:key_id=$kid`:key=$cek"
            
            $encryptionMetadata[$quality] = @{
                KeyId = $kid
                KeyFile = "$quality.key"
                Algorithm = 'AES-128-CTR'
            }
        }
        
        if (-not $SkipEncryption) {
            $inputStreams += "in=$filePath,stream=audio,init_segment=$qualityDir/init_audio.m4s,segment_template=$qualityDir/audio_`$Number$.m4s,drm_label=$quality"
            $inputStreams += "in=$filePath,stream=video,init_segment=$qualityDir/init_video.m4s,segment_template=$qualityDir/video_`$Number$.m4s,drm_label=$quality"
        } else {
            $inputStreams += "in=$filePath,stream=audio,init_segment=$qualityDir/init_audio.m4s,segment_template=$qualityDir/audio_`$Number$.m4s"
            $inputStreams += "in=$filePath,stream=video,init_segment=$qualityDir/init_video.m4s,segment_template=$qualityDir/video_`$Number$.m4s"
        }
    }
    
    $shakaArgs = $inputStreams
    $shakaArgs += '--segment_duration', '4'
    

    $shakaArgs += '--mpd_output', (Join-Path $OutputDir "manifest.mpd")

    $shakaArgs += '--generate_static_live_mpd=true'
    $shakaArgs += '--profile', 'on-demand'
    

    if (-not $SkipEncryption) {
        $keysArg = $encryptionKeys -join ','
        
        $shakaArgs += @(
            '--enable_raw_key_encryption',
            '--keys', $keysArg,
            '--clear_lead', '0'
        )
        
        $fullEncryptionMeta = @{
            ContentId = $ContentId
            SegmentDuration = 4
            Qualities = $encryptionMetadata
        }
        
        $metaFile = Join-Path $OutputDir "encryption.json"
        $fullEncryptionMeta | ConvertTo-Json -Depth 5 | Set-Content $metaFile
        
        Write-Success "Szyfrowanie zakonczone - kazda jakosc ma SWOJ WLASNY CEK!"
        Write-Info "WAZNE: Zapisz CEK-i w bezpiecznym miejscu (DRM Server database)!"
        
    } else {
        Write-Info "Szyfrowanie pominiete (--SkipEncryption)"
    }
    
    & $ShakaPath $shakaArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Segmentacja zakonczona: manifest.mpd"
    } else {
        Write-Failure "Blad segmentacji"
        exit 1
    }
}

function New-Thumbnail {
    param(
        [string]$InputFile,
        [string]$OutputFile,
        [double]$Duration
    )
    
    Write-Info "Generowanie miniaturki..."

    $timestamp = [math]::Round($Duration * 0.1, 2)
    
    $ffmpegArgs = @(
        '-ss', $timestamp.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        '-i', $InputFile,
        '-vframes', '1',
        '-q:v', '2',
        '-vf', 'scale=480:-1',
        '-y',
        $OutputFile
    )

    $ffmpegOutput = & ffmpeg $ffmpegArgs 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Miniaturka wygenerowana"
    } else {
        Write-Failure "Blad generowania miniaturki"
        Write-Host $ffmpegOutput -ForegroundColor Red
    }
}

function New-Metadata {
    param(
        [string]$OutputDir,
        [hashtable]$VideoInfo,
        [string[]]$AvailableQualities
    )
    
    Write-Info "Tworzenie metadanych..."
    
    $metadata = @{
        ContentId = $ContentId
        Title = [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
        Duration = $VideoInfo.Duration
        AvailableQualities = $AvailableQualities
        SourceResolution = "$($VideoInfo.Width)x$($VideoInfo.Height)"
        CreatedAt = (Get-Date -Format "o")
        ManifestUrl = "/content/$ContentId/manifest.mpd"
        ThumbnailUrl = "/content/$ContentId/thumbnail.jpg"
    }
    
    $metaFile = Join-Path $OutputDir "metadata.json"
    $metadata | ConvertTo-Json -Depth 5 | Set-Content $metaFile
    
    Write-Success "Metadata zapisane: $metaFile"
}

function Main {
    Write-Host @"
========================================
NEXA - Content Preparation Pipeline
========================================
"@ -ForegroundColor Cyan
    
    $shakaPath = Test-Dependencies
    
    if (-not (Test-Path $InputFile)) {
        Write-Failure "Plik nie istnieje: $InputFile"
        exit 1
    }
    
    if (-not $ContentId) {
        $ContentId = New-ContentId
    }
    
    Write-Info "Content ID: $ContentId"
    
    $contentDir = Join-Path $OutputDir $ContentId
    New-Item -ItemType Directory -Path $contentDir -Force | Out-Null

    $videoInfo = Get-VideoInfo -FilePath $InputFile
    
    Write-Info @"
        Video Info:
        Rozdzielczosc: $($videoInfo.Width)x$($videoInfo.Height)
        Dlugosc: $($videoInfo.Duration)s
        FPS: $($videoInfo.Fps)
        Bitrate: $($videoInfo.Bitrate) Mbps
        Kodek: $($videoInfo.Codec)
"@  
    if ($Qualities -contains 'all') {
        $Qualities = @('480p', '720p', '1080p', '4k')
    }
    
    $processedQualities = @()
    $tempFilePaths = @{}

    Write-Info "Rozpoczynanie transkodowania wszystkich jakosci..."
    foreach ($quality in $Qualities) {
        Write-Info "Transkodowanie: $quality"
        
        $settings = Get-QualitySettings -Quality $quality
        $tempFile = Join-Path $contentDir "temp_$quality.mp4"
        
        Invoke-Transcode -InputFile $InputFile -OutputFile $tempFile -Settings $settings -Fps $videoInfo.Fps
        
        $tempFilePaths[$quality] = $tempFile 
        $processedQualities += $quality
    }
    Write-Success "Transkodowanie ukonczone."

    $segmentParams = @{
        InputFiles = $tempFilePaths
        OutputDir  = $contentDir
        ContentId  = $ContentId
        ShakaPath  = $shakaPath
    }

    if ($SkipEncryption) {
        $segmentParams.Add('SkipEncryption', $true)
    }

    Invoke-Segmentation @segmentParams

    Write-Info "Sprzatanie plikow tymczasowych..."
    foreach ($tempFile in $tempFilePaths.Values) {
        Remove-Item $tempFile -Force
    }

    $thumbnailFile = Join-Path $contentDir "thumbnail.jpg"
    New-Thumbnail -InputFile $InputFile -OutputFile $thumbnailFile -Duration $videoInfo.Duration
    
    New-Metadata -OutputDir $contentDir -VideoInfo $videoInfo -AvailableQualities $processedQualities
    
    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Content przygotowany pomyslnie" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Content ID: $ContentId" -ForegroundColor Yellow
    Write-Host "Lokalizacja: $contentDir" -ForegroundColor Yellow
    Write-Host "Jakosci: $($processedQualities -join ', ')" -ForegroundColor Yellow
    
    
    if (-not $SkipEncryption) {
        Write-Host "`nWAZNE: Zaszyfruj i zapisz CEK-i w bazie danych DRM Server" -ForegroundColor Red
        Write-Host "Kazda jakosc ma SWOJ WLASNY klucz:" -ForegroundColor Yellow
        foreach ($quality in $processedQualities) {
            Write-Host "  - $quality`: $contentDir/$quality/$quality.key" -ForegroundColor Yellow
        }
        Write-Host "`nMetadane szyfrowania: $contentDir/encryption.json" -ForegroundColor Yellow
    }
}

Main