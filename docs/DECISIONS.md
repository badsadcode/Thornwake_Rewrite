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

## D23 — Terrain edits as a sparse 1 m offset layer read by the sampler (2026-10-05)
`TerrainEdits` stores player changes as height offsets on a 1 m vertex grid in 32 m chunks (bilinear), consulted at
the end of `TerrainSampler.Sample`, so every consumer (tile meshes, collision, scatter, the water simulation's
ground) sees the same edited ground with no special cases; an unedited world pays one empty-dictionary check and is
bit-identical to the generator (fingerprints unchanged). Chunks are copy-on-write, so tile builders on worker
threads never see a half-applied edit. An edit rebuilds the tiles it touches at levels 0–2 (old tiles stay until
replaced) and reloads the water simulation's ground there. Ground raised out of a river or lake is dry; lowered
ground does not fill by itself (the local water simulation does that). Not yet: saving edits, material layers
(soil/rock), tools and costs (M9 gameplay).

## D24 — Slope-aligned gully noise: tried and rejected (2026-10-05)
Tried "erosion noise" gullies (cosines across the local slope direction around jittered lattice points, two
octaves, 90 m and 32 m) in the sampler. With the slope direction taken from the eroded macro raster plus the broad
rolling hills, it is nearly constant over hundreds of metres, so the grooves came out as long straight parallel
scratches; at a strength where they did not, they were invisible next to the ridged mountain detail. Reverted.
A convincing version needs the slope of the finer terrain itself (iterating octaves on their own gradients), which
costs several extra noise evaluations per sample; revisit with M3 materials if hillsides still look too smooth.

## D25 — Build 5 generation kept reproducible (2026-10-05)
Tom asked to keep the build-5 generation as a fallback before changing it. Build 5 is commit `cdb2e31` on
`rewrite/main`; in addition every generation change since is a `TerrainStyle` parameter whose zero value restores
the build-5 behaviour (bank variation, shore slope), exposed as **New world → "Build 5 terrain (for comparison)"**. A
test (`Build 5 terrain still reproducible`) checks the build-5 terrain fingerprints bit for bit
(`Tests/WorldGen/Fixtures/terrain-build5.json`). Future generation changes follow the same rule.

## D26 — Natural river banks without touching the course (2026-10-05)
The centreline stays smooth. Each bank's waterline moves by its own offset along the reach (distance along the
reach and side of the river are now part of every river hit): a slow ±30 % widening/narrowing, a wobble of a few
metres with a finer ripple, and bulges (shallow shelves, wading depth) or cuts every ~100 m. The offset moves the
channel edge and bank only and fades across the bank, so the valley floor keeps its smooth shape. Noise scales come
from one width per reach (the local width drifts slightly; at tens of km along a reach that jumped the noise).
Terrain shader: bank strip in patches of mud, pale sand/gravel and damp grass.

## D27 — Rivers, lakes and the sea feed the local water simulation (2026-10-05)
Generated water cells were pure sinks, so ground dug next to a river stayed dry (Tom). They are now fixed-level
reservoirs: water flows out of them into lower non-water neighbours and is counted as drawn; runoff into them is
absorbed as before. Only water cells next to lower dry ground ("feeders", usually none until the player digs) are
stepped, so an untouched world costs nothing extra.

## D28 — Steeper shores (2026-10-05)
With the macro ramp alone, the sea stayed ankle-deep for hundreds of metres and beaches were wide flats (Tom: shallow
part too large, waves stretched). The sampler estimates the distance to the detailed coastline (land influence over
its gradient) and applies a beach profile: the sea floor deepens at ~3.5 % toward ~25 m, the land rises at ~2.5 %
toward ~3.5 m; beyond, the macro shape. Parameter `ShoreSlope` (New world → Shore steepness; 0 = build 5).

## D29 — Trees, rocks and logs are world objects, not graphics options (2026-10-05)
Tom: trees (and logs, rocks, other objects) will matter for gameplay, so what exists must not depend on a graphics
setting. Forest cover and density, rocks and props moved from Settings to the world's parameters (`TerrainStyle`,
New world → Forests and objects), fixed at generation. Settings keep only draw distances (detailed trees, tree cards,
small objects) and decorative grass. Every object already has a stable identity: its kind plus the world-aligned cell
it was placed on (deterministic from seed and parameters). Harvesting, felling or planting (M9) will store changes
per chunk against those identities, the same way terrain edits overlay the generated ground.

