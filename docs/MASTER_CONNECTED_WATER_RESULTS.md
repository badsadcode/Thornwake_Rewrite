# Connected water and excavation milestone

2026-09-28. **Implemented in the isolated cave lab.** This supersedes the static-water limitation in the earlier cave spike reports. It is a bounded hydrology prototype, not completion of the Master Rework or activation in normal worlds.

## What changed

- Water/terrain intersections now use the same piecewise-linear tetrahedra as the visible terrain. A water surface is clipped inside each terrain tetrahedron, including sloped river surfaces, instead of sampling an unrelated smooth field.
- Rendering covers partially submerged bank cells even when their centre is dry. It does not extend those bank patches into dry air-filled receiving cells. A new coverage regression finds 161 missing wet sample points out of 2,460 when this correction is disabled; it passes at both 1 m and 0.5 m with the correction. Evidence: `artifacts/cave-shoreline-negative-control.txt` (an intentionally failing diagnostic).
- Version-2 lab excavation blends cut joins with a bounded 0.2 m maximum extra removal. Mesh, collision, water and saved reconstruction all use that field. Version-1 hard-cut data remains readable. The lab upgrades its in-memory interpretation on Load; the file changes only on manual Save and retains the previous file as `.bak`.
- The diagnostic outlet uses explicit continuous capsule strokes, preventing ridges between separate spherical holes. Single-click excavation remains a spherical stroke. Stroke endpoints and water progression are included in version-2 lab saves.
- The complete sixteen-region cave patch is resident during this finite experiment, avoiding exposed water beyond a missing cave region. The separate B1 streaming planner and normal valley remain unchanged.

## Connected flow

`LocalWaterTopology` builds bounded one-metre columns with multiple vertical void intervals. Neighbouring columns connect through sampled open passages, rather than a clear vertical column test. Source descriptors carry identity, level and replenishment rate; receiving intervals store water volumes. Explicit outlets are supported by the core model. Missing/unknown region boundaries are not treated as drains.

`LocalWaterSimulation` advances on fixed 0.1-second steps. Transfer is limited by opening, head difference, available water and receiving capacity. Full roofed intervals transmit upstream head approximately, allowing water to pass through a low passage into a higher receiving room. This is a gameplay approximation, not a velocity-field fluid solver.

Natural fixture sources replenish toward their local levels at bounded rates. Water follows connected lower terrain over time. Digging remaps existing stored water into the expanded space, rather than creating water in every new hole. Older terrain-only saves initialise the original natural basins, then remap that water into the edited terrain. A legacy excavation therefore does not trigger an instantaneous fill of its entire newly connected space.

Terrain and water preparation stay on bounded background workers. Simulation workers copy the last committed water state; stale revisions cannot replace a newer result. Rendering interpolates the coarse water levels and clips them against the displayed terrain. Water querying/storage remains a coarse column representation, so fine contact/underwater gameplay adapters still require validation.

## Test it

Restart `TEST_CAVE_LAB.cmd`. Press Tab for the buttons.

1. Use **Dig outlet**, then **Outlet view**. This cuts a continuous channel from the hilltop lake down the slope. The water front advances over time; allow roughly a minute to reach the lower hillside.
2. Use **F4** to pause/resume water for inspection.
3. Dig cave walls and floors normally. Compare **F6** at 1 m and 0.5 m. Narrow terrain ridges can remain real geometry; raising resolution is not required to repair a shoreline mismatch.
4. Use **F5** to save terrain and water progression; **F9** reloads both and returns to the entrance. Opening the lab starts a fresh test, and closing does not autosave.

The normal save and portable release are not migrated by this experiment.

## Evidence

- Core suite: **1,895,509 assertions passed**. New coverage includes continuous stroke persistence, bounded/order-independent brush blending, actual shoreline coverage, water/terrain triangle agreement, gradual downhill advance, opening-dependent flow, dry sealed spaces, roofed transfer, conserved storage, closing a connection, repeated worker copies and saved reconstruction.
- Game build: **0 warnings / 0 errors**.
- `cave-floor-connected.log`: saved player floor strokes plus sixteen additional floor cuts, rendered after water has advanced. The saved player reproduction contained four cuts; it is not the exact twenty-cut screenshot state. Capture: `cave-floor-connected.png`.
- `cave-flow.log`: actual twenty-stroke hilltop outlet; downstream water reaches z >= 14 at a surface over two metres below the lake; the unbreached chamber below the lake remains dry; saved storage is restored into a newly built topology. The harness accelerates simulation steps for testing. See `cave-flow-metrics.json` for simulation ticks and wall time separately.
- `cave-feedback.log`: overlapping digs, continued movement, retained collision, stale work rejection and shoreline replacement. Its 0.5 m edit batch remains background work; brief engine mesh/collider commits are still possible.
- `cave-smoke.log`: walking into/out of the cave, ceiling collision, excavation, atomic save/reload, fresh regions, resolution/backend comparisons and hybrid interface traversal.
- Normal gameplay regression: all existing smoke markers passed, exit 0, empty error log (`cave-connected-baseline.log`). Final floor, flow and cave traversal runs also exited 0 with empty error logs. Both final floor/flow captures were visually inspected.

## Remaining production gates

This milestone addresses the reported geometric disagreement and demonstrates connected downhill water. It does not establish continent-scale hydrology performance.

