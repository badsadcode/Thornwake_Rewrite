# Current Master Rework world-generation status

Updated 2026-10-04. **Active implementation/continuation index**, not a replacement design document. GitHub `main` is the shared handoff for Tom, Codex and Noor. Start here instead of treating the final paragraph of an older checkpoint as a current work order.

## Authority and versions

- Design: [Master Plan v3](../THORNWAKE_MASTER_REWORK_PLAN_v3.md), overridden where they conflict by [Supplement V1](../THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md). Post-Master remains future scope; `design-inbox/` documents, including V2, are not promoted.
- **Accepted new macro terrain: algorithm 4.** Terrain algorithm 3 and legacy valley generators 1/2 retain their frozen reconstruction. A4 acceptance does not switch existing valley saves or install the macro world in the playable game.
- **Retained production/default baseline for generation-time adaptive topology: algorithm 3.** It remains explicitly selected by recipes; there is no automatic gameplay-water activation or implied universal recipe default. Reconciliation 1, topology 2, coarse/regional/inspection baselines remain preserved.
- **Topology 4: rejected archived reachability experiment**, not accepted by the active recipe validator. This is unrelated to accepted **terrain** algorithm 4.
- **Topology 5: retained opt-in receiver-domain experiment.** Whole-patch ocean certificates can discharge only proved external domain obligations. It is not promoted to the retained topology baseline. No caller, preview or gameplay default changed.

## Implemented and verified

A4 provides immutable world/profile manifests, continents/islands and accepted mountain morphology. Bounded world coordinates, region sampling and residency planning exist. Coarse drainage, regional river/lake proposals, inspection, reconciliation, adaptive topology, deterministic replay, neighbour reuse and diagnostics are implemented. The [receiver checkpoint](MASTER_HYDROLOGY_RECEIVER_DOMAIN.md) records that hydrology milestone and its validation: **4,813,748 Core assertions**, Game build **0 warnings/errors**, **11 historical fixtures unchanged**.

The dedicated [playable A4 lab](PLAYABLE_A4_WORLD_VALIDATION.md) now integrates bounded streamed terrain, nearby collision, a validation player, double/global to float/local rebasing, distant geography, conservative static sea and hydrology diagnostics. Desktop uses **Forward+**, verified at runtime. The normal playable valley and isolated streaming/cave/water/map labs remain available. This is a development vertical slice, not production terrain LOD, local volume promotion, persistent region transactions or gameplay migration. Current full Core validation: **4,851,436 assertions**; see the playable checkpoint for rendered evidence and build results.

## Known limitations

- All six representative topology-5 queries remain visit-limited. Mountainous broad discharges 20/25 old ocean-patch obligations; Vast medium 19/19. These are evidence-covered obligations, not fully solved lakes. The reference component is supported, but its spill is not.
- Vast reference retains 42 resolution frontiers and an unresolved 15.625 m candidate-path cell. The nominal spill is 309.9830677247165 m; a changed eligible terminal receiver does not confer support. Final candidate/frontier fields remain authoritative when the bounded detailed observer truncates.
- Coarse drainage is a kilometre-scale routing skeleton; regional corridors retain grid bias and possible bed rises. Candidate basin stages/capacities are not final lake surfaces, current water volume or guaranteed connectivity. Finite lake storage, calibrated inflows and production macro/local transfers are not implemented.
- The cave lab's terrain/water connectivity/visual issue remains open before production fluid coupling. Sampled support is not continuous-terrain proof; cross-platform bitwise identity is not certified.
- No final channels, water rendering, runtime-fluid integration, climate, biomes, geology, World Site Editor or Post-Master systems were added by this milestone.

## Immediate continuation

