# Rewrite decisions

Each entry: the decision, why, and what would make us revisit it. Newest at the bottom. Tom can overrule any of
these; questions that genuinely need him are in [QUESTIONS_FOR_TOM.md](QUESTIONS_FOR_TOM.md).

## D1 — Work on `rewrite/main` in Thornwake, not a new repository (2026-10-04)
Tom asked for a new private repo `badsadcode/Thornwake-Rewrite`. The GitHub integration available to the cloud
session refused it: `POST /user/repos: 403 Resource not accessible by integration`. Per Tom's fallback, all work
goes to the branch `rewrite/main` in `badsadcode/Thornwake`. No other branch is touched (including `main` and the
session's default `claude/nifty-feynman-ebsbs1`). Revisit: if Tom creates the empty repo himself, the whole
branch history can be pushed there unchanged.

## D2 — Keep Godot 4.5.1 .NET and C# (2026-10-04)
Same engine family as GangTheGAME, all existing gameplay is C#, Tom's toolchain is pinned to 4.5.1. A rewrite in
another engine would throw away working gameplay and art integration. "Rewrite" here means new world, water and
streaming foundations under the existing game, in line with Master plan §2.1 ("preserve what works").

## D3 — Docs live in the existing lowercase `docs/` folder (2026-10-04)
Tom said `Docs/…`. The repo already has `docs/`; adding `Docs/` would create two folders that collide on
Windows (case-insensitive). On Windows, `Docs\REWRITE_PLAN.md` opens the same file anyway.

## D4 — New engine-independent world core in `Game/WorldGen/` beside the old `Game/Core/` (2026-10-04)
The old core keeps compiling, so the valley game and the old labs keep running at every commit. New code has
its own namespace (`Thornwake.WorldGen`) and its own tests (`Tests/WorldGen`) and benchmarks (`Tests/Benchmarks`).
Old code is archived only after its replacement is proven.

## D5 — Cross-platform deterministic math (2026-10-04)
Measured: the original frozen fingerprints fail on Linux because `Math.Sin/Cos/Exp/Atan2/Pow` differ between C
libraries. The new generator uses only IEEE-exact operations (`+ − × ÷`, `Math.Sqrt`, `Math.Floor`) plus our own
polynomial `DetMath` functions. .NET's JIT does not fuse multiply-adds on its own, so results are bit-identical
across x64/ARM64 Windows/Linux. Needed for multiplayer (design bible §48, §60) and for tests that pass in CI.
Revisit only if profiling shows DetMath is a bottleneck (it is not on the hot path; noise uses only + − ×).

## D6 — Reproduce A4 macro layout instead of inventing a new one (2026-10-04)
Tom has looked at and accepted A4's continents/islands/mountain belts. The new `WorldLayout` ports the A4
placement logic (same seeds, same RNG streams), so a given profile + seed yields recognisably the same
continents and ranges. The new generator adds what A4 lacks (erosion, rivers, lakes, walking-scale detail)
and is a **new generator version**: it does not claim to match A4 heights bit-for-bit, and old A4 manifests
remain readable by the old code.

## D7 — Rasterize global structure once per world (2026-10-04)
A4 recomputes continent and mountain-belt geometry at every sample (1.4–2.2 µs). The new plan rasterizes
smooth global fields (land influence, mountain envelope, eroded base relief) into 1024×1024 float grids over
the whole world (0.5–2 km cells, ~4 MB per channel), built in parallel once. Point queries are a bicubic lookup
plus noise octaves. Fine detail that a 0.5–2 km raster cannot hold (coast wiggles, ridged rock relief, hills)
stays procedural per sample, so there is no resolution cap at walking scale. Raster-scale erosion and hydrology
(M2) become simple grid algorithms instead of bounded certified searches.

## D8 — Replace the hydrology research chain with standard grid algorithms (2026-10-04)
Priority-flood depression filling (Barnes 2014), D8 flow and accumulation, implicit stream-power erosion
(Braun & Willett 2013) on the macro raster, then river polylines and lakes as vector features carved into the
sampler. These are linear or n·log n on the raster, well understood, and produce the visible rivers, lakes and
valleys the Master plan asks for. The old chain's useful policies (ocean outlets only, lakes have spill levels
and outlets, macro is a skeleton refined locally, finite lakes with rate-limited river inflow) are kept as rules.

