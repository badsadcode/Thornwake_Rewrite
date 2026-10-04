# Cave terrain test guide

> 2026-10-04: preserved under desktop Forward+ and revalidated with the connected-flow smoke. This is an isolated historical fixture; the current playable A4 milestone and continuation are in [current status](CURRENT_WORLD_GENERATION_STATUS.md).

This is the next Master Rework architecture test, separate from the normal Thornwake world. It is a small authored terrain/cave fixture used to validate real underground geometry, collision, excavation and persistence. It is not the finished natural-cave generator.

## Start

Double-click **TEST_CAVE_LAB.cmd** in the project root. The launcher uses this project's existing Godot executable and compiled code; no download or installation is needed on this development machine. The old packaged Thornwake executable does not contain this test.

On another checkout, build `Thornwake.csproj` first using the documented Godot/.NET dependencies. You can also open `Game/Development/CaveTerrainLab.tscn` in Godot and run that scene.

## Controls

| Control | Action |
|---|---|
| WASD + mouse | Walk and look |
| Space | Jump |
| Left click | Excavate aimed terrain within five metres; development action with no inventory rewards |
| Tab | Release/capture cursor to use the buttons |
| Entrance / Cave / Lake / Dry chamber buttons | Jump to test locations |
| F4 | Pause/resume connected water for inspection |
| Dig outlet / Outlet view | Cut a continuous test channel from the hilltop lake and watch downhill flow |
| F5 / Save | Save terrain edits and water progression |
| F9 / Load | Reload terrain and water, then return to the entrance |
| F6 | Compare 1 m and 0.5 m terrain samples |
| F7 | Unload and rebuild terrain/collision from the same edit state |
| F8 | Compare hybrid terrain with fully volumetric terrain |
| F10 | Color terrain by region to inspect boundaries |
| Escape | Exit |

Close any already-running cave test and reopen the launcher to use the updated build. Terrain and water are now prepared by background workers. Digging and F6/F8 comparisons retain the old collision until the replacement is ready, so movement continues. Initial loading, F7 and loading a different saved field wait where collision is genuinely absent. A new dig is accepted once terrain and its corresponding water preparation are ready; the HUD separates worker time from main-thread commit time. Losing window focus releases the cursor; Tab captures it again.

## Suggested test (5–10 minutes)

1. Walk toward the amber lights and into the hill. Follow the tunnel into the chamber and walk back out. Look for invisible walls, falling through seams or being snapped onto the hill above.
2. Jump inside the cave. The ceiling should stop you physically.
3. Aim at a wall/floor and excavate. The shape and collision should change together. F10 helps inspect edits crossing region borders. Some outer locations and the surface support slab are deliberately protected. Deep floor cuts can create pits that cannot simply be walked out of; use the Entrance button if you get stuck.
4. Press F5, then F7. Your cuts should survive the terrain rebuild. Press F9 and return to the cave; they should also survive reloading.
5. Use Tab and the Lake/Dry chamber buttons. The elevated lake and underground pool have different levels. The sealed cavity beneath the lake stays dry.
6. Press F6 for 0.5 m samples, then repeatedly excavate overlapping parts of a cave wall. Look for missing triangles or see-through patches; keep walking while a dig rebuilds. The previous collision remains until the whole affected batch changes together.
7. Inspect the underground pool and upper lake. Both should meet their basin walls. Dig into a connected shore and allow water to advance into it. An isolated hole should stay dry. Water movement is gradual, and its coarse simulation is still an architecture prototype; swimming is not included.
8. Use **Dig outlet**, then **Outlet view**. Watch the hilltop lake feed the descending channel. Allow roughly a minute for the front to reach the lower hillside; F4 pauses/resumes it for inspection.
9. Compare F6/F8 modes and try switching resolution again while generation is busy. Report seams, movement problems, long pauses or an unexpected loss of edits.

Orange rock is a single test vein extending through solid terrain and exposed at the surface and cave wall. Excavation exposes the existing shape; it awards no ore. The slab on the hill is a protected support/collision fixture, not a new building system.

## Saves and scope

Manual F5 saves this test's cuts and water progression to `%APPDATA%/Thornwake/master-cave-spike.json`, with an atomic replacement and `.bak`. Opening the lab starts a fresh fixture; F9 explicitly restores the saved cuts. Exit does not autosave. Your normal `world.json` and existing gameplay are not loaded or modified.

The lab now includes bounded connected-water flow, gradual flooding and persistent water state. It does not implement swimming, mining rewards, tools/materials, natural cave generation, continent generation or production world migration. The cave and outlet remain authored fixtures. Visual materials and lamps remain diagnostic. See MASTER_CONNECTED_WATER_RESULTS.md for the measurements and limits.

Version-1 saves remain readable. Loading uses the smoother version-2 excavation in memory; F5 writes the upgraded terrain/water save and preserves the previous file as `.bak`. Older lab builds cannot read version-2 saves.

For useful feedback, note the mode/resolution, nearest test location, action, and whether it still happens after F7 or F9.



