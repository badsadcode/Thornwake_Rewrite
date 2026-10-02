# Supplement V1: bounded multi-patch hydrology reconciliation

2026-09-30 completed diagnostic/foundation checkpoint, continuing reviewed commit `d807567daee105900c352bb71b32d8fdf5511948`. Master Rework + Supplement V1 remain authoritative. No Supplement V2 or runtime water was implemented.

> **Continuation checkpoint:** [Adaptive topology and deterministic continuation](MASTER_HYDROLOGY_ADAPTIVE_TOPOLOGY.md) records the additive algorithm-2 milestone. The algorithm-1 results and limits below remain historical evidence.

## Scope and preservation

The interrupted local implementation was continued in place. Three new Core files implement the layer: `HydrologyReconciliation.cs` (recipe, contracts and frozen-world adapter), `BasinReconciliationSearch.cs` (bounded component/spill search), and `AdaptiveHydrologySampler.cs` (anchored profiles). The test runner registers the new tests. Every pre-existing Core source file and accepted fixture file remains byte-identical to the reviewed commit, including A3/A4 terrain, both coarse/regional baselines and inspection v1. The default remains accepted A4; saved algorithms are not migrated. No normal gameplay, Godot lab UI, terrain, water solver or renderer was changed.

This layer can reconcile a component and its lowest reachable escape **on its sampled model**, or explicitly report why it cannot. It is not permission to fill every candidate basin. Some real-world cases remain truncated, and newly discovered adaptive barriers/openings can contradict the fixed graph. Those are deliberate result states, not silently accepted lake levels.

## Frozen identity and bounded work

`HydrologyReconciliationRecipe` format 1 / algorithm 1 embeds the complete inspection dependency, including regional/coarse versions, expected coarse fingerprint, frozen terrain manifest/profile and subdivision factor. It also freezes patch, grid-sample, node, frontier, adaptive and per-face sample budgets plus explicitly supplied adaptive settings. Restore rebuilds/validates the dependency; mixed algorithms, changed seed/profile dependencies and unsupported settings are rejected. There is no mutable global cache.

Default limits: 12 patches, 300,000 distinct grid vertices, 400,000 graph visits, 8,192 reported frontier entries, 20,000 additional adaptive evaluations and 127 samples per inspected shared face. Patch memory is additionally capped at 1,048,576 quads for any valid recipe. Default fine patches have 128 x 128 quads / 129 x 129 vertices. Sampling caches, component labels, graph work and port caches are invocation-local. Cancellation is checked during sampling, labelling, traversal, adaptive work and graph-report construction.

Grid samples are charged once per integer global address, including shared vertices. Baseline regional ports are cached per source patch and their sampling count is reported separately. Their work is also bounded by inspected patches; they do not scan all basin exits. Node visits count wet/spill graph traversal, not the separate bounded per-patch labelling scan. No fine grid covering the continent is constructed.

Changing frozen budgets/settings intentionally creates another layer identity. Production migration and stable identity across different layer versions are still separate concerns.

## Components and result states

At the hypothetical coarse candidate stage, a quad is wet only when all four fine-grid corners are below it. Four-neighbour traversal crosses patch faces; touching only at a corner does not connect pools. Dry sills block this traversal. Connected wet cells can extend outside the original coarse basin footprint: coarse ownership is not a physical wall.

The result reports local component anchors, visited quad counts, cross-patch component links, the query anchor, unresolved frontier and diagnostic metrics. Both sides use the same global integer sample lattice. Inverting far-origin world coordinates uses rounded lattice addresses, avoiding false out-of-face targets with nonbinary spacing.

`ComponentStatus` and `SpillStatus` are separate:

| Result | Meaning |
| --- | --- |
| Component `Complete` | The selected wet component is fully discovered on the fixed fine grid at this candidate stage. This is not whole-lake hydraulic approval. |
| Component `TruncatedByBudget` | Some relevant cells/patches are uninspected. Explicit frontier entries and omitted-entry counts prevent a closure claim. |
| Component `OpenDomainBoundary` | Wet cells reach an undefined external domain boundary; that boundary is neither a wall nor an invented drain. |
| Component `Blocked` | The selected fine quad is dry, or the patch contains no candidate wet quad. |
| Component `Contradictory` | Finer sampled dry terrain invalidates the provisional wet-component interpretation. |
| Spill `ReconciledAtSampleSpacing` | The minimum to a declared destination is resolved on the bounded sampled graph, and the selected adaptive path does not contradict it beyond the frozen height tolerance. Check adaptive stop/spacing metadata as well. |
| Spill `TruncatedByBudget` / `UnresolvedDomainBoundary` | An uninspected continuation could improve/tie the proposed escape, or finer-path work is incomplete. A returned spill is only a provisional proposal. |
| Spill `Contradictory` | Adaptive terrain shows a materially higher barrier or lower escape than the fixed graph. Re-evaluate topology/outlet ordering before accepting a lake level. |

