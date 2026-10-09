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
