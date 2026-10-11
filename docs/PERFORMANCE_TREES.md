# The tree problem: analysis of Tom and Noor's performance tests (2026-10-09)

Source: `Thornwake_Performance_ALL_TESTS_for_Sage_2026-10-08.zip` (13 dumps; RTX 3080, Windows 10, 1280×800,
vsync on, Godot 4.5.1, build 0.9.40). **This is analysis only. No code was changed** ("Don't change any code yet").
Every proposal below waits for Tom and Noor's go-ahead.

## Short answer
Yes, it is the trees, and the cause is in how the range setting works. The engine works properly; the cost is
simply more trees. **"Detailed trees ~3.2 km" draws about 4× as many full 3D trees as "~1.6 km"**, because each step
of that setting adds a ring of tiles with four times the area of all the rings inside it. Those trees are full 3D
models with alpha-cut leaves. They are grouped in very large batches that frustum culling can hardly reject, and
nothing hides the trees behind hills. Measured: 33 → 18 FPS (30 ms → 55 ms per frame) in the fixed-camera forest,
38 → 17 in forest A and 60 (vsync cap) → 33 at the lake.

## How the detailed-tree range works (from the code)
* Terrain tiles are nested rings (`WorldStreamer.Desired`). Level 0 tiles are 64 m. Each coarser level doubles the
  tile size and fills a 6×6 block around the hole left for the finer level (at Terrain detail range ×1).
* `TreeLevels = 2 + view.trees`. Full 3D trees (`Vegetation.Trees`) are built for every tile **below** that
  level (`TileBuilder.cs:259`). Beyond it, tiles get far tree cards instead.
* Each tile draws its trees as **one MultiMesh per species × mesh part** (`TreeCatalog.Instance`). Tiles at level
  0–1 use the pack's LOD1 mesh; level 2 and above use LOD2, which still has 200–700 triangles (
  `DECISIONS.md`). There is no visibility range on these nodes. Only level-0 trees cast shadows.

Area covered by 3D trees (Terrain detail ×1):

| Level | Tile size | Reaches out to | Area of this ring | Detailed trees setting |
|---:|---:|---:|---:|---|
| 0 | 64 m | ±192 m | 0.15 km² | always |
| 1 | 128 m | ±384 m | 0.44 km² | always |
| 2 | 256 m | ±768 m | 1.77 km² | ~800 m and up |
| 3 | 512 m | ±1.5 km | 7.1 km² | ~1.6 km and up |
| 4 | 1024 m | ±3.1 km | 28.3 km² | ~3.2 km |

* ~1.6 km: 9.4 km² of 3D trees. **~3.2 km: 37.7 km², four times as much.**
* Tom's forest tests use forest density 1.6 and forest cover 2. That is one tree candidate per 5.5 m cell, with up
  to 87 % kept in full forest, so roughly 15–28 thousand trees per km² of forest. In other words **~150–250 thousand
  3D trees at 1.6 km and ~0.6–1 million at 3.2 km** are loaded, each made of 2–3 mesh parts.
* Only about a fifth of the ring is inside the camera's view (50° vertical FOV at 16:10 is ~72° horizontal). But
  level-4 batches are 1 km tiles, and a tile is drawn whole when any part of it is in view. Most of what lies
  behind hills or behind the near forest is still drawn: no occlusion culling is set up.
* For comparison, a probe of the default settings in a forest (`docs/REVIEW_VEGETATION_RESOURCES_RENDERING.md`)
  counted about 53 thousand tree-part instances in the level-2 ring alone.

The frame time went up by 25 ms when the 3D tree area went up 4×. That is about what an RTX 3080 needs for tens of
millions of extra alpha-tested leaf triangles, including the depth pre-pass. So the most likely cost is GPU vertex
work plus leaf overdraw. CPU draw submission probably isn't the cost: the extra ring adds only about 27 tiles ×
species × parts nodes. This is an estimate from the code. A profiler run (below) should confirm it before anything
is built.

## Answers to Noor's questions (SAGE_HANDOFF)
1. **GPU or CPU?** Probably GPU (see above). To be confirmed with the measurement plan.
2. **Counts, LOD and culling:** MultiMesh per tile × species × part. Two LOD steps (LOD1 near, LOD2 from 256 m
   tiles), then cards. Frustum culling works per batch, so it is coarse on 512 m and 1 km tiles. No occlusion culling and no visibility
   ranges on trees. Grass, flora, rocks and props do have visibility ranges.
3. **`fx.plant_shadows`** controls grass, flowers, reeds and shore plants only. **It does not affect trees.** Trees
   cast shadows only on level-0 tiles (within ~200 m) whenever shadows are on. So the "plant shadows off" test
   (35 FPS) could not show the tree cost, which matches what was seen.
