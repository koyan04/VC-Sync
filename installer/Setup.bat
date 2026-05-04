@echo off
REM VC Sync Setup Launcher - Version 2.1.0
setlocal enabledelayedexpansion

echo.
echo ====================================================
echo  VC Sync v2.1.0 - Setup Wizard
echo ====================================================
echo.

REM Check for administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo This setup wizard requires administrator privileges.
    echo Restarting with elevated permissions...
    powershell -Command "Start-Process cmd.exe -ArgumentList '/c \"%~f0\"' -Verb RunAs"
    exit /b
)

REM Get the directory where this script is located
set "ScriptDir=%~dp0"

REM Run the PowerShell installer
echo Launching VC Sync Setup Wizard...
echo.

powershell -ExecutionPolicy Bypass -File "%ScriptDir%Install-VCSync.ps1"

if %errorLevel% equ 0 (
    echo.
    echo Setup completed successfully!
) else (
    echo.
    echo Setup encountered an error. Please check the messages above.
    pause
)
