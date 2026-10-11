# Benchmark

A built-in, repeatable performance test (Tom, 2026-10-11: "reproducible across different PCs, with consistent test
conditions, detailed performance measurements, and exported results"). Code: `Game/Rewrite/WorldLab.Benchmark.cs`;
scenes, profiles and test suites: `Content/benchmarks.json`.

## Running it
* **In the game:** Esc → **Benchmark** tab → pick a test and a quality → *Start benchmark*. Keep the window in front and
  don't touch the mouse. **Esc stops it.** When it's done, your settings come back and the results folder opens.
* **From the command line** (it quits when finished; exit code 0 = complete):
  `Thornwake.exe -- --bench=standard --bench-profile=high --window=1920x1080`
  * `--bench=<suite>`: `standard` (all scenes, ~15 min), `quick` (forest + flight, ~5 min), `trees` (tree range
    A/B: forest and lake with detailed trees 800 m, 1.6, 3.2 and 6.4 km), `treedetail` (forest and lake with
    full-detail trees 80 / 140 / 220 m × tree shadows simplified / detailed, ~10 min).
  * `--bench-profile=<id>`: `low`, `medium` (game defaults), `high`, `ultra` (Tom's dense-forest settings) or
    `current` (your own settings; the default).
  * `--bench-out=<folder>`: where the results go (default: the `bench` folder in the game's user folder).
  * `--window=1920x1080` / `--window-mode=windowed|borderless|fullscreen`: the window for this run.
  * `--legacy-multimesh`: the build 41 way of filling object batches (to measure the round 30 change).

**To compare PCs** use the same suite, the same quality profile and the same window size. Volunteers: please run
`standard` at `high`, in a 1920×1080 window or borderless fullscreen at 1920×1080, and send the whole results folder.

## What makes it repeatable
* Every scene is a fixed place, view and hour in **one world that generates identically on every PC** (Tom's 32 km
  test world, seed 42, dense forests; world generation is deterministic and covered by the headless tests).
* **Frozen conditions:** time stopped, weather simulation off, fixed cloud cover (0.35) with clouds not moving,
  V-Sync and the frame limit off.
* A **quality profile** sets every graphics setting (from the game defaults plus the profile's list), so two PCs
  draw exactly the same scene. `current` measures your own settings instead.
* Before measuring it waits until **every terrain tile is built and has stayed built for 2 s**, then warms up until
  **no shaders have compiled for 1 s** (a material compiles once, the first time it is seen; that stall is real but
  not part of the scene's steady cost; it is counted separately).

## What it measures
Per scene (results.json, summary.txt, and `frames-NN-scene.csv` with every frame):
| Metric | Meaning |
|---|---|
| fps_avg | frames ÷ seconds over the measured time |
| fps_1pct_low, fps_01pct_low | average FPS of the slowest 1 % / 0.1 % of frames (stutter) |
| frame_ms avg/p50/p95/p99/max | time between frames (what you feel) |
| gpu_ms | the GPU's own time to draw the frame (Godot's measured render time) |
| cpu_render_ms | the CPU's time preparing the frame for the GPU (culling, draw submission) |
| script_ms | game logic time (`_process`) |
| tile_commit_ms | main-thread time adding finished terrain tiles (streaming) |
| hitches | frames over 33 / 50 / 100 ms; the slowest tile commit |
| draw calls, primitives, objects | per frame (average; draw calls also max) |
| shader_compiles | pipeline compilations during the measurement |
| memory_mb | video memory (total, textures, buffers), static memory |
| content | what is loaded per tile level: 3D trees, impostors, tree cards, and per kind (near/far 3D trees, impostors, cards, plants, rocks) the batches, instances, triangles if all drawn, shadow casters |

**What things cost** (suites with `ablate`): after the main measurement, each kind of object is hidden for 4 s, in
every pass including shadows (far 3D trees, all 3D trees, impostors, tree cards, grass and plants, rocks and props,
everything); then the shadows of each kind alone (trees, rocks, plants, terrain), the soft shadow filter and the
shadow distance (build 43; build 42 hid kinds from the camera only, their shadows stayed); and each costly
effect that is on is switched off for 4 s (shadows, volumetric clouds, volumetric fog, screen-space reflections, water
reflections, SSAO, SSIL, sun shafts, light beams, ground fog, anti-aliasing). The report lists how many GPU and frame
milliseconds each one saved. Object kinds sit on their own render layers, so hiding them is instant (no rebuild);
effects that would need a terrain rebuild are not toggled.

**Machine** (in every report): OS, CPU and threads, RAM, GPU, vendor, driver, graphics API, renderer, monitor size and
refresh rate, window size and mode, game and Godot versions. Nothing else is collected and nothing is sent anywhere:
the files stay in the folder until someone shares them.

**Overall score:** the geometric mean of the scenes' average FPS (one slow scene can't hide behind a fast one).

## Adding scenes
`Content/benchmarks.json` → `scenarios`: a place (`at` [x, z] or `landmark` name), `view` [yaw, pitch, camera
distance], `height` above the ground, `hour`, `seconds`; or a flight: `path` [[x, z], …], `altitude`, `speed` (m/s).
Suites list scenes, optional `variants` (setting values to try in turn) and `ablate`. Keep existing scenes unchanged
so results stay comparable; add new ones with new ids and raise `version`.
