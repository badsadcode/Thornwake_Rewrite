# Milestones

## Delivered: 0.8.0 — seeded landscapes and new-world setup

Generator 2 separates macro landforms, drainage/detail shaping, site preparation, river carving and resource filtering. New-world setup provides a live authoritative map, seed entry, three presets and relief/roughness/forest controls. Saved settings are fixed after creation; continued generator-1 worlds are preserved exactly. The gameplay map now reflects terrain, actual tree positions, removals and planted growth. Schema 10 preserves original schema-9 saves. See LANDSCAPES.md and VALIDATION.md.

### Next work

- Blueprint planning, material deliveries and timed labour for a first construction project.
- User feedback: digging currently feels too weak/slow. Add depth/strength tuning to world sandbox settings, including pre-game rules, in a later balancing pass. No digging-speed change in 0.8.0.
- Larger configurable bounds, streaming, authored POI placement passes, traversal/camera improvements and richer terrain materials. The 0.5 m gameplay comparison remains pending.

## Delivered: 0.7.0 — first playable earthworks

Craftable shovel, Dig/Fill/Level/Smooth, a live footprint/reference preview, contextual size/height controls, timed cancellable strokes, soil conservation, protected site clearance and bedrock restrictions. Schema 9 persists a sparse integer sample grid and unfinished work while preserving generator 1 and legacy edits. The hoe now cultivates without moving earth. See EARTHWORKS.md and VALIDATION.md.

This completes the shovel interaction slice. New landscape generation passes/settings, blueprints, material deliveries, worker labour and third-person camera revisions remain separate upcoming steps.

## Delivered: 0.6.3–0.6.4 — woodland and terrain foundation

Seventeen distinct woodland models with three LODs, stable saved appearance, planting/stumps and localized foliage cutaway; shared terrain queries, 16 m render regions and local rebuilds with matched border heights/normals.

## Delivered: 0.6.2 — visible crafting actions

Seven distinct procedural activity profiles now cover handwork, sawing, joinery, smithing, kiln tending, cooking and baking. The character turns toward the station, equipped tools are stowed, and temporary tools/workpieces are removed on completion or cancellation. A world-view progress card and Watch work button keep the queue visible while watching the action. Required station reach still pauses progress; an unreachable active task now permits walking back instead of locking movement.

The job clock controls action phase, preserving menu/focus pauses and saved elapsed work. No save-schema change or recipe-price changes. Precise work-socket placement, skeletal animation and deeper component/tool requirements are still pending.

## Delivered: 0.6.1 — cosmetic building finishes and authored models

Roof and Wall tools now separate cosmetic finish from construction material. U cycles finishes; Z toggles a repainting brush with an aimed ghost. Existing compatible walls can be finished without rebuilding. Four roof finishes and three wall finishes are available, plus Original. The brush preserves placement, support references, collision, paid costs and dismantling refunds; it requires the hammer and normal building reach.

Exact variant/pitch/mirror mappings can replace temporary visuals with GLB or Godot scene assets. A bundled fieldstone GLB exercises the full model path. Imported mesh surfaces and textures remain separate in tinted previews. Optional named overhang groups follow roof joins; door models can supply a DoorLeaf hinge. Missing artwork retains matching procedural geometry. Raised gable fills keep procedural extension geometry when a fixed authored model would leave a gap. The art guide is in BLENDER_BUILDING_ART.md.

Schema 8 saves cosmetic identity, with permanent `.v7` backup on the first upgraded save. Source backup before this update: `backups/Thornwake-source-20260926-130046.zip`, 116 files verified against the SHA-256 manifest. More physical shapes, model-driven animated production and advanced roof intersections remain future work.

## Delivered: 0.6.0 — roof junctions, growing plants and world settings

Roof Phase 2 adds hip and valley junctions at 22.5°, 30°, 45° and 60°. Matching partial and full gable fills live under the Wall tool, can be mirrored, and attach to the corresponding slope or ridge. Raised gable fills extend down to the supporting wall top. Shared profile checks cover joins, height, collision and support removal; existing placed geometry is preserved.

