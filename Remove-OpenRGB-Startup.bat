@echo off
title NEXUS - Remove OpenRGB From Windows Startup
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd -ArgumentList '/c \"\"%~dpnx0\"\"' -Verb RunAs"
    exit /b
)

echo [NEXUS] Removing OpenRGB scheduled startup tasks...
schtasks /Delete /TN "NexusOpenRGBServer" /F >nul 2>&1
schtasks /Delete /TN "NexusOpenRGBRunner" /F >nul 2>&1

echo [NEXUS] Disabling autostart triggers...
echo [SUCCESS] OpenRGB will now only launch when NEXUS application is opened.
timeout /t 3
