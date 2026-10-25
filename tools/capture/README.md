# Capture helpers (atmosphere work)

* `watchrun.sh <log> <command…>` — runs a game command; if its log does not grow for 120 s, kills only the Godot or
  Thornwake processes started after it (HUNG). Exports often hang on exit after writing everything: check
  `artifacts/exportNN.log` ends with `DONE savepack` and the data folder has 186 files.
* `sheet.py out.jpg cols width label=path …` — a labelled contact sheet of screenshots.
* `fdiff.py dir out.jpg` — what changes between burst frames (shot.png, shot-1.png, …); prints the mean change per band.

Typical capture (Godot 4.7.2 console in `.tools/godot/…`):
`watchrun.sh out/log.txt $G --path . res://Game/Rewrite/WorldLab.tscn -- --window=1600x900 <Reproduce args> <test --set …> --world-shot=out/shot.png --shot-live --wait=4`
* Put test `--set=` AFTER a dump's Reproduce arguments (later wins). `--ray-dev=key=v` goes FIRST.
* Useful flags: `--extra-yaws=a,b` (+ `--extra-every=s` for bursts), `--face-sun`, `--shot-clock` (clock runs),
  `--wind=m/s`, `--dust-devil`, `--front=km,strength`, `--clouds=cumulus,storm,mackerel,lens`, `--cloud-split-test=3`
  (cloud opacity), `--cloud-dev=1,y` (deeper self-shadow test), `--debug-draw=1` (unshaded), `--dump`.
* Benchmarks with variants: `--bench=<suite> --bench-profile=current --bench-out=<dir>` (suites in Content/benchmarks.json:
  cloudcost, cloudlevels, cloudsoft, posterize, cloudfine, perfcheck, …).
* Tom's settings: `%APPDATA%/Thornwake/rewrite_settings.cfg`. Captures add new settings' default lines to it (nothing
  else) and `--dump` saves it; never change it without Tom's go and a backup with the game closed.
