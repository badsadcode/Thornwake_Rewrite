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

## New in build 118 — waterfalls, gorges and canyons (terrain revisions 20 and 21), wet sheen
**Host and everyone joining need build 118** (versions must match to connect).
* **Waterfalls**: where a river drops over a step (escarpment edges, mountain fronts) the water now falls in one drop
  (up to 120 m) into a plunge pool, with rapids below and a narrow gorge cut back from the fall. About 100–260 per
  512 km world. Try *Go to - waterfall (Balanced)* and *Go to - waterfall (Mountain kingdoms)*.
* **Canyons**: a few rivers run through canyons ~60–180 m deep with stepped walls and ledges; tributaries cut side
  canyons. About one per 512 km world. Try *Go to - canyon (Balanced)*.
* **New world → Landforms** gains *Waterfalls* and *Gorges and canyons* (amount and size).
* **Wet sheen** (Settings → Simulation): wet ground glints in specks and far wet ground has a soft sheen, instead of
  one big shiny spot under a low sun; *build 117 (even)* brings the old look back.
* Your build 117 worlds keep their terrain; *Terrain version → Build 117* gives it to new worlds too.
* Known: the falls are drawn by the ordinary river water (no spray or white water yet: water shader rework); canyon
  ledges run a little evenly.

## New in build 117 — escarpments fixed from your dumps (terrain revision 19)
**Host and everyone joining need build 117** (versions must match to connect).
* **The comb is gone**: the rows of thin steps came from the mesas where badlands fade out; mesas there now just get
  lower, and only mesas over ~15 m have tiers.
* **No more fins and cracks** across plateaus (they came from the bends of the escarpment's line).
* **Less regular slopes**: straight gullies downhill at uneven spacing instead of wavy worm-like ones, no wavy bands,
  no teeth along the cliff top (the faces are at most ~75°, which the ground can draw cleanly).
* Your build 116 worlds keep their terrain; *Terrain version → Build 116* gives it to new worlds too.
* The dark upright slabs at cliff edges are rock outcrop models, not the ground (tell me if they should stay off
  cliff edges).

## New in build 116 — escarpments and plateaus (terrain revision 18), Land shape
**Host and everyone joining need build 116** (versions must match to connect).
* **Escarpments and plateaus**, the first landform from your list: long cliffs inland with a raised table-land behind
  them — a short caprock cliff over gullied slopes of fallen rock (scree), ravines of every size cutting back into the
  plateau, spurs between them, ends that ramp down (the ways up). Rivers flow off the plateaus or cut notches through
  the edge. About one per 100 × 100 km of land, at least one per world.
* **New world → Landforms → Escarpments and plateaus**: amount (off … everywhere) and size. *Off* gives exactly the
  build 115 ground. *Terrain version → Build 115* also keeps the old terrain.
* **New world → Land shape**: any world type's layout of land and sea under another type; the World type list is
  grouped (Natural, Classic, Your types).
* Try *Go to - escarpment in the badlands (Mountain kingdoms)* and *Go to - escarpment in the forest (Balanced)*.
* Worlds you made in build 115 keep their terrain (they recorded revision 17); new worlds get escarpments.

## New in build 115 — terrain revision 17: landform settings, worlds keep their terrain version
**Host and everyone joining need build 115** (versions must match to connect).
* **Nothing looks different** at the usual settings: the ground is exactly as in build 114 (proved by the tests).
* **New world → Landforms** (every world type): *Dunes*, *Mesas and buttes* and *Needle spires*, each with an amount
  (off, rare, normal, common, everywhere) and a size (0.5–2×). They show where their biomes are (sandy deserts,
  badlands, needle rocks). Try *Go to - badlands, mesas everywhere (Landforms)*: the whole badlands raised into one
  table-land, needles off.
* **Your saved worlds keep their terrain** from now on: every world records its terrain version, so a later build
  never changes it. Saves from earlier builds load as build 114's terrain. *Terrain version* has a new *Build 114* entry.
* While a world is generated, the loading screen names the stage it is on (land shape, erosion, rivers and lakes,
  climate, biomes) — the place your loading messages will go.
* Also in this build: the error line some saw in the log when rain changed strength (build 114) is gone.

## New in build 114 — strong rain fixed (your gale dumps)
**Host and everyone joining need build 114** (versions must match to connect).
* **No more arcs over the sky** in a gale: the far rain layers fade out when the rain is nearly horizontal.
* **No rainless patch** at the foot of a slope in strong wind: only roofs, rocks and logs shelter rain now, not the
  ground itself.
* **The near rain is visible with TAA**, also in a gale: the streaks are longer (like motion blur) and have a thin core
  TAA can follow — before, TAA averaged fast drops away.
* At a low *Rain strength* the far rain thins out with the near rain (it no longer takes over).

## New in build 113 — rain tuned from your notes on 112
**Host and everyone joining need build 113** (versions must match to connect).
* **Denser rain around you** in heavy rain (up to 40 000 drops).
* **Heavy rain veils the sky too**: distant mountains and land merge into the rain instead of standing out as grey shapes
  against a lighter sky; a downpour is thicker; drizzle stays almost clear. (Your dump 19:23 was a sun shower, which keeps
  the veil lighter on purpose — a little thicker now too.)
* **More refraction, less white** in the refractive streaks: they bend more of the picture behind them, with a slight
  colour split and only a thin glint.
* **Splashes** bigger in harder rain, smaller in drizzle, and more varied (mostly small, now and then a big one).
* **Mist gusts** lower (mostly within ~3 m of the ground), each sheet slightly different in colour; no more bright
  orange band in the badlands.
* **Lens-drop trails** start softly, are uneven and broken in places, dry in patches, and are thinner and see-through.

## New in build 112 — softer rain, the far rain fixed
**Host and everyone joining need build 112** (versions must match to connect).
* **Refractive rain streaks** (Simulation → Rain look → by strength, refractive — the new default): your note that the
  streaks were too sharp. Each streak is a thin, soft-edged water lens that bends what is behind it, with a bright head
  and a tail that fades out. **Your saved Rain look is still "by strength (build 110, dithered)"** — switch it to
  *refractive* to see this. *Smooth*, *dithered* and *build 104* stay as options.
* **Rain in depth fixed** (your dumps 18:49–18:50): no more curvy swirl pattern over the sky, and no visible circle where
  the far rain started — it now builds up gradually with distance.
* **Rain-mist gusts** now show in ordinary grey rain too, not only in sun showers.

## New in build 111 — Rain 2.1 (your and Noor's suggestions)
**Host and everyone joining need build 111** (versions must match to connect). All in Settings → Simulation, each with
the old look as another option.
* **Smooth rain streaks** (Rain look → by strength, smooth, default): your note that the drops looked grainy — with TAA
  they were dithered; now they are thin solid lines, soft at the edges. *by strength (build 110, dithered)* is the old.
* **Rain in depth** (layers): beyond the drops around you, finer, denser rain further out (~20–50 m), so a downpour fills
  the view. It stays put when you turn and leans with the wind.
* **Rain-mist gusts** (draft, needs Volumetric fog): in heavy rain, soft sheets of wet air blow past you with the wind,
  now and then, low, more in gusts. Watch it in motion: Weather test → Downpour, Wind 15 m/s.
* Visibility by rain strength was already right (the rain light from build 108): unchanged.

## New in build 110 — raindrops on the lens, animals take cover
**Host and everyone joining need build 110** (versions must match to connect).
* **Lens drops** (Settings → Lighting and effects → Lens drops, **off by default**): raindrops on the camera lens in the
  rain. Small beads land and slowly dry; now and then one gathers, breaks loose and runs down, leaving a thin wet trail
  that dries over a few seconds. Each drop bends the picture behind it like a little lens, with a soft colour fringe.
  More when you face into the rain, none under cover or under water. Turn it on and try Weather test → Downpour, then
  face into the wind.
* **Animals take cover** (with Trees in the rain): in heavy rain deer and other grazers walk to the nearest trees and
  stand under them; predators lie low.

## New in build 109 — the rest of Noor's Weather 2.0 notes
**Host and everyone joining need build 109** (versions must match to connect). Each is in Settings → Simulation with
*build 108* (or 107) as the other option, unless said otherwise.
* **Trees in the rain**: under a tree's crown most of the rain is caught by the leaves; the leaves drip (bigger, slower
  drops, more in gusts) and keep dripping for a few minutes after the rain until they dry. Under wet leaves the rain
  sounds softer and busier, and single drips tick after it.
