# Asset spec — what Thornwake takes in, and from NoorMD's benches

What the game needs from art files, so new assets drop in with one catalogue line each. Part 1 is NoorMD (Tom's own
tool, `badsadcode/NoorMD`: Texture Bench, Asset Bench) — written 2026-10-28 (D237) after reading its plans. Part 2 is
the original scatter spec (still valid for any hand-made asset). Thornwake reads **exported files and a manifest
only**: no code moves between the two projects, and nothing in NoorMD's repository is changed from here.

## Part 1 — NoorMD

### Ground rules

* **The recipe travels with the files**: NoorMD's `.noortex.json` / tree and rock recipes are committed next to the
  exports, so an asset can be remade at another size or changed later. The game never reads them.
* **A manifest per delivery** (NoorMD already writes one): the game reads names, files, levels and sizes from it,
  so files keep NoorMD's own names (no hand renaming).
* **Looks stay switchable**: a new texture set or tree set arrives beside the current one and is chosen in Settings
  (like *Ground textures: current / Noor*); the old one stays as a return point.
* **The game decides placement** (biomes, slopes, rivers, density, rotation, ±20 % scale): assets carry no placement.
* **Godot project addons**: NoorMD writes its tree import plugin into a Godot project it finds above the export
  folder. Thornwake streams trees itself, so export with engine *Other* (or to a folder outside the project) and
  copy the files in — the plugin must not land in Thornwake's `addons/`.

### 1. Terrain materials (Texture Bench) — the PBR ground

For the PBR terrain (planned after D236: the wet sheen needs real height and roughness per ground). A third choice
in *Ground textures* (current procedural / Noor pixel art / PBR), the others kept.

* **Layers**, the shader's order (the Noor set's names; one material each, variants welcome as `_B`, `_C`):
  `grass, dirt, sand, rock, river_bed, sea_bed, forest_floor, gravel, mud, wet_mud, moss, scree, snow, swamp_peat,
  ice, volcanic_rock`. **First eight to start**: grass, dirt, rock, scree, sand, forest_floor, gravel, mud.
