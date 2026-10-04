# Playable A4 world visual-validation checkpoint

2026-10-04. Implemented development vertical slice from reviewed base `c49ffb43cd99c567c6e0ec080181d8aeec3fab95`. This checkpoint does not promote production world gameplay or approve visual quality on Tom's behalf. [Current status](CURRENT_WORLD_GENERATION_STATUS.md) owns continuation; [test guide](PLAYABLE_WORLD_TEST_GUIDE.md) covers controls and setup.

## Implemented

`TEST_PLAYABLE_WORLD.cmd` builds and opens `Game/Development/PlayableWorldLab.tscn` using pinned Godot 4.5.1 .NET. A4 is explicit. Existing frozen manifests under `user://playable-a4/` reconstruct their saved profiles; new presets/seeds create isolated immutable manifests. The normal `Game/Main.tscn`, gameplay saves, terrain generation algorithms and historical fixtures are unchanged.

Desktop rendering is **Forward+**, replacing inherited GL Compatibility settings and feature metadata. Runtime reports `forward_plus / vulkan`, on the available RTX 3080. The mobile override is separately `mobile`, untested and not an architectural constraint. Lighting, shadows, sky, haze, restrained grass/soil/rock/elevation colours and static water ripple shading serve scale inspection; no final art pipeline or expensive post-processing stack was added.

The lab reuses Core macro sampling, residency planning, frozen manifests, regional drainage/inspection and imported Pine_A geometry/materials. New Core presentation policy/buffers are separate from the Godot adapter, controller and diagnostic rendering. Terrain jobs contain no Godot objects. Trees are deterministic batched visual scale references, not new harvestable entities; normal gameplay tree harvesting remains unchanged.

### Streaming and coordinates

- Render tiles batch 16 by 16 existing 16 m regions into 256 m tiles. They do not replace persistent region addresses.
- Nearby radius 3 uses 8 m samples, 2,048 terrain triangles per tile and real collision. Detail is retained through radius 5 before downgrading. Trees and sparse grass are near-only.
- The outer ring extends to radius 20, roughly 5 km cardinal view distance. Each outer tile has a sampled center and an exact 8 m boundary with a 128-triangle fan. Mixed-detail neighbours share edge positions/normals. There is no separate middle LOD tier or final production LOD claim.
- Desired residency is 1,681 tiles; retention radius 21 caps it at 1,849. One terrain preparation worker and at most one engine tile commit per frame; setup and explicit inspection each have their own bounded request. No whole-world mesh or accumulated explored-world cache.
- Collision is nearby only. Walking/collision flight checks the swept path, including capsule-width boundary guards, before entering new tiles. Fast noclip may outrun preparation. Teleports invalidate obsolete demand/results; unchanged terrain reconstructs from its manifest.
- Global positions and anchors remain doubles; local physics/render floats are computed only after subtracting the origin. Rebase threshold is 512 m, with origins snapped to 256 m in XYZ. Far roots unload before unsafe float conversion. Above 6.5 km relative altitude, new terrain preparation pauses; flight or X/Z teleport recovers.

The camera supports third-person orbit, pitch, zoom and terrain ray collision. A capsule supports walking, strafe, jumping, slopes, gravity, flight/noclip and separate speed multipliers. Debug UI exposes preset/seed, global XYZ/XZ, 16 m region teleport, descriptor landmarks, mountain approach, previous position, 64 session bookmarks, hideable HUD, detail/streaming/slope/elevation/water overlays and F8 reproduction reports with exact invariant coordinates and manifest. These are development controls, not final gameplay UI.

### Water and sculpt authority

Representative sea is clipped against sampled rendered terrain at declared sea level, with ocean-context checks at vertices/centroids. A 2 cm material-only depth bias avoids shallow-shelf coplanar flicker. Removing a previous 4 cm depth cutoff corrected a visibly over-wide dry shelf; a focused regression now requires nearby coastal coverage. Sampled masking still is not a continuous-terrain guarantee.

F5 requests one regional/inspection-v1 tile. At most 16 proposed reaches receive bounded local checks: up to 64 m, cross-sections at 2 m spacing, no rising bed, shallow trough and banks. Only surviving windows can become gold provisional static surfaces. Both measured basin targets yielded **zero supported presentation windows**; diagnostic corridor lines remain visible without inventing rivers. Candidate basins are magenta outlines only, never filled lakes. White/pink corridor proposals do not imply actual flow. No topology algorithm/default, storage, source or fluid state changed.

Sculpt tools are explicitly disabled with an extension point and a button to the existing cave lab. The finite valley edit address space and authored cave patch are not yet a compatible streamed A4 edit authority. No throwaway sculpt/save system or fake caves were added.

## Validation

Commands: `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore` (full), `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-build -- --playable-world` (focused), and `dotnet build Thornwake.csproj --no-restore`. Rendered harness: pinned Godot with `--path . res://Game/Development/PlayableWorldLab.tscn -- --playable-smoke=artifacts/playable-a4/<run> --preset=mountainous --seed=42` (or `vast-world` / `7319`).

Full Core suite: **4,851,436 assertions passed**, including **37,688 playable-world checks**. Coverage includes A4 reconstruction at normal and far origins, identical reload buffers, shared near/outer boundaries, winding, bounded residency, repeated rebasing, stale-result admission, cancellation, swept collision readiness, invalid coordinates, sampled coastal coverage and provisional/unresolved water rules. Existing macro, accepted A4, drainage, regional hydrology, topology, streaming, local-water and cave regressions remain in the full suite. Game build: **0 warnings, 0 errors**. Eleven historical fixture SHA-256 values are separately checked against the retained ledger; fixtures are not regenerated.

