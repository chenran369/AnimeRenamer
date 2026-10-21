@echo off
setlocal
where powershell >nul 2>nul
if errorlevel 1 (
    echo PowerShell not found.
    pause
    exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)
echo.
pause