* **Splashes by surface**: roofs, rocks and logs throw bright crowns; on the ground a splash is small and dull, in the
  woods fainter still.
* **Dry under cover** (with Rain under cover): the ground and grass under roofs and overhangs stay dry, no puddles.
* **Mist after rain** (draft): lingers in calm, cool air (evening, night, woods), cleared by wind and the midday sun,
  lifting off the ground as it dries.
* **Wet characters** (with Wet look): clothes and hair darken as you soak; dry off over a few minutes, faster under cover;
  swimming soaks you at once. **Footstep splashes** on wet ground and in shallow water.
* **Sun showers** (with Rain light): rain from a mostly open sky keeps the sun and brings a rainbow. Esc → Weather test →
  *Sun shower* forces one.
* **Drips from roof and ledge edges** while wet, and **lightning lights up the rain** around you.
* Weather test: *Put a test roof ahead* (a 4 × 4 m board on posts; again to remove) to try shelter anywhere.
* Try: Weather test → Downpour under a tree, then switch Rain here off and listen; the test roof in a downpour, then
  after; Moderate + Sun shower at ~16:00 facing away from the sun; a test storm at night.

## New in build 108 — rain light, shelter, wet by material
**Host and everyone joining need build 108** (versions must match to connect). All three are in Settings → Simulation,
each with *build 107* as the other option.
* **Rain light** (draft): the light changes with the rain. Drizzle already under a closed grey sky with soft light; as
  the rain grows a grey veil swallows the distance (in a downpour the near hills are soft shapes), colours and contrast
  mute.
* **Rain under cover**: no rain falls under roofs, floors and rock overhangs; in wind it still blows in under the
  windward edge; drops splash on the roof instead; the rain sounds duller while you are under cover. Test rain (Weather
  test, the rain launcher) now has its sound too with the weather off. Not yet: trees, the lean-to and the campfire.
* **Wet look** (by material): rain soaks in over time (drizzle too, slowly) and dries in sun and wind, slowly at night.
  Rock turns dark and glossy and dries first, earth and paths go darkest and stay wet longest (drying in patches), grass
  turns a richer green without shine; boulders, bark and your log buildings and thatch get wet too.
* Also: the rain costs much less CPU (~0.2 ms a frame instead of up to ~3 ms in a downpour).
* Try: Esc → Weather test → Downpour for a minute, walk into the hut or under a ledge, then switch Rain here off and
  watch the ground dry in the sun.

## New in build 107 — the rain stays put when you turn
**Host and everyone joining need build 107** (versions must match to connect).
* Your note on 106 ("the rain's direction shouldn't matter to where the camera is looking"): the rain turned with the
  camera — its emitter hung on the camera and took the camera's rotation, so the drops always leaned the same way on
  screen. Now the rain is fixed in the world: turn around in a windy downpour and it leans the other way on screen,
  always blowing with the wind. The wind swooshes had the same fault and are fixed too.
* Try it: Esc → Weather test → Downpour, Wind on 15 m/s, then turn the camera in a full circle.

## New in build 106 — each raindrop along its own path
**Host and everyone joining need build 106** (versions must match to connect).
* Your note on 105 ("the lines are always oriented the same way"): every rain streak took one shared angle from the wind
  of that moment, so all lines were parallel and turned together with each gust, not with their own drops. Now each
  streak lies along its own drop's movement: the same overall lean, a little variety between drops, and when the wind
  turns the rain swings over within a second or two. Try it in Esc → Weather test: Downpour, Wind on, then move
  *Blowing from*.

## New in build 105 — wind wisps with strands, a weather test tab, rain by strength
**Host and everyone joining need build 105** (versions must match to connect).
* **Wind wisps** (Lighting and effects → Wind wisps, with Living air): no more straight scratch lines. **Swooshes with
  strands** (default): short soft strokes that draw themselves along the wind and fade, some ending in a curl, with a
  faint haze and fine bright strands braided through it (your reference). *Swooshes* = the same without strands;
  *build 104* = the old trails.
* **Esc → Weather test** (nothing is saved; switch a test off to get the weather back; works with the weather off too):
  * Rain here: strength 0–1, or the buttons Drizzle / Light / Moderate / Heavy / Downpour.
  * Wind: speed (0–30 m/s) and where it blows from.
  * Lightning: a strike ahead at 150 m … 20 km, or a test storm striking all around you every few seconds (thunder
    comes at the speed of sound: seconds × 343 m).
* **Rain look → by strength** (Settings → Simulation, default): the drops lean with the wind (sideways rain in a gale);
  drizzle is fine, short and slow, a downpour long, heavy and fast; from moderate rain on, drops splash on the ground,
  rocks, logs and roofs around you. With TAA the old rain was nearly invisible — now it shows. *Build 104* = the old rain.
* Worth trying: Weather test → Downpour, then Wind 15 m/s and turn around; then Drizzle; then the test storm at dusk.
* Launcher "Go to - rain test spot (Weather test), 13-00.cmd": the grassland in a 10 m/s wind with a downpour (change
  it in Esc → Weather test).

## New in build 104 — cloud bottoms with shape (draft)
**Host and everyone joining need build 104** (versions must match to connect).
* **Sky → Cloud bases → shaped (draft)** — your note that the bottoms of the clouds look flat. Off by default, so the sky
  is as in build 103 until you switch it on. Shaped: the undersides come in lumps and, in a breeze, rolls along the wind;
  each cloud's base rises toward its thin edges (rounded, lowest under the core) with lobes hanging from it; under thick
  cores the base is darker, under thin edges lighter. The blue-grey colour stays. Costs well under 1 ms.
* Best seen looking up under broken cumulus (cloud cover around 0.5–0.6), at midday and in the late afternoon. Worth
  also flying just under the cloud layer and checking the horizon for any line.

## New in build 103 — no cream-white glare round the sun
**Host and everyone joining need build 103** (versions must match to connect).
* Looking up toward the sun, half the sky turned cream-white even in clear, dry air (your dump 17:39 — not a cloud). That
  was the rays in open air adding a broad glow at full strength whatever the haze. **Sky → Glow round the sun → by the
  haze** (on): in clear air a blue sky and a tight halo; the broad glow only in hazy, damp or dusty air (mornings, after
  rain). *Build 102* is the other option.

## New in build 102 — the sun behind clouds, cloud quality levels, a clean baseline
**Host and everyone joining need build 102** (versions must match to connect).
* **The sun dims behind clouds** (Sky → Sun behind clouds, on): when a cloud passes in front of the sun, the sunlight
  on you and around you drops, shadows fade and the glare in the air goes. Build 101's way is the other option.
* **Sky → Cloud quality level**: High (as before), Medium, Low — 3–13 FPS more where clouds fill the view.
* **Your settings were reset to a clean baseline** (backup: rewrite_settings.before-baseline-20261025.cfg in
  %APPDATA%/Thornwake): Wind strength 1 (it was 3.9 — a 20 m/s gale full of dust), cloud base above the land (above the
  sea put you inside the clouds in the mountains), the Natural look, and only the drafts I recommend on.
* Also in, off by default: Cloud softness, Cloud shading → smooth, Cloud detail → fine (small effects, D197–D199).

## New in build 101 — dust devils, embers, fireflies, midges (Living air draft)
**Host and everyone joining need build 101** (versions must match to connect).
With **Living air** on (subtle / more):
* **Dust devils** in deserts, badlands, needle rocks and savanna: on sunny days with light wind, now and then a whirling
  dust column forms 50–160 m away, drifts with the wind for 20–40 s and fades.
* **Embers** in volcanic land: orange sparks drifting up, best at night.
* **Fireflies** on calm nights in meadows, forests and swamps (not in winter), blinking slowly.
* **Midge swarms** on calm days by swamps and meadows: a small dark cloud hovering a few metres off.
* Performance: I measured the recent drafts with your settings; none costs anything noticeable (D193). If it still feels
  slower, a dump at that moment (it records the FPS) would show what it is.
* To try: **Go to - badlands, dust devil (Living air), 14-00**, **Go to - volcanic hills at night (embers), 23-00**,
  **Go to - forest at night (fireflies), summer**.

## New in build 100 — colour grading presets (draft), leaves in flurries
**Host and everyone joining need build 100** (versions must match to connect).
* **Settings → Lighting and effects → Colour grading (draft)**: Natural (as before), Cinematic, Fantasy, Dreamy, Rustic,
  Dark fantasy, Vintage, Vibrant, and **Colour grading strength** to blend any of them toward Natural. Only the colours
  change, not the lighting. Switch them live and compare.
