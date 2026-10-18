@echo off
rem Multiplayer test (GAMEPLAY_DESIGN.md 14 step 2, D138): bot players join a host, walk, hunt and build. No window: the
rem bots run in a game without drawing (headless) that builds the host's world for its ground. Its network log (BOTS
rem lines every 10 s) is in %%APPDATA%%\Thornwake\debug\net-...-client.log; the host's own log has its load every second.
rem Usage: TEST_MP_BOTS.cmd [address[:port]] [bots] [spread m] [seconds]
rem   default: this PC (127.0.0.1:7777), 11 bots (12 players with the host), together (spread 0), 600 s.
rem   spread 3000: each bot in a place of its own 3 km out (12 areas for the host to run).
setlocal
cd /d "%~dp0"
set "engine=%~dp0.tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe"
if not exist "%engine%" (
  echo Project Godot engine not found. Run tools\bootstrap.ps1 first.
  pause
  exit /b 1
)
set "target=%~1"
if "%target%"=="" set "target=127.0.0.1"
set "bots=%~2"
if "%bots%"=="" set "bots=11"
set "spread=%~3"
if "%spread%"=="" set "spread=0"
set "seconds=%~4"
if "%seconds%"=="" set "seconds=600"
rem (one of the connections is the carrier itself, standing at the host's start: bots-1 bots more)
set /a more=%bots%-1
start "Thornwake bots" "%engine%" --headless --path "%~dp0." res://Game/Rewrite/WorldLab.tscn -- --join=%target% --name=Bots --bots=%more% --bot-spread=%spread% --net-run=%seconds%
