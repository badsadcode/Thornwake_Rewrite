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

## New in build 12 — time, weather, wind, night sky, cliffs (round 7)
* **Fixes**: no more tree-free lanes in a grid pattern; hanging grass and roots only on real cliff edges; ground mist no
  longer shows white streaks from above; god rays break through gaps in the clouds.
* **Sun** (Settings → Lighting and effects): *classic* (the build 11 sun) or *natural* (softer at noon, larger and orange
  at sunrise/sunset); *Sun glare* sets how bright the disc and halo are.
* **Day and night** (Settings → Time): *Day length* in real minutes (default 120; up to 1440 = real time); *Daylight share*
  auto = realistic for where you are and the *Season*, or a percentage for longer days/nights; *Stop time*. The HUD shows
  the day, time, daylight hours and the real minutes of day and night.
* **Night sky**: stars that twinkle and turn with the night, the Milky Way, the moon with its phases and moonlight,
  planets (Settings → Sky: Night sky, Planets).
* **Weather** (Settings → Simulation): fronts pass every several game hours; rain is rare in deserts and frequent in wet
  climates, sometimes heavy, sometimes a thunderstorm with lightning; it snows where it is freezing or the ground is
  snowy. *Rain frequency* changes how often. H still starts/stops rain by hand.
* **Wind**: veers and gusts by itself, stronger in storms; clouds, waves, mist, grass, plants and trees move with it
  (trees sway). HUD: wind speed and where it comes from. *Wind strength* in Settings → Sky.
* **Clouds**: *Cloud resolution* 256/512/1024; volumetric clouds work now (experimental).
* **World map**: press **M** — terrain or biomes (B), wheel zoom, drag, **right-click to teleport** there.
* **Collision**: you bump into tree trunks, dead trees, cacti, stumps, logs, bushes, rocks and boulders.
* **Cliffs and rock**: a pixel rock texture (uneven layers, faults, cracks) on all bare rock; big blocky rock slabs set
  into cliff faces; huge boulders here and there; rocky stretches of shore; rocks take their biome's rock colour.
* **Rock looks per biome**: layered (temperate), granite blocks (alpine, taiga, tundra), basalt columns (volcanic),
  banded sandstone (desert, badlands, savanna), mossy rock (ancient forest, jungle, swamps), icy rock (arctic); they
  blend where biomes meet, and boulders take their biome's look. Snow no longer covers steep rock faces.
* **Lakes no longer end in straight dry walls** (your build 11 screenshot): the water fills the whole basin and closes
  with a low shore.
* **Swamps** look like swamps: murky pools in the flat wet ground, mud around them, reeds along their edges.
* **Water colour per biome**: murky swamps, turquoise glacial and alpine lakes, dark taiga water, brown badlands rivers.
* **Biome layout** (New world → Climate and biomes): *realistic* (default) or *patchwork* — organic patches of random
  biomes all over the land, with a *Patch size*; "Biomes in this world" toggles pick which biomes appear.
* **Alden Wayfarer**, the first explorer, now carries his name (Hall of Fame: `docs/HALL_OF_FAME.md`).

## New in build 11 — plants, skies, fog, cliffs
* **Placeholder plants** for the biomes that had none (pixel textures in the woodland style, built in code):
  palms, saguaro and barrel cacti, dead trees and snags, acacia (savanna), jungle giants with buttress roots and
  hanging vines, swamp cypress with hanging moss, dry desert shrubs. They are placeholders until real art exists.
* **Leaf translucency** (Settings → Lighting and effects, 0–2): sunlight glows through leaves, palm fronds and grass
  when you look toward the sun. Grass also sways a little in the breeze.
* **Clouds** (Settings → Sky → Cloud quality): *layered* (new default) adds high, thin cirrus drifting slowly on
  another heading and low, ragged clouds racing under the main clouds; *volumetric (experimental)* draws the main
  clouds as lit 3D volumes (heavier on the GPU — please tell me how it runs and looks); *one layer* is build 10.
