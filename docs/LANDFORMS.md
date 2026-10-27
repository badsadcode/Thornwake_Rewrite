# Landforms — the catalogue and the order we build them

*Started 2026-10-27 (D231). Tom picked 15 landforms from the illustrated menu (after talking it over with Noor) and
left the order to me. This file is the living list: what each landform is, where it appears, what it gives to play,
how it is built and where it stands. Update the status line of a landform when its build ships.*

Related: the module frame and its rules — `docs/WORLDGEN_MODULES_DESIGN.md` (D228, D229); the overall plan —
`docs/WORLDGEN_PLAN.md`; the original brainstorm — `docs/TERRAIN_LANDFORMS_PLAN.md`.

## Rules every landform follows

* **Its own switch** in *New world → Landforms*: amount (off · rare · normal · common · everywhere) and size, for every
  world type.
* **A new terrain revision** when it arrives: worlds made before it keep their ground (they record their revision).
* **Water wins**: it never fills a lake, blocks a river or rises out of the sea — one invariant test per landform.
* **Its own random stream**: switching it off moves nothing else.
* **Shown before it stays**: each comes in its own build with before/after captures and a *Go to* launcher; kept only
  if Tom likes it. Looks (shapes, rock colours, models) are Tom's call.
* **Three kinds** — *big* landforms shape the world plan before erosion and rivers (rivers then react to them);
  *local* landforms shape the ground at walking scale before the water stages (water carves through them); *rock
  models* are placed objects for shapes the ground cannot hold (arches, overhangs, capped columns).

## The order

Chosen so each landform builds on the ones before it (a waterfall needs a step to fall over, scree needs cliffs above
it, hoodoos reuse the rock models of the sea stacks), with the ones that give most to play early.

| # | Landform | Kind | Placed by | Work | Builds on |
|---|---|---|---|---|---|
| 1 | Escarpments and plateaus | big | terrain | medium | — (the first big landform: proves the plan hook) |
| 2 | Waterfalls and rapids | big + local | rivers | medium | 1 (rivers crossing an escarpment) |
| 3 | Gorges and canyons | big | rivers | medium | 1, 2 |
| 4 | Cirques with a tarn | big | terrain | medium | small mountain lakes in the hydrology |
| 5 | Hanging valleys | big | terrain | large | 2, 4 |
| 6 | Scree slopes and big boulders | local | terrain | small | 1, 3, 4 (cliffs to fall from) |
| 7 | Sea stacks and arches | rock models | coast | medium | the rock model path (first use) |
| 8 | Headlands and coves | local | coast | medium | 7 (stacks sit off headlands) |
| 9 | Spits, sandbars and lagoons | local | coast | medium | 8 |
| 10 | Oxbow lakes and river terraces | local | rivers | medium | — |
| 11 | Alluvial fans | local | rivers | small | — |
| 12 | Moraines and kettle lakes | local | terrain | small | 4 (glacial country) |
| 13 | Salt flats | local | biome | small | — (pairs with a *Desert basin* shape later) |
| 14 | Hoodoos and tors | rock models | biome | medium | 7 |
| 15 | Karst: sinkholes and springs | local | terrain | large | 2 (springs feed rivers) — **caves are separate**, below |

Not picked for now: passes and saddles (the roads stage will find natural passes anyway), volcanoes and calderas (with
a *Volcanic island* land shape), fjords (with sailing).

## The landforms

### 1. Escarpments and plateaus
* **What**: one long steep step between a lowland and a raised table-land; the top is a broad, gently rolling plateau.
* **Where**: across a region's grain, away from the coast and the big mountain chains; a world gets a few, each tens of
  kilometres long.
* **Play**: lookouts, routes up (ramps, ravines), natural walls; castle and watchtower sites on the edge (site hints).
* **Build**: a plan module raising a band of the macro raster with a sharp edge before erosion; rivers then cross it
  (and fall: no. 2) or run along its foot. The cliff face gets rock detail at walking scale.
* **Water**: rivers are routed after it; a lake never straddles the edge.
* **Status**: next.

### 2. Waterfalls and rapids
* **What**: a river drops over a rock step into a plunge pool; white water over a steep, rocky stretch.
* **Where**: where a river's course crosses a steep step (escarpment edges, mountain fronts, hanging valleys).
* **Play**: landmarks you hear before you see them; a river you cannot sail through; fish below the falls.
* **Build**: the river's surface steps down instead of sloping (the hydrology keeps a level per stretch); the falls and
  the white water are drawn by the water shader (spray, foam) — the visual side may wait for the water shader rework.
* **Water**: the river keeps flowing; the pool below is deeper, never dry.

### 3. Gorges and canyons
* **What**: a river cut deep and narrow where it crosses high ground, walls of rock on both sides.
* **Where**: where a river crosses a plateau or a range (it was there first and cut down as the land rose).
* **Play**: obstacles and bridge sites; roads go round or across at a narrow point; climbing (GAMEPLAY_DESIGN §10).
* **Build**: rivers crossing high ground get a steep, narrow valley profile instead of the slope-limited one; mesas'
  canyons already show the look.

