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
    [ValidateSet('144p', '240p', '360p', '480p', '720p', '1080p', '1440p', '2160p', '4320p', '4k', '8k', 'all')]
    [string[]]$Qualities = @('480p', '720p'),

    [Parameter()]
    [switch]$SkipEncryption,

    [Parameter()]
    [string]$Title,

    [Parameter()]
    [string]$Description = "Brak opisu.",

    [Parameter()]
    [string]$ReleaseDate = $null,

    [Parameter()]
    [ValidateSet('free', 'basic', 'pro')]
    [string]$RequiredPlan = 'free',

    [Parameter()]
    [ValidateSet('h264_nvenc', 'libx264', 'h264_amf', 'h264_qsv')]
    [string]$Codec = 'h264_nvenc'
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
    # Dodaje pobrane ffmpeg do PATH, aby Get-Command go widział
    $localFfmpegPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\tools\ffmpeg"))
    if (Test-Path $localFfmpegPath) {
        if ($env:PATH -notmatch [regex]::Escape($localFfmpegPath)) {
            $env:PATH = "$localFfmpegPath;$env:PATH"
        }
    }

    $required = @('ffmpeg', 'ffprobe')
    $missing = @()
    
    foreach ($tool in $required) {
        if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
            $missing += $tool
        }
    }
    
    $shakaPath = "./tools/shaka-packager/packager.exe"
    $isShakaMissing = (-not (Test-Path $shakaPath) -and -not $SkipEncryption)

    if ($missing.Count -gt 0 -or $isShakaMissing) {
        Write-Info "Brak wymaganych narzędzi. Próba automatycznej instalacji..."
        try {
            $installScript = Join-Path $PSScriptRoot "install-tools.ps1"
            & $installScript
            
            # Po instalacji upewniamy się, że nowa ścieżka do ffmpeg jest w PATH
            if (Test-Path $localFfmpegPath) {
                if ($env:PATH -notmatch [regex]::Escape($localFfmpegPath)) {
                    $env:PATH = "$localFfmpegPath;$env:PATH"
                }
            }
        }
        catch {
            Write-Failure "Błąd podczas automatycznej instalacji narzędzi: $_"
        }

        # Ponowne sprawdzenie po instalacji
        $missing = @()
        foreach ($tool in $required) {
            if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
                $missing += $tool
            }
        }
        $isShakaMissing = (-not (Test-Path $shakaPath) -and -not $SkipEncryption)

        if ($missing.Count -gt 0 -or $isShakaMissing) {
            if ($missing.Count -gt 0) { Write-Failure "Nadal brakujące narzędzia: $($missing -join ', ')" }
            if ($isShakaMissing) { Write-Failure "Shaka Packager nadal nie znaleziony: $shakaPath" }
            Write-Failure "Automatyczna instalacja nie powiodła się."
            exit 1
        }
    }
    
    return $shakaPath
}

function Get-VideoInfo {
    param([string]$FilePath)
    
    Write-Info "Pobieranie informacji o wideo..."
    
    $info = ffprobe -v quiet -print_format json -show_format -show_streams $FilePath | ConvertFrom-Json
    
    $videoStream = $info.streams | Where-Object { $_.codec_type -eq 'video' } | Select-Object -First 1
    $audioStream = $info.streams | Where-Object { $_.codec_type -eq 'audio' } | Select-Object -First 1
    
    $fpsRational = $videoStream.r_frame_rate -split '/'
    $fps = [math]::Round([double]$fpsRational[0] / [double]$fpsRational[1], 2)
    
    return @{
        Duration = [math]::Round($info.format.duration, 2)
        Width = $videoStream.width
        Height = $videoStream.height
        Bitrate = [math]::Round($info.format.bit_rate / 1000000, 2)
        Codec = $videoStream.codec_name
        Fps = $fps
        HasAudio = ($null -ne $audioStream)
    }
}

function New-ContentId {
    return [System.Guid]::NewGuid().ToString()
}

