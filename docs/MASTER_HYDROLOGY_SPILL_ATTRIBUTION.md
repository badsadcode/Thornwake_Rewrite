# Supplement V1: unresolved spill attribution

Date: 2026-10-03. Reviewed starting commit: `939b0dba823e47ef5216a70db010aebe1d100c4d`.

## Outcome

The six frozen candidates do not share a single spill-search bottleneck. Five never enter minimax because component discovery is incomplete. The two broad components reach deeply submerged ocean samples and exhaust both patch coverage and visits. The supported Vast reference reaches an ocean receiver at a proposed 309.9830677247165 m spill, but its candidate path contains unresolved sampling, its equal-cost search retains domain frontiers, and its visit budget is exhausted. More traversal alone is not established as sufficient.

**Recommendation D, in order: B (bounded receiver-aware domain/continuation contract), then C (targeted adaptive evidence), then reassess A (bounded spill traversal) only if still needed.** The first implementation should address what constitutes a finite basin query when its component or spill search reaches a declared external ocean/receiver. It must not turn a patch-budget edge into an outlet. The precise acceptance target is described at the end.

This is **generation-time world construction**, not runtime fluid simulation. This milestone is diagnostics only: active topology algorithm 3, recipes, charged work, queues, patch admission, refinement, tolerances, receiver selection, full restarts and serialized geographic results are unchanged. No new algorithm, optimisation, water system or downstream gameplay was implemented.

## Observation model and conservative binding

`AdaptiveHydrologyReconciler.ProfileSpill` returns the ordinary result, the existing graph profile and a separate attribution report. Nullable hooks observe actual passes, successful visits, frontier attempts, candidate exits and topology restarts. Candidate evidence walks only the existing parent chain and existing leaf objects; unresolved-cell residuals are reconstructed from the nine cached probes. No observer calls the terrain sampler, loads a patch, creates a root, refines terrain, alters a queue or invokes an unvisited receiver callback.

The ledger partitions **all charged visits** into wet, minimax and balancing visits. Balancing is not counted again as graph traversal. Each pass keeps its canonical spill source, candidate/receiver, last processed minimax cost, heap count at that observation, component-completion visit index and termination status. A correction marks the pass discarded. A subsequent pass starts fresh; old candidates and proof do not carry forward. The final `LastHeapCount` in the node-limited reference is the remaining heap after popping the cell that failed its visit, includes possible stale entries, and is not an estimate of visits needed to finish.

Candidate records distinguish the best encountered nominal proposal from the best encountered **fully resolved sampled path**. The latter requires every existing parent-chain cell to be resolved, the chain to reach the canonical source, and the declared receiver level to be included. Faces on those parent links were admitted by the ordinary search. A fully resolved sampled path is still not continuous-terrain certification. An unresolved nominal proposal is not used as a supported upper bound for declaring other frontiers harmless. Path inspection stops at the first unresolved cell while walking back from the receiver; it does not assert that this is the only unresolved cell on the path.

Each frontier observation stores pass/phase, original reason, cell/patch, depth/spacing, probe minimum/maximum/residual, stage, original solver bound, observed candidate/receiver level, source/outlet distances where available, and existing failed-face evidence. `RetainedBySolver=false` observes an entry omitted by the original frontier capacity; it does not add another geographic frontier. The summary reports capacity uncertainty explicitly if omissions occur. No omissions occurred in the six final results.

Binding uses these rules:

- **PotentiallyBinding:** a valid sampled-model competing bound is no higher than a fully resolved candidate. Equality stays potentially binding because the existing receiver-ID/cell tie-break can change the selected result.
- **NonBindingGivenCurrentBounds:** a valid competing bound is strictly higher than a fully resolved candidate. Distance, encounter frequency, a low sample minimum and failure to revisit a frontier never provide this proof.
- **UnknownBinding:** no canonical spill prefix, no fully resolved candidate, discarded topology or truncated diagnostics prevents a safe comparison.

