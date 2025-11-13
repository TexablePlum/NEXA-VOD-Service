#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Zarzadzanie kontenerami Docker dla projektu NEXA
.DESCRIPTION
    Skrypt pomocniczy do startowania, stopowania i monitorowania kontenerow
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('up', 'down', 'restart', 'logs', 'status', 'clean')]
    [string]$Action = 'status',
    
    [Parameter()]
    [switch]$Build,
    
    [Parameter()]
    [switch]$Detach
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

function Start-Containers {
    Write-Info "Uruchamianie kontenerow Docker..."
    
    $args = @('up')
    if ($Detach) { $args += '-d' }
    if ($Build) { $args += '--build' }
    
    docker-compose $args
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Kontenery uruchomione"
        Show-Status
    } else {
        Write-Failure "Nie udalo sie uruchomic kontenerow"
        exit 1
    }
}

function Stop-Containers {
    Write-Info "Zatrzymywanie kontenerow..."
    
    docker-compose down
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Kontenery zatrzymane"
    } else {
        Write-Failure "Nie udalo sie zatrzymac kontenerow"
        exit 1
    }
}

function Restart-Containers {
    Write-Info "Restartowanie kontenerow..."
    
    docker-compose restart
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "Kontenery zrestartowane"
        Show-Status
    } else {
        Write-Failure "Nie udalo sie zrestartowac kontenerow"
        exit 1
    }
}

function Show-Logs {
    Write-Info "Wyswietlanie logow..."
    
    docker-compose logs -f --tail=100
}

function Show-Status {
    Write-Info "Status kontenerow:"
    Write-Host ""
    
    docker-compose ps
    
    Write-Host ""
    Write-Info "Health status:"
    
    $containers = docker-compose ps -q
    foreach ($container in $containers) {
        $name = docker inspect --format='{{.Name}}' $container
        $health = docker inspect --format='{{.State.Health.Status}}' $container 2>$null
        
        if ($health) {
            $color = switch ($health) {
                'healthy' { 'Green' }
                'unhealthy' { 'Red' }
                default { 'Yellow' }
            }
            Write-Host "$name : $health" -ForegroundColor $color
        }
    }
}

function Clear-All {
    Write-Info "Czyszczenie wszystkich danych Docker..."
    
    $response = Read-Host "Czy na pewno chcesz usunac wszystkie dane? (tak/nie)"
    
    if ($response -eq 'tak') {
        docker-compose down -v --remove-orphans
        
        Write-Success "Wszystkie dane usuniete"
    } else {
        Write-Info "Operacja anulowana"
    }
}

switch ($Action) {
    'up' { Start-Containers }
    'down' { Stop-Containers }
    'restart' { Restart-Containers }
    'logs' { Show-Logs }
    'status' { Show-Status }
    'clean' { Clear-All }
}
