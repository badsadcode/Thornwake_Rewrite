"""Write a self-checking JOIN.cmd next to split build parts (25 MB parts for Tom's downloads).

Usage: python3 tools/make_join_cmd.py <parts dir> <zip name> <zip size> <exe size> [<pck size>]
The script checks every part's size before joining (a missing or half-downloaded part gave a cut-off Thornwake.exe,
whose packed game data is at its end: "missing pck file", build 27), checks the joined zip and the extracted exe, and
adds two fallback launchers (DirectX 12; simpler graphics for old or office PCs).
"""
import os
import sys

folder, zip_name, zip_size, exe_size = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
pck_size = sys.argv[5] if len(sys.argv) > 5 else None
parts = sorted((f for f in os.listdir(folder) if f.startswith(zip_name + ".part")), key=lambda f: int(f.rsplit("part", 1)[1]))
L = ["@echo off", "setlocal", 'cd /d "%~dp0"', f"echo Checking the {len(parts)} downloaded parts...", "set BAD=0"]
for n in parts:
    s = os.path.getsize(os.path.join(folder, n))
    L.append(f'if not exist "{n}" (echo   MISSING: {n} & set BAD=1) else (for %%A in ("{n}") do if not "%%~zA"=="{s}" '
             f'(echo   INCOMPLETE: {n} is %%~zA bytes, should be {s} - download it again & set BAD=1))')
out = zip_name.replace(".zip", "-Windows.zip")
L += ['if "%BAD%"=="1" (echo. & echo Some parts are missing or incomplete. Download them again into this folder and run JOIN.cmd again. & pause & exit /b 1)',
      f"echo All {len(parts)} parts OK. Joining...",
      "copy /b " + "+".join(parts) + f" {out} >nul",
      f'for %%A in ("{out}") do if not "%%~zA"=="{zip_size}" (echo Joined file has the wrong size: %%~zA bytes, should be {zip_size}. & pause & exit /b 1)',
      "echo Joined OK. Extracting (this takes a moment)...",
      f"tar -xf {out}",
      "if errorlevel 1 (echo Extracting failed. Right-click the .zip, choose Extract All, then run Thornwake.exe from the extracted folder. & pause & exit /b 1)",
      f'for %%A in ("Thornwake-Rewrite-Preview\\Thornwake.exe") do if not "%%~zA"=="{exe_size}" (echo Thornwake.exe has the wrong size: %%~zA bytes, should be {exe_size}. Antivirus may have changed or blocked it. & pause & exit /b 1)',
      *([f'if not exist "Thornwake-Rewrite-Preview\\Thornwake.pck" (echo Thornwake.pck is missing next to Thornwake.exe. Antivirus may have removed it. & pause & exit /b 1)',
         f'for %%A in ("Thornwake-Rewrite-Preview\\Thornwake.pck") do if not "%%~zA"=="{pck_size}" (echo Thornwake.pck has the wrong size: %%~zA bytes, should be {pck_size}. & pause & exit /b 1)'] if pck_size else []),
      '> "Thornwake-Rewrite-Preview\\Run (DirectX 12).cmd" echo @start "" "%%~dp0Thornwake.exe" --rendering-driver d3d12',
      '> "Thornwake-Rewrite-Preview\\Run (old or office PCs).cmd" echo @start "" "%%~dp0Thornwake.exe" --rendering-method gl_compatibility',
      "echo.",
      "echo Done. Open the folder Thornwake-Rewrite-Preview and run Thornwake.exe.",
      'echo If it does not start on this PC: try "Run (DirectX 12).cmd", then "Run (old or office PCs).cmd" (simpler graphics).',
      "pause"]
with open(os.path.join(folder, "JOIN.cmd"), "w", newline="") as f:
    f.write("\r\n".join(L) + "\r\n")
