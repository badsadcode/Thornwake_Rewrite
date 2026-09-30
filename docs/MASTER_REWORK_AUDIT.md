# Master Rework: code audit and implementation plan

2026-09-28. Both v3 plans were read before implementation. Master is the current scope; Post-Master supplies compatibility requirements only. Findings describe this checkout, rather than assumptions in older documents.

## Baseline

Godot 4.5.1 .NET; engine-independent C# core; net8.0 game/net9.0 tests. Entry: Game/Main.tscn and Main.cs. No Git repository or AGENTS.md found. Verified 718-file source snapshot: `backups/Thornwake-before-master-rework-20260928-170047.zip`, with adjacent SHA-256 manifest. Runtime assets/content and both plans are included; engine/build output and ArtKit authoring sources remain in place.

Before edits: **1,636,611 core assertions passed; game build 0 warnings / 0 errors**. This is automated compatibility evidence, not exhaustive human playtesting. Build required access to installed SDK/NuGet configuration; no engine replacement or package installation.

## 1. Reuse

| Area | Actual code and behavior | Preserve |
|---|---|---|
| Terrain | Core/TerrainData.cs owns height/normal/material queries and dirty halos. World/ValleyTerrain.cs builds 64 independent 16 m meshes and regional grass. | Shared authority, border normals, local invalidation. Already regionized, but all regions remain loaded. |
| Generation | WorldGenerator.cs: specified PRNG, stable resource IDs, generator 1. LandscapeGenerator.cs: generator 2 macro, shaping, prepared sites, river and resource filters. | Both algorithms and existing IDs/geography; new geography needs a new version. |
| Terraforming | Earthworks.cs: sparse 1 m deltas, 1 cm steps, triangular interpolation, revisions, timed work, soil conservation; legacy radial edits remain additive. | Shovel/hoe gameplay and protected footprints. |
| Construction | Construction.cs, BuildingVariants.cs, RoofGeometry.cs: shared preview/commit validation, snapping, floors/stairs, support, roof variants, finishes, paid costs and safe dismantling. | Geometry, transactions and support; adapt spatial/ground queries later. |
| Gathering/ecology | Simulation.Gather and Vegetation.cs: persistent removals, planted growth, regrowing berries, stumps. Woodland.cs maps stable visual species. | Resource identity and existing gameplay while assets migrate explicitly. |
| Art | WoodlandAssets.cs/ValleyForest.cs: 17 tree variants, 3 LODs, stumps and canopy cutaway. BuildingArt.cs provides mapped model overrides. | Existing imported art and rendering adapters. |
| Crafting/cooking | Workstations.cs, CraftingQueue.cs, Content/recipes.json: station-specific recipes, reach, reservations, queues, cancellation and saved timing. CraftActivity/CraftAnimation provide work poses. | Already differentiates carpenter, forge, kiln, cooking, oven and handwork. Roadmap's generic-station description is outdated here. |
| Saves | SaveStore.cs: schema 10; migrations from 1–9, validation, atomic replace, rotating and original-schema backups. | Legacy reader/writer and explicit rejection of unsupported saves. |
| Other gameplay | Hotbar, skills/journal, farming, trade, aggregate settlement economy, combat, camera and UI have regression coverage. | Mechanics remain intact during world foundations. |

## 2. Extend

- Streaming: separate persistent state from disposable presentation; bounded work/residency, proximity priority, stale-request cancellation and hysteresis. Later add worker sampling, collision/vegetation budgets and distant LOD.
- Coordinates: global doubles and integer region addresses, converted to engine floats only after subtracting a local origin. Legacy valley floats remain unchanged.
- Generation: frozen world profile/content revision, deterministic feature ownership independent of load order, global sampling and halos. Geography is larger than render regions.
- Saves: new-world manifest plus regional deltas and a cross-region commit policy. Pin definitions/snapshots so visited geography never silently changes. Unloading meshes must not erase state.
- Shared World Asset Library: common definition IDs for generation, biomes, sites and manual placement; adapt current loaders. Species supplies identity/hardness, physical instance dimensions/scale supply yield. Preserve legacy wood mappings until explicit migration.
- Water: local levels/river profiles, footprints and connectivity shared by terrain, placement/ecology and future tools; persistent breaches and gradual flood state independent of rendering.
- Authority: retain engine-free commands; add world/region identity, validation and revisions. No network transport exists yet.

## 3. Conflicts

