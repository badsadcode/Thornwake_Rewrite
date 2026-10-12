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

## Atmosphere captures (suite `atmosphere`)
For Atmosphere 2.0: the same places, times and weather every time, so each milestone can be compared side by side.
Ten scenes in `patch32`, Tom's test world with biome patches (every biome near the start; the 32 km test world has
no swamp or desert): temperate forest (morning walk, noon, night), plains evening, taiga morning, desert noon, swamp
dawn, beside the lake, coast at dusk, and a flight over a mountain ridge through the cloud deck.

* **Camera moves**: `turn` (360° on the spot), `walk` (along a path on the ground at walking pace), `fly` (along a
  path at an altitude). They are scripted, so a screen recording (ShadowPlay, Win+Alt+R) shows the same move each run.
* **Stills**: one at the start and eight along each move, `captures/NN-scene-KK.jpg` in the results folder, with the
  HUD hidden. Frame times are recorded as usual but are not the point (saving a still costs a frame).
* Scene options in `benchmarks.json`: `"world": "patch32"` (a world from `worlds`), `"move"`, `"settings"` (e.g.
  cloud cover for this scene only, applied after the frozen conditions).
* `--bench=atmosphere --bench-scenes=atmo-forest-noon,atmo-lake` runs only some scenes (also from the menu's suite).
* **A/B stills** (on in the `atmosphere` suite for M1: build 47's light vs light model 1): `--bench-compare=fx.light_model=0,1`
  (or `"compare": "fx.light_model=0,1"` in a suite) takes every
  still once per value, `captures/NN-scene-KK-light_model-0.jpg` and `…-1.jpg`: the same frame, the move and the clock
  paused while it renders 8 frames per value (TAA and fog history settle), then the setting goes back.
  `--bench-stills=N` changes the number of stills per scene; `--bench-settle=S` how long a scene may take to load
  (default 900 s, 1800 s for captures). Each A/B still logs the light state (`BENCH still …`).
* **Artefact shots (suite `artefacts`)**: fixed shots of the tracked artefacts — far water against the sky (sea and
  lake), glare around a low sun, sun shafts into the forest, sky seams (looking up into broken cloud, turning). Scene
  option `"face": "sun"` / `"sea"` turns the camera toward the sun or the most open water when the shot starts.
* `--clear-spot=x,z` (developer tool): the nearest small clearing in dense forest (no tree within 9.5 m), so a capture
  camera circling behind the player stays out of the crowns.
* Sites were found with `--find-sites` (developer tool: nearest dense forest, swamp, taiga, desert, plains, ridge and
  coast around the player).
