# Landscapes — generator 2 / release 0.8.0

## New-world choices

Customize new world provides a seed, Gentle/Harthwood/Rugged presets and three bounded settings: Relief 0.4–1.6, Roughness 0–1, ForestDensity 0.4–1. A draft changes the live preview without touching a played world. Create copies validated settings into a new world; Back/Escape cancels. The preview reports real generated tree count and uses the same ground height as gameplay. These settings cannot be changed in a played world through the sandbox UI.

Growth/crafting/survival rules continue to use the existing in-game sandbox. Digging depth/strength tuning and pre-game survival rules are still future work, as discussed with the user.

## Explicit generation passes

1. **Macro**: seeded broad lowland variation, an elongated western ridge, a plateau and an eastern upland. Integer hashing supplies stable variation without relying on runtime Random.
2. **Shape**: broad curved drainage swales and small surface variation. This is analytic shaping, not erosion simulation.
3. **PrepareSites**: blend authored village, farm and encounter pads into the landscape; prepare two road corridors. Current fixed POI locations remain intentional constraints.
4. **River**: carve the existing river and bank profile while retaining its fixed bridge alignment.
5. **Resources**: filter stable resource candidates by generation settings and generated slope. Density removes only tree candidates; filtered resources keep their original IDs. Player edits never rerun candidate selection against modified heights.

LandscapeStages exposes macro/shaped/prepared/final heights independently. TerrainData queries dispatch by saved GeneratorVersion; radial legacy edits and the sparse shovel overlay remain additive. Rendering, placement, movement and resources consume that shared height. Visual mesh sampling remains 1 m in 16 m regions.

## Compatibility contract

- New player worlds use GeneratorVersion 2 and GenerationSettings.Version 1.
- WorldState defaults to generator 1 for compatibility/legacy fixtures; the production new-world route explicitly calls LandscapeGenerator.Create.
- Generator 1's original formula and resource list are unchanged. Existing schemas 1–9 load as generator 1, now in schema 10 with default unused generation metadata. Their terrain and resources do not move.
- Schema 10 requires generation settings. Unsupported generators, parameter ranges, nonfinite values and settings versions are rejected. The first upgraded save retains its original `.v9` (or earlier) file independently of `.bak`.
- **Freeze generator 2 after release.** Future geography algorithm changes require a new generator version with an explicit retained version-2 path. Changing constants silently would move saved ground and invalidate resource identities.

## Maps and presentation

TerrainMapImage creates cached height shading and 1.5 m contour hints. The setup preview and in-game map share it. Maps show actual natural trees rather than unrelated decorative random dots; harvested trees disappear, while planted trees appear according to growth. Completed shovel work updates the map's terrain image. Normal terrain colours distinguish uplands and steep slopes; existing F9 diagnostics remain available.

## Current limits / next steps

The playable extent is still 110 m across. Roads, river, village sites and the bridge remain authored. There is no streaming, erosion solver, dynamic hydrology, procedural settlement layout, underground excavation or worker navigation. Current landforms fit the prototype's fixed geography. Comparing 0.5 m authoritative sampling in actual gameplay remains pending.

The next construction slice should separate free spatial blueprint planning from payments, persist deliveries/labour, and complete a simple project into ordinary building pieces while preserving snapping/support logic. Digging strength is recorded for a later sandbox balancing pass and is unchanged here.
