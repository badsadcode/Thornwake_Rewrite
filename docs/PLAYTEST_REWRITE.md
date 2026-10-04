# Thornwake rewrite: playtest build

## How to run
1. Unzip the whole folder anywhere (keep `Thornwake.exe` next to `data_Thornwake_windows_x86_64`).
2. Double-click `Thornwake.exe`. Nothing needs installing: Godot and .NET are bundled.
   Windows SmartScreen may warn because the build is unsigned: choose *More info → Run anyway*.
3. In the launcher, choose **Explore the new world (rewrite preview)**.
   **Harthwood Valley (original game)** is the unchanged original game, for comparison.

## What's new in this build (rewrite preview)
* **New world generator**: same world sizes and presets as before (512 km to 2,048 km), with the same
  continents, islands and mountain ranges as the accepted A4 layout. New on top of that:
  * **Erosion**: river valleys carved into the land and mountains.
  * **Rivers**: real river networks from springs to the sea, widening downstream, meandering, always flowing
    downhill. Up to ~100 m wide.
  * **Lakes**: lakes with outlets in natural basins.
  * **Walking-scale ground**: rolling hills, flatter plains in places, rocky detail in the mountains.
* **Streaming terrain to the horizon**: about 20 km of view distance with smooth detail levels and no loading
  stops while walking or flying.
* **Water** using GangTheGAME's water look, adapted: see-through shallows, light patterns (caustics) on the bed,
  reflections, foam at the shoreline, gentle swell on the sea.
* **Forests** from the Blender tree pack: broadleaf in the lowlands, conifers higher up, willows by rivers.
* The world builds in a few seconds (5 s or so for the 2,048 km "Vast world").

Not in this build yet: harvesting, building, crafting and saving in the new world (those still live in the
original valley game until milestone M9), the shallow-water simulation (water you can disturb or flood with:
milestone M6), caves.

## New in build 2 (after Tom's first playtest)
* Rivers: natural curves and meanders, varying width, wider estuary mouths, small deltas on big rivers, smooth
  junctions; no more trenches (river levels follow the actual valley floor).
* Water: the straight lines are gone (caused by MSAA; this world now uses FXAA anti-aliasing).
* Distant forests stay visible out to ~6 km (simple tree cards far away).
* Ground detail: grass tufts, stones, rocks, boulders and small rock outcrops on steep ground, bushes at forest
  edges, stumps and fallen logs in forests (placeholders until your assets arrive), finer close-up ground texture.
* Beaches: a sand band along the sea instead of blotchy sand on low ground.

## Controls
| Key | Action |
|---|---|
| WASD | Walk |
| Shift | Sprint (faster flying while flying) |
| Space | Jump / swim up / fly up |
| Mouse | Look (Tab frees the cursor; click to capture it again) |
| Mouse wheel | Camera zoom |
| F | Toggle flying (Space up, C down, Shift faster, Ctrl ×6) |
| N | Noclip (fly through terrain) |
| Esc | Menu: **teleport** to interesting places, new world (preset + seed), time of day, back to launcher |
| F1 / F2 | Hide help / hide HUD |
| F3 | Show terrain tile grid |
| F12 | Screenshot (saved to `%APPDATA%\Thornwake\`) |

## Please check (and tell me what you think)
1. **Frame rate and hitches** on your PC, walking and flying fast (F, then Shift/Ctrl). The FPS is on the HUD.
2. **Terrain feel**: too flat, too rough, too many or too few hills? How do plains and mountains look?
3. **Rivers and lakes**: do they look natural? Too many rivers, too few, too wide, too narrow?
4. **Water look**: colour and clarity of sea, lakes and rivers; foam amount; should it look more stylised
   to match the low-poly trees and buildings?
5. **Forests**: density, species mix, how far away trees disappear (they turn into darker green ground
   beyond about 1 km).
6. Use **Esc → Teleport** to visit the largest lake, the greatest river mouth, the foothills and the summit.
   Try another preset/seed from the same menu.
7. Anything broken: holes in the ground, falling through, water in the wrong place, odd seams. F12 saves a
   screenshot; the HUD position line lets me find the exact spot.
