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

## D40 — Small worlds keep real-size landforms (2026-10-05)
Tom: small worlds look smooth and blobby, big ones detailed. Recipes scaled mountain width with (radius ratio)^0.7,
height down to 35 %, broad hills with size^0.6: at 32 km, 2 km-wide cones and 1.5 km round hill bumps. Scale
revision 9 (`WorldChoices.ScaleRevision`; Terrain version "Build 8" keeps 8): width ^0.35, height ≥ 70 %, hill
wavelength ^0.3 (≥ min(4 km, 10 % of the world)), hill height ≥ 70 %. A small world is a region of a big one, not a
shrunken copy. (Erosion already works in grid cells, so it was not the cause.)

## D41 — Seas: 12 waves, swell, crest foam and glow (2026-10-05)
Tom: waves too artificial; Sea of Thieves as the reference (shape, shader, storm foam). Gerstner kept (gameplay
must query the same surface): 10 wind-sea waves (ratio 0.72, spread around the wind, more aligned in storms) + 2
swells from other directions (tiny when calm, ~2.4 m in storms); per-wave choppiness q = chop/(k·A·N) so crests
sharpen without folding (chop 0.45 → 0.95). Shader: whitecaps from the Gerstner Jacobian (surface compression at
crests) in a cellular foam pattern; turquoise light through thin crests when looking toward the sun; mid-scale
chop normals growing with the sea state. Storm crests ~8 m; CPU height query 10 fixed-point iterations (< 2 mm).
Next for SoT-level seas if needed: FFT spectrum on the GPU with a CPU readback for buoyancy, foam persistence buffer.

## D42 — Climate and data-driven biomes, phase 1 (2026-10-05)
Tom asked for many biomes; Noor's spec (`docs/design-inbox/NOOR_BIOME_SPEC.md`) set the architecture; plan in
`docs/BIOMES_PLAN.md`, my earlier idea in `docs/BIOMES_V0.md`. Climate: temperature = 28 − 0.0078·lat² − 6.5 °C/km
+ offset, latitude span grows with world size (6° … 70°); moisture carried inland by latitude-band winds with
orographic rain and rain shadows. Biomes are data (`Content/biomes.json`, `Content/asset_sets.json`, plus
`user://biomes/*.json`, `user://asset_sets/*.json`; same id replaces a built-in). Placement = weight × soft range
memberships × coast/river affinity × region noise at the biome's own scale; patch biomes (Ancient forest, Blighted
swamp, Volcanic) as seeded patches inside hosts; neighbour rules; the best three per cell blend (squared weights,
warped bilinear sampling → organic borders). The world generator still owns geography; biomes don't change the
terrain yet (phases 3–4). Terrain version "Build 9" and older turn biomes off (one classic biome = the build 9
look, terrain bit-exact). F4 tints biomes in game; the HUD names the biome; "Show biomes" on the world preview.

## D43 — Placeholder plants, leaf translucency, layered and volumetric clouds, biome skies (2026-10-05)
Tom (round 6): placeholder art for missing plants in pixel style (Valheim and the existing pack as reference);
optional biome skies; cloud layers at different speeds as a setting, volumetric clouds to test later; subsurface
scattering on canopies and plants, configurable.
* Plants: no Blender in the cloud, so 10 species (2 palms, saguaro, barrel cacti, dead tree, snag, acacia, jungle
  giant with buttresses and vines, swamp cypress with hanging moss, dry shrub) are built in code
  (`PlaceholderPlants.cs`): low-poly tubes, lumpy crowns, folded fronds, cut-out cards; textures are generated
  64 px pixel maps with 5-tone palettes and ordered dithering (`Tests/WorldGen --plant-textures`, PNGs in
  `Assets/Vegetation/Placeholder`, replaceable). Fixed seeds per species; biomes reference them through asset sets
  only, so real GLBs can replace them later without touching biome data. A test checks every tree-set name exists.
* Translucency: Godot's BACKLIGHT (light transmitted to the far side of thin geometry) on the woodland canopy, the
  placeholder foliage, grass and far tree cards, from one global `leaf_translucency` (setting 0–2, default 0.8).
  The valley game shares Canopy.gdshader but never sets the global, so it stays 0 there (unchanged).
* Clouds: setting Cloud quality — one layer (build 10), layered (default: cirrus at 7 km, slow, veered heading;
  main deck at 1.6 km; ragged low clouds at 0.9 km, 1.9× faster), volumetric (experimental: the main deck
  ray-marched as a 1.3–2.9 km slab, 40 steps + 4 light steps, Henyey-Greenstein silver lining, powder term,
  quarter-resolution pass). Drift wraps where each layer's texture repeats exactly (integer texture scales after a
  3-4-5 rotation), so no jumps; the main layer's detail scale 3.7 → 4 for the same reason.
* Ground fog (Tom: ground-level fog, mist in some areas): Godot height fog fogs any surface below a height by its
  depth below it alone, so the height is kept ~2 m under the player's ground (following slowly): valleys, rivers and
  lakes below fill with mist. Amount: dawn/dusk, misty biomes (Atmosphere.Mist, Fog), wet ground after rain, rain,
  water nearby; burns off toward noon. Mist wisps (both modes): soft billboard sheets on a world-anchored 16 m grid
  around the player, hugging ground or water, denser in hollows, depth-faded where they meet the ground — they work on
  any GPU. "+ mist patches" adds a volumetric fog box (fog shader, world-anchored noise); volumetric fog does not draw
  in the cloud's software renderer, so that part is untested here. Depth of field: far blur only (option).
* Cliffs (Tom liked the sharp sea cliffs, wanted them less stretched and dressed): the terrain shader blends two side
  projections on steep faces (strata, ledge seams, cracks); level-0 tiles find lips (a ≥ 1.8 m drop within 3 m next to
  flat ground) for hanging-grass and root cards (pixel textures) and cliff feet for scree boulders.
* Biome skies (option, default off = realistic): biomes set `Atmosphere.SkyTint` and `Haze`; the blend around the
  player tints the sky, adds horizon haze and fog, easing over ~3 s; "subtle" applies half.

## D44 — Round 7: time, sky, weather, wind, cliffs, collision, map, biome layouts (2026-10-05)
* Fixes: trees in cells straddling a tile's far edge were dropped (tree-free lanes along every tile border); cliff
  dressing only where a real cliff starts (face ≥ 60°, ≥ 1.2 steeper than the top, ≥ 3.5 m drop), irregular; mist
  wisps fade when seen from above; god rays only from very bright sky so clouds cut them; Sun setting classic (build
  11) / natural (colour, size and glow follow the sun's height), Sun glare multiplier.
* Collision for trunks (per species radius), dead trees, cacti, stumps, logs, bushes, rocks and boulders on level-0
  tiles (static shapes on the tile body).
* World map (M): terrain/biome view, wheel zoom at the cursor, drag, right-click teleports onto the ground.
* GameClock: day length setting (default 120 real minutes per game day, up to real time); daylight share realistic
  from latitude (the player's) and season, or a custom %; day and night run at different speeds to fit the real
  minutes. Sun and moon positions from spherical astronomy (declination from the season, 29.5-day moon); stars on
  a cube-face grid, Milky Way band, planets on the ecliptic, moon with phase and moonlight; fog dims at night.
* Wind: direction veers over game hours, speed gusts over seconds and rises in storms; drives clouds (high layer
  veered), waves (heading follows), mist, grass, plants and woodland canopy sway (global `wind`; 0 in the valley game).
* Weather: fronts over game hours (two noise scales), rain when a front exceeds a threshold set by the local annual
  rainfall (deserts rarely, rainforests often), heavier above it; storms → lightning flashes; snow when below ~1 °C or
  on snowy ground; settles over real seconds to minutes; Weather on/off and Rain frequency settings; H forces rain.
* Water tint per biome (Water.WaterTint/WaterTintStrength), carried on the water sheet's UV2.
* Cliffs: tileable 128 px pixel rock texture (irregular faulted strata, pinch-outs, fractures), projected from three
  sides on all bare rock, strata wandering along faces; blocky flat-faced rock slabs set into cliff faces (nearest
  tiles), huge boulders, rocky shore patches (rocks + bare rock instead of sand); rocks use the texture tinted with
  the biome rock colour (MultiMesh colours).
* Swamps: permanent murky pools in flat wet ground (terrain shader noise) with mud rims; reeds placed along the same
  pool edges (C# replica of the shader's hash), no grass in pools.
* Biome layout: realistic (default, unchanged) or patchwork (jittered regions at a patch size, random biomes by
  weight, rare ones stay rare, upland biomes on mountains, warped and blended borders); per-biome toggles. New
  ClimateSettings fields are omitted from JSON at their defaults (saved worlds and types unchanged).
* Cloud resolution setting (256/512/1024 with finer octaves). Volumetric clouds now render (half-res pass).

## D45 — Round 8: real wetland water, four swamp types, light beams under clouds (2026-10-05)
* Terrain revision 11 (the default; "Build 9" and older unchanged): wetland biomes carve **real standing water**.
  New `Water.Pools` (share of flat ground under water), `PoolDepth`, `PoolSize`, `PoolChannels` (0 round pools … 1
  long winding channels). A world noise mask (round pools, or lines where a domain-warped noise crosses zero) above a
  threshold from the coverage (quantile tables built at start, so the share is right for any noise) marks the
  water; the ground around follows a profile about a flat level — 0.25 m bank, bed down to the depth over a few metres,
  a little of the local relief — then blends back. The level is the ground without its relief shorter than 500 m.
  Water only where the shaped ground is below the level next to the mask: never dry ground under a pool surface.
  New water kind **Pond** (no body id, calm, lake-like shading without waves, murky underwater view).
* `Terrain.Flatten` now works in wetland biomes (walking-scale relief × (1 − flatten)), so pool surfaces stay level.
  Where there are pools the ground flattens further (to ~8 % of its relief) so pool surfaces stay level. Pools fade
  near rivers, lake shores, the coast, mountains and slopes > ~3 %. Build 12 terrain (revision 10) is kept
  bit-exact (new fixture terrain-build12.json); terrain-r1 re-recorded.
* Swamp types (Tom's references): **Swamp** (wooded, existing), **Marsh** (open grey bog, Dead Marshes: many pools,
  pale tussocks, reeds, dead snags, mist), **Fen** (northern: winding channels through green sedge and bog shrubs,
  lily pads, scattered spruce/tamarack), **Bayou** (Louisiana: broad brown water, bald cypress with Spanish moss
  standing in it, water hyacinth mats). Blighted swamp gets pools too. Mixed areas blend as before.
* Vegetation: `WaterTrees` (trees standing in pools up to ~1.2 m deep), `FloatingPlants`, `Shrubs` + `ShrubKind`
  (bog / tussock); `AquaticPlants` "Hyacinth" or lily pads. Placeholder art: bald cypress A/B (fluted base, flat
  feathery crown, long Spanish moss), bog shrub, tussock, sedge, lily pads, hyacinth mat (pixel textures).
* Pool beds: dark peat and silt with sunken leaves and twigs; wetland pool margins dark mud and moss. The old
  shader-only painted pools are gone (reeds line the real pools).
* **Light beams under clouds** (crepuscular rays; setting, default 1): a full-screen pass marches each view ray
  through the air below the cloud deck; the cloud above each point toward the light (same texture, plane, drift and
  offset as the cloud shadows) says if that air is lit; forward-scattering phase; only with broken cloud. Follows the
  moon at night (fainter, silver-blue, with its phase). Works with the sun hidden or off screen.

## D46 — Round 9: biome landforms — dunes, mesas, needle spires (2026-10-06)
* Terrain revision 12 (the default): `Terrain.Features` works for `"dunes"`, `"mesas"` and `"spires"` (strength 0 … 1,
  spires up to 1.5), applied on top of the walking-scale detail before rivers carve (rivers cut gaps and canyons).
  * Dunes: transverse ridges across one sand-moving wind heading per world (a varying heading rotated far-from-origin
    coordinates into 5 m ripples), long windward slope, ~35° slip face, 4–12 m high, ~90 m apart, crests curving and
    breaking up; dune fields and calmer sheets; other relief smoothed; fade out on tiles too coarse to carry them.
  * Mesas: flat-topped uplands 40–110 m (noise above a threshold), stepped into 12–21 m cliff-sided tiers. Only the
    uplift is terraced (terracing the regional slope gave contour benches). Outline noise keeps all octaves at every
    detail level, so far mesas do not shift.
  * Spires: needle rocks 12–55 m tall and ≥ 4.2× their radius, near-vertical walls, small scree flare, jittered 36 m
    grid; on coarse tiles wider (stay visible from afar); height fades across the edge of spire biomes.
  * Lake shores keep their basins (landforms fade within the 2-cell lake search).
* Revision 12 blends biome terrain over every contributing biome (continuous); the two-strongest blend stepped where
  the second place switched (invisible in colours, a step in landforms). Revision 11 keeps the old blend.
* Pools (revision 12): shaping fades in with coverage (no step where coverage crosses the cut-off), edge blends
  stretch with the height they cover, dry shore stands 4 cm clear of the water, lake fade ends inside the lake search.
* New patch biome Needle Rocks (Thousand Needles style) inside badlands, desert and savanna; badlands get mesas and a
  few spires; desert dunes.
* Tests: landforms (dunes, mesas, spires where their biomes are, none elsewhere); Build 13 terrain (revision 11) with
  the build 13 biome catalogue; walkability and LOD tests leave out deliberate cliffs, spires and dunes.
* Later: volcanoes (two-pass, before hydrology), canyons as deeper river incision in dry rock, ravines, boulder fields.

## D47 — Water experiment: an opt-in alternative water (2026-10-06)
* Tom asked for a richer, smooth, non-faceted water experiment for oceans, lakes and rivers, keeping biome tint and the
  terrain-aware behaviour, without losing the current water. Classic water stays the default and untouched (backup
  point 3511e4b); `WaterExperimental.gdshader` is selected by the setting fx.water_style. One material, shader swapped
  in place, so all uniforms carry over and existing tiles switch instantly.
* Per-pixel analytic wave normals (full Gerstner sum at the undisplaced position) instead of per-vertex normals: far,
  coarse water shades smoothly. Geometry keeps the waves the mesh can carry (≥ 5 vertex spacings).
* Shore waves: phase = shallow-water travel time to the point, φ = 2ω√d/(s√g) (d depth, s bed slope), so crests follow
  depth contours and travel shoreward with the right local wavelength (T√(gd)); Green's-law shoaling capped at
  breaking (H ≤ 0.75 d); breaking drives foam on and behind the crest. Bed gradient and coast exposure (macro land in 8
  directions at 0.6/2/6 km) are per-vertex data (CUSTOM0), computed as pure functions of world position (no seams).
* Lakes cut the wave set by fetch (√area); rivers get flow-advected gradient-noise ripples with along-flow smear and
  speed/depth-driven whitewater. Wind waves: six short waves fanned around the wind with noise-warped phases and gusty
  amplitude (value-noise slopes looked blocky; replaced by gradient noise).
* Foam: one swirled bubbly pattern revealed by a per-source foam amount with soft edges (cell-edge patterns read as a
  net and were dropped). Swash film alpha ramps over ~0.2 m and fades over the sheet's extension (COLOR.a), so the
  film no longer ends on triangle edges.
* Swimming now evaluates waves in scene-local coordinates like the shader (it used absolute ones, which drifted after
  a float-origin shift — also for classic); experimental surface mirrored on the CPU (WaterShore.Height).
* tools/water_test.gd: a beach/lake/river harness rendering either style in seconds (a full world render takes ~5 min).
* Details and comparison spots: docs/WATER_EXPERIMENT.md.

## D48 — Water experimental 2: spectrum ocean and layered foam (2026-10-06)
* Tom found the max sea state "big but calm" and sent Sea of Thieves references. A third opt-in style (classic and
  experimental untouched): SpectrumWaves.cs — 32 Gerstner waves from a JONSWAP spectrum (γ 3.3), Hs 0.4 + 6.8 s^1.5 m,
  peak wavelength 9 + 62 s^1.2 m (stylised: ~2× real storm steepness; physically scaled 200 m storm swells read as slow
  hills), spread 0.3–0.85 rad widening for short waves and storms, swell at +0.85 rad, per-wave kA ≤ 0.3, Σqk A =
  0.6–0.96. Wave groups: a slow noise multiplies all amplitudes (same formula on the CPU). Uniforms waves2/wave2_qp/
  wave2_hs; Atmosphere resends all wave sets when the water style changes.
* Whitecaps: with 32 mixed waves the Jacobian rarely folds, so high-and-steep crests are the main source; plus wind
  lanes (anisotropic noise along the wind), storm foam fields, folds.
* Foam layers: surface foam (cover × max(body, cell-edge lace, speckle); strong foam solid with holes, weak foam lace),
  milky under-foam (wider, tints the water body and hides the bed), shading; anisotropic along drift; lace fades to its
  average beyond ~35–160 m. A debug_foam uniform shows amount (red) and foam (green) for tuning.
* Shore-wave hand-over from 19 m (experimental: 14 m), WaterShore `wide` variants for the CPU mirror.

## D49 — Round 11: rock collision hulls; experimental 2 sea rebuilt (smooth storms, regional directions) (2026-10-06)
* Tom (build 16): standing on rocks he floated above them — rock collision was a sphere of 0.8 × the mean scale, much
  taller than the flattened rocks. Now each rock gets a convex hull of its own mesh vertices through the instance basis
  (RockMeshes.HullPoints, thread-safe, no rendering objects).
* Tom: storms not big enough, wanted "different levels of storm, preferably smooth, from calm water to huge waves",
  crisper crests, and "waves on all water bodies go in the same direction". Noor's notes (open sea follows wind/swell,
  shore waves turn gradually and not every crest hugs the beach, calm = long soft swell, storm = big swell plus chop,
  lakes gentle, rivers follow the flow) agree and were folded in. Done in experimental 2 itself (Tom asked for these
  fixes there); build 16 (`defef2c`, docs/SNAPSHOTS.md) keeps the previous experimental 2, classic and experimental
  are untouched.
* SpectrumWaves: 64 fixed waves — 52 wind-sea waves on a fixed wavelength ladder 1–260 m with golden-angle directions
  (the whole compass) and 12 swell waves 30° apart at 120/174/252 m. Only amplitudes depend on the sea state: re-picking
  wavelengths for each sea state shifted every phase (a 1% wavenumber change at 1 km is ~1 rad), so storms popped
  instead of growing; now a sea-state step only rescales (tested: ≤ 15 cm for 0.01). Wind sea Hs 0.15 + 14.1 s² m, swell
  Hs 0.45 + 1.6 s m (calm = long soft swell), peak wavelength 9 + 130 s² m, choppiness 0.62 + 0.3 s.
* Regional headings: weight cos^m(angle to the local heading), m = 10 − 8.4 s (narrow calm, confused storm); local
  heading = wind + regional turn (sea: noise over 8 and 2.5 km, ±~1 rad; lakes: their own field over 3 km and 0.9 km);
  swell weight cos^24 to a swell heading field over 20 km, independent of the wind. Amplitudes renormalised per point
  (wind sea and swell separately), so Hs is the same everywhere (tested). Headings and norms are per-vertex varyings.
  The shader follows the live wind, so wind turns no longer reconfigure the waves.
* Crisp crests: choppiness share ∝ k²A³ (mostly to the biggest steep waves), Σ q k A = chop, divided by the group
  factor where groups raise the waves (before, high wave sets could fold).
* World anchored: phases include k·d·origin (mod 2π, in double on the CPU), regional fields and groups use world
  coordinates (world_origin uniform), so the sea no longer jumps when the scene re-centres every 1.5 km.
* Shore (Noor: no perfect conformity): open-sea waves keep 40% (was 25%) into the shallows at their own angle over the
  shore-facing waves; extra crest-phase noise so segments lead or lag; breaking depth ratio 0.62–0.92 by noise, so the
  foam line is irregular.
* CPU mirror: Newton inversion with the analytic Jacobian (plain iteration crawled at Σqka ≈ 0.9), groups sampled at
  the rest position like the shader; height error ≤ 2 cm at ~14 m seas.

## D50 — Round 12: experimental 2 as a system; Noor's ground textures as an option; ragged rock edges (2026-10-06)
* Tom: "the best water so far, definitely keeping". Noor's review: foam too aggressive for normal weather, huge
  continuous white masses, largest swell should be broader, crests should organise toward the shore near land, calm must
  keep the quality, and "do not simplify this shader": one water, parameters by weather (calm → breezy → rough → storm)
  and by place (deep → coastal → shallow → breaking).
* Weather: open-water white water is gated by the sea state (caps from ~0.25, tops from ~0.3, lanes from ~0.55, foam
  fields from ~0.75), times a "life" noise (caps form and fade over seconds) and a fragment noise along the wind (pieces,
  not sheets); below breaker strength the foam reveal itself comes in streaks. Storms keep dense foam. Aerated crests
  from 0.4. The sea-state setting now names its range (calm < 0.2, breezy < 0.45, rough < 0.75, storm).
* Swell: 150/218/316 m swell waves and a 300 m longest wind wave; swell Hs 0.5 + 2 s.
* Coast (Noor's shore_factor): coastal = smoothstep(20, 4, depth) × gentle bed slope (seas only, not rocky coasts);
  both headings rotate toward the uphill bed direction by 0.85/0.95 × coastal and the wind-sea spread narrows
  (m → 16). Because only wave weights change (fixed wave set), the rotation is smooth however quickly it varies; the
  contour shore waves still take over in the last metres. CPU mirror: SpectrumWaves.Coastal / Headings(…, coast).
* Ground textures: Noor's 16 seamless pixel-art textures (Assets/Terrain/Noor, 512 px) as a switchable ground style
  (fx.ground_textures: current default / Noor), like the water styles. GroundTextures.cs: a Texture2DArray at 256 px
  (art pixels ~2.5 texels, ~8 m per tile, nearest + mipmaps) and per-layer mean colours. Biome-coloured surfaces (grass,
  sand, snow, dug soil, beach sand) use the texture as detail (texture / mean, the biome colour's average unchanged);
  materials (forest floor, mud / wet mud, gravel, moss, peat, gentle rocky ground, mountain scree, river and sea beds)
  use Noor's art up close, fading to the usual colours by ~200 m (far forests and coasts unchanged). Two rotated copies
  blended by slow noise hide the tiling. Mud turns to Noor's wet mud near the waterline and in rain. Ice and volcanic
  rock are loaded for later (frozen lakes, volcanoes). Cliffs keep the bitmap rock looks. Classic speckle detail drops
  to 30% under Noor's textures.
* Tom's "blocky wet ground" on his rocky coast was bare rock (rocky-shore exposure, a per-vertex amount) showing through
  grass with triangle-straight edges. The rock threshold now also uses the rock texture's brightness (stones show
  first) and a few-metre noise: ragged edges in both ground styles.

## D51 — Round 13: dug water joins rivers softly; water reflects the real sky; calmer ripple reflections (2026-10-06)
* Dug holes filling from a river: LocalWater redraws river/lake cells within 3 cells of simulated water so the sheets
  join; that overlap ended in 0.5 m stair-steps and had no biome tint (clear turquoise squares over a tinted, foamy
  river). ChunkView now reports a join weight (1 near the simulated water, fading to 0 at 3.5 cells), written to the sim
  mesh's COLOR.a (the experimental shaders' edge fade), and the sim mesh carries the biome water tint (UV2) from
  TileBuilder.WaterTintAt at the chunk corners. Classic water ignores COLOR.a (unchanged there apart from the tint).
* Night: the water's sky colours (sky_horizon / sky_zenith) were never set, so every water style reflected a daytime
  sky at night (lakes glowing blue-white). Atmosphere.UpdateWaterSky sets them each frame from the sky shader's own
  colours and day factor (sin(sun) × 4 + 0.35, 0.08 … 1), sunset warmth near the horizon, the biome mood, and storms
  (greyer, darker). Applies to all three water styles (a bug fix).
* Windy lakes (Tom: "this element is annoying and too strong"): the fine ripple facets tilted toward the horizon and
  mirrored the bright sky as hard white streaks. Experimental 2's Fresnel now uses a softened normal (big waves plus 45%
  of the ripples); glints and refraction keep the full normal. Ripple and chop wind response lowered (0.35 → 0.22,
  0.08 → 0.05); far chop fades (it only shimmered at the horizon).
* Aliasing: Tom's "annoying" pattern was mostly moiré — at grazing angles one pixel spans metres of water along the view,
  and short waves aliased into rings and bands around the camera. Experimental 2's cut-offs (spectrum waves, ripples,
  chop) now use the pixel footprint along the view (dist × 1.2 mrad / grazing), not distance alone.
* Rivers: rapids come in patches drifting with the current instead of one white sheet over a whole fast river; the
  general moving-water streaks are lighter.

