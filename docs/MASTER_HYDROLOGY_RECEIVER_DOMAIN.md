# Supplement V1: bounded receiver-aware domain experiment (topology 5)

Continues reviewed main `4864a9eb560c27393621bb9e0d004338b10840a1` (spill attribution). Master Rework v3 and Supplement V1 remain authoritative. This is a generation-time **experimental** path, not a production promotion. Algorithm 3 remains the retained/default topology path; rejected archived algorithm 4 stays reserved and unsupported.

## Contract and implementation

The previous queries sometimes spent their bounded component search enumerating surrounding ocean. A low floor sample, missing patch or declared outlet point alone could not safely close that domain. Version 5 adds a stronger, bounded area certificate while retaining the existing terrain generator, sampler, correction/balance logic and full-restart component/minimax searches.

`HydrologyReceiverDomain.cs` defines an internal certificate with format 1, topology algorithm 5, reconciliation domain identity, candidate basin identity, target patch, existing receiver ID/level, terminal flag and fixed evidence kind `whole-patch-ocean-v1`. The evidence kind identifies the **ocean-only** class supported by this prototype. The target patch and immutable domain specify the entire closed rectangle, not just an outlet point. The observed transition additionally records its source cell and sampled shared-face midpoint. Dependency identity is derived from the frozen inspection/regional/coarse/world recipes. No caller-supplied public certificate deserializer exists.

The production `HydrologyReconciler.CreateDomain` adapter mints a certificate only when:

1. The target patch has an existing terminal coarse ocean receiver, lies outside the candidate basin, and passes the existing nonreturning `ReconciliationExit` declaration contract.
2. The immutable macro land-shape formula proves the **entire closed target rectangle** is at or below sea level, including all entering faces.
3. The solver rechecks the certificate's version/domain/candidate/patch/evidence/level and the matching declared exit. A conflicting sampled transition remains unknown.

The ocean proof performs **no terrain sampling**. For each existing land feature, coordinate warp is bounded by `RadiusX * CoastRoughness / 2` per axis, coast displacement by `r`, and angular lobes by `1.95*r`. Rotating the padded rectangle gives interval lower bounds on the two ellipse coordinates. Only when the resulting minimum radius exceeds `1 + 2.95*r + 1e-8` for **every** feature can land influence be negative everywhere. A one-metre rectangle padding makes the test conservative against roundoff throughout the supported coordinate domain. The world-edge term can only reduce land influence. The proof is deliberately tied to unchanged macro algorithms 3/4; future geography formulas require a new proof/version. It may reject genuinely oceanic coastal patches. Falsification sampling is regression coverage, not the mathematical basis of the certificate.

A convex submerged rectangle supplies a path from every entering face midpoint to its already-declared ocean centre at receiver level. Version 5 therefore treats this **external receiver area as absorbing**: its interior is not part of the candidate basin component, and paths do not re-enter land through it. Component closure means complete sampled interior closure **or independently certified external boundaries on every escaping portion**. It does not mean the entire globally connected ocean has been enumerated.

Boundary meanings remain separate:

- `CertifiedExternalReceiver`: whole-patch proof plus declaration; omit external patch admission and expose a minimax receiver at the sampled entering face.
- `InternalContinuation`: no area proof; use the ordinary sampling/admission/face-validation path. This does not assert that unvisited terrain is known.
- `UnknownBoundary`: undeclared world edge, failed admission/refinement, invalid certificate, metadata cap or other unfinished evidence. Normal failures remain in the result's `Frontier`; invalid certificate/metadata decisions also appear in receiver diagnostics.

Non-ocean downstream receivers remain ordinary existing point exits. No whole-world receiver graph, lake storage, infinite river source, local water service or cross-query cache is introduced.

## Determinism, identities and continuation

Format-1 recipes explicitly select `Algorithm=5`; algorithm 3 and its serialized results/work semantics are unchanged. Layer/request hashes include the algorithm. Continuations retain their exact recipe and are deterministic replay, including certificate reconstruction. Expanding budgets does not change geographic identity. Nothing upgrades an algorithm-3 token. Maximum patches, visits, samples, cells, splits, passes and frontier capacities are unchanged.

