# Thornwake Rewrite

**This repository is the Thornwake rewrite** (moved here from the `rewrite/main` branch of `badsadcode/Thornwake` on
2026-10-09, with its full history; the original repository is left untouched). The rewrite lives in `Game/Rewrite/`
(game) and `Game/WorldGen/` (deterministic world generation); the original prototype's code and the notes below are
kept for reference.

* What changed and why: [`docs/DECISIONS.md`](docs/DECISIONS.md) · what each build added: [`docs/PLAYTEST_REWRITE.md`](docs/PLAYTEST_REWRITE.md)
* Return points (commit per build): [`docs/SNAPSHOTS.md`](docs/SNAPSHOTS.md) · plan: [`docs/REWRITE_PLAN.md`](docs/REWRITE_PLAN.md), [`docs/GAMEPLAY_PLAN.md`](docs/GAMEPLAY_PLAN.md)
* Tests: `dotnet run --project Tests/WorldGen/Thornwake.WorldGen.Tests.csproj -c Release` · engine: Godot 4.7.2 .NET (Forward+; 4.5.1 up to branch `godot-4.5.1`)

---

# Thornwake — Harthwood Valley

> **Playable A4 development lab:** double-click `TEST_PLAYABLE_WORLD.cmd`; see [controls and setup](docs/PLAYABLE_WORLD_TEST_GUIDE.md) and [validation checkpoint](docs/PLAYABLE_A4_WORLD_VALIDATION.md). Current desktop source uses Forward+; the OpenGL requirement below describes the older packaged prototype, not this development build.

> Master Rework development status and the current continuation point are in [CURRENT_WORLD_GENERATION_STATUS.md](docs/CURRENT_WORLD_GENERATION_STATUS.md). Accepted macro terrain A4, retained hydrology topology 3 and opt-in topology 5 are separate from the playable valley/release described below.

A standalone Windows 3D fantasy life prototype based on `Persistent_Fantasy_Life_RPG_Game_Design_Bible_v0.1.md`. This is the first playable milestone described in section 89, with small farming and settlement economy experiments. It is not the full commercial game or a multiplayer release.

## Play

Open `dist/Thornwake-Windows/Thornwake.exe`, or extract the entire `dist/Thornwake-0.8.0-Windows-x64.zip` and run `Thornwake.exe` inside it. Keep the accompanying `data_Thornwake_windows_x86_64` folder beside the executable. Godot and the .NET runtime are included. Players do not install an engine, SDK, launcher, package manager, or additional runtime. Internet access is not required. Version 0.8.0 adds new seeded landscapes, pre-game terrain settings and a live world preview. The runtime is bundled in the package. See `docs/VALIDATION.md` for verification evidence.

Target: Windows 10/11, x64, graphics hardware supporting OpenGL 3.3. This prototype is unsigned. Other platforms have not been built or tested.

Choose **Enter the valley** for a new landscape with the displayed seed, **Customize new world** to preview and adjust it first, or **Continue** to restore your saved world. New landscape settings apply only when creating a world. Continuing preserves the original terrain and trees.

## First evening in Harthwood

1. Walk beyond the village and press **E** near a tree, stone or berry bush. A tree takes three strokes; a crafted stone axe reduces this to two.
2. Press **C** for handcrafting: make a better axe or prepare food. Use **E** at the village carpenter's bench to saw planks. Open **I** for your inventory grid, item details and decoration placement.
3. Equip the hammer with **2**, then press **B**. Choose a building piece and place it within six paces. **V** cycles Object, Grid and Free placement; **R / Shift + R** rotate forward/backward in 45° steps. Foundations connect; walls and doors snap to their edges. One wall or doorway supports the connected floor frame's roof, including irregular rooms.
4. Select **3**, click open ground to prepare soil, then select **4** and click the prepared soil to plant berry seeds. After one minute of active play, approach and press **E** to harvest.
5. Visit the well to buy, sell, and hear village news. Food stock and prices change as the local economy runs. Selling berries helps the village.
6. If you want a fight, follow the map to the old camp northwest of the village. **5** equips your blade. Block the bandit's windup with right mouse, or dodge with Space and movement. Defeating him restores regular caravan deliveries.
7. Return to the fire to heal, build your own hearth, and save your place in the valley.

