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

## New in build 49 — Atmosphere 2.0, first light proposal (M1)
* **Settings → Lighting and effects → Light model**: *build 47* (default, unchanged) or *Atmosphere 2.0
  (experimental)*. The new model:
  * the sun by time of day: blue hour, golden hour (warm and low), morning, high noon (white, strongest);
  * warm sun, cool shade: a blue fill in the shadows by time of day, near-neutral grey under overcast;
  * the eye adapts a little: up to +½ stop inside a closed forest, up to +¼ stop at a dim golden hour or on an overcast
    day, never at night; over ~1.5 s (instant after a teleport). Your *Brightness* stays on top of it.
  Nothing else changed (glow, shafts, beams, fog, clouds are as in build 47: they come at the next pause points).
* **Benchmark → suite "atmosphere"** now takes every still twice, once with each light model, on the same frame:
  `captures/NN-scene-KK-light_model-0.jpg` and `…-1.jpg` in the results folder. Please run it once (all ten scenes,
  about 10–15 minutes) and send the zip; with your reference images that is pause point 2.
* A scene's stills can show the camera inside a tree crown (forest noon, part of the turn): known, the scene framing
  will be adjusted.

## New in build 48 — Quit from the Esc menu
* **Resume, Back to launcher and Quit game** now sit under the tabs of the Esc menu, on every tab (the Quit button
  used to be at the bottom of the World tab only). No more Alt+F4 in fullscreen.
* Nothing else changed: build 47 stays the baseline for Atmosphere 2.0.