Canonical selection remains minimum required elevation, then ordinal **existing receiver ID**, then source/destination cell ordering. Traversal continues through equal-cost candidates; first encounter does not win. Fixed direction order resolves otherwise identical boundary anchors. Eligible receivers are now the directly reachable terminal boundaries and ordinary exits in the retained interior. IDs beyond an already-absorbing ocean patch are not enumerated or silently promised as alternate candidates. Consequently a version-5 receiver ID/path, canonical interior component, frontiers and work can differ from version 3. This is a semantic experiment, not an algorithm-3 optimization.

Every nonterminal lower/equal route still participates. A certificate cannot erase unrelated patch/domain limits, unresolved source cells, shared-face evidence, topology corrections or competing routes. Minimax heaps, costs and parents fully restart after correction. Existing neighbour reuse is checked against fresh neighbour enumeration in every new analytic case and all six real comparisons.

## Work and reporting bounds

The receiver cache has **256 entries per query**, including unsuccessful proof attempts. Each target is proved once and certificates remain immutable across topology passes. Excess metadata produces `receiver-budget` uncertainty. Proof cost is bounded by this cap times the already-bounded immutable land-feature inventory. No external patch is sampled to establish a certificate. Existing declared-exit caching remains query-local. Transition counters aggregate repeated observations per target rather than retaining every visited face; component counters count actual uncached omitted face expansions, spill counters count examined boundary candidates. Distinct targets with observed transitions are the directly avoided admissions.

Reports separately identify:

- actual admitted patches and distinct encountered terminal targets;
- the **specific old missing target patches** independently discharged by the area proof, including those not encountered in the new execution;
- uncertified old targets whose continuation proof is still required, and whether they remain current frontiers;
- authoritative final solver frontier counts, candidate and final receiver;
- exclusive wet/spill/balance visits, samples, cells, passes/restarts and metadata;
- diagnostic completeness and the final candidate's first unresolved cell.

Do not interpret an old target's absence from a new traversal as a saved patch. The supplementary old-target audit is bounded to the recorded 11/25/19 obligations and is not consumed by the solver. It distinguishes directly observed avoided admissions from proved counterfactual obligations.

The existing detailed spill observer remains capped at 65,536 records / 2,000,000 operations. Its truncation never changes search. Final frontier/candidate reporting comes directly from the result; a separate bounded walk of the returned candidate cells checks its sampling support. Empty late observer fields after truncation are **not** evidence of a clear frontier. Compact reports intentionally retain only twelve illustrative frontier observations per pass, alongside complete authoritative reason counts.

## Focused validation coverage

Separate `Tests/Fixtures/hydrology-receiver-v5.json` freezes 27 receiver cases. Tests additionally exercise the existing 24 analytic topology controls under version 5, including barriers/openings, receiver levels, mixed-resolution face correction, budget/pass continuations and far-coordinate sampling.

Receiver cases cover inland enclosure; a proved ocean beyond a wet corridor; no proof beside ocean-like samples; undeclared edges; absent/returning declarations; lower inland alternatives; equal receiver ordering and reversed callback order; early receiver plus unresolved competing evidence; certified/unknown mixtures; four independent certificates and one missing proof; component versus spill-only transitions; shared-face correction with a terminal receiver; contradictory probes; far-origin replay; node/sample/cell/patch/frontier exhaustion; invalid version/domain/candidate/patch/evidence/terminal/level dependencies; cancellation; parallel queries; entry/caller ordering; and exact profile/clean-neighbour result equivalence. Accepted-world rectangle probes and land-core rejection cover the analytic envelope. Algorithm 3 is tested with a certificate callback that throws if invoked: it never invokes it.

## Six-case comparison and remaining blockers

The full frozen algorithm-3 results match in all six controls. Every version-5 result also matches independent reconstruction and fresh-neighbour full-restart execution. No cap changed: 32 patches, 2,000,000 samples, 800,000 cells, 2,000,000 visits, 16,384 frontiers, 180,000 splits and 64 passes; maximum depth 3, 2 m minimum spacing, 64 m maximum probe spacing and 0.25 m tolerance. Each case still exhausts its visit budget.

| Case (patch) | Component / spill after | Admitted patches A3 → A5 | Old ocean obligations proved | Direct terminal target patches | Missing patch targets A3 → A5 |
|---|---|---:|---:|---:|---:|
| Vast reference (5919) | supported / work limit | 32 → 32 | 8/11 | 4 | 11 → 3 |
| Mountainous broad (4126) | work limit / work limit | 32 → 32 | 20/25 | 9 | 25 → 8 |
| Vast medium/broad (5579) | work limit / work limit | 32 → 32 | 19/19 | 13 | 19 → 0 |
| Mountainous large (10524) | work limit / work limit | 8 → 8 | 0/0 | 0 | 0 → 0 |
| Mountainous far large (10524) | work limit / work limit | 8 → 8 | 0/0 | 0 | 0 → 0 |
| Vast large (12869) | work limit / work limit | 11 → 11 | 0/0 | 0 | 0 → 0 |

