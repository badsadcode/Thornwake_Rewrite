# Supplement V1: bounded wet-component reachability experiment

Date: 2026-10-03. Reviewed starting commit: `484cbd5f7dea9e523522fe2469c3c4074acfae9f`.

## Decision

**Reject the full-tree repair experiment. Keep reviewed topology algorithm 3 with its existing adjacency cache and full wet/spill restart.** All active Core source, tests and historical fixtures were restored byte-for-byte to the reviewed commit. Algorithm 4 is not accepted by the active recipe validator and no production/default caller is changed.

The corrected algorithm reduces repeated component visits in some cases and explores additional terrain, but its whole-tree membership/closure scans are expensive. Large cases are 73–88% slower than the retained cache, hit the fixed 16-million repair-work ceiling, and gain no supported component/spill. Broad cases hit the 131072-node dependency capacity before useful reuse. The Vast reference saves all repeated wet visits but merely spends the saved visit budget on still-unresolved minimax work. Raising either limit or adding a general dynamic graph engine would conceal this experiment's failure rather than satisfy its scope.

The corrected implementation, regression tests and separate algorithm-4 analytic fixture are preserved in a noncompiled patch for reproducibility. The rejection is based on cost/coverage, not a claim that the remaining proof cases failed. No runtime experimental path remains half-integrated. Treat topology version 4 as the archived experimental contract; a later incompatible experiment must use another explicit version rather than reinterpret these fixtures or replay recipes.

## Scope and version boundary

This experiment concerns generation-time wet-component discovery only. The retained algorithm-3 neighbour-expansion cache is unchanged. Canonical spill source selection, minimax costs/parents, receiver and outlet comparisons, paths and crossings always restart. No lake storage, runtime water, river representation, terrain generation, gameplay or later systems are changed.

The user explicitly authorized a separate experimental **topology algorithm 4** after inspection established that the existing frozen result includes visit counts and budget-limited membership/frontiers. This is unrelated to accepted **terrain algorithm 4**. Saving charged visits changes the execution contract and can spend the same limits on additional terrain exploration. Historical fixtures are not regenerated. The prototype has separate analytic expectations and compares supported geography with its full-restart control; complete result replay remains deterministic within the experimental version.

## Dependency model and proof boundary

A query-local discovery tree records each reached wet leaf, its generation, discovery parent and whether its expansion finished. Parents precede children in discovery order. A repair scans that order transactionally after shared-face balancing. An unchanged canonical seed anchors the proof. A surviving leaf must remain a resolved wet leaf at the recorded generation, and its parent must already have survived through a positive shared face. A retired ancestor invalidates every descendant, including previously preserved descendants.

Repair first constructs the entire surviving membership index, then checks closure. A geometrically unchanged wet neighbour may have lost its discovery ancestor elsewhere: a surviving face to that orphan must be requeued even when its cached adjacency is still geometrically valid. The targeted seven-node/two-branch regression failed before this correction and passed afterwards; draining the repaired queue matches an independent six-node cold flood fill. Earlier, pre-fix benchmark observations are obsolete and excluded from the decision.

Only a completely expanded surviving leaf whose entire recorded local adjacency remains valid and contains no unresolved neighbour can skip a charged visit. Other surviving boundary cells are queued for ordinary face exploration. Missing cached expansions are re-explored, never interpreted as closed boundaries. Unknown roots, domain boundaries, sample/cell/patch limits and resolution frontiers are reobserved by that original walker.

The experiment deliberately does not repair orphan trees. An orphan loses membership and can be rediscovered only through a newly validated boundary edge, acquiring a new discovery parent. Failure to establish a seed-connected proof never preserves that branch. Invalid parent faces, missing discovery dependencies and exhausted repair work fall back to full wet-component restart. Changed canonical seed also restarts. Cancellation propagates and publishes no partly repaired result. Minimax is never reused.

