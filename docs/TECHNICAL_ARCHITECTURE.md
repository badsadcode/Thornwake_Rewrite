# Technical architecture — prototype 0.6.2

## Ecology and world sandbox

`Core/Vegetation` stores planted trees, bushes, fruit state and cleared natural stumps. Growth and fruit progress use double precision and advance through active simulation time; menus, offline time and calendar speed do not advance them. Defaults are eight active hours for a tree, two for a bush and one for fruit regrowth. World settings permit growth durations up to 720 hours. Harvested natural bushes transition into persistent vegetation and retain their location. Felled trees become stumps without automatic tree respawn.

Pine saplings are crafted from a pine cone and timber; bush seedlings use three berries and timber. Cones come from tree gathering or well-side trade. Equipped seedlings use the planting tool mode and validate reach, ground contact, spacing, roads, buildings and existing plants before consuming an item. Stump removal requires an axe; uprooting requires an axe or hoe and returns no seedling. Visual growth follows saved progress.

`Core/SandboxSettings` defines and validates 13 per-world rules. `UI/SandboxMenu` edits a separate copy and sends one Apply command; presets and Reset affect only the draft. Craft-duration changes apply when the next batch starts, preserving active job timing. Crop maturity uses the configured age threshold. A separately persisted `CalendarMinutes` value advances at the selected day length, so changing day length does not jump the clock. Vegetation freezing preserves existing growth and fruit progress.

## Player menus, hotbar and quests

`Core/Hotbar` validates ten saved item/skill references, equipment, consumable effects and cooldowns. `Content/abilities.json` defines active actions; item definitions carry use actions and restoration values. Shortcuts never move inventory stacks. Depleted known bindings persist, while new assignments require ownership. Core commands charge consumables/stamina once; `Main/PlayerMenus` performs the corresponding animation or placement transition. The adapter's internal tool mode is independent of the user-selected hotbar position.

`UI/HotbarControls` uses native Godot drag-and-drop with typed payload checks. Inventory and skills provide an editor row; updates change existing controls to keep active drags stable. `UI/PlayerScreens` connects Character, Skills and Journal to the inventory/crafting navigation. Modal state suspends world input and releases the camera.

`QuestTracker` retains completed introductory milestones. The axe objective records a completed crafting job rather than item ownership. Other milestones derive from actual resource removal, supported shelter and bandit defeat. Schema 6 originally introduced starter ownership/default shortcuts with `.v5` backup retention. Schema 7 preserves these custom layouts, inventory, equipped item, remaining cooldowns and quest completions.

## Boundaries

`Game/Core` is ordinary C# without Godot references. `WorldState` contains serialized world data. `Simulation` mutates it through gathering, crafting, building, farming, trade and economy commands. `WorldGenerator` uses a specified integer PRNG for resource placement and analytic terrain. `ContentCatalog` loads and validates the JSON item, recipe, building and resource definitions in `Content`. `SaveStore` validates complete state and writes through a temporary file with atomic replacement and a backup.

`Game/World/Valley` reconstructs scenery from state. `Visuals` creates original geometry and caches materials. Grass uses instanced meshes. The terrain height function is shared by rendering and movement, with saved radial edits applied before sampling. Collision is currently a conservative horizontal obstacle test, with a height sample for ground contact. Roads and the bridge reserve traversable corridors. `Soundscape` synthesizes a small effect palette.

`Game/UI` renders menus, vitals, context, map and messages. `Main` is the local session adapter: it turns input into simulation calls, moves the character, manages the camera, drives the simple enemy, and connects saving and UI. World data survives destroying and rebuilding the visual scene.

Since 0.1.1, `Game/Camera/OrbitCamera` owns orbit presentation and cursor capture. `Core/OrbitState` smooths yaw, pitch and zoom independently of frame rate; movement uses the current rendered yaw. `Core/PointerGestures` arbitrates camera chords against combat and placement clicks. The session releases capture for menus, placement and focus loss. Camera/sound preferences use a separate Godot ConfigFile and do not change world-save compatibility.

## Current authority model and multiplayer boundary

