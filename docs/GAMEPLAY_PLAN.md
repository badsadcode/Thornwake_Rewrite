# First gameplay prototype (planned, after the dedicated repo) and the road to a complete world

Tom, 2026-10-08: "after that we need to introduce some basic game mechanics… proper swimming, sailing and survival
elements (not a final system but something to check how it could look). Maybe chopping trees, fishing, building a
campfire and cooking fish. Hunting would be nice but I don't have animal assets yet."

**A proof of concept, not the final survival system** (Tom: "just some basic mechanics"). It is a playable loop
to judge the feel. Code stays simple and separate, so it can be thrown away or rebuilt when the proper mechanics come. Everything goes in `Game/Rewrite/`, behind the existing
world. In multiplayer later, all world changes (felled trees, fires) become edits keyed by world position, the same
way dug terrain is, so this layout already suits it.

## Order (each step is one build Tom can play)
1. **Survival basics**: health, hunger, warmth, stamina on the HUD. Warmth comes from the real air temperature,
   wind and rain (Weather/Wind already know them), and being wet. A small inventory and a hotbar, reusing the ideas of the
   original game's hotbar (`Game/UI/HotbarControls.cs`), not its code.
2. **Proper swimming**: today's swim is a flying mode under water. Replace it with buoyancy, a surface stroke and a dive,
   breath, stamina drain, slower in currents (river current profile, D-rivers) and pushed by waves (the sea state
   already gives wave height at a point). Wet clothes make you colder afterwards.
3. **Chopping trees**: an axe tool. Trees are world data (deterministic per tile), so a felled tree is stored as an edit
   (tile, tree index) and removed from collision and rendering. The tree falls (a simple rigid body tipping along
   the cut), leaves a stump and gives logs and branches.
4. **Campfire**: place stones and wood. Fire and smoke particles bent by the wind map, light that flickers, warmth
   nearby. Rain weakens it, and heavy rain puts it out unless sheltered.
5. **Fishing**: a rod. Cast, the float bobs on the real water surface, wait, tug, catch. Fish depend on the water kind
   (sea, lake, river, pond), the time of day and the weather. Placeholder fish meshes.
6. **Cooking**: raw fish on the fire, cooked after a time, burnt if left. Eating restores hunger. Raw fish risks sickness.
7. **Sailing**: a small boat (raft or rowing boat first, then a sail). It floats on the sea and lake waves using the
   same wave function the water draws, so the boat rides what you see, and the regional wind drives the sail.
   This is the biggest step, so it comes last.

**Hunting** waits for animal assets. The survival needs and the inventory are designed so that meat and hides slot in later.

## Not yet
Saving progress, crafting trees, building, multiplayer sync.

## Roadmap after the prototype (Tom, 2026-10-08)
The world comes first, then the real game systems:
1. **Mechanics proof of concept** (above).
2. **Roads and settlements** (Tom: "for now just symbolic villages… generating villages/towns/castles is probably
   not the best idea if I want this to look good"). The world generates **sites**, not towns. Each site has a type
   (village, town, castle, camp, …), size, facing and exits, placed where people would settle: water, flat land,
   coasts, river crossings, passes. At first a site is symbolic: a levelled pad, a few marker houses and the roads.
   Later a **premade settlement** (hand-built, with its own terrain and road exits) is set into the site. Proposal
   for making that look natural:
   * The premade's terrain is stored **relative** (pads, terraces, ditches as height offsets plus a blend mask), not
     as absolute heights, and is blended out over a margin into the real ground.
   * **Matching, not just pasting**: each candidate site scores every premade of its type, rotated and mirrored,
     against the real slope and shape (does the hill fit the castle?), and keeps the best. Sites that fit nothing
     are dropped.
   * **Grown outskirts** hide the stamp's edge: fields, fences, orchards, paths, scattered farms, woodpiles and
     cleared or trampled ground, generated around the core from the terrain and biome.
   * **Variation inside a premade**: optional building slots (any of a few houses, empty or ruined), biome material
     swaps (thatch, stone, timber), so two villages of the same layout differ.
   * **Roads** leave by the premade's exits and are routed over the real terrain to other sites (easy gradients,
     fords, bridges), plus tracks and **roads to nowhere** (abandoned, overgrown, leading to ruins or nothing).
   * All of it is deterministic from the world seed, like the terrain, so every player gets the same map. The
     original game's settlement notes (MILESTONES §90, WORLD_GENERATION_VISION) are ideas only, not code.
   Premade settlements and POIs are made in the **World Site Editor** (`docs/WORLD_SITE_EDITOR_PLAN.md`).
3. **Premade assets in the world**: ruins, points of interest and other authored pieces placed by rules (biome,
   terrain, distance from roads and towns), terrain graded under them.
4. **The complete world**, then the proper game: final mechanics, NPCs and the other planned systems.
