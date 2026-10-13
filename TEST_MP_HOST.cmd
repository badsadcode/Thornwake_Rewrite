@echo off
rem Multiplayer test (GAMEPLAY_DESIGN.md 14.0): host a game. Others join with TEST_MP_JOIN.cmd and this PC's address.
rem Usage: TEST_MP_HOST.cmd [more game arguments, e.g. a debug dump's Reproduce line to host that world]
setlocal
cd /d "%~dp0"
set "engine=%~dp0.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
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
rem The left half of a 1920 x 1080 screen, so a second window can join beside it.
start "Thornwake host" "%engine%" --path "%~dp0." res://Game/Rewrite/WorldLab.tscn -- --host --name=%USERNAME% --window=950x1000 --window-at=0,30 %*
