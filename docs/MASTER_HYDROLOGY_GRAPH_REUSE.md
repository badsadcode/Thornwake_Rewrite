# Supplement V1: bounded local graph-reuse experiment

Date: 2026-10-02. Starting reviewed commit: `42389cab34ee1df5d1530a0290f388f13f683e68`. Continues the working tree from the bounded-work checkpoint. This is generation-time hydrology tooling, not continuous runtime water simulation.

## Decision

**Retain bounded, query-local reuse of completed neighbour expansions for topology algorithm 3.** Retain whole-search restart for all search-dependent state. No new topology algorithm/version is introduced: complete serialized results, including partial components, frontiers, evidence, work metrics, paths, identities and continuations remain identical to accepted algorithm 3. Algorithm 2's ordinary execution remains uncached; reconciliation algorithm 1 and terrain/coarse/regional/inspection layers are unchanged.

The experiment demonstrates a modest practical runtime improvement on expensive large candidates, not a reduction in graph visits or an increase in resolved lake coverage. It avoids redoing local adjacency work without attempting a general incremental reachability/minimax engine. Broad basins benefit little; the optimization is bounded and falls back to the original computation whenever it cannot prove local validity.

## Why this unit is reusable

Algorithm 3 previously restarted both wet-component discovery and minimax traversal after a shared-face correction. On the large mountainous and Vast cases, approximately 1.92 million of two million charged visits revisited cells from earlier passes. Many distant local neighbour expansions were identical, even though membership and cost chains could no longer be trusted.

The reusable unit is **one complete ordered face-neighbour expansion of a leaf**. Its entry holds the owner, neighbouring leaf references and their local generation stamps. Terrain samples and leaf bounds are immutable within a query. Refinement is monotonic: a leaf is retired by assigning children and increasing its generation; there is no coarsening, key recycling or terrain mutation inside the query.

A complete neighbour walk has already sampled/admitted the required roots and reconciled its shared faces. If the owner and every recorded neighbour are still leaves with matching generations, exactly the same face overlaps and ordered neighbours remain valid. New refinement elsewhere cannot alter that local adjacency without retiring one of those dependencies. This is a local adjacency proof, **not a proof that membership, a minimax cost or a path is still valid**.

Invalidation rules:

- Retiring a cached owner immediately removes its expansion.
- A cached neighbour whose generation changed (or now has children) invalidates the owner's expansion on its next lookup. Recompute it with the original face walk, which can cause another full restart.
- A partially enumerated walk, thrown topology correction, missing root, budget failure or domain/face failure is never cached.
- Stable unresolved leaf flags may appear in a successful neighbour list; the normal search reprocesses them into unresolved frontiers every time. No frontier result or boundary decision is reused.
- Cache capacity refusal uses the uncached walk. It does not invent a hydrological boundary or change the result.
- Every graph visit still passes the original budget/cancellation checks before expansion. Reuse saves expansion work, not charged visits; it cannot bypass a cap.

All component membership, pending queues, minimax costs, parent chains, canonical source selection, receiver/outlet comparisons, path and crossing construction, and unresolved frontiers are recomputed. Thus a corrected parent and **all transitive path dependants** are discarded, including paths that look distant from the correction. This full restart is the deliberate safe fallback; the experiment does not claim selective reuse of global search state.

## Bounds and diagnostics

Cache limits are `min(131072, recipe.Cells)` entries and 1,048,576 neighbour references with matching generation stamps. At the entry-count limit, new misses do not allocate capture lists. Per-entry neighbour enumeration is bounded by the existing dyadic depth/domain constraints. Refused entries use the original walk; no eviction policy, global cache, cross-query persistence, thread-shared mutation or serialized cache is introduced. Cache limits are internal execution choices and cannot change geographic results.

`ProfileGraph(patch, quad, reuse)` returns a separate developer record. Normal solver results/continuations do not acquire diagnostic fields. The diagnostic visit ledger has at most one record per allocated visited leaf; pass records are bounded by the recipe pass limit. There is an optional internal event observer for deterministic cancellation tests, not a runtime callback contract.