Trees and berry bushes can be planted from crafted seedlings. Default maturity is eight active hours for trees and two for bushes; harvested bushes regrow fruit in one hour. Progress is continuous and saved, with custom durations up to 720 hours. Natural harvested bushes remain and regrow berries. Felled trees leave stumps and do not automatically respawn. Seedlings can be equipped from the hotbar and planted on open land; E with an axe clears stumps, and X with an axe or hoe uproots immature trees or bushes without refunding seedlings.

The world sandbox menu provides 13 rules covering growth, crafting, gathering, survival and calendar speed. Slow living, Builder and Harsh presets change a draft; Apply commits it, while Cancel keeps current rules. This is a practical settings foundation, not a complete survival sandbox system. Schema 7 stores ecology, settings and calendar progress while retaining custom hotbars and inventory. Migrated schema-6 worlds retain a permanent `.v6` backup.

Source backup: `backups/Thornwake-source-20260925-170705.zip` — 100 files verified. Core, rendered roof/ecology/settings checks and extracted-package verification passed. Evidence is recorded in `VALIDATION.md`.

## Delivered: 0.5.0 — character menus and customizable hotbar

Inventory, Character, Skills, Journal and Handcraft share one navigation system, with visible HUD buttons and I/P/K/J/C shortcuts. Character shows a portrait, current equipment, vitals and journey statistics. Skills shows earned practice and the draggable Dodge ability. Journal tracks four persistent introductory milestones and saved activity.

Ten hotbar slots accept owned tools, weapons, food, potions, decorations and available abilities. Native dragging assigns inventory items or abilities and swaps shortcuts; right-click clears a shortcut without removing the item. Number keys follow the saved layout. Actual equipment governs building access and crafted weapon/tool improvements. Exact-item consumption, potion cooldown and a shared Space/hotbar Dodge cost are enforced in the simulation. A berry tonic is craftable at cooking stations.

Schema 6 stores layout, equipment, cooldowns and completed quests. Earlier worlds receive their previously implicit starter tools and retain a permanent older-schema backup. Source backup: `backups/Thornwake-source-20260925-162755.zip` — 86 files verified by SHA-256. Core tests and rendered menu/drag checks pass; package verification is recorded in `VALIDATION.md`.

Armor equipment slots, additional active abilities, a skill tree, character customization, quest rewards and a larger narrative quest system remain future work. The new screens display the systems currently implemented.

## Delivered: 0.1 — first playable

The design bible's section 89 establishes a bounded first prototype. This build implements an elevated zoomable camera, responsive movement and sprinting, chopping with impacts and falling trees, resource collection, an axe upgrade, physical building previews and placement, smooth soil edits, a hostile enemy with windup/block/dodge, village prefabs, and save/load. Supporting experiments include farming, crafting, market trade, aggregate settlement supply, a caravan illustration, day/night light, and skill practice counters.

The Windows release is playable offline and ships its runtime. It is a development milestone, not a declaration that all design-bible feature definitions are complete.

## Delivered: camera and construction updates

Update 0.1.1 completes the first camera-control pass: continuous keyboard orbit, mouse drag, always-follow mode, pitch/zoom limits, sensitivity/inversion, persistent preferences and safe cursor transitions. A verified source backup was created before this change in `backups/Thornwake-source-20260925-020409.zip` with a per-file SHA-256 manifest. The 0.1.0 distributable is retained.

Update 0.1.2 adds connected foundations, snapped timber walls and doors, supported roofs, raised walking surfaces, door interaction and collision, nearby roof cutaways, and ordered dismantling with material reclamation. Placement previews share the actual build rules and check affordability. Schema-1 saves migrate with a retained original backup. Source backup before construction: `backups/Thornwake-source-20260925-021653.zip` (57 files, verified against the accompanying manifest). Earlier release ZIPs are retained.