For a spill frontier, the diagnostic starts from the settled minimax prefix cost and weakens it by the minimum previously encountered unresolved prefix. At pass completion it also accounts for later lower unresolved prefixes: an unproven shortcut cannot be dismissed merely because its endpoint was approached through a more expensive branch. This is a conservative cut bound **within the current sampled graph**, not a continuous terrain envelope or a proof that another sampling version cannot lower the spill. Unresolved probe minima are never substituted as certified terrain bounds. Component-only observations have no canonical minimax source, so they keep their solver bound separately and receive no invented spill bound.

`Binding` is the assessment at observation time; `EndOfPassBinding` uses the final pass candidate and minimum unresolved prefix. `ConservativeLowerBound` contains that final weakened bound. `SolverLowerBound` preserves the exact original value, including sentinels and the low-cell node-budget entry. Historical discarded-pass observations remain available for cost analysis but cannot certify the returned topology.

All six real cases have **zero safely non-binding frontiers**. Their final frontiers are `UnknownBinding`, because the Vast candidate has an unresolved path and the other five have no candidate/source at all. Zero entries in the `PotentiallyBinding` bucket does not mean the unknown entries are harmless. Under the explicit counterfactual that the Vast candidate path became fully resolved at the same elevation, its known competing bounds equal that elevation and would be potentially binding, not non-binding.

Limits: at most 65,536 recorded frontier/candidate observations and 2,000,000 observation/path-inspection operations, plus scalar visit counters and pass records bounded by the existing recipe. Reaching either diagnostic limit stops collection and marks it incomplete without changing solver work or frontiers. Each counted operation performs bounded metadata lookup work; it is not an instruction count. The largest real profile used 31,814 records; the Vast reference used 219,430 observation/path operations. Cancellation is checked on observations, every inspected path node and report finalization. Entry and mid-collection limit regressions preserve complete solver hashes.

## Six-case matrix

Every case uses the original recipe: topology 3, depth 3, 2 m minimum spacing, 64 m maximum probe spacing, 0.25 m tolerance, 32 started patches, 2,000,000 samples, 800,000 allocated cells, 2,000,000 visits, 16,384 frontier entries, 180,000 splits and 64 passes. Nothing was increased. Each measured profile has an independent fresh-domain unprofiled control, matches the frozen coverage hash, and matches the previously archived graph profile.

`W` means WorkBudget; `S` means SupportedAtSampleSpacing. All rows spend exactly **2,000,000 charged visits**. Restarts are component/spill correction-triggered restarts; each is followed by another pass in this matrix.

| Candidate (patch) | Component / spill | Wet visits | Minimax visits | Balance visits | Passes | Restarts component / spill | Candidate |
|---|---|---:|---:|---:|---:|---:|---|
| mountainous/42 large (10524) | W / W | 1,993,710 | 0 | 6,290 | 43 | 42 / 0 | None; minimax not reached |
| mountainous/42 small/broad (4126) | W / W | 1,999,785 | 0 | 215 | 5 | 4 / 0 | None; minimax not reached |
| mountainous/42/far large (10524) | W / W | 1,993,710 | 0 | 6,290 | 43 | 42 / 0 | None; minimax not reached |
| vast-world/7319/far small reference (5919) | S / W | 437,436 | 1,456,502 | 106,062 | 9 | 0 / 8 | Ocean 3488, receiver 0 m, proposed spill 309.983067725 m; unresolved path |
| vast-world/7319/far large (12869) | W / W | 1,997,934 | 0 | 2,066 | 38 | 37 / 0 | None; minimax not reached |
| vast-world/7319/far medium/broad (5579) | W / W | 1,999,929 | 0 | 71 | 5 | 4 / 0 | None; minimax not reached |

Distinct final frontier counts below match the ordinary result. Repeated encounters are reported separately, including discarded passes; they are not additional geographic boundaries. All distinct entries below are `UnknownBinding`; both other final binding buckets are empty.