`FinalComponentId` exists only for a complete, non-contradictory **fixed-grid component**. It combines the layer/candidate identity and canonical minimum global quad anchor. It does not depend on which tributary/caller found the component first. Partial discoveries expose stable local anchors but do not fabricate a global final ID. A final component ID can coexist with an unresolved spill; consumers must not confuse geometry identity with lake/storage authorization. `GridReachesDeclaredOutletAtCandidateStage` explicitly reports a sampled-grid observation, not gameplay flow.

## Reachable spills and bilateral ports

After component discovery, minimax search begins at a canonical low quad inside that component. It minimizes the highest conservative quad elevation along a connected path. It considers explicit receiver targets as they become reachable, using stable address/identity tie breaking. A low face behind a higher intervening ridge cannot win merely because its own elevation is low.

Production targets are existing regional downstream ports in recipient patches whose coarse downstream chain never returns to the candidate basin, or sampled coarse ocean destinations. These are explicit abstract continuation contracts, not independent sources or proof of the entire downstream physical channel. Search boundaries and missing patches are never targets. If an unresolved frontier could have a lower or equal escape cost, the spill remains unresolved.

Crossings belong to the new layer. A shared face is inspected in canonical coordinate order with a frozen per-face adaptive budget, so both sides select the same point/identity. Confirmed original ports retain their identity and exact position. Other crossings get canonical two-patch/face identities scoped to the candidate/layer. Existing regional ports are reported and never edited. Crossing proposals remain provisional when the enclosing result or their face profile is unresolved.

Spill reports retain the fixed-graph elevation, finer-path requirement, conservative required elevation, explicit destination/receiver level, path, shared crossings, original outlet proposals and contradiction reasons. Both graph and profile requirements include the explicit receiver level; lower terrain cannot remove an ocean or downstream boundary constraint. The adaptive assessment separately retains the raw sampled bed maximum. `RequiredElevation` is conservative (`max(fixed graph, profile requirement)`); it must not be mistaken for a final physical lake surface, particularly when the profile reveals a lower opening.

## Adaptive refinement

The sampler inserts terrain samples into anchored polylines; it does not smooth through terrain or carve channels. It probes long spans, then recursively spends samples around deviations from linear height, uphill segments, candidate-stage crossings and sharp turns. Exact network anchors are retained. Defaults are depth 8, minimum spacing 2 m, maximum probe span 64 m and height tolerance 0.25 m; analytic fixtures use tighter frozen limits.

Selected spill paths and their shared crossing faces receive additional samples. Face inspection can locate an opening between old grid vertices. The algorithm reports `SampledTolerance`, `DepthOrSpacingLimit`, or `SampleBudget`, plus the largest unprobed span. These are sampling qualifications, not proofs about arbitrarily narrow terrain.

The acceptance examples deliberately expose both directions of disagreement: a fixed-grid 4 m escape gains a 20 m narrow barrier, while another gains a sampled 1 m path through a narrow opening. Both invalidate final spill approval; the latter still correctly reports a path below the hypothetical 3 m candidate stage. A detected dry sample within a provisional wet component also revokes its final component ID. No old fixture or terrain is changed to remove these contradictions.

## Fixtures, diagnostics and validation

`Tests/Fixtures/hydrology-reconciliation-v1.json` freezes **22 outputs**: 19 analytic scenarios and three accepted A4 world cases. Coverage includes complete three-patch connectivity, truncated searches, disconnected pools under one candidate, a reachable spill versus an attractive unreachable low face, confirmed/replacement ports, narrow barrier/opening diagnostics, several tributary entries, all budget types, corner-only contact, dry shared faces, open domain boundaries, explicit outlet reachability, receiver levels above the bed, and far origins. Additional assertions cover cancellation during work, concurrent reconstruction, parameter identity, incompatible dependencies, JSON recipe reconstruction and far-origin nonbinary boundary coordinates.

