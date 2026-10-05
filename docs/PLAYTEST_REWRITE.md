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

## New in build 3 (after Tom's second playtest)
* **Rivers**: smooth natural courses (no more grid kinks or straight runs), real meander loops, bends and wide
  swings; pools, narrows and broad stretches; every river mouth now reaches the sea; curvy delta branches; smooth
  river edges in the distance; water no longer vanishes when you look upstream.
* **Flowing water**: ripples move downstream and streak along the current; fast water gets foam streaks.
* **Esc → Settings**: grass and forest density, rocks, bushes; view distances (terrain, trees, tree cards, small
  objects); shadows, ambient occlusion, bounce light, reflections, glow, sun shafts, volumetric fog, haze,
  brightness, colour; clouds, cloud shadows, wind; field of view, anti-aliasing, render scale, V-Sync, FPS limit.
  Saved automatically. Hover a setting's name for an explanation.
* **Esc → New world**: preset and seed plus world shape (size, continents, islands, coast, mountain ranges and
  heights), terrain (rolling ground, plains, crags, erosion) and rivers and lakes (number, width, meanders,
  wandering, width variety, lakes, deltas).
* **Sky**: drifting clouds with matching cloud shadows on the land; sun shafts through trees and gaps in clouds.
* Denser grass (2×) and forests by default; distant tree cards look like crowns when seen from above.

## New in build 4 (same night as build 3)
* **Water simulation** around you: hold **G** to pour water where you look, **J** places a spring, **H** starts
  rain, **K** clears it. Water runs downhill, pools in hollows, joins rivers and lakes, slowly soaks in and leaves
  dark wet ground that dries over a few minutes. You can swim in deep simulated water.
* **Finer ground near you** (1 m instead of 2 m): smoother shorelines and creek banks.
* **Forests**: clearer forest and meadow patches; distant tree cards as dense as the real trees they replace.
* Less memory for big worlds (Vast world ~200 MB instead of ~310 MB).

## New in build 5
* **Digging**: hold **V** to dig and **B** to raise the ground where you look. Water follows: dig a channel from a
  pond and it drains into it. (Edits are not saved yet.)
* **Banks**: reed clumps along rivers and lakes, a darker damp strip just above the waterline.
* **Rain you can see**: H now also turns the weather: the sky clouds over grey, light dims, haze thickens and rain
  falls around you (puddles form from the water simulation). Dug ground shows bare soil.

## New in build 6 (after Tom's build-5 playtest)
* **River banks**: the course stays smooth, but each bank now has its own shape: slow widening and narrowing,
  small wobble, occasional bulges with shallow shelves (wading depth) and cuts into the bank; patches of mud, sand
  and gravel along the waterline.
* **Digging next to water**: rivers, lakes and the sea now flow into ground you dig beside them (a trench from a
  river fills; cutting through a river's levee floods the lower land behind it).
* **Sea shores**: the sea deepens much sooner (a few metres deep within ~150 m instead of ankle-deep for hundreds of
  metres) and the beach is a narrower band.
* **Trees, rocks and logs are part of the world**: their amounts moved from Settings to **New world → Forests and
  objects** (fixed when the world is generated). Settings keep only how far away they are drawn.
* **Going back**: New world → *Build 5 terrain (for comparison)* generates rivers and shores exactly as in build 5.
  New world → *Bank irregularity* and *Shore steepness* tune the new look.

## New in build 8
* **Ocean waves**: real waves on the sea and lakes, from calm to storm (Esc → Settings → Sky → Sea state; rain makes
  the sea rougher). You swim on them. They are built so boats can ride them later.
* **Beach waves**: the sea runs up and down the beach, with foam at the waterline and breakers rolling in.
* **River mouths**: no more step or dyke where a river enters the sea or a lake; the riverbed runs smoothly into
  the sea/lake floor, and the river's greener water spreads into the bay.
* **Digging by rivers**: no more gaps between river water and water in a dug channel.
* **Going back**: New world → Terrain version: current / Preview 7 / Build 5.
* **New world screen** (Esc → New world): pick a **world type** (Balanced continents, Great mainland, Archipelago,
  Mountain kingdoms, Many small continents, Islands + mainland, classic), a **seed**, a **world size** from 32 to
  4096 km, and optionally override any parameter (all on **Auto** by default). **Preview map** shows the whole world
  in 2D before you generate. A type keeps its character at every size (bigger worlds get more landmasses, not giant
  ones). See docs/WORLD_CREATION.md.
* **Your own world types**: set things up, type a name, **Save as world type**. It is listed with the others and
  works at any size; the JSON file in the user folder can be edited for deeper tweaks.
* **Rain water stays**: puddles and dug ponds no longer vanish when the rain stops. Soil soaks up water until it is
  saturated, then water stands; it drains and evaporates slowly, so ponds last and the world does not flood.
  Wet ground has soft edges instead of squares, and all ground darkens and gets small puddles while it rains.

## New in build 7
* **Digging tools** (1–5, left/right mouse): **shovel** digs soil and fills from a soil bag (1.5 m³, weak on rock),
  **pickaxe** breaks ground and rock in the direction you aim, **hoe** levels to the aimed height and smooths,
  **terraform (dev)** with adjustable mode (T: dig/raise/flatten/smooth), shape (Y: round soft/hard, square,
  directional), radius ([ ]) and strength (- =). A ring shows where and how wide the tool works (red: out of reach).
* **River beaches**: low irregular strips of sand, gravel or mud between water and bank, different on each side.
* Rivers no longer stop just short of lakes.

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
| G (hold) | Pour water where you look (local water simulation) |
| J | Place / remove a spring where you look |
| H | Rain on / off (Esc → Settings → Simulation: rain strength) |
| K | Clear all simulated water |
| 1–5 | Tools: hands, shovel, pickaxe, hoe, terraform (dev) |
| Left / right mouse | Use the tool (primary / secondary) |
| T, Y, [ ], - = | Dev tool: mode, shape, radius, strength |

## Please check (and tell me what you think)
1. **Frame rate** with the new defaults (more grass, clouds, sun shafts). If it drops, try Esc → Settings:
   grass density, shadows, render scale. Tell me which setting made the biggest difference.
2. **Rivers** from above and up close: curves, widths, mouths and deltas. Use **New world → Rivers and lakes**
   to try more or fewer meanders or rivers, and tell me the values you like (they can become the defaults).
3. **Flowing water**: is the direction readable? Too strong or too weak?
4. **Sky**: cloud amount, cloud shadow darkness, sun shafts (try an evening sun: Esc → World → time of day ~17).
5. **Forests**: density and the change from 3D trees to distant cards.
6. Anything broken: F12 saves a screenshot; the HUD position line lets me find the exact spot.