| Candidate | Distinct final frontiers | Final encounters / duplicates beyond distinct | All-pass encounters / discarded-pass encounters | Lowest raw solver bound | Established competing spill-prefix bound | Distinct unadmitted neighbouring patches observed |
|---|---|---:|---:|---:|---:|---:|
| mountainous large | node 1 | 1 / 0 | 1 / 0 | 198.514269719 | Unavailable: component incomplete | 0 observed |
| mountainous small/broad | patch 3,037; node 1 | 3,038 / 0 | 31,802 / 28,764 | -3,000 | Unavailable: component incomplete | 25 |
| mountainous far large | node 1 | 1 / 0 | 1 / 0 | 198.514269718 | Unavailable: component incomplete | 0 observed |
| Vast small reference | patch 1,245; resolution 42; node 2 | 1,309 / 20 | 25,974 / 24,665 | -3,500 | 309.983067725 | 11 |
| Vast large | node 1 | 1 / 0 | 1 / 0 | 215.889693141 | Unavailable: component incomplete | 0 observed |
| Vast medium/broad | patch 2,337; node 1 | 2,338 / 0 | 31,814 / 29,476 | -3,500 | Unavailable: component incomplete | 19 |

The two Vast node entries describe one exhausted visit: the original `Visit` records the cell's sampled low and the spill caller also records the settled priority. Thus -3,500 m is **not** evidence of a reachable -3,500 m competing spill. The read-only context establishes the 309.983067725 m prefix for that exhausted minimax step. Near/far mountainous results retain their historical tiny floating-point differences; their visits and frontier counts agree. No fingerprint was normalized or regenerated.

| Candidate | Primary observed blocker | Secondary evidence / unknowns | More domain evidence? | Finer sampling? | Traversal alone sufficient? |
|---|---|---|---|---|---|
| mountainous large | Component traversal/replay exhausts visits before spill | 42 correction restarts; unexplored topology not certified | No current patch frontier; global need unknown | Corrections occurred; remaining need unknown | Not established |
| mountainous small/broad | Domain closure of an extended wet component | Visit exhaustion and 4 correction restarts | Yes for current query closure; 25 observed adjacent targets | No final resolution entry; unseen need unknown | No: existing patch frontier remains |
| mountainous far large | Same as near large | 42 correction restarts; unexplored topology not certified | Unknown beyond current traversal | Unknown beyond current traversal | Not established |
| Vast small reference | Mixed: unresolved candidate-path evidence and domain closure | Minimax/balance/replay consume remaining visits | Yes to discharge existing boundary uncertainty under current contract | Yes, additional evidence or a valid envelope is needed for the candidate witness | Not established; multiple known blockers |
| Vast large | Component traversal/replay exhausts visits before spill | 37 correction restarts; unexplored topology not certified | No current patch frontier; global need unknown | Corrections occurred; remaining need unknown | Not established |
| Vast medium/broad | Domain closure of an extended wet component | Visit exhaustion and 4 correction restarts | Yes for current query closure; 19 observed adjacent targets | No final resolution entry; unseen need unknown | No: existing patch frontier remains |

## Vast reference: candidate and unresolved witness

The declared receiver is `regional-drainage:154b25274d8918f97e74ea3054981de7232b85217196e7beb6b8387ad344bba2:ocean:3488`, with receiver level **0 m** and outlet position **(7,999,496,000, -7,000,584,000) m**. It is an actual exit returned by the existing production adapter and visited by search. The adapter requires the receiving coarse route not to return to the candidate basin and binds an ocean receiver to sea level. Its declared receiver contract is therefore valid; the sampled path connecting this basin to it is the unresolved part. No unvisited receiver callback was invoked by profiling.

The best nominal candidate appears in pass 3 and remains at **309.9830677247165 m** through pass 9. No fully resolved candidate witness was observed. The final candidate's backtrace encounters this unresolved leaf after inspecting 1,173 existing path cells:

- Patch **4256**, lattice cell **(66904, 68566, size 2)**, refinement depth **3**, spacing **15.625 m**.
- World cell minimum **(7,999,498,687.5, -7,000,488,328.125) m**; centre **(7,999,498,695.3125, -7,000,488,320.3125) m**. Coordinates come from the existing 2,048 km manifest and lattice, with no extra sample.
- Existing probe range **15.0–15.93181098923958 m**; interpolation residual **0.4659054946197898 m**, exceeding the frozen **0.25 m** tolerance by **0.2159054946197898 m**.
- This is the first unresolved leaf encountered from the outlet back toward the source, not a claim that only one leaf is unresolved.

