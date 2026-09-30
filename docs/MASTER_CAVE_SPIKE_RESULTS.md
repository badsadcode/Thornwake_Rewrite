# Master Rework B2: cave terrain spike results

**Current continuation:** connected downhill water, saved water progression, aligned shorelines and smoother excavation are now implemented in the isolated lab. See MASTER_CONNECTED_WATER_RESULTS.md. The static-water descriptions and measurements below record the preceding milestone.

2026-09-28, updated after excavation/water playtest feedback. **Updated playable lab ready for testing:** restart `TEST_CAVE_LAB.cmd`. Instructions: `CAVE_LAB_TEST_GUIDE.md`. This remains a finite architecture experiment, not a production terrain replacement.

## Reused foundation and current scope

Existing `TerrainData.Height` / generator 2 supplies the base surface. An authored hill, entrance, tunnel, chambers and local reservoirs add a positive-solid/negative-air field. Hybrid mode replaces terrain and collision inside eight volume regions; full-volume comparison evaluates all sixteen regions. The actor can walk underground, hit ceilings, cross the hybrid boundary and excavate. A spatial test vein intersects both surface and cave wall. Edits use a separate, versioned atomic test save.

The normal valley, normal-world saves, gameplay controller, harvesting, building, crafting, cooking and other gameplay systems remain unchanged. No Post-Master gameplay expansion is included. Checkpoints: `backups/Thornwake-before-cave-spike-20260928-171630.zip` and `backups/Thornwake-cave-before-feedback-fixes-20260928.zip`.

## Playtest fixes

### Excavation geometry

Face winding now follows the tetrahedron's solid/air topology. Analytic density gradients are used for shading, with a geometric fallback, rather than deciding which side of a polygon is visible. In diagnostic runs against heavily overlapping cuts, the old gradient rule would reverse 67–86 faces at 1 m and 126–219 at 0.5 m. A separate defect discarded thin but nonzero triangles, leaving open edges; only truly zero-area triangles are now omitted.

The regression checks count incident triangles and opposite directed uses of every interior edge. Four excavation scenarios, including 80 overlapping cuts, pass at both resolutions with no open/nonmanifold interior edges or inconsistent shared-edge winding. Existing exact region-edge and hybrid interface tests still pass. This does not remove the deliberately coarse shape of 1 m sampling.

### Rebuild responsiveness

- One bounded terrain worker prepares meshes and vertex attributes from a private field snapshot. Height samples are reused vertically, normals and vertex colors reused at duplicate vertices.
- Cancellation and revision checks reject superseded edit, reload or resolution jobs. Repeated requests do not spawn an unbounded worker queue.
- Engine mesh/collider construction remains on the main thread, one region per frame. Affected regions are staged, then swapped together so a border edit never shows only half its new geometry.
- Existing collision remains active during digging and resolution/backend changes. Walking continues; another dig waits for terrain readiness. Initial loads and explicit F7 unloads stop movement where collision is missing.
- Water has its own bounded snapshot worker and revision checks. Its replacement waits for committed excavation, including changes to submerged bed depth.

Meshes are still triangle soup and the dirty unit is a 16 m region column. Background execution and caching greatly reduce freezes, but this is not yet a strict frame-time guarantee or a production LOD/block scheduler.

### Water

Water triangles are clipped to the actual basin instead of rejecting whole shoreline cells. The reservoir envelopes now extend to the terrain walls; the previous small circular limits created floating disks. Surface geometry updates after excavation, with bed-depth tint, transparency, animated ripples, restrained shore shading and specular response. The shader runs on the project's Compatibility renderer.

Water queries check a clear vertical path from each local surface, preserving the sealed lower cavity. This is a bounded static-reservoir fixture, not lateral flood propagation, drainage, pressure, swimming or a finished hydrology system.

## Measurements

Single local Debug runs after mesher warmup; 16 regions covering 64 x 64 m and a 28 m vertical sampling range. Raw current benchmark: `artifacts/cave-mesher-benchmark.json`. Initial times were recorded before the feedback fixes.

| Representation | Spacing | Initial core time | Current core time | Current triangles | Density samples |
|---|---:|---:|---:|---:|---:|
| Hybrid | 1 m | 495 ms | 134 ms | 26,170 | 67,048 |
| Full volume | 1 m | 851 ms | 220 ms | 43,996 | 134,096 |
| Hybrid | 0.5 m | 2,209 ms | 584 ms | 105,438 | 496,584 |
| Full volume | 0.5 m | 3,931 ms | 1,018 ms | 178,074 | 993,168 |

This is roughly 3.7–3.9 times faster core preparation. Caches trade more managed allocation for less repeated work; see the raw allocation/position-normal buffer fields. They do not represent total engine/GPU memory.

The rendered feedback scenario applies 29 cuts, exercises an eight-cut batch at 0.5 m, cancels obsolete resolution requests, updates a shoreline and saves/reloads the edit data. The high-resolution batch took **251 ms elapsed in the background**, with physics continuing. The longest measured terrain main-thread preparation/commit was **33.1 ms** across the entire scenario, including initial construction and mode changes. This is not the same metric as a frame time, and brief commit hitches remain possible. Evidence: `artifacts/cave-feedback-metrics.json` and `cave-feedback.log`.

## Verification

- **1,895,062 core assertions passed**: existing gameplay plus topology, region seams, local water, shoreline updates, bounded depths, sealed-cavity isolation, independent worker snapshots, cancellation and save reconstruction.
- Game build: **0 warnings / 0 errors**.
- Full cave physics scenario passed: actual walking into/out of the cave, upward ceiling contact, four-region excavation, atomic persistence, fresh region reconstruction, 1 m/0.5 m comparison, full-volume comparison and walking across the hybrid collider seam. `artifacts/cave-smoke-errors.log` is empty.
- Feedback engine scenario passed: overlapping cuts, retained/unaffected collision, movement during rebuild, high-resolution digging, stale-job rejection, shoreline mesh replacement and persisted edits. `artifacts/cave-feedback-errors.log` is empty.
- Rendered wall, underground pool and elevated lake inspected: `artifacts/cave-feedback-wall.png`, `cave-feedback-pool.png`, `cave-feedback-lake.png`.
- Normal-game regression passed with exit 0 and an empty error log, including all prior gameplay/streaming markers. Evidence is in `artifacts/cave-baseline-regression.log` and its error log. The test writes its isolated smoke save, never `world.json`.

## Architecture decision and next gates

**Continue with the hybrid direction, retaining full-volume as a comparison/fallback.** The experiment supports real underground geometry while reusing the surface foundation. It does not justify replacing the normal playable valley yet.

Before production integration:

1. Smaller dirty volume blocks, indexed meshes, explicit memory/commit budgets and LOD transitions. Async snapshot/revision handling is now exercised, but strict budgets remain open.
2. Dynamic promotion of arbitrary cave/edited regions to volume, including caves approaching the heightfield interface. This fixture deliberately keeps voids away from its outer hybrid boundary.
3. A world manifest and regional delta transactions that pin generation/content revisions; the experimental cut-list save is not continent-scale storage.
4. Macro geography, continents, terrain, hydrology and biome fields; then natural cave connectivity across many seeds. The current cave is an authored repeatable fixture.
5. Reservoir connectivity and gradual flooding, beyond clear-column static water occupancy.
6. A data-driven deposit catalogue, cross-region feature ownership and partial depletion. The current vein remains a diagnostic spatial feature.
7. Production actor/building/query/navigation adapters and large-origin physics checks, followed by the shared World Asset Library and vegetation/content tools in the Master order.

No release package replacement or normal-world migration is included.


