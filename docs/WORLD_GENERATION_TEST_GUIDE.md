# World generation preview

Start **TEST_WORLD_GENERATION.cmd**. This opens the isolated macro map, not a replacement for the current playable valley. The default Vast world preset is **2,048 x 2,048 km**.

- New macro worlds default to **Algorithm 4 (accepted)**. Choose **Algorithm 3 (preserved)** to inspect historical terrain. Choose a seed/profile, then **Generate**. Five JSON profiles cover mainland, several continents, island seas, mountain-heavy and vast worlds.
- Click the map to inspect real metre coordinates, elevation and chunk address. **Zoom in** centres on the selected point. **Zoom out** and **Whole world** restore context; WASD/arrows pan.
- Switch between **Elevation**, **Land ownership** and **Mountain ranges**. Elevation colours are not biome assignments.
- **Save world** creates an immutable preview manifest. **Load world** reads the slot for the seed/profile selected in the controls. Saved geography uses its embedded profile, even after catalogue tuning. Existing slots are never overwritten: change the seed/profile to create another world.
- Algorithm 4 preview saves are under `%APPDATA%/Thornwake/macro-world-previews/algorithm-4/<profile>/<seed>/manifest.json`. Algorithm 3 retains `macro-world-previews/<profile>/<seed>/manifest.json`; choose its algorithm/seed/profile before Load. They never replace `world.json` or the cave test save.

Large worlds are sampled by address. The map is only a bounded overview; zooming rerenders the same geography. The core chunk test keeps 49 nearby regions and builds at most two per planner step while jumping between continents. This does not certify production streaming performance: the new 3D renderer, distant LOD, physics, entity loading, region saves, generated rivers, climate, biomes and natural caves remain to be integrated.

Current geography is a prototype: landforms use bounded seeded shapes, irregular coasts, grouped islands, mountain backbones and broad relief. Drainage-aware valleys/erosion and finer geological variety come next. Small islands/straits can disappear at overview resolution; the reported component count depends on sampling.

## Drainage and regional diagnostics (2026-09-29)

The updated source build is ready through the same **TEST_WORLD_GENERATION.cmd** launcher. Close any older running preview first.

1. Choose **Drainage routes**, **Catchments** or **Basin candidates** in the layer menu. Yellow links show major coarse routes; cyan marks receiving ocean cells; purple/pink show basin candidates/spill cells. Click the map to inspect its coarse metadata; zoom in to see direction arrows and coarse-resolution artifacts.
2. Select a land/basin cell, then **Refine selected cell**, or choose **Regional refinement**. Green markers are incoming ports and cyan is the downstream handoff. Yellow/red lines show a proposed terrain-sampled corridor and bed rises. Purple cells are candidate lake interiors. These are development diagnostics, not rendered water or carved riverbeds.
3. Regional inspection shows one fixed tile. WASD/arrows move to neighbouring tiles; **Whole world** returns to the coarse routes. Scroll the sidebar for selection and candidate details. World/layer switches keep the previous image until the new worker result is ready.
4. **Save world / Load world** retain the existing immutable macro manifest behavior. Regional diagnostics reconstruct from that world; the lab does not save fluid state or migrate the normal game. A future production world must explicitly pin the additional refinement recipe/version.

Focused checks: `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --drainage-preview` and `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --regional-drainage`. The latter writes `artifacts/regional-drainage.json`.

The vast-world view refines 16 km cells at 250 m sample spacing, so paths and shores remain visibly grid-shaped. Bed-rise diagnostics are not water-flow predictions, and lake storage is an estimate at the coarse candidate level. See [MASTER_A4_HYDROLOGY_ACCEPTANCE.md](MASTER_A4_HYDROLOGY_ACCEPTANCE.md) for current validation, finer inspection results and the exact next step. The additional engine-free inspector is exercised with `--hydrology-inspection`; the lab still shows the original regional proposals. The existing cave water/terrain issue remains open.

## Multi-patch reconciliation developer report