## Delivered: 0.4.0 — building tools, Phase 1

A reusable tool catalogue groups Roof, Floor, Wall, Stairs and Fence pieces. Placement options remain visible beside the world: T changes shape, Y changes supported pitch, N changes material, G mirrors supported shapes, and Shift reverses the cycling controls. Each tool remembers its choices during the session. Rotation, snapping and live height controls remain available.

The first roof phase supplies slope, ridge, eave and gable-edge modules at 30° and 45°, with timber shingles and slate finishes. A shared height model keeps compatible connections and rendered surfaces aligned. Joined edges meet without overlapping overhangs; exposed edges have a fixed 0.18 m overhang. Eaves and gable edges attach to matching slope pieces. Existing houses preserve their original geometry and paid costs. Schema 5 saves tool selections and roof references, with permanent `.v4` backups for migrated 0.3.0 worlds.

Source backup before this update: `backups/Thornwake-source-20260925-160325.zip` (72 files, verified). Rendered runtime checks pass, including the inspected roof joins in `artifacts/v040-variants.png`. The extracted 0.4.0 package passed with installed runtime paths disabled, including live option mouse and keyboard input.

Phase 2 in 0.6.0 adds hip/valley junctions, matching gable wall fills and 22.5°/60° pitches. Automatic suggestions, editing placed variants and adjustable dimensions remain future work. The detailed implementation guide is in `BUILDING_VARIANTS.md`; the full user design request is preserved in `BUILDING_DESIGN_NOTES.md`.

## Delivered: 0.3.0 — placement and crafting queues

Object, Grid and Free placement are available through **V**, with the selected mode saved in preferences. **R / Shift + R** rotate pieces in 45° steps. Triangle floors and roofs, plus diagonal walls, extend angled rooms while retaining support and collision checks. The triangle kit uses right-triangle halves with 45°/90° corners; it does not reproduce the full Valheim building system. Existing live height controls remain available.

Crafting supports double-click queuing, numbered batches, Max and Until materials run out. A persistent FIFO queue reserves only the active batch's ingredients, waits clearly for missing materials or station reach, and supports removing requests, cancellation and clearing. Schema 4 preserves queues and migrates earlier active work with permanent older-schema backups. Source backup before this update: `backups/Thornwake-source-20260925-110234.zip` (68 files, verified). Core, rendered engine and extracted 0.3.0 package checks passed, including launch with installed runtime paths disabled.

## Delivered: 0.2 — workshops, upper floors and live height

Update 0.2.1 replaces catalogue floor selection with live, character-relative height adjustment. Alt + wheel raises/lowers every build family and dismantling; H resets to the character's floor; normal wheel still zooms. A unified floor tool resolves foundations versus upper floors automatically, with applicable costs shown in the live preview. Horizontal aim is held while adjusting height. Source backup: `backups/Thornwake-source-20260925-104304.zip` (68 files, verified).

Update 0.2.0 delivers connected roof coverage across irregular floor frames, roof variants, stairs, three-storey construction, height-aware movement and saves, hammer-only build mode, inventory/crafting/build grids, timed station jobs, and six craftable decorations. Source backup before this update: `backups/Thornwake-source-20260925-030035.zip`, 60 files verified by SHA-256.

## Next: physical feel and construction refinement

Detailed sawing/smithing/cooking animation, station alignment, deeper component recipes, tool conditions, profession requirements, rail collisions and furnished village interiors remain future work. Crafting currently uses a basic working pose and one sequential queue. Independent production at multiple stations is a later step.

- Playtest movement, melee reach, dodge timing, camera occlusion and input remapping on several PCs.
- Move combat and physical command validation out of the local adapter into reusable simulation systems.
- Replace village footprint collisions with explicit prefab collision and entry geometry; improve camera occlusion around trees and walls.
- Extend construction with repair, ownership, more roof junctions and deeper support rules. Current support checks span connected frames and three storeys, without structural loads.
- Add a small onboarding help panel, settings persistence, UI scaling, and controller support.

