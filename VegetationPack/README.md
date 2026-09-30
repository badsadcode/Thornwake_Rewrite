# Thornwake Woodland — vegetation asset pack v1

## Start here

- **Blender source:** `Blender/Thornwake_Woodland.blend` — open with **Blender 5.1 or later**. Older Blender versions can reject this file format. All small texture maps are packed into the file.
- **Species comparison:** `Previews/Species_Contact_Sheet.png`.
- **All variants:** `Previews/All_17_Trees.png`.
- **Game assets:** `Exports/` contains 51 self-contained GLBs: 17 trees at three levels of detail.
- **Interactive Godot review:** open this folder's `project.godot` with Godot 4.5+. Press 1 / 2 / 3 to change LOD, right-drag to orbit, and scroll to zoom. Escape closes the viewer.

These are new 3D assets and original image-generated texture maps inspired by the supplied tree references. The previews are renders of the delivered meshes. The references themselves are not included as game textures.

## Collection

| Species | Variants | Character |
|---|---|---|
| Pine | A, B, C | Irregular branch tiers, exposed lower trunks, open upper crowns |
| Spruce / fir | A, B | Denser low crowns and dark natural green needles |
| Green birch | A, B, C | Slender pale trunks, small rising branches, loose leaf sprays |
| Autumn birch | A, B | Yellow/ochre foliage and pale bark |
| Oak | A, B, C | Broad crowns, heavier trunks and substantial major branches |
| Autumn maple | A | Copper/orange foliage and a broad branching crown |
| Sparse woodland | A | Crooked trunk, open branches and grey-green leaf groups |
| Poplar | A | Tall, narrow upright crown |
| Weeping willow | A | Broad framework with irregular hanging foliage curtains |

Variants change their underlying branch layout, seed, height, crown dimensions and trunk bend. B/C variants also have subtle material color factors. They are not rescaled copies of one mesh.

## Blender organization

The opening scene, **00_Woodland_Contact_Sheet**, contains arranged collection instances, neutral daylight, a ground plane, labels and a camera. These are review objects and are excluded from the asset exports.

Use the Scene selector to open **TREE_<name>_LOD0** for an individual tree at world origin. LOD1 and LOD2 have their own scenes. Each asset scene contains one named root and two or three identity-transform mesh objects: bark, foliage core/fronds, and clipped foliage cards. Meshes are directly editable. The shared materials and packed images are available in the usual Blender editors.

**1 Blender unit = 1 metre.** Blender uses Z-up; the GLBs convert correctly to Godot's Y-up. Origins are at ground level, transforms are applied, and the meshes are grounded. Full measured dimensions and triangle counts are in `manifest.json` and `asset_dimensions.csv`.

Save working copies outside this generated folder before editing. Rebuilding the pack intentionally replaces the generated models and previews.

## Geometry and LODs

- Trunks and branches use 4–7-sided tapered sections, without subdivisions or individual leaf meshes.
- Main crowns use three-dimensional arrangements of clipped leaf sprays around small faceted interior masses. Conifers have folded branch fronds; willow has hanging cards supported by its crown structure. These are not camera-facing whole-tree billboards.
- Branch origins are constructed from trunk/parent-branch points; leaf groups are located at branch tips.
- LOD0 is the close/gameplay asset. LOD1 reduces twig/card complexity. LOD2 uses simpler branch geometry and faceted crown masses, with hanging sections retained for willow.
- Each LOD is a separate GLB; the files do **not** automatically switch themselves. Connect the three files to the engine's distance/visibility system. Start with LOD0 near the player, LOD1 at medium range and LOD2 in the background, then tune for the actual camera.

Triangle budgets across the collection: **LOD0: 1,246–4,754**, **LOD1: 944–2,678**, **LOD2: 202–934**.

## Textures and materials

`Textures/` contains 20 production PNGs:

- Four **64×64 bark** maps: pine, birch, oak and willow. Related species reuse bark maps.
- Eight **64×64 foliage** maps.
- Eight **32×32 branch-card** maps with binary alpha. Smaller cards keep their pixels comparable to the larger crown surfaces.

The original atlases and spruce correction were made with the built-in image generation tool. Those sources and the exact prompts are retained under `Textures/Source/`. Preparation uses nearest-neighbor sampling, small deliberate palettes, and hard alpha thresholds. No normal maps, soft opacity gradients or photographic bark are used.

All Blender image nodes use **Closest**. Foliage-card alpha uses a simple rounding node, which exports as **glTF MASK** with a 0.5 cutoff. Cards are double-sided. Materials otherwise use Base Color, high roughness and low specular response; B/C variants add a simple color multiplier.

The GLB sampler uses nearest magnification and nearest mip sampling. The supplied Godot viewer explicitly selects **Nearest** to avoid project defaults introducing filtering. When importing into another project, verify its material filter remains Nearest and its card materials remain alpha-scissor/mask, not soft blending. Nearest sampling intentionally exposes pixels; LOD selection is still necessary to control distant aliasing.

## Reviewing the result

- The species and complete contact sheets use fitted individual frames; height is printed under each tree.
- `Woodland_Preview_Scene.png` shows the common-scale Blender layout.
- `Birch_Close_Neutral.png`, `Pine_Close_Neutral.png`, `Oak_Close_Neutral.png` and `Willow_Close_Neutral.png` show the actual surface detail.
- `Oak_Gameplay_Neutral.png` uses a raised gameplay camera.
- `Godot_Woodland.png` and `Godot_Close.png` show engine imports; the close scene includes a **1.8 m** scale marker.
- `LOD_Comparison.png` compares representative silhouettes at all three levels.

## Validation and integration

`Reports/asset_validation.json` records native Blender scene and GLB checks: identity transforms, metre scale, ground contact, real UV maps, no unused vertices, no degenerate triangles or collapsed UV faces, packed small images, Closest interpolation, nearest GLB samplers and masked cards. `Reports/godot_validation.json` records measured import bounds and materials for all 51 exports. The original scene, close views and gameplay view were visually inspected during creation.

This delivery is an asset pack and review project. It does not replace the live game's existing forest yet. Collision, resource IDs, harvesting, growth and tree spawning remain gameplay systems; use simple trunk colliders and keep those systems separate from the art. Wind animation, felling animations, seasons, collision meshes and billboard impostors are not included.

## Regeneration

The pack includes authoring tools under `Source/`; their working originals also live in the game's `tools/` directory. The Blender scripts accept either the game project root or the extracted pack folder after `--`. The Python/Pillow scripts locate the pack relative to their own location:

1. Run `prepare_vegetation_textures.py` with Python and Pillow.
2. Run Blender in background mode with `build_vegetation_pack.py`, passing the project root after `--`.
3. Run `verify_vegetation_pack.py` through Blender with the same root.
4. Run `render_vegetation_pack.py` through Blender with `<root> all` after `--`.
5. Run `render_vegetation_lods.py` through Blender with the same root.
6. Run `layout_vegetation_previews.py` with Python/Pillow.

`SPECS` near the top of the builder exposes the deterministic seed, target height, crown radius and trunk radius for every tree. This is a small authoring script, not a required runtime generator. The game consumes the exported meshes directly.
