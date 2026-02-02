@echo off
setlocal ENABLEDELAYEDEXPANSION

set "BASE=%~dp0"
cd /d "%BASE%"

set "MODARG="
if exist "%CD%\mods\copilot\mod.yaml" (
  set "MODARG=Game.Mod=%CD%\mods\copilot"
) else (
  set "MODARG=Game.Mod=copilot"
)

start "" "OpenRA.exe" %MODARG% %*
endlocal
