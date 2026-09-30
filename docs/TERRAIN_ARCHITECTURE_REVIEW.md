# Terrain and construction architecture review

Design reference: Thornwake_Terrain_Terraforming_Tools_Construction_updated.md.
The updated file supersedes the earlier proposal. Its section 32 adds the future third-person camera direction; camera redesign is explicitly separate from the first terrain milestone.

## What exists and what can be preserved

| System | Current implementation | Decision |
|---|---|---|
| World generation | WorldGenerator.Generate uses a specified seeded PRNG; stable resource IDs. Height uses a separate deterministic function. GeneratorVersion is saved and unsupported versions are rejected. | Retain generation version 1 and resource identities. Add new terrain generation passes behind a new version rather than changing old saved geography. |
| Terrain authority | Construction.Ground combines generated height with saved radial TerrainEdits. Rendering and walking already query this independently of mesh vertices. | Extract one shared TerrainData query. Preserve legacy height math exactly. A compact sample-delta format needs a versioned migration before replacing the existing edit log. |
| Rendering | Valley.RebuildTerrain replaces one whole mesh and regenerates grass for a small edit. Mesh spacing is 2 m and ground queries are continuous. | Replace rendering with finite 16 m regions and a 1 m visual sampling experiment. Rebuild affected regions plus a normal/border halo. Keep data/query spacing separate from display mesh spacing. |
| Save/load | Atomic save replacement, validation, rotating backup, preserved older-schema backups; schema 8. | Preserve this infrastructure. New sample deltas, generation settings and construction projects will require an explicit schema upgrade and corruption/migration tests. |
| Modular building | Construction.Plan performs snapping, support, roof connection, collision, slope and material checks. Simulation.Build deducts resources and commits immediately. | Keep geometry, snapping, support and occupancy logic. Split spatial validation from inventory/payment checks for free blueprint planning; do not call instant Build when merely placing a blueprint. |
| Building persistence | StructureState stores pose, paid costs, support IDs, material/shape selection and roof connections. | Completed blueprint segments can become ordinary StructureState objects. In-progress projects must have their own IDs, reservations, delivered inventory and labour state. |
| Movement/navigation | Character and enemy use custom CanWalk/WalkHeight queries; villagers currently follow scripted paths. There is no navigation mesh or worker pathfinding system. | Keep the current query adapter. Future navigation must consume terrain revision/dirty-region events; do not claim rebuilding a navmesh that does not exist. |
| Tools | Hoe click lowers/prepares soil; Shift-click raises it. Simulation.ModifyTerrain applies an immediate bump and stamina cost. | Preserve during the foundation step. Add distinct shovel and pickaxe actions, timed work and material restrictions in the next playable step. The hoe should then specialize in cultivation. |
| Camera | Smooth orbit, mouse chord/middle drag, optional always-look, pitch/zoom limits, camera-relative movement, menu/focus ownership, foliage cutaway. No solid-obstacle camera collision. | Keep these controls during terrain work. Later add collision/shortened boom and context-sensitive planning range. Do not bind new terrain shortcuts over Q/R orbit, combat or existing build keys accidentally. |
| Vegetation | Stable species presentation, shared LODs, persistent planted growth and stumps. | Reground only affected resources after a terrain edit. Later site preparation should explicitly clear or reject vegetation rather than silently deleting it. |

## Actual conflicts to resolve before blueprint construction

1. Construction.Plan currently rejects insufficient player inventory. Free plans need spatial validity even with no materials.
2. Simulation.Build performs payment and completion together. Delivery and labour need independent, atomic commands.
3. PaidCosts on finished pieces cannot represent unspent delivered materials or segment progress.
4. Cancellation needs a persistent world drop/storage representation; no existing drop system can yet preserve unused deliveries at a site.
5. Site occupancy must include planned/reserved footprints, with ownership and support rules. Pending pieces must not count as finished walkable floors.
6. Fences can reuse modular models, but long routes require sampled terrain/slope checks, spacing, corner handling, segment IDs and partial completion.
7. Terrain edits must protect completed structures, construction reservations, village infrastructure and active work areas across the entire brush footprint.
8. The finite 55 m gameplay boundary is repeated in placement, vegetation, saves, river rules and map rendering. Exposing only a terrain mesh size would not make the world safely configurable.

## Incremental delivery sequence

### A. Shared query and local terrain presentation (current foundation step)

Extract legacy height evaluation, region coordinates, read-only sampled region data, slope/normal and initial material/moisture queries. Segment terrain/grass rendering and rebuild only affected regions. Provide developer terrain overlays. Preserve current saved terrain and controls.

Acceptance: exact legacy height equivalence; region border heights/normals agree; an edit touching a border updates both sides; unrelated region nodes remain intact; affected resources stay grounded; save/reload matches; existing gameplay tests pass.

### B. First playable earthworks loop

Introduce versioned generation settings and compact persistent sample deltas, with generator 1 migration/compatibility. Add explicit generation passes for new worlds, not a silent change to old worlds. Compare 0.5 m and 1 m data resolution separately from mesh resolution.

Add a craftable shovel with lower/fill/level/smooth modes, visible footprint and target-height reference, finite work time, stamina/reach checks and material restrictions. Add fill resources and conservation rules before substantial raising. Improve slope traversal/camera collision as required for safe testing. Save and reload the exact edited result.

### C. First construction project loop

Separate spatial planning from affordability; add persisted project/segment state. Adapt a simple fence, then two-point palisades. Deliver materials, apply timed labour, complete sections into normal structures, and leave recoverable unused deliveries when cancelling.

### Later

Grade/trench operations, site preparation, road planning, configurable world bounds, worker task adapters and real pathfinding. Streaming and underground volumes remain separate later projects. Atmosphere is deferred as requested.

## Current scope

The foundation step does not by itself complete the proposal's shovel milestone. It removes the whole-world rebuild bottleneck and establishes reusable terrain queries while keeping the existing game runnable. It does not add caves, NPC labour, blueprints, a full hydrology/erosion model or new camera controls.
## Update: earthworks interaction delivered in 0.7.0

The shovel interaction part of B now runs on sparse 1 m / 1 cm integer sample deltas with soil conservation, timed work, protected full footprints and schema-9 save/resume. Hoe gameplay only cultivates. See EARTHWORKS.md. The new generator passes/settings and 0.5 m gameplay comparison are still pending; existing worlds keep generator 1. No new blueprint or camera system is implied by this release.

## Update: generation and pre-game settings delivered in 0.8.0

Generator 2 and saved generation settings now apply to newly created worlds. Separate macro, shaping, prepared-site, river and resource stages are inspectable; setup offers live terrain/resource previews. Generator 1 remains untouched for old worlds. See LANDSCAPES.md. The current finite world extent and 1 m authority remain unchanged. A 0.5 m gameplay comparison, larger bounds, navigation, blueprint delivery/labour and camera changes are still separate work. User feedback about weak digging is recorded for later sandbox tuning.