## D9 — Rasterize A4's hills and broad mountain relief; drop its finest mountain octave (2026-10-04)
Measured: evaluating A4's hill noise, mountain warp and 3-octave ridged relief per sample made even the
macro-only sample cost 0.4 µs. All of it except the finest octave (≈ 0.16 mountain widths, 0.7–2.4 km) is smooth
at raster scale, so it now lives in the `Relief` and `Mountain` channels. The finest octave is replaced by the
new walking-scale ridged rock detail, which also removes A4's long value-noise crease arcs. Result: macro sample
0.13 µs, and heights still track A4 (median Δ 0.3 m).

## D10 — Noise amplitudes mean "typical height" (2026-10-04)
Raw Perlin-style gradient noise has a standard deviation of only ~0.18, and normalising fBm by the sum of
amplitudes shrinks it again, so the first detail pass was ~10× flatter than its parameters said. Gradient noise
is now scaled to ~0.4 std and fractal sums use RMS normalisation; skipped LOD octaves never renormalise, so
distant tiles equal near tiles minus small bumps.

## D11 — Detail is LOD-aware through a minimum wavelength (2026-10-04)
Every sampler call takes `minWavelength` (≈ 2 × vertex spacing). Octaves below it are skipped, the last one fades,
so far terrain costs fewer octaves and does not alias. Collision and gameplay use 2 m. Tests check that a 64 m LOD
stays within ~2 m mean of the 2 m terrain.

## D12 — Ship a playable preview before the renderer milestone is "finished" (2026-10-04)
Tom asked for an EXE to play. The preview builds on the finished M1/M2 core with first versions of M4 (streaming
LOD), M5 (water) and M7 (trees). Nested-ring LOD with skirts was chosen over a GPU clipmap because collision,
water and trees all come from the same CPU tile data and it is verifiable in the cloud.

## D13 — A launcher becomes the main scene (2026-10-04)
Both the new world and the original valley game must be reachable from one EXE. `Game/Launcher.tscn` offers both;
existing command-line tooling keeps working (`--smoke-test` forwards to the valley game). The `TEST_*.cmd`
scripts open their scenes directly and are unaffected; `TEST_REWRITE_WORLD.cmd` opens the new world.

## D14 — River surfaces from the detailed terrain along the final course (2026-10-04)
Tom's "canyon" screenshot: surfaces came from coarse 750 m raster cells while the terrain the player sees is the
bicubic raster plus detail; meandering courses climbed valley sides and the capped floodplain cut trenches. Surfaces
are now recomputed from the bicubic height along the smoothed, meandering course (network order, same junction/lake
rules), valley sides are slope-limited, and the terrain combines all nearby river segments continuously (per reach:
true nearest distance + softly averaged level/width/depth). Cost: point samples near rivers ~2× slower; tile building
almost unchanged; Vast world keeps ~200 MB of river data (optimisation noted for later).

## D15 — FXAA instead of MSAA in the new world (2026-10-04)
The water shader reads the screen and depth textures. With MSAA it produced lines along water triangle edges (Tom's
screenshots) and, in the cloud renderer, lost refraction entirely. The new world scene disables MSAA and uses FXAA.
The original valley game keeps the project's MSAA setting.

## D16 — Distant trees as shader-drawn cards (2026-10-04)
The woodland pack's lowest LOD is still 200–700 triangles. Beyond ~400 m trees are camera-facing quads with a
pixel-stepped silhouette in the species' crown colour (FarTree.gdshader), placed by the same forest rules with
coarser cells at greater distance; beyond ~6 km the terrain's forest tint carries the look.

