# Supplement V1: bounded hydrology work / coverage checkpoint

Date: 2026-10-02. Starting reviewed commit: `cd546ee8ffbbd8450a3decbe2f7aa9ecd1a289d7` (adaptive topology algorithm 2). This continues the interrupted local implementation; accepted terrain, drainage, regional and inspection layers are unchanged. Supplement V1 remains authoritative.

## Decision and scope

Keep topology algorithm 2 available with its existing frozen outputs. Add **explicit topology algorithm 3** for demand-driven base-cell admission and bounded shared-face balancing. This is neither terrain algorithm 3 nor Supplement V2. No caller/default, Godot preview or gameplay system is switched automatically.

Inspection confirmed that the accepted topology fixture hashes the entire serialized result, including work metrics, partial cells, refinement evidence and continuation. Algorithm 3 also gives `CompletedPatches` a stricter meaning: every base root in the patch is materialized. Different bounded traversal can expose different unresolved partial results. These are deliberately separate versioned results, not replacements for algorithm-2 expectations. Conservative connectivity, sampled maxima, minimax ordering, receiver levels and unresolved-result gates remain the same.

Changed files:

- Core: `Game/Core/AdaptiveBasinTopology.cs`, `AdaptiveHydrologyTopology.cs`, new `HydrologyDemandBalance.cs` and `HydrologyWorkProfile.cs`.
- Tests: `Tests/HydrologyTopologyTests.cs`, `Program.cs`, new `HydrologyDemandWorkTests.cs`, `HydrologyCoverageTests.cs`, and the two new fixtures under `Tests/Fixtures/` named below.
- Documentation: this checkpoint, its link from `docs/MASTER_HYDROLOGY_ADAPTIVE_TOPOLOGY.md`, and `docs/WORLD_GENERATION_TEST_GUIDE.md`.

## Implementation

Algorithm 2 still fully refines a patch before admission. Algorithm 3 registers bounded patch metadata, then refines an entire requested base root before exposing its leaves to either graph search. Unsampled roots remain unknown. Failed root construction retains charged samples/allocations and produces an explicit frontier; absence is never interpreted as dry, a wall, connected terrain or a drain. An explicit base-quad query can avoid almost all unrelated patch interiors. A default query still samples the entire seed patch to retain canonical seed selection; later neighbour roots are demand-driven.

When finer shared-face evidence invalidates a coarse leaf, the ordinary graph pass stops. A canonical queue then balances already materialized neighbouring leaves before a fresh graph pass. It visits reached refined roots and newly split leaves; it does not load new patches or admit unsampled roots. Balancing visits, samples, allocations and splits use the existing cumulative limits. Cancellation is checked throughout. No obsolete queue is certified, and balancing/pass exhaustion cannot produce a final component ID or spill. Resolution uncertainty is reobserved by the next normal search and remains explicit. Shared faces use positive-length overlap; corner contact remains disconnected.

A trial that continued the obsolete graph pass to collect more corrections consumed much more work before restart (large mountainous sampling rose to about 1.25 million; the Vast reference lost supported component coverage). It was not retained. The smaller local balance is bounded and easier to audit, but cannot discover evidence in roots the graph has not reached yet; repeated restarts remain a measured limitation.

`Profile` returns optional developer attribution separately from the frozen solver result. Its counters cover component patches/roots, roots outside graph visitation, sampled-dry and stage-straddling roots, shared-face samples/visits, spill-visited roots/samples, wet/spill visits, restarts and full/partial patches. Unique samples belong to the base root that first requested them. Attribution is bounded by allocated roots. Buckets overlap; they must not be summed as exclusive time costs. In particular, an unvisited or sampled-dry root can be a necessary boundary guard; it is not proof of wasted work or physically dry terrain. Root classification describes its probe range, not a continuous terrain proof. Profiling does not change result bytes.

## Representative before / after

Same Debug executable, machine, profiling and limits for both algorithms; timings are development observations, not frame budgets. Query timings exclude prebuilt coarse/regional/domain construction and fixture serialization. All runs use 32 started patches, 2,000,000 samples, 800,000 cells, 2,000,000 visits, 16,384 frontiers, 180,000 splits, 64 passes, depth 3, 2 m minimum spacing, 64 m maximum probe spacing and 0.25 m tolerance.