* **Biome skies** (Settings → Sky, off by default): the sky and haze take on the biome's mood — dusty desert, humid
  jungle, ashen volcanic, gloomy blighted swamp, crisp alpine.
* **Ground fog and mist** (Settings → Lighting and effects): mist pools in valleys, over rivers and lakes below you,
  thickest at dawn, after rain and in misty biomes, and burns off toward midday, with soft banks of mist drifting
  low over the ground around you. "Valleys + mist patches" adds volumetric wisps (I could not see those in the cloud's
  software renderer — please tell me if they show for you). The HUD shows the current mist amount.
* **Depth of field** (option, off by default): the far distance softens like a camera lens.
* **Cliffs**: steep faces show layered rock with ledges and cracks instead of stretched streaks; grass hangs over the
  lips, roots poke out under them near trees, and boulders gather at the feet.

## New in build 10 — biomes (phase 1)
* **14 biomes** placed from a new climate model (temperature from latitude and height, rain carried inland by the
  wind, rain shadows behind mountains): Temperate grassland, Temperate forest, Ancient forest, Taiga, Tundra,
  Arctic/Glacier, Alpine, Swamp, Blighted swamp, Jungle, Desert, Badlands, Savanna, Volcanic. Each has its own ground
  colours, tree mix and density, grass, bare rock and snow. Borders blend softly; rare biomes (Ancient forest,
  Blighted swamp, Volcanic) appear as patches inside others.
* **F4** colours the ground by biome; the HUD shows the biome you stand in, temperature and rainfall.
  New world → **Show biomes** draws the biome map; new **Climate** settings (latitude, latitude span, warmth,
  wetness) move the biomes around.
* **Your own biomes**: JSON files in `%APPDATA%\Thornwake\biomes\` (and `asset_sets\`), no code needed — see
  `docs/BIOMES.md`.
* Biomes don't change the shape of the land yet (needle rocks, canyons, volcanoes, dunes come in later phases).
  Missing art (palms, cacti, dead trees, vines, reeds) uses the closest existing trees for now.
* Terrain version **Build 9** (New world) turns biomes off and gives exactly the build 9 world.

## New in build 9
* **Diving and under water**: while swimming, **C** (or Q) dives, **Space** swims up, swimming forward under water
  follows where you look, and you drift up slowly when idle. Under water the view turns blue-green with distance,
  darker the deeper you are; looking up you see the sky through a rippling window, and the surface mirrors the deep
  water around it.
* **River mouths**: the river widens into an estuary with low, gentle banks before it meets the sea or a lake; the
  channel continues into the sea bed with the same shape. Banks no longer turn into cliffs where they bulge (the
  saw-tooth walls), and the ribs running down valley sides beside rivers are gone.
* **Water by digs and springs**: no more walls of water around a hole dug in a river bed or bank, or around springs
  placed in a river; dug ground below the river fills and shows one surface.
* **Natural world types** (New world → *Natural · …*): eleven new types whose land is grown cell by cell like in
  Azgaar's generator (own code): ragged coasts, bays, peninsulas, inner seas, island chains. Your existing types
  are unchanged. Scripts are editable (docs/WORLD_CREATION.md).
* **Small worlds**: mountains and hills keep close to their real size instead of shrinking into smooth blobs.
* **Seas**: 12 waves plus a long swell from another direction, sharper storm crests with whitecaps where they
  break, turquoise light through thin crests against the sun, finer chop.
* **Going back**: New world → Terrain version → *Build 8* generates terrain and world scaling exactly as in build 8.

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
* **Landform templates** (like Azgaar's heightmap templates, Thornwake's own version): five new world types built
  from a step list — **Inland sea**, **Shattered lands**, **Rift continent**, **Twin continents**, **Great
  peninsula**. They adapt to every world size. Templates are plain JSON steps (ranges, hills, basins, troughs,
  straits, island chains, masks), so new world types can be made by editing a saved type; see docs/WORLD_CREATION.md.
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
