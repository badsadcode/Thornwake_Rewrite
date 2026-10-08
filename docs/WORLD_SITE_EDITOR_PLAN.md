# World Site Editor: plan (proposal, 2026-10-08)

Tom: "for towns/villages we will need to start working on the World Site Editor, a tool to make assets for
Thornwake, every kind of asset: biomes, plants, towns, everything the game needs."

Earlier wishes for it are in `docs/design-inbox/THORNWAKE_MASTER_REWORK_SUPPLEMENT_V2.md` (§4 biome authoring,
§20 weather profiles, §21 environment and resources, §24.16 terrain materials), in `NOOR_BIOME_SPEC.md` (custom
biomes) and in `BIOMES_PLAN.md` step 7. Nothing of it exists in code yet.

## What it is and is not
* **An assembler, not a modelling tool.** Meshes, textures and sprites are made in Blender, Krita and so on. The
  editor imports them and turns them into Thornwake content: a plant with wind stiffness, LODs, collision and where
  it grows; a village laid out on its own patch of ground with exits and slots; a biome built from those pieces.
* **Inside the game project** (a Godot scene, started with `--editor` or from the main menu). It uses the same
  terrain, renderer, wind, weather and water as the game, so the preview is exactly what players will see.
* **Everything it saves is a data file** in a package (see below), starting with `Content/` (the JSON the game already reads, such as `biomes.json`
  and `asset_sets.json`, plus new site files). The game and the editor read the same files, so nothing is
  hard-coded and Noor or Tom can make content without touching code. The headless tests validate every content file.

## Helper tools (Tom: "not painting or modelling software, but tools that help")
* **Terrain**: generate the sandbox ground **the same way the game does**. Pick a world type, seed and biome, or
  cut a patch out of a generated world. Then sculpt it with brushes (raise, lower, smooth, flatten to a height,
  terrace, ramp between two points, noise, erode) and paint ground materials and the blend mask.
* **Splines** for everything linear: walls, fences, hedges and palisades (pieces repeated along the line, posts
  at the corners, following the ground or stepping down slopes), plus roads, paths, ditches, streams and moats
  (the terrain is cut and the surface laid along the line).
* **Placement**: snap to the ground or a grid, align to the slope, rotate and scale with random ranges, duplicate
  along a line or in a ring, and a **scatter brush** for rocks, bushes, debris and crops.
* **Quality of life**: undo and redo, a live game-quality preview (time of day, weather, season), walk mode at
  player height, measurements, a fit check (how many places in a world this site fits), autosave and
  thumbnails for every asset.

## Packages: content as add-ons (Tom: "like mods for the game, or addons")
* The editor saves **packages**. A package is a folder, or one `.twpkg` file (a zip) when shared, with a
  `manifest.json` (id, name, version, author, dependencies, game version) and its content: data files (biomes,
  plants, sites…), models (`.glb`) and textures (`.png`).
* The game loads the **base package** (its own content, made the same way) plus the packages the player enables,
  in load order. A package can **add** content (a new village, a new biome) or **override** an entry by its id.
  Models load at runtime through Godot's glTF loader, so a package needs no rebuild of the game.
* Every package is validated on load (missing files, broken references, impossible values) with a readable report.
* **Determinism**: the enabled packages and their versions become part of a world's identity. In multiplayer the
  server lists its packages and their hashes, and clients must have the same ones (later: download them from the server).

## Pages
1. **Sites** (needed first, for settlements and POIs):
   * A sandbox patch of terrain (a pick from a real generated world, or flat).
   * Place buildings and props, paint the ground as relative offsets (pads, terraces, ditches) and a blend mask.
   * Mark road exits, optional building slots (house A/B/C, empty, ruin), material swap groups and the outskirts
     rules (fields, fences, paths).
   * Set the site type (village, town, castle, camp, ruin, POI) and which terrain, biomes and sizes it fits.
   * **Test in world**: generate a world, find the best-fitting site and fly there to see it blended in.
2. **Plants and objects**: import a mesh or sprite, then set its size range, wind (stiffness, height), leaf
   translucency, LODs and far impostor, collision shape, and what it yields (wood, fibre). Preview it swaying in
   live wind and weather.
3. **Asset sets and communities**: groups of plants and objects with densities and patch rules (Noor's macro,
   patch and micro levels).
4. **Biomes**: everything in `biomes.json` as forms and sliders (climate range, plants, ground, rocks, colours,
   sky, mist, weather tendency, rarity, neighbours), with a live world map and a fly-through preview.
5. **Ground materials**: texture sets, tint patches, relief and parallax.
6. **Weather profiles, resources, landforms**: later pages, following the design inbox.

## Order
1. The package format and loader (the base game's own content moves into the base package), then the editor shell:
   a package browser, save, load, undo, and a preview viewport using the game's renderer.
2. The **Sites** page with the symbolic villages from `GAMEPLAY_PLAN.md`, so the settlement system and its
   editor grow together.
3. Plants and objects, then asset sets, then biomes (the data already exists, so these are mostly forms and previews).
4. The rest as content needs it.