Per-pass observations distinguish component versus spill restart, wet/spill visits repeated from earlier passes, cache lookups/reuse/invalidation, previously reused entries invalidated later, fresh expansions, corrected leaves, corrected cells already on minimax parent chains, and still-valid expansions when a pass is discarded. Balancing visits retain their old budget meaning but are excluded from the graph-only ledger. Every graph visit is accounted for as computed or reused expansion work, including interrupted walks. `RestartPhase` records a correction requesting restart; a pass/balancing limit can prevent the next pass. Begun full restarts are `Metrics.Passes - 1`. These counts agree for every real case in the matrix below.

`ReusableExpansionsAtRestart` is a **lower bound for tracked local expansions**, limited by cache admission; it is not an estimate of safely reusable membership or costs. `CorrectedLeaves` counts successful shared-face replacement, not every subdivision during root construction. Invalidated entries include retired cached owners and entries rejected on neighbour validation. Cache refusals and final frontier counts are reported separately. Unknown/budget frontiers are recomputed, never trusted from an old pass.

Spatial attribution is the Manhattan gap in metres between a revisited unchanged cell rectangle and the **previous restart-triggering correction**. Subsequent balancing may correct additional cells; the reported distance is explicitly not nearest-distance to the whole correction cascade. Large distances demonstrate local work far from a trigger, not dependency independence of its path.

## Representative measurements

Final matrix: **12 timed queries, 60 assertions**, each timed result matched both its independent diagnostic execution and the unchanged algorithm-3 coverage fingerprint. `S` = SupportedAtSampleSpacing; `W` = WorkBudget. Counts in the first table are identical before/after. Allocated cells include retired parents/partial work; leaves are resident admitted leaves, not all reachable wet cells.

| Candidate | Seconds uncached -> reuse | Change | Samples | Allocated / leaf cells | Visits / passes | Component / spill |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| mountainous/42 / large | 13.18 -> 10.82 | -18.0% | 314,857 | 79,263 / 77,479 | 2,000,000 / 43 | W/W |
| mountainous/42 / small | 14.65 -> 14.80 | +1.0% | 1,890,433 | 470,444 / 470,385 | 2,000,000 / 5 | W/W |
| mountainous/42/far / large | 12.67 -> 10.74 | -15.2% | 314,857 | 79,263 / 77,479 | 2,000,000 / 43 | W/W |
| vast-world/7319/far / small | 12.99 -> 12.18 | -6.2% | 1,026,049 | 286,069 / 252,343 | 2,000,000 / 9 | S/W |
| vast-world/7319/far / large | 12.02 -> 10.48 | -12.8% | 327,343 | 81,384 / 80,834 | 2,000,000 / 38 | W/W |
| vast-world/7319/far / medium | 17.44 -> 17.90 | +2.6% | 1,932,877 | 481,084 / 481,067 | 2,000,000 / 5 | W/W |

Graph-only attribution excludes balancing. All restarts, repeated visits and correction counts remain the same. Discarded visits are the cumulative graph visits in passes that ended in correction; these overlap repeated visits and must not be added together. Computed expansions include interrupted walks. Invalidations include direct retirement as well as lookup rejection, so they can exceed considered minus reused.

| Candidate | Component / spill restarts | Repeated visits | Discarded visits | Computed expansions before -> after | Considered / reused | Invalidated (previously reused) | Corrected leaves / parents |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| mountainous/42 / large | 42 / 0 | 1,922,605 | 1,980,563 | 1,993,710 -> 71,190 | 1,922,588 / 1,922,520 | 68 (1) | 49 / 0 |
| mountainous/42 / small | 4 / 0 | 1,546,692 | 1,810,861 | 1,999,785 -> 1,475,498 | 524,288 / 524,287 | 1 (0) | 13 / 0 |
| mountainous/42/far / large | 42 / 0 | 1,922,605 | 1,980,563 | 1,993,710 -> 71,190 | 1,922,588 / 1,922,520 | 68 (1) | 49 / 0 |
| vast-world/7319/far / small | 0 / 8 | 1,609,236 | 1,713,501 | 1,893,938 -> 553,431 | 1,340,510 / 1,340,507 | 3 (0) | 21 / 12 |
| vast-world/7319/far / large | 37 / 0 | 1,924,994 | 1,974,535 | 1,997,934 -> 73,046 | 1,924,980 / 1,924,888 | 95 (1) | 70 / 0 |
| vast-world/7319/far / medium | 4 / 0 | 1,535,749 | 1,855,603 | 1,999,929 -> 1,475,641 | 524,288 / 524,288 | 0 (0) | 4 / 0 |