Selection is deterministic from accepted A4 fixtures: `tiny` is the median-capacity positive single-coarse-cell basin; `small` is the largest-capacity basin with at most four coarse cells (the previous reference); `medium` is the basin closest to 16 cells among those larger than four; `large` has the most cells. Coarse-cell count is not a bound on the selected fine component or its flooded footprint. Even single-cell candidates can require large fine searches.

In the table, patches are started/completely sampled; `W` means WorkBudget and `S` means SupportedAtSampleSpacing. Component/spill states are separate. Reasons list observed frontiers, not an assertion that all other uncertainty has been eliminated.

| World / size (seed patch) | Algo | Time s | Patches | Samples | Allocated cells | Splits | Visits | Passes | Component / spill | Frontier reasons |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| mountainous/42 / tiny (2992) | 2 | 12.84 | 32/30 | 2,000,000 | 498,379 | 9 | 322,403 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42 / tiny (2992) | 3 | 11.10 | 32/11 | 1,451,137 | 360,826 | 8 | 695,202 | 2 | W/W | patch-budget |
| mountainous/42 / small (4126) | 2 | 8.32 | 32/30 | 2,000,000 | 498,045 | 236 | 476,636 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42 / small (4126) | 3 | 15.40 | 32/28 | 1,890,433 | 470,444 | 59 | 2,000,000 | 5 | W/W | node-budget, patch-budget |
| mountainous/42 / medium (7244) | 2 | 12.56 | 32/30 | 2,000,000 | 498,018 | 205 | 454,142 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42 / medium (7244) | 3 | 17.17 | 32/24 | 1,636,435 | 407,007 | 72 | 2,000,000 | 6 | W/W | node-budget, patch-budget |
| mountainous/42 / large (10524) | 2 | 15.35 | 8/8 | 629,687 | 161,948 | 7,719 | 2,000,000 | 44 | W/W | node-budget |
| mountainous/42 / large (10524) | 3 | 12.54 | 8/1 | 314,857 | 79,263 | 1,784 | 2,000,000 | 43 | W/W | node-budget |
| mountainous/42/far / tiny (2992) | 2 | 12.61 | 32/30 | 2,000,000 | 498,379 | 9 | 322,403 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42/far / tiny (2992) | 3 | 11.05 | 32/11 | 1,451,137 | 360,826 | 8 | 695,202 | 2 | W/W | patch-budget |
| mountainous/42/far / small (4126) | 2 | 8.17 | 32/30 | 2,000,000 | 498,045 | 236 | 476,636 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42/far / small (4126) | 3 | 15.49 | 32/28 | 1,890,433 | 470,444 | 59 | 2,000,000 | 5 | W/W | node-budget, patch-budget |
| mountainous/42/far / medium (7244) | 2 | 12.54 | 32/30 | 2,000,000 | 498,018 | 205 | 454,142 | 1 | W/W | patch-budget, sample-budget |
| mountainous/42/far / medium (7244) | 3 | 17.10 | 32/24 | 1,636,435 | 407,007 | 72 | 2,000,000 | 6 | W/W | node-budget, patch-budget |
| mountainous/42/far / large (10524) | 2 | 15.27 | 8/8 | 629,687 | 161,948 | 7,719 | 2,000,000 | 44 | W/W | node-budget |
| mountainous/42/far / large (10524) | 3 | 12.60 | 8/1 | 314,857 | 79,263 | 1,784 | 2,000,000 | 43 | W/W | node-budget |
| vast-world/7319/far / tiny (10216) | 2 | 15.17 | 32/15 | 2,000,000 | 574,128 | 80,045 | 393,864 | 3 | W/W | patch-budget, sample-budget |
| vast-world/7319/far / tiny (10216) | 3 | 20.68 | 32/14 | 1,728,509 | 488,148 | 61,598 | 2,000,000 | 7 | W/W | node-budget, patch-budget |
| vast-world/7319/far / small (5919) | 2 | 16.16 | 32/18 | 2,000,000 | 549,129 | 61,042 | 384,099 | 2 | S/W | resolution-limit, sample-budget |
| vast-world/7319/far / small (5919) | 3 | 12.94 | 32/6 | 1,026,049 | 286,069 | 33,726 | 2,000,000 | 9 | S/W | node-budget, patch-budget, resolution-limit |
| vast-world/7319/far / medium (5579) | 2 | 10.68 | 32/30 | 2,000,000 | 498,165 | 461 | 466,874 | 1 | W/W | patch-budget, sample-budget |
| vast-world/7319/far / medium (5579) | 3 | 18.31 | 32/29 | 1,932,877 | 481,084 | 17 | 2,000,000 | 5 | W/W | node-budget, patch-budget |
| vast-world/7319/far / large (12869) | 2 | 15.29 | 11/11 | 759,191 | 190,232 | 2,502 | 2,000,000 | 45 | W/W | node-budget |
| vast-world/7319/far / large (12869) | 3 | 12.04 | 11/1 | 327,343 | 81,384 | 550 | 2,000,000 | 38 | W/W | node-budget |