A low probe range does not prove that the whole cell is safely below the spill: the interpolation residual is not a conservative bound on unsampled terrain. The current evidence cannot distinguish a blocking rise, a different connection or an unobserved opening. It does **not** demonstrate that such a feature actually exists. A lower local saddle in this leaf alone would not establish a lower global spill: the observed prefix has already reached 309.983067725 m. Refinement could affect path validity/receiver choice; a changed continuous/sampling model might also alter upstream bottlenecks. None of that was simulated here.

The final search encounters **62 resolution events representing 42 distinct frontiers**, all at 15.625 m spacing. Their cached-probe residuals range **0.256102389554222–2.051004171647492 m**, all above tolerance. Their current prefix bounds equal the nominal spill. The known patch frontiers have the same bound and lie about **257.5–327.7 km** from the canonical source; resolution observations lie about **90.2–223.9 km** away. Distance supplies no non-binding proof.

The search drains neither its heap nor its uncertainty before the visit limit: the failed final step has priority **309.983067725 m**, with **7,375 raw heap entries remaining after that pop**. The code continues for costs equal to the best candidate and uses receiver ID/cell ordering to choose among ties. This equal-cost ocean exploration helps explain why an already reached ocean receiver does not terminate the query. Changing that policy would be a semantic change, not a harmless diagnostic optimization.

### Reference work trace

| Pass | Wet visits | Minimax visits | Balance visits | Frontier encounters | End |
|---|---:|---:|---:|---:|---|
| 1 | 48,604 | 58,126 | 48 | 2 | Spill correction/restart; no candidate yet |
| 2 | 48,604 | 59,000 | 1,405 | 22 | Spill correction/restart; no candidate yet |
| 3 | 48,604 | 144,611 | 16,177 | 1,431 | Spill correction/restart; ocean candidate |
| 4 | 48,604 | 162,849 | 19,825 | 1,482 | Spill correction/restart |
| 5 | 48,604 | 207,023 | 40,458 | 5,058 | Spill correction/restart |
| 6 | 48,604 | 226,088 | 17,471 | 5,443 | Spill correction/restart |
| 7 | 48,604 | 230,880 | 5,292 | 5,543 | Spill correction/restart |
| 8 | 48,604 | 236,092 | 5,386 | 5,684 | Spill correction/restart |
| 9 | 48,604 | 131,833 | 0 | 1,309 | Visit exhaustion; unresolved spill |

Wet membership is first supported at charged visit **48,604**. A further **1,951,396 visits (97.57% of the total budget)** occur after that first completed discovery: 388,832 repeated wet visits, 1,456,502 minimax visits and 106,062 balancing visits. Across the whole query the exclusive shares are **21.87% wet**, **72.83% minimax**, **5.30% balance**. Older work profiles grouped spill-triggered balancing with spill-phase work; the three columns here deliberately partition actual charges without overlap. Minimax is the largest cost, but it is not the only missing proof.

## Broad-domain diagnosis and bounded coverage estimates

Mountainous small/broad stops with 3,037 patch frontiers at stage **339.96807817166393 m**; every frontier's source leaf has sampled high **-3,000 m**. Vast medium/broad stops with 2,337 patch frontiers at stage **286.420841640562 m**; every source leaf has sampled high **-3,500 m**. These are supported wet connectivity boundaries at their respective query stages, not sampled dry ridges that can be ignored. The sampler's deep-ocean floor is `SeaLevel - OceanDepth`; the frozen profiles use these exact floors, and macro land is constrained above sea level. This identifies an important domain-model issue: the coarse candidate stage can make the selected wet component extend into deep ocean, while current topology requires the entire component to close before it chooses a canonical spill source.

The diagnostics do not run receiver callbacks during component discovery, so they do not claim a specific certified ocean exit for either broad query. The existing sampled connectivity and inspected generator code support the ocean-floor observation; they do not authorize treating any execution boundary as a drain or assuming an external route is valid.

