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
3. **River look**: from Experimental 4's river: flow, rapids and standing waves, foam round rocks (already baked), banks;
   options *River water → as Water style / new*.
4. **Where kinds meet**: river mouths into the sea and lakes cross-faded over a few metres (both parts drawn there, each
   fading by its share), so each kind can have its own shader without a seam.
5. **Lakes and ponds**: calm water, wind ripples and small waves by size, their own colour.
6. **Sea**: foam and breaking water round sea stacks and rocks, calm lagoons (today they get sea waves).
Each new look is an option beside today's (a return point), and each step a build Tom can try.
