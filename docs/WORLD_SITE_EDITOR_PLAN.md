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
* **Everything it saves is a data file** under `Content/` (the JSON the game already reads, such as `biomes.json`
  and `asset_sets.json`, plus new site files). The game and the editor read the same files, so nothing is
  hard-coded and Noor or Tom can make content without touching code. The headless tests validate every content file.

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
1. The editor shell: scene, file browser of `Content/`, save, load, undo, and a preview viewport using the game's renderer.
2. The **Sites** page with the symbolic villages from `GAMEPLAY_PLAN.md`, so the settlement system and its
   editor grow together.
3. Plants and objects, then asset sets, then biomes (the data already exists, so these are mostly forms and previews).
4. The rest as content needs it.