## D52 — Round 14: terrain revision 13 (no perched tributaries); Noor's river direction (2026-10-06)
* Tom's desert river (Mountain kingdoms 42, ~45350, 136120): walls of water and a clear patch with straight edges. Two
  side rivers (surfaces 314.0 and 313.1 m) ran for ~100 m across the main river's valley floor (310.5 + 0.9 bank =
  311.4 m, flat) before joining: each river carves its own valley and the deepest carve wins, so the main valley floor
  replaced the side rivers' banks while they kept their own water levels 2–3 m above the dry floor.
* Revision 13 (TerrainSampler): for each river at a point, the lowest floor the other rivers carve there caps its
  surface at that floor − 0.3 m (never below the other river's own water, smooth weight on how much they carve). A
  tributary crossing a floodplain flows at it and drops down the valley side as rapids; rivers on high ground are
  unchanged. Test: 86 → 0 dry points beside river water lying > 0.5 m below it at Tom's spot. Revision 12 frozen as
  terrain-build19.json (with the build 19 biome catalogue); the current-revision fingerprints happen to be unchanged
  (the fixture grids cross no such confluence).
* The clear patch was the water view redrawing river cells next to rain water pooled on that sub-river floor; with a
  real bank the rain drains. LocalWater now also needs 3 cm of standing water on undug ground before it redraws a river
  edge (test).
* Noor's river direction ("rivers are water with a destination"): docs/RIVERS_PLAN.md — agreed; plan: current field
  across the channel (banks, narrows, bends), river layers incl. standing waves and wind waves on wide rivers, current
  plumes and wave–current meeting zones at mouths, wakes from relative velocity (player/creatures/boats/debris), baked
  obstacle wakes.

## D53 — Round 15: rivers as moving water (RIVERS_PLAN steps 1–5, experimental 2) (2026-10-06)
* Current across the channel (TileBuilder.Flow): speed × (depth / deepest water within ~6 m)^⅔ (Manning), 0.2 … 1 —
  lazy shallow margins, fastest over the deep line.
* River surface: the flow pair plus a broad layer at ~0.55 × the current (different speeds and scales); standing waves
  in fast shallow water (crests across the flow at λ = 2π v²/g that stay put, white tops, in patches); bank turbulence on
  shallow moving margins; wind waves on wide rivers (fetch = the reach's mean width, CUSTOM0.w), steeper against the
  current.
* Mouths: the current continues into the lake/sea down the gradient of the river-water share (plume), speed
  1.1 × plume^0.7, joined to the river by the smoothing; the shader blends flow ripples in by current strength, and
  where the wind sea or the incoming shore waves run against it adds confused chop and broken foam (stronger in rough
  seas). Foam drifts with the plume.
* Wakes (Wakes.cs): the player leaves disturbances (every ~0.15 s; strength from the speed relative to the water =
  velocity − local current); the shader draws each as a ring of ripples spreading at ~0.85 m/s while carried by the
  current and fading over ~4 s, plus a splash: a moving swimmer leaves a V, someone standing in a river a downstream V,
  without any fluid simulation. 24 points, only looked for within 45 m of the source.
* Rocks in rivers (TileBuilder.ObstacleWakes): for river vertices around rocks whose base is under the water, the sea's
  shore-data slots hold (downstream, across, radius); the shader draws a pressure bulge upstream, V-wake arms (narrower
  in fast water), a turbulent core with alternating eddies, and foam in fast water. (GLSL: pow(x, 2.0) with x < 0 and
  exp of an unbounded argument gave NaN sheets in testing — squares and clamped arguments now.)
* Sky ideas for the next round: docs/SKY_IDEAS.md.

## D54 — Round 16: water reflects the real sky; twilight, rainbows, halos, auroras, shooting stars (2026-10-06)
* Tom: "that reflection is a bit boring, bland when there's a lot". The water reflected a two-colour gradient. Now
  experimental 2 traces each reflected ray to the sky's own cirrus and main cloud layers (same texture, heights and
  wind drift as Sky.gdshader, shared by Atmosphere.ShareClouds; cloud_state/cloud_layers globals), lit by the sun and
  the day/night sky colours, smeared on rough water. SSR still wins where it hits the landscape.
* Twilight from the true sun (new global sun_true; the directional light stops at −4°): a red-orange band toward the
  sun (9° … −7°), the Belt of Venus with Earth's shadow rising under it on the opposite side (4° … −7°), the blue hour
  (2° … −11°), and clouds lit from below (orange → pink, toward the sun most), in the sky and in the water's reflection
  (same formulas). No sun disc below the horizon.
* Optics: rainbows opposite the sun as rain clears (rain_wetness, storm easing, sun 0–42°): primary 40.5–42.5°,
  secondary 50.5–53.5° reversed, Alexander's dark band, brighter inside; 22° halo and sun dogs where cirrus actually
  is (sun dogs move out as the sun rises, red toward the sun).
* Night: auroras (global aurora: latitude > ~50° fading in to 68°, dark clear nights, activity per night; curtains toward
  the pole, two patchy folded curtains with irregular rays, green lower edge into violet); shooting stars (a chance every
  6 s, under a second each).
* GLSL lesson (also fixed in the existing Milky Way band): pow(x, 2.0) for x < 0 is undefined → squares.

## D55 — Round 17: cloud types from the weather and the land (2026-10-06)
* Tom approved SKY_IDEAS item 3. Each type is a quantity from the simulation, not a label: Weather derives cumulus
  (hour 10–20, temperature, annual rain, broken sky, no rain), distant storm (a front over the thunder threshold 2–6 h
  ahead → upwind, 2–5 h behind → downwind; or air-mass storms on hot wet afternoons, ~half of days) and mackerel (rain
  3–9 h ahead, still dry here) — all eased over ~40 s. WorldLab finds the highest peak 5–23 km away every 4 s; a lens
  forms over it with relief > ~450 m, wind > ~6.5 m/s, dry air, on ~2/3 of days.
* Rendering (CloudShapes.gdshaderinc, shared by the sky and the experimental 2 water's reflection): far clouds are
  drawn on vertical planes facing the camera at their distance, sunk by the Earth's curvature — cheap and exact enough
  kilometres away. Towers are built from round puffs (three per level, narrowing up, a dome on top) whose blended ball
  normals, a bumped detail normal and darkened creases give the cauliflower look; noise outlines alone looked like
  shrubs. Cells live in slices of the horizon (two rings, 13–42 km and 30–72 km) and each lives 7–11 minutes: grows,
  stands, dissolves, replaced elsewhere. Storm cells: tower + anvil (flat sharp top, streaky drawn-out underside sagging
  toward the column, spread downwind) + slanted streaky rain shaft; lightning from Atmosphere (a cell index and
  flicker in cloud_types.w) lights a spot inside, and bright flashes draw a bolt under the base. Lenticulars are real
  ray–ellipsoid intersections (four overlapping thin plates, long across the wind), so they look right from any side.
* Order: far clouds are composited after the flat deck; low in the sky they stand in front (the deck's thin slivers
  otherwise sliced the towers into bands), above ~15° the deck covers them.
* The sky's clock is now cloud_more.z (a global) instead of TIME: TIME in a sky shader makes Godot re-render the
  radiance cubemap every frame (twinkling, auroras and meteors used it since build 21).
* Not done: fog banks off the sea (needs the mist system to move offshore), towers in the volumetric (quality 2) deck.

## D56 — Round 18: volumetric clouds with real volume and colour (2026-10-06)
* Tom on build 22: the far towers "look flat" and "made of spheres, very regular"; the volumetric clouds "lack detail,
  contrast and colours"; clouds may be smudged or crisp depending on the wind ("we can fake it"). References: towering
  cumulus with blue-grey shadows, sunset clouds lit pink.
* Cause: the towers were cards facing the camera with ball-shaded puffs; the old volume was a slab from the 2D map with
  one detail texture and a fixed thickness.
* Now (Cloud quality = volumetric): a tiling 3D noise (CloudNoise3D.cs, 64³ RGBA, built at start-up: Perlin-Worley
  billows + three Worley octaves, all periodic) carves the clouds; the 2D cloud map (same as the flat layer and the
  ground shadows) only says where cloud is and how high it grows (thick parts higher; on cumulus afternoons the cores
  tower to ~7 km), read through a 3D warp so sides bulge instead of standing as walls. Domes round off near the top.
* Light: the sun (moon at night) traced through the cloud in six growing steps; three orders of multiple scattering so
  thick cloud is grey, not black; forward/back phase mix (silver linings toward the sun); "powder" darkening of
  sunlit faces seen from the shade; ambient from the zenith colour above (blue shadows) and from the horizon and the lit
  ground below; sunset light (the existing underlight) on the bases; aerial perspective toward the sky colour.
* Wind (faked, no simulation): wind speed blends extinction (calm: dense, crisp cauliflower edges) and the fine
  erosion (wind: stretched 3.5× along the wind, softer — smudged, streaky), and towers lean downwind with height.
* Cost: steps grow with distance (40–450 m), half resolution, empty columns stride; the far flat towers are off in this
  mode (the volume grows its own), far storms and the lens stay behind it. The water still reflects the flat layers.
* The other qualities are unchanged; volumetric stays "experimental" until Tom has seen its frame rate.

## D57 — Build 23 bugs: black fog, sea seams; F9 debug dump (2026-10-06)
* Black smudges at dawn (Tom: gone with the fog off): Godot's volumetric fog ignores ambient light by default, so mist
  in shadow rendered black. VolumetricFogAmbientInject = 1. Also: cloud shadows (a decal darkening the ground) now fade
  out with a low sun (3°–18°) and under overcast — at dawn they made black patches stretched across the land.
* Sea patterns (Tom: triangle-edged patches on calm water; a "wall" in a storm). Found by rendering each input as a
  colour: all were smooth, but the crest height jumped. The coastal turn of the wave headings turned by angle the
  short way round; where the shore lay straight behind the wind that angle flipped from +180° to −180°, so a line
  across the sea separated waves turned left from waves turned right. Now the headings blend as vectors (shader and
  SpectrumWaves.TurnToward, same formula). Also: the shore direction comes from the coastline's large shape (macro land
  gradient) instead of the local bed slope (noise on gentle shelves), the slope is measured over ~50 m, exposure
  changes smoothly with how far inland a probe lands, and the shore data fade out toward 25 m instead of stopping.
* F9 writes user://debug/dump-<time>.txt + .png: version, GPU, world (type, seed, parameters), position, view, time,
  weather, cloud types, wind, sea state, every setting, and a ready command line (--preset --seed --at --view --day
  --hour --fly-height and all --set) that rebuilds the scene for a --world-shot (new --day arg). --dump does it after a
  scripted shot.

## D58 — Round 19: weather you can see coming, mountain clouds, overcast mood, contrast (2026-10-06)
* Noor's list (build 23): more contrast and weight, high wisps moving differently, banks on mountains, haze where the
  clouds meet the horizon, a real overcast mood, isolated storm towers, the wind pushing the field coherently, weather
  visible from far away. Tom: more colour variance.
* Approaching fronts: Weather.FrontHours/FrontStrength (the next rain within 8 h, from the same front signal). The front
  edge sits upwind at ~9 km per game hour of lead time and becomes visible from ~70 km. Drawn (Sky.front_wall) as one
  object on a vertical curtain at its edge: a dark slate bank with rounded billowing tops (two rows of domes; taller
  towers for strong fronts — thunderstorms) and slanting grey rain under it to the ground. Nearer clouds stay in front;
  nothing beyond shows (the volume's march stops there). Oblique, far parts fade into the haze. Tried first as extra
  mass in the volume: buried by nearer clouds and washed out by aerial perspective; a flat layer cannot show its height.
* Mountains: WorldLab samples a 64 km relief map (64², macro heights, windward climb for the current wind) every 12 km
  or ~25° of wind change; the volume gains cloud over high and windward ground with its base lowered toward the ridges.
  The volume's base is now a fixed height above the sea (on a mountain the clouds come down to you). Limitation: the sky
  is drawn behind the terrain, so clouds wrapping a slope in front of the player need fog volumes (later).
* Overcast (coverage > ~0.7): the sun weakens and flattens, ambient drops, haze thickens (Atmosphere "gloom"), and the
  full deck gets thick and thin patches and a lumpy base (darker where dense), faded with distance (the 3D noise would
  repeat visibly near the horizon).
* Contrast and colour: denser cores, weaker multiple-scattering orders, sky light used up deep inside (cool slate),
  warmer sunlight, warm ground bounce on the bases, stronger aerial perspective toward the horizon.
* Already in place from earlier rounds: high cirrus on its own slower, veered wind; the volume, cirrus and low clouds
  each drift with their own wind (coherent fields, no in-place churning).
* Bug found on the way: the mountain base lowering applied everywhere, so over the sea the base fell to ~175 m and every
  cloud became a tall plume. The base now only comes down where the land holds cloud.

## D59 — Round 20: Tom's debug dumps (rivers, fog, wetlands) (2026-10-06)
Tom sent 14 F9 dumps from build 25. Each was replayed from its Reproduce line (weather forced off for water, on for
fog), and for the water also as if the game had run 35 minutes (TIME + 2100 s), because that is what Tom sees.
* **River "circles… not moving"**: one bug class, three instances. An expression mixing a direction or scale that
  changes from place to place with a large number (scene position, TIME) winds into arcs and stripes:
  * standing waves `kk * dot(p, fdir)` → evaluated in 20 m local frames blended between grid corners;
  * every river noise scrolled by `current * TIME` (foam patches, pulses, bank turbulence, mouths, rock wakes) →
    `flow_noise`/`flow_gnoise`/`foam_flow`: two copies scrolled over a short cycle and cross-faded (the method
    flow_slope already used). At TIME + 2100 s the old foam was a field of hairy arcs; the new one is drifting patches;
  * river foam was pulled into streaks in the local current's frame (`dot(q, dn)` with q ≈ position + drift·TIME) →
    unrotated; streaks come from a second tap up the current. The sea keeps the rotated streaks (the wind is one
    direction everywhere).
  Rule for later shaders: never multiply a varying direction/speed by TIME or by a scene position.
* **Rock wakes** now carry a weight per vertex (fades at the wake's ends and where a second rock is close; only rocks
  breaking the surface count) instead of hard-edged per-triangle wedges.
* **White strips along river banks**: the waterline contact foam spanned 14 cm of depth on rivers, a wide strip over
  shallow bank shelves following the terrain's triangle edges → 6 cm. Riffles fade out in the last 8–35 cm.
* **Water body codes**: sea 0, river ⅓, lake ⅔, pond 1 (were crowded together); river samples below the sea at a mouth
  become sea (bank vertices too), so the seam between river and sea water is gone. The test uses the world's sea
  level: it used the level of an ocean sample in the same tile, so a tile holding only the inner estuary kept the river
  code and drew a seam along its edge.
* **Verification lesson**: Godot runs the last built C# assembly; the first round of replays still ran build 25's C#
  (terrain revision 13, old tile codes) and showed the bayou channels unchanged. Rebuild (`dotnet build
  Thornwake.csproj`) before any replay that tests C# changes.
* **Fog "only silhouettes… a single colour"**: Godot height fog fogs a point by its depth below FogHeight, not by its
  distance, so from a mountain a valley hundreds of metres below went solid grey. Density ÷8.5, mist ×0.75, mist
  volume and wisps thinner. The cloud base stays ≥ ~1 km above the player (the sky is drawn behind the terrain, so a
  base just overhead on a mountain made a grey lid and a cloud wall at the horizon).
* **Bayou/marsh "straight channels" (terrain revision 14)**: two causes. (1) The pool size varies across biome
  borders, and noise at position / size races through its pattern far from the origin (~4 cycles/m at 236 km): hair-
  thin parallel stripes. Now evaluated at fixed sizes on a half-octave ladder and blended. (2) Near the shore the bed
  was within cm of the water, so the eroded relief (parallel gullies downhill) decided what was water. The relief now
  fades at the shore and pools start 12 cm deep. As the whole mask is now water, the cover padding drops from ×1.3 to
  ×1.15 (marsh 55 %, bayou 41 %, fen 9 % in the test squares). Build 25 terrain stays reproducible (revision 13 +
  fixtures `*-build25.json`).
* Not changed: river connections (Tom: they look good).

## D60 — Round 21: rolling ground mist (2026-10-07)
Tom: "currently it had the same density everywhere… maybe some rolling slow turbulence would help."
* The mist patch volume (Mist.gdshader, a 420 m fog box following the player) now samples the tiling 3D cloud noise:
  ~150 m **banks** drifting with the wind and slowly forming/dissolving, a per-bank **depth** of 3–18 m with a lumpy top,
  ~20 m **billows** warped by a slow 90 m flow, drifting faster than the banks and rising/sinking (rolling), and fine
  detail fraying thin edges. The even height fog drops to 30 % when mist patches are on (it only fills in).
* The 3D noise channels sit in a narrow band (Perlin-Worley 5–95 % = 0.45–0.77); thresholds over 0–1 gave one even
  block of mist. Every use is remapped to the measured band.
* Bug: the mist volume followed the ground with an 8 s lag even across teleports, so after map travel it stayed buried
  or hanging at the old height for a long time (in replays it was 130 m underground). Jumps over 60 m now snap.
* Fog voxel grid 64³ → 96³ (finer billows near the camera; small GPU cost).
* Scripted screenshots switch off temporal fog smoothing (at ~1 FPS it was still fading in when captured).

## D61 — Round 22: vegetation as populations (2026-10-07)
Noor's plan (docs/design-inbox/NOOR_VEGETATION_POPULATIONS.md) with her and Tom's sprite packs.
* **Assets**: 28 pixel-art sprites in `Assets/Flora` (12 grass/tall grass/wild wheat, 8 shore and floating plants,
  8 underwater plants), cropped to the plant, bottom-aligned on a square, 1254 → 256 px; one Texture2DArray, one
  shader (Flora.gdshader): crossed quads (upright, underwater) or a flat quad (floating), per-instance tint and
  custom data (layer, motion, phase). Grass, shore plants, floating leaves and underwater plants: one draw each per tile.
* **Placement** (GroundCover.Flora): the existing hard rules decide where plants may grow; three noise scales decide
  how many: macro lushness (~170 m), a domain-warped patch field (7 m or 24 m clumps, the scale itself chosen by a
  ~90 m region noise and blended, never a varying wavelength), and the candidate's own scatter. Between patches the
  cover stays sparse-to-normal (never a bare lawn); dense patches keep holes.
* **Patch identity**: each patch (a warped 8 m or 22 m cell) draws a community by habitat: sparse short grass;
  short grass with odd tall tufts; thick lush grass; grass with wild wheat (patches ripen green or golden); a stand of
  tall grass; wet meadow (near water / wet biomes). Reed beds, cattail stands and rushes with flowers along still and
  slow water, clumped (squared clump field), standing into the shallows.
* **Water plants** in fuzzy depth bands: lily pads, water lilies and duckweed rafts on ponds and lakes (0.2–3 m);
  submerged plants on lake, pond and river beds (shallow vs deep sets; rivers: eelgrass, algae on rocks); sea:
  seagrass meadows (0.5–6 m), kelp forests (2.5–20 m, more on rocky beds, capped below the surface), seaweed on rocks.
* **Ground**: the terrain's UV2.x now carries the same grass cover (the height above sea comes from the world
  position), so the ground under dense patches is richer and under sparse ones a touch drier — the patch pattern
  continues past the grass draw distance and lines up with the sprites up close.
* **Look**: the painted sprites were brighter and more saturated than the terrain (lime grass, orange cattails under
  the saturation setting): albedo × 0.64, a quarter desaturated, weaker translucency glow.
* **Count**: fewer instances than the classic tufts at the same setting (a meadow: 315k vs 440k), 4 triangles each.
* **Setting** "Grass and plants": plant populations (default) / classic tufts (the old look, for comparison).
* **Build 29 follow-ups**: tufts are three planes at 60° leaning ~14° out (two flat cards read as lines from a high
  camera); the placeholder bog shrubs give way to sprite sedge/rush meadows up close; floating and underwater plants
  are drawn in 32 m quarters with range floors (100 m / 70 m). A visibility range is measured from the node: one node
  per 64 m tile with Tom's 0.6× scatter distance (42 m) switched a whole seagrass bed off when turning the camera
  (it orbits the player) moved it ~7 m (dumps 20261007-122642/122651). Lake plants stay ~0.5 m deeper (wind waves).

## D62 — Round 23: colour and lighting pass, the "Look" setting (2026-10-07)
Tom: "the game in general sometimes feels very washed out … do you think HDR would help with blinding and spilling
light?" The engine already renders in HDR; what washed it out was the mapping to the screen and the extras on top
(soft-light glow over everything, strong bounce light and sky ambient filling the shade, aerial haze).
* **Setting "Look"**: natural (default) / classic (= build 29, unchanged).
* **Natural**: ACES tone mapping (white 6, exposure ×1.15) with saturation ×1.08 and contrast 1.06; glow only above an
  HDR threshold of 1.3, added (not soft-light) at ~55 % strength and without bloom over the whole image (no spilling
  light); bounce light (SSIL) .55 instead of .8; sky ambient .42 instead of .55 (shade reads as shade); aerial haze,
  base fog and volumetric fog densities ~25–40 % lower. Meadow test (dump 214248) against classic: same brightness,
  saturation +11 %, contrast (value spread) +22 %, a bluer, less milky sky.
* **Tried and dropped**: AgX tone mapping (Godot 4.5's AgX desaturates: the meadow lost 27 % of its colour and some
  contrast — the opposite of the goal); eye adaptation (auto exposure lifted the overcast meadow by ~25 % and washed
  it out again). Eye adaptation may come back later with tighter limits, tested in game rather than in stills.

## D63 — Round 23: water experimental 3, a GPU FFT ocean (2026-10-07)
Tom (with a Blender ocean-modifier render): "I like the choppiness of these waves, they are kind of crisp … aiming for
Sea of Thieves water style." Experimental 2's 64 summed waves cannot get there: summed sines stay smooth and a sharp
crest needs hundreds of them.
* **Tessendorf FFT** on the GPU (Game/Rewrite/Ocean): three cascades of 256² (480 m, 59 m, 7.9 m tiles, carrying
  480–12 m, 12–1.6 m and 1.6–0.25 m waves: no wave counted twice), compute shaders for the spectrum at time t, a
  shared-memory inverse FFT along rows then columns, and an assembly pass writing displacement (λDx, Dy, λDz, foam)
  and slopes (dDy/dx, dDy/dz, λdDx/dx, λdDz/dz) as texture arrays sampled by the water shader.
* **Same sea**: JONSWAP spectrum (γ 3.3) on experimental 2's sea state (Hs, peak wavelength, swell height), a
  cos^2s spread (wider in storms) along the wind, plus a narrow 220 m swell 40° off it. Random amplitudes from a fixed
  seed (deterministic); frequencies rounded to multiples of 2π/600 s so the sea loops exactly every 10 minutes and the
  time can wrap without a jump (float precision). Re-weighting on a sea-state or wind change keeps the same random
  numbers, so the waves change without popping.
* **Choppy**: horizontal displacement +i k/|k| h pinches the crests (λ = 0.8 + 0.4 × sea state). Foam is born where
  the Jacobian drops below 0.55 (the surface about to fold) and fades over ~4 s, so whitecaps trail behind the crests
  (task 68, storm foam on the crests).
* **Shared code**: experimental 2's shader body moved to WaterSea.gdshaderinc; experimental 3 only defines FFT_OCEAN,
  which swaps the wave function for three texture reads (cheaper than the 64-wave loop). Shore waves, rivers, foam,
  lighting and reflections are the same code. The crest glow is ×0.45 in experimental 3 (its crests are broader).
* **Fading**: the 59 m and 7.9 m cascades fade once their longest waves are a few pixels (vertices) long; the big
  waves die toward the shore (the shore waves take over) and lakes carry only the short cascades by fetch.
* **Swimming and boats**: the CPU sums cascade 0's waves longer than ~20 m (displacement undone by fixed-point steps)
  with the shader's depth and exposure weights.
* **Fallback**: the Compatibility renderer (Run (old or office PCs).cmd) has no compute shaders: experimental 3 shows
  experimental 2 there.
* **Round 24 polish**: mipmapped displacement and slope textures (a compute pass writes each level through its own
  array view; vertices pick the level from their spacing): distant sea band-limited — from 260 m up experimental 2
  showed a checkerboard interference, experimental 3 none. Sea-state and wind changes fade in over 4 s (previous and
  new spectrum blended on the GPU; the CPU height blends alike) and the spectrum is rebuilt only on real changes
  (sea state ±.02, wind ±.1 rad). Lake chop grows with the wind (×0.6 at calm … ×3 in a storm): only the sea's short
  waves reach lakes, and those barely grew with the sea state.

## D64 — Round 24: underwater light — shafts and caustics (2026-10-07)
Tom (five diving photos): "do you think this will be possible to achieve? Those underwater shafts?" Tom then asked to
finish water before anything else (water focus: underwater light → experimental 3 polish → his notes → final style).
* **Shafts** (Underwater.gdshader): each pixel marches its view ray through the water (32 samples on "full", 14 on
  "simple", crowding near the camera: st ∝ u²). At each sample it follows the refracted sun ray (Snell, n 1.33) up to
  the surface and asks how much light that surface point focuses down to this depth. Henyey–Greenstein forward
  scattering (g .75): the fan is brightest looking toward the sun. Absorbed along both paths (red first).
* **Light pattern**: experimental 3 uses the real surface: the curvature ∇²h of the 7.9 m cascade (near the surface)
  blending into the 59 m one (deeper), a thin-lens focus 1/(1 + ∇²h·d/4). Crests are converging lenses, so the
  shafts move with the waves overhead. Other water styles: a drifting ridged-noise caustic net (thin bright lines).
* **The bed**: the same pattern at the bed depth draws the dancing caustic net on the ground seen under water; looking
  up inside Snell's window, the sun is a bright flickering spot.
* **Noise**: one frame, no history — interleaved-gradient jitter drew a diagonal hatching, white noise heavy grain;
  partial white-noise jitter, near-weighted samples and a pattern that blurs a little with distance settle it.
* **Setting** "Underwater light": off / simple / full (default full; the office PC's integrated GPU may want simple).
* **Not yet**: shadows of cliffs, overhangs or boats in the shafts (the terrain is a heightfield: no caves yet).

## D65 — Round 24: experimental 3A, sun blob, alpine vegetation, seabed digging (2026-10-07)
Tom and Noor (docs/design-inbox/THORNWAKE_EXPERIMENTAL3A_PROMPT.md, dumps 20261007-17xxxx/181232): keep experimental
3's waves; fix foam that reads as paint and swings back and forth, water too matte, far sea too smooth.
* **Experimental 3A** is a new water style (experimental 3 unchanged). The FFT foam field lives in its own ping-pong
  textures and is advected by the wind drift (~3 % of the wind speed) on the GPU; the shader reads it where the
  water is *shown* (rest position + displacement): read at the rest position it swung with every orbit — the
  "oscillating streaks". Birth threshold and lifetime grow with the sea state (a breeze: few short-lived caps; a
  gale: whitecaps that trail), the 7.9 m cascade hardly makes any (it made white chips).
* **Foam pattern** (foam3a): broken patches made of bubble cells on warped coordinates, threads of varying width with
  gaps, bubble clumps in dense foam, a solid core only in fresh foam; brightness varies inside (no flat white) and
  older foam lets the water through. No soft far-away cover, no wide stretch, no milky under-layer around old
  foam — those made the "combed paint" patches Tom circled. Far away the lace coarsens instead of blurring.
* **Optics**: stronger reflection in rough seas, small facets catch more sky (Fresnel on a less softened normal), a
  broad sun sheen under the glints, ~20 % clearer sea shallows, less diffuse "paint" in the water body.
* **Far sea**: anisotropic sampling of the FFT textures, the 59 m and 7.9 m cascades fade later, the drawn
  procedural chop halved on the sea (it blurred the simulated detail).
* **Sun blob** (dump 174752, natural look, glow 1.5, haze 2, low sun): isolated by switching effects off — mist
  no, glow yes. With the HDR threshold at 1.3 the bright sky and sunlit clouds around a low sun all glowed and
  merged. Natural: threshold 2.6, luminance cap 4, glow from the small blur levels only. Sun shafts gather only the
  disc and inner halo (they counted a hazy quarter of the screen as "sun").
* **Alpine** (dump 172215: 2.1 km, 4.6 °C, bare): the tree line (fixed 1.75 km) and grass line (fixed 1.7 km) now
  follow the climate in worlds with biomes — trees thin from +2 °C to −1 °C, grass from +1 °C to −3 °C. Worlds
  without biomes keep build 9's lines (fixtures unchanged).
* **Wide rivers in a storm** (dump 172621: glassy river between whitecapped water): rivers wider than ~40 m get the
  FFT lake chop and caps by their width (FFT styles). Not verified on Tom's world (custom type only on his PC).
* **Seabed digging** (dump 181232): the local water simulation joined its flat sheet over dug sea cells (meant for
  dug channels beside rivers): a calm strip over the storm waves. Sea cells are now flagged and never drawn over;
  the sea's own surface covers holes in its floor.
* **Debug dumps** now carry a custom world type (replayed with --type-file=): Tom's WAYFARER world could not be
  rebuilt from his dumps.

## D66 — Round 25: test sliders for view range and terrain detail, plant shadows, god ray contrast (2026-10-07)
Tom asked for smooth sliders to benchmark on his GPU (this machine renders on the CPU, so it cannot judge frame
rates), plus grass shadows and more visible god rays.
* **Terrain detail range** (×1–×4, whole steps): the hole radius of every LOD ring (k tiles; level 0 spans
  (4k+2) × 64 m). Whole steps because the rings nest on even tile boundaries. ×1 = build 32. At ×3 and above the detail
  rings outgrow the outermost ring, which then drops; the next ring covers farther, so the view gets a little longer.
  Checked headless: the streamer settles at every value (×1: 19 s, ×2 with ultra mesh: 100 s on this CPU).
* **Terrain mesh detail** (normal/high/ultra): cells per tile beyond level 0 ×1/×2/×4, capped at 128; skirts still
  hide the seams (render at ×2 ultra: no cracks).
* Grass and flora are still built on every level-0 tile and culled by visibility range, so a wider detail range
  also builds more plants (2.6× instances at ×2). Kept: building grass only near the player would leave tiles bare
  when walked into (tiles are not rebuilt by distance). Noted for Tom's benchmark.
* **Grass and plant shadows**: an option, off by default (shadow cost scales with the instance count).
* **God ray contrast** (0 = build 32): a first try gathered farther with a slower falloff, and the bright sky around
  the sun added up into the round 24 blob again (render). Now contrast, not light: the sun shafts compare each
  pixel's path with its neighbours rotated ±1.5° around the sun (brighter gaps, darker lanes; an open sky is
  unchanged), and the light beams subtract more of the even haze and boost the rest. It also raises the sun's
  volumetric fog energy (1→3) and anisotropy (.65→.85). Strength ceilings raised: sun shafts 3, beams 4.

## D67 — Round 25b: lake reflections, alpine cover, ground variety and relief (2026-10-07)
* **Reflections** (Tom: alpine lake mirrored nothing, even close to the cliff). Debug renders (exit reason, hit
  colour) found three causes: the march (32 steps ×1.18) ended at ~280 m; a fade over the step count dimmed hits in
  the last steps (the far shore) to nothing; and the material's SPECULAR made Godot add the plain sky's radiance on
  top, washing out what the screen-space reflection found. Now 48 steps ×1.22 (~16 km; medium 24 ×1.38, ~2.4 km),
  4-step refinement on a hit, fade only at screen edges, engine specular ×(1 − 0.9·hit), and the ray follows the
  softened normal (as the reflection strength) so wind ripples break the mirror instead of sending rays skyward.
  Classic and experimental 1 keep their own (backup) shaders.
* **Alpine**: grass with the cold from 0 to −6 °C (was +1 to −3), tree line +1.5 to −2.5 °C (was +2 to −1); the
  Alpine biome's TreeDensity 0.25 → 0.45 and Grass 0.85 → 1. Vegetation only — terrain fingerprints unchanged.
* **Ground variety / relief** (options, Noor ground only, default on; 0 = build 33). Bare patches come from data
  already per vertex (plant-field cover, the biome's plant amount, mountain share, snow, wetness), so no new vertex
  channel: soil, gravel when cold, sand when dry, moss when wet, tinted toward the biome's dry colour. Relief is
  derivative bump mapping from a linear-filtered copy of Noor's textures (the nearest-filtered one gives stair-step
  bumps), within ~25–70 m, plus roughness per ground and darker crevices. Textures and derivatives run in uniform
  control flow (no per-pixel branches), and the bumped normal is guarded against a zero vector.
* Note for later renders: replay `--fly-height` is above the bed — over a 110 m deep lake the camera was underwater
  (black frames), not a shader fault.

## D68 — Round 26: ghosting, experimental 4 water, ground parallax (2026-10-07)
* **Ghosting**: TAA reprojects with motion vectors; the water is transparent (no motion vectors, vertex-animated),
  so storm crests smear, and the volumetric fog's temporal reprojection trails behind moving things. Not fixable
  inside TAA, so choices: SMAA (Godot 4.5, spatial, no history) and FSR 2 (Godot's Scaling3DMode.Fsr2: temporal AA +
  upscaling); and the fog's reprojection amount as a setting (0.5–0.95, 0.9 = before).
* **Experimental 4** (a test, as Tom asked): EXP3A plus EXP4 — from sea state ~0.3 to 0.9 the screen-space hit
  weakens (×0.4), Fresnel ×0.7, the reflected light ×0.6 and the engine specular ×0.5; calm water identical to 3A.
  Render on Tom's storm dump: the silvery sheen gives way to the water's colour (−15 % brightness), shallows unchanged.
* **Ground parallax**: steep parallax (8 layers, 5 cm) on the grass texture, or soil where the variety pass lays bare
  ground (its weight estimated before the shift), within 12–30 m on gentle dry ground; the whole terrain pass then
  reads the shifted coordinate. The loop samples at an explicit mip (derivatives of the unshifted lookup, taken
  outside the per-pixel branch).

## D69 — Round 27: simulated weather, lightning and thunder, plant wind, storm visuals (2026-10-08)
Tom: "proper semi-realistic weather simulation, lightning storms and thunder with nice sound propagation… wind on
plants is broken… waves of grass… stormy clouds… distant rain visible… denser rain… raindrops on water… waves
pouring further onto the shore". Return point first: build 35 (`2ed21e8`, SNAPSHOTS); everything new sits behind
*Weather model* (classic = build 35).
* **Weather field** (WeatherField.cs): deterministic in space and game time, evaluated on demand (nothing stepped
  or stored, so teleporting and screenshots are exact). Two synoptic layers drifting at different speeds (systems
  deepen and fill), ridged frontal bands across the steering, and thunderstorm cells (one candidate per 30 km grid
  cell in a slower frame, a 2.2–3.8 h life per 8–16 h period, fuel = warmth × humidity × unsettled systems ×
  afternoon). Rain threshold from annual rainfall. Probe (`--weather-probe`): 2–3 % of the time raining at
  ~250 mm/yr, 12 % at 630, 31 % at 1080; up to 6 cells within 70 km.
* **Local weather** reads the field at the player (eased as before); distant storms, the mackerel sky and the rain
  forecast come from real cells and the field ahead. Surface wind follows the steering (±35°) plus outflow from
  nearby cells; the sea state builds from the steady wind over ~1.5 min and calms over ~3 (Sea state setting × 0.6
  as background swell).
* **Lightning** (Lightning.cs): strikes per cell at ~4 s / intensity²; midpoint-displacement bolts with branches as
  camera-facing ribbons (width ∝ distance, ≥ 4 m) and a held strike for screenshots; land flash ∝ e^(−d/5 km); a
  `lightning` global lights the sky toward the strike (a broad lobe close by, low and narrow far off, mostly lost in
  daylight). **Thunder** is synthesized per strike from the bolt's segments: each arrives at its distance / 343 m/s
  (the roll), weights by thickness / distance, rings ~0.3 s, five land echoes 0.4–3 s, a decaying envelope; a
  two-pole low pass at 70 + 3800·e^(−d/1.8 km) Hz (air absorption) plus a brown-noise body, a broadband crack under
  1.6 km; played after the delay from the bolt's direction at ∝ 1/d. Rain and wind sound from a generator stream.
* **Plant wind** (WindSway.gdshaderinc): gust patches advected downwind at 0.7 × the steady wind (C# integrates the
  travel, so speed changes never jump), lean = h·(1 − e^(−0.8·s²·(0.3 + gust)))·0.6, natural swing per plant
  (grass ~0.7 Hz, flowers ~0.7, shrubs ~0.45, crowns ~0.25), faint flutter, tip dip on the arc. The old version read
  the fast gust value (changing within a second) for every plant at once: the jiggle. The `wind` global now carries
  the steady speed in the simulated model.
* **Weather map** (WeatherMap.cs): 64 × 64 over 128 km, rebuilt on a worker thread every 2 s or 4 km (field.At is
  given its own scratch list). The sky takes cover from it (flat and volumetric), raises thunderstorm towers (up to
  ~5.2 km), and draws the low grey deck only toward rain; the far storm towers point at the strongest real cell.
  **Rain curtains** (RainCurtains.gdshader): 28 steps along each view ray from 1.5 to 60 km below the cloud base,
  rain × streaks (columns, leaning downwind) → a grey veil, darker under storms.
* **Rain**: 24 000 particles in the simulated model, ratio p^0.8 × (0.7 + 0.45·gust) × Rain strength / 1.6;
  near streaks fade within 1.2–5 m (close ones drew white poles). Raindrop rings: three cell layers, a ring and a
  first-instant splash as slope, within 35 m. Swash +0.55 m at sea states above 0.55.
* The Cloud speed setting multiplied the wind speed: with Tom's 4 the simulated sea saw 63 m/s. In the simulated
  model it only moves the clouds.
* The GitHub integration cannot create repositories (403): Tom creates the empty dedicated repo; this session then
  attaches it and pushes the history.

## D70 — Round 28: horizon land (2026-10-08)
Tom: "hide the hard cutoff of the terrain in the far distance — from a mountain top you see it". The streamed
rings end in a square (~20 km at the default Terrain setting) and the sky showed straight behind it.
* **HorizonLand.cs**: one mesh of 40 concentric squares (48 vertices a side) from the exact outer edge of the top
  ring (tiles cx ± TopRadius) out to ~100 or ~250 km, the squares growing geometrically; heights from the terrain
  sampler at half the local spacing, water flat at its level (sea/lake colours, smoother), land in the blended
  biome grass/dry colours by the biome's plant amount, darker for forest, rock by the slope, snow from the climate,
  a pale strip at the shore. The Earth's curve (d² − inner²)/2R keeps the join level and drops the far land below
  the horizon (true horizon ~180 km from a 2.6 km summit, ~250 km from 5 km up). A skirt 400 m down under the inner
  edge hides gaps against the terrain. Normal Godot fog fades it.
* Built on a worker thread when the top ring moves (~8 km), placed by the float origin each frame; screenshots
  wait for it. The first render showed nothing: the strips faced down and were back-face culled — the material
  draws both sides.
* Renders (2.5 km up, thin haze): the square plateau edge against flat teal becomes hazy land to the horizon; over
  open sea no seam.

## D71 — Round 28b: raindrop splashes, regional wind, shared lightning (2026-10-08)
Tom: "love the raindrops on water… could we also add that splash effect?" and, on "is the wind global?" / "how
heavy in multiplayer?", agreed ("ok sounds good") to a regional wind computed from the shared weather field and to
lightning every player sees the same.
* **Splashes** (RainSplash.gdshader, Atmosphere): 1800 GPU particles, 0.32 s each, emitted in a 24 m square at the
  water surface below the camera (WorldLab looks for the nearest water every 0.25 s). Each one is a billboard crown:
  a ring spreading out and a few droplets thrown up and falling back. Amount follows rain × Rain strength, fewer
  on rough water; tinted by daylight. Simulated model only, not in snow, and only within 60 m above the water.
* **Regional wind** (WeatherField.WindAt): the steering flow slowed to ~0.45 at the ground and turned by the
  systems; ridges/hills above the 4 km mean height up to ×1.7, hollows down to ×0.65; calm in the lee of higher
  ground 3–6 km upwind (to ×0.4); channelled along valleys; sea/land breezes from the land/sea 3 km either side
  and the hour. Deterministic in seed, position and game time, so every client computes the same wind with no
  network traffic. The player's wind (sea state, rain slant, sound) samples it every 0.5 s; a 48×48 **wind map**
  over 12 km (WindMap.cs, worker thread, a few ms per refresh) lets grass, plants and crowns read the wind at their
  own position (WindSway `wind_here`), carrying the player's storm gust and outflow ratio. Probe: a lake valley
  3.4 m/s while the ridges above had 8–12 m/s at 14:00; 1.3 m/s at 03:00.
* **Shared lightning**: each storm cell has a stable Id; strikes are drawn per cell per game-time slot
  (1.2 real seconds) from a hash of (Id, slot, k) with probability intensity² × 0.6, and the bolt's shape is
  seeded from the same hash. Two players under the same storm see the same bolt at the same moment (each hears it
  after their own distance ÷ 343 m/s). Cost: nothing extra per player — no strike messages needed.
* Classic weather model unchanged (one wind everywhere, wind map off).

## D72 — Round 29: sun blob, clouds v2, foam net (2026-10-08)
Return point before this round: build 38 (`2df30d8`, docs/SNAPSHOTS.md). Every change is an option or a fix that
keeps the previous look selectable.
* **Sun blob** (Tom: the sun "blobbing 80 % of the screen"). Reproduced with his build 33+ settings (dumps33: God ray
  contrast 1, sun shafts 3, light beams 4, glow 1.5) at 15:30 facing the sun: a white disc half the screen wide.
  Isolation renders: contrast 0 → clean; light beams 0 → blob stays; sun shafts 0 → mostly gone. Cause: God ray
  contrast raised the sun's volumetric fog energy to 3 and anisotropy to .85, so the haze around a low sun passed the
  shafts' brightness threshold over a quarter of the sky, and the shafts gathered it into the disc. Fix: the shafts
  gather only sky within ~8° of the sun (the disc and inner halo, however bright the haze); contrast now raises fog
  energy to 1 + 0.8c and anisotropy to .65 + .1c. Same scene afterwards: a natural haze glow.
* **Clouds v2** (*Cloud quality → volumetric v2*, ideas from SunshineClouds2, docs/SKY_IDEAS.md). The sky's cloud code
  moved unchanged into SkyCommon.gdshaderinc; the march was generalised (any camera height, a distance limit,
  minimum step) and CloudsNear.gdshader, a full-screen pass, marches the same volume from the camera to the scene's
  depth over geometry (the sky keeps its half-resolution march behind it). The cloud base is no longer kept ≥ 0.8 km
  above the camera, so clouds lie on the mountains, fill valleys below a summit and can be walked into. Ray offsets
  from interleaved gradient noise, moving every frame under TAA/FSR 2. Inside a thick deck the light is grey-white
  scattered sunlight (first renders: deep blue, then dark grey fog). Haze toward the horizon also when looking down.
  Not yet: amortised far clouds (item 2), the physical sky tables (item 3).
* **Foam net** (*Foam style → net*, Tom liked emilje/godot-water-shader's foam; look only, nothing copied). FoamNet.cs
  bakes a seamless 512² texture at start-up (warped Worley cells: round clear holes of different sizes, thick
  fluffy foam between, small holes in thick parts, faint cracks; a coarse and a fine net and patch noise). The
  foam layers sample it at ~8 m and ~3 m, the fine net taking over in fresh foam; whole patches thick or bare; the
  net shows where foam thins, fresh breakers stay solid. Three tunings by render (too wide → flecks → between).
* Tom's other water ideas (sea spray, intersection foam, water queries, spline water, bicubic normals, warped
  cascades) are planned in docs/WATER_EXPERIMENT.md.

## D73 — Round 29b: storm sky, biome skies (2026-10-08)
* **Storm sky** (Tom, dump fa9d900c, swamp in a storm: "they cover the whole sky and turn a bit darker, but just a grey
  darker sky doesn't build atmosphere"). The sky mixed a flat grey overcast over everything at 85 % of the storm
  strength, washing out every cloud. Simulated weather now draws a storm deck instead (SkyCommon storm_deck): a low
  deck (~900 m) with big swells of thick and thin cloud, rolls lying across the wind (taps smeared across it), a
  lumpy underside (bulges dark beneath), slate-blue near-black cores, thin parts glowing on the sun's side, darker
  under thunder; ragged scud (~450 m) stretched along the wind and moving at twice the deck's drift; a paler band
  where the deck ends at the horizon. All scales divide the low layer's 108 km wrap (no jumps). The storm here also
  sets the deck overhead (forced rain, H). Classic weather model unchanged.
* **Biome skies** (Tom: "don't seem to change anything"): the biomes' sky tints are near white (5–20 %) and were
  scaled ×.6 / ×1.4 — invisible. Now ×1.5 (subtle) / ×2.6 (strong; ×3.2 turned a desert sky mustard), and the tint
  also colours the sunlight (35 %) and the volumetric fog (60 %).
* **Audio tab** (Tom: "I like the sounds, wind especially; rain is good but could you soften it a bit, and an audio tab
  in the options"): a new menu tab with Master volume (the master bus), Weather sounds (all, the old setting moved
  here), Rain, Rain tone, Wind, Thunder. Rain tone low-passes the hiss (it was raw high-passed white noise) and
  quietens the hiss and the drops; default 0.35 (soft), 1 = build 39's crisp rain.

## D74 — The rewrite moves to its own repository (2026-10-09)
Tom: "keep this repo as is and make a brand new one for this version with all the files". `badsadcode/Thornwake_Rewrite`
(private) received the full history of `rewrite/main` (276 commits, same ids) as `main`; `badsadcode/Thornwake` stays
untouched. Work continues on `main` here. Tags for the return points could not be pushed from the cloud session (the
git connection refuses tag pushes); `docs/SNAPSHOTS.md` lists every commit to tag from GitHub if wanted.

## D75 — Godot 4.7.2 (2026-10-09)
Tom: "If you will be able to go back to the previous version, then go for it." The project moves from Godot 4.5.1 to
4.7.2 .NET (still .NET 8). Assessment and changelog notes: `docs/GODOT_4_7_UPGRADE.md`.
* **Return point**: branch `godot-4.5.1` (= `460d823`, build 40 plus docs) and the build 40 EXE (`SNAPSHOTS.md`).
* **Same look first**: `rendering/environment/fog/use_legacy_blending = true` keeps 4.5's fog blending (without it the
  god rays, haze and mist came out hazier and ~20 % brighter). Tom and Noor plan to rewrite the atmosphere from
  scratch, so no time goes into retuning the old one for 4.6's fog and glow changes.
* Changed: the SDK line in `Thornwake.csproj`, `NuGet.Config`, `TEST_*.cmd`, `tools/build.ps1`, `tools/bootstrap.ps1`,
  `tools/setup-cloud.sh`, `project.godot` (feature tag 4.7). The re-import rewrote 52 `.import` files (new importer
  options at their defaults) and added 51 `.uid` files Godot 4.7 wants for every script and shader. No game code changed.

## D76 — Round 30: benchmark, tree field with impostors, Display tab (2026-10-11)
Tom: tree performance first ("identify the actual bottlenecks … maintaining beautiful, dense forests is important"),
an automated benchmark for volunteers on many PCs, and a Display tab (native 1920×1080, window modes, ultrawide).
* **Benchmark** (`docs/BENCHMARK.md`): fixed scenes in one deterministic world, quality profiles, frozen time/weather,
  V-Sync off; per-frame frame/GPU/CPU/script/commit times, hitches, draw calls, memory, loaded content; then the cost of
  each object kind (own render layers, hidden instantly) and of each effect. results.json + summary.txt + CSV.
* **Measured** (`docs/PERFORMANCE_TREES.md`): sub-pixel 3D trees (≈590–1,900 triangles each) and ≈4,000 draw calls
  are the bottlenecks, not the forest density.
* **Tree field + impostors + cross-faded hand-overs**: 3D trees in 128 m cells with bigger batches, impostors baked from
  the real models in 1 km cells, dithered hand-overs on circles. Setting *Tree impostors* (default ~3.2 km); *off*
  keeps build 41's renderer exactly. Batch fills in one call (commit spikes).
* **Display tab**: windowed / borderless / exclusive fullscreen, monitor, sizes for every aspect ratio that fit, UI scale,
  keep-or-revert; stretch aspect "expand" so ultrawide screens get more room, not black bars. Fullscreen always uses the
  monitor's resolution (Godot does not change video modes); Render scale lowers the 3D resolution instead.
* Debug dumps' Reproduce lines fixed (custom worlds replay).

## D77 — Round 30c: collision back to build 43, landing on the real ground, a collision test (2026-10-11)
Tom, build 44: "when I'm walking towards a rock and I hit it, I get rubber-banded back … with the new collisions, I'm
able to go up on high slopes … the character is jittering on the ground … I'm not able to jump."
* **Build 44's collision is gone** (heightmap ground and shapes added straight to the physics server, `3af5bb5`/`a8d272c`).
  The ground is the triangle shape again and every trunk, rock, log and bush its own CollisionShape3D, exactly as
  build 43. What stays from build 44: collision is built *after* a tile appears, nearest tile first, and the ground now
  in eight 64 × 8 m pieces (1,024 triangles, ~1.5 ms each here) within the frame's 8 ms streaming budget. A whole tile's
  ground in one go cost ~18 ms (up to 44 ms): that was the hitch build 44 went after. The ground under the player waits
  for its collision, as before.
* **Landing on the real ground.** The collision mesh is the terrain sampled every metre, so at a cliff step (the
  ground drops metres between two grid points) it runs up to ~4 m above the exact terrain height. Teleports, map
  jumps and the "never fall through the world" net put the player on the exact height: under the visible ground, where
  the net then caught the player every frame (not on the floor: no jumping, jitter) or lifted the player up cliff
  faces. Now all three use the collision surface (a ray straight down; trunks, stumps, bushes and logs skipped), and the
  net only acts when the player really is below it.
* **Collision test** (`--collision-test`, `tools/collision_test.sh`, run before every build): in Tom's test world at
  three places, standing still (no jitter), jumping, walking and sprinting into a trunk and a boulder (stopped, not
  pushed back; walking up a boulder's side is allowed where it is gentler than 50°, like any slope), a slope steeper
  than 50° (not climbable), landing on a cliff step (standing on the ground), and every rock kind's hull. It picks its
  sites itself: clear, walkable approaches, boulders standing at least 0.6 m out of the ground (rocks are partly
  buried: a "boulder" whose top is 2 cm above the ground is not an obstacle).

## D78 — Round 30d: streaming without starving the main thread (2026-10-11)
Tom's build 45 benchmarks: flight 1 % low 25 FPS; single frames spent 35–41 ms on one 1,024-triangle collision piece
(normally ~0.7 ms) and up to 24 ms adding a forest tile's object shapes.
* **Fewer background builders at once** (`Background.Run`: tiles, tree cells, horizon, weather and wind maps): tiles
  cores − 2 (at most 12), tree cells cores / 4 (2–6). Before: cores − 1 and cores / 2, i.e. 47 CPU-bound jobs on Tom's
  32 threads, and the main thread lost its core mid-frame. (Build 46 also ran them below normal priority; build 47
  dropped that: Windows schedules strictly by priority, so on 4–6 core PCs the builders could starve behind the game's
  own threads.)
* **Collision steps**: ground in 1,024-triangle pieces, then object shapes in batches of 16, at least one small step
  per frame and more within the 8 ms budget; the ground under the player is what holds the player. *Settled* (what
  screenshots wait for) no longer includes collision; the benchmark and the collision test still wait for it.
* **Terrain and water mesh arrays** are converted to Godot arrays on the builder threads (half of a tile's main-thread
  mesh commit).
* **Quit**: the world scene collects .NET garbage when it exits (tens of thousands of wrappers finalised after
  Godot's C# layer shut down aborted the quit now and then).
* Settings unchanged. The `treedetail` results confirm the defaults (simplified tree shadows, full detail 140 m):
  `docs/PERFORMANCE_TREES.md`.

## D79 — Atmosphere 2.0 approved (2026-10-11)
Tom and Noor approved the revised plan (`docs/ATMOSPHERE_2_0_SAGE_FEASIBILITY_REVIEW.md` §10) to start after build 47
is tested and a rollback snapshot is secured.
* **Visual quality first**, stylized and slightly dreamy, varied (bright and welcoming to dark and oppressive), not
  photorealism; Valheim's forest light as inspiration, not a copy.
* **Freedom to replace** what does not work; prefer the simplest solution that convincingly reaches the look.
* **Baseline: Look natural (ACES)**; alternatives stay until compared visually. Tom provides reference images.
* **Forest rays**: both approaches (tuned native volumetric fog; canopy shadow map + own ray march) compared on look
  and total cost. **Clouds**: structure, lighting, near detail, silhouettes, mountains and horizon first; 1.7 ms is a
  target, not a constraint.
* **Five behavioural biome profiles**: temperate forest, plains, taiga, desert, swamp; they respond to weather, time,
  terrain, vegetation and local conditions. No permanent forest haze.
* **Iterative**: small milestones, each a Windows build with captures, moving-camera tests (forest walk, toward and
  away from the sun, lakeside, flying through clouds, mountains) and the benchmark; rollback points before significant
  rendering changes; pause for feedback before significant artistic decisions; 8–10 builds as a target, not a deadline.
* **Not another optimisation project**: then roads, settlements, wildlife, NPCs, resources, caves, gameplay.
* Known visual artefacts stay tracked (review §10) until fixed.

## D80 — Atmosphere 2.0 M0: capture suite (2026-10-12)
The milestones are judged on captures, so the captures must be repeatable: the benchmark gains an `atmosphere` suite
(ten scenes, fixed places, times and weather, in a biome-patch version of Tom's test world; scripted turn / walk /
flight moves; nine stills per scene in the results folder, HUD hidden). Scenes may name their own world and settings;
`--bench-scenes` runs a subset; `--find-sites` locates biome sites. Build 47 (branch `pre-atmosphere-2.0`) is the
baseline every milestone is captured against.

## D81 — Atmosphere 2.0 M1: light model 1 proposal behind the switch (2026-10-12)
*Light model* (`fx.light_model`) 0 stays build 47 exactly (sky contribution 1, exposure multiplier 1): the return
point. Model 1 is the M1 proposal for pause point 2, all numbers in curve tables in `AtmosphereLight.cs`: the sun by
phase (blue hour, golden hour, morning, high noon); a cool fill in the shade (12–30 % of the sky light replaced by a
blue tint by phase, near-neutral grey under overcast); and a narrow, scripted eye adaptation instead of Godot's auto
exposure (which washed out overcast): up to +0.5 stop in a closed forest (forest density around the player, sampled
4× a second) and up to +0.25 stop for a dim golden hour or overcast day, never at night, adapting over ~1.5 s, snapped on
teleports and A/B stills. The scripted version is deterministic, costs nothing, and can be matched to references by
editing numbers. Bloom (pause point 3) is not touched yet. The benchmark gains A/B stills (`--bench-compare`), so the
two models are compared on identical frames.

## D82 — Light model 1 tuned to Tom's references (2026-10-12)
Tom's nine references (six Valheim screenshots, three painted skies; `docs/ATMOSPHERE_2_0_REFERENCES.md`) were
measured region by region. They disagree with model 1's first guess in one clear way: the shade is **teal-cyan**
(hue 185–200), not violet-blue; dusk is lavender to pink-violet; night is deep saturated teal; the haze glows warm
toward the sun. Model 1 now uses those hues (shade fill, a bigger fill share at night, haze colour by phase, fog sun
scatter 0.42 at a low sun → 0.2 at noon, night haze `0d2a36`). Build 47's model is untouched. Bloom, rays, clouds and
distance haze from the same references are assigned to pause point 3, M2, M3 and M4.

## D83 — Light by biome, time and weather: light profiles (2026-10-12)
Tom: "colors depend on the biome the player will be in, the time of day, weather — that was just an example." So light
model 1 has no global palette: `Content/light_profiles.json` holds five profiles (temperate forest, plains, taiga and
cold lands, desert and dry rock, swamp and wet lowland; every one of the 18 biomes is assigned, unlisted ones use
temperate). Each profile: shade-fill and haze curves over the sun elevation, night haze, overcast and storm colours,
sun tint, fill share by day and night, sun scatter and an exposure bias. The profiles of the biomes at the player are
blended by weight, drifting over ~3 s (snapped on teleports and A/B stills); overcast and storm move each profile
toward its own overcast/storm colours. The temperate profile carries D82's values, so the references still hold
there; the others are first proposals to be judged in captures. Tuning is editing the JSON, not code.

## D84 — Light model 1 nights: dark but readable; captures wait 30 frames (2026-10-12)
A/B night captures: build 47's night forest is near black (mean ~3/255), model 1 only slightly lighter. Tom's night
reference is dark but readable (silhouettes against teal mist, a moon halo). Model 1: moonlight ×1.5, sky light at night
0.16 → 0.2, up to +¼ stop of night exposure (on top of the forest lift; cap ×1.5). Build 47's model unchanged. The
benchmark now waits at least 30 frames before measuring: the sky's radiance map follows a jump in the time of day over
several frames, and on a slow machine the 3 s warm-up was one frame (the first night still was lit by the noon sky).

## D85 — M2 forest rays shoot-out: two candidates behind *Forest rays* (2026-10-12)
The approval asked to compare both forest-ray approaches before choosing. Setting `fx.forest_rays`: *build 47*
(default, unchanged), **A — volumetric forest air**: a thin 3D-noise haze under the crowns in a 160 m box around the
camera (`ForestAir.gdshader`, a FogVolume), lit by Godot's volumetric fog through the sun's shadow map, so the shafts
are real (they come through actual gaps, also with the sun off screen), with the sun's light into the fog raised up to
2.8× in a closed forest; costs the volumetric fog pass. **B — screen-space shafts**: the existing radial shafts, up to
2.5× inside a closed forest (on there even when *Sun shafts* is 0); cheap, but only with the sun on screen. Both follow
the canopy around the player (none when flying above it), the sun's height (none at night) and storms. Captures:
suite `rays` (forest shafts facing the sun, forest walk, forest noon; every still in all three modes). Tom chooses.

## D86 — Tree detail hand-over dithered in the shaders; no stale weather map (2026-10-12)
Tom's dump 104752: a grey-blue band at eye height across tree crowns. Cause: the detailed → lighter tree model
hand-over (at *Full-detail trees*, 80 m there) used Godot's visibility-range fade *Self*, which draws a fading object
in the transparent pass — out of the depth buffer the full-screen effects read. The distant rain (a full-screen pass
that only draws beyond 1.5 km) saw those crown pixels as open air and painted its haze over them; it was on although
the weather was off, because switching the weather off left the last weather map active. Fixes: the hand-over now
uses the same per-pixel ordered dither as the 3D tree → impostor hand-over (`TreeFade.gdshaderinc`, global `tree_lod`,
per-MultiMesh `tree_lod_role`; canopy, bark and placeholder-plant shaders), so the trees stay opaque and in the depth
buffer; with the weather off the weather and wind maps are disabled. Grass and scatter still use Godot's fade at their
own distance limit (near the ground, below the horizon; noted).

## D87 — Cloud seams wait for M3 (2026-10-12)
Tom and Noor agree: the remaining cloud artefacts (quality *volumetric*: mountains cut their silhouettes out of
clouds; *volumetric v2*: a flat, hard-edged patch where clouds in front of far land meet the sky's clouds) are not
patched further in the current renderer. M3 replaces the cloud model with one cloud pass drawn in front of and behind
the land alike, and these two shots (dumps 115949 / 120402, Tom's office PC) become its acceptance captures.

## D88 — Lake waves fade in the shallows; weather off resets the wind (2026-10-12)
Tom's dumps 131735 / 131752 / 131957: water standing metres above flooded lake margins, with walls where it met a
river. Water experimental 3/4 give lakes fetch-limited FFT chop that grows with the sea state, but, unlike the sea's,
with no shallow-water fade: in 0.3–2 m of water a big lake carried full storm waves. They now fade between 0.15 and 3 m
of depth (verified on dump 131752's spot: a calm, clear margin; the deep lake keeps its waves). The gale itself was
Tom's *Wind strength* at 4 (the maximum: the classic wind's 5–10 m/s × 4, with the weather off); not a bug. Turning the
weather off now also hands the wind explicitly back to the classic model (the early return left the last simulated
steering in place).

## D89 — Terrain revision 15: lake outlets keep the lake's level inside the lake (2026-10-12)
Tom: "holes in lakes … like a crater in water, steep walls of water". `--probe-water` (new developer tool) showed it on
every lake of his world: `RefineSurfaces` started each lake's outlet river at the lake level and then let it follow
the ground along its course, which inside the lake is the lake bed — all 15 outlets ran 3–113 m below their lake's
surface, a trench of water across the lake (a crater where a short stretch lies inside). Revision 15: the outlet keeps
the lake's level while its course is inside the lake (within 1.6 cells, as the lake's own reach); and in the sampler a
river point inside a lake reach, under the lake surface and below the lake's level, is lake water (outlets that leave
and re-enter). After: no trench in any lake; at the outlet most lakes step down 0–4 m, three by 8–114 m (perched lakes:
physically waterfalls, drawn as a steep sheet for now). *Terrain version* gains **Build 51** (revision 14) as the return
point; its fingerprints are frozen (`terrain-build51.json`, with the build 51 biome catalogue), the current ones
re-recorded.

## D90 — Water tool sizes and slow rain erosion (2026-10-12)
Tom: bigger water tools, and "streams that form during rain could erode the ground — REALLY SLOWLY so the whole world
won't turn into river world on day 2". **Water size** (`,` smaller · `.` bigger, 1–4): G pours 2.5 → 160 m³/s spread over
a disc up to 8 m across (not a column on one cell); J springs give 0.6 → 38 m³/s over the same disc. **Rain erosion**
(setting *Rain erosion*, default 1, 0 = off): a sediment model in the local water simulation — flowing water picks up soil
where it can carry more than it holds (capacity ∝ depth × speed²), drops it where it slows (pools, the foot of a slope,
when the water dries up), carries it with the flow, and loses it into rivers, lakes and the sea. At 1 the bed is cut by
at most 3.6 cm an hour even under a creek-sized stream (rain runoff is far less), so gullies need many storms. Every
10 s, once some cell has changed by 4 mm, the change goes into the terrain edits (as digging: the 1 m vertex takes the
mean of its four 0.5 m cells) and the touched tiles are rebuilt; the simulation's own floor already carries it. Test:
off changes nothing; on, a 0.6 m³/s stream for 10 min cuts 0.6 cm at most, and worn = settled + carried off + in the
water.

## D91 — Terrain revision 16: no sea pockets inside lakes (2026-10-12)
Tom (dump 182351, the benchmark lake): still a crater in a lake — inside it the sea, around it walls of lake water.
`--probe-water` showed a different cause from D89: the lake (level 13 m) lies near the coast, and the coastline's
fractal wiggle cut a small patch of "sea" (land ≤ 0) out of its basin; the sampler returns sea for such points before it
looks for lakes, so the water surface dropped from 13 m to 0 m over a bed at −2.1 m. Revision 16: a sea point inside a
lake's reach (within 1.6 cells, as the lake fill) with its bed below the lake surface is part of the lake. Test: Tom's
world rebuilt from the dump parameters (his spot is now lake at 13 m), and rings of probes around every lake of four
worlds — sea inside a lake: 2 before, 0 now. The preset worlds' fingerprints did not change (none had a pocket).
*Terrain version* gains **Build 52** (revision 15); its fingerprints are frozen (`terrain-build52.json`). Also fixed:
*Terrain version* stopped at index 4, so **Build 51** could not be chosen (it fell back to Build 5).

## D92 — Forest rays: candidate A (2026-10-12)
Tom compared the rays suite (build 52): "both are good, but A looks a little bit better". Cost on his RTX 3080 is the
same within ~1 fps (57 fps over the forest scenes either way). M2 goes on with **A** (volumetric forest air lit through
the canopy's shadow map). B (screen-space shafts) stays selectable until M2 is finished, then becomes a fallback for
weaker GPUs if it is still useful. The defaults do not change while Atmosphere 2.0 is experimental. His remaining note,
"an issue with mountains in the background", is taken as the cloud/mountain seam of D87 (M3) unless a dump says other.

## D93 — Tree hand-over role in the instance data, not an instance uniform (2026-10-12)
First GPU runs on Tom's PC (dump 182351, his settings): ~1,020 Godot errors per run, "Too many instances using
shader instance variables"; in the benchmark forest ~1,200–1,360; with *Trees* 3 / *Far trees* 4, 15,710. D86's
`tree_lod_role` was an `instance uniform`, and Godot gives every instance of such a shader 16 slots of the global
shader buffer (65,536 slots: ~4,000 instances), set or not, so the canopy, bark and placeholder-plant batches and also
every impostor and far-tree batch (whose shaders never read it) took slots. Batches past the limit got none and lost
the detailed → lighter dither. Raising the buffer 4× still overflowed at the heavier settings, so the role now travels
in each 3D tree instance's custom data (`INSTANCE_CUSTOM.x`, 16 floats an instance instead of 12) and reaches the
fragment as a flat varying; the impostor and far-tree shaders define `TREE_NO_LOD_ROLE`. The simplified-shadow twin,
which shared the lighter model's MultiMesh but needs role 1, gets its own copy of the buffer (near cells only).
Result: no errors at either setting; the benchmark forest matches `main` within run-to-run noise (only swaying grass
differs). A fix, no switch (as D86).

## D94 — Why the clouds look weak; *Cloud look* proposal (2026-10-12)
Tom: the clouds look weak (low contrast, little detail, smeared); find the cause before changing anything. Tested on
dump 182351 (400 m up, cumulus forced, 15:00 north and west), one variant at a time against build 53:
* **(a) Wind strength 1 instead of 4**: crisper, lumpier edges and fewer streaks (+3–13 % contrast, +7–8 % detail).
  `wind.y` = speed / 8 and the detail streaking ramps 0.5 → 1.6, so Tom's *Wind strength* 4 (~13 m/s) streaked every
  cloud fully and lowered the clouds' density (softer edges). A real, moderate cause.
* **(b) Godot's fog off for clouds only** (test: CloudsNear draws every cloud, from a twin with `fog_disabled`): by far
  the largest change — bright, clear clouds instead of grey-pink ones. Found while testing it: **Godot fogs the sky's
  half-resolution pass as well as its full pass** (`sky.glsl` skips fog only in cubemap passes; Godot 4.7.2 source), so
  the sky's clouds took the depth fog's sky affect and the volumetric fog twice, and its in-scatter was added even where
  there is no cloud. Clouds in front of the land (CloudsNear) are fogged once: with volumetric fog on, a seam of ~49/765
  across a cloud (split test), ~10 with it off. (The cloud session's split test had no volumetric fog veil.)
* **(c) Cloud aerial perspective halved**: no visible change (±1 %): at 5–15 km it is 0.1–0.3. Not a cause.
* **(d) Stronger self-shadowing**: darker bases, a little more relief (+5–27 % contrast). Helps, but the references'
  bases are light blue-grey (#aeb6c8), not dark.
* Also ruled out: the sky's half resolution (fog off, half vs full resolution: same detail).
**Proposal: setting *Sky → Cloud look*** — *build 53* (default, unchanged; verified identical) or *reference*:
(1) the double fog undone: Godot's fog is x·A + B per pixel; the half-resolution pass writes the clouds × 2048 (B becomes
negligible), a new quarter-resolution pass writes 2048 alone (≈ A × 2048), the full pass divides — the clouds are fogged
once, like the rest of the sky and like CloudsNear (seam 49 → 9); (2) wind streaks from ~12 m/s, full at ~20 m/s;
(3) crisper cauliflower edges (denser, deeper erosion, billows down to a flat base); (4) bases lit blue-grey by the sky
by day (golden hour as before), tops near-neutral with the sun high, a little more self-shadow. Cost: none measurable
(86 vs 91 fps). Artistic: Tom picks; defaults unchanged. Test switches kept for later tuning: `--cloud-split-test=4`,
`--cloud-fog-off`, `--cloud-dev=x,y`.

## D95 — Tom's first play of the reference clouds: tree hole, horizon line, far land (2026-10-12)
Tom played the source build with *Cloud look: reference* ("clouds look good with these settings") and sent dumps
220450–222024. Fixed (technical), each checked on his dumps in an isolated user folder:
* **A hole in the forest under the camera** (dumps 220524, 221042: from 120–155 m up, bare ground in a ring around a
  square of trees). A regression shown by D93, not made by it: Godot switches each tree batch by the **3D** distance to
  its box's centre (`_visibility_range_check`, 4.7.2 source), while D86's dither measured the **horizontal** distance
  per pixel; from above, the detailed batches went off while the lighter ones still left the band to them. Before D93
  most lighter batches had lost their role to the instance-uniform overflow and drew in full, hiding it (commit 5c45cda
  renders the dump whole). Now the dither measures 3D, and the two models' ranges are padded by a cell's half 3D
  diagonal (~95 m) so both are on wherever the dither needs them. The ground forest matches `main` (difference 0.5,
  run noise 0.1); cost in the densest forest at *Full-detail trees* 130 m: 60 → 56 fps. The simplified-shadow twin is
  not padded (+40 % of that cost for little): its shadows end by cell as in builds 51–53. A free alternative (switch
  whole cells, a visible cell edge between the two models at 60–110 m) is kept in reserve.
* **A stepped line along the horizon inside clouds** (dumps 220450, 221259, 221314): water writes no depth, and beyond
  the farthest seabed the far sea reads as sky — CloudsNear left those pixels to the sky, whose clouds are drawn behind
  everything, and the sea was then drawn over them: a strip of clear sea. With *Cloud look: reference*, CloudsNear draws
  every pixel below the horizon, sky ones included, and the sky's composite leaves them (both now match: D94). *Build 53*
  unchanged.
* **Horizon land cut at 90 km**: the camera's far plane was 90 km whatever *Horizon land* said (~100 / ~250 km). The far
  plane now follows the horizon land, which also reaches at least the true horizon for the camera's height (+10 %, in
  25 km steps, at most 400 km: ~160 km from 2 km up).
Explained, not changed (Tom decides): the see-through cap on a summit (dumps 222017 / 222024) — the cap wraps the summit,
so a ray toward the slope crosses only the thin layer of cloud in front of the rock; the near pass's step budget is not
the cause (128 vs 512 steps: identical); caps are not dense enough to hide what is inside them. The repeating pattern
from 20 km up (dump 221223): the tileable cloud map, dozens of periods in view (M3). The lens disk: open item (HANDOFF 2).
Wish: turbulence inside clouds. Test tools: `--cloud-dev=x,y,z` (z: CloudsNear step budget).

## D96 — *Cloud look: reference* is the default (2026-10-12)
Tom, after playing it: "keep the clouds because they are the best so far, but make a snapshot … so that we can go back".
Return point first: branch `pre-cloud-look-reference` and tag `return-pre-cloud-look-reference` = `72b98d9` (D95, with
*Cloud look* defaulting to *build 53*); in game *Sky → Cloud look → build 53* keeps the old clouds. Then the default
became *reference* (a fresh install renders as an explicit *reference*: difference 1.0, run noise 2.8; *build 53*: 17).
Players' saved settings keep their own choice. Benchmark profiles do not pin the setting: captures from here on use
the new clouds (compare older captures with `--set=sky.cloud_look=0`). Also tagged `handoff-2026-10-12` (= `67771ba`),
which the handoff asked to create on the PC.

## D97 — Open cloud choices as switchable drafts (2026-10-12)
Tom: "start working on the open choices". Each is a setting next to the current look, default unchanged, for his pick:
*Sky → Cloud caps* (build 54 / dense: the cloud mountains catch up to 3.5× denser, so a cap hides its summit — works on
dumps 222017 / 222024), *Sky → Cloud turbulence* (off / on: churning eddies ~60 m across within ~600 m of the camera —
draft: not yet visible in the stills, needs a test inside a cloud), *Sky → Lens clouds* (build 54 / stacked: smaller,
thinner plates with wandering rims and a second lens downwind — draft: from below the thin plates read as hard blades,
worse than the disk; to be reworked). Dumps now carry `--cloud-drift`, so cloud scenes rebuild exactly.

## D98 — Alden's torch (R) and first person (2026-10-12)
Tom: "nights are looking really nice … can we give Alden a torch? Nice and flickering on the wind, with a little bit of
smoke" and "zooming in to the maximum, could it go into first-person mode?". **Torch** (`Torch.cs`, R lights / puts it
out, `--torch` for shots, dumps record it): a stick with a glowing wrap; a flame of 72 small additive particles living in
the world (it trails behind when walking) that leans and streams downwind with the gusts; thin smoke drifting off with
the wind, unshaded (warm just above the flame, then dim grey: lit by the torch it burned to white balls); a warm omni
light (16 m, shadows) whose brightness breathes and licks and whose position trembles a few cm, so the shadows move.
Held in the right hand, the arm raised and the torch tilted away from the head. Particle billboards need
`billboard_keep_scale` (without it every particle was 1 m wide: a white ball). **First person**: one wheel step in from
the closest zoom; the camera at the eyes, the body faces where you look and stays as a shadow (its meshes shadow-only),
the torch at the lower right of the view; one step out returns. Not yet in a build.

## D99 — Gameplay direction: building blocks, sandbox settings, PZ survival (2026-10-13)
Planned with Tom in conversation, no code: `docs/GAMEPLAY_DESIGN.md`. Project Zomboid survival; tools and opportunities,
never goals ("as many game mechanics as possible that will allow them to live in the world of Thornwake as they
want"); every rule a sandbox setting with presets; expandable, so professions are not systems but emerge from a few
generic blocks (tagged items, one action/recipe pipeline, stations, skills by use, needs, one effects system, world
changes keyed by position, creatures, trade and reputation, written knowledge) with the content as data. Decided:
traits but no occupations; on death the gear drops at the body and skills carry over only through journals the player
chose to write (automatic share as a setting); books written by people of Thornwake are the tutorial; regrowth,
spoilage, wear with lossy repair, quality from the crafter; alchemy from ingredient properties; magic open to everyone,
spells built from parts, mana plus reagents, acting on the world's simulations; combat like Valheim / Conan /
Soulmask; climbing (important); Valheim-style sailing on the real wind and waves. Replaces the step order of
`GAMEPLAY_PLAN.md`; first slice: chop → campfire → cook a fish → survive a cold, rainy night, with a first book.

## D100 — Order of gameplay work; carts and draft animals separate (2026-10-13)
Tom agreed the order in `docs/GAMEPLAY_DESIGN.md` §14: (0) a small multiplayer spike first — two players, shared seed,
time and weather, one shared world change — so every mechanic is built multiplayer-ready instead of rewritten later;
(1) proof-of-concept mechanics as placeholders (needs, inventory, actions, chopping, campfire, fishing, cooking, a
placeholder animal, a raft or boat); (2) a real multiplayer test with 4–12 players; (3) settlement sites and roads with
placeholders; (4) the living world (economy, caravans, bandits, events). Atmosphere 2.0 continues in parallel. Carts
and draft animals are separate (an animal is a creature with an owner: it can be led, ridden, sold or stolen); hand
carts first, animals later.

## D101 — Multiplayer spike, first shape: host and join (2026-10-13)
`docs/GAMEPLAY_DESIGN.md` §14.0, branch `multiplayer-spike`. **Transport**: Godot's ENet with raw byte messages
(`SceneMultiplayer.SendBytes`, no RPCs on node paths), so the messages (`Net/NetMessages.cs`) are plain binary with no
Godot types; channel 0 reliable (welcome, edits, settings, digs), channel 1 unreliable (positions, clock). **Server and
client parts apart**: `NetServer` holds the true state (players, their states, the edits it sends) and checks what
clients send (version, a sane brush, digging near where the player stands); it reaches the world only through
`IWorldHost` (world arguments, world settings, clock, edits, apply a dig) — the host's WorldLab implements it now, a
headless dedicated server later. `NetClient` applies what the host sends through `IClientGame`. The host runs both; its
own player reaches the server part without the network (peer 1). **The player split**: `Character` (body, walk,
torch, first-person hiding) out of WorldLab; the local player's movement, camera and tools stay in WorldLab (one local
player per PC), remote players are `RemotePlayer`s (a Character and a name, drawn 100 ms behind and interpolated,
extrapolated ≤ 0.25 s). **What is synchronised**: on joining the world (WorldLab's own world arguments — preset, seed,
every generation parameter — plus a custom world type's JSON), the world-affecting settings (`time.*`, `sim.*` except
volume, cloud cover, wind strength, sea state; graphics stay one's own; applied as overrides that are never saved over
the player's own and are restored on leaving), the clock (day and phase; joining snaps, then once a second the gap
closes over ~2 s), the waves' clock, the gusts' clock and the clouds' drift (all accumulated per PC, so they come from
the host), positions ~20 a second, terrain edits as whole 32 m chunks (changed ones ~20 a second, all of them to a late
joiner). A client's dig is a request (the tool's resolved brush) the host applies and answers with the volume (the soil
bag); the hole reaches everyone as chunks. A client runs no rain erosion (only the host changes the ground). **Start**:
`--host[=port]` (UDP 7777), `--join=address[:port]`, `--name=`, `--window-at=x,y`; `TEST_MP_HOST.cmd` and
`TEST_MP_JOIN.cmd` open two windows side by side. Verified on this PC: the client got the welcome (1.7 KB), built the
same world, appeared beside the host and showed them. Not shared yet: local water (as agreed), the simulated weather's
own state (to verify).

## D102 — Multiplayer: join screen, network log, no player limit, digs verified (2026-10-13)
Tom confirmed checkpoint 1 (two windows see each other move) and asked, before an internet test with a friend in
France, for more players, a join screen and network diagnostics. **Player limit**: none in the code; ENet accepts up to
32 connections; the real limits are the host's upload (positions: ~1 KB/s per player in, the host sends everyone's to
everyone — ~1.9 Mbit/s for 16 players) and its CPU (§14.1); left as it is (Tom). **Join screen**: the launcher's
*Multiplayer…* page — name, UDP port, *Host a game*, the host's address (IP or name, `:port` optional), *Join*;
remembered in `user://multiplayer.cfg` (`Net/NetLaunch.cs`); the joining screen shows the connection's state and why it
failed. **The host changes world** (Esc → new world): everyone builds the new world and comes back beside the host
(message `World`; protocol 2). **Network log** (`Net/NetStats.cs`): while a session runs, one line a second to
`user://debug/net-<time>-<role>.log` — every peer's round trip, jitter and loss, bytes and messages by kind in and out,
the clock's offset to the host, how regularly positions arrive and how often a player was drawn guessed ahead, chunks
received — and every event (connected, welcome, joined, left, refused, world changed, digs applied or refused) with its
time; F9 dumps add a summary, the latest events and the log's path. First reading (two windows on one PC): round trip
40–90 ms (ENet is serviced once a frame in each game), positions every 60–80 ms → the send timers now keep their
remainder (20 a second on average). **Digging (checkpoint 2)**: a test (`--net-dig`, `--net-probe`) found a client's dig
dropped when it arrived before the client's first position (reliable and unreliable messages travel separately); now
accepted, and every dig decision is logged. Verified: the host's trench reaches a player joining 15 s later, the
client's dig is applied by the host (5.6 m³) and both read the same ground height after it (19.327 m).

