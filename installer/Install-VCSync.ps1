# VC Sync Setup Script - Version 2.1.0
# This script installs VC Sync backup application

param(
    [string]$InstallPath = "C:\Program Files\VC Sync"
)

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-not $isAdmin) {
    Write-Host "This installer requires administrator privileges. Restarting with elevated permissions..."
    Start-Process powershell.exe -ArgumentList "-File `"$PSCommandPath`" -InstallPath `"$InstallPath`"" -Verb RunAs
    exit
}

function Write-Header {
    param([string]$Text)
    Write-Host "`n" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Text)
    Write-Host "✓ $Text" -ForegroundColor Green
}

function Write-Error {
    param([string]$Text)
    Write-Host "✗ $Text" -ForegroundColor Red
}

Write-Header "VC Sync v2.1.0 Installer"

# Create installation directory
Write-Host "Creating installation directory: $InstallPath"
if (Test-Path $InstallPath) {
    Write-Host "Installation directory already exists. Backing up old version..."
    $backupPath = "$InstallPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Rename-Item -Path $InstallPath -NewName $backupPath -ErrorAction SilentlyContinue
    Write-Success "Old version backed up to: $backupPath"
}

New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Write-Success "Installation directory created"

# Copy application files
Write-Host "Copying application files..."
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptPath "dist"

if (-not (Test-Path $sourceDir)) {
    Write-Error "Source directory not found: $sourceDir"
    exit 1
}

Copy-Item -Path "$sourceDir\*" -Destination $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
Write-Success "Application files copied"

# Create Start Menu shortcuts
Write-Host "Creating shortcuts..."
$startMenuPath = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs\VC Sync")
New-Item -ItemType Directory -Path $startMenuPath -Force | Out-Null

$wshShell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $startMenuPath "VC Sync.lnk"
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $InstallPath "VCSyncBackupApp.exe"
$shortcut.WorkingDirectory = $InstallPath
$shortcut.Description = "VC Sync - Backup application for Outline VPN servers"
$shortcut.Save()
Write-Success "Start Menu shortcut created"

# Create Desktop shortcut
$desktopPath = [System.Environment]::GetFolderPath("Desktop")
$desktopShortcut = Join-Path $desktopPath "VC Sync.lnk"
$shortcut = $wshShell.CreateShortcut($desktopShortcut)
$shortcut.TargetPath = Join-Path $InstallPath "VCSyncBackupApp.exe"
$shortcut.WorkingDirectory = $InstallPath
$shortcut.Description = "VC Sync - Backup application for Outline VPN servers"
$shortcut.Save()
Write-Success "Desktop shortcut created"

# Add to Add/Remove Programs
Write-Host "Registering application..."
$regPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VCSyncBackupApp"
New-Item -Path $regPath -Force | Out-Null
New-ItemProperty -Path $regPath -Name "DisplayName" -Value "VC Sync" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regPath -Name "DisplayVersion" -Value "2.1.0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regPath -Name "InstallLocation" -Value $InstallPath -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regPath -Name "UninstallString" -Value "powershell.exe -File `"$(Split-Path -Parent $MyInvocation.MyCommand.Path)\Uninstall-VCSync.ps1`"" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regPath -Name "Publisher" -Value "VChannel" -PropertyType String -Force | Out-Null
Write-Success "Application registered in Add/Remove Programs"

# Launch the application
Write-Header "Installation Complete!"
Write-Host "VC Sync has been successfully installed to: $InstallPath"
Write-Host "Launching application..."
Start-Sleep -Seconds 2

& (Join-Path $InstallPath "VCSyncBackupApp.exe")
