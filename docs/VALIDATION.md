# Validation — release 0.8.0

## 0.8.0 landscape generation — 27 September 2026

- Pre-change backup: backups/Thornwake-source-before-landscape-20260927-232558.zip, 704 files hash-verified.
- Core suite passed **1,636,611 assertions**, including 26 generator-2 seeds (negative and integer extremes), deterministic sample stages, finite height ranges, distinct landscapes, prepared village/road/camp/river sites, stable resource IDs, forest-density subsets and draft-copy isolation.
- New-world save/reload preserved generation settings, resources and exact edited terrain. An unfinished shovel stroke resumed identically. Foundation placement succeeded on a new landscape. Schema-9 migration preserved generator 1, legacy edits and shovel samples exactly, with permanent `.v9` backup. Missing/nonfinite/unsupported generation metadata was rejected.
- Game compilation passed without warnings. The engine exercised the actual Customize → preset → Create route, cancellation without world mutation, saved settings, generated resource placement, sampled rendered vertex heights, road traversal, harvesting and reconstruction after saving.
- All earlier woodland, terrain, earthworks, construction, stairs, crafting, hotbar, roof, camera and combat scenarios passed. Editor runtime error logs were empty.
- Customization and gameplay captures were inspected for layout, clear terrain preview, preserved village placement and grounded resources. Evidence: artifacts/world-setup-first.png, artifacts/landscape-first.log, artifacts/landscape-gameplay.png, artifacts/landscape-gameplay.log.
- Final standalone evidence: artifacts/portable-v080-validation.json records isolated runtime conditions, exit status, file verification and archive hash; artifacts/release-v080-landscape.log contains engine results. The package checksum is in dist/SHA256SUMS.txt.
- The authored village, road/river alignment and 110 m playable extent remain. These are analytic generation passes, not an erosion simulation, streaming world, procedural town system or caves. Digging strength is unchanged and remains a documented future sandbox setting.


## 0.7.0 shovel earthworks — 27 September 2026

- Pre-change backup: backups/Thornwake-source-before-shovel-20260927-182145.zip; 694 source files hash-verified.
- Core suite passed **1,578,133 assertions**. Earthwork coverage includes exact soil conservation, inverse fill, mixed leveling/smoothing, timed work, cancellation, missing resources at completion, crafting exclusion, saved task continuation, schema-8 migration, legacy height preservation, invalid grids/tasks, bedrock, site protection and cultivation without height changes.
- Game compilation passed with zero warnings and errors.
- Engine checks passed: equipped shovel and icon integration, always-look cursor release, mode/brush/height shortcuts, ground marker and toolbar, pose driven by saved elapsed work, four-region corner rebuilding, unaffected node identities, every rendered terrain vertex using authoritative height, save reconstruction and pause presentation.
- All existing woodland, ecology, crafting, construction, stairs, roof, hotbar, camera and combat smoke checks passed in the same run. The only failure during development was a test-scene camera zoom leaking into a later camera test; the isolated scene now restores the orbit before returning.
- Dig and Level gameplay captures were visually inspected. The surface-following brush ring, dashed clearance boundary, level reference, selected hotbar tool and work progress are visible without overlapping HUD panels.
- Extracted Windows package passed with Windows-only PATH, invalid DOTNET_ROOT and DOTNET_ROOT_X64, and runtime multilevel lookup disabled. Exit 0, empty error log, and 204 packaged files hash-matched the release directory.
- Evidence: artifacts/earthwork-first.log, artifacts/earthwork-first.png, artifacts/release-v070-earthwork.log, artifacts/release-v070-earthwork-errors.log, artifacts/release-v070-earthwork-level.png, artifacts/portable-v070-validation.json. The final package checksum is in dist/SHA256SUMS.txt.
- Generator 1 is preserved. This release delivers the shovel interaction slice, not new generation passes, grading/trenching, blueprints, worker navigation, caves or third-person camera changes.

## 0.6.4 terrain foundation — 27 September 2026