## D103 — Multiplayer: the weather needs no syncing; Tom's router checked (2026-10-13)
§14.0 done-criterion 3 (same time, weather and clouds). `WeatherField` is deterministic in seed, position and game
hours (nothing stepped; storm cells carry ids shared by everyone), and the clock is the host's, so the weather follows
by itself; the local easing toward it converges. Measured with `--net-probe` (simulated weather on, day 3, 15:00):
host and client both cloud cover 0.586, no rain, no storm, wind from the west 0.984 / 0.985 (they stand 3 m apart);
gusts differ only between moments sampled at different times (their clock is synced once a second). Nothing more is
sent. Tom's network (for checkpoint 3): public address 83.175.178.135; the route's first hop after the router is a
public address (84.116.254.65), so most likely no CGNAT; the router does not answer UPnP (off), so port 7777 must be
forwarded by hand (steps in PLAYTEST_REWRITE.md, build 55). A later option: open the port by UPnP when hosting.

## D104 — Tom's line is DS-Lite: hosting over IPv6 (correction to D103) (2026-10-13)
The first internet test failed: the friend's client tried for 36 s and got no answer; Tom's host log for the same session
shows nothing arrived. The router page (UPC/Play Connect Box, CH7465PLAY) says **IPv6 DS-Lite: Enabled** (aftr01.play.pl):
the router has no IPv4 internet address of its own; 83.175.178.135 is the provider's shared gateway, so IPv4 port
forwarding cannot work. D103's "most likely no CGNAT" was wrong — the traceroute's first public hop was the DS-Lite
gateway itself. Ways ahead for Tom, in order: (1) **IPv6** — the PC has a stable global address, the game's host already
listens on IPv6 (tested over ::1) and the join screen takes IPv6 addresses (bare in build 55, also `[addr]:port` since
9b0ea58); needs the friend to have IPv6 and UDP 7777 allowed in the router's IPv6 firewall and Windows Firewall;
(2) the friend hosts if their line has its own IPv4; (3) a tunnel (playit.gg handles UDP); (4) ask the provider for a
public IPv4 (no DS-Lite). For a later build: say on the Multiplayer page that DS-Lite / CGNAT lines must host over IPv6
or a tunnel, and show the host's own IPv6 address there to copy.