* **Leaves (Living air) come in flurries**: about 40 % of the time, quiet otherwise, stronger in gusts — no longer
  always there. Dust unchanged.

## New in build 99 — Living air (draft), patchy cirrus, calmer moon glow
**Host and everyone joining need build 99** (versions must match to connect).
* **Settings → Lighting and effects → Living air → subtle / more (draft)**: when the wind blows (from ~4 m/s), a few
  faint curving wind trails glide past along the wind, more in gusts (Noor's reference). And each biome's own particles
  around you: dust in deserts and badlands, ash in volcanic land, blown snow in the cold, leaves in broadleaf forests in
  gusts, pollen, spores over swamps. Cheap, only near you. These are motion effects: walk around in a wind.
* **Cirrus** (Cloud layers draft) now comes in patches and wisps instead of regular parallel bands.
* **Moon glow** (your dump 13:53): after rain the moonlit haze glowed white round the moon; the moonlight boost now backs
  off in dense air.
* To try: **Go to - badlands in the wind (Living air), 16-00**, **Go to - forest in the wind (Living air), 10-00**
  (wind fixed at ~11 m/s for that run).

## New in build 98 — cloud layers (draft)
**Host and everyone joining need build 98** (versions must match to connect).
* **High clouds dark at night** (always on): the mackerel and cirrus layers stayed half lit at night (bright cut-outs on a
  black sky); now they go dark, faintly moonlit, and the mackerel is softer.
* **Settings → Sky → Cloud layers → layered (draft)**: thin high cirrus in fair weather too, thicker as a front comes;
  and a middle grey sheet that spreads over the sky from ~70 km before a front, thicker on the front's side, the sun
  dimmed behind it. Best seen in clear air (the dusty badlands haze hides them). Known: faint horizontal stripes in the
  sheet near the horizon.
* To try: **Go to - valley, a front coming (cloud layers draft), 11-00** (for that run: a front at 30 km, the cloud
  layers draft on, the cloud sea draft off and the cloud base above the land — you have it above the sea, which puts
  the clouds at 1.5 km, inside this 1,690 m valley).

## New in build 97 — overcast without the marble, darker rainy nights
**Host and everyone joining need build 97** (versions must match to connect).
* **Overcast without the "marble" pattern**: build 96's grain fix banded a thick overcast deck into contour lines (my
  mistake). Thick decks get the per-pixel offset back; broken cloud keeps the grain fix.
* **Rain no longer brightens the night**: the rain sky had a daylight floor (a lit grey deck that also lit the land) and
  the rain streaks were full white. Now a rainy night is darker than a clear one; daytime rain looks as before.

## New in build 96 — moon shafts, no cloud grain with SMAA
**Host and everyone joining need build 96** (versions must match to connect).
* **Moonlight (draft) tuned**: with *Moonlight → rays and shadows* the air now grows damp through calm nights, so the
  moon has haze to light: silver shafts between the trees when you face the moon, and layered moonlit ranges with
  shafts from high ground. The forest haze takes more moonlight than open air.
* **Cloud grain with SMAA fixed** (your dump 23:53): thin cloud no longer shows a fixed diagonal grain. New setting
  **Sky → Cloud grain without TAA**: *smoothed* (default) or *build 95 (grain)*. It costs a few FPS where clouds fill
  the view (60 → 50–57 in my captures); tell me how it feels. With TAA nothing changes.
* To try: **Go to - forest under the moon (draft), day 9 00-30** and **Go to - mountain ridges under the moon (draft)**.

## New in build 95 — ground fixes, moonlight with shadows (draft)
**Host and everyone joining need build 95** (versions must match to connect).
* **Straight stripes across the ground** (your volcanic dumps 00:13–00:14): the ground's fine noise lost precision far
  out in the world and broke along straight lines. Fixed everywhere.
* **Long stretched strips** (your dump 13:33, every 4 km across the world): the ground pattern was squeezed into one
  long strip along a line. Fixed. Side effect: the ground's patches lie in other places than before (same look).
* **Settings → Lighting and effects → Moonlight → rays and shadows (draft)**: at night the moon casts shadows like the sun
  (trees and mountains shade the moonlit land) and lights the haze, so from a ridge the valleys fill with moonlit haze cut
  by mountain shadows. In dense forest it is darker under the canopy and forest shafts by moonlight barely show yet.
  With *Night darkness → pitch black* the moon itself is dim, so little changes. Best around the full moon (day ~9 of
  the game's calendar).
* To try: **Go to - mountain ridges under the moon (draft), day 9 00-30** (it also sets Night darkness to Normal for
  that run, so the moon is bright enough to see it).

## New in build 94 — no jump at the cloud base, no curtains
**Host and everyone joining need build 94** (versions must match to connect).
* Your dumps 23:55: flying up through the cloud base, the clouds jumped from see-through to tall curtains. With
  *Clouds around you → one pass* the switch now fades in over about 300 m of height, so nothing jumps.
* The broken cloud sea's holes no longer cut straight columns through the layer (the tall vertical "curtains" seen
  from inside it): their outline wanders with height, so you see lumpy clouds.
* Known: a faint straight line can show along the horizon while you are near the base (where the two cloud passes
  blend); the grain in the sky's clouds with SMAA is still there (TAA hides it).
* To try: **Go to - cloud base in the volcanic hills (your dump 23-55), 11-04**: fly up and down (Space / C) through
  the base at ~2,130 m.

## New in build 93 — no more grain in thin cloud
**Host and everyone joining need build 93** (versions must match to connect).
* The grainy, dotted pattern on Alden and the ground near him when you stand in thin cloud (your screenshot at the
  badlands summit, 13:59) is gone. The clouds in front of the land are marched in steps of ~20 m; for anything nearer
  than one step a pixel got either a whole step of cloud or none. Now the last step ends at what is behind it, so
  near things are evenly veiled. Clouds further away look the same as before.
* To check: **Go to - summit in the clouds (one pass draft), 14-00**, or walk into any thin cloud.

## New in build 92 — clouds that hide the mountains (draft)
**Host and everyone joining need build 92** (versions must match to connect).
* Your "uniformly transparent" clouds in the mountains: the clouds over the land and over the sky are drawn by two
  passes that differ a little; standing in or near the cloud layer, their seam drew the mountains' outlines into
  cloud that was really opaque. **Settings → Sky → Clouds around you → one pass in the layer (draft)**: while you are
  at or above the cloud base, one pass draws them all and a cloud in front of you hides what is behind it. It costs
  some frame rate, only then (about 49 → 41 FPS at a summit in the clouds). You may see a small change in the clouds
  when you cross the cloud base.
* **Sky → Cloud body → denser, crisper (draft)**: thin cloud becomes clear air, the rest denser — a little whiter and
  crisper clouds; small change.
* Tried and not included: a cheaper cloud march (the clouds went grainy and blocky for 0–2 ms) and "crisper billows"
  for the shaving-foam look (greyer or blockier, not crisper). When clouds look like foam to you, a dump (F9) then
  would help the most.
* To try: **Go to - summit in the clouds (one pass draft), 14-00**.

