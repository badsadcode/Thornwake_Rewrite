# Thornwake rewrite plan

Branch: `rewrite/main` in `badsadcode/Thornwake` (see [DECISIONS.md](DECISIONS.md) D1: creating a separate
`Thornwake-Rewrite` repository was refused by GitHub with *403 Resource not accessible by integration*).
Started 2026-10-04. This document is the **continuation index**: a new session should be able to pick up from
here alone. Design decisions with reasons live in [DECISIONS.md](DECISIONS.md); things only Tom can judge are in
[QUESTIONS_FOR_TOM.md](QUESTIONS_FOR_TOM.md).

Goal (Tom): rewrite Thornwake to be smarter and more efficient while keeping its full scope (world size,
features, look) and quality. World generation first. Bring over GangTheGAME's water quality (shallow-water
simulation on a 0.25 m grid + its water shader), adapted to Thornwake's world scale with chunking, LOD and
simulation only near players/active areas.

---

## 1. What exists (read 2026-10-04 at `main` = `5d86b4c`)

Engine: **Godot 4.5.1 .NET, C# (net8.0)**, Forward+ desktop renderer. Same engine as GangTheGAME (which is on
Godot 4.6), so water code can be ported closely rather than only "in idea".

### 1.1 The playable valley game (`Game/Main.tscn`) — the gameplay and visual quality reference
A finite 110 m valley (generators 1 and 2) with the full prototype loop: harvesting trees/stones/berries,
handcrafting and station crafting queues, modular building (roofs, walls, floors, stairs, finishes, snapping,
support), shovel earthworks on a 1 m terrain grid with protected areas, farming, ecology/regrowth, sandbox
rules, simple economy/market, bandit combat, quests/journal, hotbar, saves (schema 10 with migrations).
Low-poly stylized look with nearest-filtered pixel textures and Blender woodland trees (17 species, 3 LODs).
Its full engine smoke test passes in the cloud (`SMOKE_TEST_PASS`, rendered through software Vulkan).

### 1.2 Master Rework world foundation (development labs, not yet gameplay)
* **Macro generator A4** (`Game/Core/MacroWorldGenerator.cs`, `MacroMountainsV4.cs`): five profiles from
  512 km to 2,048 km (`Content/world_profiles.json`): continents, island groups, spline mountain belts with
  passes, domain-warped ridged relief. Stateless point queries in doubles.
* **Coordinates/streaming** (`WorldCoordinates.cs`, `RegionStreaming.cs`): 16 m `WorldRegion` addresses, double
  global positions with float-local rebasing, a bounded residency planner. Good ideas, kept.
* **Playable A4 lab** (`Game/Development/PlayableWorldLab*`): 256 m render tiles, 8 m vertex spacing within
  3 tiles, 128-triangle fans out to 20 tiles (~5 km), trimesh collision, a single worker thread, one commit per
  frame, static sea plane clipped on the CPU, Pine_A scatter.
* **Hydrology chain** (17 files, ~2,700 dense lines / 177 KB): coarse 128×128 drainage, regional corridors,
  inspection, reconciliation, adaptive topology 2/3/4/5, graph reuse, locality/spill attribution, receiver
  domains. Heavily certified, but it **renders no rivers or lakes**: the lab reports "zero supported
  presentation windows" and candidate lakes are outlines only.
* **Cave lab** (volumetric hybrid spike + a local water solver) and **macro map lab**.
* **Tests**: one console runner, ~4.85 M assertions. Many checks compare SHA-256 fingerprints of doubles
  recorded on Windows.

### 1.3 Design authority
`Persistent_Fantasy_Life_RPG_Game_Design_Bible_v0.1.md` (vision), `THORNWAKE_MASTER_REWORK_PLAN_v3.md`
(world → hydrology/caves → content tools → characters → camera → interaction → gameplay adaptation),
overridden by `THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md` (varied fantasy geography over realism; Azgaar as
reference; landforms; finite lakes with rate-limited river inflow; macro drainage as a skeleton refined
locally; water surface quality separate from hydrology; chunked, streamed collision; terrain/water/collision
must agree). The rewrite keeps all of that scope; it changes *how*, not *what*.

---

## 2. What is slow or messy (measured, not guessed)

Baseline numbers: section 5. Main problems:

1. **Point sampling is expensive.** `MacroWorldGenerator.Sample` loops over every land feature (up to 134),
   each doing 3 noise evaluations plus `Atan2` and 3 `Sin`, then every mountain belt's segments with a domain
   warp. **1.4–2.2 µs per sample.**