This proof is for sampled, monotonic leaf refinement inside one immutable query, not continuous terrain correctness, player terrain edits, cross-query mutation or runtime fluid connectivity. It relies on leaves never coarsening or recycling identities during that query.

## Bounds and cost accounting

The tree holds at most `min(131072, recipe.Cells)` nodes. Repair uses replacement node collections of the same bound; old and replacement containers coexist during a repair. Parent links are acyclic and point to earlier surviving entries. The existing adjacency cache remains bounded by 131072 entries and 1048576 neighbour references. No new global, persistent or cross-query state is introduced.

The experiment allows at most 16000000 dependency checks per query, counting both node and adjacency-reference validation. This is separate from charged graph visits and is reported explicitly, not hidden as free computation. Reaching the node-capacity or repair-work bound disables reuse for the rest of that query. The normal visit/sample/cell/split/pass/patch/frontier caps remain unchanged. Continuations serialize only the explicit recipe and seed query; they reconstruct the tree afresh.

Diagnostics distinguish charged/repeated wet visits, spill visits, adjacency reuse, repair checks, preserved members, invalidated descendants, validated surviving parent edges, requeued boundaries, seed changes and fallback restarts. The raw `PreservedCells` diagnostic counts validation/preservation attempts, including provisional entries from a repair that later rolls back at its work limit. It is neither unique membership nor an exact count of skipped visits. Use charged and repeated visit counters for observed traversal savings; do not subtract this provisional counter from baseline work. Validated parent edges are not orphan reattachment counts. Failed orphans are dropped, not force-attached.

## Validation and observations

Focused corrected experiment: **689 assertions passed**, comprising 625 topology assertions over 24 independently frozen algorithm-4 analytic cases, 16 dependency-proof checks and 48 extra integration/budget/cancellation/replay/concurrency assertions. No historical fixture was regenerated. The proof tests cover retirement high in a tree, descendants and previously preserved descendants, unrelated branches, fresh alternate-parent rediscovery, repeated invalidation, unchanged and changed seed, missing dependencies/expansions, uncertain neighbours, bad parent faces and transactional repair-budget exhaustion. Integration cases cover barriers/openings, receiver binding, shared-face correction, corner isolation, far-origin/nonbinary reconstruction, domain/resolution uncertainty, all configured budget classes and cancellation in repair/preservation/invalidation.

The test-only forest fixture inspects the dependency model directly. Its alternate-edge traversal is a controlled graph fixture, not a new production topology algorithm. Real terrain scenarios separately compare every geographic result field with the full-restart control. Serialized work metrics and discovery-order diagnostic evidence are intentionally excluded from cross-execution geography comparisons; experimental result determinism and continuation replay compare complete hashes.

The supported real-world component was independently reconstructed after benchmarking: **4 checks passed**, including exact equality of all **48604** member addresses and the canonical query cell against algorithm 3, plus complete algorithm-4 result fingerprint replay. No new candidate gained supported component or spill coverage. All old fixture files were also checked byte-for-byte against the reviewed commit: **11 unchanged**.

The full corrected matrix passed **48 checks across 18 timed queries**, each with a separate untimed instrumented reconstruction. Both algorithm-3 modes match their existing frozen full-result fingerprints; the experimental timed results match independent profiled full-result hashes. The graph regression after restoring source passed **618 assertions**. Game build: **0 warnings, 0 errors**. Full restored Core suite: **4701278 assertions passed**, exit code 0. Within that run: topology algorithm 3 **5276 / 30 cases**, topology algorithm 2 **2035 / 30 cases**, reconciliation algorithm 1 **7348 / 22 cases**, frozen coverage **73 / 24 queries**, accepted A4 **1098592**, drainage **300985**, regional drainage **41607**. Historical frozen results remain stable.

## Corrected representative measurements

Same machine/Debug build and original six candidates. Each query retains the existing limits: 32 patches, 2000000 samples, 800000 cells, 2000000 charged visits, 16384 frontiers, 180000 splits and 64 passes; depth 3, 2 m minimum spacing, 64 m probe spacing, 0.25 m height tolerance. Times exclude coarse/regional/domain construction and include the actual query. Instrumented executions are separate. Single-run timings are observations, not statistical performance guarantees.

