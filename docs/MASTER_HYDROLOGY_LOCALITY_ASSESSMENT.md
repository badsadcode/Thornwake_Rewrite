# Supplement V1: hydrology correction-locality assessment

Date: 2026-10-03. Reviewed starting commit: `e1f0087a12bf09cccc46a8c36bb5e542426b2b01`.

## Outcome and scope

The observed descendant closures are tiny, including the surviving alternate-face boundary. However, the broad candidates require a whole-component discovery index of 454,056–464,893 nodes (about 90–92 MiB in the conservative managed-storage model). The small estimated repair scan is not a demonstrated cheap, bounded reuse implementation. Unresolved sampling/domain/spill requirements remain the next V1 investigation; no further reachability implementation is recommended by this checkpoint.

This milestone adds developer observations only. Active topology algorithm 3, its bounded neighbour cache, full component restart, full minimax restart, charged work, queues, budgets, terrain and serialized results are unchanged. No topology version was added. The rejected topology-4 archive remains inactive. Existing gameplay, saves and the macro preview are unaffected; no visual-water acceptance is claimed.

## Diagnostic model

`AdaptiveHydrologyReconciler.ProfileLocality` returns the unchanged topology result alongside separate graph/locality reports. The normal reconciliation path leaves the collector null. Diagnostics create a query-local shadow discovery tree: the first supported wet discovery gives a cell its parent, and linked child lists provide reverse dependencies. Pending discoveries and successfully visited cells are reported separately. Parents precede children; descendant traversal is iterative.

After a successful shared-face leaf replacement, the collector walks only that retired cell's newly affected discovery descendants. It records the parent links inspected, affected cells, queue/set peaks, correction depth/spacing, phase and balancing status. During cascading corrections it marks shadow removals so it does not count the same removed closure repeatedly. Each real full restart resets the shadow tree; there is no hypothetical repaired tree carried across passes.

A read-only geometric interval walk inspects positive shared faces around the affected set. It reads existing roots, leaves and cached samples; it never calls the solver's loading, sampling, scheduling, neighbour-expansion or refinement routines. Equal-sized resolved wet leaves provide the existing sampled face support. Mixed-size faces must satisfy the existing interpolation tolerance using already present samples. Missing roots/domain segments remain unknown. Known wet neighbours absent from the discovery tree are counted separately; they do not become supported membership.

**Alternate-face closure:** an orphan can have an unchanged valid face to a surviving cell whose discovery parent lies in another branch. Such faces are explicitly counted, even though that surviving cell's own ancestry did not change. The seven-cell/two-branch model retires a parent, removes three nodes including two descendants, and identifies three surviving boundary faces including the unchanged alternate entry into the orphan. An invalid face cannot certify that entry. These are candidate reconnections, not repaired/requeued nodes or proof of an entire subtree's survival.

**Outside-tree and zero-descendant corrections:** if a correction has no newly affected shadow nodes, the collector still walks the corrected cell's geometric boundary. A zero descendant count is never converted into zero work. This includes spill-side corrections: the reference case has 21 outside-tree corrections and 862 estimated local operations despite no affected wet-tree nodes. Previously removed cells encountered again during balancing likewise retain boundary work.

Distances are cell-centre Euclidean distances in metres, computed from relative lattice addresses. The far-origin mountainous case has identical locality records to its near-origin counterpart. Counts of boundary cells are unique within one correction; totals across corrections can count the same cell again. `AffectedPercent` is newly affected cells (including the retired root) divided by the hypothetical remaining discovered membership before that correction. `Descendants` excludes the retired root. Descendant-only percentages can be derived from the same denominator; their maximum is 0.012165% in the mountainous large cases, 0.003136% in Vast large and zero in the other three cases.

