@echo off
cd /d "%~dp0"
if not exist "OpenRA.Server.exe" (
    echo Error: OpenRA.Server.exe not found in current directory.
    echo Current directory: %CD%
    pause
    exit /b 1
)
"OpenRA.Server.exe" Game.Mod=copilot
if %errorlevel% neq 0 pause