The final table shows bounded cache coverage, cumulative local expansions still valid when discarded passes ended, trigger-relative distances for unchanged revisits, and final recomputed frontiers. Those local counts are not global search-state reuse certificates.

| Candidate | Peak cache cells / edges | Capacity refusals | Valid local expansions at discard | Distance mean / max km | Final frontiers (omitted) |
| --- | ---: | ---: | ---: | ---: | --- |
| mountainous/42 / large | 71,080 / 287,792 | 0 | 1,980,451 | 11.28 / 23.86 | node-budget: 1 (0) |
| mountainous/42 / small | 131,072 / 524,295 | 1,342,266 | 524,287 | 142.00 / 171.05 | node-budget: 1, patch-budget: 3,037 (0) |
| mountainous/42/far / large | 71,080 / 287,792 | 0 | 1,980,451 | 11.28 / 23.86 | node-budget: 1 (0) |
| vast-world/7319/far / small | 131,072 / 524,766 | 421,103 | 903,553 | 140.69 / 290.50 | node-budget: 2, patch-budget: 1,245, resolution-limit: 42 (0) |
| vast-world/7319/far / large | 72,914 / 292,963 | 0 | 1,974,402 | 31.21 / 64.75 | node-budget: 1 (0) |
| vast-world/7319/far / medium | 131,072 / 524,288 | 1,342,408 | 524,288 | 388.48 / 465.75 | node-budget: 1, patch-budget: 2,337 (0) |

The final run retains about 13-18% improvement on the large cases and about 6% on the Vast reference. The earlier complete comparison independently showed the same pattern (large mountainous 12.60 -> 10.64 s; far mountainous 12.66 -> 10.79 s; large Vast 12.07 -> 10.46 s). Broad cases fluctuate around no improvement; the final broad Vast run is slightly slower. These observations support retaining the small local optimization, but do not justify claiming a general speedup. Cache-limited broad candidates repeatedly use uncached fallback without changing output.

**Coverage is unchanged:** none of these six candidates has a supported spill; only the reference Vast component is supported. All final component IDs remain withheld. Frontiers and omitted-frontier counts match the original result exactly. Every query still reaches the two-million-visit cap. No higher budget or altered fixture was used to claim success.


The benchmark uses accepted A4 candidates, unchanged limits and deterministic selection from the existing coverage fixture. Debug timings exclude coarse/regional/domain construction, diagnostics, hashing and report serialization. Each timed run uses a fresh domain so baseline-port caches do not bias one side. A separate profiled execution supplies attribution; its result is compared with the timed execution and the **existing** coverage fingerprint. Times are workstation observations, not runtime frame-budget promises. The original uninstrumented baseline and the retained cached path are both measured.

All runs use 32 patches, 2,000,000 samples, 800,000 allocated cells, 2,000,000 visits, 16,384 frontier entries, 180,000 splits, 64 passes, depth 3, 2 m minimum spacing, 64 m maximum probe spacing and 0.25 m tolerance. Budgets were not increased.

Only expensive local neighbour expansion is reused. **Repeated visits avoided: zero. Full restarts avoided: zero.** The remaining expensive ordered-set/heap traversal and rebuilding of search state still occur. This explains why bypassing most neighbour expansions produces a smaller runtime reduction and why broad basins with limited cache coverage benefit little.

## Correctness and validation

| Validation | Result |
| --- | --- |
| Focused graph reuse/invalidation | **618 assertions** (shared analytic scenarios plus 41 focused checks), passed independently and in the full suite |
| Before/after comparison | **60 assertions / 12 timed queries**, each also checked against an independent diagnostic run and the existing fixture |
| Preserved topology algorithm 3 | **5,276 checks / 30 frozen cases**, focused and full-suite passes |
| Preserved topology algorithm 2 | **2,035 checks / 30 frozen cases**, focused and full-suite passes |
| Preserved reconciliation algorithm 1 | **7,348 checks / 22 cases**, focused and full-suite passes |
| Existing work/coverage fixture | **73 checks / 24 queries**, unchanged |
| Accepted A4 / inspection | **1,098,592 / 2,431 checks** |
| Coarse drainage / regional drainage | **300,985 / 41,607 checks** |
| Full Core suite | **4,701,278 assertions passed** |
| Game build | **0 warnings / 0 errors** |
| Historical fixture integrity | All **11** source fixture files byte-identical to starting commit `42389cab34ee1df5d1530a0290f388f13f683e68` |
| Final review | Nine scoped files; no source fixture, generated artifact, scene, gameplay or save-format changes; whitespace check clean |