## Then: section 90 — procedural settlements

Prefab metadata, site evaluation, lot planning, grading, road links, navigable entrances, and rejection reports. Generate and validate at least 100 settlement seeds, including path reachability rather than resource spacing alone.

## Then: section 91 — living world

Individual homes/jobs, production/consumption chains, persistent caravan cargo and road threats. Replace the current aggregate experiment with observable cause and effect. Evaluate the one-hour peaceful village test.

## Then: section 92 — multiplayer persistence

Authoritative headless server, two to four clients, validated input commands, world snapshots, identity/ownership, save/restart/reconnect, and adversarial duplicate/stale command tests. No multiplayer claim until those tests pass.

## Later: vertical slice

One region with village, town, dungeon, connected professions, bandit disruption, player housing and tested multiplayer. Final content scope follows demonstrated system quality, as required by the design bible.


## Authoring kit: Blender templates, September 2026

`ArtKit/Thornwake_Asset_Library.blend` browses 96 native Blender templates by asset family. Individual files preserve the game's actual geometry, dimensions and origins, with editable MODEL, locked original REFERENCE, non-exported guides and a MODEL-only GLB export helper. The kit includes an asset dimensions CSV, measured JSON manifest, future icon naming list, and the building art integration guide.

All templates were imported and reopened in Blender 5.1.1 with measured dimensions checked after axis conversion. Five representative files (slope roof, hip roof, door, axe and character) were exported back through the included helper and verified in Godot against the original bounds and door hinge. Characters are proportion/pivot references, not final rigs. Non-building replacement adapters and custom icon loading remain future work; prototype gameplay art is unchanged.

Source backup before authoring work: `backups/Thornwake-source-20260926-192845.zip`, 131 verified source files. Generated templates are isolated from the game import/export pipeline with `ArtKit/.gdignore`. Keep artist working files outside ArtKit so regenerating templates cannot replace finished artwork.

## Woodland integration — 0.6.3

Integrated all 17 Blender tree variants and 51 LOD exports. Stable resource identity selects species and orientation; riverbanks favor willows. The game now includes nearest-filtered foliage, distance LOD switching, player-view canopy cutaways, matching bark stumps and species-aware chopping prompts. Pine saplings use the new meshes and existing growth settings. Pine cones are gathered from pines.

Resource placement, save schema 8 and construction/crafting systems remain compatible. Further woodland work includes other species' seedlings, seasonal transitions, wind and felling animation, and longer forest-scale playtests.

## Terrain foundation — 0.6.4

Current design: Thornwake_Terrain_Terraforming_Tools_Construction_updated.md. Inspection and reuse/conflict analysis: docs/TERRAIN_ARCHITECTURE_REVIEW.md.

Completed shared legacy terrain authority, independent sampled-region queries, slope/normal and initial material/moisture classification, 16 m terrain/grass regions, local dirty-region rebuilding with seam/normal halos, affected-resource grounding, and F9 inspection overlays. Existing building/snapping, controls and schema-8 saved heights are preserved.

Next: the proposal's first playable shovel milestone, including versioned generation/settings/sample-delta persistence, lower/fill/level/smooth work, real materials/time/stamina, and appropriate terrain/tool restrictions. The current radial edit log is intentionally retained until its explicit migration is ready. Blueprints and the proposed movement/camera changes remain subsequent phases; this release does not claim those loops are complete.
## Design follow-up — 28 September 2026

The user wants full-world variation and an authored POI workflow, beyond the fixed test valley. See WORLD_GENERATION_VISION.md for the proposed world-atlas pipeline, creator/editor workflow, persistent unique story identities, settlement-name registry, compatibility constraints and recommended atlas-inspector milestone. This is a design proposal only; 0.8.0 and its generator are unchanged.
