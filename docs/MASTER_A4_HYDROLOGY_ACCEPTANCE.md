# Accepted Algorithm 4 and bounded regional hydrology inspection

2026-09-30 completed review checkpoint. The user formally accepted Algorithm 4 mountain morphology. Master Rework + Supplement V1 remain authoritative; V2 is not active. This supersedes the awaiting-review stop in the mountain experiment and the next-step section of the original regional-drainage checkpoint. Historical measurements remain in those documents.

> **Continuation checkpoint:** [Bounded multi-patch reconciliation](MASTER_HYDROLOGY_RECONCILIATION.md) now records the next implemented V1 layer, validation and resume point. The acceptance results and original next-step guidance below remain historical evidence.

## Terrain acceptance and version policy

Algorithm 4 is the default for **new macro Master-Rework worlds**. Algorithm 3 remains supported with its original sampler, explicit manifest version, save paths and frozen outputs. Loading a saved world never substitutes the current default. The normal playable valley and cave saves are unchanged. `ExperimentalAlgorithm` remains a historical API alias for accepted version 4; it does not mean acceptance is pending.

No terrain sampler or profile was changed in this milestone. Before promotion, a fresh 20-pair survey (five presets; seeds -9, 0, 42, 7319) reproduced the previously reviewed A3/A4 outputs: all 120 PNGs and 40 sampled heightfields matched byte-for-byte. Representative Mountainous/Vast World elevation grids and oblique sampled surfaces were visually inspected. This confirms preservation of the user-accepted morphology; numerical checks are not a new aesthetic approval for every seed.

Dedicated fixture files freeze embedded profiles and explicit versions:

| Fixture | Coverage |
| --- | --- |
| `Tests/Fixtures/terrain-a4-v1.json` | 22 worlds: all five presets x four seeds, plus Mountainous/42 and Vast World/7319 at far origins. Hash format 1 includes descriptors, world/mainland samples, every belt's knot cross-sections and exact/adjacent 16 m chunk seams. |
| `Tests/Fixtures/drainage-a4-v1.json` | 22 separate A4 coarse fingerprints at 128-cell resolution, basin/outlet counts and 158 regional tile fingerprints at 64 subdivisions. Includes major catchments, large basins and downstream neighbours. |
| `Tests/Fixtures/hydrology-inspection-v1.json` | Six inspection outputs and three adjacent lake-pair outputs over Mountainous/42, its far-origin copy, and Vast World/7319 at a far origin. |

A read-only inventory of the frozen terrain matrix found 86 mainland descriptors, 180 belts and 368 pass depressions, including 17 primary broken-range styles, 8 primary massif styles and 30 deep interruptions across main/secondary belts. Secondary belts and parallel-style cases are represented. The terrain sampling hashes cover their generated fields; style counts are coverage evidence, not new production metadata.

The existing A3 coarse fixture (15 worlds), regional fixture (23 tiles), terrain expectations and accepted A4 sampler are unchanged. Old tests now explicitly request A3 where they previously relied on the default. New A4 fixtures were recorded separately, then reconstructed and checked before default promotion. Record commands refuse existing files. Never overwrite historical expectations to accommodate a later generator change.

Terrain algorithm, complete world/profile identity, coarse fingerprint and regional recipe remain explicit dependencies. Mixed A3/A4 checkpoint reconstruction is rejected in both directions. A4 drainage and regional views are now enabled in the macro lab; its algorithm selector says accepted/preserved. Save locations retain their existing version separation.

## Regional inspection implemented

`RegionalHydrologyInspection.cs` adds inspection algorithm 1 over the immutable regional proposal layer. The recipe pins its format/algorithm, factor and complete regional dependency. It has no fluid quantities, terrain edits or automatic production-world activation.

- **Finer sill diagnostics:** canonical global-lattice face samples at 2x or 4x the regional density. Default is 129 x 129 vertices per inspected tile; maximum is 257 x 257. The original shared port remains unchanged. Reports include the finer face minimum and a minimax escape height from the tile's lowest interior sample to its explicitly declared downstream face, using the existing validated flood algorithm. The reachable crossing can differ from the face minimum. This is a bounded local diagnostic, not a reconciled whole-basin spill.
- **Sampled corridor assessment:** finer real-terrain samples distinguish descending bed, bed entirely below a hypothetical candidate stage, and a sampled terrain barrier. Below-stage classification is conditional on future connected storage; it does not assert that a lake exists or is filled.
- **Guarded corner proposals:** deterministic pairs of source links can form quadratic corner profiles. Junctions and handoff anchors remain exact; source links are retained once, including shared trunks. A corner is accepted only if sampled maximum bed/rise does not worsen and an unblocked profile does not become blocked. Unsafe shortcuts retain the original proposal. Barriers remain visible rather than being carved away.
- **Adjacent candidate lake connectivity:** pairs use identical fine shared-face terrain and the same candidate stage. Conservative all-corners-below-stage quads form face-connected components. Only actual shared wet faces join components; corner contact and dry sills do not. Unresolved wet external boundaries are flagged, excluding the inspected shared face. Canonical pair ordering preserves identity and reconstruction.

These additions inspect the existing routing skeleton without rewriting coarse drainage or regional algorithm 1. A candidate lake's parent identity does not certify that all its components communicate. No independent river sources, rate values or infinite lakes were added. The V1 policy remains finite lakes/pools with explicit inflow/outflow, rate-limited rivers and permitted effectively infinite ocean boundaries.