- Backup before this step: backups/Thornwake-source-before-terrain-foundation-20260927-180315.zip; 686 source entries verified.
- Core suite passed **1,578,064 assertions**. New tests compare shared terrain evaluation exactly with the old formula across seeds/edited ground, verify independent 0.5/1 m region samples, read-only queries, classification bounds, negative region indices, border halos and exact save/reload heights.
- Game build passed without compiler warnings.
- In-engine tests verified 64 terrain regions, a border edit replacing only four regions, untouched region instance IDs, affected resources grounded, actual adjacent mesh heights/normals matching and save reconstruction.
- All earlier woodland, ecology, crafting, building, camera/combat, hotbar and roof integration scenarios passed in the same run. Terrain runtime error log was empty.
- Evidence: artifacts/terrain-smoke.log, artifacts/terrain-smoke-errors.log, artifacts/terrain-foundation.png.
- Extracted 0.6.4 release passed with only the bundled runtime (Windows-only PATH, invalid DOTNET_ROOT settings, multilevel runtime lookup disabled): exit 0 and empty error log. The delta overlay was visually inspected. Release evidence: artifacts/portable-v064-validation.json, artifacts/release-v064-terrain.log, artifacts/release-v064-terrain.png.
- This is an incremental terrain foundation. No new generation algorithm, compact sample-delta save format, shovel workflow, blueprint construction or camera collision is claimed.

## 0.6.3 woodland integration — 27 September 2026

- Source backup: backups/Thornwake-source-before-forest-integration-20260927-104841.zip, 296 entries verified against source hashes.
- Core suite: **1,565,199 assertions**, including deterministic forest appearance, all 17 variants, pine-only cone rewards, all-species timber rewards, duplicate-harvest rejection and LOD hysteresis.
- Game compilation passed without warnings. All 51 authored GLBs imported, with secondary automatic mesh decimation disabled.
- Runtime smoke scenario passed woodland import/material checks, distance LOD switching, textured stump construction, trunk collision, and appearance stability after save/reload. Existing ecology checks passed for planted pine growth, chopping, planted/natural stump removal, bush regrowth and save reconstruction.
- All existing engine scenarios passed: camera, combat, construction, stairs, crafting queue, player menus, ecology, building variants and roof junctions.
- The gameplay render was inspected with the actual forest, elevated camera, player and HUD. Full-crown clearing was replaced with a small binary/dithered foliage window around the character. Pixel maps remain nearest-filtered and branches remain intact.
- Editor-runtime evidence: artifacts/forest-smoke.log, artifacts/forest-smoke-errors.log (empty), artifacts/forest-gameplay.png.
- Extracted 0.6.3 release passed with Windows-only PATH, invalid DOTNET_ROOT / DOTNET_ROOT_X64 and DOTNET_MULTILEVEL_LOOKUP=0: exit 0 and empty error log. Evidence: artifacts/portable-v063-validation.json and artifacts/release-v063-forest.log.
- Save schema remains 8; resource IDs, positions and terrain generation remain unchanged. No player's normal save was used for these checks.


## 0.6.2 — crafting actions (2026-09-27)

- Core suite: **1,541,025 assertions passed**. New checks cover recipe activity defaults/validation, read-only presentation queries, saved elapsed phase, lost reach and next-batch clock reset.
- Debug and release builds completed without compiler warnings/errors. Export completed successfully.
- Native `work-smith` scenario: seven activity profiles, station workpiece lifecycle, tool stow/restore, menu/focus pauses, unreachable-station recovery, cancellation, world progress and actual mouse input on Watch work all passed. Other existing gameplay integration suites ran in the same process.
- Extracted `Thornwake-0.6.2-Windows-x64.zip` tested with Windows-only PATH, invalid DOTNET_ROOT / DOTNET_ROOT_X64 and DOTNET_MULTILEVEL_LOOKUP=0. Both `work-smith` and `queue` runs exited 0 with empty error logs. The latter confirms native single-click selection, double-click batch queuing and safe release input.
- Visually inspected the world activity card, sawing and smithing scenes, and the crafting queue/menu at 1280x800. These remain prototype actions without precision hand/socket alignment or production rigs.
- Evidence: `artifacts/work-saw.png`, `artifacts/work-smith.png`, `artifacts/release-v062-work.log`, `artifacts/release-v062-work-errors.log`, `artifacts/release-v062-work.png`, `artifacts/release-v062-queue.log`, `artifacts/release-v062-queue-errors.log`, `artifacts/release-v062-queue.png`. `artifacts/v062-package-verification.txt` records the final package comparison.
- Source backup before work: `backups/Thornwake-source-20260926-195022.zip` (138 files verified). Original Blender templates were left unchanged after confirming the user's version mismatch; use Blender 5.1 for the supplied native files.

