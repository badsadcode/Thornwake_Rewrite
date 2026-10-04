# Playable A4 world test guide

This is an isolated visual-validation lab, not the normal game. Desktop rendering uses **Godot 4.5.1 .NET Forward+**. The lab verifies and displays the actual rendering method/driver. The normal valley entry scene and gameplay saves are preserved.

## Launch

Double-click `TEST_PLAYABLE_WORLD.cmd` in the project root. It builds the current source and starts the pinned local Godot engine. If the engine/dependencies are missing on a fresh clone, follow `GIT_WORKFLOW.md` and `tools/bootstrap.ps1` first.

Choose **Mountain kingdoms / 42** (profile `mountainous`) or **Vast world / 7319** (`vast-world`) in the panel, then **Load frozen / generate A4 world**. The other existing profiles are also selectable. Initial generation loads nearby collision first, then extends the view outward; let it settle before judging distant terrain. The lab deliberately does not change the normal game entry point.

Frozen manifests are isolated in Godot's `user://playable-a4/<profile>-<seed>.json`. Existing files are validated and reconstructed; catalogue changes do not rewrite their profiles. No player state, terrain edits or fluid quantities are saved by this lab. Bookmarks last for the session and clear when switching worlds.

## Controls

| Control | Action |
| --- | --- |
| Tab / F1 | Capture mouse and explore / open the debug panel |
| Escape | Release mouse and show the panel; close the window to exit |
| WASD | Move forward/back and strafe relative to camera yaw |
| Mouse / wheel | Look, pitch and third-person zoom |
| Space | Jump while walking; fly upward while in flight |
| Ctrl | Fly downward |
| F2 / F3 | Fly / noclip (noclip also enables fly) |
| G | Toggle gravity |
| F4 | Terrain tile boundaries |
| F5 | Inspect the current coarse hydrology cell once |
| F6 / F7 | Add session bookmark / return to previous position |
| F8 | Copy full reproduction report to clipboard |
| F9 / F10 | Hide/show HUD / compact versus detailed HUD |

Walk and fly have separate numeric multipliers and 1/2/5/10/25/50 presets. Base speeds are 5 and 20 m/s. Fast noclip flight may outrun terrain preparation. Walking and collision flight wait for detailed collision along the swept path; they do not treat missing terrain as supported ground.

Enter **X Z** to teleport just above sampled terrain, or **X Y Z** for an exact global target (metres, decimal dot). A below-ground exact target enables fly/noclip so you can recover. The region field takes integer **16 m WorldRegion indices**, not 256 m render-tile indices. Feature targets use existing continent/range/coarse drainage descriptors plus bounded local checks. **Mountain approach / face the range** moves roughly 3.5 km from the highest sampled range point and faces it. Feature targets are useful inspection locations, not guarantees of a final mountain summit, lake or river.

The coordinate system supports continental travel and double-precision global positions. Engine objects use a nearby floating origin. Very high flight beyond 6.5 km above terrain suspends terrain preparation; descend with fly/noclip or use X/Z teleport. This is an explicit laboratory rendering envelope, not the extent of the world.

## Overlays and water

Independent panel controls show tile boundaries, near/outer rings, resident/pending tiles, local origin information, slope/elevation colouring, corridor proposals, basin outlines and water status. Near/outer tint shows actual resident detail, including retained detail during transitions. Streaming wire overlays cover the nearby diagnostic window; counts include the whole bounded outer ring.

- **Cyan / representative sampled sea:** static sea at the frozen profile's sea level, clipped to sampled terrain and checked against the A4 ocean context. The material uses a 2 cm render-only depth bias on the shallow shelf; geographic clipping remains at declared sea level. It is approximate presentation, not a continuous-terrain proof or a fluid solver.
- **Gold / provisional reach:** at most a 64 m window of an inspected descending corridor, checked at 2 m intervals for terrain-supported banks. It has no storage or flow authority. A valid coarse route alone does not produce a surface.
- **Magenta / unresolved basin:** candidate footprint outline at its candidate stage. No lake is filled, and no final spill/storage claim is made.
- **White / pink corridor lines:** descending sampled proposals / barriers or hypothetical submergence. These lines are metadata, not physical flow and not confirmed river water.

F5 requests one regional/inspection-v1 tile on a separate bounded worker. Inspection does not continuously follow the player. F8 includes the selected layer/candidate identity and world manifest. A missing or unfinished inspection is unknown, not evidence that the location is dry. The existing generation-time topology-3 baseline and opt-in topology-5 experiment are unchanged and are not invoked as runtime fluids.

There is no swimming, buoyancy, ice, storage, rainfall coupling, river simulation or ocean-wave simulation. Static ripples are presentation only. Water is not a walkable collider. Representative sea and diagnostic basins are enough to inspect coast/terrain scale; do not interpret rejected river windows as completed river geometry.

## What to inspect

Start on the first continent, walk/strafe, jump and inspect tree scale. Fly and increase speed, watch tiles load/unload, use distant feature teleports and return to a bookmark. Try the mountain approach, high range point, coast and unresolved basin. Toggle detail/streaming overlays, inspect the selected basin, then turn diagnostics off to judge geography.

Report suspicious locations with **F8**, a screenshot and what looked wrong. Include whether the outer ring had finished loading. Useful questions: mountain scale/silhouettes, broad slope traversability, travel distances, coast shape, tree density and terrain transitions. The fixed 8 m nearby mesh and coarse outer interiors are a validation approximation; vegetation is temporary scale scatter, not final ecology or harvestable world content.

## Deferred tools and preserved labs

The sculpt section is an explicit disabled extension point. The current valley edit keys and authored cave patch are not a production-compatible edit authority for this streamed world. Raise/lower/dig/fill/flatten/smooth/carve must eventually use the same real terrain authority as gameplay; this lab creates no competing edit save format.

Use the panel's cave button or `TEST_CAVE_LAB.cmd` for the existing authored cave/connected-water test. Use `TEST_WORLD_GENERATION.cmd` for the macro map and drainage lab. They remain separate; the cave fixture is not a generated A4 cave. The known cave water/terrain integration issue is still open.

First-person zoom, camera collision against trees, tree harvesting/ecology, final terrain LOD/materials, persistent region edits, natural caves, runtime water, climate/biomes and map/fog-of-war are deferred.

See [the validation checkpoint](PLAYABLE_A4_WORLD_VALIDATION.md) for measured results and [current status](CURRENT_WORLD_GENERATION_STATUS.md) for the authoritative continuation. After this milestone, Tom's walking/flying feedback decides the next implementation priority.
