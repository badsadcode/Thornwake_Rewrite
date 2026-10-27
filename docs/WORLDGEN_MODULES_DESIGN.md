# Landform modules and the world types rework — design (step 2, on paper)

*2026-10-27, D228. For Tom to approve before anything is built. Follows `WORLDGEN_PLAN.md` step 2 and Tom's answers
(landforms before settlements; landform switches in New world for every world type; world types reworked, Azgaar's
map generator as a reference).*

## What exists today (read from the code)

* **Three kinds of world types, 23 in all** (`WorldRecipe.Catalog`):
  * *placed* (6): continents and islands as placed blobs (Balanced, Great mainland, Archipelago, Mountain kingdoms,
    Many small continents, Islands + mainland, + the classic Vast world);
  * *template* (5, D36): a short list of geometric steps — Raise, Lower, Basin, Range, Trough, Strait, Islands, Mask
    (Inland sea, Shattered lands, Rift continent, Twin continents, Great peninsula);
  * *natural* (11, D38): heightmap templates grown cell by cell, already built on Azgaar's idea (Hill, Pit, Range,
    Trough, Strait, Mask, Add, Multiply, Smooth, Invert).
* **New world** has ~45 parameters in groups (World shape, Terrain, Rivers and lakes, Forests and objects, Climate and
  biomes, Biomes in this world); a world type may carry its own parameter values (saved custom types).
* **Landforms in the ground**: dunes, mesas and needle spires (revision 12) — switched on by the biomes
  (`biomes.json`, `Terrain.Features`), not by the world or the player.
* **A world is rebuilt from its arguments** (`--preset=… --seed=…`, plus every `--set=gen.…` value when customised);
  saves store those arguments.

**A gap found while reading:** the arguments never record the terrain revision. "Terrain version: current" means
*whatever the game's current revision is*, so a world saved in build 52 (revision 15) already loads as revision 16
today, and would load as 17 once that ships. The fix belongs to revision 17's build (below): every new world records
its revision (`--set=gen.revision=17`), and a save without it is read as revision 16 (the last revision before the
fix). From then on no save ever changes its terrain.

## 1. Two kinds of landform modules

Landforms differ in scale, and that decides where they must run:

| | **Big landforms** (plan modules) | **Local landforms** (ground modules) |
|---|---|---|
| Size | kilometres: the shape of a region | metres to a few hundred metres |
| Examples | plateaus and escarpments, calderas/volcanoes, rift valleys, cirques, gorges | dunes, mesas, spires, hoodoos, karst towers, boulder fields, sea stacks |
| Runs | once, in the plan, on the macro raster **before erosion and rivers** | per point in `TerrainSampler`, in the biome-terrain stage (stage 3), **before the water stages** |
| Water | rivers and lakes are planned *after* them, so rivers flow round, cut through (a gorge through a plateau) or fill them (a caldera lake) | the water stages carve through them afterwards (canyons through mesas) — as today |

Both kinds follow the same five rules (`WORLDGEN_PLAN.md`):

1. **Where** — a placement rule from the world's data (see the table below).
2. **How** — shapes the ground only inside its own areas; outside them it costs nothing and changes nothing.
3. **Water wins** — never fills a lake, blocks a river or rises out of the sea; one invariant test per module.
4. **Its own random stream** — `Hashing.FeatureSeed(seed, …, "landform." + id)`: switching one module off never moves
   anything else.
5. **Remembered by the world** — its switch and strength are part of the world's arguments; a module removed from the
   game, or retuned, never changes an existing world (retuning = a new terrain revision).

### Placement rules