## New in build 47 — last streaming pass (before Atmosphere 2.0)
* **Fewer hitches while moving**: tree, rock and bush collision is added in small steps that respect the frame budget
  (build 46: up to 16.6 ms in one frame), and the terrain meshes are prepared on the background threads (half of the
  main-thread time per new tile). The background workers are back at normal priority (lower priority could starve
  them on PCs with fewer cores; build 46's gain came from running fewer of them).
* Nothing visual changed. **This build is the baseline for Atmosphere 2.0**: please run **Standard** once (Tree
  shadows: simplified), and check collisions, jumping, slopes and flying as before. It also becomes the rollback point.

## New in build 46 — smoother streaming (from your build 45 benchmarks)
Thanks for the four runs. What they show:
* **Your forest drop (56 → 39 FPS) is the Tree shadows setting**: it was on *detailed* in this run (*simplified* in
  builds 43 and 44). *Settings → Lighting → Tree shadows → simplified* gives the 53–56 FPS back; detailed costs
  12–15 ms in the forest. Keep detailed only if you prefer how it looks.
* **Full-detail trees: keep 140 m.** 80 m is much slower inside a forest (the lighter models up close cost more), and
  220 m costs 1–4 FPS.
* **Flight stutter**: the remaining hitches were the game's background workers (one per CPU thread, plus the tree
  field's) taking the CPU away from the main thread mid-frame. They now run at lower priority and fewer at once;
  trees' and rocks' collision is added in small batches. Collision itself is unchanged from build 45.
* Fixed a crash that could happen when quitting.
* Please run **Standard** once more with *Tree shadows: simplified* (and **Quick** if you like): the flight's 1 % low
  is the number to watch (build 43: 26, build 45: 25 FPS).

## New in build 45 — collisions fixed
Thanks for catching this. Build 44's faster collision was wrong: rocks pushed you back, you could walk up slopes you
could not climb before, you jittered and could not jump.
* **Collisions are build 43's again** (ground, trunks, rocks, logs, bushes): rocks stop you, steep slopes stay
  unclimbable. The only thing kept from build 44 is *when* collision is built (after a tile appears, in small pieces),
  so the streaming stutter should stay lower than in build 43.
* **Jitter / no jumping:** found a second cause, older than build 44. At cliff steps the ground you walk on is up to a
  few metres above the height the game used when placing you (teleport, map, loading, the "don't fall through the
  world" safety net). You ended up *under* the visible ground, held there by the safety net: jittering, not on the
  floor, so no jumping; and the net could lift you up cliff faces. Now you are always placed on the ground you can see.
* **A collision test now runs before every build** (standing still, jumping, running into trees and boulders, steep
  slopes, cliff steps), so this does not slip through again.
* Please try: walking and sprinting into rocks and trees, jumping, steep slopes, teleporting next to cliffs, and tell
  me if anything still pushes you around. The **Standard** benchmark again would show whether the flight stutter
  stayed down.

## New in build 44 — less stutter while moving
Your build 43 benchmark: forest 38 → 56 FPS, lake 35 → 52. What was left: stutters while moving (flight 1 % low
26 FPS). The slow frames were new terrain tiles being finished on the main thread, mostly building the ground's
collision (a triangle shape, ~20 ms per tile).
* **Ground collision is now a heightmap** (the tiles are a 1 m grid anyway): almost free to build. The walkable
  ground is the same.
* **Collision is built just after a tile appears**, nearest first, a slice per frame (you can't walk on a tile before
  its collision exists, so nothing changes in play). Trunks, rocks and bushes add their shapes straight to the physics
  engine instead of one node each.
* **Benchmark:** the "game" column is now the game's own code per frame (the old "script" column was the whole
  frame), and streaming is split into meshes, collision (ground / objects), objects, adding to the scene and the tree
  field. Please run **Standard** again and, if you have time, **Tree detail A/B**.

## New in build 43 — cheaper near trees and shadows (from your benchmark)
Your build 42 benchmark showed the far trees are solved (impostors 1.7 ms); what is left is the detailed trees near
you (~11 ms) and the shadows (~12 ms).
* **Tree shadows: simplified** (new default, *Settings → Lighting → Tree shadows*): near trees cast their shadow from
  the lighter model of the same tree; you still see the detailed one. *detailed* = build 42.
* **Full-detail trees** (*View distance*, default 140 m; build 42 used 220 m): how far trees use their most detailed
  model; beyond, the lighter model of the same tree.
* **Benchmark:** "without X" now removes things from the shadows too, and new lines measure each kind's shadows, the
  terrain's shadows, the soft shadow filter and the shadow distance. New test **Tree detail A/B**. The warm-up no
  longer always waits 30 s. Please run **Standard** again (same settings as last time) and **Tree detail A/B**.

## New in build 42 — trees: impostors and the tree field; benchmark; Display tab
* **Dense forests for much less.** Beyond the detailed 3D trees, every tree is now drawn as an *impostor*: a picture of
  its own 3D model, baked when the game starts from 8 sides and from above, lit and swaying like the 3D tree. It keeps
  its exact place, species, size and rotation. 3D trees, impostors and the far tree cards hand over to each other with
  a soft dithered blend on circles around you: no square rings popping in when you fly. Trees are now drawn in their
  own cells (bigger batches, far fewer draw calls) instead of on the terrain tiles.
  * *Settings → View distance → Tree impostors* (default ~3.2 km). **off = build 41's trees exactly.**
  * Try *Detailed trees* ~800 m with *Tree impostors* ~3.2 km (or farther), and compare with build 41's 3.2 km 3D trees.
* **Less stutter while moving:** new terrain tiles hand their objects to the renderer in one go (the 100–400 ms spikes).
* **Benchmark** (menu → *Benchmark*): fixed test scenes in your 32 km test world, time and weather frozen, V-Sync off.
  It measures frame times, GPU and CPU time, stutters, and what each kind of object and effect costs, then opens the
  results folder (results.json, summary.txt, frame-time CSVs). *Tree range A/B* compares detailed trees 800 m to 6.4 km.
  For your volunteers: `docs/BENCHMARK.md`. Please send me the whole results folder.
* **Display tab** (menu → *Display*): windowed, borderless fullscreen or exclusive fullscreen; monitor; window sizes for
  16:9, 16:10, 21:9, 32:9 (and 4:3) that fit your screen, including its own size (1920 × 1080); UI scale. A change asks
  to be kept and reverts after 15 s. Saved. The first start opens borderless fullscreen at your screen's resolution.
  V-Sync, frame limit and render scale moved here. Ultrawide screens get a wider view with the HUD in the corners.
* Debug dumps of custom worlds replay correctly now.

## New in build 41 — Godot 4.7.2
* **The game now runs on Godot 4.7.2** (was 4.5.1). Nothing in the game changed on purpose: same worlds, same
  settings, and the picture was kept the same (fog blending as in 4.5). Please play as usual and tell me if
  anything looks or feels different: the sky and fog, the water reflections, the glow around the sun, the FPS in the
  forest tests, loading, stutters.
* If build 41 does not start or misbehaves, **build 40 is the way back** (the code is kept on the branch
  `godot-4.5.1`). One thing seen here: under Wine on Linux build 41 crashes at start unless DirectInput is switched
  off (`WINEDLLOVERRIDES="dinput8=d"`); that is a Wine problem with Godot 4.7's game-controller support, real
  Windows is not affected as far as I know. If it crashes on your Windows too, tell me straight away.

## New in build 40 — storm skies, biome skies that show
* **Storms** (simulated weather): no more flat grey lid. A low, heavy deck with dark slate cores and paler patches,
  rolls lying across the wind and a lumpy underside; dark ragged scud racing underneath with the wind; lighter
  where the storm ends at the horizon; thin parts glowing on the sun's side. Lightning flashes light it up.
* **Biome skies** (*Sky → Biome skies*): now clearly visible — dusty warm light over deserts, ashen red-grey haze by
  volcanoes, humid green-grey jungles, gloomy purple-grey blighted swamps, crisp blue in the cold north. The light
  and haze are tinted too. *off* keeps the realistic sky.
* **Audio tab** in the menu: Master volume, Weather sounds (all), Rain, Rain tone, Wind, Thunder. The rain is softer
  now (*Rain tone* 0.35; 1 = the crisper rain of build 39).

## New in build 39 — no more sun blob, clouds on the mountains, foam net
* **Sun blob fixed.** With high *God ray contrast* and *Sun shafts* the hazy sky around a low sun turned into a white
  disc covering half the screen. Shafts now come only from the sun itself, and the contrast setting brightens the fog
  much less. Your settings can stay as they are.
* **Clouds v2** (*Settings → Sky → Cloud quality → volumetric v2*, experimental): the same clouds, but also drawn in
  front of the land. They come down onto the mountains, fill the valleys below a summit, and you can walk or fly into
  them (grey-white fog inside). *volumetric* keeps build 38's clouds. Please tell me how it runs — it costs more when
  you are in or near the clouds. With TAA or FSR 2 the cloud grain smooths out over a few frames.
* **Foam net** (*Lighting and effects → Foam style → net*): foam as a white net around clear holes, solid on fresh
  breakers, breaking into a patchy net as it thins (the look you liked). *lace* is build 38's foam.

## New in build 38 — raindrop splashes, wind that follows the land
* **Splashes** (simulated weather): raindrops now bounce off water — little crowns with droplets flying up, as many
  as the rain is strong, fewer on rough water.
* **The wind is local now.** It still comes from the weather systems, but the land shapes it: stronger on ridges and
  hilltops, calm in the lee of mountains and in hollows, running along valleys, sea breezes by day and land breezes
  at night on coasts. Grass, plants and trees use the wind **where they stand** — walk from a sheltered valley up to
  a ridge and watch the grass go from gentle sway to streaming. The HUD wind is the wind where you are.
* **Lightning is the same for everyone**: strikes come from the storm cells in a fixed, repeatable way, so in
  multiplayer every player will see the same bolt (and hear it after their own distance).
* *Weather model → classic* still gives build 35's weather and one wind everywhere.

## New in build 37 — land to the horizon
* **Horizon land** (*Settings → View distance*, default ~250 km; off = build 36): beyond the streamed terrain a
  coarse far land carries on — the world's own mountains, valleys, lakes and coast, in the biome colours with rock
  on steep slopes and snow where it is cold — following the Earth's curve and fading into the haze. From a summit
  you no longer see the square edge where the terrain stopped. It is rebuilt in the background only after you
  travel ~8 km, so it costs almost nothing.

## New in build 36 — simulated weather, lightning and thunder, wind in the plants
*Settings → Simulation → Weather model*: **simulated** (new, default) or **classic (build 35)** — the classic model
keeps build 35's weather, wind, sway and rain exactly.
* **Weather across the whole world.** Rain bands and thunderstorm cells drift across the map with the wind; each
  storm is born, peaks and dies within ~3 game hours, more often where it is warm and humid and in the afternoon.
  The local climate decides how much falls (deserts rarely, rainforests often) and whether it is snow.
* **You can see weather coming.** Clouds are thick and dark where the weather really is (not one grey lid over
  everything); distant showers and storms hang **grey rain curtains** under them (*Distant rain* setting); with
  volumetric clouds thunderstorms grow towers; the painted storm towers on the horizon stand over the real storm.
* **Lightning and thunder.** Strikes come from the storm cells: branching bolts with re-strikes, the land and clouds
  flashing toward them. The **thunder arrives after distance ÷ 343 m/s** from the bolt's direction — a crack close
  by, a long low roll far away (built from the bolt's own length and the land's echoes). **L** (dev) strikes ahead of
  you — count the seconds. *Weather sound* sets thunder, rain and wind volume.
* **Wind.** It follows the weather systems and gusts out of storms; the **sea builds with the wind** (Sea state now
  adds a background swell) and storm waves surge further up beaches. *Cloud speed* no longer speeds up the wind.
* **Plants and trees.** Gusts sweep across meadows as visible **waves of grass** (paler where the blades bend, also on
  the ground beyond the grass), plants lean more in strong wind and swing slowly — no more fast jiggle. Tree crowns
  sway as a whole.
* **Rain.** Denser (Rain strength now scales up to 24 000 drops), coming in sheets with the gusts, softer streaks
  near the camera, and **raindrops ringing on water**.

## New in build 35 — less ghosting, experimental 4 water, ground parallax
* **Ghosting** on storm waves and behind the character comes from TAA (it blends past frames; the water has no
  motion data) and from the volumetric fog's own blending. New choices: *Anti-aliasing → SMAA* (sharp, no ghosting —
  try this first) and *FSR 2* (temporal like TAA but sharper; also upscales with Render scale below 1), and *Fog
  smoothing over time* (lower = no fog trails, grainier fog; 0.9 = build 34).