Observation caps are 524,288 nodes (also limited by recipe cell capacity), 32,000,000 diagnostic operations and 4,096 correction records. The node cap also bounds the diagnostic unique-key ledger. Exhaustion explicitly marks the profile incomplete and stops collecting; it neither adds a solver frontier nor changes the result. Tests exhaust each cap at entry and after partial collection. Cancellation during corrections, descendant traversal and face enumeration propagates normally. All six matrix profiles completed within these diagnostic caps. Solver budgets were not raised.

## Six-case matrix

The exact original recipe is used: topology algorithm 3, depth 3, 2 m minimum spacing, 64 m probe spacing, 0.25 m tolerance, 32 patches, 2,000,000 samples, 800,000 cells, 2,000,000 visits, 16,384 frontier entries, 180,000 splits and 64 passes. Every case has a fresh-domain unprofiled control and a separate profiled run. Both complete result hashes match the existing coverage fixture. Every graph profile matches the retained algorithm-3 observation archived in the previous experiment. The full per-correction/per-pass data is in [the assessment report](experiments/hydrology-locality-assessment.json).

| Candidate (seed patch) | Corrections / outside discovery | Total / peak descendants | Newly affected | Mean / median / max affected % | Surviving boundary cells / faces | Alternate faces (corrections) |
|---|---:|---:|---:|---|---:|---:|
| mountainous/42 large (10524) | 49 / 10 | 14 / 2 | 53 | .004565 / .001696 / .038521 | 180 / 184 | 14 (11) |
| mountainous/42 small/broad (4126) | 13 / 6 | 0 / 0 | 7 | .000119 / .000220 / .000221 | 32 / 32 | 0 (0) |
| mountainous/42/far large (10524) | 49 / 10 | 14 / 2 | 53 | .004565 / .001696 / .038521 | 180 / 184 | 14 (11) |
| vast-world/7319/far small reference (5919) | 21 / 21 | 0 / 0 | 0 | 0 / 0 / 0 | 0 / 0 | 0 (0) |
| vast-world/7319/far large (12869) | 70 / 13 | 5 / 2 | 62 | .001854 / .001773 / .005877 | 213 / 215 | 6 (4) |
| vast-world/7319/far medium/broad (5579) | 4 / 0 | 0 / 0 | 4 | .000215 / .000215 / .000215 | 14 / 14 | 0 (0) |

All reference corrections occur during spill traversal; the other 185 occur during component traversal. Across six traces: 206 corrections, 60 outside discovery, 179 newly affected nodes, 33 descendants, 629 boundary faces and 34 alternate faces across 26 corrections. No correction affects even 0.04% of its remaining discovery tree. Peak scratch usage is three affected cells, two stack entries, six surviving boundary cells and seven boundary faces. At most three alternate faces and two orphan endpoints occur in one correction.

Maximum descendant/boundary-centre distance is 46.875/66.291 m in mountainous large and 125/176.777 m in Vast large. These measure the observed tree and immediate known boundary, not the eventual reach of newly explored terrain. There are also 157 unknown segments and 468 known wet faces outside the discovered tree across the matrix. These must not be silently classified as closed or safely connected.

## Work, dependency storage and comparison

| Candidate | Estimated local operations | Full replay wet visits | Local / replay % | Repeated wet visits | Peak reverse-index nodes / edges | Modeled peak MiB | Index maintenance operations |
|---|---:|---:|---:|---:|---:|---:|---:|
| mountainous large | 2,040 | 1,991,471 | .102437 | 1,922,605 | 71,524 / 71,523 | 14.19 | 12,065,677 |
| mountainous small/broad | 447 | 1,547,580 | .028884 | 1,546,692 | 454,056 / 454,055 | 90.07 | 11,984,123 |
| mountainous far large | 2,040 | 1,991,471 | .102437 | 1,922,605 | 71,524 / 71,523 | 14.19 | 12,065,677 |
| Vast small reference | 862 | 388,832 | .221690 | 388,832 | 48,604 / 48,603 | 9.65 | 2,609,865 |
| Vast large | 2,194 | 1,967,015 | .111540 | 1,924,994 | 73,301 / 73,300 | 14.55 | 11,972,674 |
| Vast medium/broad | 98 | 1,536,307 | .006379 | 1,535,749 | 464,893 / 464,892 | 92.22 | 11,984,217 |

