# Architecture review: vegetation, resources, culling and rendering performance

For Tom and Noor, 2026-10-08. **Investigation only: no code, settings or shaders were changed.** The measurements
below come from a throw-away copy of the project with counters added (not committed). Throughout, **[C]** marks a
confirmed observation from the code or a measurement, and **[H]** marks a hypothesis that still needs testing.

**Short version.** Noor's instinct is right: gameplay must not depend on graphics settings. Today it partly does
(grass and wet plants), while trees, rocks and logs are already world data. I suggest one change of framing: not two
*kinds* of vegetation, but **three layers**: world data (deterministic, plus a small edit overlay), gameplay queries,
and presentation. Most of what we need already exists. On performance, the numbers point away from culling. On Tom's
machine the game is almost certainly limited by per-pixel effects, not by geometry. So the first job is **measuring**,
then fixing the two clear CPU problems (tile-commit hitches and thousands of tiny MultiMeshes). Occlusion culling and
camera-direction streaming should wait, or not happen at all.

---

## 1. Current implementation

### 1.1 What decides where plants are

| Thing | Placement depends on | Graphics settings change it? | Where |
|---|---|---|---|
| **Trees** | World-aligned lattice (cell ≈ 7 m / √forest density), world seed, biome, slope, rivers | **No** [C]. Detail settings only choose mesh LOD or far card | `Vegetation.Trees` (Vegetation.cs:90), cell from `DetailConfig.ForestDensity` = world style |
| **Rocks, boulders, logs, stumps, bushes** | World-aligned lattices per kind, world seed, world-style amounts | **No** [C] (amounts come from the world type: `style.RockAmount`, `style.PropAmount`) | `GroundCover.Rocks/Props`, `DetailConfig.For` (TileBuilder.cs:27) |
| **Grass and land plants** ("plant populations") | Lattice whose **cell size depends on the Grass density setting** (`1.25/√(2·amount)·0.78`) | **Yes** [C]. At another density every candidate position is different, not a subset. At density 0 nothing exists | `GroundCover.LandFlora` (GroundCover.Flora.cs:170), `config.Grass = GameSettings.GrassDensity` |
| **Reeds, floating and underwater plants** | Same, with `amount = min(1.5, Grass density)` | **Yes** [C] | `GroundCover.WaterFlora`, `WetPlants` |
| **Far tree cards** | A **separate lattice** with classic elevation-based species lists | n/a. They are visual, but **do not match the real trees** [C] | `GroundCover.FarTrees` (GroundCover.cs:472) |
| **Grass cover field** (continuous 0..1, three noise scales) | World seed only | No [C]. It already tints the terrain (uv2 → `flora_tint`), so distant meadows read as grass | `GroundCover.FloraCover` (TileBuilder.cs:179) |

Other facts that matter later:
- **Only tiles at level 0 (64 m) get grass and plants** [C] (TileBuilder.cs:266). Level 0 covers about ±130–260 m
  around the player, depending on *Terrain detail range*. So a *Grass distance* above about 200 m does nothing at
  the default range [C]: the setting and the ring size are silently coupled.
- **Collision exists only at level 0** [C] (TileBuilder.cs:257, `ObjectCollision.Add`). It covers trees, rocks and
  props within the same ring. Fine for one player. A server cannot rely on client tiles.
- **Plant generation already respects terrain edits** [C]: `g.Edited(x, z)` skips flora on dug ground. The edit
  overlay (`TerrainEdits`) is exactly the pattern a resource overlay needs. It isn't saved to disk yet.
