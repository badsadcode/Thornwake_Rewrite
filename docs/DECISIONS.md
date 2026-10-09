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

## Planned — Biomes, later phases
Overlays (coast, riverbank, floodplain, cliffs), local terrain features (dunes, needle spires, ravines, boulder
fields), large features before erosion (volcanoes, mesas/canyons, glacial valleys), atmosphere, gameplay hooks,
World Site Editor pages: see `docs/BIOMES_PLAN.md`.
