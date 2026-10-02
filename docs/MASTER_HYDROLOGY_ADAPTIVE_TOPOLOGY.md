# Supplement V1: adaptive topology and deterministic continuation

2026-10-02 implementation checkpoint. Continues hydrology commit `726c6037058f92edb43075153c4b8faf0a73ef40`, after safely fast-forwarding the clean checkout to `a9d0ef2` (two design-inbox-only commits). Master Rework v3 and Supplement V1 remain authoritative. V2 remains inactive.

## Architecture and compatibility

The additive **reconciliation algorithm 2** consists of `AdaptiveHydrologyTopology.cs` (recipe, result, continuation and production adapter) and `AdaptiveBasinTopology.cs` (bounded adaptive graph). `HydrologyReconciler` only extracts its existing immutable domain adapter into an internal method; its algorithm 1 recipe, solver, sampler, and outputs remain reconstructible. Historical A3/A4 terrain, coarse/regional drainage, inspection v1 and all 22 reconciliation-v1 fixture cases are preserved. No terrain, runtime water, gameplay, Godot preview or rendering behavior changes.

`HydrologyTopologyRecipe` format 1 / algorithm 2 pins the full inspection dependency, maximum dyadic depth, minimum cell spacing, maximum probe spacing, height tolerance, and execution limits. Its geographic `LayerId` includes all semantic inputs, excluding execution limits. The separate `RequestId` includes those limits. Increasing work therefore does not rename an already supported sampled component. Changing terrain/dependency/sampling semantics creates a different layer. Neither result identity nor terrain is migrated automatically.

Each inspected patch starts from the existing fine-grid base quads. A cell samples its four corners, four face midpoints and centre, using a query-local integer lattice cache. Nonlinear interpolation residuals or overly long unprobed spans cause recursive dyadic subdivision. Parent graph cells are replaced by their children. This lets sampled barriers separate wet components and lets resolved subcell openings introduce lower routes. It does not flatten, smooth or carve terrain.

A patch is admitted to graph traversal only after its bounded refinement completes. Adjacent leaves may have different sizes. A face interval walk connects positive-length overlaps; point/corner contacts never connect. Shared samples use the same global integer addresses, including far origins and nonbinary patch spacing. A finer neighbour can expose additional shared-face evidence that invalidates a coarser cell. The coarser cell then splits, and obsolete component/minimax queues are discarded and reconstructed against the corrected topology. All samples and allocated topology remain charged; node visits and refinement passes are also cumulatively bounded. An incomplete pass never certifies its obsolete graph.

Wet-component discovery uses conservative cell maxima below the hypothetical candidate stage. A canonical low cell in the fully discovered component seeds deterministic minimax search over the corrected graph. Explicit downstream/ocean receiver levels remain binding. Original regional ports are retained as historical proposals; exact matches keep their identities, and replacement crossings use canonical shared positions and algorithm-2 layer identities. Search boundaries are never invented destinations. Receiver callbacks are reused from algorithm 1, including the check that recipient coarse routes do not return to the candidate basin.

## Result and stopping semantics

`ComponentStop` and `SpillStop` distinguish `SupportedAtSampleSpacing`, `Blocked`, `NoDeclaredOutlet`, `WorkBudget`, `DomainBoundary`, and `ResolutionLimit`. `SupportedAtSampleSpacing` describes this sampled conservative model, not a proof about all intervening terrain or a production water authorization.

- **WorkBudget:** a patch, sample, allocated-cell, split, node, frontier, or pass limit prevented completion. Partial patch sampling counts against the patch limit; `CompletedPatches` is reported separately. No completion is inferred from a budget boundary.
- **DomainBoundary:** an undefined external continuation could matter. It is neither a wall nor a drain.
- **ResolutionLimit:** detected interpolation uncertainty persists at the frozen depth/spacing limit. Known dry samples are not used to dismiss that unresolved evidence. Increasing work alone cannot remove this limit; changed sampling semantics require another layer identity.
- **Blocked:** the requested base quad/patch has no supported wet leaf. An explicit base-quad query selects its lowest supported descendant, not an invented link between disconnected descendants. The selected `QueryCell` is reported.