### Interpretation and coverage

- Large mountainous sampling falls **629,687 -> 314,857 (50%)**, allocations 161,948 -> 79,263, and time 15.35 -> 12.54 s. Final partial wet cells rise 1,040 -> 13,147, but the search still exhausts two million visits after 43 passes. The far-origin counterpart has identical work counts.
- The Vast reference keeps its **48,604-cell supported component**, uses **2,000,000 -> 1,026,049 samples (49% less)**, 549,129 -> 286,069 allocated cells, and 16.16 -> 12.94 s. Spill ordering is still unresolved, including real resolution-limit evidence. No final component identity is issued.
- Vast large sampling falls **759,191 -> 327,343 (57%)**, allocations 190,232 -> 81,384, and 15.29 -> 12.04 s. It still reaches the visit cap after 38 passes. Its final partial component is smaller (55,234 -> 23,399 cells); partial traversal count is not monotonic coverage or completed geography.
- Broad mountainous small/medium and Vast tiny/medium candidates get slower despite fewer samples: for example mountainous small 8.32 -> 15.40 s and Vast medium 10.68 -> 18.31 s. New demand-time face evidence triggers repeated graph work. This is a targeted sampling improvement, not a universal speedup or a default promotion.
- **None of the 12 real candidates has a fully supported final spill under these limits.** The Vast reference component remains supported in both algorithms; the other component searches remain unresolved. Mountainous tiny stops at the patch bound; most other algorithm-3 cases hit visits as well. Unseen resolution issues cannot be ruled out behind these work frontiers. No candidate was forced to resolve.
- The enclosed analytic pool resolves with the same component and spill path using **1,089 -> 65 samples**. Barrier/opening, receiver and shared-face analytic cases remain supported. Cheap analytic resolution does not establish real-world lake readiness.

Attribution explains the benefit and remaining cost: outside-visited-root sampling falls 354,837 -> 25,691 for large mountainous and 532,373 -> 32,271 for large Vast. Sampled-dry-root samples fall 298,887 -> 12,129 and 330,501 -> 23,043 respectively. The Vast reference falls from 1,063,650 -> 37,839 samples outside visited roots; algorithm 3 attributes 988,210 samples to spill-visited roots, 437,436 visits to wet discovery and 1,562,564 to spill search. Balancing is charged within these visits (106,062 overlapping balance visits). Large candidates still spend the full two-million-visit budget with 42/37 restarts. More samples alone will not remove that bottleneck.

## Continuation decision

Keep format-1 recipe/query replay; no persistent mutable cache or serialized graph was added. Expanded continuations reconstruct deterministically and match fresh execution at the same limits, including work counts. Replay cost is measured by the separate initial/expanded matrix below. Initial time is the additional cost a staged caller pays before an expanded replay; expanded time includes production dependency restoration. It is not a direct benchmark of a hypothetical cache, whose invalidation/memory costs are unknown.

| Query | Initial seconds | Expanded replay seconds | Initial / expanded samples |
| --- | ---: | ---: | ---: |
| mountainous/42 | 1.44 | 14.49 | 300,000 / 1,890,433 |
| mountainous/42/far | 1.41 | 14.47 | 300,000 / 1,890,433 |
| vast-world/7319/far | 2.60 | 12.37 | 289,673 / 1,026,049 |


The within-query restart bottleneck is already substantial. A cross-call cache would not fix it and would add stale-topology/persistence complexity before that problem is settled. Defer reusable continuation state until bounded graph reuse is demonstrated with correct invalidation and measured end-to-end benefit.