Estimated local work counts descendant pops/child links, existing-root/leaf boundary lookup and cached-sample face validation. It excludes future reattachment, exploration, queue scheduling, rebuilt dependencies and fallbacks. Index maintenance separately counts discovery lookups and first-insertion operations along the actual full-restart trace: 62,682,233 modeled operations in total. It is not free work and is not included in the tiny local/replay ratio. Diagnostic bookkeeping (visited flags, report serialization, tree resets, unique-key statistics) is not a proposed repair algorithm's cost model.

Full replay visits are wet visits in passes after the first (9,422,676 total). Repeated wet visits are the graph collector's repeated admissions (9,241,477 total). These are distinct: a restarted search can also reach new cells. Several corrections in a balancing group share the same following replay; the per-correction `ReplayWetVisits` field must not be summed. Local operations and graph visits are different units and their ratio is not a speedup prediction.

The conservative managed reverse-index/scratch model is `4096 + 192*peakNodes + 16*peakEdges + 16*peakStack + 96*(peakAffected + peakBoundary)` bytes. It allows for node objects, hash/list capacity, reverse links and temporary collections. It is an engineering estimate, not measured CLR peak allocation. It excludes existing terrain/adjacency storage and diagnostic-only unique-key, graph and report ledgers. A hypothetical retained implementation may amortize index construction differently; compact arrays could lower memory, but no such design or its closure maintenance has been validated here. The model still requires indexing the entire discovered component to locate an arbitrary corrected ancestor's descendants.

The rejected topology-4 experiment spent 16,000,000 whole-tree repair checks on each large case and 2,916,240 on the reference; broad cases reached its 131,072-node capacity before repair. Current local estimates are 2,040/2,194 and 862 respectively, dramatically smaller. That is favorable evidence for locality, but the two broad indices exceed the old capacity by about 3.5 times. Previously saved visits produced no additional supported real spill; this assessment changes no visits or coverage at all. Topology 4 remains archived, with all historical fixtures untouched.

Debug query timings (control/profile seconds): mountainous large 10.99/12.90; small 14.40/18.18; far large 10.53/12.69; Vast reference 11.75/12.80; large 10.42/12.46; medium 17.26/21.39. Total 75.35/90.42 seconds, about 20% observation overhead. These single workstation observations exclude domain setup and report serialization; they are not an optimization benchmark or runtime guarantee. No timing assertion or performance fixture is introduced.

## Proof limits and reason for stopping reuse work

The closure tests establish what this collector observes, not a correct membership repair. First-discovery parents are order-dependent and include pending cells. The traced algorithm always restarts; a retained tree could develop different parents, corrections and larger closure sets. New child leaves and unknown/untracked faces still need legitimate future exploration. The presence of an alternate supported face does not prove that a complete orphan subtree may be kept. Minimax dependencies are deliberately not modeled for reuse.

The evidence meets the local-descendant/local-boundary part of the acceptance gate, but does not establish a small dependency index across the representative workloads. Allocating roughly 90–92 MiB of additional modeled index per broad query is a material cost before terrain/cache storage, concurrent queries or transactional repair state. Full-restart tracing also cannot establish that these inexpensive immediate closures would persist under retained exploration. This is insufficient justification for another reachability implementation under the stated all-conditions gate; it is not a claim that every possible compact implementation is impossible.

The Vast reference remains `SupportedAtSampleSpacing` for its wet component and `WorkBudget` for spill. All other candidates remain `WorkBudget/WorkBudget`. Large-case frontiers contain node-budget exhaustion. Broad cases also contain patch-budget entries (3,037 and 2,337); the reference has node, patch (1,245) and resolution (42) entries. Partial frontier lists are evidence of encountered uncertainty, not a complete classification of everything outside the explored domain. No finite-lake consumer should treat these as proven final spill topology.