## Controls

| Control | Action |
|---|---|
| WASD | Move relative to camera |
| Shift | Sprint |
| Space + movement | Dodge |
| E | Context interaction / open or close door / gather / harvest / rest / market |
| 1–9, 0 | Use the item or skill assigned to that hotbar slot |
| Left mouse | Use selected tool / attack / place |
| Right mouse | Block with sword; cancel an active shovel stroke; otherwise cancel placement |
| Middle mouse + move | Orbit camera while using combat buttons independently |
| Both mouse buttons + move | Orbit camera; suppresses those clicks as gameplay actions |
| Hold Q / R | Smoothly rotate camera; in hammer mode R rotates the piece 45°, Shift + R reverses |
| Mouse wheel | Smooth zoom |
| Home | Smoothly reset camera angle and zoom |
| Hold Tab | Release cursor while using always-on mouse rotation |
| I / C / B | Inventory / handcrafting / building (hammer only); press again to close |
| P / K / J | Character / skills / journal and quests; press again to close |
| V (hammer equipped) | Cycle Object / Grid / Free placement |
| T / Shift + T (placing) | Next / previous tool shape |
| Y / Shift + Y (placing) | Next / previous supported roof pitch |
| N / Shift + N (placing) | Next / previous material |
| U / Shift + U (hammer) | Next / previous cosmetic roof or wall finish |
| Z (hammer, Roof or Wall selected) | Toggle finish brush; left-click a matching piece to repaint |
| G (placing) | Mirror a supported shape, currently the gable edge |
| Alt + mouse wheel | Raise/lower the live building or dismantling preview |
| H (hammer equipped) | Reset placement height to your current floor |
| T (shovel) | Cycle Dig / Fill / Level / Smooth |
| Ctrl + wheel (shovel) | Adjust brush radius, 1–2.5 m |
| G (shovel) | Sample the aimed ground as the level height |
| Alt + wheel (shovel) | Select Level and adjust its height in 10 cm steps |
| H (shovel) | Reset brush size and target height to your feet |
| F9 | Cycle terrain inspection overlays |
| M | Expand or collapse map |
| F | Eat meal, bread or berries |
| F5 | Save |
| Escape | Close panel / pause |
| F11 | Toggle fullscreen |

The local world pauses while menus are open; the selected crafting task continues in its crafting screen. Inventory, pause and loss of window focus pause crafting too. By default, one game day takes eight real minutes; world sandbox settings can change this. The simulation stops when the application closes; unfinished crafting is saved and resumes during play.

### Camera options

Press **Escape** to switch between **mouse drag** (default) and **always follow mouse**, adjust sensitivity, or invert vertical look. Mouse drag supports middle mouse or both mouse buttons. Middle mouse is useful in combat because left attack and right block remain independent. In drag mode a single left press has an 80 ms chord-detection window; a quick click executes on release. Always-follow mode dispatches attacks immediately.

Always-follow mode shows an aiming mark. Hold **Tab** to use the cursor. Menus, building, soil editing and planting automatically release it; mouse drag remains available during placement. Switching back to the axe or sword resumes mouse look. Losing window focus releases capture and suspends gameplay; returning restores the selected mode. Camera and sound options persist in `%APPDATA%/Thornwake/settings.cfg`, independently of world saves.

## Cosmetic building finishes

With the hammer equipped, choose **Roof** or **Wall** in B. **U** cycles cosmetic finishes; **Z** toggles a finish brush for repainting existing matching pieces. Finishes preserve construction costs, snapping, collision, support and reclaimed materials. Original restores the original material appearance. N still selects the construction recipe/material. Roofs offer cedar, terracotta, thatch and charcoal finishes; walls offer limewash, oak boards and fieldstone.