Mode 0 is safe full restart without adjacency reuse, mode 1 is retained algorithm 3 with adjacency reuse, mode 2 is the corrected experimental reachability path with adjacency reuse. All comparisons below use mode 1 as the retained baseline unless shown otherwise. Every query consumed the same 2000000 charged-visit cap.


| Candidate / patch | Full restart seconds | Retained A3 seconds | Experimental seconds | Repeated wet visits A3 → experiment | Full wet replays A3 → experiment | Component / spill |
|---|---:|---:|---:|---:|---:|---|
| mountainous/42/large (10524) | 14.24 | 12.16 | 22.88 | 1922605 → 1772833 | 42 → 13 | budget / budget |
| mountainous/42/small (4126) | 16.27 | 16.54 | 16.52 | 1546692 → 1546692 | 4 → 4 | budget / budget |
| mountainous/42/far/large (10524) | 14.00 | 11.98 | 22.19 | 1922605 → 1772833 | 42 → 13 | budget / budget |
| vast-world/7319/far/small (5919) | 14.22 | 13.46 | 14.02 | 388832 → 0 | 8 → 0 | supported / budget |
| vast-world/7319/far/large (12869) | 13.17 | 11.46 | 19.82 | 1924994 → 1798891 | 37 → 16 | budget / budget |
| vast-world/7319/far/medium (5579) | 20.06 | 19.76 | 19.94 | 1535749 → 1535749 | 4 → 4 | budget / budget |

Repeated wet visits avoided: **149772** for each mountainous large case, **388832** for the Vast reference, **126103** for Vast large and **0** for either broad case. Full wet replays declined by **29**, **29**, **8**, **21** respectively; these are observed count differences between runs reaching different amounts of topology, not matched corrections removed one-for-one. Total topology passes actually increased on large/reference cases. Spill traversal always restarts. Across the six candidates, that is **814479 fewer repeated wet visits** and **87 fewer full wet replays**, while total timed query work rises from **85.37 s to 115.36 s (35.1%)**. These are single-run aggregate observations; no candidate gains supported component/spill coverage.

| Candidate | Samples A3 → experiment | Allocated cells A3 → experiment | Passes A3 → experiment | Experimental repair checks | Experimental frontier counts |
|---|---:|---:|---:|---:|---|
| mountainous/42/large | 314857 → 906460 | 79263 → 226973 | 43 → 64 | 16000000 | node-budget: 1 |
| mountainous/42/small | 1890433 → 1890433 | 470444 → 470444 | 5 → 5 | 0 | node-budget: 1, patch-budget: 3037 |
| mountainous/42/far/large | 314857 → 906460 | 79263 → 226973 | 43 → 64 | 16000000 | node-budget: 1 |
| vast-world/7319/far/small | 1026049 → 1048487 | 286069 → 293336 | 9 → 11 | 2916240 | node-budget: 2 |
| vast-world/7319/far/large | 327343 → 848089 | 81384 → 210519 | 38 → 61 | 16000000 | node-budget: 1 |
| vast-world/7319/far/medium | 1932877 → 1932877 | 481084 → 481084 | 5 → 5 | 0 | node-budget: 1, patch-budget: 2337 |

Larger sampled areas on the large candidates demonstrate additional legitimate exploration under the same limits, but do not certify useful final lake topology. The reference uses 48604 charged wet visits and 1839841 spill visits in the experiment; spare work is spent on further spill traversal and balancing. Its observed frontier list changes from node/patch/resolution entries to node entries, because the bounded traversal reaches different places. Missing categories in a partial result are **not** proof that those uncertainties are resolved.