4. **Density or LOD changing with the setting:** density does not change. The same world-aligned trees appear,
   just out to a larger ring. LOD changes only by tile level.
5. **Streaming hitches** are a separate problem. The commit step builds every MultiMesh on the main thread, one
   `SetInstanceTransform` call per instance. That is slow for big tiles: the probe measured up to 420 ms per commit
   at default settings. It explains the hitches while walking and flying, not the steady low FPS.
6. **Terrain detail:** a minor factor next to trees. Test it after the tree fix.

Two more things worth knowing:
* Every test ran **Cloud quality = volumetric v2 (experimental)**. That is a full-screen ray march of up to 128
  steps. The cost is the same in A and B, so it did not cause the difference, but it does eat headroom. Please also
  test "volumetric" (2).
* **Reproduce lines in debug dumps are broken for custom worlds.** They write `--set=gen.gen.size=…` (double
  prefix, `WorldLab.cs:202`), so the world parameters are ignored and a replay generates the default world.
  With the prefix corrected by hand, a replay generates the right 32 km world. This is a one-line fix for when
  code changes are allowed. My forest replays here used an older scratch copy and landed in the wrong world, so
  the instance counts above are calculated from the code, not measured.

## What I propose (in order, each one switchable, nothing removed)
0. **Measure first** (small, test-only code): a `--bench` run that holds a dump's camera, turns vsync off, records
   300 frames (average, p95, p99) and the GPU time per pass from Godot's visual profiler, and prints instance and
   triangle counts per tile level. Tom runs it on the 3080 for the forest B pair. Everything below gets judged
   on those numbers.
1. **Better far trees instead of more 3D trees.** Make the far cards good enough to start at 800 m–1.6 km:
   multi-angle impostors baked from the real tree models (8–16 view angles, with normals, lit like the 3D trees,
   swaying). A forest seen from 1.6 km then looks like the 3D forest, and "3.2 km" stops being needed. This is
   the biggest win that keeps the look.
2. **Smaller batches for far 3D trees:** split level-2+ tiles into 128–256 m chunks, each with a visibility range
   and a cross-fade to the card or impostor. Culling then removes most of the ring, and the hand-off from card to
   tree is a fade, not a pop.
3. **Occlusion culling with terrain occluders** (Godot `OccluderInstance3D` from coarse tile heights). Forests
   behind ridges stop being drawn. This helps most in hilly ground and on the lake overlook.
4. **One mesh per tree** (all parts as surfaces of one mesh) cuts nodes and batches by 2–3×.
5. **Build MultiMesh buffers on the worker thread** (one `Buffer` array per batch instead of per-instance calls).
   This fixes the commit spikes while walking and flying.
6. **A tree budget per frame** as a safety net: past N visible 3D trees, the farthest ones become impostors
   first. The forest stays as dense as now.

Expected result: forest B at "~3.2 km" visual range with the cost of today's "~1.6 km" or less. Checking it costs one
bench run per step.

## Round 30: measured, then built (2026-10-11)
Tom: "identify the actual bottlenecks rather than blindly reducing visual quality … maintaining beautiful, dense
forests is important to me."

### Measured (the benchmark's content counts, Tom's test world, game defaults: detailed trees ~800 m)
| What | Count | Triangles if all drawn |
|---|---:|---:|
| 3D trees, levels 0–1 (LOD1, to ~400 m) | 8,758 | 16.8 M (≈1,900 per tree) |
| 3D trees, level 2 (LOD2, to ~800 m) | 28,609 | 16.8 M (≈590 per tree) |
| Draw calls per frame (forest) | ≈4,000 | |
| Near-tree batches on level 0 | 1,533 batches for 9,012 tree parts (≈6 per batch), all casting shadows | |
| Rocks/bushes/logs batches, levels 0–1 | 1,221 | |

At ~3.2 km the level 3 and 4 rings add ≈115,000 and ≈460,000 more 3D trees (hundreds of millions of triangles),
each 4–8 px tall at that distance. Two bottlenecks: **GPU geometry** (sub-pixel leaf triangles with alpha cut-outs)
and **draw calls** (thousands of tiny batches, several times over for the shadow cascades).

### Built (each switchable; *Tree impostors: off* = build 41 exactly)
1. **Benchmark** (`docs/BENCHMARK.md`): repeatable scenes, frame/GPU/CPU times, hitches, and what each object kind
   and effect costs, measured by turning it off for a few seconds. Every change below gets judged with it.
2. **Batch fills in one call** (`MultiMeshFill.cs`): tile commits no longer make one engine call per instance
   (the 100–420 ms commit spikes). `--legacy-multimesh` brings the old way back for comparison.
