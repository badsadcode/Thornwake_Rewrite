# Terrain: tidy-up, landform modules and new landforms — plan

*From a planning conversation between Tom and the cloud session, 2026-10-13/14. Planning only: no code was written.
For the local session: read this, then `CLAUDE.md`, and follow "How to proceed" at the end. Ask Tom before building
any new landform.*

## 1. Why

Tom: the terrain is beautiful and huge, but it needs more land formations to be interesting — more variety, more
landmarks, more places worth going to.

Tom's worry, and a fair one: the terrain is already many layers of height changes stacked on each other (continent
shape, coastline wiggle, hills, mountain ridges, erosion, rivers carving valleys, banks, lakes, wetlands, dunes, mesas,
spires, river mouths, player edits). Adding more could become chaotic and look weird. The two recent bugs were exactly
layers disagreeing: a lake outlet cutting a trench through its own lake (D89, terrain revision 15) and the coastline
cutting a pocket of sea inside a lake (D91, revision 16).

## 2. What already protects the work

* **Old worlds never change**: every generator change is a new terrain revision; the previous ones stay selectable in
  *Terrain version* and their exact shape is frozen by fingerprint tests (`Tests/WorldGen/CoreTests.cs`, fixtures in
  `Tests/WorldGen/Fixtures/`).
* **Invariant tests and tools** catch layers disagreeing: no sea inside a lake, no trench inside a lake, no dry ground
  below a lake's surface; `--probe-water`; before/after captures.
* **It has been done once**: dunes, mesas and needle spires (revision 12, `Game/WorldGen/Landforms.cs`) were added as
  their own layer.

## 3. Step 1 — tidy the terrain code (no visible change)

`TerrainSampler.Sample` (`Game/WorldGen/TerrainSampler.cs`) has grown into one long chain of
"if revision ≥ N then …" branches. Before adding landforms, split the height pipeline into clear stages:

```
continent → mountains → erosion → water (rivers, lakes, coast) → landforms → detail
```

Rules:
* **No output changes.** Every frozen terrain fingerprint (current and all earlier builds) must still pass, bit for bit.
  The full test suite must pass.
* Revision branches move into the stage they belong to, so it is clear what each stage does and which revisions it
  affects.
* `Game/WorldGen` stays free of Godot and of Thornwake's gameplay (it is meant to become a framework for other games
  too — `docs/GAME_VISION.md`).
* Measure terrain generation speed before and after; it must not get slower.
* Log it as a decision in `docs/DECISIONS.md`.

## 4. Step 2 — landforms as modules

Once the stages are clear, each landform becomes a **module** that plugs into its stage and can be added to or removed
from the generation process. Each module defines:

* **Where it fits** — a placement rule from the world's data (a fjord needs a mountain coast; a cirque needs high
  mountains; a waterfall needs a river crossing a step; an alluvial fan needs a stream leaving the mountains).
* **How it shapes the ground** — only inside its own area.
* **What it must respect** — **water always wins**: it never cuts a lake, blocks a river or punches into the sea. Each
  module gets an invariant test for that.
* **Its settings** — on/off and strength, chosen per world type (recipe) or in world creation, like biomes are today.

Three rules make modules safe:

1. **Each module has its own random seed stream**, so adding or removing one never shifts where anything else lands.
   (Without this, switching volcanoes off could move every mountain in the world.)
2. **A world remembers which modules it was made with** (saved with the world, like its parameters), so removing a
   module from the game never silently changes existing worlds; the frozen tests check it.
3. **Modules only work where they are**: placement is decided once (in the world plan), and each module knows its own
   areas, so 20 modules cost no more to walk around than 2.

Some shapes do **not** belong in the heightmap: a heightmap only goes up and down, so overhangs, natural arches, sea
arches, caves and hoodoo caps are **rock models placed on the terrain** (like today's 3D rock walls and boulders). The
module places them; the ground stays simple.

## 5. Step 3 — new landforms, a few at a time

* **Few, deliberate, in the right place**: each feature appears only where it makes geological sense, and rarely —
  then it reads as a landmark. Sprinkled everywhere it reads as noise.
* **One at a time**, each its own module with its own switch, a new terrain revision, an invariant test, and
  before/after screenshots for Tom to judge. Kept only if it looks right to him.
* Tom picks which ones (reference photos help most, as with Atmosphere 2.0). Choosing landforms is an artistic
  decision: ask him.

### Landform vocabulary (to pick from)

**Already in the world:** rolling hills, mountains with ridges, rivers with meanders, deltas and mouths, lakes, swamp
pools, cliffs and rocky shores, sand dunes, mesas, needle spires, erosion.

| Group | Landforms |
|---|---|
| Mountains, highlands | **cirque** (glacial bowl, often with a small lake — a *tarn*), **U-shaped valley**, **hanging valley** (side valley ending high on a cliff, with a waterfall), **pass / saddle**, **plateau**, **escarpment** (one long steep step), **scree / talus slopes**, **tors and rock outcrops**, volcanic **cones, calderas, lava fields** |
| Valleys, water | **canyon / gorge**, **ravine / gully**, **waterfalls and rapids**, **river terraces**, **oxbow lake**, **alluvial fan**, **fjord** |
| Coast | **sea stacks and arches** (rock models), **headlands and coves**, **spits, sandbars, lagoons**, **tidal flats** |
| Lowlands | **moraines, drumlins, eskers** (glacial ridges and mounds), **kettle lakes**, **karst** (sinkholes, caves, vanishing streams, springs), **hoodoos** (rock models), **salt flats** |
| Walking scale | rock ledges and overhangs (models), small bluffs, lone big boulders (*erratics*), hollows and mounds, stony patches, small gullies |

### What makes terrain good to play in (not only to look at)

* **Landmarks** to navigate by: a sea arch, a waterfall, a lone spire.
* **Routes and obstacles**: passes, gorges, escarpments — and with climbing (`docs/GAMEPLAY_DESIGN.md` §10) a cliff
  becomes a shortcut.
* **Hidden places**: caves, hanging valleys, cirques — natural spots for points of interest.
* **Sailing**: fjords, sea stacks, lagoons (§11).

## 6. How to proceed (for the local session)

1. Do step 1 (tidy-up) on its own; show Tom the stages and the passing tests. No visible change.
2. Design the module system (step 2) on paper and **show Tom before building it**.
3. Ask Tom which 3–5 landforms he wants first (vocabulary above, reference photos welcome).
4. Build them one at a time as in step 3.

Order relative to other work: after the current Atmosphere 2.0 task and the multiplayer spike
(`docs/GAMEPLAY_DESIGN.md` §14), unless Tom decides otherwise.

Prompt Tom can paste:

> Read `TERRAIN_LANDFORMS_PLAN.md` (from the cloud session). Before adding new landforms, tidy the terrain generation
> code: split TerrainSampler's height pipeline into clear stages (continent, mountains, erosion, water, landforms,
> detail) without changing any output — all frozen terrain fingerprints must still pass. Then design landforms as
> modules: each with a placement rule, a shaping function limited to its own area, water always wins, its own seed
> stream, settings per world type, saved with the world. Show me the design before building any new landform.