## New in build 91 — cloud sea broken from below (draft)
**Host and everyone joining need build 91** (versions must match to connect).
* **Settings → Sky → Cloud sea → broken from below (draft)** (*under the high ridges* is build 89's first draft). Seen
  from a ridge it is still a cloud sea; down in a valley you now stand *under* it instead of in fog: a broken sky of sunlit
  clouds with blue between, pink-lit clouds in the morning. At about 90 % cover it closes into a grey day.
* Near you, while you are below the sea, the clouds keep a few hundred metres clear of the ground; climb a slope up into
  it and they come down around you. (So two players at different heights see a valley differently.)
* Light under the sea comes down through its gaps, with ragged edges, and the valley air is damper. Honest note: the
  shafts are still subtle in the valleys I tried; they need a wide valley with the sea high above. Tell me (or dump)
  where you see them or miss them.
* Known: the edges of the holes can still look like soft vertical curtains close overhead; looking down onto the cloud
  sea and up through it costs some frame rate.
* To try: **Go to - valley under the cloud sea (draft), 08-00** (next to your ridge) and **Go to - mountain ridges,
  cloud sea (draft) 08-48** (now with the broken option); both turn it on for that run only.

## New in build 90 — no more fast fog waves
**Host and everyone joining need build 90** (versions must match to connect).
* The fast waves in the air (your dumps 19:46 and 19:49) were the sun shafts' fine lanes. They turned about the
  world's centre, about 230 km away there, so the slowly moving sun swept them through the haze a few lane widths a
  second, worst at sunrise. Now they turn about where you are and stay put; far away they follow the sun slowly, as
  real shafts do. Your guess that it was the moving sun was right.
* New setting **Lighting and effects → Ray lanes**: *steady* (default) or *build 89 (fast waves)* to compare.
* To check: your sunrise dump with the clock running: **Go to - swamp at sunrise, clock running (your dump 19-49)**.

## New in build 89 — cloud sea under the high ridges (draft)
**Host and everyone joining need build 89** (versions must match to connect).
* Why your ridge view was flat: there the clouds' base was ~100 m below you and their tops 1–2 km above, so you were
  standing in the bottom of the layer, and every view down crossed its blue-grey underside.
* **Draft: Settings → Sky → Cloud sea → under the high ridges.** It needs *Cloud base → above the land* and volumetric
  v2 clouds. In high country the clouds form a thin, broken layer about 350 m over the region's ground, below the
  ridges and summits: from up high you look down onto lit, lumpy tops with gaps onto the valleys. In the valleys the
  ceiling is lower. Lowlands are unchanged, and storms keep their thick deck. *Build 88* keeps the old clouds.
  The launcher **Go to - mountain ridges, cloud sea (draft) 08-48** turns it on for that run only, without saving it.
* Looking down onto the cloud sea costs more than build 88's opaque layer (in our captures ~44–53 FPS where build 88
  was at the 60 cap). Not checked yet: shafts under the gaps, seen from a valley.
* **Draft: Sky → Mountains shade the clouds** (from before; you have it on): cloud in a mountain's shadow is now grey,
  not a saturated sky blue.
* **Draft: Lighting and effects → Haze and height → height-aware**: Godot's distance haze thins with your height above
  the region (clear up high, hazy in the valleys). Small effect: with no clouds the far ranges are a little crisper.

## New in build 88 — clouds hide the rays
**Host and everyone joining need build 88** (versions must match to connect).
* The "blue wall" high on your ridge is the real cloud sea below you (70 % cover, the deck between ~1.5 and 3 km).
  The rays in open air used to paint the hidden land's haze and shafts over those clouds — that was most of your
  10:04 ridge view. New setting **Rays and clouds**: *clouds hide the rays* (default, correct) or *rays over clouds
  (build 87)* for the old look.
* Known (next): from above, that cloud sea is flat and even — no billowed tops yet.
* With *Cloud base → above the land*, mountains now catch cloud by how high they rise above their region, so a
  whole high country is no longer one cloud bank.

## New in build 87 — look presets, wind waves slider
**Host and everyone joining need build 87** (versions must match to connect).
* **Look presets** in the Settings tab's preset list: *Look: Clear*, *Natural*, *Atmospheric*, *Dramatic*. Each sets only
  the atmosphere's look (haze, glow, rays, mist, contrast), never quality or performance, so you can combine any of
  them with Low–Ultra. A starting point: change anything afterwards and *Save as* your own.
  * Clear: crisp and far, few rays. Natural: rays and haze only when the weather makes them. Atmospheric: hazier,
    rays any time. Dramatic: strong shafts and glow, richer colour.
* **Wind waves on grass** (Lighting and effects): the lighter bands that gusts sweep over meadows — 0 turns them off
  (the grass still sways). I think these were the "waves moving like the wind" in your last dump; tell me if not.
* Known: high up in the mountains (around 3,000 m) with *Rays in open air* off, the volumetric v2 clouds can cover the
  whole view in an even blue (Clear has air rays off). Next on my list.

## New in build 86 — presets keep your look; no more fast bands in the light
**Host and everyone joining need build 86** (versions must match to connect).
* **Fixed: loading Ultra blew the picture out.** The built-in presets (Low–Ultra) now only change quality and
  performance (view distances, detail, shadows, effect quality); your look settings (haze, glow, sun shafts, god ray
  contrast, light beams, field of view, exposure, saturation) stay as you set them. Ultra used to carry an old test
  look (haze 1.95, glow 1.5, sun shafts 3…).
* I reset those values in your settings to their defaults (haze 1, glow 0.6, sun shafts 0.6, god ray contrast 0,
  light beams 1, field of view 70, Rays strength 1); the old file is kept as
  `rewrite_settings.before-look-reset-20261019.cfg`. Everything else is as you had it.
* **Fixed (I think): fast-moving bands in the lit fog.** The ray effects moved their sampling pattern every frame,
  which only TAA smooths out; with SMAA it crawled through the light. Now it holds still unless TAA or FSR 2 is on.
  Tell me if you still see them.

## New in build 85 — cloud drafts, settings presets
**Host and everyone joining need build 85** (versions must match to connect).
* **Settings presets** (Settings tab, top row): pick *Low*, *Medium*, *High*, *Ultra* or one of your own and press
  *Load*; type a name and *Save as* to keep your current look and performance settings; *Delete* removes your own.
  A preset covers graphics and effects, not the time, weather, simulation, interface, window or audio.
* **Sky → Cloud shape → rounded (draft)**: clouds no longer "locked in a box" — each cloud has its own height, domed
  tops and slightly uneven, lumpy bases, finer billows close up. Overcast and storm decks stay as they were. Known:
  at sunset there is less cloud than before.
* **Sky → Cloud base → above the land (draft)**: in high country the cloud base rides ~1.5 km over the region's
  ground instead of 1.5 km over the sea (in your mountain world: ~2,950 m instead of 1,500 m), so the deck no longer
  sits at valley height. High spots near 3,000 m are now at the cloud base itself.
* The noon sky's rows of small white dashes (the "mackerel" clouds) are smoothed with the rounded shape.
* Both cloud drafts need *Cloud quality: volumetric v2* (the Ultra preset sets it). The "Go to" launchers are here too.

## New in build 84 — "Go to" launchers for the test spots
**Host and everyone joining need build 84** (versions must match to connect).
* Next to `JOIN.cmd` there are **"Go to - … .cmd"** files: each starts the game at one of the test spots — the same
  place, view, time and weather as the captures — with your own graphics settings. Then switch *Light and shadow in
  the haze*, *Rays and haze* or *Rays strength* there and compare. (Run `JOIN.cmd` first.)
  * mountain ridges after rain (your dump at 10:04), the same ridges with a low sun (08:48), your rock pillar in the
    badlands (03:43), and the cliff lake at sunrise after rain (in the test world).
* A dump's Reproduce line now also works for playing: the game starts at the dump's place instead of the world's start.
* Otherwise the same as build 83.

## New in build 83 — Noor's notes: light and shadow in the haze (draft); stronger after rain
**Host and everyone joining need build 83** (versions must match to connect).
* New setting: Settings → Lighting and effects → **Light and shadow in the haze** — *build 82* (default) or
  **environment contrast (draft)**, from Noor's notes: contrast from the world, not a darker picture.
  * Mountains shade each other out to ~10 km: ground in a far mountain's shadow loses most of the direct sun (the sky
    still lights it, so not black).
  * The haze near you is shaped by height: thick in valleys, thinner on ridges, so the mountain layers separate, and
    it takes sunlight only where the sun reaches it, so shafts stand out against darker haze.
  * Works with **Rays in open air** on (any level).
* **After rain** the air fills with much more haze, even in dry country (wet ground steams); the shafts are stronger.
* New slider **Rays strength** (0.5–2.5, 1 = as build 82) for the forest rays and the rays in open air.
* Behind the scenes: my capture windows no longer react to your mouse and keys if you play while they run.

## New in build 82 — rays by the weather (draft)
**Host and everyone joining need build 82** (versions must match to connect).
New setting: Settings → Lighting and effects → **Rays and haze** — *always (build 81)* (default) or **by the weather
(draft)**. With *by the weather*, shafts show only when the air holds something to catch the light:
* **Moisture**: morning haze after sunrise, burning off toward midday (a little again before sunset); wet ground after
  rain; more near water, in misty biomes and in forests; little in dry climates. Calm air keeps it, wind clears it.
* **Dust**: dry country (badlands, deserts) when the wind blows.
* So a dry, windy noon is crisp and clear, even in the forest; a calm wet dawn fills with shafts. It works on all ray
  effects at once (forest rays, rays in open air, the near fog round rocks and single trees).