## D105 — First internet test: Poland ↔ Brazil through playit.gg (2026-10-13)
§14.0 done-criterion 4 (a friend over the internet, ping and bandwidth measured). Tom hosted build 55 on his DS-Lite line
(D104) through a playit.gg UDP tunnel (free plan; only the host runs the playit program; the game server still runs on
Tom's PC, playit only relays); the friend (TeaBeca, Telefônica Brazil, IPv4 only) joined `cairo-teal.tun.ply.gg:49088`.
The friend's earlier "no answer" was a timing miss (Brazil is 5 h behind; the attempt came 15 s after Tom's host closed).
Two sessions, 20 min and 4 min (host logs net-…-184317 / -191708): round trip **~320 ms** (10–90 %: 310–330 ms, jitter
~11 ms, spikes to 1.1 s), loss 0.2–0.5 % (bursts to 10 %), the host's upload **~1.9 KB/s** with one guest (bursts of
100–120 KB/s when edit chunks go out), the guest's positions every 50 ms as designed, 100+ of the guest's digs applied
(shovel and the terraform tool). Found: (1) the join screen lost the port of `address:port` (fixed, 8c7bd27); (2) a 48 s
gap in the guest's positions while their terrain loaded after a 20 km jump (no state is sent while `_holding`) — send
positions while loading too; (3) DS-Lite / CGNAT lines need a relay: later Steam networking or our own relay, and the
Multiplayer page should explain it. Waiting for the guest's own log (clock offset, how often the host was drawn ahead)
and how it felt.