2. **Massive oversampling when building tiles.** A detailed 256 m tile is built from 256 separate 16 m
   sub-chunks (duplicated borders), and every vertex normal costs 4 extra full samples. That is 11,520 samples
   for 1,089 vertices, **~20 µs per vertex**, and only at **8 m spacing**. The valley game uses 1 m.
3. **CPU sea clipping samples the world per triangle** (up to 4 extra samples per sea triangle); coastal tiles
   are the slowest.
4. **Weak LOD.** Two tiers only (8 m near, 128-triangle fans far). Visible faceting and popping, 1,681 separate
   nodes/draw calls, trimesh colliders built from render meshes (32–36 ms commits measured on Tom's RTX 3080
   machine; hitches), one worker thread for all terrain.
5. **No player-scale relief.** The terrain spectrum stops at ~4 km wavelengths (hills) and ~0.7 km (mountain
   detail). Walking-scale ground is flat and featureless (see the captured lab screenshots in section 5).
6. **Hydrology is an over-engineered research chain with no visible output.** The world has no rivers, lakes
   or eroded valleys, which the Master plan requires.
7. **Not cross-platform deterministic.** Generation uses `Math.Sin/Cos/Exp/Atan2/Pow`, which come from the
   platform C library. On Linux the frozen A4 terrain and hydrology fingerprints recorded on Windows **fail**
   (`ACCEPTED_A4: frozen terrain balanced/-9`, `HYDROLOGY_INSPECTION: fixed inspection fingerprint`). A
   multiplayer world (design bible §48–60) needs identical generation on every machine.
8. **Two separate worlds.** Gameplay exists only in the 110 m valley; the large world is a viewer. Terrain
   authority, saves and streaming are duplicated between them.
9. **Readability.** Very dense one-line code, long experiment/checkpoint docs that override each other, and
   tests that cannot run on Linux.

---

## 3. Target architecture

```
WorldManifest (seed, profile, generator version)       -- immutable, saved with the world
   │
   ▼
WorldPlan  (built once, ~0.5 s, parallel, deterministic)
   ├─ Layout: continents, island groups, mountain belts (A4 shapes, reproduced)
   ├─ MacroRaster: 1024² float grids over the whole world (land, base relief, mountain envelope, …)
   ├─ Erosion: implicit stream-power (Braun–Willett) on the raster → dendritic valleys      [M2]
   └─ Hydrology: priority-flood + D8 accumulation → river polylines, lakes (spill levels)  [M2]
   │
   ▼
TerrainSampler  (pure function of (x, z); thread-safe; no per-call allocation)
   height = bicubic(macro raster) + player-scale detail (erosion-style fBm, ridged in mountains)
            + river channel / lake bed carving (distance to river splines)                 [M2]
   attributes: surface material, moisture, mountain/coast/river influence                  [M3]
   batch API: SampleGrid(x0, z0, spacing, n) — fills a tile with shared raster lookups
   │
   ▼
Streaming terrain (Godot adapter)                                                        [M4]
   quadtree chunk LOD (1 m near → 512 m far), skirts, worker pool, ms-budgeted commits,
   HeightMapShape3D collision only near observers (multi-observer ready), float rebasing
   │
   ▼
Water                                                                                   [M5–M6]
   Static bodies from hydrology: ocean (sea level + Gerstner swell), lakes (spill level),
   rivers (ribbon meshes with stage profile + flow direction)
   Shader: GangTheGAME water.gdshader ported (refraction, depth absorption, caustics, SSR + sky,
   shore/contact foam, wake texture) with per-body profiles (ocean/lake/river/pond/cave)
   Local shallow-water sim: GangTheGAME WaterField (virtual pipes, 0.25 m cells) in 16 m chunks,
   active only near players/edits/breaches; static bodies are its boundary conditions; chunks
   sleep when settled. WaterWaves (damped wave equation) for wakes in active chunks only.
   One queryable surface API for rendering, buoyancy and gameplay (Supplement §8.2).
```

Principles:
* **Engine-independent core** (`Game/WorldGen/`, namespace `Thornwake.WorldGen`): no Godot types, compiled by
  both the game and headless test/benchmark projects.
* **Deterministic everywhere**: generation uses only IEEE-exact operations (+ − × ÷ √, floor) and our own
  polynomial `DetMath` for sin/cos/exp/atan2. Fingerprint tests then hold on Windows and Linux.