The detailed JSON retains allocated/admitted counts, every graph pass, correction locations, adjacency statistics, frontier counts and separate dependency diagnostics. Dependency retirement tests verify previously preserved descendant invalidation even though the final real matrix records zero in that particular counter. The whole-tree model validates/requeues boundaries; it does not track a separate count of successfully reattached orphan branches or classify every repair fallback by cause. Those diagnostic limits are explicit; no successful orphan-reattachment count is inferred from validated parent edges.

## Remaining bottleneck and exact next V1 step

Keep algorithm 3 as the active baseline. Large candidates still repeatedly replay components after late shared-face corrections. Broad candidates are dominated by large admitted regions and patch/visit limits; the reference already has supported wet membership but remains limited by fully restarted spill search and unresolved bounded evidence. This experiment moved some work into millions of dependency scans without producing a supported real spill.

**Next recommended step: a diagnostics-only locality assessment, not another routing implementation.** For the same frozen large cases, measure the corrected leaf's descendant set and the surviving faces bordering removed membership, and estimate a bounded reverse-dependency index's memory/work. Include the alternate-face closure regression from this archive as a non-negotiable proof requirement. Compare estimated affected-only repair work with the observed 16-million full-scan exhaustion before proposing a new versioned implementation. Keep minimax on full restart. If localized proof still requires scanning most of the component or exceeds the small query-local budget, stop pursuing reachability reuse and document the sampling/domain requirements of unresolved spill candidates instead. Do not raise budgets or begin lake representation to hide the lack of supported spill topology.

No minimax reuse, lake storage, runtime fluids, climate, biomes, caves, geology, rendering or editor systems were started. The existing visual water/terrain consistency issue remains open and outside this experiment.

## Reproduction and review archive

- [Corrected rejected patch](experiments/hydrology-reachability-v4-rejected.patch) includes the exact experimental Core hooks, dependency model, focused/proof/real-membership tests, test CLI registration and the separate 24-case algorithm-4 fixture. It is documentation data, not compiled code.
- [Corrected matrix](experiments/hydrology-reachability-v4-comparison.json) stores all 18 observations and per-pass diagnostics. Pre-closure-fix observations are deliberately excluded.
- [Historical fixture hashes](experiments/hydrology-reachability-historical-fixtures.json) records the 11 unchanged inputs.

To reproduce, use a disposable checkout of reviewed commit `484cbd5f7dea9e523522fe2469c3c4074acfae9f`, copy the archive patch into that checkout, and apply it there. Do not apply the rejected experiment to an active production working tree. `git apply --check` and the reverse check both passed for the archived patch. SHA-256: `15322536ec93fec696830f1889c722acec1dd10e8d5994051867633c2a75a215`.

```powershell
git apply --check hydrology-reachability-v4-rejected.patch
git apply hydrology-reachability-v4-rejected.patch
dotnet run --project Tests/Thornwake.Core.Tests.csproj -- --reachability
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --reachability-comparison
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --reachability-world-proof
```

On the retained source, normal validation remains `--graph-reuse` and the full Core suite, followed by `dotnet build Thornwake.csproj --no-restore`. Experimental flags are not registered in the active source. Full Core includes topology algorithms 2/3, reconciliation 1, frozen coverage, accepted terrain, drainage, regional reconstruction, gameplay and save tests.

Generated local logs remain under ignored `artifacts/hydrology-reachability/`: `closure-before.log`, `closure-after.log`, `focused-experiment.log`, `comparison.log`, `world-proof.log`, `restored-focused.log`, `restored-full-core.log`, and `restored-game-build.log`. The archive and this checkpoint suffice to reproduce the experiment after a fresh checkout.

Stop at this checkpoint; no new topology version is promoted.

## Locality assessment follow-up (2026-10-03)

The diagnostics-only follow-up is complete in [the locality assessment checkpoint](MASTER_HYDROLOGY_LOCALITY_ASSESSMENT.md). It preserves this rejection and all algorithm-3 results. Its measured index costs and final recommendation supersede the next-step guidance above; no new reachability algorithm was implemented.