The new inspection is an engine-free developer API and reproducible test report, not an extra in-game editor. The lab continues to display the original regional proposals, now over either accepted A4 or preserved A3 terrain. It does not display the new inspected corner profiles yet.

## Acceptance scenarios and observations

Focused scenarios cover continuous downhill flow intent, a fine ridge missed by coarse endpoints, submerged versus exposed bed rises, refined spill 5 versus coarse candidate 3, three merging tributaries, exact neighbouring continuation, two disconnected candidate pools, stage-dependent merging, east/west and north/south barriers, corner-only contact, dry patches, far origins, and A4 foothill/pass fields. Recipes reconstruct identically; caller order and concurrent queries do not change output. Invalid versions, mixed terrain, excessive budgets, mismatched shared terrain and cancellation are checked.

Actual sampled examples from the fresh diagnostic report:

| A4 case | Fine spacing | Face minimum | Local escape sill | Coarse candidate stage |
| --- | ---: | ---: | ---: | ---: |
| Mountainous/42 foothill tile 10136 | 46.875 m | 676.25 m | 678.41 m | none |
| Mountainous/42 basin-outlet tile 10780 | 46.875 m | 249.50 m | 273.84 m | 316.79 m |
| Vast World/7319 far-origin foothill tile 6733 | 125 m | 1131.40 m | 1150.39 m | none |
| Vast World/7319 far-origin basin-outlet tile 13254 | 125 m | 173.73 m | 180.30 m | 325.09 m |

The Vast World foothill output retains 12 barrier-classified segments among 25 proposals; its basin-outlet output retains another exposed barrier. These contradictions are reported, not forced through terrain. The inspected real lake pairs join two local components into one at their candidate stage, but all still have unresolved external boundaries. Synthetic fixtures establish multi-component behaviour. Near/far reconstruction is pinned separately; translating a world need not yield bitwise-identical floating-point heights.

Fresh face-profile and corridor plots were inspected in `artifacts/a4-acceptance/inspection-mountainous_42.png` and `inspection-vast-world_7319_far.png`. They show remaining long cardinal sections as well as accepted corner rounding. These are not natural final river shapes. Generated review evidence stays local under `artifacts/`, as established by the Git workflow.

## Validation

| Check | Result |
| --- | --- |
| Focused accepted-A4 suite | 1,098,592 checks; 22 terrains/coarse worlds and 158 regional tiles |
| Focused hydrology inspection | 2,431 checks; all nine new output fingerprints retained |
| A3 coarse / regional regression | 300,985 / 41,607 checks; all old fixture bytes and expectations preserved |
| Full Core suite | **4,685,909 assertions passed** |
| `dotnet build Thornwake.csproj --no-restore` | **0 warnings, 0 errors** |
| Rendered default-A4 Mountainous lab | Drainage/regional smoke passed; save/load restored explicit A4 after selector changes |
| Rendered explicit-A3 Archipelago lab | Drainage/regional smoke passed; preserved version restored after selector changes |

Engine scenarios include layer/world switches, selected-cell restoration, exact saved manifests, zoom and stale-worker rejection. Error logs are empty. Artifacts: `artifacts/a4-acceptance/full-core.log`, `game-build.log`, `default-a4.*`, `preserved-a3.*`, `hydrology-inspection.json`, `morphology-coverage.json` and `terrain-survey/`.

The six measured default inspections took approximately **180-253 ms per tile** in the final Debug Core run, including regional proposal generation and finer inspection, excluding initial coarse/refiner construction and rendering. They use 16,641 fine grid samples when a tile has an outgoing face. Work is bounded per tile/pair; no world-wide fine grid or unbounded cache is introduced. These observations are not a production streaming budget.

Reproduce focused reports with `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --accepted-a4` and `--hydrology-inspection`; normal runs verify fixtures without rewriting them. Run `TEST_WORLD_GENERATION.cmd` for accepted A4 terrain/drainage views; choose Algorithm 3 to inspect historical worlds.

## Remaining limits and exact next V1 step

Stop here for review of the bounded inspection layer. Do **not** treat its local low-point escape height as the final lake spill, relocate ports unilaterally, fill coarse candidate basins, or connect it to playable water.

Next implement **bounded multi-patch basin/outlet reconciliation** on the accepted A4 fixtures. Follow candidate components through neighbouring patches; preserve explicit unresolved continuations when a budget ends. Compare actual connected spill paths, reconcile both sides of any proposed port change under a new output version, and refine corridor samples adaptively around exposed barriers/sills. Pin fixtures for complete versus truncated component discovery and agreed cross-patch outlets before promoting any reconciled result. Existing coarse/regional/inspection fingerprints remain historical baselines.

Current limitations: fixed-factor samples may miss narrower ridges/openings; corner smoothing does not remove all cardinal-grid bias; a two-patch union does not solve a whole lake; the local minimum used for the spill diagnostic may belong to a different component from an incoming route; no final stage, storage curve, discharge, shoreline or channel geometry is established. Cross-platform bitwise equality is not yet certified.

After reconciliation, define finite stage/storage and explicit upstream rate-limited inflow/outflow descriptors. The known cave water/terrain connectivity and visual issue remains open; its thin-ridge/narrow-opening/ceiling/border regression is still required before a separate local realization spike. No V2, climate, biomes, scatter, materials, populations, content editors, final water rendering or Post-Master gameplay was started.