* **Water → experimental 4**: 3A, but rough and stormy water reflects less — the mirror breaks up and dims and the
  water's own colour carries the storm. Calm water is the same as 3A.
* **Ground parallax** (*Lighting and effects*, Noor ground textures, default 1; 0 = build 34): within ~30 m the
  ground textures get depth — dark gaps sink a few centimetres and shift as you move. Best judged while walking.

## New in build 34 — mirrored lakes, fuller alpine slopes, varied ground
* **Lakes mirror the shore and mountains.** Three things hid them: reflections reached only ~280 m, hits far away
  were faded out, and the engine's own plain-sky reflection was painted on top of them. Now ~16 km (high) / ~2.4 km
  (medium), following the smoothed surface, so wind ripples break the mirror up instead of hiding it
  (experimental 2, 3 and 3A).
* **Alpine** has meadows up to ~−6 °C (was −3 °C), trees to ~−2.5 °C (was −1 °C), and denser alpine forest and
  grass.
* **Ground variety** (*Lighting and effects*, Noor ground textures, default 1; 0 = build 33): bare patches where
  plants thin out, in the biome's character — soil, gravel in cold mountains, sand where dry, moss where wet — and
  warm/cool tint patches in the grass.
* **Ground relief** (same place, default 1; 0 = build 33): small bumps from the textures up close (grass, soil,
  forest floor, rock catch the light), each ground its own roughness, darker crevices.