3. **Tree impostors** (`TreeImpostors.cs`, `TreeImpostor.gdshader`): every species is baked at start-up from 8 sides
   and from above, colour and normals, 128 px (29 species in 3 batches of a few frames each). A distant tree is
   one lit, swaying quad showing its own model; the two nearest views are blended as the camera moves around it.
4. **Tree field** (`TreeField.cs`): trees no longer ride on the terrain tile rings. 3D trees sit in 128 m cells
   (one batch per species and part: 10–50× bigger batches), detailed LOD1 with shadows near the player and LOD2
   beyond, switched per cell with a dithered fade; impostors sit in 1 km cells. Cells are built on worker threads
   with ready instance buffers.
5. **Cross-faded hand-overs** (`TreeFade.gdshaderinc`): 3D trees → impostors → tree cards on circles around the
   camera with one shared dither, so a pixel shows either the nearer or the farther kind. No square rings, no popping
   when flying. Each tree keeps its place, species, size and rotation whatever draws it.

Settings: *Detailed trees* is how far the 3D models go (400 m × 2ⁿ); *Tree impostors* how far the impostors go
(1.6 / 3.2 / 6.4 / 12.8 km, default 3.2 km); *Distant tree cards* beyond. Suggested: detailed trees ~800 m,
impostors ~3.2 km or more. The forest looks as dense as before at any distance, at the cost of today's ~800 m.

### Still to do
* Tom's numbers from the benchmark on the 3080 (the cloud machine renders in software, so only counts are real here).
* Rocks, bushes and logs: the same cell batching (≈1,200 small batches today).
* Occlusion behind hills (Godot occluders built from the coarse terrain), if the benchmark shows it is worth it.

## Tom's first benchmark (build 42, 2026-10-11)
RTX 3080, Ryzen 9 5950X, 1920×1080 fullscreen, his own settings (detailed trees ~800 m, impostors ~3.2 km, shadows
high, grass density 3 to 400 m, volumetric clouds v2, SSIL). `standard` suite:

| Scene | FPS | 1 % low | GPU ms | CPU render ms | draw calls |
|---|---:|---:|---:|---:|---:|
| forest | 37.9 | 35.6 | 25.8 | 5.9 | 6,254 |
| lake | 35.3 | 32.1 | 27.8 | 4.3 | 4,789 |
| coast | 105.8 | 98.6 | 9.0 | 1.5 | 1,227 |
| mountains | 99.8 | 84.1 | 9.5 | 1.3 | 1,035 |
| flight | 48.2 | 25.6 | 20.1 | 3.9 | 3,447 |

* **GPU-bound** (the CPU needs 4–6 ms of the 26).
* **Far trees solved:** 661 000 impostors cost 1.7 ms, the far 3D trees (LOD2) 2.0 ms, the cards 0.3 ms.
* **What is left:** the near detailed trees (all 3D trees 12.9 ms, of which far ones 2.0 → **near LOD1 trees ≈ 11 ms**)
  and **shadows 12.1 ms** (high: 450 m, 4 cascades, 8192² atlas, soft filter, every near tree casting with its full
  model). Volumetric clouds 2.2–3.2 ms; everything else under 1 ms.
* Build 42's "without X" hid kinds from the camera only; their shadows stayed. Build 43 hides them everywhere and
  measures each kind's shadows, the terrain's shadows, the soft filter and the shadow distance separately. The
  compile counter was wrong (Godot counts in total), so warm-up always ran its 30 s maximum; fixed.

### Build 43 changes (to be measured)
* **Simplified tree shadows** (new default; *Lighting → Tree shadows: detailed* = build 42): near trees cast from the
  lighter LOD2 model of the same trees (≈590 instead of ≈1,900 triangles), shadow only; the camera still sees LOD1.
* **Full-detail trees** distance (*View distance*, default 140 m; build 42: 220 m): how far trees use LOD1.
* `treedetail` suite: forest and lake, full detail 80 / 140 / 220 m × tree shadows simplified / detailed.

## Tom's second benchmark (build 43, 2026-10-11): +48 % in the forest
Same PC and settings. `standard`:

| Scene | build 42 FPS | build 43 FPS | GPU ms (43) |
|---|---:|---:|---:|
| forest | 37.9 | **56.0** | 17.4 |
| lake | 35.3 | **52.2** | 18.7 |
| coast | 105.8 | 110.6 | 8.6 |
| mountains | 99.8 | 100.0 | 9.5 |
| flight | 48.2 | 53.2 (1 % low 26) | 17.8 |
| overall (geometric mean) | 58.4 | **70.3** | |