This release is **single player**. The local session is the only authority. There is no network transport, dedicated server, client prediction, ownership or replicated save/reconnect support. Rendering is separate from persistent data to make the next milestone possible, but that separation does not make the present build multiplayer safe.

Before enabling multiplayer, move character movement, reach/collision checks, stamina, enemy combat, placement clearance and action timing from `Main` into a server-owned command processor. Core commands currently assume a trusted local caller and do not enforce all physical interaction constraints. Never expose them directly as remote RPCs. Introduce player IDs, sequence numbers, authority validation, snapshots, ownership and reconnect tests before adding a join button.

## Content and save compatibility

JSON definitions have stable IDs. The loader validates references, quantities, prices and building radii. Adding recipes or items using existing behavior does not require gameplay code. New renderable building/resource types need a visual mapping; a public mod loader, asset packs and code mod API are future work.

Save schema is 7; generator version remains 1. Schemas 1–6 migrate while retaining applicable positions, support metadata, active work and player configuration. Configured structures store a `BuildSelection` with definition ID, variant ID, slope, material ID and mirror state, alongside their transform, level, support and paid costs. Roof modules can also reference a parent roof connection. Legacy houses retain their original geometry and placement data. Piece levels and player height remain persistent. An optional `CraftJob` stores recipe/station IDs, time remaining, reserved inputs and its queue request ID. `CraftQueue` stores at most 50 requests with recipe/station IDs, remaining unstarted batch count and an until-materials flag. The first upgraded save preserves the exact older file as `.v1` through `.v6`, as applicable; `.bak` continues to rotate. Schema-3 active tasks migrate without a queue link and finish normally before newly queued work. Schema-6 migration adds ecology/settings/calendar fields without regranting inventory or replacing custom hotbar bindings. Generated resource IDs remain unchanged. Unknown save versions are rejected.

`Core/Construction` produces the same placement plan for preview and commit: snapping, support, reach, obstruction, terrain and cost checks. Modular structures use oriented footprints, door gaps and deck heights for movement. `Simulation` handles build, door and reclaim commands, preventing the removal of needed supports and duplicate refunds. Village collision remains approximate. Ground cover is refreshed when player structures change.

Support propagates across connected floor frames. Upper floors always reference the floor below, avoiding circular support. Narrow stair ramps and height-aware collision allow three storeys without teleporting a player from the ground to an upper deck. Piece levels also control placement, picking and cutaways. Connected roof trim is removed on adjoining edges; pitched variants are manually arranged. This is a simple frame rule, not a physical load solver.

Since 0.2.1, the session stores a relative build-height offset instead of exposing a catalogue level selector. The live target resolves against the player's current floor (including stair height); Alt-wheel is intercepted before camera zoom in both captured and free-pointer input. H or selecting a new piece resets the offset. The same resolved kind and level are used by previews and committed builds. Floor selection resolves to foundation at ground height or upper_floor above it. Underlying support and three-storey limits remain unchanged. This feature originally shipped with schema 3.

Version 0.3.0 adds `BuildSnapMode.Object`, `Grid` and `Free` to the shared preview/commit plan. Object mode chooses nearby edge or object connections and floor-local quarter-metre alignment. Grid uses a half-metre world grid; Free keeps the raw horizontal pointer position. Rotation is quantized in 45° steps, with object connections able to supply their matching orientation. The session persists the preferred mode in settings. Polygon footprints support right-triangle floor/roof halves and diagonal walls, so support, overlap and walking checks use the actual piece shape. This is a limited geometric kit, not exact Valheim parity or a general polygon editor.

### Building tools and roof junctions

Version 0.4.0 adds `Content/building_tools.json` and `Core/BuildingVariants`. A tool contains visible variants, allowed slopes and materials, defaults and legacy piece mappings. `BuildSelection` is shared by the catalogue, persistent pieces, placement previews and commits. The selected variant resolves predetermined physical definitions and costs; material surcharges are part of the cost shown and charged. Floor, Wall, Stairs and Fence reuse the same selection layer with existing geometry. Each tool's options are remembered for the current session.