Run `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --reconciliation`. Read `artifacts/hydrology-reconciliation/report.json` for connected components, explicit unresolved frontiers, reachable spill proposals and adaptive profiles. The Godot preview is unchanged; this new layer has no gameplay-water activation. See [the reconciliation checkpoint](MASTER_HYDROLOGY_RECONCILIATION.md) for result-state meanings, limitations and the next V1 step.

## Adaptive topology and continuation report

Run `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --adaptive-topology`. The separate algorithm-2 report is `artifacts/hydrology-topology/report.json`; it includes corrected components/spills, refinement evidence, explicit stop reasons and serializable replay continuations. Use `--topology-analytic` for a quick analytic-only pass. This does not activate gameplay water or change the Godot preview. See [the adaptive topology checkpoint](MASTER_HYDROLOGY_ADAPTIVE_TOPOLOGY.md) for supported semantics, measured work and remaining limits.

## Bounded-work hydrology report

Use `--demand-work` for the focused demand/balance checks, `--demand-topology` for topology algorithm 3, and `--hydrology-work` for the 24-query before/after coverage matrix. Run them with the Core test project shown above. Algorithm 2 remains available through `--adaptive-topology`; neither path activates gameplay water. The comparison is a multi-minute Debug developer test. See [the bounded-work checkpoint](MASTER_HYDROLOGY_BOUNDED_WORK.md) for measurements, unresolved cases and the next V1 step.

## Local graph-reuse diagnostics

Use `--graph-reuse` for focused invalidation/full-restart comparisons and `--graph-comparison` for the six-candidate timed matrix. Both use the Core test project above; the latter takes several minutes. Read `artifacts/hydrology-graph/comparison.json` and [the graph-reuse checkpoint](MASTER_HYDROLOGY_GRAPH_REUSE.md) for the exact dependency proof, gains and unchanged unresolved coverage. This is generation tooling; it adds no gameplay water or preview UI.

## Rejected reachability experiment (2026-10-03)

The explicit topology-4 reachability experiment is archived and is not an active algorithm or preview option. Production remains unchanged. See [the experiment checkpoint](MASTER_HYDROLOGY_REACHABILITY_EXPERIMENT.md) for its corrected closure regression, benchmark matrix and disposable-checkout reproduction instructions. Do not confuse this rejected topology version with accepted terrain algorithm 4.

## Read-only correction-locality report

Use `--locality` for focused closure/boundary/cap/determinism checks and `--locality-matrix` for six frozen algorithm-3 candidates with independent controls. Reports are under `artifacts/hydrology-locality/`. These observations do not retain component membership or alter graph visits. See [the locality assessment](MASTER_HYDROLOGY_LOCALITY_ASSESSMENT.md) for the indexed-memory estimate, limitations, validation and next V1 step. No preview UI or gameplay-water behavior changes.

## Unresolved-spill attribution report

Use `--spill-attribution` for focused binding/path/limit/equality checks and `--spill-attribution-matrix` for the six frozen candidates. Reports are under `artifacts/hydrology-spill-attribution/`; the full matrix retains repeated and discarded-pass evidence and is deliberately large. See [the attribution checkpoint](MASTER_HYDROLOGY_SPILL_ATTRIBUTION.md) and its compact/lossless archives for domain, sampling and work blockers. This is read-only generation tooling and changes no gameplay water or preview UI.

## Experimental receiver-aware topology 5

Use `--receiver-domain` for the separate frozen analytic cases and `--receiver-matrix` for six real candidates compared with frozen algorithm 3 and clean neighbour traversal. Reports are under `artifacts/hydrology-receivers/`. See [the receiver-domain checkpoint](MASTER_HYDROLOGY_RECEIVER_DOMAIN.md) for the whole-patch ocean proof, explicit terminal-boundary semantics, authoritative final-frontier reporting, measured remaining limits and next V1 step. Version 5 is opt-in and is not promoted to production; the preview and gameplay water remain unchanged.

For the current development order and version registry, follow [CURRENT_WORLD_GENERATION_STATUS.md](CURRENT_WORLD_GENERATION_STATUS.md). Historical checkpoint next-step suggestions are superseded by that active index.