Forest costs now (GPU): all vegetation 8.1 ms (3D trees 3.9, grass and plants 1.8, impostors 1.6, cards 0.2), all
shadows 3.8 ms (trees 1.7, grass and plants 1.0, terrain 0.4; the soft filter and the distance barely matter),
volumetric clouds 3.3 ms (the biggest single effect; for Atmosphere 2.0). **Simplified tree shadows and 140 m full
detail: tree shadows 12 → 1.7 ms, 3D trees 12.9 → 3.9 ms.**

**Still to fix: stutter while moving** (flight 1 % low 26 FPS). The slow flight frames coincide with terrain tiles
being committed (10–36 ms on the main thread). The benchmark's "script" column was the whole frame time
(Godot's TIME_PROCESS); build 44 measures the world's own per-frame code and splits commits into meshes, collision,
objects, adding to the scene and the tree field, to find which part to spread over frames.

## Tom's benchmarks of builds 44 and 45 (2026-10-11)
**Build 44** (`standard`): forest 55.0, lake 51.1, flight 53.8 FPS with 1 % low **33** (build 43: 26): the heightmap
ground collision removed most streaming hitches, but it broke collision (rocks pushed the player back, steep slopes
became climbable, jitter, no jumping) and was withdrawn in build 45 (D77).

**Build 45** (`standard`, `quick`, `treedetail`, `trees`). The `standard` run had *Tree shadows: detailed* (builds 43
and 44: simplified), which explains forest 38.7 / lake 43.9 FPS; with simplified shadows `treedetail` gives the build
43/44 numbers again (forest 53.2 at 140 m):

| treedetail (FPS) | forest, simplified | forest, detailed | lake, simplified | lake, detailed |
|---|---:|---:|---:|---:|
| full detail 80 m | 29.9 | 26.3 | **52.9** | 48.8 |
| full detail 140 m (default) | **53.2** | 38.4 | 50.6 | 43.0 |
| full detail 220 m | 50.7 | 36.0 | 46.0 | 33.3 |

* **Simplified tree shadows stay the default**: detailed costs 12–15 ms in the forest.
* **140 m stays the default.** 80 m is far slower *inside* a forest (GPU 32.9 ms against 18.2): standing among trees,
  the lighter LOD2 models fill the screen up close, and their alpha-tested leaf cards cost more pixels than the LOD1
  models they replace. From the lake shore (trees farther away) 80 m is slightly faster.
* `trees` (3D tree range 1–4): 36.6 / 29.4 / 17.4 / 7.8 FPS in the forest, draw calls 5.8k → 50k, CPU render time
  6.8 → 94.6 ms: confirms the impostors were the right answer for distance; ranges 3 and 4 are for comparison only.

**Flight stutter in build 45** (1,024-triangle ground pieces, objects at once):

| flight | commit frames > 8.5 ms | > 12 ms | > 20 ms |
|---|---:|---:|---:|
| build 43 | 667 | 99 | 6 |
| build 44 (heightmap, withdrawn) | 72 | 7 | 0 |
| build 45 | 192 | 15 | 1 |

The worst frames were single collision pieces of 35–41 ms (normally ~0.7 ms) and objects up to 24 ms in one frame.
Measured here: the native shape build has a long tail while every core runs a tile builder: the main thread is pushed
off the CPU. Build 46: background builders below normal priority and fewer at once (tiles: cores − 2, at most 12;
tree cells: cores / 4, at most 6; was cores − 1 and cores / 2, i.e. 31 + 16 on Tom's 32 threads), object shapes in
batches of 128 after the ground, and an over-budget frame only forces a step for the player's own tile.

## Tom's benchmark of build 46 (2026-10-11): best so far
`standard`, *Tree shadows: simplified*, full detail 140 m. Overall **70.7 FPS** (build 43: 70.3).

| Scene | build 43 | build 45* | **build 46** |
|---|---:|---:|---:|
| forest | 56.0 | 38.7 | **56.2** |
| lake | 52.2 | 43.9 | **52.6** |
| coast | 110.6 | 97.6 | **110.1** |
| mountains | 100.0 | 92.6 | **99.8** |
| flight (1 % low) | 53.2 (26.0) | 47.8 (25.4) | **54.5 (29.9)** |

\* build 45 ran with detailed tree shadows.

Flight streaming: slowest commit frame 18.5 ms (build 45: 37.9), no frame over 50 ms (build 45: 2), commit frames
over 8.5 ms 135 (45: 192; 43: 667), over 20 ms none. A ground piece now costs at most 10.5 ms in a frame (45: 34.6).
What remains: object shapes up to 16.6 ms in one frame (a batch of 128, rock hulls are the slow ones) and tile mesh
commits up to 13.2 ms; build 44's heightmap (1 % low 33) showed what is left to gain.