* Try: the same place at dawn and at noon; after rain (H, then stop it); a desert with *Wind strength* up. Your debug
  dumps (F9) now show the **Air load** (moisture, dust), so I can see what the air was doing.
* Known: your rock pillar still casts no clear shaft of its own (its shadow is thin and the noon sun is high).

## New in build 81 — the sun under the trees no longer blinds
**Host and everyone joining need build 81** (versions must match to connect).
* With *Forest rays* A or A2, looking at the sun from under the trees blew the sun and the crowns round it out to
  white (the forest air's extra sunlight piles up toward the sun). New setting: Settings → Lighting and effects →
  **Forest rays: sun glare** — *softened* (default): that glow eases off the more directly you look at the sun, so the
  shafts beside and across the view keep their strength; *build 80 (full glare)*: as before.
* Your two milky-white dumps on the mountainside (02:55–02:56) were the cloud deck at that height, not the forest rays.
* Your settings now have *Distance haze* 0 and *God ray contrast* 0 — if you didn't mean to set those, put them back
  (they were 1.85 and 0.55).

## New in build 80 — rays in open air (draft)
**Host and everyone joining need build 80** (versions must match to connect).
New setting: Settings → Lighting and effects → **Rays in open air (M2 draft)** — *off* (default) / low / medium / high.
* Shafts of sunlight outside forests, kilometres long: past mountain peaks and ridges and through gaps in the clouds,
  in the haze over valleys, lakes and plains. Each ridge casts a fan of light lanes with darker lanes between.
* **Mountains block the sun now**: the glow no longer shines through the rock, and a valley in a mountain's shadow is
  in shade (its air and ground too; the terrain casts shadows up to ~3 km away while this is on).
* While on, it replaces the screen-space sun shafts and the light beams under clouds.
* Best seen: a low sun (sunrise or evening, 5–15° up) just behind a ridge or cliff, looking toward it, over a valley
  or lake. Cost on your 3080: about +0.3 / +0.7 / +1.3 ms (low / medium / high).
* Known: shafts from cloud gaps are still soft (no lanes yet); in the high mountains the cloud deck sits at valley
  height, so many views there look through cloud. Send me a dump (F9) where it looks wrong or especially good.
* Also: a dump's Reproduce line now gives the same time of day after the jump to its place.

## New in build 79 — forest rays A2 at noon
**Host and everyone joining need build 79** (versions must match to connect).
* **A2 at noon**: with a high sun the forest air went milky white (much more light comes through the crowns). A2's
  sunlight into the air now eases as the sun climbs past 30°, down to about a third at 65°; low sun (sunrise, evening)
  is unchanged, morning (40°) nearly so. Tell me if noon now looks too plain or still too hazy.
* Behind the scenes: benchmarks no longer write their test settings into your settings file while they run (a run
  that froze last night had left them there; I restored yours from your 23:22 dump, and *Forest rays A2: forward
  scattering* is back at 0.75 — set your value again if you had changed it).

## New in build 78 — the time slider no longer ages you
**Host and everyone joining need build 78** (versions must match to connect).
* **Fixed: dragging the time slider counted as time lived** — food, warmth and health ran down as if the hours had
  passed, so testing the light in winter froze you to death and you started a new life at the world's start (the
  "teleport back to spawn"). Setting the clock now passes no time for you, fires or animals.
* Your autosave from the evening of the 18th was written by my test runs, not by you; your own saves are untouched.

## New in build 77 — rays seen from the side
**Host and everyone joining need build 77** (versions must match to connect).
* New slider: Settings → Lighting and effects → **Forest rays A2: forward scattering** (0.75 = build 76).
  Lower (try **0.3–0.45**): the shafts stay visible when you turn away from the sun or have it behind you, with more
  glowing haze between the trees; higher: crisper and brighter toward the sun, gone from the side. Tell me the value
  you like (and a dump where it looks wrong).
* The noon sun stood only 23° up because your *Season* is **winter** (Time → Season): at your latitude (43.6°) that is
  real. In summer it reaches ~70°; straight overhead only happens in the tropics.
* The game opens on the monitor it was last moved to: Settings → Display → **Monitor** brings it back.

## New in build 76 — forest rays A2 (draft) and fog quality
**Host and everyone joining need build 76** (versions must match to connect).
Nothing changes until you switch it on; to try the rays:
1. Settings → Lighting and effects → **Forest rays** → *A2: sharp rays (M2 draft)*.
2. Settings → Camera and performance → **Volumetric fog quality** → *high* (or *very high*: smoother shafts,
   ~3 ms more on your 3080). *Off* removes all volumetric fog (rays, mist patches, the torch's glow) for weak PCs.
3. Stand in a dense forest and move the time (Esc → time slider; *Time → Paused* keeps the sun still): the shafts are
   strongest with the sun low (5–15° up) and when you look toward it. Tuned only at sunrise so far: tell me how noon,
   evening and the light on the ground look (an F-key dump where something looks wrong).
* A2 = A with more sunlight into the forest air, less sky light filling the shadows between the shafts, and the fog's
  cells kept close to you. *A* is unchanged (build 75), *build 47* is no rays.

## New in build 75 — caustics, scan, crouching (your notes on build 74)
**Host and everyone joining need build 75** (versions must match to connect).
* **Caustics follow the sun**: the light net on the bed of shallow water fades at dusk, is gone at night and weaker
  under clouds (it glowed white at dawn in your pond dump). Settings → Lighting and effects → **Caustics**:
  *by the sun* (default) or *build 74 (always)*.
* **Scan (Z)**: the blue wave and its tint are gone after 3 s, leaving the marks; **items glow like animals** — stones
  (grey-white), berry bushes (pink), carcasses, piles and blueprints — as the ring reaches them.
* Fixed: the scan's wave did not draw while your torch smoked (only a patch round Alden).
* **Crouching (C)**: Alden bends forward at the waist with his head up, legs one before the other — no more legs
  swinging forward into the ground.

## New in build 74 — your smoke texture, torch in mist
**Host and everyone joining need build 74** (versions must match to connect).
* **Smoke from your texture** for the torch and, new, **campfires** (they smoke while burning, not as blueprints or
  once the wood is gone). The torch's smoke starts right at the flame, denser, as you asked. At night the smoke takes
  the moonlight instead of glowing white; a campfire lights its own smoke from below.
  Esc → Settings → Lighting and effects → **Smoke**: *smoke texture* (default) or *build 72 (soft dots, torch only)*.
* **Torch in mist** (same page), to try and tell me: *build 72 (even glow)* is still the default — in a mist bank the
  torch lit everything within 16 m evenly (your dump 221438); *halo at the flame* makes the mist glow round the flame
  and fade within a few metres.
* Debug dumps (F9) now also carry the mist's drift and the ground's wetness, so I can rebuild the same mist.

## New in build 73 — ready for the real multiplayer test (4–12 players)
**Host and everyone joining must have build 73** (the network messages changed; build 72 does not connect).
* Tested on one PC with 12 players (bot players that walk, hunt with the bow and build log huts): the host stays at
  60 fps with 12 players together or in 12 different places, everyone sees the same pieces, others are drawn within
  ~0.4 m of where they really are (~0.6 m over a 320 ms line like the Brazil test).
* **Less to download**: each player now receives ~14 KB/s with 12 players together (was 26), ~8 KB/s when far apart;
  the host sends ~160 KB/s for 12 (1.3 Mbit/s). Players more than 400 m away are updated 4 times a second.
* **Faster host**: animals far apart no longer slow the host (350 animals around 12 players: from a quarter of a frame
  budget to almost nothing); many buildings no longer slow anyone's game.
* **Bots for your own test** (developer folder): `TEST_MP_BOTS.cmd [address] [players] [spread m] [seconds]` joins your
  hosted game with bot players, no window. `TEST_MP_BOTS.cmd 127.0.0.1 8` adds 7 bots beside you; a spread of 3000 puts
  each in its own place 3 km out.
  Without the developer folder, from the game folder: `Thornwake.exe --headless -- --world --join=<address> --bots=7` (8 players: that one and 7 bots).
* **Please, when you test with friends**: after the session send me the network logs of the host and of each friend
  (`%APPDATA%\Thornwake\debug\net-….log`) — they now record the host's load every second (frame times, animals,
  pieces, where the time goes) — and say how it felt (anyone jumping or lagging, actions slow to happen).