## 0.6.1 — finishes and authored building art

- Source backup: `backups/Thornwake-source-20260926-130046.zip`, 116 source files verified by SHA-256 against the adjacent manifest before modifications.
- Full core suite passed **1,540,992 assertions**. The large total is mostly existing deterministic generation checks. New cases cover every finish's unchanged cost, refinish atomicity and reach/equipment checks, legacy walls, unchanged collision/snapping/refunds, selection copying, invalid mappings, save roundtrip and permanent `.v7` migration backup.
- Debug compile and Release export succeeded with zero compiler warnings/errors.
- Rendered tests exercised four roof finishes on slope/ridge/hip/valley at all four pitches; actual GLB loading, visual replacement and save/reload; native finish/brush buttons, aimed repaint preview, unchanged piece/inventory counts, and Z returning to building. Screenshot `artifacts/release-v061-finishes.png` was visually reviewed at 1280×800. This is a comparison fixture, not a new world spawned for players.
- The extracted standalone package was launched from its own directory with Windows-only PATH, a nonexistent DOTNET_ROOT and DOTNET_MULTILEVEL_LOOKUP=0. Both `finishes` and `variants` runs exited 0 with empty error logs. They also ran existing ecology, sandbox simulation, camera, combat, hotbar/menu, queue, construction, height and roof-junction runtime scenarios. Native pitch/material/shape controls remain functional after the new appearance controls.
- Evidence: `artifacts/release-v061-finishes.log`, `release-v061-finishes-errors.log`, `release-v061-finishes.png`, `release-v061-variants.log`, `release-v061-variants-errors.log`, `release-v061-variants.png`. The final ZIP's game/runtime/license files are compared by hash with the tested extraction; the result is in `artifacts/v061-package-verification.txt`.
- This verifies the included GLB example and current procedural art. Future Blender models still need visual checks for their own dimensions, textures, edge groups and door pivots. Clean-machine and lower-spec-PC testing remain appropriate before a public release.

## Version 0.6.0 verification status

- Full core suite: **1,540,904 assertions**, with zero compilation warnings or errors. Most assertions are deterministic seed/resource comparisons, not separate test scenarios.
- New roof tests cover four pitches, rotated hip/valley edge connections, committed L junctions, mismatch atomicity, interior crease intersections with triangular upper floors, gable support/removal, outline collision and saved selections. Matching gable fills include the section from a raised roof down to the supporting wall top.
- `ECOLOGY_TEST_PASS` and `SANDBOX_POINTER_TEST_PASS` passed in the rendered engine. Growth, regrowth, planting/removal and sandbox draft/apply behavior are exercised through simulation/session input paths.
- `artifacts/v060-sandbox.png` was visually reviewed; the settings panel is readable and keeps Apply/Cancel accessible.
- Source backup: `backups/Thornwake-source-20260925-170705.zip`, all 100 files verified against its SHA-256 manifest.
- ROOF_JUNCTION_TEST_PASS passed in the rendered engine: folded mesh vertices/normals, shallow and steep joins, gable cutaways, raised wall collision, picking, previews and save reconstruction. Inspected artifacts/v060-junctions.png, v060-valley.png, v060-gables.png and v060-ecology.png; associated error logs are empty.
- Standalone export and extracted-package verification passed. The extracted game ran from its own directory with a Windows-only PATH, nonexistent DOTNET_ROOT and disabled multilevel runtime lookup. Both sandbox and hotbar runs exited 0 with empty error logs, including all roof/ecology/session checks and their native pointer tests. Evidence: artifacts/release-v060-sandbox* and artifacts/release-v060-hotbar*. The final archive is hash-checked against the tested game/runtime/license files.

Schema 7 validation covers saved ecology/settings/calendar state and migration from schema 6 while retaining inventory, custom hotbar bindings and permanent `.v6` source-save backup. Craft-duration changes preserve an active job and affect the next batch; changing day length preserves current calendar position. Long active-hour growth and very slow custom growth use persistent double-precision progress.

## Version 0.5.0 verification