The interrupted seven-case draft was retained locally as `artifacts/hydrology-reconciliation/interrupted-seven-case-fixture.json`; it was an unaccepted draft of this new layer. Only that new fixture was finalized after analytic review. No historical accepted expectation was regenerated.

| Validation | Result |
| --- | --- |
| Focused reconciliation | **7,348 assertions**, all 22 frozen outputs match |
| Accepted A4 terrain/coarse/regional | **1,098,592 checks**, 22 terrain/coarse worlds and 158 regional tiles preserved |
| Inspection v1 | **2,431 checks**, all existing outputs preserved |
| A3 coarse / regional | **300,985 / 41,607 checks**, original fixtures retained |
| Full Core suite | **4,693,257 assertions passed** |
| Game build | **0 warnings / 0 errors** |
| Existing-source/fixture byte comparison | All pre-existing Core sources and fixture files unchanged |

Run `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --reconciliation`. It verifies expectations and writes the readable developer report at `artifacts/hydrology-reconciliation/report.json`. Normal runs never overwrite source fixtures. `--reconciliation-spike` runs analytic assertions and emits a candidate matrix for deliberate review; it is not a substitute for fixture validation. The explicit recorder refuses an existing fixture.

Report fields include component graph/identity, unresolved frontier, original/reconciled crossings, candidate/spill elevations, adaptive profiles, metrics and contradiction reasons. Local evidence includes `full-core.log`, `game-build.log` and a visually inspected `acceptance-profiles.png` comparing the three analytic sill cases. Generated reports/plots remain excluded from Git. No Godot UI changed, so no new rendered engine smoke was necessary; the existing gameplay/save and diagnostic regression suites ran in the full Core validation. These tests do not certify playable water correctness.

## Performance observations

Validated Debug Core run on this workstation; timings include the reconciliation query but exclude initial coarse/refiner construction, recipe restoration, verification repeats and rendering. World cases deliberately use a 16-patch cap (otherwise the frozen standard settings).

| Case | Component / spill | Patches | Local components | Grid + baseline port + adaptive samples | Adaptive subdivisions | Time |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Mountainous/42, patch 4126 | truncated / truncated | 16 | 16 | 264,321 + 315 + 0 | 0 | 1.26 s |
| Mountainous/42 far origin, patch 4126 | truncated / truncated | 16 | 16 | 264,321 + 315 + 0 | 0 | 1.21 s |
| Vast World/7319 far origin, patch 5919 | complete / truncated | 16 | 8 | 263,553 + 756 + 7,139 | 2,515 | 2.25 s |

The first two candidates spread beyond their small coarse-basin footprint at the hypothetical stage. The third discovers the component but cannot rule out a better outlet beyond its budget. None is approved as a filled production lake. Small analytic cases take roughly 0.1-3.5 ms warm (first-use/JIT was around 30 ms), with 2-15 inspected patches. These are development observations, not frame-budget promises.

## Known limits and exact next Supplement V1 step

This is a coherent **bounded diagnostic reconciliation milestone**, not complete final hydrology for every world. Sampling still misses features narrower than its probes. Conservative quads may overestimate sills and footprint separation. A discovered adaptive change is reported but is not yet inserted into a new adaptive connectivity graph. The sampled path is not final metre-scale channel geometry. Search state is reconstructible from recipe/query, but truncated searches do not yet persist resumable frontier work. Identities are layer-scoped; cross-platform bitwise equality and production migrations remain unproven.

**Recommended next step:** review the new component/outlet results, then make a bounded adaptive-topology follow-up for contradictory cases: insert the discovered barrier/opening evidence into connectivity and minimax ordering, and validate agreed replacement outlets. Add deterministic resumable/expanded frontier handling for the large candidates without treating budget edges as geography. Preserve this version's fixture outputs when introducing changed semantics.

Finite lake stage/storage descriptors should then consume only appropriately complete, non-contradictory geometry/spill results; stage-dependent component changes also need explicit handling. Continue afterward with rate-limited upstream/spring/river inflow descriptors, explicit downstream exports, the documented terrain-water connectivity regression, and a separate local realization spike. Do not create volumes or gameplay water from unresolved outputs. The known cave visual/connectivity issue remains open. No V2, climate, biome, geology, runtime water, erosion, editor or Post-Master gameplay work was started.
