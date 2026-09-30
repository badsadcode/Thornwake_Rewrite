# Thornwake — Blender asset template kit

**Start with `Thornwake_Asset_Library.blend`.** It contains a separate scene for each asset family. Choose a family from Blender's Scene selector, select an object and press **Numpad .** to frame it. The overview is arranged for browsing; open an individual file under `Templates/` to work at the correct origin.

There are **96 native .blend templates**, each built from the actual prototype geometry and checked against the game's measured dimensions. **1 Blender unit = 1 metre.** Blender is Z-up; GLB handles conversion to the game's Y-up coordinates.

## What is included

| Folder | Count | Contents |
|---|---:|---|
| 01_Roofs | 33 | Slopes, ridges, hips, valleys, eaves and gable edges at 22.5°, 30°, 45° and 60°; existing legacy roof shapes |
| 02_Walls | 19 | Solid, doorway, diagonal, and both sides of sloped/peaked gable fills at each pitch |
| 03_Structure | 6 | Full/triangular foundation and upper floor, stairs and fence |
| 04_Stations | 7 | Campfire, workbench, carpenter, kiln, forge, cooking station and oven |
| 05_Decor | 6 | Chair, table, planter, painting, lantern and rug |
| 06_Nature | 10 | Pine, birch, sapling reference, stump, rock, fruiting/bare bush, grass, young/mature crop |
| 07_Characters | 3 | Player, villager and bandit proportion/pose references |
| 08_Tools | 5 | Axe, hammer, hoe, seed bag and sword |
| 09_Village | 7 | Cottages, workshop, well, caravan cart, bridge and an optional existing shrine helper |

`asset_dimensions.csv` gives every name, game ID, actual width/depth/height, nominal building footprint, pitch and integration status. `asset_manifest.json` is the same inventory with precise bounds and origin notes. `icon_manifest.json` lists inventory icon names and a proposed **256 × 256 transparent PNG** art target; this is an art convention for future replacement, not a claim that the current UI loads custom PNGs.

## Inside each template

- **MODEL** — editable prototype geometry under a clearly named asset root. Replace, simplify or rebuild these meshes. Keep moving pivots such as `DoorLeaf` where required.
- **REFERENCE__DO_NOT_EXPORT** — a locked original copy, hidden by default. Toggle its eye in the Outliner to compare silhouettes and proportions.
- **GUIDES__DO_NOT_EXPORT** — wire bounds, the building footprint where applicable, origin axes, name and dimensions.
- **READ_ME_FIRST** — Blender text with that object's dimensions and origin contract.
- **EXPORT_MODEL_GLB** — Blender text script that exports only MODEL, excluding guides and reference geometry.

The root object's Custom Properties also identify the game object and integration status. Files are saved with **Blender 5.1.1**. The matching GLBs under `References/` provide an interchange option for other Blender versions.

## Working on an asset

1. Open its individual `.blend` from `Templates/<family>/`.
2. **Save As a working copy outside ArtKit**, for example in your own modelling folder or `ArtSource/`. The kit is generated and its templates may be regenerated later.
3. Keep the asset root at **(0, 0, 0)**. Build new meshes under that root and in the MODEL collection. Delete prototype meshes from MODEL when replacing them. Do not export a translated object from the library overview.
4. Keep the footprint, origins, pitch and key attachment points. Materials, textures and decorative details are yours to replace. The default templates contain one geometry for each physical shape; create separate working copies for wood, stone, thatch, clay, metal and other visual styles.
5. In Blender's Text Editor, select **EXPORT_MODEL_GLB** and run it. It exports `Exports/<asset_id>.glb` beside your working file. Rename the resulting file to include a finish if desired, e.g. `Roof_Slope_45_Thatch.glb`.
6. Roof/wall replacements can be placed in the game's `Assets/Buildings/` and mapped to a finish in `Content/building_tools.json`. Follow `Building_Art_Integration.md` for the mapping format and optional edge/door groups. Rebuild the standalone game to distribute new art.

GLB export is also available through Blender's File → Export menu. Export the MODEL hierarchy only, with metres and the standard glTF axis conversion. Do not include the reference or guide collections.

## Important dimensions

- Building grid: **3 × 3 m**.
- Standard wall panel: **3 m span × 2.6 m height**; timber frame details extend slightly beyond that panel.
- Storey rise: **2.88 m** (2.6 m wall plus 0.28 m deck).
- Stair walking surface follows that storey rise; its railings make the full visual bounding box taller.
- Prototype player: **1.82 m** measured total height.
- Trees and rocks: templates use the base scale; placed instances can vary and planted trees scale as they grow.

**Visual bounds and snap dimensions are different.** The CSV and wire guides distinguish them. A roof's rafters can extend slightly beyond its 3 m footprint. Do not resize a model to make its entire visual bounding box exactly 3 m; preserve the connection profile.

## Origins and motion

- Roof/gable templates use the module's origin. A gable lies along one edge of the module, so its geometry is intentionally offset from its origin. Never centre a gable on its visible geometry.
- Roof templates omit optional exposed-edge overhangs. Add your overhang geometry beneath the named `EdgeNorth`, `EdgeEast`, `EdgeSouth`, `EdgeWest` groups described in the integration guide. Shared edges need to stay flush.
- Door templates retain the `DoorLeaf` hinge. Do not join the leaf into the wall or move the hinge when creating an interchangeable door model.
- Held tools retain the current arm/hand attachment frame. Their visible geometry is deliberately offset below the root. A new centred tool origin would require changing the game attachment adapter.
- Character templates preserve proportions and rigid limb pivots. They are **not production character rigs**. Model from them now; the eventual skeleton, skinning and animation integration still need a dedicated pass.
- Flame shapes are static prototype references. Lighting, particles, water and animated shader effects remain separate game systems.

## Replacement roadmap

The current game remains on prototype art. Roof/wall finish mappings already accept custom static GLB models. The other families are supplied as accurate modelling references; their replacement adapters, character rigs/animations and custom icon loading will be added as those systems are developed. A template existing here does not mean that dropping its GLB into the game automatically replaces that object.

Inventory-only materials/food do not all have world meshes yet; their IDs and future icon names are listed separately. Continuous terrain, river, roads and distant hills are generated world surfaces, so they need terrain/material work rather than fixed-size object templates. Embedded details in village houses/stations remain part of their reference assembly until separated into reusable props.

## Verification

All 96 GLB references were imported to Blender and compared with measured game-space vertex bounds after axis conversion (tolerance 2 mm). `template_validation.json` records the measured errors and file paths. All 96 native files were reopened and checked. Five representative templates were exported through the included helper and reimported into Godot: their dimensions and axes were preserved, the door hinge survived, and guides/reference geometry were excluded. Results are in `verification.json` and `game_roundtrip_verification.json`. A rendered sample is available in `Template_Preview.png`.

The kit is a separate authoring deliverable and is not added to the player download. Players continue to need no Blender, Godot or extra runtime installation.
