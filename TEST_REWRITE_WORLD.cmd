@echo off
setlocal
cd /d "%~dp0"
set "engine=%~dp0.tools\godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe"
if not exist "%engine%" (
  echo Project Godot engine not found. Run tools\bootstrap.ps1 first.
  pause
  exit /b 1
)
dotnet build Thornwake.csproj --no-restore
if errorlevel 1 (
  pause
  exit /b 1
)
start "Thornwake Rewrite World" "%engine%" --path "%~dp0." res://Game/Rewrite/WorldLab.tscn
