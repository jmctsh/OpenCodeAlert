@echo off
setlocal ENABLEDELAYEDEXPANSION

set "BASE=%~dp0"
pushd "%BASE%win-x64" >nul 2>&1

set "MODARG="
if exist "%CD%\mods\copilot\mod.yaml" (
  set "MODARG=Game.Mod=%CD%\mods\copilot"
) else if exist "%BASE%..\mods\copilot\mod.yaml" (
  set "MODARG=Game.Mod=%BASE%..\mods\copilot"
) else (
  set "MODARG=Game.Mod=copilot"
)

start "" "OpenRA.exe" %MODARG% %*

popd >nul 2>&1
endlocal