## New in build 33 — test sliders for view range, plant shadows, god rays
Everything new is off or at build 32's value by default; turn it up to test and benchmark.
* **Terrain detail range** (*Settings → View distance*, ×1–×4): every terrain detail ring reaches farther, so the
  full-detail ground (and with it grass, 3D trees and the coarser rings) moves out. ×1 = build 32. ×2 builds about
  2.5× as many tiles; grass and plants are built over the whole nearest ring too (only drawn within the grass
  distance), so expect more memory and longer loading as well as frame time.
* **Terrain mesh detail** (normal / high / ultra): ground triangles beyond the nearest ring at 4 m (build 32), 2 m or
  1 m. Ultra at range ×2 is heavy (here: 19 million terrain triangles, 0.4 s per tile build, against 0.9 million).
* **Wider view sliders**: terrain to ~80 km, detailed trees to ~6.4 km, tree cards to ~48 km, grass to 400 m,
  rocks/bushes/logs ×4.
* **Grass and plant shadows** (*Lighting and effects*, off by default): grass, flowers, reeds and shore plants
  cast shadows.
* **God ray contrast** (*Lighting and effects*, 0 = build 32): sun shafts and the light beams under clouds stand out
  more — brighter rays through gaps, darker lanes behind clouds, ridges and trees — without making the sun bigger.
  With volumetric fog on it also strengthens the shafts through trees and fog. **Sun shafts** now go up to 3 and
  **Light beams under clouds** up to 4.

**Please benchmark**: FPS and stutter while flying, for Terrain detail range ×1/×2/×3/×4, each with mesh
normal/high/ultra; then plant shadows on/off; then god ray contrast 0/0.5/1 at sunrise with broken cloud.