Both queries have already consumed their **32 admitted-patch allowance** when additional patch requests fail, then eventually consume the visit allowance. It is not a case of visits preventing use of still-available patch slots. Their final unknown frontiers request **25** and **19** distinct additional neighbouring patch IDs respectively; the reference requests **11**. These are bounded counts of directly encountered missing targets, not estimates of total patches required for final certification. Continuing them may expose further unknown patches. Full closure size cannot be safely estimated without additional work, which was not performed. A future boundary certificate might discharge some targets without fully materializing them; this report does not assume such certificates exist.

The broad queries have no canonical spill source, candidate or minimax prefix, so their -3,000/-3,500 m raw bounds cannot establish a final spill elevation. They remain `UnknownBinding` for spill ordering while being known obstructions to the current component-closure contract. Optimizing minimax would save zero visits in these two observed runs.

## Large candidates and counterfactual blockers

Large Mountainous near/far and Vast queries stop during component discovery after 42/42/37 correction restarts. Their final node frontiers alone do not prove domain or sampling completeness. Successful corrections leave no unresolved-frontier entry in discarded passes, but the graph profile records **49/49/70 corrected leaves**. This is topology-correction replay cost and cumulative visit exhaustion before spill, not a measured minimax bottleneck. The previous decision against wet-component reachability reuse is unchanged.

Counterfactuals below reason about the recorded state only; no zero-cost solve or expanded-budget run was performed:

| Case | If traversal cost/limit vanished | If domain uncertainty vanished | If sampling uncertainty vanished |
|---|---|---|---|
| Vast reference | Candidate-path uncertainty and patch frontiers still require proof; more steps alone do not discharge them | Unresolved candidate path and exhausted traversal remain | Patch ties and traversal remain; at the same candidate elevation, existing bounds would be potentially binding |
| Mountainous broad | Wet patch frontiers still prevent component closure, so spill remains premature | Current visit exhaustion/replay remains; a subsequent spill witness is not yet known | Patch frontiers and visits still block closure |
| Vast broad | Same: existing wet patch frontiers remain | Current visit exhaustion/replay remains | Patch frontiers and visits still block closure |
| Mountainous large, near and far | Could progress, but new domain/resolution/correction evidence may be encountered; completion is unproven | Current component visit exhaustion remains | Current component visit exhaustion remains |
| Vast large | Could progress; unobserved proof obligations remain unknown | Current component visit exhaustion remains | Current component visit exhaustion remains |

It would be incorrect to say resolution is the only blocker in the reference, that extra traversal alone would certify all six cases, or that a missing frontier category proves unexplored geography is resolved.

## Validation and performance

All commands completed in the requested sequence after the final diagnostic metadata was fixed. No source changes followed this validation; remaining edits are checkpoint/archive documentation.

| Validation | Passed |
|---|---:|
| Focused spill attribution | **658 checks**: 625 analytic topology/equality/visit-partition checks plus 33 focused collector/integration checks |
| Six-case attribution matrix | **30 checks**, six independent controls; complete diagnostic profiles, frozen result hashes, previous graph profiles, visit partition and omission counts |
| Graph neighbour reuse | **618 checks** |
| Locality diagnostics | **635 checks** |
| Topology algorithm 3 | **5,276 checks / 30 frozen cases** |
| Topology algorithm 2 | **2,035 checks / 30 frozen cases** |
| Reconciliation algorithm 1 | **7,348 checks / 22 cases** |
| Frozen coverage | **73 checks / 24 queries** |
| Full Core | **4,702,571 assertions** |
| Game build | **0 warnings / 0 errors** |
| Historical fixture integrity | **11/11** byte-identical to reviewed `939b0db` and the historical SHA-256 archive |

Focused checks include lower/higher/equal binding bounds, receiver levels, no supported candidate, unresolved candidate-path location, later uncertainty weakening earlier proof, invalidated topology, domain/patch failures, node-only minimax exhaustion, frontier omissions, entry/mid-collection diagnostic limits, cancellation during frontier/candidate/path work, repeated and parallel determinism, and complete disabled/enabled result equality for all 24 existing analytic topology cases. Full-suite terrain, drainage, region/chunk reconstruction, local water, cave, gameplay and save tests also passed. No visual-water acceptance is claimed.

