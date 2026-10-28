# Landforms — the catalogue and the order we build them

*Started 2026-10-27 (D231). Tom picked 15 landforms from the illustrated menu (after talking it over with Noor) and
left the order to me; then added volcanoes and fjords ("the more landforms, the better — even some fictional,
fantasy-like"), so passes came back too and fantasy ideas have their own section. This file is the living list: what each landform is, where it appears, what it gives to play,
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
| 6 | Fjords | big | coast | large | 5 (U-shaped valleys reaching the sea) |
| 7 | Volcanoes and calderas | big | terrain, shape step | large | 4 (the crater lake is a small lake) |
| 8 | Scree slopes and big boulders | local | terrain | small | 1, 3, 4 (cliffs to fall from) |
| 9 | Sea stacks and arches | rock models | coast | medium | the rock model path (first use) |
| 10 | Headlands and coves | local | coast | medium | 9 (stacks sit off headlands) |
| 11 | Spits, sandbars and lagoons | local | coast | medium | 10 |
| 12 | Oxbow lakes and river terraces | local | rivers | medium | — |
| 13 | Alluvial fans | local | rivers | small | — |
| 14 | Moraines and kettle lakes | local | terrain | small | 4 (glacial country) |
| 15 | Salt flats | local | biome | small | — (pairs with a *Desert basin* shape later) |
| 16 | Hoodoos and tors | rock models | biome | medium | 9 |
| 17 | Passes and saddles | big | terrain | small | the roads stage (roads look for them) |
| 18 | Karst: sinkholes and springs | local | terrain | large | 2 (springs feed rivers) — **caves are separate**, below |

Then the fantasy landforms Tom picks (section at the end).

## The landforms

### 1. Escarpments and plateaus
* **What**: one long steep step between a lowland and a raised table-land; the top is a broad, gently rolling plateau.
* **Where**: across a region's grain, away from the coast and the big mountain chains; a world gets a few, each tens of
  kilometres long.
* **Play**: lookouts, routes up (ramps, ravines), natural walls; castle and watchtower sites on the edge (site hints).
* **Build**: a plan module raising a band of the macro raster with a sharp edge before erosion; rivers then cross it
  (and fall: no. 2) or run along its foot. The cliff face gets rock detail at walking scale.
* **Water**: rivers are routed after it; the face looks downhill so the lowland drains away from the foot.
* **Status**: built, terrain revision 18 (D232); broken up with ravines, buttresses and scree (D233); rebuilt like
  real escarpments — caprock cliff over gullied debris slopes, ravines of every size, scree texture (D234); in build
  116; Tom's dumps: a comb (mesas), fins at bends, worm-like rills — fixed in terrain revision 19 (D235), in build 117.

### 2. Waterfalls and rapids
* **Status**: built, terrain revision 20 (D238), not in a build yet: falls at knickpoints with a plunge pool, rapids
  below, a steep-walled gorge below each fall. Spray and white water wait for the water shader rework.
* **What**: a river drops over a rock step into a plunge pool; white water over a steep, rocky stretch.
* **Where**: where a river's course crosses a steep step (escarpment edges, mountain fronts, hanging valleys).
* **Play**: landmarks you hear before you see them; a river you cannot sail through; fish below the falls.
* **Build**: the river's surface steps down instead of sloping (the hydrology keeps a level per stretch); the falls and
  the white water are drawn by the water shader (spray, foam) — the visual side may wait for the water shader rework.
* **Water**: the river keeps flowing; the pool below is deeper, never dry.

### 3. Gorges and canyons
* **Status**: built, terrain revision 21 (D239), not in a build yet: antecedent canyons (raised land beside a few
  rivers), gorge walls with ledges for canyons and the gorges below waterfalls.
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

### 6. Fjords
* **What**: a long arm of the sea reaching far inland between steep mountain walls, deep water close to the shore.
* **Where**: mountain coasts in cold country, where deep U-shaped valleys (no. 5) reach the sea.
* **Play**: sailing deep into the mountains (GAMEPLAY_DESIGN §11), harbours at the fjord's head, waterfalls off the
  walls.
* **Build**: a plan module drowning glacial valleys at the coast: the valley floor drops below sea level and the sea
  follows it inland. The coast and sea stages must accept sea that far inside the land (an invariant test: the fjord
  is sea, joined to the open sea, never a lake).

### 7. Volcanoes and calderas
* **What**: a cone rising alone or in a chain, a crater at the top (a lake in it when quiet), lava-stone slopes and old
  flows around it; a caldera is a collapsed volcano — a wide ring of cliffs round a lake.
* **Where**: a few per world on fault lines, along mountain chains or alone; a *Volcanic island* land shape builds a
  world around one (a land-shape step, D228).
* **Play**: the landmark seen from everywhere; hot springs, obsidian, rich soil; danger later (smoke, heat).
* **Build**: a plan module raising cones before erosion (rivers radiate from them), the crater carved at walking
  scale; the crater lake is a small lake (no. 4). The volcanic biome already exists for the ground.

### 8. Scree slopes and big boulders
* **What**: fans of loose stone at the foot of cliffs; lone big boulders (erratics) on open land.
* **Where**: below steep rock (escarpments, gorges, cirques, mountain walls); erratics in glacial country and on moors.
* **Play**: hard footing, cover, landmarks; stone to gather.
* **Build**: a local module (scree ground) plus more and bigger boulders in the scatter.

### 9. Sea stacks and arches
* **What**: rock pillars and arches standing in the sea off steep coasts.
* **Where**: steep, exposed rocky coasts facing the open sea.
* **Play**: landmarks from land and sea; sailing hazards and passages.
* **Build**: the first **rock models** placed by a landform (models with collision, like boulders; placeholders first,
  art later).

### 10. Headlands and coves
* **What**: rocky points reaching into the sea, with sheltered beach coves between them.
* **Where**: rocky coasts; coves where softer ground meets the sea.
* **Play**: harbour and fishing-village sites (site hints), sheltered landings.
* **Build**: a coast module shaping the coastline at a few hundred metres, beaches in the coves; sea stacks
  (no. 9) stand off the points.

### 11. Spits, sandbars and lagoons
* **What**: a long sand ridge across a bay or a river mouth with calm, shallow water behind it.
* **Where**: flat, sandy coasts with a drift along the shore; near river mouths.
* **Play**: calm water for small boats and fishing, a walkable sand path.
* **Build**: a coast module laying a sand ridge just above the sea; the water behind stays sea (lagoon look by depth).

### 12. Oxbow lakes and river terraces
* **What**: crescent lakes from cut-off river loops; flat steps along the sides of wide river valleys.
* **Where**: oxbows on wide, meandering lowland rivers; terraces in broad valleys.
* **Play**: fishing ponds, flat building ground above the floods.
* **Build**: small lakes beside the meanders (uses the small-lake support of no. 4); terraces in the valley profile.

### 13. Alluvial fans
* **What**: gravel fans where a mountain stream spills onto a plain, its channel splitting.
* **Where**: at mountain fronts, especially in dry country.
* **Play**: gentle routes into the mountains; gravel and stones.
* **Build**: a local module building a cone at the mountain foot; the stream spreads over it.

### 14. Moraines and kettle lakes
* **What**: rolling ridges of old glacial debris and small round lakes between them.
* **Where**: below once-glaciated mountains and in cold lowlands.
* **Play**: lake country with small ponds, winding paths.
* **Build**: a local module of ridges and hollows; the hollows hold small lakes (no. 4's small lakes).

### 15. Salt flats
* **What**: dead-flat white plains in dry basins.
* **Where**: the lowest floors of dry, closed basins (deserts, steppe).
* **Play**: wide open views, salt to gather, mirages; pairs with a later *Desert basin* land shape.
* **Build**: a local module flattening basin floors; a salt crust ground look (Tom's call on the look).

### 16. Hoodoos and tors
* **What**: capped rock columns in dry lands; piles of rounded granite blocks on moors and hilltops.
* **Where**: hoodoos in badlands and dry canyons; tors on hilltops in cool, open country.
* **Play**: landmarks, cover, climbing.
* **Build**: rock models (after no. 9), placed by the biome; the needle spires stay as they are.

### 17. Passes and saddles
* **What**: the low gap between two peaks where a path crosses a range.
* **Where**: along mountain chains, where the ridge dips.
* **Play**: the way through the mountains; inns, toll posts and ambushes later.
* **Build**: lowers the ridge at chosen points before erosion; gives a site hint to the roads stage, which routes over
  passes.

### 18. Karst: sinkholes and springs (caves later)
* **What**: round hollows where limestone ground collapsed; streams that disappear and come out again as springs.
* **Where**: limestone country (a new biome trait), mostly hills.
* **Play**: hidden springs, holes to climb into, odd rivers.
* **Build**: sinkholes are a local module; a vanishing stream needs the hydrology to send water underground and back.
* **Caves are a project of their own**: the ground is a height field (one height per point), so a cave needs separate
  geometry — cave models first (an entrance into a short cave), real underground spaces much later. Planned with
  the cave work, not with this list.

## Fantasy landforms (ideas for Tom and Noor to pick)

Tom (2026-10-27): "even some fictional, fantasy-like". Ideas, not yet picked; looks are Tom's call. The ones a height
field cannot hold need models (marked *models*).

* **Floating islands** — rock islands hanging in the air above a valley, waterfalls pouring off them into mist
  (*models*, the biggest of these).
* **Giant's stairs** — basalt columns in hexagonal steps down to the sea or a river (real, but looks made).
* **Crystal fields** — clusters of giant crystals breaking out of the ground in a hollow, faintly glowing at night
  (*models*).
* **Titan bones** — a ridge that is the skeleton of something enormous: ribs as rock arches, a skull as a cliff
  (*models* on a ridge).
* **The great rift** — a chasm kilometres long and too deep to see the bottom, crossed by natural bridges (needs the
  cave/underground work for its depths).
* **Sky arches** — natural rock bridges spanning a whole valley far above the river (*models*).
* **Stone forest** — a field of tall, thin rock blades close together, a maze to walk through (like the needle
  spires, packed).
* **Mirror lakes** — perfectly round lakes in perfectly round basins, with a ring of standing stones (a crater lake
  that is too regular).
* **Weeping cliffs** — a cliff wall with dozens of thin waterfalls coming out of the rock face (springs; after
  waterfalls).
* **Sunken forest** — a lake with the dead trunks of a drowned forest standing in it (lake + standing trunks).
* **Shattered plateau** — a plateau broken into blocks by straight, narrow chasms, like a cracked plate (after
  escarpments and gorges).
* **Upside-down mountain** — a mountain balanced on its tip over a lake, held by roots or magic (*models*; pure
  fantasy).

## Changes to this list

Add a row and a section when Tom picks a new landform; mark a landform's status (next / in build N / kept / dropped)
when it ships.