Custom Blender artwork can replace these visuals through exact shape/pitch/finish model mappings. The fieldstone wall uses a working GLB example. See [Blender building art](docs/BLENDER_BUILDING_ART.md) for dimensions, export workflow and mappings. Model replacements are included when the standalone game is rebuilt.

## Character menus and quick access

**I** opens inventory, **P** character, **K** skills, **J** journal and quests, and **C** handcrafting. A shared navigation bar connects them. The character screen shows your equipped tool, health, stamina and journey statistics; the skills screen shows actual practice and the active Dodge ability. The journal tracks four introductory objectives and retains completed milestones, with the activity log alongside them.

The ten-slot hotbar is yours to arrange. Drag a usable inventory item or an active skill onto any slot; drag between slots to move or swap shortcuts. Right-click a slot to clear it. Items stay in your inventory. Empty or depleted shortcuts cannot be used; depleted bindings remain ready for when you replenish them. The inventory and skills screens include a hotbar editor.

Default keys **1–5** equip the travelling axe, hammer, hoe, seeds and blade; **6** eats berries, and **8** dodges. **7**, **9** and **0** start empty. These are defaults, not fixed tool keys. Building requires the hammer to be equipped wherever you put it. Equip crafted axes and blades to gain their improvements. Food and potions consume the exact assigned item and keep your current tool equipped. Space and the Dodge shortcut share the same stamina cost and cooldown.

A **berry tonic** can be made at a cooking station from three berries and one grain. It restores 40 health and has an eight-second cooldown. Full-health use does not waste it. Hotbar layout, equipped item, cooldowns and completed quests are saved with your character. Old saves receive the previously implicit starter tools once during migration.

## Saves

Saves are stored in `%APPDATA%/Thornwake/world.json`. Autosave runs every 45 seconds during play; F5, Save, and normal exit also save. The previous successful save is retained as `world.json.bak`. Starting another world preserves the prior save as `world.json.previous` before it is replaced. One active world slot is supported.

Saved state includes seed and generator version, character position and stats, inventory, skill practice, removed resources, structures, soil edits, crops and growth time, bandit defeat, economy, and journal. Corrupt or unsupported saves produce a load error rather than being silently reset. Runtime smoke tests use `smoke-world.json`, separate from the player's world.

Version 0.8.0 upgrades older worlds to save schema 10 while preserving their pieces and unfinished work. The first upgraded save retains the original as `world.json.v1`, `.v2`, `.v3`, `.v4`, `.v5`, `.v6`, `.v7`, `.v8` or `.v9`, separately from the rotating `.bak`. Older game builds cannot read schema 10; restore a copy of the matching older backup if returning to an older build. Configured building pieces retain their tool, shape, pitch, material and mirror choice, plus support connections. Height, building levels, the active crafting task and pending crafting requests are saved. Loading does not charge ingredients again or grant offline progress.

## Build a shelter

Place a foundation, then point beside it to connect another at the same height. **Object** placement connects floor edges, walls and nearby objects; **Grid** places on the half-metre world grid; **Free** follows the pointer without position snapping. **V** cycles these modes, and the choice is remembered. **R / Shift + R** rotate in 45° steps. Object snapping may align the piece to an existing connection; Grid and Free give more direct placement control. All three modes retain support, reach, collision and material checks.

A wall or doorway anywhere on a connected floor frame supports its roof tiles. Connected and flat tiles form continuous coverage for L-shaped rooms and courtyards. Triangle floor and roof pieces are right-triangle halves with 45°/90° corners; diagonal walls close their long edges. Press **E** near a doorway to open or close it.

The catalogue now groups existing pieces into **Roof**, **Floor**, **Wall**, **Stairs** and **Fence** tools. Options stay beside the world during placement: **T / Shift + T** change shape, **Y / Shift + Y** change supported roof pitch, **N / Shift + N** change material, and **G** mirrors a supported shape. Panel buttons provide the same choices. Each tool remembers its options for the current session, and the live ghost shows the chosen shape and finish with a green or red validity tint.