Debug query controls/profiles total **78.47/83.82 seconds**, about **6.8% observation overhead**. Times exclude domain construction and serialization, are single workstation observations and are not runtime promises. The observation cap completed in every case. The full JSON is about 99.3 MiB due to deliberately retaining repeated/discarded evidence; its lossless archive is about 1.91 MiB. No generated report is consumed by the solver.

## Files, archive and reproduction

- `Game/Core/HydrologySpillAttribution.cs`: separate bounded collector, binding predicates, existing-probe/path inspection and profile entry point.
- `Game/Core/AdaptiveBasinTopology.cs`, `AdaptiveHydrologyTopology.cs`: optional read-only hooks and public developer method.
- `Tests/HydrologySpillAttributionTests.cs`, `HydrologyTopologyTests.cs`, `Program.cs`: focused tests, independent controls and suite registration.
- This checkpoint, the previous checkpoint's continuation link and the world-generation test guide.
- [Compact six-case report](experiments/hydrology-spill-attribution.json) retains summaries, all pass counters/candidates, representative frontier records and the full-report hash. [Complete lossless report](experiments/hydrology-spill-attribution-full.json.gz) contains every observation, including discarded passes. Decompressed SHA-256: `7cb09321e81fbc58c581c43f97b741a6e65fc07d802f5327084be7113213dd5e`.

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --spill-attribution
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --spill-attribution-matrix
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --graph-reuse
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --locality
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --demand-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --adaptive-topology
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --reconciliation
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --hydrology-work
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build
dotnet build Thornwake.csproj --no-restore
```

Reports and logs are under ignored `artifacts/hydrology-spill-attribution/`: `analytic.json`, `matrix.json`, `focused.log`, `matrix.log`, `graph-reuse.log`, `locality.log`, `topology3.log`, `topology2.log`, `reconciliation1.log`, `coverage.log`, `full-core.log`, `game-build.log`. Matrix timings will vary; geographic hashes and attribution records are deterministic. The gzip archive was round-trip checked against the original raw report.

## Exact next V1 implementation recommendation

**D: B first, then C; assess A only after those proof obligations are understood.** Start a separately reviewed **bounded receiver-aware query-domain/continuation prototype** for generation-time basin reconciliation. Its first acceptance target is to represent a certified transition into an existing declared external ocean/receiver without requiring the basin's wet closure or same-cost spill traversal to enumerate the surrounding ocean. Keep unproven patch boundaries explicit, preserve legitimate lower competing routes, and define deterministic equal-elevation receiver selection at the receiver-boundary level. Do not merely stop at the first receiver, invent drains at query edges, or increase limits. This is a future semantic contract, so retained algorithm 3 and every historical fixture must remain unchanged; any implementation changing results requires explicit separate versioning and comparison.

Use the same reference and two broad candidates, plus analytic inland enclosure, ocean escape, returning receiver, lower alternative, tie selection and incomplete boundary cases. Require a bounded coverage/evidence outcome, not a claim that all missing patches became resolved. The 11/25/19 observed missing patch targets and ocean-floor frontiers are the concrete first test inputs. This targets the shared domain obligation affecting all three priority cases; it does not resume component reachability reuse.

Next, address **C** at unresolved candidate-path/competing boundary evidence such as patch 4256's 15.625 m leaf, keeping uncertainty until a valid sampled/envelope proof exists. Do not weaken the current tolerance to manufacture support. Even a better domain contract cannot certify the recorded unresolved path by itself. Only then reprofile whether **A**, bounded spill traversal work, is the remaining bottleneck. Minimax state reuse is not recommended by this checkpoint.

No recommended implementation has started. Finite lake storage, river meshes, runtime water, climate, biomes, caves, geology, editors and other future systems remain deferred. The known visual cave-water/terrain issue remains open. Stop after this diagnostics milestone.

## Follow-up implementation

The B-first prototype is now documented in [MASTER_HYDROLOGY_RECEIVER_DOMAIN.md](MASTER_HYDROLOGY_RECEIVER_DOMAIN.md). Topology 5 adds bounded, explicitly versioned ocean-area certificates; topology 3 and this historical diagnostic report remain unchanged. Read the newer checkpoint for current results and the next V1 step.