### 4. Cirques with a tarn
* **What**: a bowl carved high on a mountainside, steep back wall, a small lake (tarn) in its floor.
* **Where**: high on mountain flanks, mostly facing away from the sun (north in the northern half of the world).
* **Play**: hidden places, cold clear water high up, the source of a stream.
* **Build**: a plan module carving bowls into the mountains; the hydrology learns **small lakes** (today a lake needs
  ~2 km², 8 m depth and a share of the land — Tom's question about mountain ponds); the tarn spills into a stream.

### 5. Hanging valleys
* **What**: a side valley that ends high on the wall of a deeper main valley, its stream falling as a waterfall.
* **Where**: where a side valley joins a deep mountain valley (glacial country, with no. 4).
* **Play**: a waterfall landmark, a hidden valley above it.
* **Build**: deepens main mountain valleys into a U shape and leaves side valleys at their old level; the join is a
  waterfall (no. 2). The largest of the river-related landforms.

### 6. Scree slopes and big boulders
* **What**: fans of loose stone at the foot of cliffs; lone big boulders (erratics) on open land.
* **Where**: below steep rock (escarpments, gorges, cirques, mountain walls); erratics in glacial country and on moors.
* **Play**: hard footing, cover, landmarks; stone to gather.
* **Build**: a local module (scree ground) plus more and bigger boulders in the scatter.

### 7. Sea stacks and arches
* **What**: rock pillars and arches standing in the sea off steep coasts.
* **Where**: steep, exposed rocky coasts facing the open sea.
* **Play**: landmarks from land and sea; sailing hazards and passages.
* **Build**: the first **rock models** placed by a landform (models with collision, like boulders; placeholders first,
  art later).

### 8. Headlands and coves
* **What**: rocky points reaching into the sea, with sheltered beach coves between them.
* **Where**: rocky coasts; coves where softer ground meets the sea.
* **Play**: harbour and fishing-village sites (site hints), sheltered landings.
* **Build**: a coast module shaping the coastline at a few hundred metres, beaches in the coves.

### 9. Spits, sandbars and lagoons
* **What**: a long sand ridge across a bay or a river mouth with calm, shallow water behind it.
* **Where**: flat, sandy coasts with a drift along the shore; near river mouths.
* **Play**: calm water for small boats and fishing, a walkable sand path.
* **Build**: a coast module laying a sand ridge just above the sea; the water behind stays sea (lagoon look by depth).

### 10. Oxbow lakes and river terraces
* **What**: crescent lakes from cut-off river loops; flat steps along the sides of wide river valleys.
* **Where**: oxbows on wide, meandering lowland rivers; terraces in broad valleys.
* **Play**: fishing ponds, flat building ground above the floods.
* **Build**: small lakes beside the meanders (uses the small-lake support of no. 4); terraces in the valley profile.

### 11. Alluvial fans
* **What**: gravel fans where a mountain stream spills onto a plain, its channel splitting.
* **Where**: at mountain fronts, especially in dry country.
* **Play**: gentle routes into the mountains; gravel and stones.
* **Build**: a local module building a cone at the mountain foot; the stream spreads over it.

### 12. Moraines and kettle lakes
* **What**: rolling ridges of old glacial debris and small round lakes between them.
* **Where**: below once-glaciated mountains and in cold lowlands.
* **Play**: lake country with small ponds, winding paths.
* **Build**: a local module of ridges and hollows; the hollows hold small lakes (no. 4's small lakes).

### 13. Salt flats
* **What**: dead-flat white plains in dry basins.
* **Where**: the lowest floors of dry, closed basins (deserts, steppe).
* **Play**: wide open views, salt to gather, mirages; pairs with a later *Desert basin* land shape.
* **Build**: a local module flattening basin floors; a salt crust ground look (Tom's call on the look).

### 14. Hoodoos and tors
* **What**: capped rock columns in dry lands; piles of rounded granite blocks on moors and hilltops.
* **Where**: hoodoos in badlands and dry canyons; tors on hilltops in cool, open country.
* **Play**: landmarks, cover, climbing.
* **Build**: rock models (after no. 7), placed by the biome; the needle spires stay as they are.

### 15. Karst: sinkholes and springs (caves later)
* **What**: round hollows where limestone ground collapsed; streams that disappear and come out again as springs.
* **Where**: limestone country (a new biome trait), mostly hills.
* **Play**: hidden springs, holes to climb into, odd rivers.
* **Build**: sinkholes are a local module; a vanishing stream needs the hydrology to send water underground and back.
* **Caves are a project of their own**: the ground is a height field (one height per point), so a cave needs separate
  geometry — cave models first (an entrance into a short cave), real underground spaces much later. Planned with
  the cave work, not with this list.

## Changes to this list

Add a row and a section when Tom picks a new landform; mark a landform's status (next / in build N / kept / dropped)
when it ships.