## D30 — Terrain tools (2026-10-05)
`TerrainSculpt` (engine-free, tested) applies one tool use to the edit layer: modes dig/raise/flatten/smooth, shapes
round (soft), round (hard), square and directional (a capsule along the aiming ray: dig into a slope, build out
from it). Each use is limited by the tool's strength, by ground hardness (mountains and steep ground are rock: soft
tools reach ~10 %, the pickaxe ~90 %) and by a volume budget (the soil bag, 1.5 m³: digging fills it, filling
empties it). `Toolbox` holds the tools (1 hands, 2 shovel, 3 pickaxe, 4 hoe, 5 terraform dev tool with adjustable
mode, shape, radius and strength, no limits), reach checks and a projected preview ring coloured by mode (red out of
reach). Objects (trees, grass, rocks) are on render layer 2 so previews project onto the ground only.
Limits now: 1 m edit grid (small tools touch few vertices), edits not saved yet.

## D31 — Caves need a volumetric layer (planned, M10) (2026-10-05)
Tom needs player-dug caves and generated natural cave systems. The surface stays a height field (fast, LOD to the
horizon); caves will be a separate volumetric layer only where needed: a signed-distance field in sparse chunks
(e.g. 0.5 m voxels in 16 m chunks) combining generated cave networks (worm/tunnel splines and chambers placed
deterministically per region, more in karst/mountain areas) and player carving, meshed with dual contouring /
marching cubes near the player, with openings cut into the height field where a cave meets the surface. The same
`TerrainSculpt` tool interface will carve it (the directional dig becomes a true tunnel there).

## D32 — River mouths blend into sea and lake beds (2026-10-05)
Tom: a hard step where rivers enter the sea or a lake. Two causes: raised banks (levees) continued along estuaries,
standing as dykes between river and sea; and the channel bed ended abruptly against a shallower sea/lake floor.
Now (style `MouthBlend`, New world → Terrain version "Preview 7" restores the old shape): levees fade out over the
river's last stretch; the channel continues as a groove into the sea floor (fading over the ~880 m continuation) or
the lake bed; samples near a mouth carry a `Plume` value so the water shader tints the sea/lake with the river's
greener, siltier water.

## D33 — Ocean waves shared by shader and gameplay (2026-10-05)
Boats are planned (Valheim / Sea of Thieves level), so waves must be queryable. `OceanWaves` (engine-free) defines
6 Gerstner waves from a sea state 0..1 (calm ripples of a few cm … storm swell of ~80 m wavelength and ~5 m crests)
and the wind direction; the water shader evaluates the same waves from the same uniforms and the same clock (global
`ocean_time`). `Height(x, z, t)` inverts the horizontal Gerstner motion (tested to < 1 mm). Waves shrink in water
shallower than ~12 % of their wavelength; lakes carry only the short waves; coarse distant tiles drop waves shorter
than a few vertex spacings. Sea state: Settings → Sky, plus up to +0.45 while it rains. Swimming follows the waves.
Shore: the sea's water mesh reaches ~14 m under the beach and rises and falls every ~9 s (swash), with foam at the
moving waterline and breaker bands rolling in over the shallows; whitecaps on crests in rough seas.
Next for boats: a buoyancy helper sampling `Height` at hull points; wave-aware sea floor (deeper water offshore
already from D28); later spectral waves (FFT) if Gerstner sums look too regular at sea.

## D34 — Digging by a river: one water surface (2026-10-05)
The river's tile water sheet was stretched over dug ground next to it while the local simulation drew the dug water
at its own level: overlapping sheets with gaps. Dug vertices are now left to the simulation, and the simulation also
draws river/lake cells next to its water at their surface, so the two meet.

## D35 — World types are size-independent recipes (2026-10-05)
Tom: pick world size, world type, seed and optional parameters (Auto by default), see a 2D map, then generate —
inspired by Azgaar's Fantasy Map Generator, no Azgaar code. A `WorldRecipe` anchors each type on a tuned preset at
its native size and returns exactly that preset there with every choice on Auto (existing worlds unchanged; tested
by digest). At other sizes counts follow coverage (continents and islands ∝ size^0.8, radii adjusted so the land
share stays in range — tested at 128 km / native / 4096 km); mainland types keep one landmass; mountains narrow on
small worlds. Overrides (continents, islands, ranges, size/roughness/height/width multipliers) win over Auto.
Compromise: the presets' land share (13–19 %) is lower than Tom's suggested targets; raising it would change every
existing world, so *Continent size ×* is the knob for now. Details: WORLD_CREATION.md.