`Core/RoofGeometry` supplies the common surface-height sampler, rise and connection edges. Phase 2 supports slope, ridge, hip, valley and trim profiles at 22.5°, 30°, 45° and 60°. Three-metre slope modules rise along local +Z; ridge modules peak at local Z=0. Hip and valley profiles use the diagonal X=Z crease. Matching semantic edges transfer height between roof modules and reject incompatible pitches. Profile intersection checks include interior crease extrema, preventing a triangular upper deck from cutting a hip peak or valley trough.

Wall variants `gable_fill` and `gable_ridge_fill` attach to matching slope and ridge parents respectively. Mirroring chooses the opposite end. Rendering and collision follow the fill outline; a raised fill extends down to the supporting wall top, including the rectangular section below its sloped cap. Decorative eaves and gable edges retain their existing behavior. Roof references are validated for missing parents and cycles, and required supports cannot be dismantled first.

`World/VariantVisuals` uses a geometry factory registry and the same sampler for procedural rendering. Ridge faces split at the peak, timber shingles and slate have distinct surface patterns, and underside geometry remains visible indoors. Surface details are batched. A fixed 0.18 m overhang appears only on exposed eaves/gables; it does not move the three-metre connection grid. The live ghost uses the selected shape, slope and material with validity tinting. Existing legacy roof renderers preserve older shapes.

An optional variant `Mesh` reference selects a visual factory independently of validated physical `Geometry`; empty Mesh falls back to Geometry. `ValidateBuildingAssets` reports unresolved visual references at startup. Rendering missing assets produces a magenta diagnostic marker and a deduplicated warning, while invalid physical definitions are rejected by the content loader. This is an internal extension point, not an external mod loader.

Automatic shape suggestions, adjustable dimensions/overhangs and edits to an already placed variant remain future work. Dimensions remain fixed modular sizes. See `BUILDING_VARIANTS.md` for controls and `BUILDING_DESIGN_NOTES.md` for the preserved design request.

### Crafting and decoration

`Workstations` resolves village and player-built stations with horizontal and vertical reach. `Core/CraftingQueue` implements timed work in the partial `Simulation` class. It reserves one active batch at a time, produces each completion once and carries unused frame time into subsequent batches. The queue head remains linked to the active job; its remaining count excludes that active batch. Numeric requests wait FIFO for missing inputs, while until-materials requests finish when inputs run out and advance to the next request. Required station reach pauses work. Cancellation refunds the active reservation and removes that request's remaining batches; removing pending requests requires no refund. Clearing cancels active and pending work. Stations referenced by active or queued jobs cannot be dismantled.

Core crafting timing is separate from economy ticks so a crafting screen can pause the world while work advances. The queue, active timing and reservations survive saves without recharging or offline progress. A basic work pose is present; dedicated character/station animations and interaction alignment remain future work.

`UI/CollectionMenus` provides category tabs, icon grids, selected-item details, ingredient counts, station locks, progress and cancellation. Recipe cards support double-click queue submission, a batch selector, Max, until-materials mode and queue controls. `UI/ItemIcon` draws original vector-like item glyphs with Godot drawing calls. Decorations are inventory items consumed by placement and fully returned by reclamation.

## Deliberate prototype choices

- One authored village layout inside a bounded 110×110 metre seeded region. This is not yet a procedural settlement generator or streamed continent.
- Analytic heightfield with radial sculpting replaces a Terrain3D dependency for the physical-feel experiment. Terrain3D remains an evaluation item before larger terrain and navigation work.
- Four visual villagers follow simple time-based paths; economy is aggregate, with 15-second ticks and 90-second caravan opportunities. Individual NPC professions and schedules are not implemented.
- The cart is a visual representation of trade; it is not an independently persistent caravan actor with cargo or navigation.
- Hunger, thirst, tool durability, furnished village interiors and structural load simulation are not implemented. Bush fruit regrowth and planted vegetation growth are implemented; automatic tree respawn is not. Player shelters support up to three storeys, stairs, doors, furnishings and cutaways. There is no fall damage or solid stair railing yet.
- Menu panels pause the local world. A crafting screen advances its active job while focused. The application must be running for work to advance.
- Primitive geometry and synthesized sound establish a coherent original prototype kit. Final art, animation, ambient audio and accessibility work remain.

## Reproducible release

