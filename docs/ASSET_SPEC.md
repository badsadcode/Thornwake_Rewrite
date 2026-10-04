# Asset spec for world scatter (trees, rocks, grass, stumps, logs, clutter)

For Tom's incoming asset batches. Follow this and new files drop into the rewrite world with no code changes beyond
one catalogue line each. Matches the existing woodland pack conventions (`docs/VEGETATION_PACK.md`).

## Files
* **Format:** `.glb` (glTF binary), one asset per file. Blender source welcome alongside, but the game loads the GLB.
* **Folder:** `Assets/Scatter/<Category>/` with category one of `Trees`, `Bushes`, `Rocks`, `Grass`, `Stumps`,
  `Logs`, `Clutter` (put new trees in `Assets/Vegetation/` if you prefer to keep the old layout; both work).
* **Name:** `<CATEGORY>_<Name>_<Variant>[_LOD1|_LOD2].glb`, e.g. `ROCK_Granite_A.glb`, `ROCK_Granite_A_LOD1.glb`,
  `TREE_Sequoia_A.glb`, `CLUTTER_Fern_B.glb`. Variants A, B, C... of the same thing share a name.

## Geometry
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

## Materials and textures
* Pixel-art look like the woodland pack: small textures (32–128 px), **nearest filtering** (the game forces it).
* Foliage and grass: alpha-masked (scissor) textures, not soft transparency. Keep foliage in separate material slots
  from bark; bark material names should contain `Bark` (needed for the canopy cut-away near the camera).
* Vertex colours are fine instead of textures for rocks and clutter.
* One to three materials per asset.

## What the game decides (no need to author)
* Where things go: forest density, elevation (broadleaf → mixed → conifer → tree line), slope, rivers, beaches,
  mountains. Tell me rules in words if you want special ones ("ferns only in deep forest", "sequoias rare, in
  valleys, in groups").
* Random rotation and ±20 % scale per instance, distance LODs, far-distance cards for trees.

## Delivery
Commit to any branch, or upload the files to the session; batches are fine. Include a one-line note per asset if it
has a special placement wish. I'll add each to the scatter catalogue and send back screenshots in the world.