The final commands exited successfully. Logs: `focused.log`, `comparison.log`, `topology3.log`, `topology2.log`, `reconciliation1.log`, `full-core.log`, `game-build.log` under `artifacts/hydrology-graph/`. Existing local water, cave surface, gameplay and persistent-save regressions also passed; no new rendered-water or gameplay behavior is claimed.


Focused comparisons cover all 24 existing analytic topology cases against the uncached implementation. Extra fixtures create sequential component-side and spill-side shared-face corrections, including a corrected minimax parent, a distant unchanged branch, and an expansion that was reused before a later correction invalidated it. Complete serialized-result hashes are compared, rather than comparing only the chosen elevation. Tests also cover alternate tributary entry, lower openings, binding receiver levels, expanded JSON continuation, pass/node/sample/split/frontier exhaustion, cancellation specifically on reuse and invalidation, and parallel query-local profiles.

The retained implementation does not regenerate any accepted expectation. Existing algorithm-3 and coverage fingerprints exercise the normal cached entry path; a separate developer measurement explicitly runs the uncached path. Barrier separation, corner-only isolation, far-origin/nonbinary sampling, open-domain uncertainty and original/replacement port identity remain covered by the historical suites.

## Files and reproduction

- `Game/Core/AdaptiveBasinTopology.cs`: generation/correction hooks and algorithm-3 query-local cache activation; existing search decisions remain intact.
- `Game/Core/HydrologyGraphReuse.cs`: bounded local dependency validation, original-walk fallback, and separate restart diagnostics.
- `Game/Core/AdaptiveHydrologyTopology.cs`: developer profiling entry point.
- `Tests/HydrologyGraphReuseTests.cs`: analytic invalidation/replay comparisons and six-candidate before/after matrix.
- `Tests/HydrologyTopologyTests.cs`, `Tests/Program.cs`: reuse comparison mode and test registration; no old fixture changes.
- This checkpoint, its continuation link from `MASTER_HYDROLOGY_BOUNDED_WORK.md`, and the world-generation test guide.

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --graph-reuse
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --graph-comparison
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --demand-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --adaptive-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --reconciliation
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --hydrology-work
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore
dotnet build Thornwake.csproj --no-restore
```

`--graph-baseline` collects only uncached restart measurements. Generated reports/logs are under ignored `artifacts/hydrology-graph/`; `comparison.json` contains all per-pass records, `analytic.json` the focused cases. `topology-analytic/` keeps graph comparison evidence separate from ordinary topology fixture reports. No source fixtures are written by these commands. The full Core suite includes focused reuse checks plus the existing 24-query frozen coverage suite; the separately invoked comparison provides the timed uncached control. No rendering/UI changed, and no visual-water acceptance is claimed.

## Remaining limitation and precise next V1 step

Representative real supported **spill topology is still not established**. The reference Vast component remains supported, but spill frontiers remain unresolved; the other measured components also remain budget-limited. Cached local adjacency makes reconstruction cheaper without removing full graph restart/visit exhaustion, domain extent, or genuine resolution uncertainty. No lake representation should consume these unresolved results.

**Next V1 step:** isolate bounded wet-component reachability reuse as a separate experiment before touching minimax reuse. Use the frozen large and broad cases; record the canonical seed and an explicit discovery-parent tree, invalidate the descendants of corrected cells, and reattach only through validated surviving boundary edges. Treat a changed canonical seed, an uncertain dependency, or an incomplete invalidation budget as a full-restart fallback. Compare supported results with uncached reconstruction and version any changed serialized work/result contract. Keep minimax costs, outlet ordering and paths on full restart initially. The goal must be fewer repeated charged visits and measurable real-basin coverage, not merely another cache. If that small dependency model is not safe or useful, retain full restart and document the exact unresolved sampling/domain requirements before progressing.

Stop after this checkpoint. No finite lake storage/final lake representation, river rendering, runtime water, climate, biomes, caves, geology, editors, loading-screen UI or living-world systems were started. The existing cave water/terrain integration issue remains open.