## D106 — The host's freezes after teleports were rain erosion; rain shared; join fixes (2026-10-13)
Tom's host froze for 12 s and 48 s (and once for good) after map jumps in the internet test, with rain started by H.
Both logs line up (the friend's clock is 407 s behind in session time): each freeze ends with one Chunks message of 24–28
edit chunks (98–122 KB) from the host, and the friend's side shows the host silent while they kept sending. Cause: rain
erosion (every 10 s, host only) hands over the worn cells of the local water window **and** of the window just left;
`ApplyErosion` changed one rectangle over all of them, so after a jump it walked every 1 m vertex between the old and
new place (366 × 92 km in the reproduction: 33 billion vertices, the game hung) and invalidated every tile in between.
My earlier reproductions had no rain. Now `TerrainEdits.Add` changes only the vertices given, chunk by chunk, and only
the touched chunks are rebuilt; the ground comes out identical (same values as `Modify`, test *erosion far apart*).
Reproduced and checked with `--rain --teleport-test=6,35` (old: hung at the first jump; new: no hitch over 500 ms
from erosion). `--rain` now starts the rain outside captures too, as the dump's Reproduce line meant.
Correction to D105 (2): the 48 s gap in the guest's positions was this freeze of the host, not the guest loading.
Also: rain switched on with H on the host is now part of the clock message and rains for everyone (protocol 3);
on a joined game H only says the host decides. From the friend's logs: a failed join (the address with two ports)
crashed in `StepJoining` because the client part was made after the connection; it is now made first. After a drop
the game kept sending positions to a closed connection (thousands of errors per minute in the log); nothing is sent
now unless connected.
Tom's requests from the same session: the world map's marker is now an arrow with a notch at the back (the "▲"
triangle did not show which way it pointed); the other players are on it as arrows in their tunic colour with their
names, moving while the map is open, and a *Players* list beside the map goes to them (beside them, on the ground: the
"teleport to another player" menu). To set a game up before hosting: Esc → *Host this world…* opens the world already
here (made and dug alone first) on the port and name from the launcher's Multiplayer page; *Host a game* in the
launcher still starts hosting at once. The custom world type sent to joiners is now the one the world was built from
(it was whatever the menu's list showed). Diagnostics kept: frames over 500 ms are logged as HITCH with the slow steps
(and into the network log), the log is flushed on every line (a crash keeps it), `--teleport-test=count,seconds`,
`--net-dig-repeat=seconds,radius,strength`, `--host-here=seconds[,port]`, `--map-shot=png,seconds`.

## D107 — Storm: no sun dog and less sun glow through the deck (2026-10-13)
Tom's dump 204727 (storm, 12:23, sun 30° up, Atmosphere 2.0 light model, volumetric v2 clouds): "during the storm …
I can still see the sun glow". Two things, found by switching parts off one at a time and measuring the sky's
brightness on the sun's side against the other side (167 vs 109 in the reproduction):
1. **A sun dog** (the coloured spot of the 22° halo) shone through the storm deck: the sky shader adds the halo after
   compositing every cloud layer, dimmed only by the eased storm value (0.34 there). The halo forms in the cirrus, so
   now it is also hidden by every cloud in front of the cirrus (`clear`, tracked with the sun disc's dimming).
2. **The broad glow** came mostly from the volumetric fog: Godot lights it through the shadow map, which the clouds
   are not in, so under a full deck it went on scattering full sunlight forward, toward the hidden sun. Its sun energy
   now dims with the cloud cover (`gloom`, the value that already halves the sunlight under an overcast): × (1 − 0.9 ×
   gloom), 0.37 under a full deck. Sun side 167 → 145, the other side 109 → 106. A clear or broken sky (gloom 0) is
   unchanged. Not the cause (no change when switched off): the storm deck's sun-side glow, the sun glare, the storm
   overlay's 5 % leak, the deep-cloud forward light in the cloud march. What is left is the deck's own structure (thinner
   toward the storm's edge). Return point: build 56 (`404a9c5`).

## D108 — Step 1 starts: felled trees as a world change (2026-10-13)
§14 step 1 (placeholder mechanics) begins with §15's first slice. A generated tree's identity is its lattice cell
(Vegetation's 7 m lattice from the seed; the cell size follows the world's forest density, a world setting, so it is the
same on every PC). `ObjectRemovals` (Game/WorldGen, beside `TerrainEdits`: generic, no gameplay in WorldGen) holds the
cells taken out, with the game time, and the keys changed for the host to send — a felled tree reaches late joiners
like dug ground does, and the time stamp is there for regrowth (§6). `Vegetation.Trees` leaves removed cells out, so
the tree field, impostors and the level-0 collision all lose the tree once the tiles around are invalidated.
`TreeAt` / `TreesNear` find the tree to chop. Trees come out bit-identical to before (a fingerprint of the trees around
two places, old vs new code; `--dump-tile` prints it). Next: the gameplay core (items, inventory, recipes, needs) as
engine-free code in `Game/Gameplay` with data in `Content/` (the original game's items.json / recipes.json are there
and are left alone: new files), then chopping, the campfire and the network messages.

## D109 — Darkness settings: overcast and storms, forests, nights (2026-10-13)
Tom: full overcast and storms should be darker; dense woods a little darker; and a sandbox setting for how dark nights
are, "without ruining how the sky and clouds look". Three settings, each at build 56's light by default (Tom picks the
defaults from the captures):
* *Overcast and storm darkness* (`fx.storm_darkness`, 0–1): under the deck (gloom) the sun's energy × (1 − 0.65 d),
  the sky light × (1 − 0.5 d), and the eye's lift under overcast (up to +¼ stop) shrinks to none. The clouds darken
  with the sun's energy (the sky shader lights them with it).
* *Forest darkness* (`fx.forest_darkness`, 0–1): the eye's lift under a closed canopy (up to +½ stop) shrinks to none
  and the sky light under the crowns × (1 − 0.35 f). Dense forest at noon: exposure ×1.50 → ×1.11.
* *Night darkness* (`sim.night_darkness`, Simulation page, so the host's applies to everyone): bright / normal / dark /
  very dark / pitch black = the sky light on the land at night × 1.8 / 1 / 0.5 / 0.25 / 0.06 and the moonlight the
  same. The exposure is not touched, so the sky, moon, stars and clouds look identical at every level (checked in the
  forest night captures). A torch matters at night now.
Defaults (Tom agreed with the proposal from the captures): storm 0.5, forest 0.5, night *normal*. 0 / normal brings
build 56's light back. Storms are to get more atmosphere later (Tom: "Storms have to be atmospheric, but we can work on
that later").

## D110 — Step 1's first slice playable: chop, campfire, needs, through the server part (2026-10-13)
* **Core** (`Game/Gameplay`, engine-free, headless tests `GameplayTests`): items with tags, an inventory, recipes as one
  pipeline (inputs, tools, a working station within reach, time, outputs or a placed object), placed objects (a
  campfire burns its fuel in game minutes), needs (food, body warmth, wetness, health; 0–100, per game hour) and
  `GameRules`, the host's rules for chop / make / fuel / eat. Content: `Content/gameplay_items.json`,
  `Content/gameplay_recipes.json` (new files; the original game's items.json / recipes.json are left alone). Start kit:
  a stone axe, three dried meats, *Notes on Felling* (Old Berta's line from §5).
* **Authority**: a player's PC asks (`Msg.Action`), the host's server part checks against where it knows the player
  to be (reach, tools, materials, a lit fire) and answers (`Result`); inventories go to their owners (`Inventory`),
  placed objects and felled trees to everyone (`Objects`, `Removals`) and all of them to late joiners in the welcome.
  The host's own player and **single player use the same path**: alone, a NetServer and NetClient talk without a
  network (Host this world swaps in the network's). Protocol 4.
* **Controls** (Tom): 6 holds the axe (1–5 put it away); the left mouse held on a tree in front of you (within 4.5 m,
  inside 60° of the view) chops it in 3 + 2.5 × size seconds with an arm swing others see (state flags Axe, Chopping);
  E puts the best wood in the pack on the nearest fire; I opens the pack (items, eat, make). Making takes the recipe's
  time and stops if you walk away; a campfire goes 1.8 m in front of you, lit, with 90 min of wood.
* **Felling**: the tree leaves the forest (tiles, tree field and collision rebuilt around it) and the original game's
  stump stands there; 1 + 2 × size logs and 3 + 3 × size branches.
* **Needs**: air temperature = the place's yearly mean (climate) + season (±3 °C in the tropics … ±10.5 °C at 50°) +
  hour (±4.5 °C, warmest at 15:00) − 3 °C in rain; feels-like = air + fire − wind chill − wetness. Food lasts ~2 game
  days. Freezing and starving hurt health; no death yet (health stops at 0 with a warning). Setting *Hunger and cold*
  (`sim.needs`, on; off for testing the world).
* Checked by `--gameplay-test` (alone and as a joined guest: chop, stump, tree gone, campfire built and drawn, fuel,
  eat, warmth) and a late joiner receiving the felled tree. Placeholders: the campfire's flames are the original
  game's static cones (a flickering light), the axe and stump its models; no saving yet (the rewrite has no saves).

## D111 — Chopping like Valheim: hits, a shaking tree, a fall, a log pile; torch and axe in their own hands (2026-10-13)
Tom (build 57): chopping should feel impactful, as in Valheim — the tree shakes, falls with physics, and turns into
logs; and the torch and the axe ended up in one hand, the torch in different hands in first and third person.
* **Hits, not a timer**: each axe stroke that lands (the swing's impact moment, ~1.4 a second) is one `Chop` action;
  the host counts them — a tree takes 3 + 4 × its size hits (≈ 6–8) — and sends every hit to everyone (`TreeHits`).
  Wood chips fly from the trunk at each impact.
* **Shake and fall**: on the first swing or hit the tree is drawn on its own (`TreeField.Proxy`: the detailed model's
  parts as one-instance batches, the forest's own meshes and materials) and left out of the forest's batches
  (`TerrainSampler.Hidden`, local, never sent). Each hit shakes its crown (a quick, dying sway). On the last hit the host
  picks the direction — away from the chopper, the wind pushing it (its share grows with the wind, up to 80 % at
  12 m/s: Old Berta's "the wind decides where a tree lands") — and every PC tips it over its base as a trunk pivoting
  under gravity (θ'' = 3g / 2L · sin θ, a nudge at the start), stopped by the ground ahead (higher ground: sooner), with
  a small bounce; it lies 2.5 s and sinks away.
* **Logs**: its wood then lies where the middle of the trunk came down as a **log pile** (a placed object with
  contents), picked up with **E** (Tom's gathering key) — the same for everyone and for late joiners. Valheim's next
  step (one big log you chop into pieces) is left for Tom to decide.
* **Hands**: the original model names its limbs from the viewer's side ("RightArm" is Alden's left). The torch is now
  always in his left hand, the axe in his right, and the first-person torch sits on the left too.
* Not done: the falling tree passes through other trees; the fallen trunk has no collision. Protocol 5. Checked by
  `--gameplay-test` alone and as a joined guest (20 checks: hits come back as shakes, the fall, the stump, the pile,
  picking it up, the campfire, eating).

## D112 — Felling in three acts: notch and fall side, impact, a physical trunk cut into logs (2026-10-13)
Tom (after build 58): a whole tree falling with physics looks stiff; in Valheim the tree becomes a bare log that rolls,
can be pushed, hurts, and is chopped into pieces — and felling should feel like an achievement and work, not deep or
difficult. Agreed plan: GAMEPLAY_DESIGN §5a (three acts; tree size sets effort and yield). Built:
* **Act 1**: the first hit sets the **notch** on the chopper's side (host: `_treeHits` keeps hits and the notch); a pale
  cut face grows on that side with the hits, then a back cut (placeholder geometry on the proxy); at two hits left it
  creaks ("It creaks…"), leans toward the notch and drops leaves; at 60 % the chopper is told to finish from behind.
* **Act 2**: the tree falls **toward the notch** (the wind up to half: 14 m/s and more), scripted as before; it hurts the
  player it comes down on (20 + 25 × size health, checked on each PC for its own player). On impact the crown **breaks
  up** (a burst of leaves and twigs, dust along the trunk, a camera shake nearby) and the proxy is gone.
* **Act 3**: the rules leave a **trunk** object (length 7.2 × size, radius 0.12 + 0.12 × size, the tree's species for
  its bark) and a **branch pile** where the crown came down. The trunk is a Godot RigidBody3D on the PC of its
  **owner** — who felled it, or who last walked into it (`Claim`; the host gives it over) — which reports where it is
  (`Move`, ~10 a second while it moves; `Moves` to everyone, unreliable); other PCs follow smoothly (a kinematic
  body). It rolls, can be pushed (walking into it), hurts when it rolls into you faster than 3 m/s, freezes when its
  owner is more than 150 m away (no ground collision there), and is let go when its owner leaves. The axe cuts it:
  two hits per cut, one cut fewer than its logs, each cut a pale ring on it; then it lies as a log pile (E).
* **Yield by size**: logs = the trunk's volume (π r² L) / 0.3 m³ (a size-0.8 birch 3, size 1.15 6, size 1.6 12),
  branches 3 + 3 × size; hits to fell 3 + 4 × size. Placeholders to tune.
* Code: the felling part moved to `WorldLab.Felling.cs` (dev-separable, as promised to Tom); protocol 6.
* Checked: headless (notch side, fall toward the notch, trunk owner, moves, claims, release, cuts, yield) and
  `--gameplay-test` alone (29 checks, among them: standing behind it is safe, pushed across it rolls 1.3 m, the host
  knows where it went) and as a joined guest (28).
* Not yet: the falling tree passes through other trees; the notch is a placeholder face, not a cut in the model;
  carrying weight (logs are heavy) waits for building (§5a). Tom will make tree assets by hand: a guide when he starts.

## D113 — E gathers: loose stones, berries, long grass; a fishing rod (2026-10-14)
Tom: E is for gathering — grass for plant fibre, berries, loose stones. Fishing (Enshrouded style, GAMEPLAY_DESIGN
§5b) needs a rod, the rod needs fibre: gathering first.
* **Identity**: loose stones (the 3.5 m lattice of `GroundCover.Rocks`, level 0) and berry bushes (the 9 m lattice of
  `GroundCover.Props`) are lattice cells like trees; `RockInstance.Cell` carries the cell (packed), `ObjectRemovals`
  gains layers **Stones** (picked up, for good) and **Berries** (picked: the bush is drawn without fruit, as a plain
  bush; the fruit is back after 48 game hours — the host's rules restore it). Generation is unchanged while nothing
  is taken (the same cells, the same chances; only a check of the removals added).
* **Finding them**: the streamer keeps, per drawn level-0 tile, its stones and fruiting bushes at their world places
  (`GatherablesNear`); E picks the nearest within reach. Order of E: wood lying in a pile, a campfire (fed), a berry
  bush, a loose stone, else **long grass** where you stand (land, the biome's grass > 0.3, not steep, not in water).
  Gathering takes a moment (stone 0.8 s, berries 1.5 s, grass 1.2 s); moving stops it.
* **Rules** (`GameRules.Forage`, action Forage, protocol 7): the host checks the reach (the cell's place from the seed)
  and that it was not taken already; whether one really grows there is the player's PC's word (it draws them).
  Stone ×1, berries ×4 (food 5 each, raw), plant fibre ×2.
* **Fishing rod** recipe: a branch and 4 plant fibre (6 s). Fishing itself next.
* Checked: headless (14 checks: reach, once only, regrowth after two days, berries as food, fibre, the rod) and
  `--gather-test` alone (7 checks) and as a joined guest (6; the host applied every gather).

## D114 — Fishing, a small Enshrouded-style version (2026-10-14)
GAMEPLAY_DESIGN §5b (Tom: fishing must be engaging, Enshrouded style for the final game). Step 1 builds a small
version of exactly that minigame, so it grows rather than being replaced:
* **Rod** (7; made from a branch and 4 plant fibre) with *strength* and *endurance* (item props); **bait** is required
  (berry bait: one berry → three baits; later worms from digging, better baits at a station).
* **Cast**: hold the left mouse button to throw farther (4–14 m where you look), release; the bobber must land in
  water. **Bite** after 4–14 s (×0.6 at dawn and dusk, ×1.3 at night, ×0.8 in light rain); the bobber goes under:
  click within 0.9 s, else it swims off. Moving reels in.
* **Fight** (`FishFight`, engine-free, tested): the fish pulls left or right (1–2.5 s), rests (0.4–0.9 s), pulls
  again; holding the other way (A / D) tires it (rod strength / its stamina in seconds); the wrong way or nothing wears
  the line (its pull × 0.2 a second / rod endurance); holding on while it rests wears it a little; a 0.2 s reaction
  window after each switch (perfect play loses no line; the test's hands, a quarter second late, still land a pike).
  Walking stops while reeling (A/D are the fish's).
* **Which fish**: by the water at the bobber (ocean, lake, river, pond) — trout and grayling in rivers, perch and pike
  in lakes, carp and tench in ponds, mackerel and cod at sea; pike and tench more at dawn and dusk. All land as raw
  fish for now (cooking at a fire exists).
* **Authority**: the fight runs on the player's PC; landing (or losing) one is the `Catch` action — the host checks a
  rod and bait, water near the player where that fish lives, not more than one catch per 0.02 game hours; the bait is
  used up either way (protocol 8). Others see the rod and the fishing pose (not the line yet).
* Checked: headless (216 checks: fish by water and hour, bite timing, perfect / slow / idle / wrong play, a stronger
  rod, the host's checks) and `--fish-test` (finds the nearest water, casts, strikes, fights, lands a grayling).

## D115 — Death drops the pack; a lean-to for a cold, rainy night (2026-10-14)
The first slice's end (GAMEPLAY_DESIGN §15: get through a cold, rainy night) and §4's death.
* **Death**: when health reaches 0 (cold, hunger, a falling tree, a rolling trunk) the player's PC asks `Die`; the host
  drops everything carried as a **pack** where they fell (E picks it up — the owner on a corpse run, or anyone else) and
  the player goes on as a new character with the start kit, at the world's start, fed and whole. Journals carrying
  skills over (§5) come with skills.
* **Lean-to** (3 logs, 8 branches, 4 plant fibre, 10 s): placed 1.8 m in front, open toward the builder; within 2.2 m of
  it the rain stays off and the wind drops to 30 %. Eight game hours of 2 °C rain and wind: out in it health falls;
  under a lean-to by a campfire it stays (tested). The needs line says "sheltered".
* Protocol 9.

## D116 — Saving the world (2026-10-14)
The rewrite saved nothing; the first slice needs it (a night survived should still be there tomorrow).
* **What**: `WorldSave` (Game/Gameplay, engine-free, tested): the world as its generation arguments (it is generated
  again from the seed — a save stays tiny: 906 bytes after a felled tree and a stone) plus every change — terrain edit
  chunks, generated objects taken out (trees, stones, berries, with their times), placed objects (fires, trunks, piles,
  lean-tos, packs) — the world-affecting settings (the world's rules), the clock, the clouds' drift, and players **by
  name** (ids change with every connection): every pack, and for the host's own player where they were and their needs
  (a joined player's needs live on their PC and come back fresh). A joined friend who comes back under the same name
  gets their pack back.
* **When**: Esc → *Save* (a time-stamped file), **F5** a quick save, an autosave every 10 minutes (alone or hosting; a
  joined player does not save). Esc → *Load…* lists the saves (newest first). Files: `%APPDATA%\Thornwake\saves`.
* **Loading**: the save's world rules are set, the world is built again (as a joining player builds the host's), then
  the changes are laid on, sent to everyone as changes (the host's own view included), the packs restored, the clock and
  the clouds set, the host's player put back. While hosting, everyone joined rebuilds the world with it.
* Not yet: a launcher *Continue* (proper menus later), several characters per world, the local water simulation's
  water (puddles are not kept).
* Checked: headless (10 checks: round trip, a fresh world with the changes, packs by name, a returning friend) and
  `--save-test` (fell a tree, pick a stone, save, walk away, load: tree, stump, trunk, stone, pack, place, time).

## D117 — Fishing as Tom plays it; the lean-to's roof the right way (2026-10-14)
Tom (build 60, dump 203051): the lean-to looked wrong; fishing should let him walk, the bobber should ride the water,
the fish should run left and right with the rod turned against it and bending, and high tension should show.
* **Lean-to**: its rafters and roof were mirrored (rising to the back); now the ridge pole on its posts is the high
  front edge and the roof slopes down to the ground behind (two signs).
* **Walking while fishing**: the bobber stays where it landed; the line (22 m) follows the rod; walk farther and it
  drags the bobber toward you along the water; dragged out of the water, it is reeled in. Only a click reels in (then
  cast again). With a fish on, **A/D turn the rod** (and Alden) against it instead of stepping sideways; W/S still walk.
* **The fight shows**: `FishFight` gains **Side** (where the fish has run, fast when unanswered, slowly when held) and
  **Tension** (0.1 resting … 0.45 answered … 1 unanswered, eased). The bobber is dragged up to 3 m left or right (kept in
  the water); the rod is a chain of five joints that **bends** toward the water with the tension and sideways toward
  the pull; the **line turns red** as the tension rises (and goes taut).
* **The bobber bobs** on its own (a few centimetres, a slow sway) on top of the drawn water surface (the sea's and
  lakes' waves): rivers and calm lakes have no waves to ride, which is why it stood still for Tom.
* Checked: headless (tension unanswered > 0.9, answered < 0.7) and `--fish-test` (pictures of the lean-to and a fight).

## D118 — Longer fish runs; a progress bar over the head (2026-10-14)
Tom: the fish should run farther left and right, at random; timed actions should show a small progress bar over the
player's head, with optional text.
* **Fish runs**: each run has its own length (×0.5 … ×3.5, mostly short, sometimes a long dash) and a fish that ran far
  one way tends to turn back; the bobber goes up to 7 m to either side (in the water).
* **Action bar** (`WorldLab.ActionBar.cs`): a small bar over Alden's head (in front of the view in first person) while
  he makes something, gathers, fells a tree (its hits so far), cuts up a trunk, charges a cast; the text ("Felling a
  birch", "Picking berries") can be switched off: *Interface → Action text*. Own player only for now (others' busy bars
  would need their action in the state flags).

## D119 — Pulled grass leaves a bare patch (2026-10-18)
Tom: the grass should disappear when harvested (stones already do: D113). `ObjectRemovals` gains the layer **Grass**
on a 1 m lattice; pulling long grass bares the 3 × 3 cells around the spot (for everyone, saved, sent to late joiners);
`GroundCover.LandFlora` leaves out the plant populations' tufts there (the same way it leaves dug ground bare) and the
tile is drawn again. Only bare cells: nothing left to pull ("No long grass left here"); a patch half-pulled gives one
fibre. It grows back after 72 game hours (the rules restore it, like berries). Checked: headless (the patch, nothing
twice, regrowth) and `--gather-test` (43 tufts in the patch before, 0 after).

## D120 — Hunting: animals, a spear, a bow, a sword, butchering (2026-10-18)
Tom: "implement hunting; for animals use the asset pack (ArtKit/AnimalsAssets.zip); basic weapons (sword, bow, spear,
arrows); simple butchering." §2's *Creatures* block, §9's first weapons; step 1's "placeholder animal to hunt".
* **Models**: the pack is Quaternius's *Ultimate Animated Animals* (CC0: free to use, no credit needed; credited anyway in
  `Assets/Animals/LICENSE.txt`), rigged with walk, gallop, eating, idle, attack, hit and death animations. Taken: **deer,
  stag, fox, wolf** (the wild ones); the horse, cow, bull, donkey, alpaca and dogs stay in the zip for the draft and farm
  animals of §12. Copied unchanged as `.gltf`; each is scaled at run time to its kind's height (`Content/gameplay_animals.json`).
* **The host simulates them** (§14.1: the host does everything first), ten steps a second (`Gameplay/Wildlife.cs`,
  engine-free, tested). Groups appear **out of sight** (90–170 m from every player) where the biome's tags suit them and
  go when nobody is within 280 m: the land keeps its animals, not each animal (they are not saved — cheap, and a
  reloaded world is not emptied by hunting). Each player is sent the animals within 230 m ten times a second (~30 bytes
  each, unreliable) and draws them 120 ms in the past, interpolated (`AnimalView.cs`), with the animation of their state.
* **Behaviour by temper**: *shy* (deer, fox) graze and wander, notice a hunter by **sight** (less at night) or
  **hearing** (a sprinter is heard twice as far, a still hunter half as far — so creeping up works), look up for a moment,
  then **flee, the whole group**; hurt, they run. *Proud* (stag): flees, but may **turn on a hunter who hurt it** close by
  (a kick, 22). *Predator* (wolf): packs of 2–4 hunt a player **at night or who comes close**, bite (12 every 1.3 s),
  **run when badly hurt**. Bites are the host's word, health is the player's PC's (a `Hurt` message; with *Hunger and
  cold* off nothing hurts). Flying players are not noticed. Sandbox setting **Simulation → Animals**: none / few /
  normal / many (§3).
* **Weapons** (items with `damage`, `reach`, `cooldown` props; Tom's controls of §9: what you hold decides the left
  mouse): **8 hunting spear** (34, reach 2.7 m: a thrust), **9 rough bow** (hold to draw — longer draws fly farther and
  hit harder, up to 40; release to shoot), **0 stone-edged sword** (27, quick cuts). The world has no metal yet, so the
  sword is a **hardwood blade edged with stone flakes** (a macuahuitl): a sword that fits the stone-tool start; an iron
  one comes with smithing. **The axe (6) strikes an animal in front of you** instead of a tree. **Arrows** (5 from 2
  branches, a stone or a bone, 2 fibre) fly **with gravity and a little drag** — they drop with distance (§9) — and
  others see them fly. The shooter's PC flies its own arrow and tells the host what it struck; the host checks the bow,
  an arrow in the pack (one is used either way), range (90 m) and pace; a miss can be **found again where it fell**
  (65 %, E), a hit may be **in the carcass** (50 %). Melee: the host checks reach (+1.2 m for the network's delay) and
  pace. Recipes: stone knife, spear, bow, arrows, bone arrows, sword, roast meat.
* **Butchering**: a fallen animal becomes a **carcass** (its model in the death pose; a placed object, saved). **E** with
  anything sharp (knife, axe, sword) butchers it — a few seconds (deer 7 s; a **knife** 40 % quicker) — into **raw meat**
  (cook at a fire: roast meat, 40 food), **hide** and **bone** (deer 4/1/2, stag 6/2/3, wolf 3/1/2, fox 1/1/0).
* **Not yet**: animals do not avoid trees or buildings (they walk through), no blood, no sounds, no tracks, no
  skinning or tanning steps, no PvP, others do not see your health. Values are placeholders to tune.
* Checked: headless (senses, herds fleeing together, reach and pace, kills, carcasses, butchering, the bow's damage and
  lost arrows, a wolf's bites, a hurt wolf running, spawning out of sight and none at *none*, leaving with the players, the
  network format) and `--hunt-test` in the game (a deer struck twice with the spear, its death animation, butchered; a
  stag shot through the camera's aim; a wolf's bite: health 100 → 64). Host + guest: the guest drew the host's four animals.

## D121 — Building: blueprints first, shapes by category, snapping (2026-10-18)
Tom: "basic shelter building (log foundation, log walls, log walls with window, stairs, roof from logs and/or thatch,
different shapes of tiles allowing detailed roofs); better placement of built objects (including the lean-to and the
campfire). First the player places a blueprint of the element of their choice, then adds the materials. Soulmask's
approach: build a wall, then decide it needs a window or a doorway — press a key and get the other shapes; categories of
objects. Snapping and rotation versatile: players build what they want from the available pieces. Use Blender if needed."
* **Pieces are data** (`Content/gameplay_pieces.json`, `Gameplay/Building.cs`): category, shape, material, cost, the
  box it fills, its **snap points**, the groups it may join (`attach`: a roof rests on walls and roofs, not on floors),
  whether it may stand on bare ground (with how far its legs reach down). 36 pieces in 7 categories: **Camp** (campfire,
  lean-to), **Foundation**, **Floor** (full, half: a stair opening), **Wall** (wall, window, doorway, half height; gables
  steep and low, peaks steep and low), **Door**, **Stairs** (a storey, 3 m over 4 m; steps, 1.5 m), **Roof** — slope,
  ridge, outer corner (hip), inner corner (valley), peak (pyramid cap) × steep (45°) and low (26.6°) × **logs or
  thatch** (20 roof pieces: square, hip, gabled, L-shaped roofs). Grid 3 m (the original game's and the ArtKit's), walls 3 m
  high so a steep roof's gable is one wall-sized triangle. Costs are placeholders (a wall 3 logs, a foundation 4…).
* **Controls (Soulmask style)**: **B** opens building: a bar of categories (**1–7**), the current one's shapes — **V**
  cycles them (Shift+V back), **X** the materials (roofs: logs / thatch); **R** turns 45°, the **wheel** 15° (Shift: 5°),
  **Page Up/Down** raise or lower (foundations on slopes), **Home** resets, **Ctrl** places without snapping; the **left
  mouse** places a **blueprint**, the **middle mouse** or **Backspace** takes the aimed piece down (all its materials
  back: building should invite trying things; a sandbox share later), **B / right mouse / Esc** closes. **E** on an aimed
  blueprint **brings materials** from the pack (as much as it still needs and you have; a second's work); a blueprint
  shows what it needs when you look at it, glows when aimed, and is not solid. **Aiming at the middle of a piece of the same
  category swaps it for the chosen shape** — its materials come back and go straight into the new one (Tom's wall →
  window wall), or **turns** it if it is the same shape; aiming at its edges snaps beside or on top of it.
* **Snapping** (engine-free, the host checks with the same code): the target is the snap point of a piece nearby
  closest to where you aim; the new piece is turned in quarter turns of that piece, and the pose chosen is the one that
  makes **the most of its points meet** others (a wall along a foundation's edge rather than across it; a roof's eave
  along a wall's top), with **its body toward where you aim** (a hip corner in the corner you point at, not half a cell
  along the wall), then **closest to the heading you want** (the camera's, plus your turns: R flips a roof between
  sloping inward and outward). No snap point within 2.2 m, or Ctrl: placed **freely** where you aim, at any angle. A piece
  may not overlap a piece of its own group (two walls in one place); a ground piece needs ground under it (or a joint),
  others a joint. Free-standing pieces stay up when what they rest on is taken down (no structural strength yet).
* **The campfire and the lean-to are pieces** (Tom: better placement): placed with the ghost (turned, anywhere in reach)
  instead of 1.8 m in front; the campfire is lit when finished; their recipes left the pack. **Finished roofs shelter**
  (rain off, wind ×0.3), like the lean-to; blueprints do not. **Doors** open and shut with E (the leaf swings, its
  collision with it). Finished pieces are solid (collision from their meshes); walking into a piece's edge up to 0.55 m
  steps up onto it (a foundation's deck) — only pieces, the terrain's own steps stay as they were.
* **Models**: `tools/build_gameplay_models.py` (Blender 5.1, `--background`) builds every piece and the weapons from
  simple shapes — stacked logs with their ends crossing at the corners, split-log decks and floors, log or layered thatch
  roofs, a plank door on a hinge — as GLBs in `Assets/Gameplay/` (34 pieces + 5 weapons, under 1 MB). Their materials are
  named (Bark, Wood, Thatch, Stone, Fibre) and get the game's own (a colour and a stretched noise grain:
  `GameplayModels.cs`). **Placeholders**: the shapes and sizes are the contract (§5e), the looks are for Tom to decide —
  any GLB can be replaced by a hand-made one of the same name, size and origin.
* **Network and saves**: placing, supplying, taking down and using are actions the host checks (reach 9 m); a placed
  object carries `Blueprint`, `Supplied` (what went into it), `State` (a door) and `Ref` (a carcass's animal): placed
  objects format 2, **save version 2** (version 1 saves still load: their campfires and lean-tos are finished pieces),
  **protocol 10** (build 63 does not connect to 62).
* Checked: headless (blueprint → materials → finished; reach, overlaps, support; snapping a foundation beside, a wall
  along an edge from three headings, a roof's eave on a wall top, a door into a doorway, a 6 × 6 m hip roof's four
  corners meeting at the top; swapping a wall for a window wall and a doorway; turning; taking down; roofs shelter only
  when finished; the saved blueprint) and `--build-test` in the game (the ghost and the left mouse; E; a 3 × 3 m hut with
  four walls, a thatch ridge roof, two peaks and a door; standing on its floor inside, sheltered; the door opening; the
  east wall swapped for a window wall; a 6 × 6 m house with a log hip roof). Host + guest: the guest saw all 26 pieces
  and placed its own campfire blueprint through the host.

## D122 — Over the shoulder when aiming (2026-10-18)
With the bow in hand (or building) the camera's centre — the aim — was Alden's head: the target hidden behind him. The
camera now slides 0.7 m over his right shoulder while the bow is held or building is open (*Interface → Over-the-shoulder
aim*, on; off = as before), and a small cross marks the screen's centre.

## D123 — Building after Tom's first try: walls on the deck, foundation height, stilts (2026-10-18)
Tom (build 63, swamp, dumps 130414 and 130944): after placing a foundation, the wall's ghost went **under** it; only
once a wall blueprint stood there did the next one go on the deck. Cause: the aim often slips past the deck's edge onto
the ground 0.3 m lower, and a wall hanging from the edge (its top points on the deck's edge points) has as many joints
as one standing on it and is *closer* to that aim. The snap now **prefers the side of the surface you aim at** (the hit's
normal) **and up over down**: a body behind the aimed surface costs 0.8 per metre, one below the aim 0.25 per metre.
Headless: a wall aimed at 13 points on and just outside a foundation's edges from 8 headings — the old scoring put 48
of 104 under the deck, now none; in the game (`--build-test`) a wall aimed at the ground just past the deck stands on it.
**Foundation height** (Tom: "change the height so it is not only sticking to the ground"): placed freely, a foundation
now stands **level above the highest ground under its corners** (no corner sinks into a slope); **Page Up / Page Down**
raise or lower it by 0.25 m (Shift: 1 m), **Alt + wheel** by 0.1 m, **Home** resets — the menu shows the raise. A new shape,
**Foundation on stilts** (6 logs, legs to 6.2 m, braced, deck 1.5 m up by default): for swamps, shores and steep ground.

## D124 — A build menu with pictures (2026-10-18)
Tom: "a nice interface with previews of the elements I'm building". The text bar is replaced by a menu along the bottom
(`WorldLab.BuildMenu.cs`): the categories as tabs, the current category's shapes as **cards with a picture of each piece**,
the materials (Logs / Thatch), the chosen piece's cost against what you carry (red when short), and what the ghost is
doing (snapped / free and raised / why it cannot go there), plus the keys. The pictures are rendered in the game from
each piece's own model (`PieceIcons.cs`: a small off-screen viewport with its own world and light, one piece every two
frames, kept), so they always match what is built — the campfire and lean-to too. With the cursor free (Tab) the tabs,
cards and materials can be clicked. The key help (F1) hides while building. Return point for the text bar: build 63.

## D125 — Aiming the bow with the right mouse (2026-10-18)
Tom: "when I hold the right mouse button, a kind of focus — an aiming mode." With the bow in hand, holding the **right
mouse** eases the camera in close behind the shoulder over 0.45 s (to 2.2 m, a little farther right), narrows the view by
up to a third (a gentle zoom) and slows the mouse by 45 % for a steadier aim; Alden faces where you look. Draw and loose
with the left mouse as before. (Tom wrote "zooms out": taken as moving into an aiming view — a one-line change if he
meant pulling back.)

## D126 — Finding animals (2026-10-18)
Tom found no animals. Main cause, mine: the in-game tests set *Simulation → Animals* to none and the game saved it into
Tom's settings (they ran with his user folder). His setting is set back to *normal*; tests now use an override that is
never saved. Also: where two biomes blend (a swamp's edge into forest), animals of either may live (both biomes' tags);
**deer** also live in wet lowlands (swamps, marshes, fens), **stags** in lowlands; groups appear 70–150 m away (was 90–170)
and up to 12 per player's surroundings (was 9). At Tom's swamp spot: 13 animals within 230 m after 45 s (8 deer, 5
wolves; `--census=45`). A HUD line tells how many animals are within 230 m and where the nearest is ("nearest deer 78 m
W"), or that they are switched off. Hot biomes (desert, savanna, jungle) still have none: the pack has no animals for them.

## D127 — Taking down and changing pieces with the mouse; campfires on decks; arrows found again (2026-10-18)
Tom (build 64): "I need to be able to dismantle and build objects; the campfire must be allowed on the floor, not only
on the ground; a mode that lets me use the mouse while building — selecting what to place and changing the type of a
placed blueprint; a way to retrieve arrows if I find them."
* **The campfire and the lean-to could not be aimed at** (drawn in code, they had no collision), so they could not be
  taken down or swapped. Every piece now has something to aim at; theirs is a box on the aim-only layer (nobody bumps
  into it).
* **Ground pieces stand on floors and foundations too**: a piece that may stand on the ground is also supported when its
  origin is on the top of a floor or foundation (`Building.OnFloor`) — a campfire on the deck, a free wall on a floor.
* **Choosing with the mouse**: **Tab** while building frees the cursor over the menu (a gold title says so); a click on a
  category tab changes the category, a click on a piece's picture picks it and goes straight back to placing it. Tab or
  Esc returns without changing.
* **The right mouse on a placed piece** (building open) opens its menu at the screen's centre: what it is (a blueprint's
  needs), **every shape of its category in every material** as pictures — a click changes it (blueprint or built: its
  materials come back and go into the new one) — and **Turn it round**, **Bring materials**, **Take it down**, **Close**.
  The right mouse elsewhere still closes building; the middle mouse and Backspace still take the aimed piece down.
  Every piece's picture is made as soon as building opens (about a second), so no menu waits for one.
* **Arrows**: a missed arrow is left where it fell 85 % of the time (was 65 %), one that struck an animal is in its
  carcass 75 % of the time (was 50 %). An **E prompt** under the screen's centre now says what E will do — pick up an arrow
  (or logs, a pack…), butcher the deer, bring materials to the aimed blueprint, open the door, put wood on the fire, pick
  up the stone — so a lost arrow is found by walking over it.
* Checked: headless (a campfire on a deck and not in the air, taken down again; arrows) and `--build-test` (a campfire on
  a lone foundation's deck, aimed at, its menu, choosing with the mouse, taken down by aiming at it).

## D128 — Testing tools (2026-10-18)
Tom: "a menu or a checkbox in the crafting menu so I don't have to chop down a lot of trees before I start testing."
A sandbox rule **Simulation → Testing tools** (off by default; in multiplayer the host's), also a checkbox in the pack
(I; a joined player sees the host's choice). On: **blueprints are built at once without materials** (recorded as
supplied, so taking them down gives the materials back), and the pack shows **Give materials** (60 logs, 60 branches, 60
plant fibre, 30 stones, 20 bones, 10 hides, berries) and **Give weapons and tools** (axe, knife, spear, bow, 40 arrows,
sword, rod, bait, roast meat) — the host checks the rule.

## D129 — Snapping that does what you turn it to; more snap points; steep stairs; fixes (2026-10-18)
Tom (build 65, dumps 133310, 133701, 142416): building is not versatile enough — "maybe more snapping points"; stairs
and half floors would not turn; picking a piece with the mouse placed it at once; a tree being chopped could be walked
through.
* **More snap points**: foundations and floors have a **0.75 m grid over their whole top** (25 points; a half floor 15),
  walls and half walls points every 0.75 m along their bottom and top, gables and peaks along their bottom (and the
  middle of a gable's slope), roof slopes and ridges every 0.75 m along their eaves and top. Inner walls can stand on any
  grid line of a deck, half floors, stairs and walls can sit at quarter positions, foundations can be staggered.
* **The player's turn decides the facing**: a snapped piece faces the quarter turn nearest the way you want it (the
  camera's heading, plus R in **90°** steps, the wheel in 15°) — a quarter turn away now costs more than a few meeting
  points, so snapping finds the *place* and you choose the *direction* (Valheim's way). Facing a foundation's edge, a wall
  lies along it; turned a quarter, it stands across the deck as an inner wall. Only **doors** still line up with their
  doorway by themselves (tag `align`).
* **Steep stairs** (3 m up over 3 m: 45°; Stairs → V): a staircase fits inside one 3 × 3 m room (the 4 m stairs do not).
* **Picking with the mouse no longer places**: the click that picks a piece (or a piece menu's button) was still held
  when the cursor was captured again and counted as a placing click; it is swallowed — the next click places.
* **A tree being chopped keeps its collision**: from the first hit it is drawn on its own (it can shake and fall), out of
  the forest's tiles — and their collision with it. It now carries its own trunk collision until it falls. Checked in the
  game: one hit, a probe in the trunk finds it solid (0 colliders before the fix, 1 after).
* **Dumps (F9) carry the building state**: what you hold, the chosen piece, turn and raise, the ghost's place and why it
  cannot go there, the pieces within 25 m (kind, place, turn, blueprint), the animals near and the pack.

## D130 — Snap points shown (2026-10-18)
Tom: "could we make the snapping points toggleable, visible or invisible?" While building, small dots (drawn through
walls) mark the snap points of the pieces within 10 m of the ghost (pale blue), the ghost's own (yellow, larger) and the
ghost's points that meet a piece's (green) — so you see where a piece can join and that it did. **P** while building
switches them; *Interface → Show snap points* (on) remembers it.

## D131 — The bow hits where you aim; arrow trails (2026-10-18)
Tom: "there seems to be an offset when aiming a bow; it's hard to tell if I'm aiming correctly; a small trail after the
arrow to see the arc, fading out."
* **The offset was the drop**: the arrow left toward the point under the crosshair and fell on the way (about 1.6 m at
  30 m at full draw, much more with a short draw). Now the shot is **raised just enough to come down on that point**
  (the lower of the two ballistic arcs for the arrow's speed; beyond its reach, 45°), so a part-drawn arrow lands on the
  crosshair: in the game, 0.00 m from it at 30 m. Setting *Interface → Bow aim*: *allow for the drop* (default) or
  *straight* (as builds 63–66: hold over yourself). The arc stays real — longer shots fly visibly higher.
* **An animal under the crosshair is the target**, not the ground behind it (animals have no physics shape, so the aim's
  ray passed through them and the drop was allowed for the ground beyond: arrows went over the stag). Their body test is
  now exact (closest points between the ray and the body's axis) instead of nine samples along the segment.
* **Arrows fly the exact arc** from where they were loosed (no drag, computed from the time since): the same on every PC
  and at any frame rate, and what the aim allowed for (the frame-by-frame flight with drag fell ~1 m short at 30 m).
* **Trails**: a thin pale ribbon behind every arrow (yours and others'), turned to the camera, each part fading over
  1.4 s — the whole arc visible for a moment, gone shortly after the arrow lands. *Interface → Arrow trails* (on).
* The E prompt hides while drawing or aiming.

## D132 — Tracking and scanning, blood, sneaking, stamina, aiming from either shoulder (2026-10-18)
Tom (build 67, after his first deer): blood where an animal is hit; a Soulmask-like scan key that highlights the
terrain's interactable things and shows animal tracks that stay for a while (a base for a tracking skill and skill
levels later); sneaking to approach animals; the bow in the right hand; a key to switch the camera's shoulder while
aiming; aiming should slow you down — stamina; he could not see the loose stones.
* **Blood**: when an animal's health falls (it comes with the positions, so every PC sees it), a burst of dark red drops.
* **Tracks**: every animal this PC sees leaves a pair of prints every 1.2 m (smaller for foxes and wolves), kept 5 minutes,
  hidden until you scan. **Z scans**: a ring sweeps out 45 m in a second; what it passes is marked for 20 s (fading in the
  last 5): the 30 nearest loose stones and berry bushes (named within 15 m), arrows, piles and packs, carcasses ("deer
  (butcher)"), blueprints and what they need, animals with their distance (to 72 m) — and the tracks light up amber. A
  6 s rest between scans. The radius and the tracks' lifetime are constants a tracking skill will raise later.
* **Loose stones** are drawn, but at 15–25 cm and partly sunk they disappear in dense grass (Tom's grass density 3): the
  scan marks them and the E prompt names the one at your feet. (Bare ground around them would change the ground
  cover's generation: left for Tom to decide.)
* **Sneaking**: **C** crouches (walking only): 2.2 m/s, the body low; animals hear a sneaking hunter at 35 % and see them
  at 60 % of the usual distance (the top bit of the state's held byte tells the host). Flying stands you up.
* **Stamina** (with *Hunger and cold* on): sprinting spends 16 a second (about six seconds from full), a jump 8; it
  refills at 14 a second standing, 8 walking, half as fast when hungry or cold; at 0 you are out of breath until 30.
  **Aiming or drawing the bow** slows you to 2.6 m/s and allows no sprint. The needs line shows it.
* **V switches the shoulder** the camera sits over (eased); **Interface → Bow hand**: right (default now) or left (builds
  63–67).

## D133 — Smaller pieces; walls on upper floors (2026-10-18)
Tom: "a little bit more blocks, like quarter blocks, half blocks"; walls aimed at a second floor snapped to the floor
below (dump 154613: wedged between the storeys — its top on the upper floor's grid, its bottom on the foundation's,
twice the joints of a wall standing on the floor). New pieces (Blender): **half foundation** (3 × 1.5 m, 2 logs),
**quarter foundation** (1.5 × 1.5 m, 1), **quarter floor** (1.5 × 1.5 m, 1), **narrow wall** (1.5 m wide, 2), **narrow half
wall** (1.5 × 1.5 m, 1), all on the 0.75 m grid. The snap now charges **2.5 per metre for a body behind the surface you
aim at** (was 0.8), so a wall aimed at a floor stands on it; a test builds a foundation, a wall and an upper floor and
aims at the upper floor from eight places and headings. (A first try capping the joints at five broke ridge roofs,
which truly meet ten points: reverted.)

## D134 — Building and hunting after build 68; a sonar scan (2026-10-18)
Tom (build 68, dumps 164310–165142): a wall would not go somewhere ("something is in the way", nothing visible), then did
from a higher spot; toggleable shadows for blueprints; wolves bit him through the floor of a high foundation; grass and
stones could be gathered from up on a platform; the scan should look like sonar — a wave growing from the player that
lights the surroundings — and scanned animals could glow for a while.
* **Nothing blocks unseen**: when a piece is in the way, the bar names it (kind and id) and it **flashes red** while it
  blocks. The overlap test shrinks long sides a little more than thin ones (walls meeting in an L touch exactly side to
  end; a test at Tom's corner, turned 208° and 210 km out, nudged both ways). The dumps did not catch the blocked moment;
  the likeliest cause is the facing (D129): from his first spot the camera turned the wall across the one beside it.
* **Blueprint shadows**: *Interface → Blueprint shadows* (off = as before).
* **Bites need the same level**: a predator bites only within 1.6 m up or down — on a platform you are safe; it waits
  below (animals still walk through pieces).
* **Gathering on your level**: stones and berries within 1.6 m of your feet's height, grass only with your feet on the
  ground there; the host refuses foraging more than 2 m above the ground.
* **Sonar scan** (`ScanWave.gdshader`): a screen-filling pass reads each pixel's world position from the depth buffer and
  lights a 3 m band where its distance from the scan's centre is the wave's front (out to 45 m in 1.4 s), leaving a faint
  rippled tint behind it that fades over five seconds. Scanned animals (within 72 m) **glow** (`AnimalGlow.gdshader`: a
  rim of light laid over their materials, seen through grass and trees) for 12 s, fading. *Interface → Scan look*: sonar
  wave / ring (build 68).
* **Next (Tom)**: diagonal walls — snapping at 45° with their own turning steps (a diagonal wall piece 3·√2 m long, points
  on the diagonals of foundations and floors, and triangle foundations and floors to close the corners).

## D135 — Diagonal walls, triangles; animals go round trees and buildings (2026-10-18)
The last pieces of the multiplayer proof of concept (§14.a step 1; Tom: "allow players to build diagonal walls — rotate
and snap them diagonally with different rotation steps").
* **Diagonal walls** (full and half height, 3·√2 = 4.24 m long, 4 and 2 logs) with points every 1.06 m along their bottom
  and top: corner to corner across a foundation or floor their points meet the corners, the quarter points and the middle
  of the 0.75 m grid. Pieces tagged `diagonal` snap in **eighth turns** of what they join and **R turns them 45°** (the
  rest: quarter turns, R 90°). **Triangle foundation** and **triangle floor** (half of a 3 × 3 m square cut along its
  diagonal; 2 and 1 logs) close the corners: their overlap box is the triangle's core, so two complete one cell, while the
  same triangle twice or a square over them is in the way.
* **Animals go round obstacles** (host): steering probes a body length and a stride ahead in nine directions for water,
  too-steep ground, **tree trunks** (from the world's tree lattice, so it works wherever the animals are, not only near
  the host) and **finished building pieces** (a point at the animal's back height inside a piece's box, grown by its body:
  walls, stairs and low decks block; a deck high on stilts can be walked under); a step into an obstacle is not taken. An
  animal already inside one (spawned in a thicket) ignores obstacles until it is out. Loose rocks are not seen (only the
  drawn tiles know them; the slope check keeps animals off boulder fields). Cost: none measurable (33 → 32 FPS with 24
  animals in a dense forest at Tom's settings).
* Checked: headless (a diagonal wall corner to corner on both diagonals, five points meeting; triangles beside a square
  and completing a cell; a deer fleeing at a trunk goes round it — without the avoidance it ran through, 0.10 m from the
  trunk's centre; a deer fleeing at a row of walls never inside them) and `--build-test` (a diagonal wall and a triangle
  foundation built, pictured).

## D136 — Walls and stairs on edges (2026-10-18)
Tom: "can diagonal walls and other elements — diagonal doorways and doors — snap to a diagonal line by themselves? Or a
better way?"; "we will need diagonal steps to snap to diagonal triangle floors." Point-to-point snapping left the size
and the angle to the player (a diagonal wall was a shape of its own, turned by hand) and caused most of the odd
placements (walls wedged between floors, turned across a deck). Walls and stairs now attach to **edges**, as in
Soulmask and Conan Exiles:
* **Every floor and foundation has edges** (`edges` in `gameplay_pieces.json`): its sides, the lines through its middle
  (inner walls), a square's two diagonals, a triangle's long side; **a wall's top is an edge** (walls stack).
* **Walls come in families** (`family` + `length`): wall (3 m, narrow 1.5 m, diagonal 4.24 m), half wall (the same three),
  window and doorway (3 m and diagonal: new diagonal window and doorway models), gables and peaks (3 m). You choose the
  **shape** (V; the menu shows one card per shape); the **edge picks the size**: aimed near a foundation's diagonal, a
  doorway is the diagonal doorway; on a half foundation's short side a wall is the narrow one. The wall is centred on the
  edge, facing the side nearest the way you look (R flips it). Doors still line up with their doorway, so a door in a
  diagonal doorway is diagonal. Swapping a placed wall (aiming at its middle, or its right-click menu) keeps its size.
* **Stairs attach by their top** to an edge, slid along it to the nearest quarter of it, **running up onto the piece**
  that has the edge (only when you clearly face the other way, up from beneath it): onto a triangle's long side they come
  out diagonal by themselves.
* **Which edge**: the nearest to the aim within 1.6 m, an inner line costing 0.25 m and an edge askew to the view up to
  0.4 m — where a side and the middle line meet, the side you look at wins. No edge in reach (or Ctrl): the point snapping
  as before (floors, roofs, doors and the camp pieces always use points).
* Checked: headless (a wall near the diagonal is the diagonal wall corner to corner, near a side the straight one, on a
  half foundation's short side the narrow one; doorway and window near diagonals are the diagonal ones; steep stairs up
  onto a triangle's long side, diagonal, their top on it; stairs up onto a foundation's side) and `--build-test` (a
  doorway aimed at a diagonal is the diagonal doorway, a door in it, steep stairs onto a triangle's long side; the earlier
  houses built through the edges).

## D137 — Vertical half and quarter walls, fences; stairs on floors, floors over rooms; a crash on edges (2026-10-18)
Tom (build 71): "we're missing half a wall — we have the horizontal one, not the vertical; and maybe a quarter wall; and
a fence"; stairs could not be placed on floors and foundations (no second storey); a floor put on walls snapped in the
middle (dump screenshots).
* **Half wall, vertical** (1.5 m wide, full height) and **quarter wall** (1.5 × 1.5 m) are shapes of their own again
  (build 71 had hidden them as sizes of the wall): on a 3 m edge they **slot into the half nearest the aim**; the
  horizontal one is now called *Half wall, horizontal*. Walls and half walls on a 1.5 m edge still use them by themselves
  (`smaller` in the content file).
* **Fences** (new category *Fence*): a 3 m rail fence, the diagonal one, the narrow one (posts into the ground, two rails;
  1 log and 2 branches); on floor and foundation edges like walls, or freely on the ground, **chained end to end**,
  turning at the joints in 45° steps.
* **Stairs aimed at the top of a floor or foundation stand on it** (by their points, toward the next storey) instead of
  hanging from its edge; aimed at an edge from outside, they still run up onto it.
* **Floors join others only by the points of their outer edges** (`joins`), and a floor whose middle lies over a floor or
  foundation a storey below gets a 0.8 bonus: on a room's walls a floor lies over the room, a half floor along the wall,
  inside.
* **A crash**: placing on an edge a hair shorter than its nominal length (2.9999999 m for 3, from rounding) made a clamp
  throw and the ghost stop updating — build 71's stairs on 1.5 m edges too. Clamped safely now.
* Checked: headless (vertical half and quarter walls in either half of a side; fences on a side, on the diagonal, chained
  on open ground and turned 45° at a joint; a floor aimed at any of a room's wall tops lies over the room; a half floor
  along the east wall, inside; steep stairs aimed at the deck stand on it) and `--build-test` (vertical half and quarter
  walls, two fences chained on the ground).

## D138 — The real multiplayer test: 12 players hunting and building (2026-10-18)
GAMEPLAY_DESIGN §14 step 2 / §14.a item 2: 4–12 players with hunting and building, host load, bandwidth, sync.
**How it was run**: no twelve people, so **bot players** (`Game/Rewrite/Net/NetBots.cs`): a joined game started with
`--bots=N` opens N more connections to the host, each its own player with its own client part, sending exactly what a
player's PC sends — positions 20 a second, `Give` (testing tools), bow shots (`Shot` + `Shoot`), `Butcher`, `Place` of
log huts (4 foundations, 8 walls snapped with `Building.SnapPiece`, a campfire), and `Dismantle` of the oldest hut. They
walk on the ground this PC built and draw nothing. `--bot-spread=m` (0: together; 3000: each in a place of its own),
`--bot-huts=n`, `--net-run=s` (summary and quit; the host turns on testing tools and **many** animals for the run, never
saved), `--net-lag=ms[,loss%]` (a simulated slow line). The bot carrier and/or the host can run `--headless`, so two games
on one PC do not share the GPU. `TEST_MP_BOTS.cmd [address] [bots] [spread] [seconds]` for Tom: bots into his own game.
The network log gained the host's load each second (players, animals, objects, frame times, the server part's time, the
animals' thinking and messages, the world queries they make, main-thread steps, GPU time, draw calls), and every 10 s a
**BOTS** line (bytes per bot by kind, actions done and refused and why, how long the host took to answer, gaps between
positions, objects each bot knows, how far from their true place this PC draws them). Place: Tom's forest from dump
190242 (`--at=-40494.721,-105936.31`), 12 players = host + carrier + 10 bots, late joiners with `--guest-check`.

**Found and fixed**
1. **Bandwidth**: each player received **26 KB/s** (host upload ~300 KB/s for 12 together): positions 11 KB/s (46 bytes a
   player, doubles), animals 14 KB/s (~40 bytes an animal, its kind as a string). Now **compact** (protocol 11): positions
   26 bytes a player (offsets from one point, half-float velocity), each player is not sent itself, players farther than
   400 m 4× a second instead of 20 (`NetServer.NearPlayers`); animals 18 bytes (kind as an index, centimetres from the
   player, 1/65536-turn heading). Together: **14 KB/s per player (host ~160 KB/s, 1.3 Mbit/s)**; apart: **7.6 KB/s
   (host ~85 KB/s)**.
2. **Host CPU, players apart**: 12 places = ~350 animals; their thinking took **250 ms a second** of the host's main
   thread (48 fps, frames up to 45 ms). 80 % of it was the trunk check (`IWorldQuery.Obstacle`, ~5,000 calls a second
   at 27 µs, each growing the trees of its lattice cells again from terrain, biome and rivers). Each cell's tree is now
   worked out once (cache, felled trees still checked live): **5 ms**; the animals' total **~35 ms a second**, host
   steady **60 fps**.
3. **Placed objects were found by walking all of them** (`PlacedObjects.Near`: animals ten times a second, building
   ghosts, the rules): a 16 m grid now, kept up to date through moves and network copies (test *placed objects found by a
   grid*).
4. **A joined PC's time grew with every piece built**: each frame, every object view was moved and its children searched
   for flames (2 ms a frame at 206 pieces, climbing). Views are now placed when they change or the floating origin
   moves; fires keep their flames and light, found once. Flat at ~1 ms a frame from 13 to 492 pieces.
5. **Errors when several players left at once** ("Unable to send packet on channel 0") came from Godot's server relay
   (notices of others joining and leaving, which the game does not use): off; nothing is sent to a closing connection.
6. A window sized on the command line (`--window=`: tests, benchmarks, TEST_MP windows) no longer saves its size into
   the player's display settings; `--guest-check` accepts a campfire built at once when the host has testing tools on.

**Measured after the fixes** (Ryzen 9 5950X, RTX 3080, all on one PC): host with 12 players together, ~45 animals, 400
pieces: 60 fps, server part ~40 ms a second (animals ~35), GPU 8 ms a frame (host alone: 9 ms; the earlier 25 ms and 45
fps were the second game window sharing the GPU). A joined PC drawing 11 players, ~50 animals and 500 pieces: GPU
9–11 ms, 60 fps. **Sync**: every bot and the carrier always knew the same objects as the host (all 413 / 492 / 373 in
the runs); positions arrive every 50 ms (far ones 250 ms), drawn **0.3–0.4 m** from where the player really is (100 ms
interpolation; **0.6 m, max 1.7 m** with 320 ms round trips); actions answered in 15–40 ms locally, **~350 ms** at
`--net-lag=160,1` (the Brazil line); late joiners received all pieces (316–444) and the animals near them, and placed a
campfire through the host. Not measured here: real routers, playit.gg's relay under 12 players' traffic, Tom's upload.
**Next for the test with people**: Tom hosts, friends join (and `TEST_MP_BOTS.cmd` fills up to 12); the logs of both ends
tell the rest.

## D139 — The torch in mist: a halo at the flame (draft, Tom to pick); mist in dumps (2026-10-18)
Tom (dump 221438, build 57): the torch lights the ground fog evenly far into the night — far trees stand dark against an
orange haze. **Reproduced** on build 73 with Tom's Reproduce line once a mist bank sits round Alden (the banks drift
with the wind and the drift was not in dumps): the torch's light (16 m, fog energy 1, Godot's default) lights all of the
bank within its reach evenly — a 30 m orange ball the trees stand in. Only with the fog's frame-to-frame smoothing on,
which still shots switch off. Also in the same shots: **the smoke as white balls** at night — unshaded dark grey,
raised to white by the night's exposure (the torch's own light already skipped it, D98).
* New setting **Lighting and effects → Torch in mist** (`fx.torch_look`): *build 72 (even glow)* — the default until
  Tom picks — or *halo at the flame*: the torch's light lights the ground as before but the fog only at 0.12; a second
  light without shadows, lighting no surfaces (cull mask 0), only the fog (energy 3.5, 5 m, falling off steeply) makes a
  glow round the flame that fades within a few metres; the smoke is shaded (moonlight and sky, so dark at night).
  Without mist the two look the same on the ground (shots compared).
* Test tools: `--shot-live` (a `--world-shot` that keeps the fog's smoothing), `--mist=amount[,1]` (mist forced; `,1` a
  bank everywhere — a test-only shader input, 0 in play), and dumps now carry `--mist-drift=x,z` and `--wet=ground,puddles`
  in their Reproduce line, so the same mist comes back.
* Captures (top build 72, bottom halo): `artifacts/torch/torch-in-mist-…jpg`, `torch-no-mist-…jpg`.

## D140 — Smoke from Tom's texture, for torches and campfires (2026-10-18)
Tom added `smoke_particles_texture.png` (a 256 px puff with soft alpha): "add this texture for smoke for torches and maybe
campfires". Moved to `Assets/Effects/smoke_puff.png` (mipmaps on: smoke is seen from afar). One emitter for both,
`Game/Rewrite/SmokePlume.cs`; each puff turned its own way and slowly spinning, so the one picture does not repeat.
* Setting **Lighting and effects → Smoke** (`fx.smoke`): *smoke texture* (default, Tom asked for it) or *build 72 (soft
  dots, torch only)* — build 72's torch smoke unchanged, and no campfire smoke, as before.
* The textured smoke is always lit by the scene (moon, sky, sun), so it is not raised to white at night (D139); the
  torch's lights skip it (its layer), a campfire's firelight lights it — a warm glow over the fire at night. Near-white
  ramp: grey picture × grey ramp under daylight read almost black.
* **Campfires smoke** while finished and burning (not as blueprints, not once the fuel is out): a taller, slower plume.
* Tom (first look): the torch's smoke "starts a little too far off the torch … denser, more particles"; the campfire
  looked good. Cause: the first puffs were clear and tiny, seen only after the wind had carried them toward the camera.
  Now visible from the flame's tip, bigger at birth, 36 instead of 18.
* Fixed on the way: a newly built campfire's first puffs started at the scene's origin (the view was placed a frame
  later since D138) — object views are placed when created. `--shot-campfire` (captures): a lit campfire beside Alden.
* The texture is Tom's own (self-made), noted in `Licenses/README.txt`.

## D141 — Caustics by the sun; a shorter scan wave, items glow; crouching bends the waist (2026-10-18)
Tom on build 74 (dumps 214459, 214534, 214828):
* **Caustics bright at dawn and at night** (a white net on a pond bed, the sun 4° up): the water shaders added a fixed
  amount (`0.4 + ground`) whatever the light. Now scaled by a global `caustic_light`, the direct sun the caustics focus:
  smoothstep from 2° to 30° of sun elevation, less under a storm and an overcast (Atmosphere). Setting **Caustics**
  (`fx.caustic_light`): *by the sun* (default) or *build 74 (always)*. Water.gdshader, WaterExperimental.gdshader,
  WaterSea.gdshaderinc (all the water styles).
* **The scan's circle should fade sooner, leaving the marks**: the sonar wave and its tint are gone by 3 s (were 6 s);
  the marks stay their 20 s, the glows their 12 s. **Items glow like animals**: stones and berry bushes get a copy of
  their own meshes wearing the glow (in the marks' colours: grey-white, pink), shown as the ring reaches them;
  carcasses, piles and blueprints get the glow over their own meshes. `--scan-test` takes a third picture at 4 s.
* Found on the way: the textured smoke's **backlight** (D140) stopped the sonar wave from drawing while a torch smoked
  (the wave showed only round Alden). Backlight off.
* **Crouching bent the legs**: the figure's legs are rigid, swung forward from the hip into the ground. Now the body
  from the belt up hangs from a waist pivot that leans forward (0.55 rad), the head from a neck pivot turned back
  (85 %), so the gaze stays almost level; the legs stand one before the other, angled so both feet reach the ground,
  0.1 m lower. Built in Character from Visuals.Person (unchanged: the original game uses it). `--shot-crouch`.
* Checked: `--hunt-test`, `--build-test` (weapons and tools hang from the arms, now under the waist), captures of
  each change.

## D142 — Rays like Unity HDRP's as M2's target; heavy effects stay player settings (2026-10-18)
Tom, after trying Unity HDRP: its volumetric lighting (volumetric fog lit through the sun's shadow map — crepuscular
rays / god rays) is the quality he wants for the forest rays. Godot 4.7 has the same family of technique (a froxel
grid, shadow-mapped lights, temporal reprojection); it falls short by default on grid resolution (ours 96 × 96 × 96 over
180 m), on denoising (reprojection only) and on per-light controls. M2's plan (HANDOFF "Next" item 3): captures beside
Tom's HDRP screenshots, then tune the native fog (finer grid, shorter length, sun energy, anisotropy) with benchmarks,
and only if that falls short, our own ray-marching pass.
**Rule (Tom): settings that may be heavy for some GPUs and PCs stay selectable in the options, also in the finished
game**, so players choose: the volumetric fog's resolution and the rays' quality join *Camera and performance* (or the
effect's own page) as levels with an *off*, and presets (M6) only set them. This adds to CLAUDE.md's rule that visual
changes stay switchable: switches made for comparing looks may go once Tom picks; performance and quality levels stay.

## D143 — M2 rays at sunrise: tuning Godot's own volumetric fog; A2 draft, fog quality levels (2026-10-18)
Step 1–2 of M2 (D142). Tom's HDRP screenshots are not in `docs/references/` yet: captures made without them.
* **Scene**: suite `rays2` — `rays-sunrise` (first person, facing the sun) and `rays-sunrise-turn`, 05:24 (sun ~10° up,
  summer, latitude 45), mist forced at 0.35 (new scene option `"mist": [amount(, 1)]`), Forest rays A, light model 1,
  `patch32` at [-3141.8, 2362.6]. The old `art-forest-shafts` place faces a hillside at sunrise (the sun behind it):
  the new spot came from **`--ray-spot=x,z,azimuth[,elevation[,radius]]`** (developer tool: dense temperate forest on
  level ground whose land toward the sun stays below it for 1.5 km). Dense there: no 9.5 m clearing for the
  third-person camera, so the shots are first person. Captures hide the gameplay HUD too (needs line, prompts).
* **Rays A as in build 75**: an even faint haze; no shafts visible at sunrise.
* **Tuning** (`--ray-dev=key=value,...` developer tool; RTX 3080, profile high, 1920×1080, GPU ms against 25.4):
  froxel grid 160 × 160 × 128 +0.8 ms, 256 × 256 × 160 +3.2 ms (visibly smoother shafts); the grid, the filter and a
  shorter fog (80 m, spread 3) alone change almost nothing visible — the rays appear only with **more sun into the
  forest air (×8 instead of ×2.8), less sky light injected into the fog (0.15 instead of 1: it filled the shadow lanes)
  and stronger forward scattering (0.75)**: shafts from the sun through the crowns and short horizontal shafts between
  the trunks at ground level. Lower anisotropy (0.45–0.6) with denser air softens the glare round the sun but blurs the
  shafts; views across or away from the sun stay dark in every variant (shadowed air, little side scatter).
* **Not reached with Godot's fog**: HDRP's many crisp lanes across the whole frame. Remaining options: denser/brighter
  air near the ground only, or step 3 (our own ray pass through the shadow map) — after Tom compares with his screenshots.
* **Switchable** (defaults unchanged): *Forest rays* gains **A2: sharp rays (M2 draft)** (density 0.03, sun ×8,
  anisotropy 0.75, ambient 0.15, fog length 80 m and spread 3 — blended in with the forest, back to build 75 values
  outside it). New **Camera and performance → Volumetric fog quality** (`perf.fog_quality`, D142 rule): *off* (no
  volumetric fog at all: rays A/A2, mist patches, the torch's glow) / low 64³ / **build 75 96³ (default)** / high
  160 × 160 × 128 / very high 256 × 256 × 160.
* Captures (local, `artifacts/rays/`): `m2-sunrise-A-vs-A2.jpg` (A vs A2, same frames, grid 160), `m2-sunrise-tuning-variants.jpg`.

## D144 — Rays seen from the side: forward scattering as a slider; the noon sun; test --set not saved (2026-10-18)
Tom on build 76 (dumps 232115, 232214, 232225): A2's shaft through the crowns looked good, but turning away it was
gone — "only when I look at the sun, or the sun is on the screen". Cause: Godot's fog has one phase function for the
whole volume (Henyey–Greenstein); A2's anisotropy 0.75 sends ~650× more light straight forward than sideways, so a
shaft lights up only within a few tens of degrees of the sun. Reproduced from his dump at four yaws.
* New setting **Forest rays A2: forward scattering** (`fx.ray_scatter`, 0.2–0.85, default 0.75 = build 76). Less
  forward scattering throws less light toward the camera, so the sun's energy into the air rises as it falls (×8 at
  0.75 → ×20 at 0.3). At 0.3 the shaft toward the sun is nearly as bright and the side and back views gain lit haze
  and soft lanes, at some cost in contrast. Tom to pick by playing (no HDRP screenshots: his eye is the reference).
* **"The sun at zenith is not above my head"**: his Season is *winter* at latitude 43.6°, so the noon sun reaches
  90 − 43.6 − 23.4 = 23° (8.9 h of daylight). Correct astronomy: in summer it reaches ~70°, overhead only in the tropics.
  No change.
* **Command-line `--set` no longer saves** into the player's settings (`GameSettings.Override`): a Reproduce run with a
  changed value would have kept it in Tom's settings file. (Earlier runs only wrote his own values back.)
* Game window on the other monitor: `display.cfg` remembers monitor 2 (saved when the window was last moved or
  resized there); test runs open on the saved monitor too. Settings → Display → Monitor picks it. No change.
* Capture: `artifacts/rays/m2-dump232214-scatter-0.75-vs-0.3.jpg`.

## D145 — Setting the clock passes no time for the body; test runs never autosave (2026-10-19)
Tom on build 77: "why am I getting teleported back to the spawn location?" — while dragging the time slider to test
the rays in winter (Warmth 26 in his dumps). The needs (and fires, animals) took the clock's change each frame as time
lived, ignoring only jumps over 2 h; a slider dragged a little each frame passed them all. `--clock-sweep-test`
(headless: 06:00 → 18:00 in 0.05 h steps, winter, needs on): before, Food 78 → 54, Warmth 54 → 34, Health 100 → 89
per sweep — a few sweeps and he froze to death, and dying starts a new life at the world's start (D115). Now
`GameClock.Jumps` counts every set (slider, --hour, a loaded save, a snap to the host's clock) and a frame with a jump
passes no time: the same sweep leaves Food 78, Warmth 54, Health 100. A fix, no switch.
* My capture runs on Tom's PC (23:29, 23:48) had written `autosave.twsave` into his saves (the autosave ran in test
  runs too). Autosave is now off in shots, smoke runs and benchmarks.

## D146 — A2 through the day; a frozen capture run left its settings in Tom's file (2026-10-19)
* Suite **`rays3`**: the `rays2` spot turning at sunrise (05:24), morning (08:30, ~40°), noon (12:00, ~68°) and
  evening (18:30), *Forest rays* A2, fog quality high, every still at forward scattering 0.75 and 0.3. Sunrise,
  morning and evening: shafts as intended (0.3 adds milky haze). **Noon: the whole air went milky white** — a high sun
  lets far more light through the crowns and A2's ×8–20 boost floods it. A2's boost now eases from a 30° to a 65° sun,
  down to 35 % (draft, part of A2). Rerun (no freeze, run under a watchdog that stops it if its log stalls for 2 min;
  Tom's settings and saves unchanged, checked by hash): the veil is gone at both values, the shafts through the crowns
  stay; at 0.3 some haze remains. Morning (40°) keeps ~87 % of the boost. `artifacts/rays/m2-noon-before-after.jpg`.
* The rerun **froze** (00:20, between two noon stills while turning): 0 % CPU and GPU, nothing in Godot's log, no
  driver reset in Windows' log. Cause unknown; not seen in the earlier runs of the same suite. Killed after Tom saw it.
* **The benchmark wrote its test profile into Tom's settings file** while running (restored only at its end), so the
  killed run left 44 changed values (season, view distances, grass, clouds, V-Sync…). Restored from his dump 232225
  (the old file kept as `rewrite_settings.bench-left-20261019-0020.cfg`; *Forest rays A2: forward scattering* set to
  0.75). Now **settings are not saved while a benchmark runs** (`GameSettings.SaveSuspended`), so a killed run leaves
  the file as it was — Tom's rule that tests never save settings. Also: the fog grid reaches the renderer only when it
  changes (ApplyAll runs on every setting change; it was resent on each A/B still).

## D147 — Shafts with the sun behind a mountain; why nothing shows on a high peak (2026-10-19)
Tom on build 79: dump 005618 (taiga 1,896 m, facing the sun) shows a bright shaft "even though the sun is behind the
mountain"; dump 005742 (tundra 3,729 m) shows no sun effects at all: "is there a certain height where it works?"
* **005618**, rebuilt with one effect off at a time (`artifacts/rays/m2-dump005618-what-lights-the-air.jpg`): the glow
  goes only without *volumetric fog* or without *Forest rays A2* (light beams under clouds and the screen-space sun
  shafts change almost nothing). Without the sun's shadows the whole valley glows evenly, so the shadow map does shade
  the air: the band is the air past the edge of the mountain's shadow, where the sun slips past its shoulder (a real
  effect when the sun stands just behind a ridge). New probe **`--sun-block`** (with a dump's arguments): toward the sun
  the land rises above the sun's line from 120 m out (horizon 29.4° against a 17.9° sun) — the sun is hidden from
  Tom's spot, the air beside the peak is not. It reads strongly because A2 is on in this taiga (canopy from the forest
  density, though the trees there are sparse) with forward scattering 0.85 (Tom's value): very bright toward the sun.
* **005742**: no effect applies there. A/A2 need forest around and switch off 12–30 m above the ground; mist pools in
  valleys below the player; the light beams belong under the cloud deck (bases ~1,500 m above the sea; that shader
  places the deck 1,600 m over the camera wherever it is — wrong above the deck, to fix with M3); the sun was not
  hidden there (horizon 4°), so nothing cut shafts; the global volumetric fog is thin, even haze. No change yet:
  rays in open mountain air would be a new effect (Tom's call).

## D148 — Rays in open air: a draft pass through a terrain sun-shadow map (2026-10-19, in progress)
Tom wants shafts everywhere outdoors, not only in forests; his references (kept locally, not in git:
`artifacts/rays/reference-1-cloud-shafts.webp` — a stock photo with a watermark — and `reference-2-mountain-shafts.png`,
a game screenshot): shafts from a gap in the clouds over a fjord, shafts past mountain peaks at sunrise.
* Why Godot's fog cannot: it reaches ~180 m, the sun's shadow map 450 m, distant terrain casts no shadow (D147).
* **Draft**: setting *Rays in open air (M2 draft)* (`fx.air_rays`: off / low / medium / high = 24 / 40 / 64 steps;
  default off). `WorldLab.AirRays.cs` builds, on a worker thread, a 256² map over 20 km around the player: ground
  heights and each cell's **sun-shadow height** (the lowest height whose air sees the sun over every ridge toward it);
  heights ~0.15 s, shadows ~10 ms, again after moving 2.5 km or when the sun moves ~0.36°. `AirRays.gdshader`
  (full-screen, after the clouds in front of the land) marches each view ray: air below the shadow height is in a
  mountain's shadow, air under a cloud toward the sun is in the cloud's shadow (the deck at its real height, dimmed as
  the ground's cloud shadows); the haze's opacity is Godot's distance fog's, thicker low down and close above the
  ground. It **darkens Godot's haze where the air is shadowed and adds sunlight where it is lit**, so Godot's colours
  and aerial perspective stay (a first version that replaced Godot's fog went black at dawn: that fog's colour is
  mostly the sky's).
* **Where it stands**: the shadow geometry is right (probes and debug views: the sunlit share of the haze, the haze's
  opacity), no shader errors, Tom's files untouched in every run. Visible only exaggerated so far: a mountain's shadow
  as a dark wedge with a sharp edge across the sky (`artifacts/rays/m2-air-rays-first-shadow-wedge.jpg`). Not yet
  many shafts nor Tom's look. Found on the way: the light beams' cloud-cover dimming hid it under Tom's 52–80 % cover
  (removed for the pass); the near-cloud pass covered it (it now draws after); test spots above the cloud deck or
  wholly in one mountain's shadow show nothing by nature.
* Developer tools: `--air-preview=out.png[,yaw offsets]` (headless: the sunlit share of the air per view, from the
  same map), `--ray-vantage` (sunlit raised viewpoints with many lit/shadow changes toward the sun), `--sun-block`,
  `--ray-dev=air…` (airgain, airside, airsun, airshade, airdens, airhaze, airvalley, aircloud, airdebug 1/2/3).
* Also fixed: a Reproduce line's `--hour` was set at the start position's latitude, so the clock read another hour
  after the jump to `--at` (dump 005618: 10:59 instead of 10:40, sun 22.9° instead of 17.9°).
* Next: a finer map (more, narrower shafts), strength, a clear-weather test scene against the references, the cost.

## D149 — Air rays: the mountain blocks the sun; a finer map; a scene like the references; the cost (2026-10-19)
Tom: "keep going: finer map, strength, a clear-weather scene against my references, the cost". On the way, Tom: "it
still looks like the light is going through the mountain".
* **Light through the mountain**, three causes, all handled while *Rays in open air* is on: (1) Godot's distance fog
  glows toward the sun on every pixel — its sun scatter moves into the pass, where only sunlit air gets it, and the
  sky's glow returns by the sunlit share of the air toward that point of the sky (a peak's shadow runs through it);
  (2) the screen-space sun shafts smear the bright sky across the silhouette — off while air rays are on; (3) near
  air and ground knew nothing of a mountain more than ~600 m away — the near volumetric fog is dimmed by the
  sun-shadow map while the camera stands in a mountain's shadow, and terrain tiles cast shadows to level 3 (~3 km;
  the setting re-lays the tiles). Valley at 09:12 with the sun behind the mountain: before, glowing through it; now the
  valley, its air and the ground are in the mountain's shadow (`artifacts/rays/m2-air-rays-mountain-blocks-the-sun.jpg`).
* **Map 512² over 20 km** (39 m cells; heights ~0.5 s, shadows ~0.1 s on a worker thread). Water surfaces count as
  the ground (a sea floor's ridges were taken for land).
* **Scenes**: Tom's mountain world puts the cloud deck (~1,500 m above the sea) at valley height or below his
  1,900–3,800 m terrain, so most views there look through or over cloud. `--ray-vantage` (and `=edge`: the sun at a
  ridge line) found, in `patch32`, a lake below a cliff at sunrise (05:24, sun ~10°, 35 % cloud): benchmark scene
  **`air-cliff`**. There the pass shows a lit haze shaft past the cliff's edge with the cliff's shadow beside it
  instead of the glow smeared over the rock (`artifacts/rays/m2-air-rays-cliff-vs-reference.jpg`, beside Tom's
  reference 2). Still one soft shaft rather than many sharp ones.
* **Cost** (suite `airrays`, RTX 3080, high, 1920×1080, air-cliff): GPU 10.86 ms off; low 11.18 (+0.3), medium
  11.57 (+0.7), high 12.13 (+1.3), the farther terrain shadows included.
* The pass draws after the clouds in front of the land (they had covered it). Developer: `--ray-dev=airsun,
  airskyglow` added.

## D150 — Air rays sharper and stronger, closer to Tom's references (2026-10-19)
Tom: "go for sharper and stronger shafts, closer to my references, even if less physically exact".
* **Lanes along ridge shadows**: a ridge's real skyline has teeth, trees and notches the 39 m map cannot hold, so the
  shadow edge is moved up and down (±150 m) by two octaves of value noise that stay constant along the sun's direction
  (coordinates across the light, in world metres: the lanes stay put when the scene origin moves). Each ridge casts a
  comb of parallel lanes, ~70 m wide, into the haze near the edge of its shadow. Edge softness 25 → 8 m.
* **Stronger**: a broad glow of sunlit haze toward the sun (pow 3, 0.6), shadowed haze darkened more (shade 0.6 →
  0.85), more light from the side (0.5 → 0.8), gain 1 → 2.2. The sky's glow follows the same lit share.
* Captures at `air-cliff` (sun ~10° behind a cliff over a lake), facing the sun and turned 34° left, off / new
  default / strong (gain 3.5), beside Tom's two references: `artifacts/rays/m2-air-rays-sharper-vs-references.jpg`.
  A fan of light lanes from behind the cliff with dark lanes between; the rock itself no longer glows. Still softer and
  fewer than reference 1's cloud-gap shafts (the cloud shadows get no lanes yet).
* Developer: `--ray-dev=airnoise,airlane,airedge,airglow`. A shader input named `edge` clashed with a local variable
  (SHADER ERROR caught in the log; renamed `shadow_edge`).

## D151 — The sun blinding under the trees (forest rays); Tom's milky mountain dumps are the cloud deck (2026-10-19)
Tom on build 80: "in the forest the sun is so bright, blinding; above the canopy it is normal, I can see its disc".
* Reproduced in `patch32` forest (winter noon, sun ~22°, first person facing it): with *Forest rays* A or A2 (0.85 or
  0.75) the sun and the crowns round it blow out to white; forest rays off and above the canopy (forest rays fade out
  12–30 m above the ground) the sun is normal. Cause: the extra sunlight in the forest air (A ×2.8, A2 ×5–8)
  scatters forward, so straight toward the sun it piles up into a white glare.
* New setting **Forest rays: sun glare** (`fx.forest_sun_glare`): *build 80 (full glare)* or **softened** (default): the
  extra light eases off as the view turns toward the sun (smoothstep over 14–46° from it, to 20 % looking straight at
  it), so shafts beside and across the view keep their strength
  (`artifacts/rays/m2-forest-sun-glare.jpg`). A fix of a reported glare; build 80's look stays selectable.
* His dumps 025558/025615 (milky white, 17–23 m above a slope at 2,200 m) are the cloud deck at that height, not the
  forest rays: identical with A2, A or no forest rays.
* Noted: his settings file now has Distance haze 0 and God ray contrast 0 (set by him after the dumps; tests never save).

## D152 — Rays by the weather: an air load drives every ray effect (draft) (2026-10-19)
Tom: shafts "are not always present in real life, some requirements need to be met first; I don't need them all the
time, only where weather effects are met for them to naturally show up".
* **Air load** (Atmosphere, 0 crisp … 1 heavy): **moisture** = humid place (biome mist, water nearby, forest canopy,
  less in dry climates by the climate's rain) × (a morning haze after sunrise burning off toward midday, a little before
  sunset) + wetness after rain, kept by calm air and cleared by wind; **dust** = dry climate (< ~600 mm/yr) × wind,
  none on wet ground. Eased over ~20 s in play. In debug dumps (`Air load …`) and capture logs (`AIR_STATE`).
* New setting **Rays and haze** (`fx.ray_weather`): *always (build 81)* (default until Tom picks) or *by the weather
  (draft)*: forest air density ×0.1–1.4 and its sunlight ×0.3–1.2, rays-in-open-air haze from the air load (not from
  the Distance haze setting, which keeps fading the distance), the near volumetric fog everywhere ×0.25–3.5 (so rocks
  and single trees cut shafts in loaded air), dust warming the fog's colour.
* Captures, Tom's current settings, each place both modes (`artifacts/rays/m2-rays-by-weather.jpg`): forest at a dry
  windy noon (load 0.08) — *always* hazy with shafts, *by the weather* crisp with a blue sky through the crowns; cliff
  lake at dawn (0.21) and after rain (0.91) — strong shafts past the cliff by the weather; Tom's rock pillar (dump
  034332) calm dry noon 0.01 (crisp either way), windy 0.79 (dust haze), after rain 0.71. The pillar casts no clear
  lane even in loaded air (thin shadow, sun high and off to its side): open.

## D153 — Stronger haze after rain; a Rays strength slider (2026-10-19)
Tom on build 82: "shafts show up after rain but they are very weak; but there are beautiful moments like my last
dump" (100445: alpine ridges at 3,060 m, half-dried ground, wetness 0.42, layered shafts through the valley haze).
* The after-rain moisture was 0.7 × wetness, and a dry climate (284 mm/yr there) adds little else: load 0.31. Now
  0.95 × √wetness (wet ground steams whatever the climate): his dump 0.31 → 0.63, the cliff lake half-dried
  (wetness 0.4) 0.46 → 0.75. More haze, more contrast in the lanes (`artifacts/rays/m2-after-rain-stronger.jpg`).
  Part of the *by the weather* draft (developer: `--ray-dev=airwetk,airwetp`; 0.7 and 1 give build 82).
* New slider **Rays strength** (`fx.ray_strength`, 0.5–2.5, 1 = build 82): the forest rays' extra sunlight and the
  rays in open air; at 2 his dump's shafts become bright, separate lanes. Tom sets it by eye while playing.
* (His running build 82 wrote its own autosave at 10:06:44 during these captures; none of the test runs saved.)

## D154 — Noor's notes: contrast from the environment (draft); capture windows take no input (2026-10-19)
Noor (through Tom): shaded mountain slopes should keep some darkness in haze (not black); stronger separation of the
mountain layers (dark foreground, softening middle, dissolving far); sunlight selective, shafts in a misty valley with
darker neighbours; **no global contrast** — from the relation of scattering, sunlight, ambient and terrain shadows.
* New setting **Light and shadow in the haze** (`fx.atmo_contrast`): *build 82* (default) or *environment contrast
  (draft)*. With it:
  * **Far mountain shadows on the ground**: the terrain shader moved unchanged into `TerrainBody.gdshaderinc`
    (`Terrain.gdshader` includes it: build 82's terrain is the same code); `TerrainAirShadow.gdshader` adds a `light()`
    (Godot's Burley diffuse and GGX specular) whose sun is dimmed where the air map (now also a global shader input,
    `air_map`/`air_frame`, built whenever this is on) says a mountain hides it, beyond ~350–700 m where the shadow map is
    sharper; 70 % of the sun (`--ray-dev=farshadow`), the sky light untouched. Swapped in place on the one terrain material.
  * **The pass carries the near haze itself** (rays in open air must be on): shaped by height (falling to a third over
    1,150 m) and thick close above the ground (valleys ×6), lit by the sky everywhere and by the sun only where it
    reaches, out to ~10 km; Godot's even fog keeps 60 % and the far distance. The sky's light in that haze is Godot's
    fog light mixed toward the sky's horizon colour by the aerial perspective (plain fog light made the shaded far
    ridges a dark violet band).
* Captures (`artifacts/rays/m2-noor-environment-contrast.jpg`): Tom's mountain dump 100445 — deeper, bluer layers that
  separate, a darker foreground, more distinct shafts; cliff lake after rain — the cliff face and shore keep their
  form; low sun across the ranges — a little more ridge separation. The far ground shadows show little in these views
  (heavy haze); their effect is mostly the light falling between ranges.
* Found on the way: Tom played his build 82 while captures ran; a capture window that got focus took his mouse (turned
  the camera) and his R (lit a torch). Screenshot runs now ignore input and their window never takes focus
  (`WindowFlags.NoFocus`). His game also rewrote his settings (dropping keys it does not know): not the tests.

## D155 — Play from a dump's place; "Go to" launchers for the test spots (2026-10-19)
Tom could not find the places of his screenshots again to compare. The game's teleport only knows landmarks and map
clicks, and `--at`/`--view`/`--fly-height` were used only by screenshot and test runs: a play session from a dump's
Reproduce line started at the world's start. Now a play session (no screenshot, test or benchmark arguments) starts once
at `--at` with `--view` and `--fly-height` (log `PLAY_AT`; checked: dump 100445's place, 3,062 m, view and 12:12).
Each build folder gets **"Go to - … .cmd"** launchers beside `JOIN.cmd`: the test spots' world, place, view, time and
weather (`time.*`, `sky.clouds`, `sky.cloud_speed`, `sky.sea_state`, `--wet`, `--hour`), not Tom's graphics settings,
which stay his (and `--set` is never saved): his mountain dump 100445, the same at a low sun, his rock pillar 034332,
and the cliff lake in the test world.

## D156 — The atmosphere's look choices stay player settings (2026-10-19)
Tom, after build 84: "I'll leave some of the options for players so they can set up their game how they like, because
it's hard to decide." Extends D142 (heavy effects stay selectable) to look choices that are a matter of taste:
*Forest rays* (and A2's *forward scattering*, *sun glare*), *Rays in open air*, *Rays and haze* (always / by the
weather), *Light and shadow in the haze*, *Rays strength*. They are not removed when M2 is closed; Tom picks good
defaults, and options still named after builds ("build 82", "always (build 81)") get player-facing names before release.
Pure A/B comparison switches may still go once Tom picks. The final strengths are best set after M3 (clouds), which
changes the light under the clouds and the cloud-gap shafts.

## Planned — Biomes, later phases
Overlays (coast, riverbank, floodplain, cliffs), local terrain features (dunes, needle spires, ravines, boulder
fields), large features before erosion (volcanoes, mesas/canyons, glacial valleys), atmosphere, gameplay hooks,
World Site Editor pages: see `docs/BIOMES_PLAN.md`.