* **Pay once, query cheaply**: expensive global structure is rasterized once per world; point queries are a
  bicubic lookup plus a fixed number of noise octaves.
* **Continuity by construction**: every layer is a continuous function of world position, so chunk edges
  can never become geography (Master plan §5).
* **Legacy stays runnable** until its replacement is proven: the valley game and the old labs are not deleted
  in early milestones; new scenes sit beside them.

---

## 4. Milestones (ordered)

Status legend: ✅ done · 🔨 in progress · ⏳ next · ☐ later

| # | Milestone | Status |
|---|---|---|
| M0 | Setup: branch, cloud toolchain (`tools/setup-cloud.sh`), baseline benchmarks, plan/decision docs | ✅ |
| M1 | Deterministic world-gen core: DetMath, hashing, gradient noise, profiles/manifest, A4 layout port, MacroRaster, TerrainSampler with player-scale detail, batch tile sampling, PNG map previews, tests and benchmarks | ✅ |
| M2 | Erosion and hydrology on the raster: priority-flood, D8 flow, Braun–Willett stream-power valleys, river network (IDs, discharge, monotonic stage), lakes with spill level and outlet, channel and lake carving into the sampler | ✅ (core; tuning continues) |
| M3 | Landforms and surface: material classification (grass/soil/sand/gravel/rock/snow), moisture, cliffs/plateaus/floodplain shaping, coast detail; data-driven landform parameters per profile | ☐ |
| M4 | 🔨 first version playable — Streaming terrain renderer: new `WorldLab.tscn`, quadtree LOD with skirts, worker pool, budgeted commits, HeightMapShape3D near-collision, rebasing, valley-game terrain look (shader port), frame-cost benchmarks | ☐ |
| M5 | 🔨 first version playable — Static water: ocean/lake/river meshes from hydrology; water shader ported from GangTheGAME with per-body profiles; Gerstner swell; shoreline foam; underwater fog | ☐ |
| M6 | Local shallow-water simulation: WaterField port on 0.25 m chunks, activation near players/edits, sleep/wake, coupling to static bodies (lake storage, river inflow, ocean boundary), wakes/waves, surface query API, benchmarks | ☐ |
| M7 | 🔨 first version playable — Vegetation and scatter on the streamed world: Blender trees and grass by density fields, LOD, MultiMesh per chunk, harvestable identities | ☐ |
| M8 | Terrain edit authority: sparse per-region deltas (earthworks port), persistence, collision/water refresh, protected areas | ☐ |
| M9 | Gameplay integration: valley gameplay (player, harvesting, crafting, building, farming, saves) running in the streamed world; legacy valley kept as a world type | ☐ |
| M10 | Caves / volumetric hybrid (Master §19–23) on top of the new terrain authority | ☐ |

Retirement: once M2 has replaced it, the old hydrology chain (17 files) and its fingerprint tests move to an
archive folder outside the build. The A4 lab is retired after M4 shows the same worlds better.

---

## 5. Benchmarks: before and after

Machine: cloud container, 4 cores, .NET 8.0.31, Release. Command:
`dotnet run -c Release --project Tests/Benchmarks -- [--legacy|--new] [--quick] [--out=file.json]`.
Single-threaded unless noted. Tom's desktop will be faster; compare ratios, not absolutes.

### Baseline: original A4 (Game/Core), 2026-10-04

| Measure | mountainous / 42 | vast-world / 7319 |
|---|---:|---|
| World construction | 14.8 ms | 1.0 ms |
| Point sample | 2.20 µs | 1.42 µs |
| Detailed 256 m tile (8 m spacing, 1,089 verts), inland | 23.7 ms | 21.3 ms |
| Detailed tile, coast (with CPU sea clip) | 18.9 ms | 30.8 ms |
| Outer 256 m fan tile | 1.4 ms | 1.2 ms |
| Cost per detailed vertex | 21.7 µs | 19.6 µs |
| Fill full 5 km view on one worker (estimate) | 3.4 s | 2.9 s |
| Coarse drainage 128² | 90 ms | 52 ms |
| Process peak working set (benchmark) | 115 MB | |

Rendered A4 lab (software Vulkan in the cloud, so FPS is meaningless here): walking-scale ground is flat
green with no relief below kilometre scale. Captures are not committed (artifacts are ignored).