The Roof tool supplies **Slope**, **Ridge**, **Hip**, **Valley**, **Eave** and **Gable edge** at **22.5°, 30°, 45° or 60°**, finished with timber shingles or slate. Slate adds stone to the displayed cost. In Object mode, matching edges carry their height into the next section; incompatible pitches do not connect. Eaves and gable edges attach as trim to a matching slope, and G moves the gable trim to the opposite side. Exposed edges have a fixed **0.18 m overhang**; adjoining edges meet at the shared grid line. Old placed houses retain their geometry, and the earlier flat, triangular and shallow roof shapes remain available.

The second roof phase adds compatible hip and valley corners plus **Sloped gable** and **Peaked gable** wall fills. Gable fills attach to matching slope/ridge roofs; G switches sides, and raised fills extend down to the supporting wall top. Automatic shape suggestions, transitions, complex ridge junctions and editing already placed pieces remain future work. Module dimensions are fixed. See `docs/BUILDING_VARIANTS.md` for the building guide and `docs/BUILDING_DESIGN_NOTES.md` for the preserved design request.

Choose a piece first, then adjust its live preview in the world. **Alt + mouse wheel** raises or lowers the target by a storey relative to the character's current floor; **H** resets it to your floor. The same controls apply to floors, walls, doors, roofs, stairs, stations, decorations and dismantling. Normal scrolling zooms the camera. A new selection starts at your floor, and the height indicator shows whether you are building above or below yourself. Raising the preview keeps its horizontal aim until you move the pointer or character.

**Floor** groups full and triangular pieces: each becomes a foundation on the ground or an upper floor when raised. The live preview shows the applicable material costs. Build up to three storeys with the existing support rules. Add a staircase on a floor; keep both ends and the tile directly above it open. Choose Floor and use Alt + wheel up to place a landing beside the stair's high end. Continue floors over supported rooms, then add walls, furnishings and roofs. Roofs and higher pieces cut away near the player so you can see inside. Railings are visual; walking off an unprotected edge drops the player down.

Choose **Dismantle a piece** at the bottom of the **B** menu, then point and click. Each piece returns half its paid materials, rounded down per material. Remove roofs and furniture before their required supports. Placement messages explain blocked ground, occupied edges, missing supports and missing materials. Soil cannot be edited beneath existing structures; prepare uneven ground before building.

## Craft and decorate

Handcrafting makes simple tools, rations, rugs and paintings. The carpenter makes boards, chairs and tables; a kiln fires bricks and planters; a forge makes fittings and lanterns; cooking stations and ovens prepare food. These stations are available around the village and can also be built at home. Buy clay, iron ore and grain at the village well. The market remains a simple trade menu in this update.

Recipes take 4–10 seconds per batch. Select a recipe, set **Batches** (1–999), and add it to the queue; double-clicking its card also queues it. **Max** selects the number affordable from the supplies currently in your satchel. **Until materials run out** repeats the recipe until another full batch cannot be paid for, then moves to the next request. Several recipes can share the queue, up to 50 requests.

Only one batch works at a time, and only its ingredients are reserved. Numbered requests wait at the front of the queue if ingredients are missing; later requests wait behind them. Work requiring a station pauses while you are out of reach. Remove an unstarted request without losing materials, cancel active work to return its reserved materials and remove the rest of that request, or clear the whole queue. Finished batches are kept. Queue progress survives saving and loading.

The player stays in place while working. This release includes a basic working pose; dedicated sawing, smithing, kiln and cooking animations remain future work.

Crafted furnishings appear in your satchel. Select one and choose **Place in the world**, or select it under **Decor** in the hammer catalogue. Object mode aligns decorations to nearby objects and a quarter-metre floor grid; Grid and Free offer other positioning choices. Decorations can be placed inside or outside, and rugs can sit beneath furniture. Dismantling a decoration returns the complete item so it can be rearranged. Paintings currently stand on easels.

