@echo off
setlocal
set "engine=%~dp0.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%engine%" (
  echo The project's Godot engine was not found. See docs\CAVE_LAB_TEST_GUIDE.md.
  pause
  exit /b 1
)
start "Thornwake Cave Test" "%engine%" --path "%~dp0." res://Game/Development/CaveTerrainLab.tscn
