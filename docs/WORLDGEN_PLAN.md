# World generation: tidy-up, modules, settlements and roads — the plan

*2026-10-27, after build 114. Tom's `TERRAIN_LANDFORMS_PLAN.md` (cloud brainstorm, 2026-10-13/14) checked against the
code as it is now, with the settlement and road stages added as agreed (`GAMEPLAY_DESIGN.md` §14.a, step 4). Planning
only: nothing is built yet.*

## Where the code stands

* **One long function.** `TerrainSampler.Sample` (`Game/WorldGen/TerrainSampler.cs`, ~230 lines of an 835-line file) does
  everything for a point: coast, macro height, detail, biome terrain, landforms, rivers, lakes, pools, player edits.
  It carries **20 revision branches** (revisions 9–16), each "if revision ≥ N then …" in the middle of the flow.
* **The landforms that exist** (dunes, mesas, needle spires, revision 12) are also methods of `TerrainSampler`, switched
  on by the biomes' terrain features — not separate pieces.
* **The plan side is already split well:** `WorldPlan` builds the layout, the macro raster (land, height, mountains —
  erosion happens here, at plan time), hydrology (rivers, lakes), climate and biomes once per world.
* **Safety net in place:** current revision 16; 11 frozen fingerprint sets (builds 5 … 52) that any change must still
  match bit for bit; invariant tests for water (no sea in a lake, no trench in a lake, no dry ground under a lake).

One correction to the brainstorm's stage order: in the code the **water comes last** — detail and landforms are laid
down first and rivers and lakes then carve through them (canyons through mesas). That is exactly what makes "water
always wins" work, so it stays:

```
coast & continent → mountains → detail → biome terrain & landforms → sites → roads → water → player edits
```