function Get-QualitySettings {
    param([string]$Quality)

    $settings = @{
        '144p' = @{
            Width = 256
            Height = 144
            VideoBitrate = '200k'
            AudioBitrate = '64k'
            Profile = 'baseline'
        }
        '240p' = @{
            Width = 426
            Height = 240
            VideoBitrate = '400k'
            AudioBitrate = '96k'
            Profile = 'baseline'
        }
        '360p' = @{
            Width = 640
            Height = 360
            VideoBitrate = '750k'
            AudioBitrate = '128k'
            Profile = 'baseline'
        }
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
        '1440p' = @{
            Width = 2560
            Height = 1440
            VideoBitrate = '10M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
        '2160p' = @{
            Width = 3840
            Height = 2160
            VideoBitrate = '20M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
        '4k' = @{
            Width = 3840
            Height = 2160
            VideoBitrate = '20M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
        '4320p' = @{
            Width = 7680
            Height = 4320
            VideoBitrate = '50M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
        '8k' = @{
            Width = 7680
            Height = 4320
            VideoBitrate = '50M'
            AudioBitrate = '320k'
            Profile = 'high'
        }
    }

    return $settings[$Quality]
}

# Oblicza wartość bufsize na podstawie podanego bitrate
function Get-BufferSize {
    param([string]$Bitrate)

    # Bitrate w kilobitach: np. "800k"
    if ($Bitrate -match '^(\d+)k$') {
        $kbps = [int]$Matches[1]
        return "$($kbps * 2)k"
    }
    # Bitrate w megabitach: np. "2M"
    elseif ($Bitrate -match '^(\d+)M$') {
        $mbps = [int]$Matches[1]
        return "$($mbps * 2)M"
    }
    else {
        # Domyślna wartość, jeśli format jest inny niż oczekiwany
        return "2M"
    }
}

function Invoke-Transcode {
    param(
        [string]$InputFile,
        [string]$OutputFile,
        [hashtable]$Settings,
        [double]$Fps,
        [string]$Codec
    )

    Write-Info "Transkodowanie do $($Settings.Height)p..."

    $segmentDuration = 4
    $gopSize = [math]::Round($Fps * $segmentDuration)

    Write-Info "FPS: $Fps, GOP size: $gopSize (keyframe co ${segmentDuration}s)"

    $bufsize = Get-BufferSize -Bitrate $Settings.VideoBitrate

    $ffmpegArgs = @(
        '-i', $InputFile,
        '-c:v', $Codec, # Kodek wideo: h264_nvenc (NVIDIA GPU), libx264 (CPU), h264_amf (AMD GPU), h264_qsv (Intel Quick Sync)
        '-profile:v', $Settings.Profile,
        '-b:v', $Settings.VideoBitrate,
        '-maxrate', $Settings.VideoBitrate,
        '-bufsize', $bufsize,
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

     # Przekierowanie wyjścia, żeby nic nie trafiało do potoku
    & ffmpeg $ffmpegArgs 2>&1 | Out-Null

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
        [switch]$SkipEncryption,

        [Parameter()]
        [bool]$HasAudio = $true
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
            # Generuje CEK tylko w pamieci
            $cek = -join ((1..16) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
            $kid = (New-Guid).ToString('N')

            Write-Info "[$quality] CEK: [REDACTED - in memory only]"
            Write-Info "[$quality] KID: $kid"

            $encryptionKeys += "label=$quality`:key_id=$kid`:key=$cek"

            # Przechowuje metadane szyfrowania z CEK w pamieci
            $encryptionMetadata[$quality] = @{
                KeyId = $kid
                Cek = $cek  # Tylko w pamieci
                Algorithm = 'AES-128-CTR'
            }
        }

        if (-not $SkipEncryption) {
            if ($HasAudio) {
                $inputStreams += "in=$filePath,stream=audio,init_segment=$qualityDir/init_audio.m4s,segment_template=$qualityDir/audio_`$Number$.m4s,drm_label=$quality"
            }
            $inputStreams += "in=$filePath,stream=video,init_segment=$qualityDir/init_video.m4s,segment_template=$qualityDir/video_`$Number$.m4s,drm_label=$quality"
        } else {
            if ($HasAudio) {
                $inputStreams += "in=$filePath,stream=audio,init_segment=$qualityDir/init_audio.m4s,segment_template=$qualityDir/audio_`$Number$.m4s"
            }
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

        Write-Success "Szyfrowanie zakonczone - kazda jakosc ma SWOJ WLASNY CEK (in-memory only)!"
        Write-Info "SECURITY: CEK-i pozostaja w pamieci i zostana bezpiecznie przeslane do DRM Server"

    } else {
        Write-Info "Szyfrowanie pominiete (--SkipEncryption)"
    }

    # Przekierowanie wyjścia, żeby nic nie trafiało do potoku
    & $ShakaPath $shakaArgs 2>&1 | Out-Null

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Segmentacja zakonczona: manifest.mpd"
    } else {
        Write-Failure "Blad segmentacji"
        exit 1
    }

    # Zwraca metadane szyfrowania do wywołującego (upload-content.ps1)
    return $encryptionMetadata
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
        [string[]]$AvailableQualities,
        [string]$Title
    )

    Write-Info "Tworzenie metadanych..."

    # Używa podanego tytułu lub nazwy pliku jako domyślnej wartości
    $finalTitle = if ([string]::IsNullOrEmpty($Title)) {
        [System.IO.Path]::GetFileNameWithoutExtension($InputFile)
    } else {
        $Title
    }

    $metadata = @{
        ContentId = $ContentId
        Title = $finalTitle
        DurationSeconds = $VideoInfo.Duration
        AvailableQualities = $AvailableQualities
        SourceResolution = "$($VideoInfo.Width)x$($VideoInfo.Height)"
        CreatedAt = (Get-Date -Format "o")
        ManifestUrl = "/content/$ContentId/manifest.mpd"
        ThumbnailUrl = "/api/catalog/$ContentId/thumbnail.jpg"

        Description = $Description
        ReleaseDate = $ReleaseDate
        RequiredPlan = $RequiredPlan
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
        $Qualities = @('144p', '240p', '360p', '480p', '720p', '1080p', '1440p', '2160p', '4320p')
    }
    
    $processedQualities = @()
    $tempFilePaths = @{}

    Write-Info "Rozpoczynanie transkodowania wszystkich jakosci..."
    foreach ($quality in $Qualities) {
        Write-Info "Transkodowanie: $quality"

        $settings = Get-QualitySettings -Quality $quality
        $tempFile = Join-Path $contentDir "temp_$quality.mp4"

        # Używa [void] aby zapobiec przekazywaniu wyjścia do potoku
        [void](Invoke-Transcode -InputFile $InputFile -OutputFile $tempFile -Settings $settings -Fps $videoInfo.Fps -Codec $Codec)

        $tempFilePaths[$quality] = $tempFile
        $processedQualities += $quality
    }
    Write-Success "Transkodowanie ukonczone."

    $segmentParams = @{
        InputFiles = $tempFilePaths
        OutputDir  = $contentDir
        ContentId  = $ContentId
        ShakaPath  = $shakaPath
        HasAudio   = $videoInfo.HasAudio
    }

    if ($SkipEncryption) {
        $segmentParams.Add('SkipEncryption', $true)
    }

    # Zwraca metadane szyfrowania do wywołującego (upload-content.ps1)
    $encryptionData = Invoke-Segmentation @segmentParams

    Write-Info "Sprzatanie plikow tymczasowych..."
    foreach ($tempFile in $tempFilePaths.Values) {
        Remove-Item $tempFile -Force
    }

    $thumbnailFile = Join-Path $contentDir "thumbnail.jpg"
    [void](New-Thumbnail -InputFile $InputFile -OutputFile $thumbnailFile -Duration $videoInfo.Duration)

    [void](New-Metadata -OutputDir $contentDir -VideoInfo $videoInfo -AvailableQualities $processedQualities -Title $Title)

    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "Content przygotowany pomyslnie" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Content ID: $ContentId" -ForegroundColor Yellow
    Write-Host "Lokalizacja: $contentDir" -ForegroundColor Yellow
    Write-Host "Jakosci: $($processedQualities -join ', ')" -ForegroundColor Yellow

    # Zwraca metadane szyfrowania do wywołującego (upload-content.ps1)
    # CEK pozostaje w pamieci, nigdy nie jest zapisywany na dysku
    if (-not $SkipEncryption) {
        Write-Host "`nSECURITY: CEK-i zostana automatycznie przekazane do DRM Server (in-memory)" -ForegroundColor Green
    }

    # Zwraca uporzadkowany wynik do upload-content.ps1
    $returnValue = @{
        ContentId = $ContentId
    }

    if (-not $SkipEncryption -and $null -ne $encryptionData) {
        $returnValue.EncryptionData = $encryptionData
    }

    return $returnValue
}

# Uruchamia główną funkcję i zwraca wynik (do użycia przez upload-content.ps1)
Main