## D17 — River courses: smoothed grid path + sine meanders (2026-10-04)
The D8 cell path (750–1000 m cells) is resampled every quarter cell and Gaussian smoothed (σ ≈ 0.8 cell, ends
fixed), then displaced perpendicular to it by three scales: a sine meander whose wavelength drifts ±40 % around
12 widths with amplitude ≤ 0.16 wavelengths (bend radius ≥ ~2 widths, as in real rivers); valley bends (2-octave
noise, ~700 m); km-scale wander. Points every ~0.9 widths (12–110 m). Delta branches use the same shaping.
Sea mouths continue 900 m straight out so they always meet the detailed coastline. All three scales and the width
variation are `TerrainStyle` parameters (New world tab).

## D18 — LOD-aware channel edge (2026-10-04)
On tiles coarser than level 0 the sampler replaces the channel's cross-section within ±1.5 vertex spacings of
the waterline by a straight ramp in distance (same slope inside and out). Linear interpolation of such a function
reproduces the waterline exactly, so distant river edges are smooth instead of saw-toothed. Channels narrower than
one spacing are drawn one spacing wide (continuous rather than dotted far away). Level 0 (collision) is unchanged.

## D19 — Settings as data (2026-10-04)
`GameSettings` holds every player setting as a `Setting` (key, section, range/options, default, tooltip,
"rebuild tiles" flag); the menu is generated from the list and values persist in `user://rewrite_settings.cfg`.
Tile builds capture a `DetailConfig` snapshot when scheduled (worker threads never read live settings); a detail or
distance change rebuilds tiles in place (old tiles stay until replaced). World-generation parameters are a separate
list (`WorldGenOptions`) applied to the preset profile and `TerrainStyle` at generation; the same preset, seed and
parameters always give the same world.

## D20 — Clouds via a global shader uniform; shadows via a decal (2026-10-04)
Sky clouds sample a tileable noise texture on a plane 1.6 km up. Cloud position and coverage are a *global* shader
uniform, so per-frame updates do not dirty the sky material (which would re-bake the radiance map every frame).
Cloud shadows: one large decal (3×3 copies of the same tile, so it can jump by a tile invisibly) offset along the sun
direction, which darkens everything opaque (terrain, trees, rocks, the player) under its cloud. Sun shafts: a
screen-space radial pass over unoccluded sky pixels (cheap; works with clouds, ridges and canopies).

## D21 — River current from the water surface gradient (2026-10-04)
Each water vertex gets a current vector: downhill along the water level's gradient on the tile grid, speed
√slope × 6 m/s (0.3–3.5), smoothed and extended to bank vertices. The shader advects ripples in two cross-faded
phases (a standard flow map) and smears them along the flow with a second tap; no rotated coordinates, because at
world positions of hundreds of km rotating them scrambles the pattern.

## D22 — Local water simulation: a moving 128 m window over the generated world (2026-10-05)
GangTheGAME's WaterField ("virtual pipes" shallow water, exact mass conservation) is ported engine-free as
`Game/Simulation/LocalWater.cs` and adapted to world scale: 0.5 m cells (0.25 m in GangTheGAME: 4× cheaper, and the
world's mesh is 2 m), a window of 8×8 chunks of 16 m that follows the player in whole chunks, a dormant store for wet
chunks that leave the window (resumed when they return), and only the wet bounding box stepped (~1.4 ms per step
with rain over the whole window). Ground is interpolated exactly like the rendered level-0 mesh. Generated rivers,
lakes and the sea are *sinks*: simulated water that reaches them joins them. The generated water bodies themselves
are not simulated (their levels come from hydrology, which is not a hydraulic equilibrium; simulating them would
make them drift away from the rendered rivers). Simulating rivers locally (e.g. damming) is a later step once
terrain editing (M8) exists.