- Debug compilation: zero warnings and errors. Full core suite: **1,539,495 assertions**, including 70 new hotbar/quest checks. Most assertions are seed/resource comparisons, not separate test scenarios.
- `PLAYER_MENUS_TEST_PASS`: reordered number keys, actual hammer building gate, exact food consumption, potion reuse cooldown, shared Dodge stamina/cooldown, screen shortcuts, modal input isolation, saved layout and equipment reconstruction.
- `HOTBAR_POINTER_TEST_PASS`: real viewport mouse movement starts native item and skill drags, dropping assigns shortcuts, hotbar dragging reorders them, inventory remains intact, key 0 activates the relocated sword, and a navigation button opens Character.
- Character, Skills, Journal and Inventory screens were rendered at 1280×800 and visually inspected. Evidence: `artifacts/v050-character.png`, `v050-skills.png`, `v050-journal.png`, `v050-hotbar.png` and associated logs.
- Source backup: `backups/Thornwake-source-20260925-162755.zip`, all 86 entries verified against its SHA-256 manifest.
- Standalone export and extracted-package verification passed. The extracted game launched from its own directory with a Windows-only PATH, nonexistent DOTNET_ROOT and disabled multilevel lookup. It exited 0 with an empty error log and passed all simulation/session checks plus native item/skill drag-and-drop. Evidence: `artifacts/release-v050.log`, `release-v050-errors.log`, `release-v050.png`. The final ZIP's game/runtime files are hash-checked against this tested extraction; packaging includes only the executable, runtime, documentation and licenses.

## Build and package

- C# Debug build: successful, zero warnings, zero errors.
- Version 0.4.0 release export succeeded. The ZIP was extracted and launched from its own directory with a Windows-only PATH, nonexistent DOTNET_ROOT and disabled multilevel runtime lookup. It exited 0 with an empty error log. All runtime suites passed, including `BUILD_VARIANTS_TEST_PASS` and `BUILD_OPTIONS_INPUT_TEST_PASS`. Evidence: `artifacts/release-v040.log`, `artifacts/release-v040-errors.log`, `artifacts/release-v040.png`.
- The established player package layout contains `Thornwake.exe` with embedded game resources and `data_Thornwake_windows_x86_64`, including `hostfxr.dll`, `coreclr.dll`, game assemblies and .NET 8.0.31 runtime dependencies.
- Historical portable 0.3.0 verification launched the extracted game from its own directory with installed runtime paths disabled. Evidence remains in `artifacts/release-v030*`.
- Portable checks on the development machine do not substitute for testing on a clean Windows VM and several lower-spec PCs before public release.
- Source backup before 0.4.0: `backups/Thornwake-source-20260925-160325.zip`, 72 files verified against the accompanying manifest.

## Core validation

The historical 0.5.0 console suite passed **1,539,495 assertions**, primarily resource-spacing comparisons across 100 world seeds. Current 0.6.0 status is recorded above. These are assertion counts, not counts of independently designed scenarios. The test runner reports caught failures and returns an error exit code instead of opening a Windows crash dialog.

The 72 new building-variant checks cover generic selection resolution, physical definition validation independent of missing visual overrides, exact material costs, selection copying, both 30° and 45° roof chains, raised ridge height, opposite slope orientation, trim overlays and mirroring, incompatible pitches, duplicate and unaffordable placement atomicity, support removal, roof reference cycles, invalid saved variants, full save roundtrip, and real schema-4 migration with permanent `.v4` backup retention. Profile-based collision checks reject roof surfaces crossing upper floors and walls in both placement orders while allowing underside contact.

## Version 0.4.0 engine validation

`BUILD_VARIANTS_TEST_PASS` constructs a supported three-by-three roof frame through simulation commands, then exercises the live brush, material cost and ghost updates. A 45° slope row feeds into a ridge raised three metres and descends through an opposite slope row. Eave and gable overlays survive scene reconstruction and saving. The same shape controls work on floor and wall tools. Returning to Roof restores its settings. A ray query picks an eave separately from its parent roof, allowing ordered dismantling.

`BUILD_OPTIONS_INPUT_TEST_PASS` sends actual viewport mouse events to the pitch and material buttons, then keyboard T and Shift+T after clicking. The preview remains active, options update, and no extra structure is placed. These are scripted UI inputs, not a substitute for human playtesting.

Inspected `artifacts/v040-variants.png`: roof joins, slate finish, retained legacy village scenery, readable live options, pitch mismatch feedback and material-tinted ghost. The associated engine run exited 0 with an empty error log. Invalid visual asset references are diagnosed at startup; all shipped references resolved. This historical scenario covers Phase 1; 0.6.0 Phase 2 verification is recorded separately above.