* **Maps per layer**: albedo (sRGB, no baked light or strong shadows), normal (OpenGL / Godot convention, Y up),
  **ORM** packed (R ambient occlusion, G roughness, B metallic = 0), **height 16-bit** (low = where water collects:
  the game fills a ground's hollows first when it rains, and blends grounds by height so rock pokes through soil).
  NoorMD's *Godot* naming and packing preset.
* **Size and scale**: 1024 × 1024 exports (2048 kept as masters); one tile covers **2 m** of ground (state
  `tile_metres` in the manifest if another size suits a layer better); the height map's full range in centimetres
  (`height_cm`, about 2–8 cm for soil and grass, up to 20 cm for scree and rock).
* **Tiling**: seamless (the bench's wrapped lattice), no single feature that repeats visibly every tile, little
  large-scale brightness change (the game adds its own variation over hundreds of metres from biome colours).
* **Brightness**: albedo means in a natural range (grass and soil 0.15–0.35 linear, sand and snow brighter); the
  game reads each layer's mean so textures add detail without shifting the biome's colour.
* **Rock looks** for cliffs and boulders (the shared `rock_tex` array): `layered, granite, basalt, sandstone, mossy,
  icy`, same maps; cliffs are textured from three sides (triplanar), so no direction may look stretched.
* **Delivery**: `Assets/Terrain/<SetName>/` with the PNGs, recipes and `ground_set.json` (layer → files, tile_metres,
  height_cm).

### 2. Trees (Asset Bench)

* **Levels**: three per tree — LOD0, LOD1 (~half), LOD2 (~a quarter; the game draws it without shadows). The game
  bakes its own far impostors (9 views per species) and fades to them, so NoorMD's billboard level is not needed
  (export it if handy; it is ignored).
* **Files**: one GLB per level (NoorMD's *file per level*), Y up, metres, pivot at the base of the trunk. Today's
  catalogue names are `TREE_<Species>.glb`, `TREE_<Species>_LOD1.glb`, `_LOD2.glb` in `Assets/Vegetation/`; with the
  manifest the game will map NoorMD's `Name_LOD0.glb…` itself (a small loader on Thornwake's side).
* **Materials**: bark material names contain `Bark` (the game swaps bark to its trunk shader and needs it for the
  canopy cut-away near the camera); foliage is alpha-cut (scissor), not blended; one to three materials. The game
  uses the colour texture and tint; normal and ORM maps are used once the tree shaders take PBR (part of the same
  look decision as the ground).
* **Wind**: the game has its own canopy wind today. Keep NoorMD's wind data in TEXCOORD_1 (its default) — the game
  can switch to it later without re-exporting.
* **Budgets (LOD0)**: tree ≤ 5,000 triangles, giant ≤ 12,000 (the original spec); leaf cards count.
* **Most wanted** (species the game draws as placeholders today, `Game/Rewrite/PlaceholderPlants.cs`): palm,
  cactus, dead tree, acacia, jungle tree, cypress, dry shrub, bald cypress (swamps). NoorMD's swamp, tropical, dry
  and fantasy presets cover most of them.
* **Look**: the current woodland pack is pixel art (32–64 px textures, nearest filtering). NoorMD's realistic and
  stylized looks are a new set beside it — Tom's call which look the game goes with, per species or as a whole.

### 3. Rocks (Asset Bench, its stage 2)

What the world needs, in order:

1. **Scatter stones and boulders** (today procedural icospheres, `RockMeshes.cs`): 3–5 variants each of small
   stones (0.2–0.5 m), rocks (0.5–1.5 m) and boulders (1.5–4 m).
2. **Outcrops** for cliff edges and rocky ground (they now stand on escarpment rims, D235), 2–6 m, flattened
   underside.
3. **Scree packs** (landform no. 8): many small stones as one mesh per 2–4 m patch, low poly.
4. **Landform models** (`docs/LANDFORMS.md`): sea stacks and arches (no. 9, 10–60 m), hoodoos and tors (no. 16,
   5–25 m), later overhangs and cave mouths.

* **Pivot** at the centre of the ground contact, Y up, metres; the contact may reach 10–30 % below the pivot (the
  game sinks them).
* **Levels**: LOD0 plus LOD1 and LOD2 for anything over ~1 m; budgets LOD0: stone ≤ 150, rock ≤ 400,
  boulder ≤ 1,200, outcrop ≤ 2,500, landform model ≤ 8,000 triangles.
* **Collision**: a `COL_` mesh (convex, ≤ 64 triangles; several convex pieces for arches) in the same GLB for
  anything over ~1 m — the player climbs and walks on these.
* **Materials** from the six rock looks above, so cliffs and rocks match; moss or snow on upward faces welcome
  (the game also tints by biome).

### 4. Other scatter

Bushes, stumps, logs, grass tufts and clutter: Part 2 below applies unchanged (budgets, pivots, alpha-cut).

### What to send first (suggested)

1. The first eight ground layers at 1024 px, one variant each — enough to build the PBR ground and judge the look.
2. Two or three placeholder species as trees (palm, dead tree, bald cypress).
3. Boulders and outcrops when the rock bench exists.

## Part 2 — scatter models (original spec, a3d139f)

For Tom's incoming asset batches. Follow this and new files drop into the rewrite world with no code changes beyond
one catalogue line each. Matches the existing woodland pack conventions (`docs/VEGETATION_PACK.md`).

### Files
* **Format:** `.glb` (glTF binary), one asset per file. Blender source welcome alongside, but the game loads the GLB.
* **Folder:** `Assets/Scatter/<Category>/` with category one of `Trees`, `Bushes`, `Rocks`, `Grass`, `Stumps`,
  `Logs`, `Clutter` (put new trees in `Assets/Vegetation/` if you prefer to keep the old layout; both work).
* **Name:** `<CATEGORY>_<Name>_<Variant>[_LOD1|_LOD2].glb`, e.g. `ROCK_Granite_A.glb`, `ROCK_Granite_A_LOD1.glb`,
  `TREE_Sequoia_A.glb`, `CLUTTER_Fern_B.glb`. Variants A, B, C... of the same thing share a name.

### Geometry
* **Scale:** metres, real size (a 1.8 m person for reference). Giant fantasy trees (sequoia) are fine: just model
  them at their real size.
* **Origin (pivot):** at ground level, centred under the object (tree: base of the trunk; rock: centre of the
  part that touches the ground; log: centre of its length). Y up, identity transform on export, no parent offsets.
* **Facing:** any; the game rotates instances randomly around Y.
* **Ground contact:** rocks and logs may extend 10–30 % below the origin; the game sinks them slightly anyway.
* **LODs:** LOD0 always. For anything bigger than ~1 m, add LOD1 (≈ 40–60 % of LOD0 triangles) and LOD2
  (≈ 10–20 %). Small clutter and grass: LOD0 only, keep it light (grass tuft ≤ 60 triangles, small clutter ≤ 300).
* **Triangle budgets (LOD0):** tree ≤ 5,000 (giant tree ≤ 12,000), bush ≤ 1,500, boulder ≤ 1,200, rock ≤ 400,
  stump/log ≤ 800.

### Materials and textures
* Pixel-art look like the woodland pack: small textures (32–128 px), **nearest filtering** (the game forces it).
  (2026-10-28: one look of several — NoorMD's realistic and stylized sets come beside it, Part 1.)
* Foliage and grass: alpha-masked (scissor) textures, not soft transparency. Keep foliage in separate material slots
  from bark; bark material names should contain `Bark` (needed for the canopy cut-away near the camera).
* Vertex colours are fine instead of textures for rocks and clutter.
* One to three materials per asset.

### What the game decides (no need to author)
* Where things go: forest density, elevation (broadleaf → mixed → conifer → tree line), slope, rivers, beaches,
  mountains. Tell me rules in words if you want special ones ("ferns only in deep forest", "sequoias rare, in
  valleys, in groups").
* Random rotation and ±20 % scale per instance, distance LODs, far-distance cards for trees.

### Delivery
Commit to any branch, or upload the files to the session; batches are fine. Include a one-line note per asset if it
has a special placement wish. I'll add each to the scatter catalogue and send back screenshots in the world.

## Open for Tom

* **The look**: realistic, stylized or pixel art — for the ground, for trees, for rocks (they should agree).
* **Texel density**: 1024 px per 2 m of ground is sharp up close; 512 px per 2 m is cheaper. Decided with the
  first set.