1. Heightfield Height(x,z) cannot represent cave ceilings/overhangs. Construction.WalkHeight starts at overworld height; Main.MovePlayer and targeting depend on it. Cave art alone would be unplayable.
2. Terrain meshes have no Godot terrain collider. Movement uses custom horizontal obstacle checks and height sampling. Cave collision/navigation requires explicit integration.
3. Water is one plane at (27,-0.1,0) in Valley.Generate. River restrictions, moisture, sand, willow placement and bridge walking repeat fixed-coordinate assumptions; generator 2 retains them.
4. Finite bounds recur in WorldGenerator, Valley, Construction, Simulation, Vegetation, TerrainGrid, SaveStore and maps. Changing one constant is unsafe.
5. TerrainGrid.Key packs signed 16-bit X/Z: distant edits would alias. Keep this codec confined to legacy worlds; new state needs region-local samples and vertical addressing.
6. Global resource generation/validation, collection scans, bounded save coordinates and 5 MB save cap cannot serve continents. Regionized meshes alone are not streaming.
7. Woodland hardcodes species/models/ecology; Gather pays fixed generic rewards regardless of size. Harvesting is real, but metadata needs extension.
8. Village lots/roads/stations and NPC paths are authored in code. Convert through adapters without relocating old saves or deleting working stations.
9. NPCs are visual paths plus aggregate economy, with no individual persistent schedules, navigation mesh or multiplayer transport.

## 4. Architecture recommendation

See MASTER_WORLD_ARCHITECTURE.md. Retain generators 1/2 and their finite playable adapter. New worlds use hierarchical feature data, bounded streaming, local rendering origins and regional deltas. **Provisional terrain preference: hybrid surface plus locally volumetric terrain**, with one solid/void authority and real surface replacement at entrances. No backend/library commitment before seam, collision and edit spikes. Fully volumetric terrain remains a comparison/fallback candidate.

Hydrology and spatial deposits belong to world data, not mesh vertices. Veins/seams/pockets have stable owner identity and spatial bounds; intersecting regions query the same feature across borders. Depletion addresses portions of that feature independently of fill. Dig/raise cycles must not create valuable ore.

## 5. Safe implementation order

1. **A — Baseline/audit:** snapshot, existing tests/build, this inventory and architecture record.
2. **B1 — Streaming foundation:** safe coordinates/addresses, deterministic feature identity, bounded streaming planner and an inspectable engine harness reusing current terrain. Verify movement/teleport, cancellation, unload/reload, edges and edit retention. This is a foundation experiment, not continent generation.
3. **B2 — Terrain decision spike:** entrance across a border, two underground levels, collision, cross-border edit, two water elevations and save/reload. Compare hybrid and a full-volume block using measured mesh/collision time and memory.
4. **C — New-world generation:** versioned profile/manifest, continents/islands, mountain/valley backbones; then drainage/basins/local water, climate and weighted data-driven biomes. Many seeds, far-origin and boundary tests. Never change old-world algorithms.
5. **D — Natural caves:** tunnel/chamber graphs, entrances/shafts, water integration; prove traversal/connectivity across streamed boundaries.
6. **E — Deposits:** generic spatial veins/seams/pockets/beds, cave/terrain exposure, deterministic owners, persistent state and diagnostics. Data-driven initial catalogue, no mining/crafting expansion.
7. **F — Asset Library then vegetation:** register existing art once, physical dimensions/species/yields, ecology scatter and manual placement sharing harvesting behavior.
8. **G/H — Content tools/sites:** terrain preview, biome/profile/feature authoring, irregular site blending, local water, sockets, uniqueness, protection and NPC semantics. Sample content must be authorable without core edits.
9. **I–M — Remaining Master:** modular characters, creator, camera, shared targeting and gameplay adaptation after stable world foundations. Post-Master remains deferred.

Each gate requires old core tests, game build and relevant engine tests. Activating new-world gameplay waits for spatial/save/gameplay adapters; development harnesses remain explicit.

## 6. Spikes required before major commitments

- Hybrid versus full-volume entrance: seams, ceiling collision, editable boundaries, normal/LOD transitions and memory. Compare 1 m and 0.5 m local sampling.
- Large-coordinate streaming: negative boundaries, small distant offsets, origin shifts, observer union, teleport cancellation; later migrate physics/audio/particles together.
- Hydrology: dry disconnected cavity under a lake, breach, bounded connectivity work, gradual filling, closure/reopening and save restoration. A local water level alone does not prove flooding.
- Deposits: branching curved vein and seam spanning at least four regions, exposed by caves and edits, depletion surviving reload without duplication.
- Persistence: modified region unload, interruption during multi-region commit, unsupported manifests/revisions. New content must not silently alter visited regions.
- Sites: irregular cross-border footprint, safe foundations/protection, semantic markers queryable without instantiating the site.