The 19 additional camera assertions compare smoothing at 30 and 144 FPS, extreme pitch/zoom input, reset and inversion, both chord orders, single-click dispatch, release suppression, independent combat with middle-mouse orbit, immediate always-look attacks, and cancellation of pending input.

Coverage includes deterministic IDs and placement; reserved village, camp, farm, river and road space; finite heights; recipe reference validation; resource collection; insufficient-material atomicity; structure bounds and overlap; soil and crop growth; market stock, prices, balances and exhaustion; frame-independent economy ticks; complete save roundtrip; backup retention; rejection of malformed, future-version, invalid-coordinate, negative-inventory and missing-content state.

The 34 construction checks cover joined floor elevations, edge snapping, duplicate placement, roof support, closed/open door collision and jambs, closing over the player, safe dismantling order, exact refunds and duplicate reclaim rejection, furniture support, protected terrain, placement reach, and real schema-1 JSON migration. The original `.v1` backup is checked after a second save.

The 125 advanced construction assertions cover L-shaped roof coverage, shared frame support, redundant versus essential walls, two upper levels, stairwell openings, blocked entry rejection, narrow ramp ascent, no ground-to-upstairs teleporting, height-aware collisions, second-flight ascent and upstairs furniture.

The 82 crafting assertions cover reserved ingredients, delayed output, single completion, cancellation refunds, matching stations and reach, paused work while away, saved job resumption, occupied station protection, all six decoration craft/place/pickup cycles, furniture over rugs, actual schema-2 migration and exact `.v2` backup retention, and malformed saved jobs.

The 91 queue assertions cover numbered and mixed requests, active-only ingredient reservations, frame-time carryover and split-step equivalence, until-materials exhaustion, FIFO waiting and resumption after acquiring supplies, cancellation/refund integrity, removing pending requests, clearing, station reach, quantity and queue limits, save/reload, schema-3 active-job migration, permanent `.v3` backup retention and invalid queue references.

The 151 snapping assertions cover Object, Grid and Free placement, 45° rotation, rectangular and right-triangle footprint connections, diagonal walls, support and overlap decisions, and consistency between placement previews and committed builds. Triangle pieces use 45°/90° right-triangle halves; the checks do not establish exact parity with another game's building system.

## Version 0.3.0 engine validation

Rendered source-build runs log `SNAP_QUEUE_TEST_PASS`. The scenario exercises the session's snapping controls and recipe double-click handler, quantity selection, mixed queued recipes, saving and loading unfinished work, and the until-materials checkbox. It supplements core tests with the actual game UI and input path.

The viewport pointer-event check logs `RECIPE_POINTER_TEST_PASS`: the first click selects a recipe without queuing it, the second double-click press queues the selected two batches exactly once, and releasing the mouse does not queue another request. The development run exited with code 0 and an empty error log. The updated `artifacts/v030-queue.png` was visually reviewed after adjusting the queue panel spacing.

Screenshots and logs are recorded under `artifacts/v030-queue*` and `artifacts/v030-snapping*`. These capture the updated queue controls and placement presentation. The exported package also passed, with evidence under `artifacts/release-v030*`. After the release notes were updated, all 192 game and runtime files in the final ZIP were hash-checked against the successfully tested extraction. Dedicated sawing, smithing and cooking animations remain future work; this build uses the basic working pose.

## In-engine smoke scenario

Historical exported releases exercised the same session handlers used by the game for:

1. Opening and closing inventory through key events.
2. Character movement and tool switching.
3. Multi-hit gathering and removal of a rendered tree.
4. Crafting, safe building clearance, placement and rendering.
5. Terrain editing, crop planting, time advance and harvest.
6. Blocking through a buffered right-mouse input event, dodge invulnerability and combat victory.
7. Saving, loading, rebuilding the complete scene and retaining resource removals, structures, terrain edits and enemy defeat.
8. Capturing a rendered frame, then exiting normally.

The release smoke test is also required to log `CONSTRUCTION_TEST_PASS`. The construction scenario builds a two-foundation shelter through normal simulation commands, checks engine movement against its walls and door, opens the door, checks deck height and roof cutaways, rejects unsafe dismantling, then saves and reconstructs the scene. Tests use their own save file and do not replace a player's normal world.

