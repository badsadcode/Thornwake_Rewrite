# C2 drainage checkpoint and source audit

2026-09-29. Read with `THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md`, which overrides the older Master Plan and conflicting historical notes. C2 is in progress; C1 remains the stable macro-generation milestone.

## Audit before this continuation

The Master Plan and Supplement were read before edits. Current macro generation, drainage, tests, streaming/query boundaries, local-water interfaces and development/checkpoint notes were inspected. All existing `Game/`, `Tests/` and `Content/` files matched the PRE_WORLDGEN_RESUME backup at the start of this continuation. The forensic source review and backup are historical records and are not being rewritten.

1. **Reuse:** algorithm-3 macro terrain, frozen profile manifests, global double coordinates, bounded multi-observer residency planning, shared region samples, existing valley gameplay/save systems and isolated cave/water labs. Preserve working harvesting, crafting, building, cooking and interaction. No terrain replacement is justified by this milestone.
2. **Extend:** the interrupted `DrainageAnalysis` / `MacroDrainage` implementation already provides four-neighbour priority-flood routing, explicit outlets, contributing area, candidate basin spill/storage, reach prerequisites, region windows and immutable reconstruction recipes. It has tests and a 15-world diagnostic report, but is not integrated with the map lab or local water. Independent correctness and fixed reconstruction fixtures were missing. The previous source-review run passed 3,307,298 assertions, including 274,397 drainage checks; that is historical evidence, not a fresh verification of this continuation.
3. **Conflicts / limits:** coarse routing heights must not become visible terrain or actual lake surfaces. The current cave pools are explicitly replenished test fixtures; their source policy is not the supplement's finite-lake model. Candidate capacity is neither stored water nor proof a lake exists. Current generator geometry is still a surface sampler; the separate volumetric cave prototype is not a complete production terrain backend.
4. **Architecture:** retain the macro sampler as a deterministic, low-cost geographic baseline, with coarse drainage metadata separate from physical terrain and local water. Refine corridors/basins regionally through shared terrain sampling before carving or coupling. Continue toward streamed local volumetric terrain/caves/edits with independent regional physics and persistent deltas; preserve existing gameplay during that migration. Coarse cells span kilometres and cannot directly supply metre-scale water geometry.
5. **Safest order now:** finish coarse drainage validation -> inspectable stable metadata/reconstruction -> regional river/lake refinement prerequisites -> water policy/connectivity gates -> local integration. Climate and weighted biome variety follow the supplement's pipeline. Natural caves, spatial mineral bodies, shared World Asset Library and content tools remain later Master milestones.
6. **Spikes before production commitments:** verify local transfer openings against terrain/collision at both 1 m and 0.5 m (thin ridges, narrow cuts, ceilings, partial walls and borders); validate regional refinement, finite lake storage and rate-limited upstream flow; prove transactional cross-region water handoff/abstract downstream export and save/load ordering; measure streamed volumetric terrain/collision/LOD budgets. No unbounded whole-world fluids or climate/weather simulation is required.

## This continuation's bounded implementation

- Preserve macro algorithm 3 and drainage algorithm 1 output; add independent minimax spill-level checks, basin/connectivity tests and fixed frozen-world reconstruction fixtures.
- Add read-only cell metadata queries: original bed, routing spill level, catchment area, upstream/downstream links, terminal outlet, candidate basin identity and stable layer-scoped cell identity. These are routing facts, not source rates, lake levels, wetness or final channels.
- Test boundaries, query order, far coordinates, recipe reconstruction and cancelled/corrupt saves. Keep generation and all runtime gameplay paths separate.
- Run the full core suite and game build, then record results and the exact next step below.

## Supplement water policy / integration gates

Oceans may be effectively infinite boundaries. Rivers and springs supply rate-limited inflow; lakes/ponds/cave pools have finite storage unless explicitly fed or authored as exceptions. Runtime weather need not drive hydrology. No new source is created merely because a chunk loads. Valid downstream export to an unloaded abstract network is allowed only through an explicit connection; arbitrary chunk edges are not drains. Player edits must override the natural baseline locally.

The previously identified local-water/terrain connectivity issue remains open. This continuation does not couple macro metadata to local water, so it does not claim to fix that issue or the cave lab's remaining jagged geometry. The targeted regression is mandatory before that coupling; it must not be substituted by a coarse-grid diagonal-routing test.