**Next action: Tom launches `TEST_PLAYABLE_WORLD.cmd`, walks and flies through Mountainous/42 and Vast World/7319, and reports suspicious locations with F8 plus screenshots.** Read the [test guide](PLAYABLE_WORLD_TEST_GUIDE.md) and [completed vertical-slice checkpoint](PLAYABLE_A4_WORLD_VALIDATION.md). Do not automatically begin another generation or hydrology experiment.

The prior readiness recommendation A has now been implemented as a separate visual-validation scene. Candidate lakes remain diagnostic outlines; inspected river windows require local bank checks; there is no runtime fluid coupling or terrain carving. Accepted terrain A4 and all historical fingerprints remain unchanged. The earlier GL Compatibility desktop setting was inherited from the prototype and is superseded by Forward+; the mobile override remains separate and unvalidated.

After Tom's feedback, choose a bounded follow-up addressing concrete player-scale geography, presentation or integration findings. Production LOD/edit authority and unresolved hydrology remain explicit future work; climate, biomes, geology, natural caves, storage, runtime water and other future systems are not authorized by this checkpoint. Historical checkpoint tails must not override this stop condition.

## Documentation review and historical record

Reviewed for current-state/continuation conflicts: README; `GIT_WORKFLOW`; `MILESTONES`; `TECHNICAL_ARCHITECTURE`; `VALIDATION`; `WORLD_GENERATION_VISION`; `WORLD_GENERATION_TEST_GUIDE`; `MASTER_REWORK_AUDIT`; `MASTER_WORLD_ARCHITECTURE`; `MASTER_STREAMING_FOUNDATION`; `MASTER_MACRO_WORLD_FOUNDATION`; `MASTER_DRAINAGE_CHECKPOINT`; `MASTER_REGIONAL_DRAINAGE_FOUNDATION`; `MASTER_A4_HYDROLOGY_ACCEPTANCE`; hydrology checkpoints `RECONCILIATION`, `ADAPTIVE_TOPOLOGY`, `BOUNDED_WORK`, `GRAPH_REUSE`, `REACHABILITY_EXPERIMENT`, `LOCALITY_ASSESSMENT`, `SPILL_ATTRIBUTION`, `RECEIVER_DOMAIN`; the Master Plan, Supplement V1 and Post-Master scope/continuation sections; `design-inbox/README.md` authority policy.

The audit, B1/C1, drainage/regional, mountain acceptance and hydrology checkpoint chain remain dated evidence. In particular the topology-4 rejection and earlier performance/fixture measurements are unchanged. Legacy release architecture/roadmaps describe the valley, not current Master-Rework priorities. The earlier world-generation vision remains a proposal. Master design intent and the design inbox are unchanged.

Updated entry points:

- README and `MILESTONES`: distinguish the current Master-Rework priority from the old playable-release roadmap.
- `MASTER_WORLD_ARCHITECTURE` and `MASTER_A4_HYDROLOGY_ACCEPTANCE`: link active status without replacing their historical decisions/evidence.
- `VALIDATION`: append the completed V5 checks; preserve previous release/visual evidence.
- `WORLD_GENERATION_TEST_GUIDE`: add V5 commands and link active continuation.
- `MASTER_HYDROLOGY_SPILL_ATTRIBUTION`: link its implemented B-first follow-up.
- `PLAYABLE_WORLD_READINESS`: new, read-only assessment after the implementation push; records recommendation A and conservative visual-water rules.
- `MASTER_HYDROLOGY_RECEIVER_DOMAIN`: complete contract, results, bounds, uncertainty, retention and continuation; tracked `experiments/hydrology-receiver-domain.json` preserves the comparison beyond ignored artifacts.

The checkpoint chain leads through [A4 acceptance](MASTER_A4_HYDROLOGY_ACCEPTANCE.md) → reconciliation → adaptive topology 2 → bounded-work topology 3 → neighbour reuse → rejected reachability 4 → locality → spill attribution → [receiver-domain 5](MASTER_HYDROLOGY_RECEIVER_DOMAIN.md). This index owns the **current next step**; historical tails must not override it.