- The hydrology domain is a finite 28 x 52 m patch with one-metre storage columns and quarter-metre vertical passage sampling. Sub-cell cracks and complicated thin barriers need finer/adaptive validation. Source strengths and the hill/cave are authored diagnostic content.
- Surface interpolation is a presentation approximation of stored column volume. Swimming, buoyancy, drowning and underwater rendering are not implemented here.
- Work is bounded and asynchronous, but simulation time can lag under worker saturation. There is no offline catch-up, multiplayer replication or handoff between streamed hydrology regions yet.
- Unknown outer boundaries stay closed. Production needs persistent connections/outlets to neighbouring regions instead of treating unloaded terrain as an ocean or sink.
- Filling terrain into stored water is intentionally rejected when it would discard volume. A displacement/overflow policy must precede player fill/dam integration. Closing a connection without displacing its stored volume is tested in the core.
- Indexed/smaller dirty terrain blocks, strict commit/memory budgets, dynamic volume promotion, world manifests/regional storage, macro geography/climate/biomes and natural cave generation remain subsequent foundation work.

No Post-Master inventory/tool/crafting expansion or normal-game camera/controller replacement was introduced.


## Shoreline and polygon feedback follow-up (2026-09-28)

The next playtest reported small water defects, jagged edges and possibly missing/scrambled terrain polygons. The available save contained six strokes, while the screenshot HUD showed seven. A read-only copy is retained as `Tests/Fixtures/cave-player-shore-cuts-v2.json`; the original save was not changed. The unsaved seventh stroke and exact camera pose are not known.

Two reproducible implementation faults were corrected:

- Terrain used an analytic normal normally, but replaced it with each individual face normal when they disagreed. The same vertex could therefore have several different lighting normals. The mesher now gathers incident faces with a one-cell border halo. Every copy uses the same angle-weighted normal from the actual surface. Region-border copies agree as well. Vertex positions, winding, collision and excavation/save interpretation are unchanged. No sampling-resolution increase was introduced.
- The water renderer made full cell-sized patches, which could end in a raised rectangular shelf at a dry receiver. Surface nodes now sit at storage centres and taper toward a dry receiving bed. Shared bank nodes are selected deterministically, and surfaces are still clipped against the terrain tetrahedra. This is presentation interpolation; the underlying stored quantities and flow simulation are unchanged.

Water vertex displacement was removed because independently clipped edges contain T-junctions that nonlinear displacement can open into cracks. Moving fragment normals/highlights still provide ripples. The final shallow film fades into the bed instead of retaining a hard, partially opaque polygon outline.

Validation: **2,970,446 core assertions passed**, including closed/consistently wound meshes for the six saved cuts and a synthetic seventh connecting cut across four regions at 1 m and 0.5 m. Shared-normal checks also cover the existing 20/80-cut stress cases. Water coverage, terrain clipping, coplanar-overlap and advancing-front tests pass. Restoring the old terrain mesher makes the shared-normal test fail; restoring the old water renderer makes the dry-front taper test fail. These intentionally failing controls are in `artifacts/shore-negative-VolumeMesher.cs.txt` and `shore-negative-LocalWaterMesher.cs.txt`.

The game builds with zero warnings/errors. Final digging feedback smoke passed: 29 cuts, continued physics during preparation, retained collision and stale-job rejection. The measured 0.5 m batch took 200.7 ms in background preparation, with a maximum terrain commit of 32.6 ms; brief frame hitches remain possible. Evidence: `artifacts/cave-edge-feedback*`. Downhill flow/save reconstruction passes in `cave-edge-flow*`; saved shoreline rendering uses `cave-shore-fixed*`. The traversal/collision/backend scenario passed in `cave-edge-traversal*`.

Limits: these checks found no open interior mesh edges in the saved reproduction or tested stress cases; they do not prove that every possible excavation is hole-free. Thin rock left between cuts and angular silhouettes at the selected terrain spacing can still be real geometry. Water remains the bounded coarse prototype described above. This is a cave-lab fix, not a change to the normal valley or a completed Master Rework.



A lighting comparison was also captured with shadows temporarily disabled (`cave-shore-shadow-diagnostic.png`). It did not establish missing triangles; the normal cave shadow settings were restored. The final saved-cut capture is `cave-shore-fixed.png`, visually inspected with the final shared geometric normals, tapering water front and shallow-film fading.

## Known issue to verify next time local hydrology resumes

User follow-up (2026-09-28): water may cross a visible ridge/lip between spherical excavations. Do not treat shoreline shading changes as a connectivity fix. Code inspection found that `LocalWaterSimulation.ForCave` fixes terrain sampling at 0.5 m, while visible terrain/water clipping can use the selected 1 m or 0.5 m spacing. `LocalWaterTopology.Build` checks nine horizontal points at vertical-band midpoints (0.25 m bands); it then assigns the entire band to a connection. These are candidate sources of disagreement, not a confirmed diagnosis.

Required regression: thin intact ridge, barely breached ridge, low ceiling and overlapping spherical cuts, at both terrain resolutions and across a region boundary. Compare each transfer opening/sill/ceiling against the actual piecewise-linear tetrahedral surface and collider, including points below the sampled midpoint; assert no transfer before the real spill elevation and conserved storage afterward. Distinguish topology, mesh, clipping and resolution defects. Keep this bounded spike ahead of linking generated rivers to local simulation. Current macro milestone was completed first as requested.