Rendered validation uses the actual scene and controller entry points, with screenshots inspected separately. It exercises spawn, walk/strafe, jump/landing, high-speed flight, repeated rebase, distant feature teleports, mountain slope movement, coast, candidate inspection and return/reconstruction. See the tracked compact report `experiments/playable-a4-validation.json` for exact manifests, global XYZ, local positions, origins, region indices and capture metrics. Frame-count-dependent checks/timings are measurements, not deterministic fixtures.

| World | Runtime checks | Seconds | Builds / unloads | Rebases | Peak resident / detailed | Peak terrain triangles | Max commit ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| mountainous / 42 | 14,808 | 124.0 | 2,813 / 2,733 | 10 | 1,250 / 65 | 254,080 | 53.8 |
| vast-world / 7319 | 15,152 | 126.7 | 2,812 / 2,736 | 9 | 1,250 / 63 | 254,080 | 41.0 |

Both scenarios exited 0 under `forward_plus / vulkan`. Captured near-tile worker times were approximately 85–140 ms and engine commits 32–36 ms; outer work was approximately 3–8 ms and commits 0.2–0.4 ms. These are sampled single-machine results, not guarantees. Vast rejected two obsolete preparations during travel; Mountainous completed without a stale result. Core tests separately exercise cancellation and revision mismatch.

Local rendered evidence: `artifacts/playable-a4/final-mountainous/` and `final-vast-world/` contain eight PNGs each and `report.json`; sibling `.log` files record renderer and pass markers. Screenshot names distinguish spawn, ground, flight, mountain approach, ridge/slope, coast, basin diagnostics and return. Ground, approach, coast and diagnostic captures were visually inspected; the shoreline correction reaches the coast in both presets. Trees appear at the lower-altitude basin/continent areas; the high Vast spawn exceeds the temporary tree elevation filter.

Preserved scenes also ran on Forward+/Vulkan:

- Legacy game: `forward-legacy.log/png`; all existing camera, woodland, terrain/streaming, earthworks, landscape, building/workshop, menus, ecology and save smoke markers passed, ending `SMOKE_TEST_PASS`.
- Macro lab: `final-macro.log/png`; `DRAINAGE_LAB_PASS` and `REGIONAL_LAB_PASS`, including world/layer switches and immutable reload; exit 0. Its existing default smoke seed is 7319 (Mountainous profile, A4), separate from the playable Mountainous/42 case.
- Cave connected water: `final-cave.log/png` and `final-cave-report.json`; outlet reached z >= 14 after 520 ticks, persistent water and sealed chamber checks passed; exit 0. Existing faceted cave presentation remains visible and is not fixed here.
- Legacy streaming lab: `final-streaming.log/png`; captured a populated 25-region view, exit 0. Legacy game/Core tests cover its traversal and reconstruction behavior.

All four retained-scene captures were visually inspected. Final logs contain no `ERROR`/`WARNING` markers. These are rendered engine-driven scenarios and screenshot review, not manual verification of every UI button or Tom's aesthetic acceptance. Raw images/logs remain ignored artifacts; exact playable metrics/manifests/coordinates are preserved in the tracked compact report.


### Rendered findings and performance limits

Distant ridges are now visible from the mountain-approach target about 3.5 km from a range descriptor point; the earlier nearby-only horizon no longer prevents seeing that geography. Coarse outer fans visibly facet slopes and can pop during detail transitions, even with continuous shared edges. Haze reduces far contrast. Nearby surfaces, sparse vegetation and capsule scale are intentionally plain. These remain review findings, not automatic approval or a reason to alter accepted A4 heights.

The worker/commit budget bounds counts rather than guaranteeing a millisecond deadline. Detailed collision commits can cause visible frame hitches; outer tiles are cheaper because they have no collider/vegetation. Initial and post-teleport loading is visible. Triangle metrics count terrain only, excluding trees, water and overlays; residency is not a measured process-memory figure. Very high speed can outrun the bounded queue. No sustained production frame-time or memory certification is claimed.

Two earlier renderer runs exited natively with code -1 and no engine exception, amid other Godot activity; cause was not established. Final isolated validation used a SHA-256-identical copy of the pinned executable named `ThornwakeValidation.exe` in ignored `.tools/`. This is test-process isolation, not another engine/backend or launcher dependency. Standard pinned-engine legacy gameplay smoke also passed. No system/driver settings were changed.

## Deferred and exact next step

Deferred: production terrain LOD, richer materials/vegetation, harvestable world-asset integration, persistent region edits/transactions, natural caves, actor/NPC streaming, final player camera/animation, maps/minimap/fog-of-war, climate/biomes/geology, weather, ice, runtime fluids, finite lake storage, river realization and final oceans. Existing cave water/terrain visual/connectivity issues and unresolved hydrology certification remain open before production coupling. Mobile and cross-platform bitwise/graphics behavior are unvalidated.

**Stop for Tom's visual review.** Launch the guide's two worlds, walk/fly, allow the outer ring to load, inspect mountains/coasts and use F8 plus a screenshot for suspicious places. Tom's player-scale findings determine the next bounded implementation milestone. Do not automatically start another hydrology experiment or later Master/Post-Master system.

Active documentation reviewed: current status, readiness, world-generation guide, milestones, streaming foundation, world architecture, technical architecture, README, validation and cave test guide. Updated active entry points link this checkpoint while preserving historical findings and their dated scope.
