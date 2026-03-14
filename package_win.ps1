$ErrorActionPreference = "Stop"

$distBase = "dist"
$distDir = "$distBase\win-x64"

# Ensure output directory exists and is clean
if (Test-Path $distBase) {
    Remove-Item $distBase -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir | Out-Null

Write-Host "Copying core files from bin directory..." -ForegroundColor Cyan

# We need to merge files from bin (managed dlls) and bin\win-x64 (runtime and exe)
# 1. Copy the win-x64 runtime/exe files first
$binWin64 = "bin\win-x64"
if (Test-Path $binWin64) {
    Copy-Item "$binWin64\*" -Destination $distDir -Recurse -Force
} else {
    Write-Warning "bin\win-x64 not found. The game might not launch."
}

# 2. Copy the managed DLLs from bin root, avoiding the nested win-x64 folder
# This fixes the missing OpenRA.Mods.Cnc.dll issue
$binRoot = "bin"
if (Test-Path $binRoot) {
    Get-ChildItem -Path $binRoot -File | ForEach-Object {
        Copy-Item $_.FullName -Destination $distDir -Force
    }
}

# 2. Copy Game Assets (Mods and Shaders)
# These are usually in the root, not inside bin
Write-Host "Copying assets (mods, glsl)..." -ForegroundColor Cyan
$modsDest = "$distDir\mods"
$glslDest = "$distDir\glsl"

if (-not (Test-Path $modsDest)) { New-Item -ItemType Directory -Path $modsDest | Out-Null }
if (-not (Test-Path $glslDest)) { New-Item -ItemType Directory -Path $glslDest | Out-Null }

# Copy all mods
if (Test-Path "mods") {
    Copy-Item "mods\*" -Destination $modsDest -Recurse
} else {
    Write-Warning "Directory 'mods' not found in root."
}

# Copy shaders
if (Test-Path "glsl") {
    Copy-Item "glsl\*" -Destination $glslDest -Recurse
} else {
    Write-Warning "Directory 'glsl' not found in root."
}

# 3. Copy Documentation
Write-Host "Copying licenses and docs..." -ForegroundColor Cyan
Copy-Item "AUTHORS", "COPYING", "socket-apis.md", "VERSION", "global mix database.dat" -Destination $distDir -ErrorAction SilentlyContinue

# 4. Create Launch Scripts
Write-Host "Creating launcher scripts..." -ForegroundColor Cyan

# Launch OpenRA (Generic) - similar to user's reference
$genericLauncher = @"
@echo off
setlocal ENABLEDELAYEDEXPANSION

set "BASE=%~dp0win-x64"
cd /d "%BASE%"

set "MODARG=Game.Mod=copilot"
if exist "%CD%\mods\copilot\mod.yaml" (
  set "MODARG=Game.Mod=%CD%\mods\copilot"
)

start "" "OpenRA.exe" %MODARG% %*
endlocal
"@
Set-Content -Path "$distBase\launch-openra.cmd" -Value $genericLauncher

Write-Host "------------------------------------------------------" -ForegroundColor Green
Write-Host "Build Complete (Copy Mode)!" -ForegroundColor Green
Write-Host "Location: $distBase" -ForegroundColor Green
Write-Host "Action: Run 'launch-openra.cmd' to play." -ForegroundColor Green
Write-Host "------------------------------------------------------" -ForegroundColor Green
