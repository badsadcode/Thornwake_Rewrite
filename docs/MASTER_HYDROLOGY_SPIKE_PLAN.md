# Hydrology spike: breached lake, downhill channel and connected caves

> 2026-09-29: `THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md` supersedes conflicting policy below. In particular, ordinary lakes/ponds use finite storage, rivers/springs provide rate-limited inflow, and valid abstract downstream exports are distinct from arbitrary chunk-edge drains. See [the current drainage checkpoint](MASTER_DRAINAGE_CHECKPOINT.md) for inspected source state, validation and the exact next step. The older investigation/results below remain historical.

2026-09-28. **Update: the bounded connected-flow spike is now implemented and tested in the lab; see MASTER_CONNECTED_WATER_RESULTS.md.** The investigation below records the pre-spike limitations and production acceptance goals. Prompted by the second water playtest: limited patches after excavation, and whether breaching a hilltop lake creates a descending stream.

## What the current code actually does

- `CaveSpikeField.LocalReservoir` has a fixed `Level`, `Radius` and `Bottom`. The two lab reservoirs are recreated from the fixture, not evolved through a water simulation.
- `ReservoirMesher` intersects that horizontal level with the density field inside the radius. It supplies animated surface geometry and bed-depth shading only.
- `WaterAt` accepts clear vertical columns under each local level. It does not discover lateral connectivity through tunnels or around rock, propagate an advancing front, calculate outflow or evolve reservoir storage.
- The test save contains the seed and terrain cuts, with no dynamic water state. Reloading reconstructs the same static reservoir definitions.

Consequently the current lab cannot turn a lake outlet into a downhill stream. Enlarging reservoir radii would extend the same horizontal surface and would not solve that problem. Some screenshot gaps may also involve coarse terrain/surface intersection and thin remaining rock; the exact cause of every visible gap cannot be established from an image alone. Connectivity and visual watertightness must be tested separately.

## Intended behavior and source policy

The user asks for meaningful downhill response to an excavated outlet. This does not by itself select finite-volume lakes or override the plan's permitted persistent-source approximation.

The Master target architecture, sections 14–18 and 23, allows effectively persistent/infinite natural sources, locally varying river surface elevations, terrain-connected gradual flooding and outflow. The Post-Master sections 38–39 reuse these rules; the present work is the Master water foundation, not a tool/crafting expansion.

For an outlet below a hilltop lake's surface:

1. An intact bank prevents transfer, even when nearby terrain is lower.
2. A real breach starts bounded, time-dependent outflow.
3. A descending open channel carries water downhill, with its own changing surface profile, pooling at downstream obstacles. Do not extend the upstream horizontal water plane over the hillside.
4. Connected enclosed cavities can fill progressively; sealed cavities remain dry. An underground route may pass beneath a solid roof, so a clear vertical column is insufficient.
5. Source policy is explicit. The initial natural-lake policy may maintain its upstream level while feeding the stream. A future finite reservoir instead loses stored water and lowers its level; that is a separate policy and must not be claimed as already implemented or selected.

## Proposed implementation boundary

Retain the existing solid/void authority and worker/revision infrastructure. Investigate a sparse, regional connectivity representation for 3D voids, including several stacked spaces at the same X/Z. Represent water sources, receiving storage and connections separately from their rendered surfaces.

A local update should recompute connectivity around the edited terrain, identify reachable basins/outlets, then advance simplified transfer on a fixed simulation tick. Use opening size, source strength, height difference and receiving/outlet capacity as bounded gameplay parameters. Prevent negative storage, instantaneous remote filling and transfer through solid walls. Surface-channel routing must allow descending elevation; enclosed-basin filling and flowing channels need distinct presentation rules.

For huge streamed worlds, investigate region-owned basin/connection records, stable IDs and cached connections across unloaded regions. Bound work per update and retain pending work across ticks. Unloading visuals must not erase water state, create a fresh source or turn an unresolved boundary into an outlet. Keep simulation authoritative for multiplayer; persist breached connections and necessary water-state deltas with schema/version checks. These are proposed constraints to validate, not established performance results.

No global per-frame fluid solve or existing gameplay rewrite is required for this spike. Do not replace the current lab water until the alternative is measurable and independently testable.

## Required acceptance scenarios

- Dry excavation beside a lake stays dry until a breach is opened.
- Hilltop lake feeds a player-cut downhill channel without a suspended sheet at lake elevation.
- A connected passage turns beneath a roof and still carries water; a sealed chamber directly below remains dry.
- Wider breach fills a receiving chamber faster than a narrow breach; outflow changes the final behavior.
- Closing a breach stops new inflow; remaining downstream water follows an explicit retention/drainage rule.
- Water meshes agree with the visible terrain at both 1 m and 0.5 m sampling, including narrow shore cuts. Test geometric gaps independently of wet/dry classification.
- Cross-region propagation, unload/reload and save/load preserve source identity and accumulated state.
- Repeated digs and cancelled background jobs cannot install stale terrain connectivity or water surfaces.
- Record simulation work, frame/commit cost, propagation delay and memory before integration.

The previous static geometry/worker tests did not certify dynamic hydrology. The new connected-water report distinguishes completed bounded-spike checks from the remaining production gates; region handoff and world-scale integration are still open.