| Rule | Reads | Examples |
|---|---|---|
| **Biome** | the biome's `Terrain.Features` strength (as today) × the module's world setting | dunes in deserts, mesas and spires in badlands, karst towers in tropical karst |
| **Terrain** | the plan's rasters: height, slope, mountain share, distance to coast | escarpments on the edge of high ground, cirques high on mountain flanks, boulder fields below cliffs |
| **Water** | the plan's hydrology: river courses, catchment, lakes | gorges where a river crosses a range, waterfalls and rapids at steep river steps |
| **Coast** | coast distance, shore steepness, exposure to the open sea | sea stacks and arches on steep exposed coasts, coves |
| **Shape step** | a step of the world type's land shape (section 3) | "Volcano 1 at 40 60", "Plateau 2-3 in the north" |

### Inside the game (technical sketch)

```
LandformModule
  Id, Name, Kind (Plan | Ground), Since (first terrain revision)
  Settings: Amount (0 = off … 1 = as designed … 3), Size (0.5 … 2)
  Place(plan, settings, seed) → Areas        // once per world, in the plan
  Shape(point, area, height) → height         // only inside its areas
  Models(area) → rock models                  // arches, overhangs, hoodoo caps, sea stacks (optional)
  Hints(area) → site hints                    // "plateau top", "gorge crossing", "cove" (for the sites stage)
```

* The plan keeps a **coarse grid of areas** (cells of ~2 km); a point asks its cell which areas reach it, so a world
  with 20 modules samples as fast as one with 2 (only the modules present at that spot run).
