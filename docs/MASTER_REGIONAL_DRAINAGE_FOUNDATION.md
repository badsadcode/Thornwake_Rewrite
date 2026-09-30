# C2: drainage diagnostics and first regional refinement foundation

2026-09-29. Completed stop checkpoint. The Master Rework Supplement v1 overrides conflicting policy in the older Master Plan and historical notes. This milestone stops before climate, biomes and local-water realization.

## What is implemented

The existing macro lab now has **Drainage routes**, **Catchments**, **Basin candidates** and **Regional refinement** views. The first three overlay unchanged algorithm-3 terrain. Routes show accumulation and major downstream links; ocean markers identify receiving sampled outlets; basin candidates show spill cells. Selection exposes bed versus routing spill, contributing area, upstream neighbours, next cell, terminal outlet, basin membership and candidate storage. Colours and lines are diagnostic metadata, not final terrain, rivers or lakes.

**Refine selected cell** inspects one coarse cell on a denser terrain grid. The view marks proposed corridors, sampled uphill bed segments, inlet/continuation ports and candidate lake interiors. It shows network order, destination, sample spacing, a hypothetical downhill-channel cut estimate and lake-patch statistics. Regional arrows/WASD move one tile; Whole world returns to coarse diagnostics. Regional inspection is deliberately fixed to one tile; it is not a giant editor.

The lab retains one background worker, cancellation, revision checks and the old displayed image during preparation. Coarse data/refiner caches are matched to the exact frozen world. Pending or stale worlds cannot replace the current view. World/layer switching and saved-manifest reload are exercised by the engine scenario. Normal gameplay and player saves are untouched.

## Regional architecture

`Game/Core/RegionalDrainage.cs` adds an opt-in, engine-free layer over the existing `MacroDrainageRecipe`. It neither changes the coarse solver nor writes elevations back to the macro sampler.

- `RegionalDrainageRecipe` freezes its format, refinement algorithm, subdivision budget, complete coarse recipe and expected coarse fingerprint. Restore validates/rebuilds that dependency and rejects unsupported versions or mismatches. JSON round-trip reconstruction is tested. The lab's Save world button still saves the existing macro manifest; it does not save fluid state or a new production-world format. A future production world must explicitly persist this additional recipe/version before depending on it.
- A regional solve covers **one coarse cell**, not one 16 m render chunk. The lab uses a 128 x 128 macro grid and 64 subdivisions per cell: 65 x 65 sampled vertices, 4,225 heights per request. Supported subdivision budgets are 16, 32, 64 and 128; the maximum has 16,641 vertices. No entire-world fine grid or growing tile cache is allocated.
- Each directed coarse edge gets one canonical boundary port: the lowest sampled interior point of its common face, with stable sample-order tie breaking. Port identity and position depend on the frozen layer and directed edge, not the requesting tile. Global lattice arithmetic gives identical shared vertices at far origins. Non-port borders are closed to routing. Only an explicit coarse ocean outlet is terminal.
- A bounded Dijkstra search connects incoming ports to the outgoing port, or to the ocean terminal sample. Positive costs favour shorter, lower-terrain paths and penalize downstream bed climbs. The versioned formula is distance multiplied by `1 + 4 * heightAboveTileMinimum / tileSpan`, plus `16 * positiveDownstreamBedRise`. It is a corridor proposal, not a hydraulic solve. A tile with no upstream branch may have a headwater candidate origin; this does not inject water.
- Tributary paths share one deduplicated directed link graph. Shared trunks are represented once. `CorridorsForRegion` clips these canonical links to 16 m render regions, retaining link identity and upstream/downstream continuation flags. Clipping creates neither a new source nor a drain. Border-aligned segments have deterministic ownership.
- Metadata includes upstream ports, downstream reach identity, terminal outlet, Strahler order and a relative headwater/tributary/mainstem category. This is flow potential, not calibrated discharge in cubic metres per second. River IDs currently identify regional reaches, not a final named river spanning the whole network.
- A coarse basin can produce several regional lake patches under one stable candidate-lake identity. Four corner samples must all lie below the coarse candidate spill to classify an interior cell; mixed cells are explicitly unresolved shoreline. Separate face-connected components are retained. Patch area/capacity estimates are finite and patch interiors do not overlap between tiles. Inlets/outlets distinguish external basin connections from internal continuation; ports at/above the candidate surface are flagged as blocked. Patch connections are not a whole-lake hydraulic model.

There is no independent infinite river source per region. There are no rates or stored fluid quantities yet. The future policy remains finite lake/pond storage plus explicit inflow/outflow, rate-limited rivers/springs, and effectively infinite ocean boundaries where appropriate. Explicit ports and continuation IDs prepare that separation without connecting the local water solver.

## What remains approximate

At 2,048 km world width, a 128-cell skeleton has **16 km cells** and the default refinement samples every **250 m**. The archipelago preset uses 4 km cells and 62.5 m samples. This is a meaningful regional tier, but still not metre-scale river geometry. Paths have cardinal-grid bias and may contain long straight segments. They must be refined further before final channels are realized; the diagnostic lines must never be substituted for finished river meshes.

