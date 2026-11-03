@echo off
title NEXUS - Setup Hardware RAM RGB Control
echo ======================================================================
echo  NEXUS Hardware RGB Integration - Corsair VENGEANCE RAM Setup
echo ======================================================================
echo.

:: Check for Administrator elevation
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [NEXUS] Requesting Administrator privileges to authorize PawnIO SMBus driver...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~dpnx0\"\"' -Verb RunAs"
    exit /b
)

echo [1/3] Authorizing PawnIO Kernel Driver for AMD SMBus Access...
sc config PawnIO start= demand >nul 2>&1
net start PawnIO >nul 2>&1

echo [2/3] Registering Background Elevated Task for OpenRGB Server...
set OPENRGB_EXE=C:\Users\Mabar\AppData\Local\NEXUS\tools\OpenRGB\OpenRGB Windows 64-bit\OpenRGB.exe

if not exist "%OPENRGB_EXE%" (
    echo [ERROR] OpenRGB executable not found at: %OPENRGB_EXE%
    pause
    exit /b 1
)

:: Register the scheduled task with highest privileges
set LAUNCHER_BAT=C:\Users\Mabar\AppData\Local\NEXUS\tools\OpenRGB\Start-OpenRGB-Server.bat
schtasks /Create /TN "NexusOpenRGBServer" /TR "\"%LAUNCHER_BAT%\"" /SC ONLOGON /RL HIGHEST /F

echo [3/3] Starting OpenRGB Hardware Server with Elevated Privileges...
schtasks /Run /TN "NexusOpenRGBServer"

echo.
echo ======================================================================
echo  [SUCCESS] OpenRGB Elevated Service Registered & Started!
echo  Port 6742 is now active with direct AMD SMBus access for RAM RGB.
echo ======================================================================
echo.
timeout /t 5