## New in build 32 — your and Noor's notes
* **Water experimental 3A** (*Settings → Water*, Noor's brief): experimental 3's waves with foam born on breaking crests
  that drifts away with the wind and breaks up, fading — no more paint blobs or streaks swinging in place; foam with
  bubbles and gaps inside, older foam see-through; glassier, deeper water; more detail far out. Experimental 3 is
  unchanged for comparison.
* **No more sun blob** with the natural look: only the sun and real highlights glow.
* **Alpine slopes** have trees and grass up to the cold line (generic ones for now).
* **Digging the seabed** no longer lays a flat calm strip over the waves.
* **Wide rivers** get wind chop and whitecaps in storms (experimental 3/3A).
* Debug dumps now include your own world type, so I can rebuild your exact world.

## New in build 31 — water: under the surface, calmer far sea
* **Underwater light** (dive with C): sunlight shafts fanning down from the moving surface, brightest looking toward
  the sun; a dancing net of light on the sea and lake bed; the sun's bright spot when you look up. With water
  experimental 3 the shafts come from the real waves above you. *Lighting and effects → Underwater light*: full,
  simple (for the office PC) or off.
* **Experimental 3**: no shimmer or checker patterns on the far sea; wind and sea-state changes fade in smoothly;
  windy lakes get proper chop.
* Please tell me how experimental 3 and the underwater light run on both PCs (FPS with and without), and whether
  experimental 3 should become the default water.

## New in build 30 — colour and lighting, choppy sea
* **Less washed out** (*Settings → Lighting and effects → Look*: natural, the new default): richer colour and more
  contrast, real shade, glow only from genuinely bright things (no light spilling over the whole picture), less grey
  haze. *Classic* is exactly the build 29 look, to compare.
* **Water experimental 3** (*Settings → Water*): the sea simulated on the graphics card, like Blender's ocean
  modifier — choppy, crisp crests that pinch and can fold, whitecaps born where they fold and trailing behind them for
  a few seconds. Shore waves, rivers and the lighting are experimental 2's. Try it calm, fresh and in a storm (*Sky →
  Sea state*). On *Run (old or office PCs)* it falls back to experimental 2.

## New in build 29 — plant fixes from your testing
* **Underwater plants no longer vanish** when you turn the camera (a whole bed was switched off at once).
* **Fuller tufts** from above (three leaning planes instead of two flat ones).
* **Bogs and swamps**: sedge and rush meadows replace the old dark-green shrub models up close.

## New in build 28 — plants as populations (Noor's plan, your sprite packs)
* **Grass grows in patches**: lush stretches and sparse ones, clumps of all sizes with gaps between, and each patch
  has its own character — short grass with the odd tall tuft, thick lush grass, a stand of tall grass, grass with wild
  wheat (some green, some golden), wet meadow near water. The ground under dense grass is richer, so the pattern
  continues to the horizon.
* **Water's edge**: reed beds, cattail stands and rushes with white flowers in clumps along ponds, lakes and slow
  rivers, standing into the shallows.
* **On and under the water**: lily pads, water lilies and duckweed on ponds and lakes; underwater plants on lake and
  river beds; seagrass meadows, kelp and seaweed in the sea (dive to see them).
* *Settings → Detail → Grass and plants*: switch back to the classic tufts to compare.

## New in build 27 — rolling mist
* **Ground mist with shape** (Ground fog: valleys + mist patches): mist now lies in banks with clear air between them,
  each bank with its own depth and a lumpy top; inside, the mist rolls and churns slowly and drifts with the wind. The
  even grey layer underneath is much weaker.
* **Mist after teleporting** no longer stays at the old height (it was buried in the ground or hanging in the air).

## New in build 26 — fixes from your debug dumps
* **River "circles"** gone: the foam and ripples on rivers were scrolled in a way that slowly wound them into frozen
  arcs the longer the game ran (that is why they barely moved). Foam now drifts with the current in patches, and the
  standing waves in rapids stay short crests across the flow. Rocks in the current: softer wakes that fade out instead
  of white wedges; no more white strip along the water's edge on shallow banks.
* **Fog and mist**: much gentler. From a mountain, valleys below no longer turn into one flat grey colour; mist is
  thinner overall. Clouds stay at least ~1 km above you, so on high ground they no longer press down as a grey lid.
* **Bayou, marsh and fen** (new terrain, revision 14): the long straight parallel channels are gone (two causes: the
  wetland noise raced through its pattern far from the world's centre, and small eroded gullies decided what became
  water). Pools are now winding, connected and a little deeper at the edge. Wetlands hold a bit less water than build 25.
  The build 25 terrain stays reproducible from its snapshot (docs/SNAPSHOTS.md).