## D36 — World types as data; player-made types (2026-10-05)
A world type is a JSON definition (id, name, description, mainland flag, full `WorldProfile`, starting parameter
values). The creation screen saves the current setup as a new type in `user://world_types/`, loaded at startup and
listed after the built-in types; files are hand-editable for deeper tweaks and validated on load (broken files are
skipped). Landform templates (the idea of Azgaar's heightmap templates, own code): an optional `Landforms` step list
in the profile (Hills, Raise, Lower, Basin, Range, Trough, Strait, Islands, Mask), all positions and sizes as world
fractions with seed-picked ranges, compiled per world into blobs and smoothly bending lines (domain-warped, coast
noise, group-max at line joints, cuts never deepen open sea) and added to the macro land/relief/mountain channels
before erosion and rivers. `Landforms` is omitted from the profile JSON when null, so every existing digest and world
is unchanged; the profile may then have 0 continents. Five built-in template types (inland sea, shattered lands,
rift continent, twin continents, great peninsula). Next: a world type editor with a live map.

## D37 — Rain water persists; cosmetic puddles (2026-10-05)
Tom: rain water vanished the moment rain stopped, but unlimited rain must not flood the world. The simulation now
has soil with limited capacity: infiltration falls as the soil saturates (∝ (1 − saturation)²) down to a small
floor, saturated soil drains slowly, and standing water evaporates slowly. A dug pond fills during rain and stays
for a long time; puddles on open ground soak away over minutes (tested: 2.3 of 3 m³ left after 10 min). Wetness
edges are blurred and noise-thresholded instead of 0.5 m squares. Separately, a cosmetic `rain_wetness` global
darkens and glosses all terrain and draws non-simulated puddles in hollows, fading after the rain. Reservoirs (river
and lake water) feed only edited cells, so they never leak into untouched banks.

## D38 — Natural world types: heightmap templates (2026-10-05)
Tom: land shapes too round; Vast continents best; Azgaar's more interesting — new versions, existing unchanged,
adapted from how Azgaar does it. Read his heightmap generator (MIT, open source) for the method; wrote Thornwake's
own: 160×160 hex grid, heights 0–100 (sea 20), steps Hill/Pit (breadth-first spread, child = parent^0.985 ×
U(0.9,1.1) − 0.35: the subtraction gives a finite reach so many hills' tails do not add up to land everywhere),
Range/Trough (greedy wandering paths, sideways spread h^0.86 − 1, downhill spurs), Strait, Mask, Add, Multiply,
Smooth, Invert, fractional counts; a final smoothing pass (single-cell roughness read as mossy coasts). Sampled
bilinearly with a domain warp and a light fractal at sub-grid scales into the macro land/relief/mountain channels
(land = (h − 20)/22 above the sea, (h − 20)/16 below; uplands from h; mountains from h > 55 × ruggedness). Own
templates for all world types (11 natural types), tuned by maps at 512 km over several seeds. `Profile.Heightmap`
is omitted from the digest JSON when null, so every existing world is unchanged.

## D39 — Estuaries and walkable banks (terrain revision 9) (2026-10-05)
Tom: a step where rivers meet the sea/lakes; water walls by dug/spring cells. Found by a 1 m step probe at mouths:
bulging banks squeezed the bank into cliffs (up to ~4× steeper), and per-segment along jumps drew streaks and ribs.
Revision 9 (`TerrainStyle.Revision`; Terrain version "Build 8" keeps 8, bit-exact test): estuary over the last
8 widths + 200 m before the coastline/lake (channel ×1.7 wider, bank ×3 wider and 60 % lower, footprint unchanged
so spatial-index hits stay complete); bulges as shallow ledges, only slow widening and cuts shift the bank (fade
grows with the shift); beach shift fades behind the bank; along blended between equally near segments with a
tight cutoff (a wide one pulled in far meander loops at spatial-grid borders: square terraces); sea groove matches
the estuary. Water: tile sheets skip squares at dug ground below the water; the simulation draws dug river cells
and keeps undrawn river cells at the surface (no curtains); river cell levels are corner averages.

## Planned — Biomes
Tom: many biomes, each with its own terrain, plants and atmosphere. Plan: a biome map from climate (temperature from
latitude/elevation, moisture from distance to sea and rain shadow) drives `TerrainStyle` per region (blended at
borders), vegetation catalogues, ground materials, water colour and atmosphere presets.