Mountainous broad discharges **20/25** old target obligations. `1438, 1564, 1566, 1692, 1694` remain unproved; the last two remain current frontiers. Only old `1310, 1436` were directly encountered as terminals in the new execution. Nine terminal patches were encountered overall. The new missing set is `1188, 1315, 1441, 1442, 1567, 1568, 1692, 1694`: 1,501 distinct patch-budget frontiers plus one node frontier. This is reduced, not closed, domain obligation.

Vast medium/broad discharges **19/19** old obligations. None is claimed as directly encountered in the new execution; thirteen other terminal patches were encountered. The final bounded execution has no patch-budget frontier, but still has a node frontier and incomplete wet closure. Unvisited regions are not certified.

Vast reference discharges **8/11** old obligations. `3360, 3999, 4001` remain unproved; only old `3871` was directly encountered as a terminal. Four terminal patches were encountered overall. Current missing targets are `3868, 3995, 4001`. Final frontiers: **350 patch-budget, 42 resolution-limit, 2 node-budget**. Nominal spill remains **309.9830677247165 m**, receiver level **0 m**, now at `regional-drainage:154b25274d8918f97e74ea3054981de7232b85217196e7beb6b8387ad344bba2:ocean:3869` rather than A3's `ocean:3488`. The changed eligible absorbing boundaries explain this separately versioned receiver choice; neither spill is supported.

The final V5 candidate is unresolved at cell `(67744,83874,size 2)`, patch **5153**, depth 3, **15.625 m** spacing, sampled low/high **15 / 17.092652177830217 m**, residual **0.4149421026955338 m** versus 0.25 m tolerance. The earlier A3 patch-4256 witness belongs to a different candidate path. Neither positive-elevation witness can have a whole-patch ocean proof. The final V5 path ends at `(7999440062.5,-7000528000)`, bed -3500 m. All 42 resolution frontiers remain, with no final component identity or supported receiver. Its detailed observer reaches the diagnostic-work cap; these final counts and path witness come independently from the actual result and materialized cells, not empty late observer fields.

The three large cases reach no certified terminal. Work, samples, cells and unresolved outcomes remain unchanged. No gain is claimed there.

| Case | Wet / spill / balance visits A5 | Passes | Samples A3 → A5 | Cells A3 → A5 | Metadata entries | Debug seconds A3 → A5 |
|---|---:|---:|---:|---:|---:|---:|
| Vast reference | 437,436 / 1,420,447 / 142,117 | 9 | 1,026,049 → 1,092,897 | 286,069 → 312,843 | 39 | 14.79 → 17.32 |
| Mountainous broad | 1,999,785 / 0 / 215 | 5 | 1,890,433 → 1,890,433 | 470,444 → 470,444 | 81 | 18.16 → 17.93 |
| Vast medium/broad | 1,999,929 / 0 / 71 | 5 | 1,932,877 → 1,930,317 | 481,084 → 481,084 | 68 | 20.98 → 22.08 |
| Mountainous large | 1,993,710 / 0 / 6,290 | 43 | 314,857 → 314,857 | 79,263 → 79,263 | 8 | 13.18 → 13.17 |
| Mountainous far large | 1,993,710 / 0 / 6,290 | 43 | 314,857 → 314,857 | 79,263 → 79,263 | 8 | 13.27 → 13.41 |
| Vast large | 1,997,934 / 0 / 2,066 | 38 | 327,343 → 327,343 | 81,384 → 81,384 | 11 | 12.54 → 12.46 |

All real certificates validate; metadata refusals are zero. Each case has one fewer restart than passes. The reference spends 36,055 more visits balancing and correspondingly fewer in spill search as different patches expose correction work. Other visit partitions are unchanged. This is **not a measured general speedup**. The useful result is reduced proved domain obligation, including no observed patch obstruction in Vast medium. Timings are single workstation Debug observations, not runtime promises. Receiver metadata peaks at **81/256** entries; sampled graph/diagnostic memory still dominates. No process-memory saving is claimed.