The 0.2.0 scenario also logs `WORKSHOP_FLOOR_TEST_PASS`: B is rejected without the hammer, build and inventory grids open, station work reserves ingredients and survives a save mid-task, and a furnished upper floor with an L-shaped roof is constructed. The actual character movement routine walks up the stairs onto the landing and restores that height after save/reload. Captures include the interior, inventory, carpenter's progress screen, building catalogue and release exterior. Final portable-package evidence: `artifacts/release-v020.log`, `artifacts/release-v020-errors.log`, `artifacts/release-v020.png`.

The 0.1.1 engine checks also exercise cursor capture and release with both buttons; orbit motion; pause/resume; Tab cursor access; placement transitions; focus loss/restoration; and a settings save/reload. Blocking, dodging and combat victory run while middle-mouse orbit is active. Test preferences are isolated in `smoke-settings.cfg`. The camera options panel was visually inspected at 1280×800.

## Visual inspection

The 0.2.1 engine scenario logs `BUILD_HEIGHT_TEST_PASS`: Alt-wheel across all building families, above/below character placement, anchored horizontal aim, adaptive floor kinds, preview-to-click height and material costs, height limits, H reset, menu isolation, captured orbit, and ordinary zoom. Inspected the live height hint and catalogue without floor buttons. Portable release evidence: `artifacts/release-v021.log`, `artifacts/release-v021-errors.log`, `artifacts/release-v021.png`.

Inspected rendered gameplay, title menu and crafting panel at 1280×800. Corrected overbright lighting, ambiguous context text, notification placement, tool visuals, and uniform ground detail. Tree removal leaves persistent stumps. Final gameplay rendering includes the UI, village, character, grass, map, bridge and world lighting.

For 0.1.2, inspected a constructed shelter outside and inside with roof cutaways, open doorway, connected deck and cleared ground cover, plus the scrollable construction menu. Construction test captures and logs are under `artifacts/construction-*`. A shutdown crash during repeated scene reconstruction led to explicit scene/resource cleanup and draining managed finalizers before stopping the engine; repeat rendered runs exited successfully.

For 0.2.0, inspected stairs, the upper landing, planted pot and easel painting, and the new grid menus at 1280×800. Adjusted notification placement to stay below open menus, added distinct item glyphs, and fixed hand recipes being incorrectly labelled as requiring a station at a workshop. Evidence is under `artifacts/v020-*`.

## Remaining verification

Human playtesting, long sessions, low-end GPU performance, unusual window sizes, accessibility, save migration between future versions, and multiplayer are not certified by this release. Automated smoke checks use scripted actions; they do not establish that the whole game is polished or production ready.

## Master Rework B1 (2026-09-28)

Baseline source preserved in `backups/Thornwake-before-master-rework-20260928-170047.zip` with verified hashes. Audit/decision/acceptance details are in `MASTER_REWORK_AUDIT.md`, `MASTER_WORLD_ARCHITECTURE.md` and `MASTER_STREAMING_FOUNDATION.md`.

Core suite: 1,639,105 assertions passed. Godot game build: zero warnings/errors. Rendered gameplay smoke includes `TERRAIN_STREAMING_TEST_PASS` and all previous markers; `artifacts/master-streaming-game-errors.log` is empty. Isolated lab exits 0 with an empty `artifacts/master-streaming-lab-errors.log`; both captures were inspected. No release package rebuild, save schema change, terrain backend replacement or Post-Master system expansion is included.

## Master Rework cave spike (2026-09-28)

Playable isolated test: `TEST_CAVE_LAB.cmd`; guide: `CAVE_LAB_TEST_GUIDE.md`; architecture evidence: `MASTER_CAVE_SPIKE_RESULTS.md`.

Core: 1,656,997 assertions passed. Build: zero warnings/errors. Cave physics smoke and normal-game regression both exited 0 with empty error logs (`artifacts/cave-smoke-errors.log`, `artifacts/cave-baseline-regression-errors.log`). The cave test covers actual walking into/out of underground space, ceiling contact, edited collision, unload/reload, hybrid seam traversal and alternate sampling/backends. No main-world save or terrain-backend migration is included.

## Cave playtest feedback fixes (2026-09-28)