* **Sea and river seams** at river mouths gone (river water below sea level is now drawn as sea).

## New in build 25 — weather you can see coming (Noor's cloud list)
* **Rain fronts on the horizon**: when rain is on its way, a dark storm bank with billowing tops appears upwind up to
  ~70 km away, with a grey wall of rain hanging under it, and it closes in as the rain approaches. Strong fronts tower
  higher (thunderstorms). Clouds nearer than it stay in front of it. Works with layered and volumetric clouds.
* **Overcast changes the mood**: under a full deck the sunlight weakens and flattens and the air gets greyer; the deck
  itself has thick and thin patches and a lumpy base (volumetric).
* **Mountains catch clouds** (volumetric): more cloud over high, windward ground, and the cloud base is now a fixed
  height above the sea — on a mountain you are closer to the clouds. (Clouds wrapping around slopes in front of you need
  a different technique; later.)
* **More weight and colour** (volumetric): darker cores and undersides, cool slate shadows, warmer sunlight, warm light
  bounced from the ground on the bases, thicker haze where clouds meet the horizon.

## New in build 24 — fixes from your build 23 screenshots, debug dump
* **F9 debug dump**: writes a text file (and a screenshot) to the game's user folder → `debug/`, and opens the folder.
  It has everything I need to rebuild exactly that place, time, weather and settings — send me the .txt and the .png.
* **Black smudges in fog** (dawn, desert) fixed: the fog was not lit by the sky in shadow. Cloud shadows also fade at
  sunrise and sunset and under overcast skies.
* **Sea patterns** fixed (experimental 2): the triangle-edged patches on calm water and the odd wall in storms came
  from the waves near coasts turning the wrong way round in places. The sea is continuous again.

## New in build 23 — real 3D clouds (Settings → Sky → Cloud quality: volumetric)
* **Volumetric clouds rebuilt**: real billowing 3D shapes (lumps on lumps, no rows of balls), towers on warm afternoons
  that grow up to ~7 km, flat bases, rounded tops.
* **Colour from the light**: blue-grey shadows from the sky above, bright sunlit sides, glowing edges and silver
  linings toward the sun, grey (not black) thick cores; golden, pink and violet at sunset and dusk; distant clouds fade
  into the sky colour.
* **Wind changes them**: in calm air crisp cauliflower edges; in wind softer, smudged edges drawn out along the wind,
  and tall clouds lean downwind.
* Still experimental: it is heavy on the GPU — please tell me the FPS with it on and off. "Layered" stays the default.

## New in build 22 — clouds that belong to the weather and the land
* **Towering cumulus on warm afternoons**: around the horizon, clouds bubble up into towers over a few minutes, stand,
  then dissolve while new ones grow elsewhere — flat grey bases, bright cauliflower tops, silver edges toward the sun.
  Overhead the clouds get darker flat bases. Needs warmth and moisture; none in the cold, at night or in rain.
* **Distant thunderstorms**: when a storm front is a few hours away (on the side the wind comes from) or has just
  passed (downwind), or on hot humid afternoons, huge storm cells stand on the horizon 30–100 km away: anvil tops
  spreading downwind, grey rain shafts hanging under them, and at night **lightning flickering inside the clouds**,
  now and then a bolt to the ground.
* **Lenticular clouds**: smooth lens-shaped stacks of plates standing still over the highest peak nearby when the
  wind is strong and the air dry (not every day).
* **Mackerel sky**: rows of small high cloudlets in patches when rain is on its way.
* **Rain**: the grey deck in steady rain now has slow darker and paler parts instead of a flat colour.
* Calm water reflects the towers and storms on the horizon (experimental 2).

## New in build 21 — rivers that flow, a richer sky (water style experimental 2)
* **Current across the river**: slow and lazy along the shallow edges, fastest down the deep middle.
* **River surface**: ripples moving downstream at different speeds; in fast, shallow water **standing waves** — crests
  across the river that stay in place while the water rushes through them, with white tops; small churning ripples
  and bits of foam along moving edges; **wide rivers get wind waves**, choppier where the wind blows against the
  current.
* **Wakes**: wade or swim and you leave a wake — a V behind you when you move, a wake trailing downstream when you
  stand in a current; it drifts with the water instead of spreading in perfect circles.
* **Rocks in rivers** push the water up in front of them and leave a V-shaped wake with eddies and foam behind them
  (in fast water).
* **River mouths**: the river's current carries on into the lake or sea, fanning out and fading; where waves run into
  it the water gets choppy and foamy (much more in rough seas).