## Living trees, bushes and world sandbox

Felled trees leave stumps. Equip an **axe**, approach a stump and press **E** (or use the axe) to remove it and recover timber. Removed stumps stay removed after loading.

Trees yield **pine cones**, which are also sold at the village well. In **C**, prepare a pine sapling from one cone and one timber, or a berry seedling from three berries and one timber. Equip a seedling from the inventory or hotbar, aim at open ground and left-click. The preview checks space for roots, neighbouring plants, buildings, roads and water. Prepared crop soil is not required for trees or perennial bushes. With an axe or hoe equipped, **X** uproots a nearby young tree or planted/regrowing bush; it does not refund a seedling.

Default growth is intentionally slow: **8 active hours for a pine**, **2 hours for a bush**, and **1 hour for berries to return after harvest**. Plants grow gradually, retain their progress in saves, and only mature trees can be harvested for timber. Berry bushes remain after harvesting and bear fruit again. Old felled trees do not automatically return; plant replacements. Growth pauses in menus and while the game is closed.

Open **Escape → World sandbox** to adjust this world. Ecology, Crafting, Survival and World pages expose 13 rules: tree/bush growth, berry regrowth, crop duration, plant spacing, stump timber yield, growth freeze, crafting time, gathered yields, stamina recovery, incoming damage, death coin loss, and day length. Tree/bush/regrowth durations can reach **720 active hours**. Slow living, Builder and Harsh presets change a draft; **Apply** commits it and **Cancel/Escape** discards it.

Changing growth settings preserves each plant's completed percentage. Day length changes the calendar independently of real-time growth. Crafting speed applies to new batches; the active batch keeps its reserved ingredients and duration. Crop duration applies to existing crop age. Settings are saved per world.

This is the first sandbox framework. More crafting dependencies, tool wear, skill requirements, production chains, world-generation controls and station-specific animations remain planned additions.

## Development

Stack: Godot **4.5.1 .NET**, C#, .NET 8 target; compiled with the .NET 9 SDK on the development machine. Rendering uses Godot's compatibility renderer. Scenery combines original procedural models and the new Blender woodland models with original generated pixel textures; no asset downloads occur during play.

Run `tools/build.ps1` from PowerShell to restore, test, compile, import, export and package. If needed, `tools/bootstrap.ps1` downloads the pinned official Godot engine and Windows export templates into `.tools`. A .NET SDK is required only for development. The downloaded template archive is large because upstream distributes platforms together; bootstrap extracts only Windows x64 templates.

Open `project.godot` in the Godot .NET editor to work on the game. Pure simulation tests: `dotnet run --project Tests/Thornwake.Core.Tests.csproj`. Engine smoke test: `Thornwake.exe -- --smoke-test --capture=<absolute PNG path>`.

See `docs/TECHNICAL_ARCHITECTURE.md`, `docs/MILESTONES.md`, and `docs/VALIDATION.md` for boundaries, remaining scope and evidence. The original design bible remains the design source of truth.





## Blender asset templates

The separate authoring kit starts at [ArtKit/README.md](ArtKit/README.md). Open ArtKit/Thornwake_Asset_Library.blend to browse 96 models, then work from an individual template in ArtKit/Templates. Templates use actual prototype geometry, named roots and metre scale. Roof/wall model mapping is available now; other asset families and UI sprites retain their prototype implementations until their dedicated replacement adapters are added. Keep artist working copies outside the generated ArtKit folder.

## Crafting actions (0.6.2)

Crafting now has separate prototype motions for sawing planks, joinery, smithing, tending a kiln, stirring food, tending an oven and handwork. Characters turn toward their station and temporarily stow their equipped tool. Workpieces and action tools are presentation only; ingredients, output and work time still belong to the saved crafting job.

Choose **Watch work** in the crafting menu, or close it with Escape, to watch the action. A compact **Current work** card shows the activity, batch progress, remaining time and number of orders. Press **E** to reopen the active queue, where you can cancel or clear orders. Camera orbit remains available while watching.