Canonical minimum-sill ports are deterministic proposals, not proof that the chosen crossing is the correct natural outlet. Fine terrain may contradict coarse routing. `MaximumUphillStep` and `DownhillCutEstimate` expose sampled bed rises; they do not automatically carve terrain. In particular, a rise in a submerged lakebed does not by itself block surface flow or require flattening. Unsampled barriers between vertices remain possible.

Lake patches retain the **coarse candidate spill level**, not a validated final lake level. Capacity estimates use sampled quad means and the all-corners criterion; they are not final bathymetry or currently stored water. Separate components are not silently assumed to communicate. Final spill reconciliation, shoreline tracing, cross-patch component resolution, stage/storage curves and actual inflow/outflow rates remain open. The parent candidate ID intentionally does not certify physical connectivity.

All identities are scoped to a frozen refinement layer. Resolution/input/algorithm changes intentionally create another layer; production migration still needs a deliberate policy. Cross-platform bitwise equality has not been established. Existing frozen world outputs remain unchanged on the tested Windows runtime.

The known cave lab water/terrain consistency issue remains open. It did not block this isolated metadata milestone and was not altered. The targeted thin-ridge/narrow-opening/ceiling/border regression is still a prerequisite before production macro/local water coupling. No climate, biomes, geology, ore, content editors, weather/ocean rendering, sailing or Post-Master gameplay was started.

## Validation and observations

| Check | Result |
| --- | --- |
| Focused diagnostic raster tests | 28 checks: reconstruction, unchanged terrain, bounded extreme zoom and cancellation |
| Focused regional tests | 41,607 checks: deterministic/culture-independent/concurrent reconstruction, all shared face heights, canonical ports, exact route endpoints, 16 m chunk continuity, unique shared trunks, tributary order, basin links, finite patch estimates, disconnected components, unsupported inputs and cancellation |
| Original coarse fixtures | All 15 fingerprints preserved; coarse drainage suite remains 300,985 checks |
| New regional fixtures | 23 fixed tile hashes, including path/link topology and lake metadata, pinned in `Tests/Fixtures/regional-drainage-v1.json` using frozen profiles |
| Full core suite | **3,375,521 assertions passed** |
| Game build | **Zero warnings/errors** |
| Rendered vast-world and archipelago scenarios | Both exited 0 with `DRAINAGE_LAB_PASS` and `REGIONAL_LAB_PASS`; error logs empty; captures visually inspected |

Final engine evidence: `artifacts/regional-final-vast-world-20260929-233310.*` and `artifacts/regional-final-archipelago-20260929-233310.*`. The scenarios test zoom/stale-worker rejection, drainage/basin/catchment layers, location selection, regional inspection, a different generated world, saved-manifest reload and restoration of the selected regional cell. The earlier coarse overview capture is `artifacts/drainage-preview-20260929-231527.png`.

`artifacts/regional-drainage.json` records the final focused run. On this machine, building 11 vast-world tiles took about **699 ms** total (~64 ms/tile); 12 archipelago tiles took about **484 ms** (~40 ms/tile). These timings exclude initial coarse analysis, map rasterization and validation. The fixture paths exposed 13 and 17 routes with sampled bed rises, respectively; these are retained diagnostics, not hidden errors or automatic terrain modifications.

The 320 x 320 regional preview raster dominates the displayed preparation time: inspected warm views took roughly **1.45 s** and **1.07 s**, off the main thread. A prior 640 x 640 regional view took ~5.8 s, so the regional diagnostic raster was reduced without reducing refinement density. Coarse overview views retain 640 x 640 pixels. These are development-machine observations, not production frame/streaming budgets. No new normal-game rendered smoke was needed for this isolated lab/core layer; existing gameplay/save regressions ran in the full core suite.

> **2026-09-30 inspection update:** the [mountain-quality audit](MASTER_MOUNTAIN_QUALITY_AUDIT.md) found repeated raised-band geometry in actual macro elevation across five presets. It recommends a version-preserving mountain-quality decision before the hydrology step below. No generator changes were made; current frozen worlds and refinement remain intact.

> **2026-09-30 mountain experiment checkpoint:** [Algorithm 4 is implemented as an opt-in review candidate](MASTER_MOUNTAIN_V4_EXPERIMENT.md). Algorithm 3 remains default and its fixtures are unchanged. Stop for visual/design review; the hydrology work below is deferred until the terrain decision and separate accepted-version fixtures.

## Exact next recommended Master step

Start a **regional hydrology acceptance spike** over selected neighbouring candidate basins/reaches. Reconcile proposed ports with higher-resolution terrain: find true sill profiles, distinguish submerged lakebed rises from blocked channels, resolve connected lake components across patches, and refine the grid paths into bounded adaptive corridor profiles. Establish acceptance fixtures for a continuous downhill route, an explicitly blocked route, a basin with a revised spill, and several merging tributaries. Keep macro algorithm 3 and drainage algorithm 1 unchanged; version any regional-output changes and retain the reconstruction fixtures.

Then define finite lake stage/storage and upstream rate-limited inflow/outflow descriptors and prove the terrain/water connectivity regression before a separate local terrain/water realization spike. Do not silently activate this layer in the playable valley. Climate and weighted biome work remain later milestones. This task stops here as requested.