- **`GroundCover` lives in `Game/Rewrite`** and works on the per-tile `Grid` (built from the tile's mesh samples) with
  Godot types (`Transform3D`, `Color`) [C]. The terrain itself (`TerrainSampler`, `WorldGen/`) is pure C# and runs
  headless in the tests.

### 1.2 Culling and LOD: what we do and what Godot does

| Technique | Who | Notes |
|---|---|---|
| Frustum culling per instance (each MeshInstance3D and MultiMeshInstance3D, using its AABB) | **Godot, automatic** [C] | A MultiMesh is culled as one unit (the union of its instances) |
| Shadow-caster culling per cascade | Godot [C] | Directional shadows to 90/200/450 m (*Shadows* setting, Atmosphere.cs:300) |
| Depth prepass (hidden opaque pixels are not shaded) | Godot Forward+ [C] | This is why "occluded" forests cost vertices and depth, not full shading |
| Occlusion culling (`OccluderInstance3D`) | Godot provides it, **we don't use it** [C] | Disabled in project settings, no occluders. Noor's finding still holds |
| Mesh LOD | Godot can do it for imported meshes; **we use our own levels** (tree LOD by ring, far cards) [C] | |
| Nested terrain rings (64 m … 8 km tiles), streaming, unloading | **Ours** (WorldStreamer.cs:219) [C] | Omnidirectional; build order is finest level first, then nearest |
| Visibility ranges with self-fade | **Ours** [C] | Grass/plants = *Grass distance*; reeds ×1.3; water plants ≥ 70–100 m; stones 90 m; rocks 400 m; props 260 m (all × *Rocks, bushes and logs*); trees: **no range** (the rings decide) |
| Range measured from the instance centre, not the tile corner | **Ours** [C] | The underwater fix; also water plants split into 32 m quarters |
| Batching by MultiMesh per tile × kind (× tree species × mesh part) | **Ours** [C] | Granularity is the tile: 64 m at level 0, 128/256 m beyond |
| Far horizon land (one mesh to 100/250 km) | **Ours** [C] | Cheap; rebuilt every ~8 km |
| Custom AABBs | Far-tree card mesh ±40 m (WorldStreamer.cs:96, reasonable); full-screen effect quads with huge margins (correct for screen quads); water and grass: **none** [C] | |

### 1.3 Measurements (llvmpipe software renderer, so counts are valid but timings are not)

Forest location from an earlier dump, flying 45 m up, **default settings** [C]:

- 567 draw calls, 2,870 objects, 1.0 M primitives in view. 787 k terrain triangles resident in 214 tiles.
- About **2,900 MultiMeshInstance nodes**:
  - trees (unranged): 349 nodes at level 0, 743 at level 1, 751 at level 2 (53 k tree instances drawn as full meshes out to level 2)
  - props: 326 nodes with **949 instances** at level 0 (≈3 per node) and 542 nodes with 3,364 at level 1
  - grass/plants: 36 nodes holding 102,670 instances (one per level-0 tile), plus 5.5 k reeds
  - far cards: 79 nodes with 260 k cards
- VRAM 319 MB (textures 143, buffers 150).
- `MaxCommitMs` (worst single tile commit on the main thread): **420 ms**.

Tom's settings and storm location (dump fa9d900c) [C]:
- 193 draw calls, 536 k primitives, VRAM 237 MB.
- On his Intel UHD 770 at 0.5 render scale with shadows off, Tom got **23 FPS**.
- The worst tile commit was again 478 ms. That number includes first-use shader compilation, so take it as an upper
  bound: real stutters are smaller, but worth measuring.

---

## 2. Potential problems

1. **Graphics settings change gameplay-relevant vegetation** [C]. Grass and wet plants are generated *by* the
   density setting. Tom plays with *Grass density 0*, so in his world no grass exists at all. Fine while it's decoration,
   wrong the day grass yields fibre.
2. **Density changes the lattice, not just how many plants are kept** [C]. Two players at different densities see
   different plants in different places, never a sparser version of the same field. Harmless for decoration, but it
   rules out using the same placements for gameplay, and it means harvesting can't be shown consistently.
3. **Resource placement is tied to render tiles** [H→C for the dependency]. Plants are generated from level-0 tile
   grids. A server, or a far-away query ("is there wheat 500 m from here?"), would need tiles nobody is drawing.
4. **Far tree cards don't match the trees** [C]. Felled trees, or a cleared village site, would still show as forest
   cards from a distance. The species in cards also ignore the biomes [C].
5. **Main-thread commit hitches** [C in code, size H]. `Multi()` / `MultiFlora()` set instances one by one through
   the engine API, plus LINQ grouping, on the main thread. A level-0 tile carries about 3 k plant instances, and the
   default ring has 36 such tiles. This is the most likely cause of hitches when walking into new ground.
6. **Thousands of tiny MultiMeshes** [C]. Props average about 3 instances per node, and trees split into
   species × mesh part per tile. Each node costs CPU every frame (culling, scene bookkeeping) and a draw call when
   visible. Draw calls are moderate (567), but the node count is high. [H] This matters more on weak CPUs.
7. **Grass distance silently capped by the level-0 ring** [C]. Above about 200 m at the default *Terrain detail
   range*, the setting does nothing. Grass also appears and disappears in 64 m tile steps as the ring moves (masked
   only while the range is shorter than the ring).
8. **Water tiles have no cull margin while the vertex shader lifts waves** [H]. A storm crest at the very edge of the
   screen could pop. Low risk, cheap to check.
9. **The likely real bottleneck on weak GPUs is per-pixel work** [H, strongly suggested]. 193 draw calls and 536 k
   triangles are light, yet the UHD 770 manages 23 FPS at half resolution. That points to the full-screen and
   per-pixel passes:
   - volumetric clouds (up to 150 steps per pixel)
   - experimental 4 water with SSR
   - volumetric fog, SSAO and SSIL
   - sun shafts at 3 (32–80 depth and colour reads per pixel)
   - rain curtains and other full-screen quads

   Culling would not touch any of these.

---

## 3. Recommendations

### 3.1 Vegetation: three layers instead of two kinds

Not "decorative vs gameplay vegetation" as two systems, but every plant passing through three layers:

1. **World data**: pure, deterministic functions of (seed, position, world settings) plus a sparse **edit overlay**.
   No Godot types, no tiles, no graphics settings. It lives next to `TerrainSampler` (WorldGen) so a server and the
   tests can use it.
2. **Gameplay queries**: what can be harvested here, how much, in what state. They read layer 1 and the overlay.
3. **Presentation**: what the client draws. It reads layers 1 and 2, and may be as dense or sparse as the settings
   allow, but never invents or removes resources.

Inside that, plants fall into two **data shapes**, not two renderers:

- **Fields** (ordinary grass, hay, reeds in a reed bed, seaweed meadows): resource = a continuous density at a point.
  **We already have it**: `GroundCover.FloraCover` is a deterministic 0..1 grass field used today for the terrain tint.
  Combine it with the habitat rules `LandFlora` applies (slope, water, forest, alpine, biome plant amount). Harvesting
  writes to the overlay; grass appearance and the terrain tint read the overlay back.
- **Things** (herbs, berry bushes, mushrooms, special flowers, single trees, rocks): resource = an individual with an
  identity. **We already have the pattern**: trees use a world-aligned lattice, so id = (kind, cell x, cell z) and
  the same tree comes out whatever tile or LOD asks. Herbs etc. should use the same, with a **fixed cell size per
  species** (rarity from world settings), never scaled by graphics.

### 3.2 Ordinary grass (Part 2)

- **Area harvesting: yes.** A sickle cut affects a disc. The yield is the field value × the cut area × (1 − already cut).
- **Overlay:** a sparse grid of small cells (2 m is plenty) storing *when* each was cut. Regrowth is computed
  lazily when someone looks: amount = min(1, time since cut / regrow time). No simulation, no ticking, and
  fully-regrown cells can be dropped from storage. The same overlay holds "trampled", "burnt", "built over", "ploughed".
- **Presentation:** tufts in cut cells are drawn shorter or left out, and the terrain tint becomes stubble. Because
  the decorative grass reads the same overlay, a meadow you mowed looks mowed on every client.
- **Terrain edits, building, farming:** they already exclude plants via `Edited()`. Buildings and fields simply write
  "occupied/ploughed" into the overlay, and crops are player-made *things*, not procedural.
- **Server:** reconstructs the unmodified field from the seed and persists only the overlay. Exactly what Noor proposed.
- **Simpler alternative worth considering:** don't make grass harvestable from the world at all. Fibre comes from
  specific plants (*things*: flax, nettles, reeds), and grass stays decoration. It avoids the whole field/overlay
  machinery for one resource. That's a design question (§6).

### 3.3 Distinctive plants

- Lattice per species, cell size from rarity (world data). Habitat rules from biome and terrain, the same shared rules
  that drive the populations, so herbs grow where the look suggests (wet meadow herbs in wet meadows).
- **State:** overlay map id → harvested time, with lazy regrowth.
- **Presentation:** a **guaranteed interaction radius** (for example 60–80 m) independent of decorative settings, a
  readable model, and LOD beyond that. Inside the radius they are always drawn. That's Noor's "minimum guaranteed
  visibility distance", and I agree with it.
- **Fairness on low settings:** with grass off, herbs are easier to spot. It can't be made perfectly equal. Options:
  - keep a *minimum* grass density near the player regardless of settings, which also fixes "grass is a gameplay
    hiding place"
  - give herbs a recognisable silhouette so finding them is about knowing where to look, not about grass hiding them

  I'd do both, modestly.

### 3.4 Keep the populations (Part 3)

The organic population rules (macro/patch/micro fields, communities, habitats) are world-space and deterministic
already. Only the *candidate lattice* depends on the setting. Two small changes would make decoration and resources
share them:
- **Decoration:** a fixed fine lattice, and the density setting thins it by a keep-threshold (each candidate has its
  own random value, kept if below the density). Lower settings then show a true subset of the same plants. That also
  makes LOD fade and harvest overlays line up.
- **Resources:** call the same community and habitat functions from the WorldGen layer, without the tile grid.

### 3.5 Rendering and performance: the order I would actually do things

1. **Measure first** (cheap, decisive):
   - a performance overlay with GPU frame ms (`ViewportSetMeasureRenderTime`), draw calls, primitives, objects, and the
     worst commit ms over the last 10 s
   - an F9 dump that includes them
   - a **benchmark flight**: a fixed path that records average and 1 % low FPS, run once per effect switched off.
     It tells us in one evening whether clouds, water, fog, SSAO/SSIL or shafts cost Tom his frames.
2. **Commit on worker threads:** build MultiMesh buffers (one float array) on the worker and hand Godot one buffer
   instead of thousands of `SetInstanceTransform` calls. Do the grouping on the worker too. Verified with the
   worst-commit number.
3. **Fewer tiny MultiMeshes:** merge props of the same mesh across a 2×2 or 4×4 block of tiles at levels ≥ 1. Trees
   at level 2 could use one MultiMesh per species per block. Aim for a few hundred nodes instead of ~2,900. Verified
   with object count and CPU frame time.
4. **Far cards from the real tree lattice** (or from a density map that honours edits): no species mismatch, no
   forest where trees were felled. Needed before tree chopping matters at a distance.
5. **Decouple Grass distance from the level-0 ring:** grass generation in its own ring sized by the setting, or
   clamp and explain the setting.
6. **Quality presets aimed at iGPUs** once measurements show which effects cost most.

---

## 4. Alternatives and trade-offs

- **Occlusion culling** (Godot `OccluderInstance3D`, can be fed runtime `ArrayOccluder3D` meshes such as a coarse
  terrain heightfield per tile):
  - Saves vertex work and draw calls for forests behind mountains.
  - Costs CPU rasterisation every frame, plus occluder updates as tiles stream.
  - Thanks to the depth prepass, hidden geometry is already cheap to shade.
  - **Not now.** Worth one experiment only if measurements show we are vertex- or draw-call-bound in mountains. Test:
    count primitives in a valley facing a ridge, add coarse terrain occluders for levels ≥ 1, compare.
- **Camera-direction streaming:** excluding what's behind the camera would make the landscape assemble itself when
  you turn, and break with flying, teleporting and mounts. Not worth it. A free middle ground is **build-order
  priority**: within a level, build tiles in front of the camera first. Everything still gets built, so nothing pops,
  but you see the important tiles sooner after a teleport.
- **Smaller MultiMesh batches for culling:** grass at 64 m tiles is already a good size. Making batches smaller
  costs more draw calls than culling saves. Our problem is the opposite: too *many* tiny batches for props and trees.
- **GPU-instanced grass** (a compute shader scattering grass each frame from the cover field, the "Ghost of
  Tsushima" style): beautiful and cheap per blade, and gameplay-neutral by nature. But it's a big rewrite of something
  that works. Only if grass becomes the measured bottleneck.
- **Two separate vegetation systems** (Noor's literal proposal): would work, but duplicates the population rules.
  The three-layer split gets the same guarantees with one set of rules.

---

## 5. Priority assessment

**Now (before the survival and gathering prototype):**
- Measurement tools: overlay, dump fields, benchmark flight.
- Decide the resource model with Tom and Noor (§6).
- Agree the rule "resources = WorldGen layer + overlay; presentation reads, never decides", and a test that generates
  resources under different graphics settings and asserts they're identical.

**With the first gathering mechanics:**
- The overlay (cut/harvested times, lazy regrowth).
- Herbs and bushes as lattice *things* with a guaranteed interaction radius.
- Grass harvesting, if chosen.
- Decoration as a subset lattice.

**Soon, independent of gameplay:**
- Worker-thread MultiMesh buffers (commit hitches).
- Merging tiny MultiMeshes.
- Grass distance decoupling.
- A water cull margin check.

**Later:**
- Far cards from the real tree lattice (before chopping matters at distance).
- Server-side resource queries and persistence of the overlay (with multiplayer).
- iGPU presets after measurements.

**Probably never (unless measurements say otherwise):**
- Occlusion culling, camera-direction streaming, per-blade grass objects, a botanical simulation.

---

## 6. Questions for Tom and Noor

1. **Is ordinary grass a resource at all**, or does fibre/thatch come from specific plants (reeds, flax, nettles,
   wild wheat)? This decides whether we need the field + overlay for grass.
2. **How hard should finding herbs be?** Do we want "knowing where to look" (habitat-driven) or "searching closely"
   (visual hunting)? The second makes graphics fairness much harder.
3. **Is a minimum grass density near the player acceptable**, even at *Grass density 0*? For example, short grass
   within 10–15 m.
4. **Regrowth:** rough times (grass hours, herbs days, bushes a season?) and whether seasons matter.
5. **Trees:** do felled trees leave stumps, and do they regrow? Does a large clearing need to show from afar?
6. **Multiplayer visibility of harvesting:** should everyone see a mown meadow? I assume yes, and the overlay gives it
   for free.
7. **Minimum hardware:** is Tom's Intel UHD 770 the target low end, and at what FPS (30? 60?) and resolution? This
   decides how aggressive the iGPU preset must be.
8. **Farming:** I assume crops are player-made objects, not procedural. Correct?

*Questions 1, 2 and 7 matter most; the rest can wait for the gathering prototype.*