Other menus and loss of window focus pause both work and the action pose. Out-of-reach station work pauses and now allows walking back to resume. Cancellation/completion removes temporary workpieces and restores the equipped tool. Saved elapsed time restores the action's phase; opening the game does not grant offline crafting progress. Existing schema-8 worlds remain compatible.

These are procedural prototype actions. Automatic positioning at a work socket, precise hand contact, authored skeletal animation, tool wear and deeper component recipes remain future work. The existing Blender kit is unchanged and requires Blender 5.1 for its native files.
## Woodland vegetation pack

[VegetationPack/README.md](VegetationPack/README.md) contains the new 17-tree woodland collection: Blender 5.1 source, 51 GLBs across three LODs, pixel textures with nearest filtering, and rendered previews. The portable archive is dist/Thornwake-Woodland-Asset-Pack-v1.zip. The pack includes its own Godot review project. Version 0.6.3 uses copies of its exported trees in the live forest.

## Woodland forest (0.6.3)

The forest now uses the Blender tree pack: pine, spruce, green and amber birch, oak, copper maple, sparse woodland trees, poplar and willow, with 17 distinct variants. Willows favor the riverbanks. Species, model variant and orientation derive from each resource's persistent identity, so loading an existing world gives it the new art without relocating resources or restoring felled trees.

Trees use crisp nearest-filtered pixel textures and three authored detail levels. Distance switching has a dead band to avoid flicker during camera orbit. A small pixel-dithered window clears foliage between the camera and the character, then restores it as the view changes; the rest of each crown stays visible. Trunks remain visible and block walking. Far trees do not cast expensive distant shadows. Amber/copper foliage is currently a fixed visual variant, not a seasonal simulation.

Pine saplings grow into the new pine models at the existing sandbox-controlled rate. Stumps match the tree's bark and trunk size and remain removable with the axe. Every species yields timber; pine cones now come from wild pines and mature planted pines. Species names appear in chopping prompts. Other species' seedlings, wind and authored felling animations remain future work.

The woodland release retained schema 8; earthworks now upgrades it to schema 9. Existing buildings, resource IDs, planting progress and removed stumps are retained. Source backup: backups/Thornwake-source-before-forest-integration-20260927-104841.zip.

## Terrain foundation (0.6.4)

The updated terrain/tools/construction proposal is the current design reference. The architecture review and implementation sequence are in [docs/TERRAIN_ARCHITECTURE_REVIEW.md](docs/TERRAIN_ARCHITECTURE_REVIEW.md).

Terrain queries now share one engine-independent source for height, original height, player changes, surface normal, slope and initial material/moisture classification. Existing saved height calculations remain exact; generator version 1 is retained. The earthworks update extends save schema 8 to 9.

The visible ground is divided into 16 m regions with a 1 m mesh sampling experiment. A small terrain edit refreshes only affected ground and grass regions, including neighbouring borders/normals, and regrounds nearby resources. Other terrain region nodes remain intact. Display sampling remains separate from authoritative terrain queries.

**F9** cycles optional terrain inspection views: region boundaries, cut/fill changes, slope, material, moisture, then normal view. A persistent legend identifies the active view. These classifications describe the current simple valley; they are not a new erosion or hydrology simulation.

This foundation supports the shovel workflow described below. New generation passes and world-generation settings remain pending; palisade blueprints/material delivery follow the earthworks foundation. The proposed third-person camera improvements are tracked separately, as requested by the updated design.
## Shovel earthworks (0.7.0)