## New in build 72 — vertical half and quarter walls, fences, stairs on floors
* **Half wall, vertical** (half width, full height) and **Quarter wall** (Wall, V): on a 3 m side they take the half you
  aim at. The old half wall is now *Half wall, horizontal*.
* **Fences** (category Fence): straight, narrow and diagonal; on floor edges like walls, or on the ground — the next one
  chains to the end of the last and can turn 45° (R).
* **Stairs on a floor**: aim at the floor and the stairs stand on it, up toward the next storey.
* **Floors on walls** lie over the room now, not centred on the wall; a half floor goes along the wall, inside.
* **Fixed**: the ghost could freeze near some short edges (stairs especially).

## New in build 71 — walls and stairs find their edge
* **Walls snap to edges** of floors and foundations (their sides, the lines through their middle, a square's diagonals,
  a triangle's long side) and to the tops of walls. Choose the shape with V — wall, window, doorway, half wall, gables —
  and **the edge picks the size**: aim near a diagonal and you get the diagonal doorway or window; on a half foundation's
  short side, the narrow wall. Where two lines meet, the one you look at wins. R flips which way a wall faces.
* **A door in a diagonal doorway** goes diagonal by itself.
* **Stairs attach by their top** to an edge and run up onto the piece — onto a triangle's long side they come out
  diagonal by themselves.

## New in build 70 — diagonal walls, triangles, animals that go round things
* **Diagonal walls** (Wall, V: *Diagonal wall*, *Diagonal half wall*): corner to corner across a foundation or floor.
  With a diagonal piece **R turns 45°**.
* **Triangle foundation** and **triangle floor** (Foundation / Floor, V): half a square, to close the corners a diagonal
  wall leaves; two make a full square.
* **Animals go round trees and your buildings** instead of walking through them (they can still pass under a deck on
  stilts).

## New in build 69 — sonar scan, blockers shown, no bites through floors
* **Scan (Z)** sweeps out like sonar: a glowing wave lights the land as it passes, and the animals it finds glow for a
  while (Settings → Interface → Scan look: the old ring).
* **When something is in the way** of a piece, the menu names it and it flashes red.
* **Blueprint shadows** can be switched on (Settings → Interface).
* **Wolves cannot bite you through a floor** any more; on a platform you are safe (they wait below).
* **No gathering from up on a platform**: stones, berries and grass only on your level.

## New in build 68 — tracking, sneaking, stamina, smaller pieces
* **Z — look around (scan)**: a ring sweeps out 45 m and marks stones, berry bushes, arrows, carcasses, blueprints and
  animals (with distance) for 20 seconds, and lights up the **animal tracks** around (they stay for five minutes).
* **Blood** where an animal is hit.
* **C — sneak**: slow and low; animals hear and see you much less.
* **Stamina**: sprinting and jumping tire you, rest refills it (shown with your needs). Aiming or drawing the bow slows
  you down.
* **V** switches the camera to the other shoulder. The bow is now in the **right** hand (Settings → Interface → Bow hand).
* **New pieces**: half and quarter foundations, a quarter floor, a narrow wall and a narrow half wall.
* **Fixed**: walls aimed at a second floor went below it.

## New in build 67 — snap points shown, the bow hits where you aim, arrow trails
* **Snap points**: while building, dots show where pieces can join — blue on the pieces near the ghost, yellow on the
  ghost, green where they meet. **P** shows or hides them (Settings → Interface → Show snap points).
* **Bow**: the arrow now comes down on the point under the crosshair (the shot allows for the drop), also with a short
  draw, also when the crosshair is on an animal. Settings → Interface → Bow aim → *straight* gives the old behaviour.
* **Arrow trails**: a thin fading trail shows each arrow's arc (Settings → Interface → Arrow trails).

## New in build 66 — testing tools, snapping you can steer, steep stairs
* **Testing tools**: in the pack (I) tick *Testing tools (free building)* (or Settings → Simulation). Blueprints are then
  built at once without materials, and *Give materials* / *Give weapons and tools* fill your pack.
* **Turning**: a snapped piece now faces the way you turn it — **R turns 90°**, the wheel 15°. Facing a foundation's edge,
  a wall goes along it; turn it a quarter and it stands across the deck (an inner wall). Stairs and half floors turn with R.
* **Many more snap points** (every 0.75 m on decks, floors, walls and roof edges): inner walls anywhere on a deck's grid,
  half floors and stairs at quarter positions, staggered foundations.
* **Steep stairs** (Stairs, V): 3 m up over 3 m, so a staircase fits inside a 3 × 3 m room.
* **Fixed**: picking a piece with the mouse placed it at once (now the next click places); a tree being chopped could be
  walked through; F9 dumps now include what you are building and the pieces around you.

## New in build 65 — building with the mouse, campfires on decks, arrows found again
* **Choose with the mouse**: while building, press **Tab** — the cursor is free over the menu; click a category, then a
  piece's picture, and you are back placing it (Tab or Esc returns without changing).
* **Right-click a placed piece** (while building): its menu shows every shape of its kind with pictures — click one to
  change it (a blueprint, or a finished piece: its materials go into the new one) — plus *Turn it round*, *Bring
  materials*, *Take it down*. The middle mouse / Backspace still take down the piece you aim at.
* **Campfires and lean-tos can be taken down and changed** now (they could not be aimed at before).
* **A campfire (or any piece that stands on the ground) can stand on a floor or foundation** too.
* **Arrows**: most missed arrows can be found where they fell, most that hit come back with the carcass, and a prompt under
  the screen's centre tells you what E will do ("E  Pick up arrow", "E  Butcher the deer", "E  Bring materials…").

## New in build 64 — after your first try of hunting and building
* **Walls go on the foundation**, not under it (the ghost now keeps to the side of the surface you aim at).
* **Foundation height**: placed on a slope, a foundation stands level above the highest ground under it. **Page Up /
  Page Down** raise or lower it (Shift: 1 m steps), **Alt + wheel** in small steps, **Home** resets. New shape
  **On stilts** (V in Foundation): a deck on 6 m braced posts for swamps and steep ground.
* **Build menu with pictures** of every piece: tabs for the categories, a card per shape (V still cycles; press Tab to
  free the cursor and click), Logs / Thatch, the cost against what you carry, and why a piece cannot go somewhere.
* **Aiming the bow**: hold the **right mouse** — the camera moves in close over the shoulder and the view zooms a little;
  draw and shoot with the left mouse as before.
* **Animals**: build 63's tests had switched your *Animals* setting to *none* — sorry; it is back to *normal*. Animals
  are also more common now, deer live in swamps and marshes too, and the HUD says where the nearest one is.

## New in build 63 — hunting and building
**In multiplayer everyone needs build 63** (build 62 and 63 do not connect). Saves from builds 60–62 load.
* **Animals**: deer, stags and foxes in temperate woods and meadows, wolves in forests and the cold north (none in
  deserts, badlands or on ice). Deer and foxes see and hear you — walk, don't run, and they let you come closer — and
  flee with their herd. A stag you hurt may turn on you. Wolves hunt in packs at night or when you come close; hurt one
  badly and it runs. Settings → Simulation → *Animals*: none / few / normal / many.
* **Weapons** (make them in the pack, I): **8** hunting spear (2 branches, a stone, 3 fibre), **9** rough bow (2
  branches, 8 fibre) with **arrows** (5 from 2 branches, a stone or a bone, 2 fibre), **0** stone-edged sword (a log, 4
  stones, 4 fibre), and a stone knife for butchering. The left mouse thrusts or cuts at what is in front of you; with the
  bow, hold it to draw (longer = farther and harder) and release — arrows drop with distance; a missed arrow can often be
  picked up where it fell (E). The camera moves over Alden's shoulder while the bow is out (Settings → Interface).
  The axe (6) also strikes an animal in front of you.
* **Butchering**: E on a fallen animal with a knife, axe or sword: raw meat, hide, bones. Roast the meat at a campfire
  (I → *Roast meat*).
* **Building — B**: the bar at the bottom shows the categories (**1–7**: camp, foundation, floor, wall, door, stairs,
  roof) and the shapes of the current one. **V** changes the shape (a wall → a window wall → a doorway → half wall →
  gables and peaks), **X** the material (roofs: logs or thatch). A see-through ghost shows where it goes: it **snaps** to
  the pieces nearby (walls onto a foundation's edges, a roof's eave onto a wall's top, a door into a doorway), or stands
  freely on the ground. **R** turns 45°, the **wheel** 15° (Shift: 5°), **Page Up / Page Down** raise or lower it,
  **Home** resets, hold **Ctrl** to place without snapping. **Left mouse**: place a **blueprint** (free). Then **E** on it
  brings the materials from your pack — friends can help. Aim at the **middle** of a wall (or any piece) with another
  shape chosen to **swap** it — the logs go into the new one. **Middle mouse / Backspace** takes a piece down (everything
  back). **B**, right mouse or Esc closes.
* **The campfire and the lean-to are built the same way** now (Camp, 1): turn them and put them exactly where you want.
* **Roofs keep the rain off** (and most of the wind) like the lean-to. **Doors** open and shut with E. You step up onto
  foundations by walking into them.
* The pieces' looks are placeholders (plain logs and straw) — tell me what you want them to look like.

## New in build 62 — bare patches where grass was pulled, a progress bar, longer fish runs
* **Pulled grass** leaves a bare patch (about 3 × 3 m, for everyone; it grows back in three game days). Nothing left
  there to pull: move to fresh grass.
* **Progress bar over Alden's head** while something takes time — making, gathering, felling (the hits so far),
  cutting up a trunk, charging a cast — with a short text (Settings → Interface → Action text turns the text off).
* **Fish run farther**: each run its own length, short tugs and long dashes, up to ~7 m to either side.

## New in build 61 — fishing as you asked; the lean-to fixed
* **Fishing:** walk about while the bobber is out — it stays, the line follows (go too far and it drags the bobber after
  you; out of the water, it is reeled in). Click to reel in and cast again. With a fish on, A/D turn the rod against it
  (W/S still walk); the bobber runs left and right, the rod bends toward the pull, the line turns red when the tension
  is high. The bobber bobs and sways on the water (and rides the waves of lakes and the sea).
* **Lean-to:** the roof now slopes from the poles at the front down to the ground behind (it was mirrored).

## New in build 60 — gathering, fishing, shelter, death and saving: the first slice is complete
**In multiplayer everyone needs build 60** (build 59 and 60 do not connect).
* **E gathers** (nearest first): wood lying in a pile, a campfire (wood on it), a berry bush (4 handfuls; they grow back
  in two game days), a loose stone, else long grass where you stand (plant fibre). It takes a moment; moving stops it.
* **Fishing:** make a rough rod (a branch and 4 plant fibre) and berry bait (one berry → three baits) in the pack (I).
  **7** takes the rod. Hold the left mouse button to throw farther, release to cast at water; when the bobber goes under,
  click; then hold **A** or **D** against the fish's pull and let go when it rests — land it before the line gives.
  Rivers: trout, grayling; lakes: perch, pike (dawn and dusk); ponds: carp, tench; the sea: mackerel, cod. The bobber
  rides the waves and drifts with a river's current. Cook the fish at a campfire.
* **Lean-to** (3 logs, 8 branches, 4 plant fibre): under it the rain stays off and most of the wind — with a fire in
  front you get through a cold, rainy night.
* **Death:** at no health you drop everything you carried where you fell (go back for it — or someone else will) and
  start again at the start with the basic kit.
* **Saving:** Esc → *Save* / *Load…*, **F5** a quick save, an autosave every 10 minutes. Felled trees, picked stones and
  berries, dug ground, fires, trunks, your pack, where you were and the time of day come back. Hosting, your friends'
  packs are kept under their names.

## New in build 59 — felling a tree in three acts
**In multiplayer everyone needs build 59** (build 58 and 59 do not connect).
* **Notch and fall side:** your first axe hits cut a notch on the side you stand; the tree will fall **toward the
  notch** (the wind has some say). When the notch is done you are told to finish from behind — near the end it creaks,
  leans and drops leaves. Stay out of where it lands: a tree coming down on you hurts.
* **The fall:** it comes down faster and faster; on impact the crown breaks up (leaves, twigs, dust, the camera shakes
  if you are close) and leaves a heap of branches (E picks them up).
* **The trunk:** a bare log is left lying there. It rolls down slopes, you can push it by walking into it, and a log
  rolling into you fast hurts. Cut it up with the axe (each cut shows as a ring); it splits into logs (E). Bigger trees
  take more hits and give more logs.

## New in build 58 — chopping with hits, a falling tree, log piles; torch in the left hand
**In multiplayer everyone needs build 58** (build 57 and 58 do not connect).
* **Chopping** (axe: 6, hold the left mouse on a tree in front of you): every stroke that lands is a hit, wood chips
  fly and the tree shakes; after 6–8 hits it falls — away from you, and the wind pushes it — crashes with a small
  bounce, lies a moment and sinks away. Its logs and branches then lie where it came down: **E** picks them up.
  Everyone sees the same hits and the same fall.
* **Hands:** the torch is always in Alden's left hand (also in first person), the axe in his right.
* Not yet: the fallen trunk staying as a log you can push and cut up (Valheim) — to be discussed.

## New in build 57 — chop trees, build a campfire, hunger and cold; storm and darkness settings
**In multiplayer everyone needs build 57** (build 56 and 57 do not connect).
* **First gameplay (placeholders):** you start with a stone axe, three dried meats and *Notes on Felling*.
  * **6** takes the axe out (1–5 put it away). Hold the **left mouse button** on a tree in front of you: Alden chops,
    the tree falls after a few seconds and leaves a stump; logs and branches go into your pack.
  * **I** opens the pack: your items (**Eat**) and what you can make (**Make it**). A **campfire** takes 2 logs and 4
    branches; it is built in front of you, lit, with about 1½ hours of wood. Cooking a fish needs a fish — fishing
    comes next.
  * **E** puts wood on the nearest fire (later: gathering grass, berries, stones).
  * **Needs** (bottom right): food, body warmth (from the real temperature, wind, rain and a fire nearby), wetness,
    health. Cold and hunger hurt; there is no dying yet. *Settings → Simulation → Hunger and cold* turns them off.
  * In multiplayer the host checks everything; felled trees and fires are the same for everyone, also for players who
    join later. Nothing is saved yet.
* **Storms:** no more sun dog or glow around a sun hidden by the storm deck.
* **New darkness settings:** *Overcast and storm darkness* and *Forest darkness* (Lighting and effects; default 0.5,
  0 = build 56), *Night darkness* (Simulation: bright, normal, dark, very dark, pitch black; the sky looks the same at
  every level; in multiplayer the host's applies).

## New in build 56 — no more freezes after teleports, players on the map, host a world you set up
**Host and friends must all use build 56** (build 55 and 56 do not connect to each other).
* **Fixed: the host's game froze (12 s, 48 s, or for good) after teleports while it rained** (H). Rain erosion then
  reworked all the ground between the old place and the new one — hundreds of kilometres. Now only the worn spots.
* **Rain started with H on the host now rains for everyone.** On a joined game H says the host decides. The natural
  weather was already the same for everyone (computed from the world and the shared time); lightning (L), poured
  water and springs are still each player's own.
* **World map (M):** your marker is an arrow (the notch at the back shows the way you look). The other players are on
  it as arrows in their tunic colour with their names, moving while the map is open. **Players** list beside the map:
  *Go to …* puts you right next to them.
* **Set the world up before hosting:** start alone (*Explore the new world*), make the world you want (Esc → New
  world: type, seed, sliders; settings; digging), then **Esc → Host this world…** — it hosts on the port and under the
  name from the launcher's Multiplayer page. Friends join as usual and get that world with your digging. While
  hosting, Esc → New world still takes everyone along to the next world.
* **Fixed for joining players:** the game crashed when the address could not be used (e.g. with two ports); after a
  lost connection it filled its log with errors.
* **Logs:** the game log is now written line by line (a crash keeps everything up to it), and any frame over half a
  second is noted in it and in the `net-…log` with what was slow. After a problem: F9, then send the newest
  `dump-…txt` and `net-…log` from `%APPDATA%\Thornwake\debug` (and `%APPDATA%\Thornwake\logs`) from both of you.

## New in build 55 — multiplayer test, Alden's torch, first person
* **Multiplayer (first test):** launcher → *Multiplayer…*: type your name; one player presses **Host a game**, the
  others type the host's address and press **Join**. Everyone sees the same world, time of day, weather and clouds,
  each other walking, flying and carrying torches, and holes dug by anyone (also players who join later). The host's
  *New world* takes everyone along. Not shared yet: poured water, springs and puddles (each player's own for now).
* **Hosting over the internet** (the host does this once):
  1. *Check for CGNAT first.* Open the router's page (usually http://192.168.0.1 or http://192.168.1.1 — the
     "Default Gateway" that `ipconfig` shows) and find its WAN / Internet IP address. Compare it with the address
     https://www.whatismyip.com shows. **Same** → good, go on. **Different**, or it starts with 100.64–100.127, 10.,
     172.16–172.31 or 192.168. → the provider shares one public address among customers (CGNAT) and port forwarding
     cannot work: ask the provider for a public IP address (often a free or cheap option), or tell me and we use a
     tunnel (Tailscale, ZeroTier or playit.gg) for the test.
  2. *Give this PC a fixed local address* in the router (DHCP reservation / "static lease" for this PC's network
     card), so the next step keeps pointing at it. `ipconfig` shows the PC's current IPv4 address (e.g. 192.168.0.17).
  3. *Forward the port:* router → Port forwarding / Virtual server / NAT: protocol **UDP**, external port **7777**,
     internal IP **this PC's address**, internal port **7777**. Save.
  4. *Windows Firewall:* the first time you press Host a game, Windows asks whether Thornwake may use the network —
     allow it (tick *Private* and *Public*).
  5. Send your friends the address whatismyip shows. They type it in *Join* (port 7777 is the default).
* **If something goes wrong:** press **F9** in the game (a debug dump) and send me the newest `dump-…txt` and
  `net-…log` from `%APPDATA%\Thornwake\debug` — from **both** the host and the friend. The `net-…log` records the
  connection once a second (ping, packet loss, traffic, time differences, every join, leave and dig).
* **Alden's torch: R** — a small flickering flame that streams downwind, a little smoke and warm light with moving
  shadows. **First person:** scroll all the way in (one more wheel step after the closest view); scroll out to return.
* Fixed: every player keeps one tunic colour on every screen (the host wears Alden's green).

## New in build 54 — new clouds, no forest hole from above, land to the horizon
* **Sky → Cloud look** (new, default *reference*): brighter, clearer clouds. The sky's clouds were veiled by the fog
  twice (Godot fogs the sky's half-resolution pass too) — now once, like the clouds in front of the land, so the two
  match. Wind streaks only in strong wind (~12 m/s and up), crisper cauliflower edges, flat blue-grey bases, bright tops,
  golden hour as before. *build 53* brings the old clouds back.
* **Fixed: a hole in the forest under you** when flying 100+ m above the trees (bare ground in a ring around a square
  of trees). Costs a few fps in the densest forests at high *Full-detail trees*.
* **Fixed: a stepped line along the horizon inside clouds** (a strip of clear far sea; *Cloud look: reference*).
* **Horizon land now reaches the horizon:** it was cut at 90 km whatever the setting; it now goes at least to the true
  horizon for your height (~160 km from 2 km up).

## New in build 53 — lake crater fixed, clouds over land match the sky, tree batches fixed
* **Fixed: the crater in the benchmark lake** (Tom's dump 182351, terrain revision 16). The coastline cut a small pocket
  of "sea" out of a lake near the coast, so the water dropped from the lake's 13 m to sea level inside it, with walls of
  water around. Sea inside a lake's reach is now lake. *World settings → Terrain version → Build 52* brings the old
  terrain back; *Build 51* can now actually be chosen (it fell back to Build 5).
* **Sky → Cloud blend (v2)** (new, default *matched*): clouds in front of land now look like the same clouds over the
  sky: the sun's true colour (not pale pink-white at sunset), the same haze and fog, no mountain outline through them,
  no cloudless band under the horizon, also over land beyond ~47 km. *build 52* brings the old near clouds back.
* **Fixed: trees losing their detail hand-over** at high settings. Godot reported up to thousands of errors a run
  ("Too many instances using shader instance variables") and the tree batches past its limit switched model without
  the dithered cross-fade. No setting changes; trees look as before.
* **Benchmark:** the camera turns to each shot's direction before the warm-up (the first still of every scene had a
  beige veil of fog left over from the previous view).
* *Lighting and effects → Forest rays*: **A** is the one going forward (your pick); B stays selectable for now.

## New in build 52 — water tools, rain erosion, lake fixes
* **Water size: `,` smaller · `.` bigger** (1–4, shown at the top). Size 1 is as before. Each step ×4: **G** pours up to
  160 m³/s spread over a disc up to 8 m across; **J** springs give up to 38 m³/s over the same disc (J on a spring removes it).
* **Rain erosion** (Settings → Simulation → *Rain erosion*, default 1, 0 = off): streams of rain water (and springs)
  slowly wear the ground away and leave the soil where they slow down, in pools and at the foot of slopes; what reaches
  rivers and lakes is gone. At 1 even a creek cuts at most ~3.6 cm an hour, so gullies take many storms (up to 10× for
  impatient tests). Around the player (the water simulation's 128 m), saved like digging; the ground updates every ~10 s.
* **Fixed: trenches and "craters" of water in lakes** (terrain revision 15). Every lake's outlet river ran through the
  lake below its surface; now it keeps the lake's level. Most outlets now step down 0–4 m where the river leaves; a few
  perched lakes step down more (a steep sheet for now, a waterfall later). *World settings → Terrain version → Build 51*
  brings the old terrain back.
* **Fixed: walls of water in lake shallows** (water experimental 3/4): lake waves now fade in shallow water like the
  sea's. (The gale in those shots was *Wind strength* at 4, the maximum.)
* **Clouds (volumetric v2):** clouds in front of far land now take the biome's haze like the sky's (no white band in
  dusty biomes). The remaining seam waits for the new cloud model (M3).
* With the weather off, the wind is the classic wind again (no left-over simulated wind).

## New in build 51 — tree band fix, nights, forest rays switch, artefact shots
* **Fixed: the grey-blue band across tree crowns at eye height** (Tom's dump 104752). Trees switching from the
  detailed to the lighter model (around *Full-detail trees*) were drawn as transparent while they faded, and the
  distant rain painted over them. The switch is now a dithered cross-fade that keeps the trees solid. Also: with the
  weather off, the distant rain no longer keeps drawing the last weather.
* **Light model → Atmosphere 2.0:** nights are dark but readable (more moonlight and night sky light, up to +¼ stop),
  as in Tom's night reference. Build 47's light is unchanged.
* **Lighting and effects → Forest rays** (M2, two candidates to compare): *build 47* (default), *A: volumetric forest
  air* (a thin haze under the crowns lit through the gaps: real shafts, also with the sun off screen; costs the
  volumetric fog), *B: screen-space shafts* (the sun shafts, stronger in forests; only with the sun on screen). Only
  under the canopy, by day.
* **Benchmark suites:** *rays* (forest shots, every still with build 47 / A / B) and *artefacts* (fixed shots of the
  tracked artefacts: far water against the sky, low sun glare, forest shafts, sky seams). The atmosphere captures now
  wait at least 30 frames after a time jump (the first night still could show the noon sky's light).
* **Clouds in front of mountains:** cloud quality *volumetric v2* draws the clouds in front of the land too (quality
  *volumetric* only draws them in the sky, so a mountain cuts its silhouette out of a cloud). Please compare on the
  gaming PC.
* **Tonight's test:** Benchmark → *atmosphere*, *rays* (and *artefacts* if there is time); then play with *Light
  model: Atmosphere 2.0* through a day and different biomes, and try *Cloud quality: volumetric v2*.

## New in build 50 — Light by biome (Atmosphere 2.0 M1)
* **Light model → Atmosphere 2.0 (experimental)** now takes its colours from **light profiles by biome**
  (`Content/light_profiles.json`): temperate forest, plains, taiga and cold lands, desert and dry rock, swamp and wet
  lowland. Each has its own shade colour and haze by time of day, its own overcast and storm colours, sun tint, sun glow
  in the haze and exposure. Walking into another biome blends the light over ~3 s.
* The temperate forest profile is tuned to Tom's reference images (teal shade, lavender dusk, deep teal night, warm
  haze toward the sun); the other four are first proposals.
* The haze glows warm toward a low sun and stays cool away from it (stronger than before, light model 1 only).
* Atmosphere captures: forest noon and night moved to a small clearing, so the camera no longer passes through crowns.
* **Please test on the gaming PC:** Benchmark → suite *atmosphere* (all ten scenes, every still with both light
  models), and play a while with *Light model: Atmosphere 2.0* through a day and through different biomes.

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
