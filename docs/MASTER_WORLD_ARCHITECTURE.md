# Master world architecture decision record

> 2026-09-29: `THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md` supersedes conflicting policy below. In particular, ordinary lakes/ponds use finite storage, rivers/springs provide rate-limited inflow, and valid abstract downstream exports are distinct from arbitrary chunk-edge drains. See [the current drainage checkpoint](MASTER_DRAINAGE_CHECKPOINT.md) for inspected source state, validation and the exact next step. The older investigation/results below remain historical.

2026-09-28: migration constraints accepted. The B2 fixed-patch cave experiment now passes; hybrid remains the preferred direction, with production integration gates still open. See MASTER_CAVE_SPIKE_RESULTS.md.

| Alternative | Reuse | Constraint | Decision |
|---|---|---|---|
| Enlarge existing heightfield | Existing queries, edits, rendering | One Y per X/Z cannot represent caves; finite saves/keys/walking also fail at scale. | Preserve for legacy and early tests; insufficient final backend. |
| Decorative cave meshes below heightfield | Surface and art | Ground queries still treat overworld as solid; excavation/exposure inconsistent. | Reject as authoritative solution. |
| Hybrid surface and local volumetric blocks | Surface generation, sparse edits, dirty halos; volume work concentrated where needed | Needs coherent solid/void authority, removed surface triangles/collision at openings, editing/LOD seams. | Preferred spike candidate, not yet proven. |
| Fully volumetric plus distant surface LOD | Seeds/content/commands; substantial terrain/movement adapters | Requires measured meshing, storage, collision and transition budgets. | Comparison/fallback candidate. No dependency installed. |

This alternatives table records the initial code-review rationale. Fixed-patch measurements and collision/editing evidence now appear in MASTER_CAVE_SPIKE_RESULTS.md; they do not establish production world-scale performance.

## Shared model

- World manifest: seed, generator version, frozen profile and content revision. Render residency never determines geography or feature identity.
- Global double positions and bounded integer region addresses; local engine floats. Horizontal stream addresses do not prescribe vertical voxel subdivision.
- Macro geography/hydrology above render-region scale. Deterministic feature owners with bounded neighborhood queries/size halos; long rivers/veins reference the owner across regions.
- Surface height remains a convenience query. Solid/void, ray/sweep, support-surface and water queries enter when the spike requires them. Height sampling is not a substitute.
- Roads/buildings retain protection checks. Sites contribute policies via content.
- Regenerable base plus regional persistent deltas; visited areas pin generation/content. New resources need explicit new-world/unvisited-region/migration policy.
- Authoritative commands address world, feature/region and revision. Rendering cannot grant rewards, reset depletion or advance persistent simulation.

## First implementation boundary

B1 adds address/origin math and a bounded planner, then tests real unload/reload in a finite engine harness using existing TerrainData and Valley meshes. Preserve normal 64-region presentation, existing saves, generator behavior and controls. No Post-Master expansion.

Planner memory/work is bounded by local observer count/radius, never total world area. Replanning replaces stale pending work. Presentation installs only successful builds; visual unload never mutates WorldState. Mesh commits initially use a count budget. Async workers and millisecond budgets follow measurement.

## B2 exit criteria

Test a border-crossing cave entrance, two surfaces at one X/Z, walking inside/outside, ceiling collision, editable seam and supported building. Record density/mesh/collider time, memory and unload/reload; compare local 1 m and 0.5 m sampling. Reject visual-only surface hiding with solid legacy queries underneath.

Add elevated pool/lower disconnected cavity to establish water-query compatibility; gradual flood state still needs its own validation. A persistent 3D deposit must intersect cave and surface through the same authority. Final terrain choice follows this evidence.

## B2 result

The fixed-patch hybrid and full-volume comparison is now playable through `TEST_CAVE_LAB.cmd`. Real underground traversal, ceiling collision, an editable region seam, isolated persistence, a hybrid collider interface and local water/deposit fixtures passed. Hybrid used fewer samples/triangles and less meshing time in this test. Retain it as the preferred direction while validating dynamic volume promotion, async budgets, LOD, regional persistence and production gameplay adapters. Existing generators/saves remain unchanged. See MASTER_CAVE_SPIKE_RESULTS.md for measurements and explicit limits.
## B2 playtest corrections (2026-09-28)

The cave lab now uses topology-based face winding, preserves nonzero thin triangles, prepares terrain/water on bounded background workers, rejects stale revisions and retains collision until an affected batch can be swapped together. Water follows basin shores and has an animated depth-aware surface. Core topology, worker isolation and water checks plus actual rendered digging/movement pass. This closes specific prototype defects; smaller dirty blocks, indexed geometry, strict budgets, dynamic volume promotion and production hydrology remain open. See MASTER_CAVE_SPIKE_RESULTS.md for measured costs and the next gates.

## Breached-lake hydrology gate (2026-09-28)

Further water feedback exposed the distinction between clipped static surfaces and connected downhill flow. The current lab does not implement stream outflow, lateral cave flooding or changing reservoir storage. `MASTER_HYDROLOGY_SPIKE_PLAN.md` records the inspected limitations, the hilltop-lake acceptance scenario and the proposed regional connectivity spike. Natural lakes may retain their level under the Master plan's persistent-source policy while their outgoing stream descends; finite-volume lake drainage is a separate policy, not an implicit requirement of the question. Validate this before treating the water foundation as stable.

## Connected-water spike (2026-09-28)

The isolated cave lab now demonstrates bounded connected downhill flow and gradual flooding, with saved water progression, saturated-passage head transfer, terrain-aligned surfaces and partial-bank coverage. Excavation joins use bounded smoothing, with explicit continuous strokes for the diagnostic outlet. See MASTER_CONNECTED_WATER_RESULTS.md for evidence and limits. This advances the hydrology gate; production region handoff, narrow-feature validation, world-scale performance and actor adapters remain open. The static-water limitation above describes the previous milestone.

## C1 delivered: macro geography (2026-09-28)

`MacroWorldManifest`, `MacroWorldGenerator`, `MacroWorldMap` and `MacroRegionSampler` now provide additive algorithm-3 geography, frozen content profiles and independently sampled chunks. `TEST_WORLD_GENERATION.cmd` opens the diagnostic map. The vast-world profile covers 2,048 x 2,048 km; only bounded macro descriptors and requested sample buffers exist in memory. No game terrain, entity or cave-volume array is allocated for the complete area.

The current sampler reuses 16 m `WorldRegion` addresses with 1/2/4/8 m sample spacing. This is a tested addressing/sampling foundation, not a decision that every future render LOD or storage block must be 16 m. Production should batch surface rendering and use coarser distant LOD; local cave/edit blocks can remain smaller. New-world 3D residency, physics/rebasing and region delta transactions still need integration. No normal-world activation occurs in this milestone.

Read `MASTER_MACRO_WORLD_FOUNDATION.md` for tests and limits, and `MASTER_AZGAAR_REFERENCE.md` for the user-suggested generator review and next drainage/climate gate.