### After M1 (Game/WorldGen), 2026-10-04, same machine

| Measure | mountainous / 42 | vast-world / 7319 | vs A4 |
|---|---:|---:|---|
| World plan build (layout + 3-channel raster, 4 cores) | 414 ms (1024²) | 818 ms (2048²) | new one-off cost |
| Plan memory (rasters) | 12 MB | 48 MB | |
| Point sample, macro only | 0.14 µs | 0.12 µs | **16× / 11× faster** |
| Point sample, full walking-scale detail (2 m) | ~1.2 µs | ~0.8 µs | similar cost, far more detail |
| 256 m tile at 8 m (legacy lab's near tile, 33²) | 0.75 ms | 0.74 ms | **~30× faster** (21–31 ms) |
| 64 m tile at 1 m (rewrite near tile, 65²) | 3.8 ms | 3.6 ms | — (A4 would be ~90 ms) |
| Cost per detailed vertex | 0.9 µs at 1 m | 0.86 µs | **~23× cheaper**, at 8× the resolution |
| 1 m tiles per second, all 4 cores | ~980 | ~975 | |
| Benchmark peak working set | 104 MB (both worlds) | | |

Container timings vary by ±15 % between runs. Fidelity to the accepted A4 shapes (test "Macro height tracks
A4"): 100 % land/sea agreement on sampled points; median height difference 0.2–0.3 m; 95th percentile 15–50 m
on relief of 2.9–4.2 km (A4's finest mountain octave is now walking-scale rock detail). Layout (continents,
islands, belt splines, feature IDs) is identical to A4.

| Target | Result |
|---|---|
| Point sample (full detail) ≤ 0.5 µs | ❌ 0.8–1.3 µs — detail octaves dominate; optimise in M4 if tile building needs it (macro part is 0.13 µs) |
| 64 m tile at 1 m ≤ 3 ms | ≈ 3.6–3.8 ms single core; ~1 ms effective on 4 workers |
| Plan build ≤ 1 s | ✅ 0.4–0.8 s |
| Plan memory ≤ 64 MB | ✅ 12–48 MB |
| Cross-platform fingerprints | ✅ on Linux; needs one run on Tom's Windows machine (Q3) |

---

## 6. Running things

* Cloud/Linux setup: `bash tools/setup-cloud.sh`, then `godot --headless --import --path .`
* Game build: `dotnet build Thornwake.csproj`
* New core tests: `dotnet run -c Release --project Tests/WorldGen` (`-- --filter=<text>`, `-- --record` after an intentional generator change)
* Map previews (PNG, no engine): `dotnet run -c Release --project Tests/WorldGen -- --maps=<dir> [--profile=mountainous]`
* Benchmarks: see section 5
* Original Core tests (Windows; fingerprints fail on Linux): `dotnet run --project Tests/Thornwake.Core.Tests.csproj`
* Render a scene in the cloud: `xvfb-run -a -s "-screen 0 1440x900x24" godot --path . <scene.tscn> -- <args>`
* Original valley game smoke: `godot --path . -- --smoke-test --capture=<abs png>`

---

## 7. Status and next steps

**2026-10-04, stretch 1.**
* M0 done: branch pushed, toolchain verified in the cloud (dotnet 8, Godot 4.5.1, headless import,
  software-Vulkan rendering), baselines recorded, docs written.
* M1 done: `Game/WorldGen/` — `DetMath` (fdlibm kernels, ≤ 4e-16 from libm), `Hashing`/`RandomStream`
  (A4-compatible), `Noise` (A4 value noise; gradient noise with derivatives; eroded, plain and ridged fBm with
  LOD-stable normalisation), `WorldProfile`/`TerrainStyle`/`WorldManifest`, `WorldLayout` (exact A4 layout port),
  `MacroRaster` (bicubic, parallel fill), `WorldPlan`, `TerrainSampler` (point + grid API, LOD knob).
  Tests in `Tests/WorldGen` (13 tests, 735,760 checks, ~6 s) and benchmarks in `Tests/Benchmarks`.
  The game is unaffected (new code is not yet used by any scene).

**Next: M2 (erosion + hydrology on the raster).** Concretely:
1. Add a `Height` raster channel = macro base height (lowland ramp + coastFade·relief) so erosion can work on
   real elevations; the sampler then reads eroded height instead of recomputing the base.
2. Priority-flood (Barnes 2014, ε-variant) over the land part of the raster; D8 receivers; topological order.
3. Braun–Willett implicit stream-power erosion (a few iterations, uplift = original macro height, K scaled by
   profile) plus thermal smoothing; store the eroded height. Keep the coast and A4 range crests recognisable.
4. Flow accumulation → river network above a discharge threshold → polylines with stable IDs, upstream/downstream
   links, monotonic bed profile; lakes = filled depressions above size/depth thresholds, with spill level and
   outlet river (finite storage metadata per Supplement §9.4).
5. Sampler: carve river channels (distance to segments via a spatial hash; width/depth from discharge;
   meanders from noise) and lake beds; return water level/kind at a point (for M5 water meshes).
6. Tests: rivers descend monotonically and end in ocean or lake; lakes sit at spill level; carving continuous
   across tiles; determinism fingerprints; maps showing river networks.

**2026-10-04, stretch 1 (continued): playable preview for Tom.** Tom asked for a playable EXE. Done ahead of the
strict milestone order, on top of M1/M2:
* M2 core: `Game/WorldGen/Hydrology.cs` (priority-flood, stream-power erosion, lakes, river reaches with
  monotonic surfaces, continuous junctions/lake levels, meanders, spatial index); sampler carves channels, banks
  and floodplains and reports water. 19 tests, 1.26 M checks.
* First playable 3D world: `Game/Rewrite/` — `WorldLab.tscn` (player with the valley character, walk/sprint/
  jump/swim/fly/noclip, orbit camera, HUD, Esc menu with teleports, presets/seeds, time of day),
  `WorldStreamer` (nested LOD rings, 64 m → 8 km tiles, ~20 km view, worker pool, budgeted commits, skirts,
  float rebasing, level-0 collision), `TileBuilder`, `Vegetation` (17 Blender species by elevation, willows
  by rivers), `Terrain.gdshader` (valley palette), `Water.gdshader` (GangTheGAME port).
* `Game/Launcher.tscn` is now the main scene: new world preview or the original valley game.
  `-- --smoke-test` still goes straight to the valley game; `-- --world-smoke=<dir>` runs the new world's
  screenshot test (`WORLD_SMOKE_PASS`).
* Windows export from Linux works (templates in ignored `.tools/templates`); verified the EXE boots under Wine
  (headless), generates and streams. Package: `Thornwake-Rewrite-Preview-Windows.zip` sent to Tom with
  `docs/PLAYTEST_REWRITE.md` as `PLAYTEST.txt`.

Known limitations of the preview: 2 m nearest terrain spacing (river shorelines show 2 m steps), trees only
within ~1 km (forest colour beyond), first-time tree model loads may hitch briefly, no shallow-water simulation
yet, rivers narrower than ~4 m are not represented, filled basins make some very flat plains.

**Next (unless Tom's feedback says otherwise):** finish M2 tuning from feedback; M3 slope-aligned gully detail
and materials; M6 local shallow-water simulation (GangTheGAME WaterField port) in active chunks near the player.

**2026-10-04, stretch 2: playtest round 1 fixes → build 2 sent to Tom.** Details and status per item:
`docs/PLAYTEST_FEEDBACK.md`; decisions D14–D16. Summary:
* Rivers: surfaces from detailed terrain, slope-limited valleys, natural meanders + wander, width variation,
  estuaries, deltas, funnel junctions, continuous all-segment blending (`Hydrology.RefineSurfaces`,
  `Hydrology.AddDeltas`, `TerrainSampler.Profile`). Tests: 19, ~2.8 M checks.
* Water lines: MSAA → FXAA in the new world.
* Visuals: far tree cards (`FarTree.gdshader`), ground cover (`GroundCover`: grass, stones, rocks, boulders,
  outcrops, bushes, stumps, logs), `RockMeshes`, `PropCatalog` placeholders, close-up terrain detail, beach band.
* Tooling: `WorldLab -- --world-shot=<png> --at=x,z [--view=yaw,pitch,zoom] [--fly-height=m] [--msaa=0|2|4]`
  renders one screenshot (prints `SCATTER` instance counts).
* Asset pipeline for Tom's upcoming batches: `docs/ASSET_SPEC.md`.

Costs after stretch 2 (cloud, 4 cores): plan build mountainous ~1.7 s, vast ~6 s; retained memory mountainous
~36 MB, vast ~205 MB (mostly river polylines + index: optimise with float attributes and world-scaled buckets);
point sample ~0.6 µs macro / ~1.5 µs full detail; 64 m tile at 1 m ~1 µs/vertex.

**Next:** wait for Tom's build-2 feedback and asset batches. Otherwise continue with: M6 local shallow-water
simulation near the player (GangTheGAME WaterField port), river data memory optimisation, slope-aligned gully
detail (M3), and level-0 terrain at 1 m spacing near the player (shoreline steps).

**2026-10-04, stretch 3: playtest round 2 → build 3.** Details per item: `docs/PLAYTEST_FEEDBACK.md` (round 2);
decisions D17–D21. Summary:
* Rivers: smoothed grid path, sine meanders + valley bends + wander, width variety, sea mouths reach the detailed
  coast, lake outlets start in the lake, meandering delta branches, LOD-aware channel edges (smooth far shorelines),
  crease-free bank profile (`Hydrology.Shape`, `TerrainSampler.Profile/ChannelProfile`).
* Water: visible looking upstream (depth fade fix), river current per water vertex with flow-mapped ripples and
  foam streaks, cleaner banks.
* Settings: `GameSettings` (persisted, generated menu: detail, view distances, effects, sky, camera/performance) and
  `WorldGenOptions` (New world tab: shape, terrain, rivers and lakes). Detail changes rebuild tiles in place.
* Atmosphere (`Atmosphere.cs`, `Sky.gdshader`, `SunShafts.gdshader`): clouds, matching cloud shadows (decal), sun
  shafts, glow, SSAO/SSIL/SSR, volumetric fog, haze, exposure, saturation, warm low sun.
* Denser grass (2×) and forests; far tree cards become crowns from above.
* Tooling: `WorldLab` switches `--hour=h --menu=tab --set=key=value --grid --debug-draw=n --dump-tile=x,z`;
  `Tests/WorldGen` modes `--window=…`, `--dump=…`, `--line=…`.

Costs after stretch 3 (cloud, 4 cores): plan build mountainous ~2.3 s, vast ~5.8 s; retained memory mountainous
~51 MB, vast ~314 MB (3.4 M river points at ~0.9-width spacing, 10 M index entries; transient allocation during the
vast build ~1.8 GB). This is now the top technical debt.

River data memory, same night: the segment index stores runs of consecutive segments per bucket (10.4 M → 0.23 M
entries in the vast world), width/depth/catchment are float32, and a Douglas–Peucker pass drops points that linear
interpolation reproduces. Vast world: retained 314 → 205 MB, peak working set 975 → 679 MB, 3.15 M points; point
samples unchanged or faster. Further savings would need float32 positions (API change) or coarser meander
sampling (visible facets on small creeks), so this is left here.

**M6 first version (night of 2026-10-04/05):** `Game/Simulation/LocalWater.cs` (decision D22) — GangTheGAME's
virtual-pipes shallow water at 0.5 m in a 128 m window following the player, dormant chunks, rivers/lakes/sea as
sinks, ground matching the rendered mesh; `LocalWaterView` draws it with the terrain water material; debug tools
G pour / J spring / H rain / K clear; swimming in simulated water; ground that held water stays dark for a few
minutes (`WetGround.gdshader`); puddles soak away (~1 mm/min); 8 headless tests (conservation, downhill, level
pool, sinks, moving window, real terrain, soaking, cost ~1–1.4 ms per step for a full window under rain).

Same night: level-0 tiles now have 1 m vertices (64 cells per 64 m tile; `TileBuilder.CellsFor`): smoother
shorelines and creek beds near the player (+~240 k triangles, ~6 ms per level-0 tile build on a worker); the water
simulation's ground follows. Forests: sharper forest/meadow patches; tree cards as dense as the trees they replace.

**M8 first step (night of 2026-10-05):** `TerrainEdits` (D23): hold V to dig / B to raise at the aimed point;
tiles, collision and the water simulation's ground follow; tests for the edit layer and for water flowing into a dug
channel. Also reeds and damp earth along river and lake banks.

Then rain weather (overcast sky, dim light, haze, falling rain when H rain is on), bare soil on edited ground,
and a rejected gully experiment (D24). **Build 5 (v0.9.4)** sent with everything above; 28 tests pass.

**Next:** Tom's build-5 feedback and asset batches first when they arrive. Then: M6 second step (optional local
river simulation once terrain edits exist; splashes; sound), M3 gully detail, M8 next steps (save edits, soil/rock material layers, tools).
