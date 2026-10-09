@echo off
setlocal
cd /d "%~dp0"
set "engine=%~dp0.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%engine%" (
  echo Project Godot engine not found. See docs\PLAYABLE_WORLD_TEST_GUIDE.md.
  pause
  exit /b 1
)
dotnet build Thornwake.csproj --no-restore
if errorlevel 1 (
  pause
  exit /b 1
)
start "Thornwake Playable A4 Lab" "%engine%" --path "%~dp0." res://Game/Development/PlayableWorldLab.tscn