## Validation

| Validation | Result |
| --- | --- |
| Focused preserved topology algorithm 2 | **2,035 checks / 30 unchanged cases** |
| Focused preserved reconciliation algorithm 1 | **7,348 checks / 22 unchanged cases** |
| New topology algorithm 3 | Candidate pass **5,245 checks**; full frozen verification **5,276 checks / 30 cases** |
| Focused demand/balance | **19 checks**, repeated in the full suite |
| Before/after coverage | Candidate pass **72 checks**; frozen repeat **73 checks / 24 queries** |
| Accepted A4 terrain/coarse/regional | **1,098,592 checks**, frozen fingerprints unchanged |
| Inspection / coarse drainage / regional drainage | **2,431 / 300,985 / 41,607 checks** |
| Full Core suite | **4,700,660 assertions passed** |
| Game build | **0 warnings / 0 errors** |
| Historical fixture bytes | All **9** fixture files from the starting commit byte-identical |
| Staged scope and whitespace | Reviewed; only the 13 milestone files, no generated artifacts or caches |

Required barrier, narrow-opening, receiver, minimax, mixed-resolution/shared-face, domain/corner, caller-order, far-origin/nonbinary, cancellation, concurrency, all work limits and expanded continuation regressions passed for both supported topology algorithms. Existing local-water, cave-surface, gameplay and save regressions also passed. No Godot UI/rendering changed, so this milestone uses the game compile and Core tests rather than claiming a new visual-water acceptance.


Two new fixture files are separate from accepted history: `hydrology-topology-v3.json` (30 cases) and `hydrology-work-coverage-v1.json` (24 queries). Timing/profile observations are not frozen; the complete solver outputs are hashed. Existing topology tests are parameterized without changing their default algorithm or expected physical results. Additional focused demand tests cover sampled-root omission, failed-root unknowns, multiple face corrections, pass limits, caller order, optional profiling, concurrency and expanded reconstruction.

Reproduction:

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --demand-work
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --demand-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --hydrology-work
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --adaptive-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --reconciliation
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore
dotnet build Thornwake.csproj --no-restore
```

`--demand-topology-analytic` runs only new analytic checks. `--demand-topology-spike` and `--hydrology-work-spike` generate candidate fixtures under ignored `artifacts/`; they never overwrite accepted source fixtures. `--hydrology-work-baseline` and `--hydrology-work-after` select one side of the comparison. Compact profiles are in `artifacts/hydrology-work/comparison.json`; the milestone observation is copied to `milestone-comparison.json` before repeat validation. Detailed topology results are in `artifacts/hydrology-topology-v3/report.json`. Logs are in `artifacts/hydrology-work/`. Tests compile in one sequential process stream on Windows to avoid locked test assemblies.

## Limits and exact next V1 step

This remains finite-resolution, conservative diagnostic topology, not certified continuous terrain or playable water. Unresolved candidates stay gated. No finite lake storage, inflow/outflow implementation, runtime water, meshes, erosion, climate, biomes, geology, V2 or gameplay changes were made. The known cave water/terrain consistency issue stays open. Existing generator fingerprints and gameplay remain preserved.

**Next V1 step:** a bounded graph-reuse experiment on the now-frozen topology-3 candidates, starting with the large mountainous and Vast cases and checking the broad-floodplain regressions. Measure repeated component/minimax traversal after each shared-face invalidation, then test retaining only demonstrably unaffected graph work with explicit cell-generation/dependency invalidation. Bound every retained queue/cache; require corrected-face barrier/opening and alternate-caller tests, expanded replay equality and unchanged accepted fixtures. Keep whole-search restart as the safe fallback. Change algorithm version if serialized meaning changes. Do not add a cross-call persistent cache first, and do not raise budgets merely to hide the bottleneck. Establish representative real supported spill topology (or document the precise resolution/domain obstruction) before a separate finite stage/storage milestone. Stop this pass here.

## Subsequent V1 checkpoint

The completed local graph-reuse experiment and its next-step guidance are in [MASTER_HYDROLOGY_GRAPH_REUSE.md](MASTER_HYDROLOGY_GRAPH_REUSE.md). That newer checkpoint supersedes the next-step recommendation above. Algorithm 3 retains its complete frozen result contract; only validated local expansion work is reused.