Core suite: **1,895,062 assertions passed**. Build: zero warnings/errors. Closed, consistently wound excavation meshes are tested with four overlapping-cut scenarios at 1 m and 0.5 m, including 80-cut stress cases. Water tests cover basin clipping, shoreline expansion, bounded depth, sealed lower-cavity isolation and independent snapshots.

Rendered cave traversal and the new 29-cut feedback scenario both passed with empty error logs. The latter verifies continued physics during worker preparation, retained collision, unchanged unaffected regions, stale resolution rejection and actual water mesh replacement after a shoreline cut. High-resolution eight-cut batch: about 251 ms background elapsed time; longest terrain main-thread preparation/commit in the scenario: 33.1 ms. The benchmark improved roughly 3.8x; strict frame-time guarantees, indexed meshes and smaller dirty blocks remain future work.

The normal-game regression exited 0 with every existing smoke marker and an empty error log. Updated visual captures were inspected at `artifacts/cave-feedback-wall.png`, `cave-feedback-pool.png` and `cave-feedback-lake.png`. Logs, metrics and limits are documented in MASTER_CAVE_SPIKE_RESULTS.md. Test using TEST_CAVE_LAB.cmd after closing any older running lab instance; the portable release and normal world save are unchanged.

## Connected cave water and excavation (2026-09-28)

Implemented and tested in the isolated cave lab; full details and production limits are in `MASTER_CONNECTED_WATER_RESULTS.md`. No normal-world migration or Post-Master expansion is included.

Core suite: **1,895,509 assertions passed**. Game build: **zero warnings/errors**. The shoreline coverage regression fails with 161 uncovered wet sample points when the bank correction is disabled, and passes at both 1 m and 0.5 m with the correction restored. The intentionally failing negative-control evidence is `artifacts/cave-shoreline-negative-control.txt`.

Final rendered floor and downhill-flow scenarios exited 0. Their captures were visually inspected. The twenty-stroke outlet test reaches the lower channel after 520 fixed simulation ticks, preserves a dry sealed chamber, and restores 156.307564686 cubic metres through save/reload. The accelerated test took 13.71 seconds wall time; this is not a real-time performance guarantee. Evidence: `artifacts/cave-flow-metrics.json`, `cave-flow.log`, `cave-floor-connected.log` and their PNG captures.

Cave traversal/backend/collision smoke and the full normal-game regression also exited 0. The earlier feedback scenario passed continued movement, retained collision, stale-result rejection and overlapping cuts: its high-resolution edit batch took 333.8 ms in background preparation, with a maximum measured terrain main-thread commit of 47.4 ms. This still permits visible brief hitches and is not a strict frame budget.

All five scenario error logs are empty: `cave-floor-connected-errors.log`, `cave-flow-errors.log`, `cave-smoke-errors.log`, `cave-feedback-errors.log`, `cave-connected-baseline-errors.log` under `artifacts/`. Test using `TEST_CAVE_LAB.cmd`, then Tab, Dig outlet, Outlet view; allow roughly a minute for gradual water advance. F4 pauses flow and F5/F9 save/reload terrain and water together.

## Cave shoreline/polygon follow-up (2026-09-28)

Core: **2,970,446 assertions passed**; game build: zero warnings/errors. New regressions cover shared lighting normals, four-region closure/winding for the saved six-cut player fixture and a synthetic seventh cut, coplanar water overlap and a tapering wet/dry front. Negative controls prove the old normal fallback and old water front fail their respective checks. Sampling resolution, terrain vertex positions and stored water quantities are unchanged.

Rendered evidence: `artifacts/cave-shore-fixed*`, `cave-edge-feedback*`, `cave-edge-flow*` and `cave-edge-traversal*`. The digging smoke kept physics running while preparing a 200.7 ms high-resolution batch; max terrain commit was 32.6 ms. Full explanation, source limitations and remaining angular-geometry caveats are in `MASTER_CONNECTED_WATER_RESULTS.md` under the feedback follow-up.


## Master Rework C1 macro geography (2026-09-28)

Full core suite: **3,032,901 assertions passed**. Includes 80 profile/seed combinations, immutable manifests, deterministic reconstruction, far-origin queries, exact sampled chunk seams, and bounded residency through a continental teleport in the 2,048 km preset. Game build: **zero warnings/errors**.

Final rendered macro smoke passed selection/zoom, manifest reload and stale-worker rejection; vast-world and archipelago captures were inspected. Their error logs are empty (`artifacts/macro-vast*`, `macro-archipelago*`). Existing normal-game regression passed all prior markers with an empty `macro-baseline-errors.log`.