* **Water reflects the real sky** (experimental 2): the clouds overhead appear in lakes and the sea (sharp on calm
  water, smeared when rough) instead of a plain colour gradient.
* **Twilight**: a red-orange glow along the horizon at sunset, the pink band opposite the sun with Earth's blue
  shadow rising under it, the deep blue hour, and clouds lit gold and pink from below after the sun has set — in the
  sky and in the water.
* **Rainbows** after rain when the sun breaks through (with the faint second bow), **halos and sun dogs** around the
  sun in thin high cloud.
* **Night**: **auroras** in the far north and south on clear dark nights (some nights strong, some quiet), and the
  occasional **shooting star**.
* More sky ideas: SKY_IDEAS.txt.

## New in build 20 — rivers meeting rivers
* **No more walls of water where side rivers join a big river**: a side river crossing the big river's valley floor now
  flows at that floor and drops down to it, instead of standing 2–3 m above the dry ground beside it (terrain revision
  13; your desert spot at 45282, 135940 in *Mountain kingdoms* seed 42). Only such confluences change; the rest of the
  terrain is identical.
* Rain water on a river bank no longer makes clear patches with straight edges on the river.
* Noor's river ideas and the plan: RIVERS_PLAN.txt.

## New in build 19 — water fixes from your screenshots
* **Digging next to a river**: the water filling the hole now has the river's colour (biome tint) and blends into the
  river softly instead of showing square, stepped edges.
* **Water at night** is dark and reflects the night sky (and the moon) instead of glowing as if it were day — in all
  three water styles. Sunsets warm the reflections; storms grey them.
* **Windy lakes**: the hard white reflection streaks and the rings / fine lines around you (shimmering moiré on low
  views over water) are gone; the ripples still sparkle in the sun.
* **Fast rivers** foam in patches that drift with the current, not one white sheet.

