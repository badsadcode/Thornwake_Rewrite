@echo off
rem Multiplayer test (GAMEPLAY_DESIGN.md 14.0): join a host.
rem Usage: TEST_MP_JOIN.cmd [address[:port]] [name]   (default: this PC, 127.0.0.1:7777, name Guest)
setlocal
cd /d "%~dp0"
set "engine=%~dp0.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%engine%" (
  echo Project Godot engine not found. Run tools\bootstrap.ps1 first.
  pause
  exit /b 1
)
set "target=%~1"
if "%target%"=="" set "target=127.0.0.1"
set "who=%~2"
if "%who%"=="" set "who=Guest"
rem The right half of a 1920 x 1080 screen, beside the host's window.
start "Thornwake join" "%engine%" --path "%~dp0." res://Game/Rewrite/WorldLab.tscn -- --join=%target% --name=%who% --window=950x1000 --window-at=965,30
