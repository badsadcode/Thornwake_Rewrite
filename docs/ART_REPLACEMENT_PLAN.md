# Handmade asset replacement plan

The project owner intends to replace all prototype models, sprites and graphics with handmade assets. Keep prototype visuals during gameplay development; their silhouettes, colours and mesh layouts are not final art direction.

## Contracts to preserve

- Saved game/content IDs identify gameplay objects. Asset paths and cosmetic finishes are separate and can change without rewriting saved houses, inventory or vegetation.
- Physics, snapping, support, reach and resource costs come from gameplay definitions. Replacing a mesh does not infer new collision or change those rules.
- Modular dimensions, roof profiles and attachment pivots are explicit contracts. Decorative appearance is flexible. Changing functional dimensions is a deliberate gameplay change and should update the definition and its checks.
- Growth progress, door state, crafting duration and action events belong to gameplay. Future models/animations consume those states rather than driving elapsed time or resource transactions.
- Missing art must retain an appropriate prototype fallback and a useful diagnostic.
- Source `.blend` files belong in artist working folders such as `ArtSource/`; exported runtime GLBs/textures belong in `Assets/`. Generated `ArtKit/` templates are disposable references and must not contain the artist's only working copy.

## Current state

Roof/wall cosmetic mappings support static GLB/Godot scene overrides. Optional roof edge groups and the DoorLeaf pivot already have presentation hooks. These mappings preserve physical shape and historic build costs. The 96-file Blender kit supplies actual-scale references for current object families.

Nature, props/stations, village prefabs, characters and held tools still use procedural presentation. Add per-family visual adapters as their handmade models arrive. Continue using their stable game IDs and explicit scale/origin metadata. Character rig/skeleton compatibility and production animations need a dedicated integration pass; the supplied mannequin is a proportions/pivots reference.

UI icons are currently drawn in code. `ArtKit/icon_manifest.json` reserves readable names and proposes 256-pixel transparent source icons. Add an icon resolver with the existing procedural fallback when custom sprites arrive. HUD styling and fonts can then evolve separately from inventory/crafting/quest state.

Generated terrain, roads, river and distant hills will need terrain/material or environment-system replacement rather than individual object templates. Separate reusable props from village prefabs when useful instead of treating every facade detail as a permanent gameplay object.