1. Press **C** and craft **Wooden shovel** by hand (3 wood, 2 stone, 5 seconds). Equip it in **I**, or drag it onto any hotbar slot.
2. Stand on open ground and aim nearby. The solid ring shows the brush; the dashed outer ring shows the full clearance needed around its edges. Green means the stroke is allowed; red explains what blocks it.
3. Choose **Dig**, **Fill**, **Level** or **Smooth** in the toolbar, or cycle with **T**. Left-click starts one 1.6-second stroke. Soil and stamina are spent only when the stroke completes. Move, switch tools or right-click to cancel unfinished work.
4. Digging collects loose soil in 10-litre units. Filling uses it; leveling and smoothing reuse removed soil within the stroke before spending inventory. Each completed stroke costs 8 stamina.
5. **Ctrl + wheel** adjusts size. For leveling, **G** samples the ground under the cursor and **Alt + wheel** adjusts the reference height. **H** resets the brush and height. Ordinary wheel still zooms; middle mouse and the two-button chord still orbit.

The shovel protects roads, the river, village sites, existing buildings, crops, plants, rocks and stumps across its full footprint. Clear a site first. Shovels cannot excavate bedrock; maximum cutting depth is about 1.2 m below the generated surface, with a 2.5 m overall shaping envelope. Standing outside the brush keeps work from changing the ground beneath your feet. The hoe now only cultivates; Shift no longer creates fill.

Menus and lost focus pause shovel work. Terrain and any unfinished stroke are saved exactly; no offline progress is awarded. Original schema-8 worlds retain their old radial terrain edits, and their first upgraded save keeps a permanent `.v8` copy.

This first earthworks release uses the existing seeded valley and a compact 1 m terrain sample grid. New geography generation, grading/trenching, pickaxes, underground mining, blueprints, delivery/labour, and the proposed third-person camera changes remain later work. See [earthworks implementation notes](docs/EARTHWORKS.md).

## New-world landscapes (0.8.0)

**Customize new world** opens a live map preview with **Gentle**, **Harthwood** and **Rugged** presets. Adjust hill/ridge height, small surface variations and woodland density, or enter a different seed. The map shows actual generated elevations and trees, 1.5 m contours, the village and river crossing. Create this world starts with those choices; Back/Escape discards the draft.

New worlds use generator 2: broad lowlands, elongated ridges, plateau areas, drainage-shaped valleys, prepared village/road/camp sites and river banks. The current playable valley remains 110 m across. Terrain settings are saved and fixed at creation. Growth, crafting and survival rules remain adjustable in **Escape → World sandbox**, whose World tab also shows the saved landscape settings.

The in-game map uses actual terrain and resource locations; felled trees disappear from it, planted trees appear as they grow, and completed shovel work refreshes its ground shading. Terrain rendering, movement, construction, resources and shovel edits share the same ground queries.

**Existing worlds keep generator 1**, including their original heights, resources, buildings and all earthworks. Schema 10 adds generation settings; the first upgraded save preserves a `.v9` backup. New generation is not silently applied to a played world.

This remains a finite valley prototype. Large world bounds, streaming, true hydrology/erosion, procedural settlements and caves remain future work. The next construction milestone is blueprint planning, material delivery and timed labour. Digging strength/depth per stroke is recorded for a future sandbox setting; its current tuning is unchanged in 0.8.0.

## Master Rework foundation (development)

The v3 Master Rework is the current development priority; Post-Master crafting/tool expansion remains deferred. Read [the code audit and milestone plan](docs/MASTER_REWORK_AUDIT.md) and [the terrain/world decision record](docs/MASTER_WORLD_ARCHITECTURE.md). Existing gameplay, generators 1/2 and schema-10 saves remain active.

The first streaming foundation is opt-in. Open `Game/Development/WorldStreamingLab.tscn` in Godot and run that scene, or use the command in [the milestone report](docs/MASTER_STREAMING_FOUNDATION.md). This finite test harness reuses the actual terrain/earthwork renderer, with bounded loading/unloading. It is not yet continent generation or a cave backend.

## Cave terrain laboratory

The next Master Rework test is available through **TEST_CAVE_LAB.cmd** in the project root. It exercises real cave floors/ceilings, collision, editable region boundaries and isolated saves while preserving normal gameplay. See [the test guide](docs/CAVE_LAB_TEST_GUIDE.md) for controls and a short test route. This is an architecture fixture; the finished procedural cave/world generator follows later.