`export_presets.cfg` exports Windows x64 with embedded game resources and a neighbouring self-contained .NET runtime folder. The ZIP must retain that folder. Player installation requires no engine or runtime download. Development dependencies stay in `.tools` and are excluded from the exported game and source control.

Earlier releases through 0.5.0 passed extracted-package checks with a Windows-only PATH, a nonexistent DOTNET_ROOT and disabled multilevel lookup. Version 0.6.0 also passed extracted-package checks for sandbox and hotbar input, including all roof and ecology scenarios, with empty error logs. Evidence is recorded in `VALIDATION.md`.


## Cosmetic building art (0.6.1)

`BuildSelection.AppearanceId` is separate from construction `MaterialId`. `BuildingAppearances` validates palette/model definitions and exact variant/pitch/mirror mappings. `Simulation.Refinish` validates equipment, work state, reach and compatibility before replacing only the appearance choice. Existing compatible legacy walls acquire an equivalent selection. Shape, support, paid costs and reclamation are preserved.

`World/BuildingArt.cs` resolves optional packed scenes, copies their mesh/transform/material hierarchy into ordinary visual nodes, and applies edge-group visibility and door-leaf state. Missing scene mappings fall back to the matching procedural visual. Ghosts duplicate per-surface materials, retaining textures and vertex colours without mutating shared resources. This is static cosmetic artwork; skeletons/animation are not an imported building gameplay system.

Save schema 8 migrates earlier selections to Original and permanently retains `.v7` saves on upgrade. Model paths live in the content catalogue, not in saved houses. See `BLENDER_BUILDING_ART.md` for the exact authoring contract.
## Craft action presentation (0.6.2)

`Core/CraftActivity` resolves a recipe's optional `activity` string and station default, validates allowed combinations, and derives elapsed time, progress and readable stages from `CraftJob`. Only the plank recipe explicitly selects `saw`; existing recipes retain their costs, duration and output. These queries cannot advance simulation or award items.

`World/CraftAnimation` owns disposable hand tools and station workpieces. It turns the actor toward a work point using the station's world transform, blends both arm poses, and animates from saved elapsed work rather than wall-clock time. `Valley.StationNode` resolves both village stations and player-built structures. A plain campfire gets a temporary cooking pot. Lost reach clears the pose; menu/focus pauses freeze it. Teardown restores static station tools and player equipment, including during world reconstruction and shutdown.

`UI/CraftActivityHud` presents current work outside modal menus. Watch work closes the menu without changing the queue; E reopens the active station's queue. `Simulation.IsCraftInReach` lets the local adapter permit movement when an existing job is paused outside station reach. Current work still uses one sequential player-operated queue. Animation does not add independent station production or authoritative tool items. The prototype adapter is replaceable by authored rigs/clips without changing saved job timing or recipes.
## Shovel earthworks (0.7.0)

Core/Earthworks.cs introduces the versioned sparse height overlay, integer soil ledger, preview validation and saved atomic work task. TerrainData evaluates legacy ground plus triangular sample deltas; shared material/depth classification drives shovel restrictions and overlays. Schema 9 retains original older saves and initializes empty earthworks for migration.

EarthworkPresentation.cs owns context input, preview meshes and the timed working pose. EarthworkHud.cs supplies the compact four-mode toolbar and progress. Completing a task invalidates only affected ValleyTerrain regions. Craft queues and earthworks share exclusive player work. See EARTHWORKS.md for units, timing, protection, save rules and remaining design steps.

## Landscape generation (0.8.0)

LandscapeGenerator implements generator 2 as pure sample passes, with settings frozen per world. WorldGenerator.Generate(WorldState) dispatches resource generation by version; stable candidate IDs survive density/slope filtering. TerrainData.BaseHeight dispatches both landscape algorithms, then retains the existing legacy-plus-earthworks overlays. Save schema 10 requires valid generation metadata and never upgrades the generator for schemas 1–9.

WorldSetup supplies a draft, live preview, bounded settings and explicit create/cancel actions. TerrainMapImage shades the same height queries used by gameplay; ValleyMap caches this image and draws actual natural/planted trees. Completed shovel strokes refresh map shading. See LANDSCAPES.md for compatibility and remaining scope.