`FinalComponentId` is withheld unless **both** component and spill searches are supported after topology convergence. A completed component search with an unresolved spill is still useful diagnostic evidence, but may not receive final identity while further search/refinement could alter the answer. `SpillProposal` is deliberately named a proposal; it is not certified when `SpillStop` is unresolved. Exact receiver level, minimax elevation, cell path, real sampled path anchors, shared crossings, old outlet proposals, unresolved frontiers and work counts are exposed.

The report includes the first 64 refinement evidence records (old corner maximum, new sample extrema, interpolation residual and split decision). This fixed diagnostic cap does not restrict solver evidence or affect decisions. Samples narrower than the actual probes can still be missed. Maximum probe spacing forces probing where needed, but finite sampling is not analytic terrain certification. Conservative cells retain some grid character and may overestimate a spill or omit partly wet shore cells.

## Continuation contract

`HydrologyTopologyContinuation` format 1 contains the exact recipe and query (seed patch and optional base quad). It stores **no trusted terrain samples, mutable cache, heap, fluid quantities or runtime simulation state**. `Expand` permits componentwise nondecreasing execution limits. `AdaptiveHydrologyReconciler.Continue` validates/restores the frozen dependency and deterministically replays the query with those limits.

This is expanded deterministic reconstruction, not incremental CPU reuse. Replay intentionally pays again for earlier work. It is the smallest safe continuation representation for this diagnostic layer: old budget frontiers are rediscovered, not mistaken for geography, and a continued query matches a one-shot query at the same expanded limits, including work counts. Intermediate discarded graph passes are likewise bounded. Tokens can be serialized with `System.Text.Json`; malformed versions/dependencies and shrinking limits are rejected. Independent concurrent queries share no mutable topology or cache.

Default work limits: 16 started patches, 600,000 unique lattice samples, 300,000 allocated cells, 1,000,000 graph visits, 8,192 reported frontier entries, 60,000 split attempts and 64 graph passes. Supported maximum limits remain bounded (64 patches, 4 million samples, 1,048,576 allocated cells, 4 million visits, 65,536 frontier entries, 250,000 splits, 1,024 passes). Per-patch baseline regional-port sampling is separately reported and bounded by visited patches. Allocated-cell counts include parents and abandoned partial work; leaf counts cover completed resident patches. Quadtree construction is charged to sample/cell/split limits; `NodeVisits` measures graph traversal, not all CPU operations.

## Acceptance and diagnostics

`Tests/HydrologyTopologyTests.cs` and the separate `Tests/Fixtures/hydrology-topology-v2.json` cover 30 cases: 24 analytic/budget/far-origin cases and six real-world initial/expanded cases. The original reconciliation-v1 fixture is not regenerated.

Key outcomes:

- The same continuous analytic ridge/opening terrain produces contradictions in preserved algorithm 1. Algorithm 2 actually separates the ridge's components: the left component needs a 20 m spill, while the right retains its 4 m spill. The narrow opening becomes a supported 1 m route.
- A nominally lower outlet behind the 20 m ridge loses to a reachable 6 m receiver. A 7 m receiver remains binding even when the opened bed is 1 m.
- Finer shared-face evidence forces another graph pass and converges from either tributary entry. Exhausting the pass limit returns unresolved state; expanded replay matches one-shot execution.
- Patch/sample/cell/split/node/frontier/pass limits and a spill-only frontier are tested. JSON continuation, caller-order independence, canonical replacement ports, preserved original ports, concurrency, cancellation during sampling/search, far-origin reconstruction and nonbinary spacing are covered.
- Corner-only pools remain separate. Unresolved sharp/discontinuous curvature at insufficient resolution is explicitly retained, not promoted to a supported lake.

