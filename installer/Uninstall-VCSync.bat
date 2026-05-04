@echo off
REM VC Sync Uninstaller - Version 2.1.0
setlocal enabledelayedexpansion

echo.
echo ====================================================
echo  VC Sync v2.1.0 - Uninstaller
echo ====================================================
echo.

REM Check for administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This uninstaller requires administrator privileges.
    echo Restarting with elevated permissions...
    powershell -Command "Start-Process cmd.exe -ArgumentList '/c \"%~f0\"' -Verb RunAs"
    exit /b
)

REM Define paths
set "InstallPath=C:\Program Files\VC Sync"
set "StartMenuPath=%APPDATA%\Microsoft\Windows\Start Menu\Programs\VC Sync"
set "DesktopPath=%USERPROFILE%\Desktop\VC Sync.lnk"

echo Stopping VC Sync application if running...
taskkill /IM VCSyncBackupApp.exe /F 2>nul

echo Removing installation directory...
if exist "%InstallPath%" (
    rmdir /s /q "%InstallPath%"
    echo [OK] Installation directory removed
) else (
    echo [INFO] Installation directory not found
)

echo Removing Start Menu shortcuts...
if exist "%StartMenuPath%" (
    rmdir /s /q "%StartMenuPath%"
    echo [OK] Start Menu shortcuts removed
)

echo Removing Desktop shortcut...
if exist "%DesktopPath%" (
    del /q "%DesktopPath%"
    echo [OK] Desktop shortcut removed
)

echo Removing registry entries...
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\VCSyncBackupApp" /f >nul 2>&1
if %errorLevel% equ 0 (
    echo [OK] Registry entries removed
)

echo.
echo ====================================================
echo  VC Sync has been successfully uninstalled
echo ====================================================
echo.
pause