(erosion stays where it is, in the plan's macro raster.)

## Step 1 — tidy-up, no visible change

* Split `Sample` into one method per stage, passing a small state (land, base height, detail, water…) between them.
  Code moves, it is not rewritten: the arithmetic stays in the same order so every frozen world matches bit for bit.
* Each revision branch moves into its stage; a short table at the top of the file says which revision changed which
  stage.
* Dunes, mesas and spires move out of `TerrainSampler` into their own landform classes (still behind revision 12).
* Measure generation speed before and after (`HeightGridParallel` over a fixed set of tiles); it must not get slower.
* `Game/WorldGen` stays free of Godot and gameplay.
* What you see: nothing changes in the game. I report the stages, the passing tests and the timings.

## Step 2 — landforms as modules (design first, shown to you before building)

Each landform becomes a module with: **where** it may appear (a rule from the world's data), **how** it shapes the
ground (only inside its own areas), **what it respects** (water always wins: it never cuts a lake, blocks a river or
punches into the sea — one invariant test each), and **its settings** (on/off, strength, per world type and in New world).
Three rules keep it safe:

1. **Its own random stream** (seed mixed with the module's id): switching one off never moves anything else.
2. **The world remembers its modules** (saved with the world like its parameters): removing a module from the game never
   changes an existing world.
3. **Placed once, in the plan**: each module knows its areas, so 20 modules cost no more per point than 2.

Shapes a heightmap cannot hold — arches, overhangs, sea stacks, caves, hoodoo caps — are **rock models** the module
places on the ground, like today's boulders.

The module pipeline arrives as **terrain revision 17**: dunes, mesas and spires become the first three modules, and
revision 17 with the same settings is proved identical to 16 by test. Older revisions keep their old path.

## Step 3 — settlement and road stages, planned in from the start

Both stages exist in revision 17 but are **empty** (no change to the ground), so filling them later is a clean new
revision:

* **Sites** — village, town and castle places chosen once in the plan from the world's data (coasts, river crossings,
  passes, good land, spacing). Each site grades a level pad with soft edges. Water wins: a pad never fills a lake or a
  river.
* **Roads** — routed once in the plan between sites over the real terrain (easy gradients, fords and bridges, tracks and
  roads to nowhere). Each road cuts and fills a gentle corridor. Rivers keep their channel (a ford or a bridge spot).

Then **settlements and roads as placeholders** (the next step in our order): marker houses on the pads, the roads
visible, later premade settlements from the World Site Editor.

## Step 4 — new landforms, one at a time

You pick which ones (3–5 to start; reference photos help). Each comes as its own module with a switch, a new terrain
revision, an invariant test and before/after screenshots, kept only if you like it. From the brainstorm's list, the ones
that would add most to play: **waterfalls and rapids**, **gorges / canyons**, **cirques with a small lake**,
**escarpments and plateaus**, **sea stacks and arches** (models).

## Order and checkpoints

1. Tidy-up (step 1) → I show you the stages, tests and speed. No build needed (nothing visible).
2. Module design on paper → **you approve** before I build it.
3. Revision 17: modules + empty site and road stages → a build where nothing looks different (proved by tests).
4. You choose the first landforms → one per build.
5. Settlement sites, then roads, as placeholders.

## Tom's answers (2026-10-27)

* **Landforms before settlements** — sites will be chosen partly by landforms (a village on a plateau, a castle on an
  escarpment, a harbour in a fjord), roads route round gorges and through passes.
* **Switches in New world, for every world type** — "high customization": each landform's switch and strength in
  *New world* before generating. **World types get reworked too** (Azgaar's map generator as a reference): a world
  type becomes a starting preset of those switches and of its landform template. A step of its own, with step 2.
* **Which landforms first** — open. I bring a short illustrated menu (captures or reference images) when we get there.

## Tom's ideas for later (2026-10-27)

* **Speed is not critical**: ~1 % slower terrain sampling is fine (it only affects building terrain tiles on background
  threads and loading, never the frame rate).
* **Loading messages by stage**: while a world is planned, funny lines for the stage under way — debating where the
  mountains go, flooding the valleys while rivers and lakes form — a few random sets per stage (layout, continents and
  mountains, erosion, rivers and lakes, climate, biomes). Talked over with Noor; to design later. The plan already runs
  in these stages, so each can report its name and progress.
* **Interesting sea and lake beds**: not a smooth slope going deeper and deeper — underwater ravines and canyons,
  ridges, shelves and drop-offs. A landform module of the sea and lake stages (same rules: its own switch, a new terrain
  revision, old worlds unchanged). Later, after the first landforms.

## Step 1 — done (D227)

`TerrainSampler.Sample` now walks nine short stages (coast, sea, continent, detail, biome terrain, rivers, lakes, pools,
player edits), each taking and giving plain values; the water stages and their helpers live in
`TerrainSampler.Water.cs`, the revision-12 landforms in `BiomeLandforms.cs`; a table at the top of `TerrainSampler.cs`
says which terrain revision changed which stage. Proof: every field of every sample identical over 110 cases (5 world
types × 2 seeds × 11 terrain versions, 4 detail levels, `--sample-hash`), all 11 frozen fingerprint sets and the full
suite pass. Speed: 1 172 ns per sample vs 1 158 before (~1 %, `--speed`).

## Step 2 — designed (D228)

On paper in `docs/WORLDGEN_MODULES_DESIGN.md`: big and local landform modules, placement rules, the *Landforms* group
in New world, world types split into land shape + landforms + climate + parameters, revision pinning in saves, and the
hooks for loading messages, sea and lake beds, sites and roads. Waiting for Tom's approval.

## Revision 17 — done (D229)

The module frame with nothing visible: dunes, mesas and spires as modules with amount and size in *New world →
Landforms*, the plan hook for big landforms, empty site and road lists, stage names on the loading overlay, and worlds
that record their terrain revision (saves before it load as 16; *Terrain version* has *Build 114*). Proved identical to
16 at the designed settings.

## Questions for you (answered above)

* Is this order right (landforms before settlements), or should sites and roads come first?
* Should the module switches show in **New world** (a *Landforms* section), or only per world type?
* Which landforms first? (Can wait until step 4.)