## Validation

| Validation | Result |
|---|---|
| Focused locality | **635 checks passed** (601 analytic comparisons, 13 collector-model checks, 21 additional integration/cap/cancellation/determinism checks) |
| Six-case measurement | **24 checks / 6 independently controlled queries passed**, complete observations and unchanged result/graph hashes |
| Graph neighbour reuse | **618 checks passed**, focused and full suite |
| Topology algorithm 3 | **5,276 checks / 30 frozen cases passed**, focused and full suite |
| Topology algorithm 2 | **2,035 checks / 30 frozen cases passed**, focused and full suite |
| Reconciliation algorithm 1 | **7,348 checks / 22 cases passed**, focused and full suite |
| Frozen coverage | **73 checks / 24 queries passed**, focused and full suite |
| Full Core | **4,701,913 assertions passed**, including accepted terrain, drainage/regional reconstruction, local water/caves, gameplay and saves |
| Game build | **0 warnings / 0 errors** |
| Fixture integrity | All **11** historical files match the archived SHA-256 list and reviewed `e1f0087` commit byte for byte |
| Final review | Ten scoped files; no algorithm/default, fixture, budget, terrain, scene, gameplay or save-format changes; staged whitespace check clean |

The focused collector suite covers descendant retirement, leaf and outside-tree boundaries, the mandatory alternate-face closure, invalid faces, full seed closure, repeated/cascading corrections, missing roots, diagnostic caps at entry and mid-collection, targeted cancellation, deterministic repeated/concurrent profiles and complete disabled/enabled result equality over all 24 analytic topology cases. The matrix adds six independent controls, frozen result hashes and archived full graph-profile equality. No fixture is regenerated.

## Reproduction and changed files

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --locality
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --locality-matrix
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --graph-reuse
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --demand-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --adaptive-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --reconciliation
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --hydrology-work
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build
dotnet build Thornwake.csproj --no-restore
```

- `Game/Core/HydrologyLocalityDiagnostics.cs`: independent bounded shadow collector and read-only geometry adapter.
- `Game/Core/AdaptiveBasinTopology.cs`, `AdaptiveHydrologyTopology.cs`: nullable observation hooks and developer entry point only.
- `Tests/HydrologyLocalityTests.cs`: collector/integration regressions and frozen six-candidate measurements.
- `Tests/HydrologyTopologyTests.cs`, `Tests/Program.cs`: optional analytic comparisons, focused commands and full-suite registration.
- This checkpoint, the JSON assessment archive, the previous checkpoint's continuation link and the world-generation test guide.

Ignored reports/logs live under `artifacts/hydrology-locality/`: `analytic.json`, `matrix.json`, `focused.log`, `matrix.log`, `graph-reuse.log`, `topology3.log`, `topology2.log`, `reconciliation1.log`, `coverage.log`, `full-core.log`, `game-build.log`. The committed report and unchanged fixture hashes make the decision reviewable after checkout.

## Exact next V1 step and final recommendation

Retain algorithm 3 and both full restarts. Next, perform a bounded diagnostics-only attribution of unresolved sampling/domain/spill requirements, starting with the already supported Vast reference wet component and the two broad patch-limited candidates. Relate encountered spill frontiers and their lower bounds to proposed receiver/spill levels; distinguish insufficient domain coverage, unresolved shared-face/sample evidence and exhausted traversal work. Use the same frozen seeds/recipes, preserve uncertainty and identify what evidence is missing before proposing any separately versioned change. Do not raise budgets, reuse minimax or start lake storage as part of that assessment.

No finite lake storage, runtime water, climate, biomes, caves, geology, rendering or later systems were started. The known cave water/terrain visual consistency issue remains open and outside this milestone. Stop here; the next assessment has not been implemented.

**Recommendation B: Stop pursuing wet-component reachability reuse and investigate unresolved sampling/domain/spill requirements instead**