Developer commands:

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --adaptive-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --reconciliation
```

Normal validation checks the frozen matrix without updating it. `--topology-analytic` runs only analytic assertions; `--topology-spike` also measures real worlds and writes a candidate matrix for deliberate review. Neither overwrites a source fixture. The readable report is `artifacts/hydrology-topology/report.json`; generated evidence and logs remain excluded from Git. No Godot UI changed, so rendered engine smoke is not required by this milestone. Core tests do not certify gameplay water.

## Validation and performance

Representative Debug observations from the final candidate run (not production frame budgets):

| World/query | Started / completed patches | Terrain samples | Allocated cells | Splits / passes | Graph visits | Query outcome | Elapsed |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| Mountainous/42 initial | 8 / 4 | 300,000 | 74,585 | 9 / 1 | 50,199 | component/spill unresolved | 1.83 s |
| Mountainous/42 expanded | 32 / 30 | 2,000,000 | 498,045 | 236 / 1 | 476,636 | component/spill unresolved | 7.42 s |
| Mountainous/42 far initial | 8 / 4 | 300,000 | 74,585 | 9 / 1 | 50,199 | component/spill unresolved | 1.78 s |
| Mountainous/42 far expanded | 32 / 30 | 2,000,000 | 498,045 | 236 / 1 | 476,636 | component/spill unresolved | 7.38 s |
| Vast World/7319 far initial | 6 / 4 | 300,000 | 74,709 | 0 / 1 | 23,764 | component/spill unresolved | 2.32 s |
| Vast World/7319 far expanded | 32 / 18 | 2,000,000 | 549,129 | 61,042 / 2 | 384,099 | component supported; spill unresolved | 15.79 s |

Initial runs exclude prebuilt coarse/regional dependency construction. Expanded timings include production continuation restoration and the replay query, excluding subsequent independent verification. Baseline port sampling adds 63 samples in Mountainous cases and 819 in expanded Vast World. Both mountainous candidates retain sample/patch frontiers. The expanded Vast World candidate discovers its component but retains sample, resolution and omitted-frontier uncertainty (905 omitted entries); its final component identity is withheld. None of these three production candidates has an approved final spill. Expansion resolves more of the Vast World's component discovery and inspects more mountainous terrain, without forcing closure. Small analytic cases took about 0.1-2.8 ms warm; first-use compilation/JIT effects made the first case about 25 ms.

| Validation | Result |
| --- | --- |
| New algorithm-2 acceptance | **2,035 checks / 30 frozen cases** in the final full suite, including authoritative real-world path/endpoint checks |
| Focused new frozen matrix | Passed (605 checks before adding the extra real-world path assertions exercised in the full suite) |
| Preserved reconciliation algorithm 1 | **7,348 checks / 22 unchanged cases** |
| Accepted A4 terrain/coarse/regional | **1,098,592 checks**, all frozen outputs retained |
| Hydrology inspection v1 | **2,431 checks**, unchanged |
| A3 coarse / regional | **300,985 / 41,607 checks**, unchanged |
| Full Core suite | **4,695,292 assertions passed** |
| Game build | **0 warnings / 0 errors** |
| Historical fixture byte comparison | All **8** pre-existing fixture files byte-identical to the starting checkout |
| Final staged whitespace/scope review | Clean; generated reports/caches excluded |

Logs: `artifacts/hydrology-topology/focused.log`, `full-core.log`, and `game-build.log`. All accepted expectation files remain unchanged; only the separate algorithm-2 fixture was introduced.

## Remaining limits and exact next V1 step

No finite lake volumes/storage descriptors, runtime water, river meshes, erosion, terrain edits, weather/climate/biomes, V2 or gameplay systems were introduced. The existing cave water/terrain integration issue remains open.

This milestone establishes bounded adaptive corrections and safe deterministic expansion, not unlimited basin resolution. Fully inspecting/refining a patch before graph admission can spend samples on dry terrain. Shared-face correction restarts graph traversal within the frozen pass/visit limits. Replay saves continuation complexity and integrity risks but does not save CPU. No cross-platform floating-point guarantee is added. Identity is scoped to the frozen sampled model; later changing its interpretation requires another explicit version.

**Exact next recommended Supplement V1 step:** review the new supported analytic results and unresolved real-world frontiers, then perform a focused bounded-work/coverage pass on the accepted large candidates. Prioritize demand-driven local cell refinement and continuation reuse only if measurements justify the additional state complexity; preserve algorithm-2 fixtures when semantics change. Establish representative real components with supported spill topology before consuming them as finite lake stage/storage descriptors. Keep unresolved candidates gated. After that review, stage/storage can be a separate milestone, followed by explicit rate-limited inflow/outflow descriptors, the terrain-water connectivity regression, and a separate local realization spike. Stop here; none of those downstream systems is part of this commit.

## Subsequent V1 checkpoint

The bounded-work/coverage pass is documented in [MASTER_HYDROLOGY_BOUNDED_WORK.md](MASTER_HYDROLOGY_BOUNDED_WORK.md). It preserves this algorithm-2 acceptance and adds explicit topology algorithm 3 with separate fixtures; the next-step guidance in that newer checkpoint supersedes the recommendation above.