> **Continuation completed:** drainage diagnostics and the first regional refinement foundation are now validated. See [MASTER_REGIONAL_DRAINAGE_FOUNDATION.md](MASTER_REGIONAL_DRAINAGE_FOUNDATION.md) for current results, limits and the exact next recommended step. The validation checkpoint below records the earlier state.

## Verification and resume point

### Delivered and verified

- `DrainageAnalysis.DescribeCell` / `TryDescribeCell` expose read-only metadata. Upstream lookup inspects at most four neighbours and allocates no world-wide index. Query order and chunk residency cannot alter routing. Unknown coordinates return no data; an absent result must not be interpreted as a drain, dry land or an instruction to spawn water.
- Cell, basin and reach identities are scoped to the frozen analysis fingerprint. They survive exact reconstruction, not an algorithm/input/resolution change. No separate persistent local state is stored in these records.
- `Tests/DrainageValidationTests.cs` checks 100 small analytic/random reference scenarios against independent threshold-reachability spill levels. It additionally counts contributing area along complete routes and checks connected basin membership, storage coverage, routing exits, unequal boundary heads, explicit outlet ordering, flats and one-cell domains.
- `Tests/Fixtures/drainage-v1.json` freezes the five original profiles and 15 expected output fingerprints from the interrupted run. These fixtures do not consult the live content catalogue. All 15 still match: macro algorithm 3 and drainage algorithm 1 output were preserved. Do not regenerate expected values merely to make a changed algorithm pass.
- Metadata checks cover upstream tributaries, basin identity, half-open coordinate ownership at an 8-billion-metre origin, adjacent chunk windows in reversed query order, missing/nonfinite coordinates, immutable upstream lists and reach selection thresholds. Persistence checks cover metadata reconstruction, cancellation and rejected out-of-range queries in addition to the existing atomic/no-overwrite/tamper/version tests.

Fresh checks on 2026-09-29:

| Check | Result |
| --- | --- |
| `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --drainage-spike` | PASS, 300,985 drainage checks; report `artifacts/drainage-spike.json` |
| `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore` | PASS, 3,333,886 assertions, including existing gameplay, saves, macro/chunk, local-water and cave-surface checks |
| `dotnet build Thornwake.csproj --no-restore` | PASS, zero warnings and errors |

No generator, profile, scene, renderer, collision, local-water or normal-game save behavior changed. This continuation did not rerun rendered engine scenarios: the added API is engine-free and not yet called by runtime scenes. Prior rendered evidence remains historical; this checkpoint does not claim a new visual water/terrain fix.

### Next step at the earlier checkpoint (now completed)

Add an opt-in drainage diagnostic layer to the existing macro preview using these immutable cell queries: distinguish catchments/routes and candidate depressions from actual rivers/lakes, show original bed versus routing spill and candidate storage on selection, and retain bounded background work, cancellation and stale-result rejection. Test selection, zoom, saved-world regeneration and layer/world switching without changing algorithm-3 terrain or regular world manifests. This makes the coarse skeleton inspectable before regional refinement is committed.

Then build a bounded regional refinement spike over selected coarse reaches/basins, with shared global boundary anchors and the same authoritative terrain samples on each side. Prove downhill continuity or explicitly report a required cut/blockage; do not force flow through a ridge just because the coarse parent points across it. Establish a finite-lake storage/inflow/outflow contract separately from candidate capacity. Keep sources and valid abstract exports tied to persistent network identity, not render chunks. Complete the targeted local-water/terrain connectivity regression before any production water coupling.

Remaining C2 limits: four-neighbour routing has grid bias; priority-flood candidates are connected filled depressions, not a full nested dynamic lake hierarchy; all sampled below-sea cells are treated as ocean outlets by the current macro recipe; whole-grid resolution is bounded to 512 per side and still kilometres per cell in huge worlds. Coarse area is not discharge, spill elevation is not a lake surface, and storage estimates are not final bathymetry. Cross-platform bitwise equivalence, local channel carving, lake selection, finite runtime storage, regional transactions and production streaming remain unproved or unimplemented. No new river/lake UI or gameplay is claimed at this checkpoint.


