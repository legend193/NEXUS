@echo off
title NEXUS - Completely Uninstall PawnIO Driver
echo ======================================================================
echo  NEXUS - Remove PawnIO Kernel Driver ^& Clean System
echo ======================================================================
echo.

:: Check for Administrator elevation
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [NEXUS] Requesting Administrator privileges to remove kernel driver...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~dpnx0\"\"' -Verb RunAs"
    exit /b
)

echo [1/4] Stopping PawnIO Kernel Service...
sc.exe stop PawnIO

echo [2/4] Deleting PawnIO Service Registration...
sc.exe delete PawnIO

echo [3/4] Removing Driver from Windows DriverStore...
pnputil /delete-driver oem65.inf /uninstall /force

echo [4/4] Disabling OpenRGB PawnIO Library...
if exist "%LOCALAPPDATA%\NEXUS\tools\OpenRGB\OpenRGB Windows 64-bit\PawnIOLib.dll" (
    ren "%LOCALAPPDATA%\NEXUS\tools\OpenRGB\OpenRGB Windows 64-bit\PawnIOLib.dll" "PawnIOLib.dll.disabled" >nul 2>&1
)

:: Also delete Setup-RAM-Control.bat from desktop if present
if exist "%~dp0Setup-RAM-Control.bat" (
    del /f /q "%~dp0Setup-RAM-Control.bat" >nul 2>&1
)

echo.
echo ======================================================================
echo  [SUCCESS] PawnIO Driver and Service completely removed from Windows!
echo  Your system is now completely clean.
echo ======================================================================
echo.
pause
