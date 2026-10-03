# Playable-world readiness assessment

2026-10-04. Read-only assessment performed **after** validated receiver milestone `4c69bbcd1ae8aceb03a11c616ebd09e547de3831` was committed and pushed. No implementation experiment, code change, scene change or default promotion was performed. This document records the assessment; [current status](CURRENT_WORLD_GENERATION_STATUS.md) owns the active continuation.

## Evidence and practical gate

Accepted terrain A4 is stable enough to render at player scale: the accepted 22-world terrain/drainage fixtures and 158 regional baselines pass, including macro terrain boundary/reconstruction checks. This preserves previously accepted mountain morphology; it does not certify visual quality at every walking location. A real-scale test is the appropriate next source of evidence.

Source inspection confirms that `MacroRegionSampler.Build` produces disposable bounded region heights/normals and `MacroTerrainChunk.Vertex` subtracts a double-precision render origin before float conversion. `RegionStreaming.Plan` bounds residency and admission independently of total world area. The existing macro-chunk test validates continental teleport and reconstruction using preserved terrain A3; accepted A4 has its separate seam/reconstruction coverage. These are reusable foundations, not an already integrated A4 player scene. `ValleyStreaming` is still a finite legacy adapter. Renderer batching/LOD, collision residency, safe rebasing and actor integration must be validated in the playable milestone rather than inferred from Core tests.

`RegionalDrainageRecipe`, tile/link IDs, `CorridorsForRegion`, `HydrologyInspectionRecipe` and topology recipes/results separate hydrology descriptors from terrain sampling and rendering. Corridor compatibility, unresolved shore cells, blocked outlets, frontier reasons and missing final identities expose uncertainty. Replaceable presentation adapters can consume these versioned descriptors without making their output the terrain or save authority. Later descriptor changes require explicit layer/version migration, not a rebuild of the whole playable-world architecture. No such adapter was implemented by this assessment.

## Conservative water representation

Current hydrology is adequate for **representative visual testing**, not indiscriminate realization of all candidate rivers/lakes. Declared/proved ocean context and locally checked coastal windows can support static sea presentation. Use locally inspected, continuous, terrain-compatible reaches for representative river surfaces. Candidate basin outlines and stages remain inspection information. A provisional static lake surface may be shown in an explicitly labelled, terrain-checked test window; it must not assert final spill, storage, connectivity or flood behavior.

Unresolved basins/routes should default to distinct diagnostic outlines/overlays with their layer ID, candidate stage, resolution and stop reason. Do not silently fill all coarse depressions, draw water uphill across flagged barriers, turn coarse cell-centre links into final channels, or invent sources/drains at stream-region edges. Keep provisional surfaces independently toggleable and separate from persistent fluid state, terrain carving and gameplay consequences. Missing/unfinished descriptors remain unknown; they are not dry terrain by implication.

These restrictions address the known ways a walking-world test could mislead. Unsolved global spill certification does not prevent seeing accepted mountains, coast shape, slope scale, terrain seams, travel distances and representative water in supported/test windows. Static water is acceptable for those questions while dynamic behavior remains unclaimed.

## Known issues carried forward

All six measured V5 cases remain visit-limited. Mountainous broad proves 20/25 old ocean obligations, Vast medium 19/19; neither has a complete supported spill. The Vast reference retains 42 resolution frontiers, including an unresolved final candidate-path cell at 15.625 m spacing and 0.4149421026955338 m residual. Topology 3 remains the retained baseline; topology 5 is opt-in and topology 4 stays rejected.

Regional corridors still have grid bias/bed rises; candidate lake stages and capacities are not final water levels/volumes. Finite storage, rate-calibrated inflow/outflow, local/macro handoff, and cave-water/terrain connectivity remain open. The cave visual/connectivity issue is a gate for production fluid coupling, not a reason to block surface-world traversal with diagnostic water. Continuous-terrain and cross-platform bitwise guarantees remain limited. Generation may take longer initially; it must still be bounded, deterministic and cancelable. Another sampling/minimax experiment would chiefly change internal certification/coverage now, not resolve the missing playable A4 renderer/actor integration.

## Exact next milestone (not started)

**Playable A4 world visual-validation vertical slice.** Start from frozen A4 manifests and the existing bounded sampling/streaming architecture, preserve the valley, then validate real 3D terrain/collision and coordinate rebasing with player-scale traversal. The intended milestone includes representative conservative water, basic terrain materials, existing trees/simple grass, atmospheric presentation, intended camera/movement and useful world inspection tools. Fly/speed controls, coordinate/feature teleport, saved bookmarks and streaming/hydrology overlays should make failures reproducible. Later debug sculpting/cave/water tools must call real terrain/edit authorities, keep temporary and persistent test edits separate, and respect the existing integration gates; finite valley edit keys cannot simply become huge-world save addresses. These are implementation requirements for the subsequent authorized work, not features delivered here.

No additional hydrology prerequisite is justified solely for initial-generation speed or unresolved global certification. Targeted candidate-path/face sampling remains deferred until a concrete visual/integration need justifies it. Preserve replaceable versioned water descriptors and do not promote V5 implicitly. Do not automatically start this milestone from the assessment.

**A) Hydrology foundation is sufficient to begin playable world visual validation; carry remaining hydrology limitations as explicit known issues.**