* **Rock models** for shapes a heightmap cannot hold (arches, overhangs, sea stacks, caves' mouths, hoodoo caps) are
  placed like today's boulders, with collision; the module places them, the art comes later (placeholders first).
* **Site hints** are how landforms reach settlements: a plateau top offers a castle site, a cove a harbour, a gorge a
  bridge; the sites stage (step 3) weighs them with its own rules.

### Revision 17: the frame, nothing visible

* Dunes, mesas and spires become the first three *ground modules* (Biome rule), with the module settings at 1.
  Proven by test: revision 17 with every module at 1 is bit-identical to revision 16 (the `--sample-hash` probe of
  step 1 over all world types and seeds).
* The empty **Sites** and **Roads** stages are put in place (no change to the ground), plus the plan-module hook
  before erosion (empty).
* Revision pinning in the world arguments (the gap above), and *Terrain version* gets "Build 114" (revision 16) so
  today's worlds stay selectable.
* New world gains the **Landforms** section (section 2) with the three modules.
* Old revisions keep their old path untouched.

## 2. Landforms in New world

A new group **Landforms** in New world, for every world type, one row per module:

```
Landforms                                  amount            size
  Dunes            (in deserts)       [ off · rare · normal · common · everywhere ]   [ 0.5× … 2× ]
  Mesas & canyons  (in badlands)      [ off · rare · normal · common · everywhere ]   [ 0.5× … 2× ]
  Needle spires    (in badlands)      [ off · rare · normal · common · everywhere ]   [ 0.5× … 2× ]
  …each new module adds a row
```

* **Amount** — how often it appears where it may (*off* removes it; *everywhere* fills every place its rule allows).
  For Biome modules it multiplies the biome's own strength.
* **Size** — how big and high each one is.
* The note in brackets says where it can appear, so a switch that does nothing in a world (dunes with deserts turned
  off) is explained rather than silent; the map preview shows the result before generating, as for the other
  parameters.
* Settings are ordinary `gen.landform.<id>.amount` / `.size` values: they travel in the world arguments, in saves, in
  multiplayer and in saved custom world types like every other parameter, and a world type may set its own defaults.

## 3. World types rework

Azgaar's generator separates **the land's shape** (a heightmap template: Volcano, Atoll, Archipelago, Continents,
Mediterranean, Peninsula, Pangea, Isthmus, Shattered, Taklamakan, Old World, Fractious…) from everything else. Our
natural types already copy that idea for the shape, but each type bundles its shape with fixed parameters. The
rework splits a world type into four parts the player can mix:

| Part | What it holds | In New world |
|---|---|---|
| **Land shape** | the heightmap template (natural steps), or a classic placed/template layout | a *Land shape* list; *Edit steps* for players who want to write their own |
| **Landforms** | module amounts and sizes | the *Landforms* group |
| **Climate** | latitude, warmth, wetness, biome layout and biome toggles | the *Climate and biomes* groups (as today) |
| **Parameters** | terrain, rivers, forests (as today) | the other groups (as today) |

A **world type** becomes a named starting preset of all four — choosing it fills the four parts, and the player may
change any of them. Saved custom world types already work this way for parameters; they gain the shape and the
landforms.

### New shapes and types (all new, nothing existing changes)

New natural land shapes after Azgaar's list, with the heightmap steps we have (plus two new steps where needed):

| Shape | The land | New step needed |
|---|---|---|
| **Volcanic island** | one high island built round a volcano, small islets | `Volcano` (cone with crater; later a caldera module) |
| **Atoll ring** | a ring of low islands round a lagoon in open ocean | `Ring` |
| **Pangea** | one supercontinent filling most of the world, inland deserts | — |
| **Isthmus** | two landmasses joined by a narrow land bridge | — |
| **Old world** | one long continent of many regions, broken coasts | — |
| **Fractious** | many medium landmasses of very different sizes | — |
| **Desert basin** | a vast dry basin ringed by mountains (Taklamakan) | — |
| **Inland sea**, **Continents**, **Mainland**… | the existing natural shapes, as they are | — |

Each also becomes a world type preset that fits it: *Desert basin* = its shape + hot dry climate + dunes common;
*Volcanic island* = its shape + warm wet climate + the volcano. (Climate and landform defaults per new type are an
artistic choice: I'll bring the list with map previews for you to tune before they ship.)

### The list in New world

```
Natural          Continents · Mainland · Archipelago · Mountain kingdoms · … · Volcanic island · Atoll · Pangea · …
Classic          the placed and template types, exactly as today
Your types       saved custom types
```

The existing 23 types keep their ids, steps and parameters, so every existing world and every Reproduce line stays the
same; only the list's grouping changes.

### Edit steps (later)

Azgaar's template editor in our form: the shape's steps as a short list (add, remove, reorder, change a value) with the
map preview updating as you edit; saved with a custom type. A step of its own after the new shapes.

## 4. Hooks for what comes later

* **Loading messages by stage** — the plan reports `(stage, progress)` as it goes: layout, land shape, big landforms,
  erosion, rivers and lakes, climate, biomes, local landforms, sites, roads. The generation overlay shows the stage
  name now (it says "continents → erosion → rivers and lakes" today) and later a random funny line per stage from a
  data file (`Content/loading_messages.json`, written with Noor).
* **Interesting sea and lake beds** — two more ground-module places: the sea stage (1b) and under a lake's surface in
  the lakes stage. Bed modules (underwater ravines, ridges, shelves, drop-offs) follow the same rules, with "water wins"
  read as: a sea bed never rises to the surface (no surprise shoals), a lake bed stays at least its minimum depth below
  the lake's level.
* **Sites and roads** — read the modules' site hints and avoid their hard areas (cliffs, gorges) unless a module offers
  a crossing.

## Order of work (each step its own build and approval)

1. **Revision 17 frame** (section 1, "nothing visible"): modules, Landforms group with the three existing landforms,
   empty sites/roads stages, plan hook, revision pinning, loading stage names. Tests prove identity with revision 16.
2. **World types rework, part 1**: the four-part split and the grouped list (no new shapes yet — existing types
   unchanged).
3. **First new landforms** — you choose from an illustrated menu (waterfalls and rapids, gorges, cirques with a lake,
   escarpments and plateaus, sea stacks and arches…), one per build.
4. **New land shapes** (Volcanic island, Atoll, Pangea, …) with map previews for you to tune.
5. **Sites, then roads**, as placeholders.
6. Later: Edit steps, sea and lake beds, loading messages.

## Questions for you

* **Two sliders per landform** (amount and size), or one simpler *strength*?
* **Order:** world types rework (step 2 above) before the first new landforms, or the landforms first?
* **New shapes:** which from the list interest you most (or none for now)?
