# Water rework — plan (D284, 2026-11-08)

Tom's order (HANDOFF): after the landforms where water meets land, separate ocean, river and lake water, a mesh part and a
setting each, today's looks kept selectable; foam round rocks; the hard part is where the kinds meet (mouths, falls).

Tom's answers (2026-11-08):
* **Look**: torn between fully realistic and realistic motion with simpler colour. "The water is important. The realistic
  motion is important. Shaders are very important." So: realistic motion is fixed (currents, waves, foam, falling water);
  colour and detail are settled by showing both side by side in the game and letting Tom pick, both kept selectable.
* **Starting point**: Experimental 4 (the style he plays). Each new look starts from it.
* **First**: rivers and waterfalls.

## Today (survey, 2026-11-08)
* One water mesh per terrain tile with every kind in it (`TileBuilder.BuildWater`); the kind is only per vertex
  (`COLOR.g`: sea 0, river ⅓, lake ⅔, pond 1), bank vertices take their neighbours' highest code; one `ShaderMaterial` for
  all water, its shader swapped by *Water style* (classic, experimental 1–4).
* `CUSTOM0` means a different thing per kind (sea: bed gradient and exposure; lake: fetch; river: rock wakes).
* Waterfalls are plain river water: the surface between two river points is a straight ramp, blended across the river, so
  the water over a lip is a rounded dome (D275), shaded as flat water.
* Foam round rocks: river rocks only (styles 2+); sea stacks and rocks only get the contact foam line.
* The local water simulation draws with the same material, always as river.
* Far water (beyond the tile rings) is part of the far-land mesh.

## Steps
1. **Split** (done, D284; no visible change): each tile's water mesh gets three parts — sea, river, lake (ponds with lakes) — each with
   its own material; all three draw with today's *Water style* until a kind gets a look of its own. Triangles where kinds
   meet go to the part of the higher kind code (as the bank vertices do). The atmosphere's uniforms go to all three.
2. **Waterfalls** (done, D285, first version): the river surface ends at the lip with an edge; a falling curtain from the lip to the pool (its own mesh
   and shader: streaks, thinning, see-through at the edges), white water and spray at the foot, mist. Fixes the dome.
3. **River look** (started, D286: its own shader and setting, stones in rivers with foam, the foam's direction fixed): from Experimental 4's river: flow, rapids and standing waves, foam round rocks (already baked), banks;
   options *River water → as Water style / new*.
4. **Where kinds meet**: river mouths into the sea and lakes cross-faded over a few metres (both parts drawn there, each
   fading by its share), so each kind can have its own shader without a seam.
5. **Lakes and ponds**: calm water, wind ripples and small waves by size, their own colour.
6. **Sea**: foam and breaking water round sea stacks and rocks, calm lagoons (today they get sea waves).
Each new look is an option beside today's (a return point), and each step a build Tom can try.

## River currents that push the player (Tom, build 141: "we need to slowly start thinking about river currents")
Not started; a plan to agree on first (D302). What already exists: every river point's direction and speed (`TileBuilder.ReachFlow`,
the same current the river water is drawn with: 0.45–3.5 m/s by the grade and depth, faster in rapids, the pool under a fall
taking its river's flow), the player's swimming (deeper than 1.25 m) and wading (feet in water).
1. **The current where the player is**: the same speed and direction as drawn, sampled at the player (one function the
   water and the player share, so what you see is what pushes you). Slower near the banks and the bed (a shallow, slow
   edge to climb out at).
2. **Swimming**: the current carries the player at most of its speed; swimming across or against it is the player's
   speed on top (1.8 m/s, sprint 3.2: you can beat a lowland river, not a rapid). Settings: *River currents → off / gentle /
   full*, off as today.
3. **Wading**: knee-deep water pushes a little (a slower walk upstream, a nudge downstream), waist-deep more; standing still
   in a rapid you slide. Stamina drains faster against the current.
4. **Over a fall**: carried to the lip you go over it (falling with the water into the pool, no fall damage into deep water);
   the pool's churn pushes away from the foot.
5. **Things floating**: dropped items, logs and animals carried the same way (later, with the item physics).
6. **Multiplayer**: each player's own movement (the current is the same everywhere, nothing to send).
Test: a headless check that the current sampled at points matches the drawn flow; captures of a swim across a river.