C1 is an isolated macro preview and chunk-sampling foundation, not a completed enormous playable world. Production rendering/LOD, region transactions, generated drainage, climate and biome integration remain open. See `MASTER_MACRO_WORLD_FOUNDATION.md` and `WORLD_GENERATION_TEST_GUIDE.md`. The new Azgaar assessment adds no runtime dependency or replacement of working systems.

## C2 coarse drainage validation checkpoint (2026-09-29)

Read the newer Master supplement before continuing and audited the interrupted implementation. The existing priority-flood layer was a tested prototype, not completed hydrology. Added read-only global cell metadata, independent threshold-connectivity spill checks across 100 small scenarios, and frozen profiles/fingerprints from 15 pre-continuation worlds. Added basin separation/storage, upstream metadata, far-origin chunk-window, reconstruction and cancellation coverage. Macro algorithm 3 and drainage algorithm 1 outputs remain unchanged.

Targeted drainage: **300,985 checks passed**. Full core suite: **3,333,886 assertions passed**. Game build: **zero warnings/errors**. This is an engine-free metadata/validation change, with no new rendered-engine run or claimed water/terrain visual fix. Details, remaining limits and exact resume order are in [MASTER_DRAINAGE_CHECKPOINT.md](MASTER_DRAINAGE_CHECKPOINT.md).

Next: an opt-in coarse drainage diagnostic layer in the existing macro preview, then bounded regional river/lake refinement. Finite lake storage and the targeted local terrain/water connectivity regression are gates before production coupling. Settlements, roads, climate implementation and other future systems were not started.

## C2 diagnostics and regional refinement foundation (2026-09-29)

Full core suite: **3,375,521 assertions passed**. Build: **zero warnings/errors**. All 15 original coarse frozen fingerprints remain unchanged; 23 new regional tile hashes are pinned. The focused preview tests pass 28 checks and regional tests pass 41,607, including shared boundary heights/ports, unique merged trunks, chunk continuation, hierarchy, candidate lake storage/outlets, concurrent reconstruction and version rejection.

Both final Godot scenarios (vast-world and archipelago) exited 0 with empty error logs, exercised layer/world switches and reload, and were visually inspected. Evidence: `artifacts/regional-final-vast-world-20260929-233310.*` and `regional-final-archipelago-20260929-233310.*`. Current test instructions are in `WORLD_GENERATION_TEST_GUIDE.md`; architecture, performance, approximations and the exact resume step are in [MASTER_REGIONAL_DRAINAGE_FOUNDATION.md](MASTER_REGIONAL_DRAINAGE_FOUNDATION.md).

This is a diagnostic/refinement foundation. No terrain was carved, local water was not coupled, and the cave consistency issue remains open. Stop before climate/biomes; next validate adaptive corridors and reconcile candidate lake spill/connectivity against finer terrain.

## Receiver-aware topology 5 completion (2026-10-04)

The preserved validation run completed successfully: 111,177 receiver checks; 37 six-case comparison checks; all required historical hydrology and accepted A4 regressions; **4,813,748 full Core assertions**; Core/Game builds with **0 warnings/errors**; **11 historical fixture files byte-identical**. See [the V5 checkpoint](MASTER_HYDROLOGY_RECEIVER_DOMAIN.md) for exact per-suite totals and [current status](CURRENT_WORLD_GENERATION_STATUS.md) for the active continuation. Topology 5 is retained only as an opt-in experiment; topology 3 remains the baseline. This is not new rendered gameplay/water acceptance or a packaged release. Earlier measurements and visual evidence above remain historical.

## Playable A4 Forward+ validation (2026-10-04)

Full Core: **4,851,436 assertions**, including **37,688 playable checks**; Game build **0 warnings/errors**; **11 historical fixtures byte-identical**. Rendered Mountainous/42 and Vast/7319 passed with 14,808 and 15,152 runtime checks, 10 and 9 rebases. Legacy gameplay, macro drainage/regional preview, connected cave water and streaming lab also passed under Forward+/Vulkan. See [the checkpoint](PLAYABLE_A4_WORLD_VALIDATION.md) for exact metrics, limitations and retained evidence. Next: Tom tests the playable lab; no automatic later-system work.
