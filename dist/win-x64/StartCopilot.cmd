@echo off
cd /d "%~dp0"
if not exist "OpenRA.exe" (
    echo Error: OpenRA.exe not found in current directory.
    echo Current directory: %CD%
    pause
    exit /b 1
)
"OpenRA.exe" Game.Mod=copilot
if %errorlevel% neq 0 pause