[Tracked comparison and integrity evidence](experiments/hydrology-receiver-domain.json) retains budgets, A3/A5 hashes, all target dispositions, authoritative frontier/candidate fields, work metrics and bounded pass examples. Generated reports remain under `artifacts/hydrology-receivers/`; timings vary.

## Completed validation

The process launched before the usage interruption completed successfully. On resume, its complete logs and terminal pass markers were inspected against the preserved source; no code redesign was needed. All required suites completed on that source. Counts below are suite totals, not numbers to add to the full Core total.

| Validation | Result |
|---|---:|
| Receiver-focused, including 27 frozen V5 cases and 24 inherited analytic controls | **111,177** checks |
| Six real cases: frozen A3, independent V5 replay, fresh-neighbour comparison and bounds | **37** checks / **6** controls |
| Spill attribution | **658** checks |
| Locality diagnostics | **635** checks |
| Graph-neighbour reuse | **618** checks |
| Topology 3 | **5,276** checks / **30** cases |
| Topology 2 | **2,035** checks / **30** cases |
| Reconciliation 1 | **7,348** checks / **22** cases |
| Frozen coverage | **73** checks / **24** queries |
| Accepted A4 terrain/drainage | **1,098,592** checks / **22** worlds / **158** regional baselines |
| Full Core | **4,813,748** assertions |
| Game and Core builds | **0 warnings / 0 errors** |
| Historical fixture integrity | **11/11** byte-identical to reviewed `4864a9e` and historical SHA archive |

The full suite also passes 62,159 macro-world, 209,365 A4 mountain, 296 macro-chunk, 300,985 drainage, 28 drainage-preview and 41,607 regional checks, alongside gameplay, save, local-water and cave tests. No new rendered/visual acceptance or packaged release is claimed.

Reproduce with `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --receiver-domain`, then `--no-build -- --receiver-matrix`. Other focused switches are `--spill-attribution`, `--locality`, `--graph-reuse`, `--demand-topology`, `--adaptive-topology`, `--reconciliation`, `--hydrology-work`, `--accepted-a4`. Run without a switch for the full Core suite; build with `dotnet build Thornwake.csproj --no-restore`. Logs are in `artifacts/hydrology-receivers/validation/`; durable results and conclusions are recorded here and in the tracked comparison.

## Files and documentation handoff

Implementation: `Game/Core/HydrologyReceiverDomain.cs`, `AdaptiveBasinTopology.cs`, `AdaptiveHydrologyTopology.cs`, `HydrologyReconciliation.cs`. Tests: `Tests/HydrologyReceiverTests.cs`, `HydrologyTopologyTests.cs`, `Program.cs`, separate `Fixtures/hydrology-receiver-v5.json`. The latter is the only new frozen fixture; historical fixtures are untouched. The macro generator, gameplay, renderer, local water, scenes and save formats are unchanged.

Active project status and continuation are centralized in [CURRENT_WORLD_GENERATION_STATUS.md](CURRENT_WORLD_GENERATION_STATUS.md). This checkpoint and the tracked comparison preserve milestone evidence. The test guide adds reproduction commands; the previous spill checkpoint links forward. README, milestones, the world architecture entry point, A4 acceptance and validation index point readers to current status without rewriting their historical measurements. The Master Plan, Supplement V1, Post-Master roadmap, design inbox and historical checkpoint evidence retain their design authority/status.

## Decision and next step

**Retain version 5 as an opt-in experimental prototype only.** It provides bounded proof that materially reduces the recorded broad-case ocean patch obligations. It does not establish complete broad-basin closure, reduce the configured visit cap, or demonstrate a general speedup. Production remains algorithm 3. No historical fixture is regenerated.

Implementation commit: `4c69bbcd1ae8aceb03a11c616ebd09e547de3831`, pushed to `main`. The subsequent [read-only readiness assessment](PLAYABLE_WORLD_READINESS.md) recommends **playable A4 world visual validation next**, with unresolved hydrology carried as explicit known issues. [Current status](CURRENT_WORLD_GENERATION_STATUS.md) is the authoritative continuation index. The previously proposed targeted sampling follow-up is deferred unless a concrete integration or visibly misleading geography issue justifies it; it is not an automatic next task. Neither the playable milestone nor further hydrology was implemented by this assessment.

Finite lake storage, runtime water, river rendering, climate, biomes, caves, geology, editors and other gameplay remain deferred. The known cave-water/terrain visual consistency issue remains open and was not changed.
