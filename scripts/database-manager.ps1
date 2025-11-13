#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Zarzadzanie baza danych NEXA
.DESCRIPTION
    Migracje, seedowanie danych, backup i restore
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('migrate', 'seed', 'reset', 'backup', 'restore')]
    [string]$Action = 'migrate',
    
    [Parameter()]
    [string]$MigrationName,
    
    [Parameter()]
    [string]$BackupFile
)

$ErrorActionPreference = "Stop"
$ProjectPath = "./src/DrmLicenseServer"

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

function Invoke-Migration {
    Write-Info "Wykonywanie migracji bazy danych..."
    
    Push-Location $ProjectPath
    
    try {
        $pendingMigrations = dotnet ef migrations list --no-build 2>&1 | Select-String "Pending"
        
        if ($pendingMigrations) {
            Write-Info "Znajdowano oczekujace migracje"
            dotnet ef database update --no-build
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "Migracje wykonane pomyslnie"
            } else {
                Write-Failure "Blad podczas wykonywania migracji"
                exit 1
            }
        } else {
            Write-Info "Baza danych jest aktualna"
        }
    }
    finally {
        Pop-Location
    }
}

function New-Migration {
    if (-not $MigrationName) {
        $MigrationName = Read-Host "Podaj nazwe migracji"
    }
    
    Write-Info "Tworzenie nowej migracji: $MigrationName"
    
    Push-Location $ProjectPath
    
    try {
        dotnet ef migrations add $MigrationName
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Migracja utworzona: $MigrationName"
            Write-Info "Wykonaj 'database-manager.ps1 migrate' aby zastosowac"
        } else {
            Write-Failure "Nie udalo sie utworzyc migracji"
            exit 1
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-Seed {
    Write-Info "Seedowanie bazy danych..."
    
    Push-Location $ProjectPath
    
    try {
        dotnet run --no-build -- --seed
        
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Dane testowe dodane"
        } else {
            Write-Info "Brak custom seed command - dodaj recznie"
        }
    }
    catch {
        Write-Info "Implementuj seed data w Program.cs:"
        Write-Host @"

if (args.Contains("--seed")) {
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    // Dodaj test users
    if (!context.Users.Any()) {
        var users = new[] {
            new User { Email = "free@test.com", SubscriptionPlan = "free" },
            new User { Email = "basic@test.com", SubscriptionPlan = "basic" },
            new User { Email = "pro@test.com", SubscriptionPlan = "pro" }
        };
        context.Users.AddRange(users);
        context.SaveChanges();
    }
}

"@ -ForegroundColor Gray
    }
    finally {
        Pop-Location
    }
}

function Reset-Database {
    Write-Info "Reset bazy danych..."
    
    $response = Read-Host "Czy na pewno chcesz usunac wszystkie dane? (tak/nie)"
    
    if ($response -ne 'tak') {
        Write-Info "Operacja anulowana"
        return
    }
    
    Push-Location $ProjectPath
    
    try {
        dotnet ef database drop --force --no-build
        
        dotnet ef database update --no-build
        
        Write-Success "Baza danych zresetowana"
        
        $seedResponse = Read-Host "Czy dodac dane testowe? (tak/nie)"
        if ($seedResponse -eq 'tak') {
            Invoke-Seed
        }
    }
    finally {
        Pop-Location
    }
}

function Backup-Database {
    if (-not $BackupFile) {
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $BackupFile = "./backups/nexa_backup_$timestamp.sql"
    }
    
    Write-Info "Tworzenie backupu: $BackupFile"
    
    $backupDir = Split-Path $BackupFile -Parent
    if (-not (Test-Path $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    }
    
    $appsettings = Get-Content "$ProjectPath/appsettings.json" | ConvertFrom-Json
    $connectionString = $appsettings.ConnectionStrings.DefaultConnection
    
    if ($connectionString -like "*Data Source=*") {
        $dbFile = $connectionString -replace '.*Data Source=([^;]+).*', '$1'
        $dbPath = Join-Path $ProjectPath $dbFile
        
        if (Test-Path $dbPath) {
            Copy-Item $dbPath $BackupFile -Force
            Write-Success "SQLite backup utworzony: $BackupFile"
        } else {
            Write-Failure "Plik bazy danych nie znaleziony: $dbPath"
        }
    }
    else {
        Write-Info "Dla PostgreSQL uzyj pg_dump:"
        Write-Host "  docker exec nexa-postgres pg_dump -U nexa_user nexa_drm > $BackupFile" -ForegroundColor Yellow
    }
}

function Restore-Database {
    if (-not $BackupFile) {
        $BackupFile = Read-Host "Podaj sciezke do pliku backup"
    }
    
    if (-not (Test-Path $BackupFile)) {
        Write-Failure "Plik backup nie istnieje: $BackupFile"
        exit 1
    }
    
    Write-Info "Przywracanie z backupu: $BackupFile"
    
    $response = Read-Host "Czy na pewno chcesz nadpisac obecna baze? (tak/nie)"
    
    if ($response -ne 'tak') {
        Write-Info "Operacja anulowana"
        return
    }
    
    $appsettings = Get-Content "$ProjectPath/appsettings.json" | ConvertFrom-Json
    $connectionString = $appsettings.ConnectionStrings.DefaultConnection
    
    if ($connectionString -like "*Data Source=*") {
        $dbFile = $connectionString -replace '.*Data Source=([^;]+).*', '$1'
        $dbPath = Join-Path $ProjectPath $dbFile
        
        Copy-Item $BackupFile $dbPath -Force
        Write-Success "SQLite przywrocony z backupu"
    }
    else {
        Write-Info "Dla PostgreSQL uzyj psql:"
        Write-Host "  docker exec -i nexa-postgres psql -U nexa_user nexa_drm < $BackupFile" -ForegroundColor Yellow
    }
}

switch ($Action) {
    'migrate' { Invoke-Migration }
    'seed' { Invoke-Seed }
    'reset' { Reset-Database }
    'backup' { Backup-Database }
    'restore' { Restore-Database }
}