## New in build 18 — water as a system, Noor's ground textures (switchable)
* **Experimental 2** (Noor's review): foam follows the weather — calm: no whitecaps; breezy: a few short-lived caps;
  rough: many; storm: dense. Whitecaps come in streaks and fragments that form and fade, not big white sheets. Broader,
  longer swells. Near coasts the waves turn toward the shore and run in as sets. Sea state now says calm / breezy /
  rough / storm.
* **Esc → Settings → Lighting and effects → Ground textures: current / Noor.** Current (default) is the ground you
  know, unchanged. Noor puts Noor's pixel-art textures on the ground: grass, sand, snow and soil keep their biome colours
  with the texture's pattern; forest floor (leaf litter), mud (wet mud near water and in rain), gravel, moss, peat,
  scree on mountain slopes, rocky ground and river/sea beds use Noor's art up close and fade to the usual colours in the
  distance. Cliffs keep their rock. Switch any time to compare.
* **Rocky patches on coasts** (the "blocky wet" ground): their edges are ragged now, following the stones, instead of
  straight triangle edges — in both ground styles.

## New in build 17 — storms from calm to huge, waves that differ from place to place, rocks you stand on
* **Experimental 2 sea, rebuilt** (classic and experimental unchanged; the build 16 version is kept as a snapshot):
  * **Sea state is smooth from calm to huge**: calm is a long, soft swell gently lifting the whole sea; moderate seas
    are lively; sea state 1 is ~14 m storm seas. Raising it — or a storm rolling in — grows the waves gradually,
    nothing jumps.
  * **Crisper crests** on the big waves.
  * **Directions depend on where you are**: the wind waves follow a local wind that turns from region to region, each
    lake has its own, and swells from distant storms come from their own directions across the oceans.
  * **Shores**: waves arrive at varied angles instead of all perfectly parallel to the beach, and break raggedly.
  * The sea no longer jumps when the world re-centres as you travel.
* **Rocks**: you now stand on the stone itself (you used to float above flat rocks).
* Try: *Mountain kingdoms* (seed 42), open sea at −66000, 115600: Sea state 0 → 0.5 → 1 (fly a few metres up at
  1, the waves are taller than the player); fly high to see different wave directions across the sea.

## New in build 16 — "experimental 2" water (Sea of Thieves references)
* **Esc → Settings → Lighting and effects → Water** now has three choices: classic (default, unchanged), experimental
  (build 15, unchanged) and **experimental 2**. All three switch instantly on the spot, so you can compare.
* Experimental 2 = experimental (shores, lakes, rivers) with a livelier ocean:
  * **32 sharp waves** from a real ocean wave spectrum, crossing in mixed directions plus a swell from another heading;
    **wave sets** (groups of higher waves) drift through with the wind.
  * **Storms are wild, not just big**: at sea state 1 the seas are ~8 m and stylised steep, directions go confused,
    crests break white, foam spills down the backs, foam lanes run along the wind, old-foam fields drift, the sea goes
    dark grey-teal and reflections dull.
  * **Look**: deep blue-teal, bright turquoise glow through thin wave tops, dark troughs and lit crests (waves read as
    masses), dense sun sparkle.
  * **Foam with detail**: dense where fresh, breaking into lace threads around bubble cells and speckle as it thins,
    a milky layer under the surface, streaked along the wind or current, shaded (not flat white).
  * **Waves turn toward the land**: from ~19 m deep the open-sea waves hand over to shore waves that face the beach
    (answer to your question in WATER_EXPERIMENT.txt).
* Try: *Mountain kingdoms* (seed 42): open sea at −66000, 115600 with Sea state 1 (fly low), beach at −60420, 116300
  with Sea state 0.5.

## New in build 15 — experimental water (switchable)
* **Esc → Settings → Lighting and effects → Water: classic / experimental.** Classic (the default) is the water of
  build 14, unchanged. Experimental is a new water look to compare; switch any time, it changes instantly.
* Experimental water:
  * **Oceans**: rolling swells with wind waves on top; near land the waves turn to face the shore, bunch up and
    steepen in the shallows and break into foam; open coasts get real surf, bays stay calmer, steep rocky shores
    churn. Storms: big steep seas, whitecaps, milky crests, grey rough reflections.
  * **Lakes**: wind waves scaled to the lake's size — small lakes stay calm, huge lakes get proper waves.
  * **Rivers**: ripples carried downstream showing the flow, glassy pools, whitewater in fast shallow stretches.
  * Smooth at every distance (no faceted waves), soft moving foam, sun glints; biome water colours kept.
* Try: *Mountain kingdoms* preset (seed 42), beach at −60420, 116300 with Settings → Sky → Sea state 0.5, then 0.9.
  Details: WATER_EXPERIMENT.txt.

## New in build 14 — dunes, mesas, needle rocks
* **Deserts have real dunes**: long ridges with gentle windward slopes and steep slip faces, all lined up with the
  world's prevailing wind, in dune fields and flatter sand sheets.
* **Badlands have mesas**: flat-topped table-lands 40–110 m high with stepped, cliff-sided tiers; rivers cut canyons
  through them. A few stone needles stand among them.
* **Needle Rocks** (new rare biome, in the spirit of Thousand Needles): a forest of stone needles 12–55 m tall rising
  from dry ground, in patches inside badlands, deserts and savannas.
* Swamp pools fit hilly edges better (no cliffs where a swamp only just reaches); no seams at lake shores.
* Terrain version "current" is the new terrain; build 13 worlds look the same except in deserts, badlands and
  needle-rock areas (and a little at swamp edges).

## New in build 13 — real swamp water, four swamp types, light beams (round 8)
* **Real water in swamps**: pools are now real water you can wade and swim in (0.5–1.6 m deep), carved into the
  ground with a flat surface, tinted to the biome, with dark peat beds (sunken leaves and twigs) and muddy margins.
  Reeds line the pools. Swamp ground is flatter, as real wetlands are.
* **Four swamp types**:
  * **Swamp** — the wooded swamp you know, now with real pools and lily pads.
  * **Marsh** — open and grey like the Dead Marshes: many dark pools, pale grass tussocks, reeds, dead snags, mist.
  * **Fen** — northern bog: dark water channels winding through green sedge and bog shrubs, lily pads, scattered
    spruce, birch and tamarack.
  * **Bayou** — Louisiana: broad brown water, bald cypress with long Spanish moss standing in it, water hyacinth
    mats, green islands.
  Blighted swamps get pools too. Biomes blend as before, so mixed areas stay.
* **Light beams under clouds** (Settings → Lighting and effects → *Light beams under clouds*, 0 = off): sunlight
  falling through gaps in broken cloud in visible shafts, even when the sun itself is behind a cloud or off screen;
  at night the moon makes fainter silver beams (strongest at full moon). Best with a low sun and partly cloudy sky.
* **Cloud cover** setting works with the weather on (it biases the simulated weather: low = mostly clear).
* Terrain version "Build 12" is kept internally (tests); the world menu's "current" is the new terrain.

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
