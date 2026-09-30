# THORNWAKE MASTER REWORK PLAN
## World Generation, World Content Tools, Characters, Camera, and Integration of the Existing Playable Game

---

## 0. Purpose

This document defines the next major development phase for Thornwake.

The goal is **not to restart the game**.

The goal is to preserve the systems that already work, build the missing foundations underneath and around them, and then adapt the current playable game to those foundations in controlled stages.

The current playable game already proves important parts of the basic loop:

- the player can move through the world;
- trees, stones, and berry bushes can be harvested;
- resources can be crafted;
- multiple crafting benches and stations exist;
- cooking stations exist;
- houses and structures can be built;
- terraforming already exists in an early/WIP form;
- roads and existing buildings can use protected terrain areas;
- the game already has enough working systems that destructive rewrites would create unnecessary risk.

This working state should be preserved as a baseline before the rework progresses.

The rework should be performed incrementally. Existing systems should be reused, adapted, wrapped, or extended whenever practical instead of being replaced simply because this document describes a cleaner long-term architecture.

The intended order is:

1. Preserve the existing playable baseline.
2. Build the new World Generation foundation.
3. Build hydrology and natural cave support as part of that foundation.
4. Build the World Content Editor and its related authoring tools.
5. Build the modular Character System.
6. Build the new camera and control model.
7. Build a shared world-targeting / interaction layer.
8. Adapt existing harvesting, crafting, building, stations, cooking, and terraforming to the new foundations.
9. Continue expanding gameplay after those foundations are stable.

---

# 1. Thornwake's Core Principle Remains the Same

Thornwake is intended to become a **world to live in**, not simply a sequence of quests or combat encounters.

The long-term game should support a world in which:

- players can build homes and settlements;
- crafting is meaningful;
- resources are gathered from the world;
- characters can specialize naturally through what they do;
- players can become hunters, fishermen, carpenters, furniture makers, lumberjacks, builders, traders, explorers, bounty hunters, farmers, miners, cooks, adventurers, and many other roles whenever mechanics support those roles;
- NPCs should eventually live in the world independently of whether a player is standing beside them;
- NPCs should have tasks, routines, destinations, work, needs, and roles;
- settlements should feel inhabited rather than spawned only for the player;
- the world should support roleplay naturally without requiring every player to roleplay.

These ideas should influence architecture choices, but **they are not all immediate implementation requirements for this rework**.

For example, a town authoring system should be capable of storing workplaces, residential areas, population zones, roads, and important locations because those will later be useful for living NPCs. However, this phase does not need to implement a complete autonomous NPC economy before world generation is finished.

The foundation should make future living-world systems possible without forcing them into the current milestone.

---

# 2. Development Philosophy

## 2.1 Preserve what already works

Before replacing any major system, inspect the current implementation.

Prefer:

- extending existing code;
- adding adapters;
- reusing current gameplay logic;
- separating old input from gameplay logic;
- improving data structures without deleting functioning content;
- migration paths for saves and content.

Avoid:

- rewriting systems only to match terminology in this document;
- changing working mechanics with no concrete benefit;
- broad folder/class renames simply for cleanliness;
- replacing proven gameplay logic when only the presentation or input layer needs to change.

## 2.2 Keep the project playable

At the end of every major phase, the project should:

- compile;
- launch;
- load a test world;
- allow meaningful testing of the new work;
- retain as much existing functionality as possible.

Large multi-week branches that leave the game unusable should be avoided where possible.

## 2.3 Sophisticated results, pragmatic simulation

The world should look and behave natural enough to feel believable.

However, Thornwake does not need to simulate Earth perfectly.

The guiding principle is:

> Generate plausibility rather than scientific accuracy.

Examples:

- rivers should generally flow downhill;
- lakes should occupy believable basins;
- mountain ranges should look like ranges rather than isolated noise spikes;
- wet areas should tend to support wetter biomes;
- elevation should influence temperature;
- cave types may be influenced by regional geology;
- biome borders should blend naturally.

But Thornwake does not require:

- physically correct plate tectonics;
- full atmospheric simulation;
- real-time sediment transport across continents;
- accurate groundwater pressure simulation;
- real fluid dynamics.

Complexity should be spent where players can perceive and enjoy it.

## 2.4 Fantasy can intentionally break natural rules

Natural rules establish the believable baseline.

Fantasy content may override them deliberately.

Examples:

- a magical forest may exist in a climate where it normally would not;
- a supernatural lake may appear high in the mountains;
- a cursed region may alter vegetation and terrain materials around itself;
- an ancient World Site may impose a local biome influence;
- magical caves may contain impossible ecosystems;
- a volcanic or arcane region may violate normal climate expectations.

The ordinary world should make sense so that extraordinary places feel extraordinary.

---

# 3. High-Level Rework Roadmap

The intended dependency chain is:

```text
CURRENT PLAYABLE THORNWAKE
        ↓
WORLD GENERATION
        ↓
HYDROLOGY + NATURAL CAVES
        ↓
WORLD CONTENT EDITOR
        ↓
BIOME / TERRAIN FEATURE / WORLD SITE AUTHORING
        ↓
CHARACTER SYSTEM
        ↓
CAMERA + CONTROLS
        ↓
WORLD TARGETING / INTERACTION
        ↓
ADAPT EXISTING GAMEPLAY
        ↓
DEEPER BUILDING / TERRAFORMING / CONTENT
```

This order is deliberate.

World structure affects almost everything later.

Characters need to exist properly before first-person camera behavior can be finalized.

Camera behavior needs to exist before world interaction, building targeting, and player-facing terraforming controls are finalized.

The current crafting/building/harvesting gameplay should then be adapted to the new foundations rather than rebuilt prematurely.

---

# 4. Phase Zero - Preserve the Current Thornwake Baseline

Before major rework begins:

- preserve a stable known-working version of the project;
- document the current working systems;
- identify the important current scenes/resources/data;
- note which parts of terraforming are already functional;
- note how protected roads/buildings currently prevent terrain damage;
- note how harvesting targets trees, stones, and berry bushes;
- note how construction placement works;
- note how crafting stations and cooking stations interact with the player;
- record current save behavior;
- create a short technical inventory of systems that should survive the rework.

This baseline is not a dead archive.

It is the reference that proves which systems used to work if a later refactor causes regressions.

---

# 5. World Scale and Streaming

Thornwake should support extremely large worlds, including worlds with multiple large continents and island groups.

The design target is a world large enough to support the feeling of a massive fantasy continent-scale or multi-continent setting rather than a small survival-game map.

The generator should be capable of producing:

- one or several major continents;
- archipelagos;
- isolated islands;
- inland seas;
- mountain regions;
- long rivers;
- lake districts;
- plains;
- forests;
- swamps;
- deserts;
- taiga;
- tundra;
- volcanic regions;
- magical/fantasy regions;
- underground cave networks.

The whole world must not need to remain fully active in memory.

World generation, terrain, vegetation, structures, NPC simulation, and rendering should support region/chunk/streaming concepts appropriate to the current engine and existing project architecture.

Important rule:

> Technical streaming boundaries must never become visible world-generation boundaries.

A player should never be able to recognize:

- square biome borders;
- terrain discontinuities at chunk edges;
- repeated chunk patterns;
- square vegetation changes;
- hard river seams.

Chunks are implementation units, not geography.

---

# 6. World Generation - Overall Structure

World generation should be hierarchical.

A useful conceptual flow is:

```text
World Seed / World Generation Profile
        ↓
Continents and Islands
        ↓
Macro Elevation
        ↓
Mountain Systems / Major Valleys / Plains
        ↓
Drainage and Water Bodies
        ↓
Regional Climate
        ↓
Biome Suitability and Blending
        ↓
Natural Terrain Detail
        ↓
Natural Caves
        ↓
Subsurface & Mineral Resource Generation
        ↓
Optional Curated Terrain Features
        ↓
World Sites
        ↓
Vegetation / Rocks / Resources / Populations
```

This is a conceptual dependency order, not a requirement that every step be implemented as a completely separate system.

Codex should adapt this to the architecture that best fits the existing game.

---

# 7. Organic Procedural Terrain

Most of the world should be generated organically.

The game should **not** be assembled entirely from terrain presets or premade terrain blocks.

Ordinary geography should emerge from the procedural world system:

- rolling hills;
- small valleys;
- ridges;
- coastal cliffs;
- gentle plains;
- local rock formations;
- small depressions;
- minor gullies;
- natural clearings;
- ordinary lakes and ponds;
- small islands;
- irregular coastlines.

Curated terrain-feature presets are an additional tool, not the only generator.

The system should avoid the feeling that the world is built from a small catalogue of reusable pieces.

---

# 8. Procedural Terrain Should Be More Than Raw Noise

Noise functions are useful, but they should not define the entire terrain by themselves.

Avoid a design equivalent to:

```text
sample one noise field
multiply it by elevation
call high values mountains
```

Instead use multiple techniques appropriate to each scale.

Potential techniques include:

- large-scale warped fields;
- ridgeline generation;
- spline or graph-defined mountain backbones;
- region-based elevation tendencies;
- erosion-inspired shaping;
- drainage-aware valley carving;
- basin identification;
- domain warping;
- multi-frequency terrain detail;
- authored procedural feature rules.

The exact algorithms are Codex's implementation decision.

The requirement is the result:

- mountains should form believable systems and ranges;
- broad plains should exist;
- valleys should connect naturally;
- coastlines should be irregular;
- terrain should have large-scale structure;
- local detail should not destroy macro geography.

---

# 9. Simplified Geological Influence

Thornwake may use a simplified regional geology layer to add variety and consistency.

It does not need to simulate actual geological history.

Regional geology can influence:

- exposed rock appearance;
- cliff materials;
- ore/resource tendencies;
- natural cave archetypes;
- some terrain roughness;
- volcanic regions;
- some erosion-style variation.

Example conceptual region types could include:

- hard crystalline rock;
- softer sedimentary rock;
- limestone/karst-like areas;
- volcanic areas;
- mixed stone;
- magical geological regions.

These regions should be influences rather than rigid laws.

A fantasy World Site or special region should be able to override them.

---

# 10. Erosion-Looking Terrain

The generator should create landforms that appear weathered and connected rather than purely mathematical.

This may include:

- river incision;
- slope smoothing;
- sediment-like valley floors;
- foothill transitions;
- exposed rock on steep slopes;
- drainage gullies;
- softened old hills;
- sharper young-looking mountains.

A real-time scientific erosion simulation is not required.

Pre-generation or procedural approximation is sufficient if the result looks convincing.

---

# 11. Hydrology

Hydrology is part of world generation.

The world should support:

- oceans;
- rivers;
- creeks;
- lakes;
- ponds;
- wetlands;
- swamp water;
- underground pools;
- underground streams where cave generation supports them;
- special magical water bodies.

Water bodies must not all exist at sea level.

Examples:

- ocean at global sea level;
- mountain lake high above sea level;
- forest pond at local terrain elevation;
- river descending across a continent;
- creek following a hillside;
- underground pool inside a cavern.

---

# 12. Rivers and Drainage

Rivers should generally follow plausible terrain drainage.

They should not simply be randomly drawn across the terrain.

The generator should use the terrain to determine likely drainage routes.

Desired results include:

- rivers beginning in higher terrain;
- tributaries merging;
- rivers generally descending;
- rivers interacting with basins and lakes;
- river valleys shaping local terrain;
- river networks contributing to nearby biome moisture;
- rivers reaching larger water bodies or the ocean when appropriate.

The implementation can be an efficient approximation rather than a full hydrological simulation.

---

# 13. Lakes and Ponds

Lakes and ponds should occupy believable terrain basins.

They may exist at any sensible local elevation.

They should support:

- irregular shorelines;
- varying depth;
- surrounding wet ground;
- wetland or marsh transitions;
- inflowing streams/rivers;
- optional outflow where applicable.

World Sites may also contain authored or semi-authored lakes and ponds.

---

# 14. Water Representation

Thornwake does not need full fluid simulation.

Natural water bodies should behave more like persistent reservoirs.

A practical abstraction is:

- the water body knows its local surface level or surface shape;
- rivers/creeks may have changing surface elevation along their course;
- most natural water bodies are effectively persistent/infinite sources;
- water bodies may have different practical flow strengths;
- terrain connectivity determines whether nearby excavations become flooded.

This is an intentional gameplay approximation.

It avoids tracking every litre of water while still allowing water to matter.

---

# 15. Terraforming Near Water

The player should have substantial freedom to terraform.

Existing protected zones around roads and important structures should be preserved or improved where necessary.

Near ordinary water:

- digging a hole beside a lake does not automatically fill it;
- the excavation stays dry until it is physically connected to the water;
- if the player creates a breach below the local water surface, the connected underground/open space becomes floodable.

The game should determine the potential flooded region when a connection is made.

However, the flooding must **not be immediate**.

---

# 16. Gradual Flooding

Flooding should progress over time.

This creates actual danger and gameplay.

A breach event should conceptually consider:

- size of the opening;
- type of water body;
- practical flow strength;
- difference between source water elevation and the flooded area;
- approximate size/volume of the connected cavity;
- possible drainage/outflow.

Examples:

- a tiny crack may leak or fill slowly;
- a narrow tunnel opened into a pond may create steady flooding;
- a large breach into a lake can flood rapidly;
- a breach into the ocean should be extremely dangerous;
- a small creek should not behave like the ocean.

The system does not need full fluid dynamics.

It only needs a convincing approximation of inflow and rising/advancing water.

---

# 17. Flooding Gameplay

Gradual flooding can create meaningful emergent situations:

- caves becoming unsafe;
- mines flooding;
- underground bases being damaged or cut off;
- players drowning;
- escape routes becoming submerged;
- accidental water entry during excavation;
- deliberate flooding;
- future drainage engineering;
- future dams or barriers;
- water hazards in natural caves.

The system should provide readable feedback:

- water visibly entering;
- rising water surface;
- splashes or flow effects at breaches;
- appropriate sound;
- clear physical danger.

---

# 18. Infinite Natural Water Sources Are Acceptable

For normal natural water bodies, it is acceptable that they effectively provide unlimited water.

This is a deliberate simplification.

Do not require:

- lakes to permanently drain;
- rivers to recalculate global discharge;
- shoreline systems to move across kilometres because a player dug a trench;
- full conservation-of-water simulation.

Special small artificial or magical water sources may later use different behavior if desired.

---

# 19. Terrain Representation and Natural Caves - Critical Technical Decision

Before the terrain system is locked down, Codex should evaluate how Thornwake will support:

- natural caves;
- overhangs;
- underground chambers;
- underground water;
- potentially player-dug tunnels/caves;
- terrain deformation around those spaces.

A traditional heightfield alone cannot represent arbitrary underground voids or multiple surfaces at the same horizontal position.

Therefore this phase needs a deliberate technical decision.

Possible categories include:

- fully volumetric terrain;
- hybrid heightfield overworld plus volumetric/mesh underground regions;
- another architecture that allows natural caves and player excavation without losing the current strengths of the game.

This should be treated as a technical design spike before deep implementation.

The decision should consider:

- performance at very large world scale;
- streaming;
- saving terrain modifications;
- multiplayer;
- building foundations;
- terraforming;
- cave generation;
- water flooding;
- editing tools;
- visual quality;
- collision;
- navigation.

Do not destroy the current terrain implementation before determining what can be reused.

---

# 20. Natural Cave Generation

Natural caves are a required part of the world.

Caves should not simply be random spherical holes or raw 3D noise.

A better conceptual approach combines:

- cave suitability by region;
- entrance selection;
- tunnel graphs;
- chambers;
- vertical shafts;
- branching;
- widening/narrowing;
- 3D procedural distortion;
- smoothing;
- connectivity validation;
- local water;
- resource zones;
- creature/POI opportunities.

The exact algorithm is an implementation choice.

The requirement is that caves should be explorable spaces with meaningful structure.

---

# 21. Cave Archetypes

Different cave styles should be possible.

Examples:

### Ordinary Rock Cave
- irregular tunnels;
- medium chambers;
- occasional vertical changes.

### Karst-Like Cave
- more branching;
- sinkholes;
- vertical shafts;
- large chambers;
- underground streams.

### Lava Tube
- long connected passages;
- collapsed sections;
- volcanic textures;
- occasional large lava chambers.

### Deep Cavern
- larger scale;
- fewer entrances;
- very large chambers;
- rare and dangerous.

### Underground River Cave
- water-focused;
- tunnels following natural drainage;
- waterfalls/pools.

### Fantasy Cave
- crystal caverns;
- magical vegetation;
- impossible formations;
- supernatural water;
- special local rules.

These are archetypes, not mandatory scientific outputs.

---

# 22. Cave Entrances

Natural cave systems should be able to connect to the surface through varied entrances:

- hillside openings;
- cliff openings;
- sinkholes;
- vertical shafts;
- collapsed terrain;
- river caves;
- underwater entrances;
- openings inside World Sites;
- mine entrances connected to deeper natural systems.

Hand-authored cave entrances should also be supported.

A World Site may use a handmade entrance while the interior remains procedural.

---

# 23. Underground Water

Caves may contain:

- pools;
- streams;
- flooded sections;
- waterfalls;
- water connected to surface sources.

The same simplified water rules should apply where practical.

If a player opens a connection between a higher water body and a lower cave, flooding should progress over time.

---


# 23A. Subsurface & Mineral Resource Generation

Mineral and subsurface resource generation belongs in the Master Rework because these resources must physically exist inside the generated world.

This layer should be part of world creation rather than being added later as an unrelated collection of spawn nodes.

The world should be able to contain resources:

- buried below ordinary terrain;
- inside mountains;
- intersecting natural caves;
- exposed in ravines;
- exposed on cliffs;
- exposed by rivers or terrain cuts;
- visible as surface outcrops;
- present as shallow deposits;
- discovered later by player excavation.

The resource layer should use information already created by World Generation, such as:

- terrain shape;
- depth;
- cave geometry;
- broad geological influence;
- elevation;
- biome;
- mountain regions;
- volcanic or magical regions;
- water/coastal context.

These factors are placement influences rather than rigid scientific laws.

The system should remain fantasy-friendly.

---

# 23B. Generic Deposit System

Do not build one bespoke generator for copper, another for iron, another for salt, and so on.

Create a general resource/deposit framework that can represent multiple deposit shapes.

Useful deposit forms include:

## Veins

Suitable for resources such as:

- copper;
- iron;
- tin;
- silver;
- gold;
- quartz;
- some magical ores.

Veins should be capable of:

- having substantial length;
- curving;
- changing thickness;
- branching;
- varying in richness;
- passing through large sections of rock.

A vein should feel like something a miner can discover and follow.

## Seams / Layers

Suitable for resources such as:

- coal;
- salt;
- some sedimentary resources.

These may form broader sheet-like or layered deposits rather than narrow veins.

## Pockets / Clusters

Suitable for:

- gems;
- rare crystals;
- high-value minerals;
- magical materials.

These are smaller concentrated deposits.

## Beds / Shallow Deposits

Suitable for:

- clay;
- sand;
- gravel;
- limestone;
- other broad near-surface materials.

## Surface Deposits / Outcrops

Suitable for:

- exposed ore-bearing rock;
- loose mineral stones;
- quartz/crystal clusters;
- salt crusts;
- clay banks;
- other resources intended to be discoverable before deep mining.

The exact geometry and implementation should match the final terrain architecture selected during the Master Rework.

---

# 23C. Initial Resource Catalogue

The first implementation only needs enough resources to validate the architecture.

A sensible initial mineral/subsurface set includes:

### Metal Ores
- copper ore;
- tin ore;
- iron ore.

### Fuel / Industrial Minerals
- coal;
- salt.

### Earth / Building Resources
- clay;
- sand;
- gravel;
- limestone;
- ordinary stone where useful.

### Gems / Crystals
- quartz;
- diamond;
- emerald;
- ruby;
- sapphire;
- amethyst.

Not every gem needs to be common or fully used by crafting immediately.

The purpose is to prove that the system supports a broad category rather than only metal ores.

Future additions should be straightforward, for example:

- silver;
- gold;
- sulfur;
- obsidian-like materials;
- amber;
- rare crystals;
- magical ores;
- fantasy minerals.

Adding an ordinary new resource later should primarily be a content/data operation rather than a world-generator rewrite.

---

# 23D. Resource Placement Rules

Each resource definition should be able to describe where it tends to occur.

Possible influences include:

- typical depth;
- minimum/maximum depth tendencies;
- mountain preference;
- broad rock/geological-region preference;
- cave-region preference;
- volcanic-region preference;
- magical-region preference;
- biome modifiers;
- elevation modifiers;
- surface exposure probability;
- cave exposure probability;
- rarity;
- deposit-size tendency;
- deposit-shape tendency;
- minimum spacing;
- regional abundance.

These should generally behave as weighted preferences rather than absolute restrictions.

For example:

- iron may be more common in some mountainous regions but still occur elsewhere;
- gems may strongly prefer deeper terrain or caves;
- salt may prefer certain broad regions, basins, or underground seams;
- sulfur may later prefer volcanic areas.

The world should remain believable without becoming geologically deterministic.

---

# 23E. Ore Veins Should Be Spatial Features

Ore veins should not simply be independent collectible nodes randomly sprinkled underground.

Where the terrain architecture allows it, a vein should exist as a spatial deposit extending through the rock.

A vein may:

- run horizontally or vertically;
- curve;
- widen and narrow;
- split;
- disappear and resume;
- cross multiple terrain chunks/streaming regions;
- intersect caves;
- intersect cliffs;
- remain entirely buried.

This creates meaningful mining behavior later:

```text
discover exposed ore
→ mine into the wall
→ follow the deposit
→ deposit eventually thins or ends
```

The Master Rework only needs to create and persist the deposit.

Mining gameplay itself is developed in the Post-Master phase.

---

# 23F. Cave, Cliff, Ravine, and Surface Exposure

Deposits and terrain should interact naturally.

Preferred behavior:

```text
world contains hidden deposit
+
terrain/cave cuts through that deposit
=
resource becomes exposed
```

Examples:

- a natural cave crosses an iron vein and exposes iron in the cave wall;
- a ravine cuts through copper-bearing rock and creates a visible outcrop;
- a cliff exposes quartz;
- player excavation later reaches a previously buried tin vein.

Avoid creating completely independent "cave ore" that has no relationship to the surrounding deposit system.

Cave generation and resource generation may be ordered or iterated however is technically convenient, but the final result should support believable intersection/exposure.

---

# 23G. Surface Resource Discovery

Not all useful materials should require deep mining.

The generator should support deliberate early-game discoverability through:

- exposed outcrops;
- loose ore-bearing rocks;
- shallow deposits;
- visible clay banks;
- quartz clusters;
- mineral-stained rock;
- salt deposits where appropriate.

Some surface clues may be associated with larger underground deposits.

This creates a natural progression:

```text
surface discovery
→ cave exploration
→ intentional mining
→ deeper prospecting later
```

The game does not need a prospecting skill during the Master Rework, but the world architecture should support one later.

---

# 23H. Resource Identity, Determinism, and Persistence

Generated deposits that can later be mined need stable world identity.

The same world seed and generated region should not produce different deposits every time it loads.

Once a deposit has been discovered/mined, its state must be persistable.

The architecture should support future states such as:

- untouched;
- partially mined;
- depleted;
- exposed by player excavation.

Do not require the entire resource/mining gameplay loop now.

Simply ensure generated deposits are deterministic and capable of persistent state.

---

# 23I. Streaming and Large Deposits

A large vein or seam may cross streamed terrain boundaries.

The deposit system must not create visible discontinuities at chunk/region borders.

If deposits are generated lazily as regions stream in, their shape and identity must remain deterministic across those boundaries.

A player following one ore vein should not see it stop abruptly merely because a terrain chunk ended.

---

# 23J. New Resource Types in Existing Worlds

The architecture should make later addition of new resource definitions possible.

For new worlds, the new resources can simply participate in generation.

For existing worlds, safe policies may include:

- new deposits appearing only in terrain/regions that have never been generated or visited;
- explicit migration tools where appropriate;
- leaving already-generated areas unchanged.

Do not automatically inject a newly added resource underneath existing player structures unless a deliberate migration process supports it.

The exact policy can be chosen later, but world generation should not make future additions impossible.

---

# 23K. Resource Debugging and Authoring Support

Resource generation should be inspectable.

Development/debug tools should be able to visualize things such as:

- deposit suitability;
- hidden deposit paths/volumes;
- vein direction;
- seam extents;
- pocket locations;
- exposed versus buried sections;
- cave/deposit intersections;
- surface outcrops;
- resource density by region.

The World Content Editor may eventually expose data authoring or preview tools for resource definitions.

A developer should be able to add a new mineral and test many generated worlds without editing the world-generation algorithm itself.

---

# 23L. What Resource Generation Does NOT Define Yet

The Master Rework answers:

> What resources physically exist in the world, where are they, and what shape do their deposits have?

It does **not** need to fully define:

- smelting recipes;
- copper + tin → bronze;
- iron → steel;
- gem cutting;
- jewelry;
- mining skills;
- pickaxe progression;
- forge progression;
- weapon recipes;
- armor recipes.

Those belong to the Post-Master item/crafting/tool phase.

This separation is intentional.

The world should know that copper ore exists inside a mountain without needing to know how a blacksmith eventually turns it into a bronze shovel.

---

# 24. Climate - Advanced Enough to Feel Natural, Simple Enough to Maintain

Climate should be based on continuous influences rather than hard region boxes.

Useful inputs include:

- broad regional temperature;
- elevation;
- broad regional moisture;
- nearby water;
- latitude or large-scale world position if useful;
- procedural regional variation;
- optional mountain/rain-shadow approximation;
- special magical influence.

Do not implement a full atmospheric simulator unless there is a concrete gameplay reason.

Climate exists primarily to generate convincing biome patterns.

---

# 25. Biome Generation

Biomes should emerge from continuous suitability rather than square ownership.

Examples:

- cold + reasonably wet → taiga tendency;
- hot + dry → desert tendency;
- warm + very wet → rainforest/swamp tendency;
- cold + dry → tundra tendency;
- moderate + wet → temperate forest tendency.

Elevation and special regional influences may further adjust suitability.

The exact biome catalogue should remain data-driven and editable.

---

# 26. Smooth Biome Blending

Biome transitions must look natural.

Avoid:

- hard borders;
- squares;
- obvious circles;
- visible region cells;
- texture seams.

At boundaries, terrain should transition through weighted influence.

This can affect:

- ground textures;
- ground color;
- normal/roughness appearance;
- grass;
- flowers;
- bushes;
- tree populations;
- rock types;
- ambient props;
- fog/atmosphere where appropriate.

Example experiential transition:

```text
dense taiga
→ mixed conifer forest
→ mixed woodland
→ temperate forest
```

rather than:

```text
taiga | invisible line | forest
```

---

# 27. Fantasy Biome Overrides

World Sites and special world regions should be able to influence local biome presentation.

Examples:

- corrupted forest around an ancient temple;
- magical flowers around a moonwell;
- ash-covered terrain around a cursed crater;
- dead vegetation around a necromantic ruin;
- glowing fungus around an underground magical lake.

These local influences should blend outward rather than creating visible rectangular patches.

---

# 28. World Generation Profiles

Before world generation, the game should eventually support world-generation profiles.

A profile may influence high-level characteristics such as:

- overall world size;
- number/range of continents;
- continent size tendencies;
- ocean coverage;
- island frequency;
- mountain prevalence;
- hill prevalence;
- ravine/canyon prevalence;
- lake frequency;
- river density;
- cave abundance;
- wetland prevalence;
- biome diversity;
- broad climate tendencies;
- settlement density;
- POI density;
- wilderness level;
- curated terrain-feature frequency.

These are high-level influences, not literal chunk percentages.

For example, "more mountains" should cause the macro terrain generator to produce more mountainous geography, not paste mountain stamps into a fixed percentage of squares.

Profiles may eventually include presets such as:

- balanced;
- continental;
- archipelago;
- mountainous;
- wet;
- cold;
- wild;
- custom.

---

# 29. World Content Editor - Umbrella Tool

A major goal of this rework is to give Tom tools to curate and extend the world without rewriting generator code.

The authoring system should be thought of as a **World Content Editor**.

It may contain several related editing modes/tools:

- World Site Editor;
- Biome Editor;
- Terrain Feature Preset Editor;
- water-body tools;
- scatter/vegetation zone tools;
- NPC/creature spawn zone tools;
- road/path/entrance connection tools;
- world-generation profile editing;
- testing/preview tools.

These tools may live in one editor application/scene or in multiple integrated editor modes, depending on what best fits the project.

---

# 29A. World Asset Library

The World Content Editor should also provide a data-driven **World Asset Library**.

This library is the central place where reusable world content is registered and described.

Examples include:

- trees;
- bushes;
- grasses;
- flowers;
- rocks;
- fallen logs;
- stumps;
- mushrooms;
- resource nodes;
- decorative props;
- buildings;
- furniture;
- fences;
- ruins;
- lights;
- magical props;
- environmental effects.

Adding a normal world asset should not require modifying World Generation code.

The generator, Biome Editor, World Site Editor, vegetation scatter tools, and manual placement tools should all reference the same reusable asset definitions.

For example, an Ash tree should be registered once and then become usable by:

- biome vegetation generation;
- World Site procedural vegetation zones;
- manual World Site placement;
- general world generation;
- future forestry/resource systems;
- harvesting gameplay.

---

# 29B. Vegetation Asset Definitions

Trees and other harvestable vegetation should be more than decorative meshes.

A tree definition should be able to describe concepts such as:

- species identity;
- compatible models/visual variants;
- growth or size category where useful;
- wood family;
- physical hardness;
- biome compatibility;
- moisture/elevation tendencies;
- slope tolerance;
- spacing;
- clustering tendencies;
- procedural scale range;
- harvestable resource families;
- stump/fallen-tree visuals;
- protection behavior when used by World Sites.

The exact implementation and data format are Codex's decision.

The important requirement is that a new tree species should be addable primarily through data/content.

Example workflow:

```text
Add new tree species
↓
assign models
↓
assign wood/resource properties
↓
assign ecological/world-generation rules
↓
tree becomes available to biome generation and World Sites
```

---

# 29C. Tree Species Versus Tree Instance

Separate the concept of a tree species from the physical instance placed in the world.

The species defines things such as:

- what kind of wood it provides;
- whether that wood is broadly softwood/hardwood/etc.;
- what secondary resources it can provide;
- how difficult it is to chop;
- which biomes/environmental conditions prefer it;
- which model variants belong to that species.

The individual tree instance determines things such as:

- chosen model;
- procedural scale;
- physical trunk size;
- maturity/size category where applicable;
- actual harvest yield;
- current health/state.

This allows two Ash trees to use the same species definition while producing different amounts of resources because one is physically much larger.

---

# 29D. Tree Yield Should Depend on Physical Size

Do not define tree harvest yield only as a fixed number per species.

A physically large tree should generally produce more usable wood than a small tree.

Yield should be derived from a practical approximation of physical tree size, using whatever information is reliable in the asset pipeline.

Possible inputs include:

- trunk diameter;
- trunk height;
- overall model scale;
- maturity category;
- species-specific yield factor.

The game does not need scientifically exact timber-volume calculations.

The result simply needs to behave plausibly.

Example:

```text
small/young tree
→ few or no full logs
→ several branches

normal mature tree
→ moderate log yield
→ more branches

large/old tree
→ high log yield
→ many branches
```

A large softwood tree may yield more logs than a small hardwood tree even if the hardwood is harder to chop.

Therefore keep these concepts separate:

```text
physical size
→ amount of material

wood hardness
→ harvesting difficulty/tool demand

wood species/family
→ material identity / visuals / later crafting uses
```

---

# 29E. Tree Resource Outputs

Trees should be capable of producing more than logs.

The vegetation/resource architecture should support secondary outputs such as:

- branches;
- bark;
- leaves;
- seeds;
- nuts/acorns where appropriate;
- resin;
- sap;
- fruit for fruit-bearing trees;
- special fantasy resources for magical trees.

Not every tree species needs every output.

These outputs should be data-driven.

Example conceptual Ash/Oak-like tree:

```text
Primary:
- species logs

Secondary:
- branches
- bark
- leaves
- seeds/acorns where appropriate
- resin/sap where appropriate
```

World Generation only needs to know that the tree species exists and what resource families it can later provide.

Detailed recipes belong to the Post-Master phase.

---

# 29F. Generic Versus Species-Specific Secondary Resources

Avoid unnecessary inventory clutter.

Some secondary tree resources may remain generic:

```text
Branch
Leaves
Bark
Resin
```

while primary lumber can remain species-specific:

```text
Ash Log
Oak Log
Pine Log
Birch Log
```

and later:

```text
Ash Plank
Oak Plank
Pine Plank
Birch Plank
```

Species-specific secondary materials should only be introduced where they create meaningful gameplay.

Examples where specificity may matter later:

- Oak Acorn;
- Willow Bark;
- Pine Resin;
- magical tree sap.

The system should support both generic and species-specific outputs.

---

# 29G. Early-Game Surface Wood Resources

The world should be able to spawn or scatter small natural wood resources that allow early-game progression without immediately felling a mature tree.

Examples:

- fallen branches;
- sticks;
- small deadwood;
- bark pieces;
- pine cones/acorns/seeds where relevant.

These can be produced by:

- biome scatter;
- tree-adjacent scatter;
- storms/weather later;
- fallen-tree sites;
- manual World Site placement.

This allows a fresh player to obtain primitive crafting materials before having a proper axe.

---

# 29H. Tree Harvest State

Tree assets should support gameplay state where practical.

Potential states include:

- living;
- damaged;
- felled;
- stump;
- fallen/dead;
- regrowing/sapling later if forestry is added.

The initial World Generation rework does not need to implement complete forestry growth simulation.

However, the asset architecture should not make future tree regrowth or managed forestry impossible.

---

# 29I. Biome Integration for New Tree Species

When a new tree species is created, it should be possible to make it available to biome generation by editing data rather than World Generation code.

A biome may use tree species with weights/preferences based on:

- moisture;
- elevation;
- temperature;
- slope;
- proximity to water;
- density;
- clustering.

Example:

```text
Temperate Forest:
    Oak      common
    Ash      common
    Birch    medium
    Pine     low
```

A World Site can override or supplement those rules.

---

# 29J. World Site Use of Registered Assets

Manually placed trees inside a World Site should use the same registered vegetation definitions as procedurally spawned trees.

This means a manually placed Ash tree:

- is still an Ash tree;
- uses the same wood family;
- produces the same resource categories;
- derives yield from physical size;
- can be harvested if the site allows it;
- can be protected if the individual site marks it protected.

Do not create decorative-only copies of ordinary harvestable trees unless deliberately intended.

---

# 29K. Asset Preview and Yield Diagnostics

For harvestable vegetation, the World Content Editor should provide useful diagnostics.

A creator should be able to inspect:

- selected model;
- physical dimensions;
- procedural scale range;
- estimated log-yield range;
- estimated secondary-resource range;
- wood family;
- hardness category;
- compatible biomes.

This helps detect obviously incorrect assets, such as a tiny sapling producing the same timber as a giant mature tree.

The exact presentation is flexible.

---


# 30. Terrain Feature Presets

Terrain Feature Presets allow the developer to curate how important terrain features should look without forcing every feature to be identical.

Examples:

- major ravine;
- canyon;
- crater;
- glacial valley;
- mountain archetype;
- volcanic ridge;
- mesa formation;
- lake basin;
- cliff system;
- special hill type.

A preset should define the **character and parameter ranges** of the feature.

For a ravine, this might include concepts such as:

- typical length range;
- width range;
- depth range;
- curvature;
- branching tendency;
- wall steepness;
- bottom width;
- rock exposure;
- vegetation tendencies;
- water chance;
- biome compatibility;
- edge blending.

Each generated ravine should still receive procedural variation.

A preset must not produce clones.

---

# 31. Terrain Feature Presets Are Optional Influences

The world must not be composed exclusively from these presets.

Normal world generation continues producing:

- ordinary hills;
- ordinary valleys;
- minor gullies;
- small cliffs;
- normal ridges;
- natural small depressions.

Terrain Feature Presets are mainly useful for:

- larger recognizable features;
- curated visual styles;
- biome-specific feature identities;
- special regional geography.

The generator should remain capable of producing believable terrain even if no curated feature presets exist.

---

# 32. Biome Editor

The World Content Editor should support creation and editing of biomes.

A biome definition should be able to describe:

- environmental suitability;
- ground/material sets;
- vegetation sets;
- tree pools;
- bush/grass/flower pools;
- rock/prop pools;
- density tendencies;
- terrain-feature preferences;
- water tendencies;
- cave tendencies;
- resource tendencies;
- ambient wildlife pools;
- monster pools where relevant;
- compatible World Sites;
- special atmosphere or effects where appropriate.

Adding a new biome should primarily be a content/data task.

It should not require rewriting the world generator.

---

# 33. Use Existing Good Assets

The new world systems should reuse the good assets already created for Thornwake, including the existing stylized tree work produced during current development.

The rework should not discard successful art simply because world generation is changing.

The procedural vegetation systems should be designed to use:

- current tree models;
- future tree variants;
- bushes;
- grass;
- rocks;
- fallen logs;
- props;
- biome-specific asset pools.

---

# 34. World Sites - Core Concept

A **World Site** is a reusable authored/procedural piece of world content.

It is more powerful than a normal prefab.

A World Site may represent:

- a tiny magical pond;
- a shrine;
- a bandit camp;
- a farm;
- a village;
- a town;
- a city;
- a castle;
- a fortress;
- a ruin;
- a battlefield;
- a cave entrance;
- a mine;
- a dungeon entrance;
- a temple;
- a harbor;
- a bridge complex;
- an ancient grove;
- a special lake;
- a magical crater;
- an entire terrain feature with gameplay logic.

A World Site may include terrain and water, not only placed models.

---

# 35. World Site Contents

A World Site may contain:

- terrain deformation;
- terrain material/biome influence;
- water;
- hand-placed trees;
- hand-placed rocks;
- buildings;
- ruins;
- furniture;
- props;
- decoration;
- roads;
- paths;
- entrances/exits;
- connection points;
- vegetation scatter zones;
- creature zones;
- monster zones;
- NPC zones;
- loot zones;
- resource zones;
- ambient effect zones;
- sound zones;
- quest/logic markers;
- workplaces;
- residential areas;
- special interaction locations.

Not every site needs every type of content.

---

# 36. World Sites Can Contain Terrain

The World Site Editor should allow local terrain to be generated using the same basic world-generation logic used by the main world.

A creator should be able to:

1. create a new site;
2. select a preview biome/environment;
3. generate a local terrain sample;
4. sculpt or reshape it;
5. flatten specific areas;
6. raise/lower ground;
7. paint terrain materials or local biome influence;
8. add water;
9. add models;
10. define procedural zones;
11. define placement rules;
12. preview several seeds;
13. save the site.

This is particularly important for places such as:

- magical ponds;
- sacred lakes;
- castle hills;
- crater temples;
- villages;
- ruined valleys;
- cave entrances.

---

# 37. Site Terrain Must Blend Into the Main World

A site should not arrive as an obvious square terrain tile.

Site terrain modification should use an irregular influence/footprint and edge falloff.

The integration process should be capable of blending:

- elevation;
- slope;
- ground materials;
- vegetation;
- biome influence;
- roads/paths;
- water edges.

The site should preserve its important authored shape while adapting naturally to the host landscape.

---

# 38. Relative Terrain and Water

World Site terrain and water should normally be stored relative to the site's local anchor/reference rather than relying on absolute world altitude.

This allows the same magical pond, town, or shrine to be placed:

- in a low forest;
- on a plateau;
- in a high mountain valley;

while retaining its intended internal shape.

Absolute elevation may still be used for special content when explicitly required.

---

# 39. Handmade + Procedural Content in the Same Site

World Sites should support a mixture of hand-authored and procedural content.

Example magical pond:

### Hand-authored
- pond shape;
- shrine;
- bridge;
- important ancient tree;
- statue;
- unique stones.

### Procedural
- surrounding trees;
- grass;
- mushrooms;
- flowers;
- small rocks;
- ambient creatures;
- monster population.

The generated details can vary per world seed while the site's identity remains recognizable.

---

# 40. Procedural Zones

The World Site Editor should support painted or shaped procedural zones.

Examples:

### Vegetation Zone
The creator paints an area and defines:
- allowed tree/plant types;
- density;
- spacing;
- scale variation;
- rotation variation;
- slope rules;
- exclusion around structures.

### Monster Zone
The creator defines:
- eligible creature/monster pool;
- population range;
- spawn behavior;
- respawn behavior if relevant;
- allowed subareas.

### NPC Zone
The creator defines:
- eligible NPC types/roles;
- population count/range;
- suitable destinations;
- future work/residential meaning.

### Loot Zone
The creator defines:
- loot category;
- container/spawn behavior;
- rarity.

### Resource Zone
The creator defines:
- herbs;
- ore;
- wood;
- special gatherables.

The zone stores rules rather than a fixed final placement.

---

# 41. Site Randomness

World Site instances should be able to vary.

Variation may include:

- tree placement;
- bushes;
- rocks;
- decoration;
- minor structures;
- selected building variants;
- monster populations;
- NPC populations;
- loot;
- local clutter;
- minor terrain detail.

Important authored landmarks remain stable.

This allows the same site definition to be recognizable without looking copied.

---

# 42. World Site Generation Modes

Conceptually, sites may fall anywhere on a continuum from handmade to procedural.

It is useful to support three broad categories:

## Authored
Mostly handmade.

Suitable for:
- legendary castle;
- lore-critical temple;
- unique capital;
- major ruin.

## Hybrid
Handmade identity with procedural variation.

Suitable for:
- villages;
- magical ponds;
- camps;
- ordinary towns;
- ruins;
- farms;
- shrines.

## Procedural
Mostly generated from rules.

Suitable for:
- small caves;
- generic camps;
- some villages;
- roadside ruins;
- small mines;
- resource sites.

The rest of the game should not need to care how the final placed site was produced.

---

# 43. Procedural Towns and Villages

Settlements should not be limited to fully handmade layouts.

A town or village may contain:

- a hand-designed town center;
- a hand-designed important building;
- a road structure;
- residential zones;
- commercial zones;
- workshop zones;
- farm zones;
- decorative zones;
- building sockets/lots.

The procedural settlement system can select compatible buildings for those lots.

Examples:

- choose one of several houses;
- choose a blacksmith/carpenter/tannery for a workshop area;
- populate decoration zones;
- assign fences, sheds, wood piles, carts;
- place trees in allowed areas.

This produces variation while preserving settlement identity.

---

# 44. Settlement Logic and Future Living NPCs

World Sites representing settlements should be capable of storing semantic information that future NPC systems can use.

Examples:

- homes;
- beds;
- workplaces;
- market areas;
- taverns;
- crafting areas;
- farms;
- docks;
- gates;
- public squares;
- road connections;
- defensive areas.

The current milestone does not need to fully implement autonomous NPC society.

However, settlement authoring should not discard information that will later be useful for NPC routines.

---

# 45. Procedural Caves as World Sites

Natural caves are generated by World Generation.

World Sites may also define cave-related content.

Examples:

- handmade cave entrance with procedural interior;
- natural cave containing an authored shrine;
- abandoned mine connected to natural caves;
- dungeon occupying part of a generated cave network;
- underground settlement;
- monster nest;
- rare resource chamber.

The same site framework should be capable of working above or below ground.

---

# 46. Site Placement Rules

Every World Site needs environmental placement rules appropriate to its content.

Possible considerations include:

- allowed biomes;
- preferred biomes;
- forbidden biomes;
- elevation;
- slope;
- terrain roughness;
- proximity to river;
- proximity to lake;
- proximity to coast;
- proximity to road;
- distance from settlements;
- distance from other sites;
- required cliff;
- required cave;
- required flat area;
- required water;
- compatible climate;
- regional/faction restrictions.

The generator should select suitable locations rather than simply choosing random coordinates.

---

# 47. Site Occurrence Rules

World Sites need occurrence-count rules.

Possible behaviors:

- guaranteed exactly once;
- optional unique;
- limited to a few occurrences;
- common/repeatable;
- effectively unlimited subject to spacing.

A site should support minimum/maximum occurrence intent.

Examples:

### Lore Capital
- must exist;
- exactly one.

### Ancient Shrine
- several allowed.

### Bandit Camp
- many allowed.

### Legendary Tree
- exactly one.

### Roadside Camp
- optional and repeatable.

---

# 48. Unique Groups and Variants

Several different site variants may represent the same lore concept.

Example:

- coastal version of a lore town;
- river version;
- lake version.

Only one should be chosen in a world.

The system should support grouping variants under a shared unique identity so the generator selects one appropriate form rather than placing all of them.

---

# 49. Lore Identity Without Fixed Coordinates

Important lore locations should not need fixed map coordinates.

A canonical town/castle can:

- always exist;
- retain its name;
- retain its faction;
- retain important buildings;
- retain important NPC roles;
- retain story/quest identity;

while appearing in a different suitable location for each world seed.

Example concept:

A major lore town may require:

- temperate climate;
- river or coast access;
- substantial flat terrain;
- connection to a major road network.

One world may place it on a coast.
Another may place it beside a river delta.
Another may place it on a large lake.

The place is canonical.
The coordinates are not.

---

# 50. Road and Connection Interfaces

World Sites should expose meaningful connection points.

Examples:

- road entrance;
- secondary road;
- path;
- harbor;
- cave connection;
- river connection;
- bridge approach;
- gate;
- trail.

The regional road/path generator can use these to connect sites sensibly.

This helps prevent roads from connecting to random building walls or approaching settlements from unusable directions.

---

# 51. World Site Editor Preview

A site should be testable against multiple host environments.

Useful preview conditions include:

- flat forest;
- hilly forest;
- riverbank;
- taiga;
- swamp;
- desert;
- rocky mountain;
- different terrain seeds.

The editor should help reveal:

- bad blending;
- buried buildings;
- floating props;
- broken road connections;
- impossible slopes;
- water problems;
- vegetation collisions.

---

# 52. World Site Validation

Before a site is considered ready, validation should be able to identify common problems such as:

- objects below terrain;
- objects floating;
- required road sockets missing;
- required water missing;
- invalid terrain slope;
- overlapping critical structures;
- buildings placed below water;
- inaccessible entrances;
- scatter zones overlapping prohibited areas;
- unassigned required semantic markers.

---

# 53. World Generation + World Sites Together

The main world should feel like a continuous generated place.

World Sites add intentional authored content.

The final result is:

```text
procedural geography
+ curated terrain character
+ smooth biomes
+ procedural ecosystems
+ authored/hybrid landmarks
+ procedural settlements/caves where appropriate
```

Not:

```text
random terrain
+ prefab pasted on top
```

and not:

```text
world made entirely from premade modules
```

---

# 54. Character System - Start Only After World Foundation Stabilizes

Once World Generation and World Content tools have a stable foundation, work moves to characters.

The Character System should support:

- multiple races;
- body presets;
- face presets;
- skin colors;
- eye colors;
- hairstyles;
- hair colors;
- facial-hair styles;
- facial-hair colors independent of hair;
- tattoos;
- scars;
- race-specific features;
- modular clothing;
- modular armor;
- weapons;
- shields;
- future accessories.

The system should remain extendable without requiring a huge sculpting UI.

---

# 55. Shared Character Architecture

Player characters, NPCs, multiplayer characters, and character-creator previews should share the same underlying appearance system.

Avoid parallel implementations.

The same modular character should be usable for:

- player;
- villager;
- trader;
- guard;
- companion;
- quest NPC;
- remote multiplayer player;
- character creator.

This is important for the future living-world concept because NPCs should be full inhabitants of the same world, not visually simplified exceptions.

---

# 56. Shared Humanoid Rig Where Practical

Humanoid races should share a common animation-compatible skeleton when practical.

This reduces:

- animation duplication;
- equipment complexity;
- first-person complexity;
- maintenance cost.

Races can still differ through:

- body proportions;
- face meshes;
- ears;
- tusks;
- horns;
- skin;
- hair;
- race features.

Extremely different future races may require their own rig family.

---

# 57. Character Customization

Initial character creation should focus on modular choices rather than dozens of sculpting sliders.

Recommended categories:

- race;
- body preset;
- face;
- skin color;
- eye color;
- hairstyle;
- hair color;
- facial hair;
- facial-hair color;
- tattoos;
- tattoo color where appropriate;
- scars;
- name.

Additional customization may be added later.

---

# 58. Modular Clothing and Armor

Each equipped clothing/armor piece should visibly change the character.

Examples:

- shirt;
- chest armor;
- trousers;
- boots;
- gloves;
- bracers;
- helmet;
- shoulders;
- cloak;
- belt/waist gear;
- main-hand weapon;
- off-hand weapon/shield.

The character should not switch between single full-body costume meshes for every combination.

Equipment should work as modular visual layers.

---

# 59. Preventing Clothing Clipping

Character bodies and equipment should support hiding underlying body regions where necessary.

Examples:

- boots hide the underlying foot/lower-leg geometry;
- gloves hide underlying hands where needed;
- heavy chest armor may hide parts of the torso;
- helmets may hide hair;
- some helmets may use compatible hair variants.

This should be handled systematically rather than with one-off hacks per item.

---

# 60. Character Content Should Be Data-Driven

Adding:

- a face;
- hairstyle;
- beard;
- tattoo;
- scar;
- race feature;
- shirt;
- armor;
- boots;
- cloak;

should primarily be a content/data workflow.

Ordinary new cosmetic/equipment content should not require controller-code edits.

---

# 61. Character Asset Templates

Codex should create clear asset templates and a guide for future content creation.

The guide should explain:

- expected scale;
- orientation;
- skeleton use;
- rigging expectations;
- material slots;
- texture conventions;
- race compatibility;
- body compatibility;
- equipment slot assignment;
- which body regions are hidden;
- how helmets interact with hair;
- how to register the asset;
- how to test it.

The workflow should be simple enough that Tom can create/import content and register it without changing core gameplay code.

---

# 62. CharacterLab

Before the final Character Creator, create a dedicated character-development scene/tool.

CharacterLab should allow rapid testing of:

- race;
- body;
- face;
- skin;
- eyes;
- hair;
- hair color;
- facial hair;
- facial-hair color;
- tattoo;
- scar;
- equipment;
- weapons;
- animations;
- first-person/third-person visibility.

This becomes the main validation environment for character content.

---

# 63. Character Creator

Once the modular system is proven, build the actual character-creation screen.

It should use the real character appearance system.

It should not use a separate fake preview implementation.

The creator should support:

- character rotation;
- zoom;
- race-specific options;
- randomization;
- reset;
- final creation.

---

# 64. Camera and Controls - After Character Foundation

The new camera system should be implemented after the character architecture is stable because it depends on:

- character root;
- head location;
- first-person anchor;
- body visibility;
- equipment visibility;
- animation behavior.

The target feel is closer to modern third-person survival/RPG controls such as Valheim/Soulmask than classic MMO movement.

---

# 65. Third-Person Movement

The intended default feel:

- WASD movement relative to camera;
- left/right strafe rather than keyboard turning;
- smooth character facing;
- continuous mouse camera;
- sprint;
- jump;
- optional crouch;
- optional free-look.

The player should not need to hold a mouse button simply to rotate the camera.

---

# 66. Third-Person Camera

The camera should:

- orbit smoothly around the character;
- zoom smoothly;
- avoid walls and terrain;
- recover smoothly after collisions;
- allow the player to look around without automatically rotating the standing character;
- remain usable for exploration, combat, building, and terrain work.

---

# 67. Seamless Zoom Into First Person

Mouse-wheel zoom should move through a continuous range:

```text
wide third person
→ normal third person
→ close third person
→ first person
```

Zooming back out reverses the transition.

No menu should be required.

No separate player controller should be created.

No loading transition.

Movement/combat/building state should survive the switch.

---

# 68. First-Person Uses the Real Character

First person should use the same modular character.

Where practical, looking down should show:

- torso;
- arms;
- trousers;
- gloves;
- boots;
- armor;
- equipped items.

Locally hide only geometry that obstructs the camera, such as:

- head;
- hair;
- facial hair;
- problematic helmet interior.

Changing clothes should immediately be reflected in first person.

Do not maintain an independent fake first-person outfit.

---

# 69. First-Person Interaction With Existing Gameplay

First person should support:

- melee combat;
- ranged combat;
- interaction;
- harvesting;
- building;
- terraforming;
- tools;
- crafting-station interaction.

Gameplay rules remain shared.

Only camera presentation, targeting origin, and visual offsets differ.

---

# 70. Controls and Settings

The final control system should support rebinding of keyboard and mouse inputs.

Important groups include:

- movement;
- combat;
- interaction;
- building;
- terraforming;
- interface.

Mouse configuration should support:

- horizontal sensitivity;
- vertical sensitivity;
- aim sensitivity;
- first-person sensitivity;
- inversion;
- smoothing/acceleration options where supported.

Camera options should include:

- zoom speed;
- default distance;
- field of view;
- first-person field of view;
- camera smoothing;
- first-person bob;
- camera shake;
- other accessibility-relevant presentation controls.

Use the existing engine input system appropriately rather than hardcoding physical keys in gameplay logic.

---

# 71. Shared World Targeting / Interaction Layer

After the camera exists, create a shared system that answers:

> What is the player currently targeting or intending to interact with?

Conceptually:

```text
active camera
        ↓
world query / ray / interaction area
        ↓
target resolution
        ↓
gameplay system
```

The same targeting foundation should support:

- interaction;
- harvesting;
- combat;
- building;
- terraforming;
- future NPC commands.

Third- and first-person cameras feed the same gameplay systems.

---

# 72. Interaction Priorities

Interaction should prefer what the player is visibly aiming toward.

Useful behavior:

1. object near the center of view;
2. valid visible object in a small central area;
3. nearest sensible fallback within short interaction range.

Avoid choosing an object behind the player simply because it is technically closest.

---

# 73. Adapt Existing Harvesting

Current tree, stone, and berry harvesting should be preserved.

Adapt targeting and control where required.

Existing resource logic should not be rewritten unless necessary.

The new system should eventually support:

- aiming at a tree and chopping;
- targeting rock and mining;
- gathering bushes;
- future resource types.

---

# 74. Adapt Existing Crafting and Stations

Existing crafting benches, cooking stations, and similar interactables should remain.

Adapt them to:

- the shared interaction system;
- new camera controls;
- improved world placement if required.

Do not redesign crafting simply because the camera changes.

---

# 75. Adapt Existing Building

Existing building should be preserved as much as possible.

Eventually adapt it to camera-based world targeting.

Desired long-term behavior:

- building ghost follows valid world target;
- snapping remains;
- existing construction logic remains where useful;
- structures can be placed from third or first person;
- detailed placement becomes easier in close/first-person views;
- large construction can be planned from wider views.

Existing protected terrain behavior around buildings should remain or improve.

---

# 76. Future Building Depth

After the foundation rework, deeper building can continue.

Potential systems already discussed include:

- blueprint construction;
- line placement for palisades/fences;
- structure variants;
- roof variants;
- multiple roof slopes;
- snapping;
- material delivery;
- construction progress.

These should come after the core world/camera/interaction migration unless existing work already supports them cleanly.

---

# 77. Adapt Existing Terraforming

Existing terraforming work should be preserved as a backend where practical.

Do not over-polish its current player-facing controls before camera/interaction architecture is ready.

After the new targeting layer exists:

- terrain tools should act on camera-targeted ground;
- third person and first person should both work;
- protected roads/buildings should remain protected;
- water connections should matter;
- cave/volumetric terrain architecture should be respected.

---

# 78. Terraforming Tool Philosophy

Long-term terrain tools may support actions such as:

- dig;
- raise;
- level;
- smooth;
- flatten;
- path creation;
- mining/excavation.

Tool tier/quality may eventually influence:

- speed;
- strength;
- affected area;
- stamina;
- durability;
- ability to work harder terrain.

These gameplay details can be refined after the underlying terrain architecture is stable.

---

# 79. World Sites and Player Terraforming

Placed World Sites may need different protection policies.

Examples:

### Fully Protected
Important lore structures cannot be destroyed/deformed.

### Partially Protected
Core foundations/roads are protected but surrounding terrain is editable.

### Editable
Ordinary procedural sites may be heavily altered by players.

### Special Rules
A magical site may resist excavation or use unique behavior.

Protection should be part of content/site behavior, not hardcoded globally.

---

# 80. World Sites and Player Building

World Sites should be able to define:

- no-build areas;
- restricted areas;
- open build areas;
- road protection;
- structure protection;
- future ownership/faction restrictions.

Again, the framework should permit these rules even if all are not implemented immediately.

---

# 81. Living World Compatibility

The long-term living-world goal should influence data design.

NPCs will eventually need to live and act when players are not physically present.

Therefore world/settlement content should be capable of representing:

- homes;
- jobs;
- workplaces;
- travel connections;
- resources;
- gathering areas;
- farms;
- shops;
- workshops;
- social areas;
- guard posts;
- beds;
- storage;
- production.

This does **not** mean the current rework must immediately implement full offline NPC simulation.

It means editor/site architecture should not make that future impossible.

---

# 82. Player Freedom Compatibility

The game's systems should remain flexible enough that player roles emerge naturally.

If mechanics support it, players should be able to spend their time as:

- hunter;
- fisherman;
- lumberjack;
- miner;
- farmer;
- carpenter;
- furniture maker;
- builder;
- cook;
- blacksmith;
- trader;
- explorer;
- bounty hunter;
- adventurer;
- other world-supported professions.

The current rework should avoid designing everything around a fixed "hero class" or mandatory quest path.

This is a design constraint, not an immediate profession-system implementation task.

---

# 83. Procedural Resources

Resource distribution should take world context into account.

The Master Rework includes a dedicated **Subsurface & Mineral Resource Generation** layer for persistent physical deposits such as ore veins, seams, mineral pockets, clay beds, salt, coal, and gems.

Other resources should also use world context.

Examples:

- tree types from biome;
- ore/mineral deposits from resource-generation rules and broad geological influence;
- fish from water-body type;
- herbs from biome/moisture;
- special resources from caves or magical regions.

Avoid purely uniform world-wide random placement.

Do not duplicate the mineral system by spawning unrelated ore nodes in caves or terrain when the same resource can be represented by the persistent deposit layer.

---

# 84. Vegetation

Vegetation generation should use:

- biome rules;
- moisture;
- slope;
- elevation;
- water proximity;
- local clearing rules;
- World Site overrides;
- hand-placed assets;
- procedural scatter.

Use the high-quality existing tree assets where they fit.

Trees and other harvestable vegetation should use the World Asset Library rather than being generator-specific hardcoded models.

A registered tree species may provide:

- species-specific logs/lumber;
- generic or species-specific branches;
- bark;
- leaves;
- seeds/nuts/acorns;
- resin;
- sap;
- fruit or special fantasy resources where appropriate.

Physical tree size should influence the amount of harvestable material.

The system should make it easy to add future tree/bush/grass sets through data/content rather than World Generation code.

---

# 85. Roads

Road generation can be expanded once major sites are placed.

Roads should logically connect:

- towns;
- villages;
- castles;
- ports;
- major POIs.

Sites should provide sensible road-entry points.

Roads may influence settlement growth and future NPC travel.

Existing road terrain protection should be preserved.

---

# 86. Saving and Determinism

World generation should support stable world seeds.

A saved world should preserve:

- world-generation seed/profile;
- placed World Sites;
- their procedural variations;
- player-built structures;
- terrain modifications;
- flooding/water-state changes that need persistence;
- relevant generated cave state;
- harvested/regrown resources as appropriate;
- character appearance;
- future NPC state.

Do not regenerate important content differently each time the save loads.

---

# 87. Generated Content Identity

Generated sites and procedural content that need persistent state should have stable identities.

This is particularly important for:

- NPC homes;
- shops;
- loot;
- destroyed structures;
- quest locations;
- player modifications;
- multiplayer synchronization.

The exact implementation is Codex's decision.

---

# 88. Performance Principles

The world is intended to be huge.

Therefore systems must avoid assuming everything remains active.

Important considerations include:

- streamed terrain;
- streamed vegetation;
- site activation/deactivation;
- simplified distant simulation;
- character LOD;
- world-site LOD;
- limited expensive physics;
- efficient terrain modification storage;
- efficient save deltas.

Do not prematurely optimize every detail, but do not build architecture that requires the entire world to exist in memory.

---

# 89. NPC Simulation and Streaming - Future Compatibility

Eventually NPCs should continue to exist conceptually even when their area is not fully loaded.

Future approaches may use:

- high-detail simulation near players;
- simplified schedule/task simulation at distance;
- event-driven updates;
- regional state simulation.

This rework does not need to finish that system.

However, settlement and World Site data should support it later.

---

# 90. Debugging and Visualization Tools

Large procedural systems are difficult to tune without visibility.

Codex should create useful development/debug views for world generation.

Possible overlays include:

- continent regions;
- elevation;
- slope;
- drainage;
- water basins;
- river paths;
- temperature;
- moisture;
- biome weights;
- cave suitability;
- cave systems;
- mineral/resource suitability;
- hidden deposit paths/volumes;
- exposed resource sections;
- cave/deposit intersections;
- World Site candidate locations;
- selected site locations;
- terrain-feature placements;
- protected areas;
- streaming regions.

The exact UI can evolve, but world generation should not be a black box.

---

# 91. Generation Diagnostics

For each generated world, useful diagnostics may include:

- continent count;
- land/ocean ratio;
- biome coverage;
- river count;
- lake count;
- cave count;
- mineral deposit counts by type;
- surface outcrop counts;
- broad resource-density sanity checks;
- major site placement success;
- sites rejected because no valid location existed;
- road connection failures;
- world-generation time.

These tools are important for tuning and automated testing.

---

# 92. World Generation Testing

Test with many seeds.

Do not validate the system using one attractive world.

Generate worlds with:

- very different continent profiles;
- different biome distributions;
- high/low mountain settings;
- high/low water;
- many/few caves;
- different mineral/resource abundance profiles where supported;
- dense/sparse settlements;
- unusual profile combinations.

Check for:

- broken rivers;
- unusable towns;
- giant terrain spikes;
- impossible site placement;
- biome seams;
- water floating above terrain;
- buried structures;
- inaccessible cave entrances;
- veins/seams terminating visibly at streaming boundaries;
- implausible resource clustering;
- missing cave/deposit intersections;
- repeated obvious patterns.

---

# 93. Editor Testing

The World Content Editor should be tested by actually creating:

1. a new harvestable tree species (for example Ash) with multiple model variants and world-generation rules;
2. a small magical pond site;
3. a simple ruin;
4. a hybrid village;
5. a biome definition using the newly registered tree;
6. a major ravine preset;
7. a natural cave-related site.

If creating these requires code edits every time, the editor/data architecture is not yet accomplishing its purpose.

---

# 94. Character Testing

CharacterLab should prove:

- at least two races can use the architecture;
- faces change correctly;
- hair works;
- facial hair works;
- colors work independently;
- tattoos/scars work;
- helmets/hair interact correctly;
- layered equipment works;
- animations work across compatible races;
- first-person visibility works;
- NPC and player appearance use the same system.

---

# 95. Camera Testing

Test:

- wide third person;
- close third person;
- transition into first person;
- transition back out;
- walking;
- sprinting;
- jumping;
- harvesting;
- combat;
- building;
- station interaction;
- terrain targeting;
- camera collision;
- interior spaces.

The camera should not require special-case gameplay copies.

---

# 96. Integration Testing

Once existing systems are migrated, test the original base loop in the new world:

```text
spawn in generated world
→ walk
→ harvest trees/stones/berries
→ craft
→ place/use stations
→ cook
→ build shelter
→ terraform
→ interact with water
→ visit generated/hybrid World Site
```

If this loop works, the rework has preserved Thornwake rather than replacing it.

---

# 97. Suggested Milestones

## Milestone A - Baseline Snapshot
Current playable game preserved and documented.

## Milestone B - World Architecture Technical Decisions
Streaming, terrain representation, natural cave support, terrain modification strategy evaluated and chosen.

## Milestone C - Macro World Generation
Multiple continents/islands, macro terrain, mountains/hills/valleys/coasts.

## Milestone D - Water + Climate + Biomes
Rivers, lakes, water bodies, climate fields, smooth biome blending.

## Milestone E - Natural Caves + Mineral Resource Layer
Cave generation and underground representation functioning, together with deterministic 3D subsurface/mineral deposits, surface outcrops, and cave/deposit exposure.

## Milestone F - World Content Editor Foundation
Editor can create and save World Sites and preview generated local terrain.

## Milestone G - Biome + Terrain Feature Authoring
New biomes and curated procedural features can be added without generator-code rewrites.

## Milestone H - World Sites
Handmade/hybrid/procedural site placement works, including occurrence rules and blending.

## Milestone I - Character Foundation
Modular races/appearance/equipment working in CharacterLab.

## Milestone J - Character Creator
Player can create/save/load appearance.

## Milestone K - Camera and Controls
Modern third-person movement plus seamless first-person zoom.

## Milestone L - Shared World Targeting
One interaction/targeting layer feeds existing gameplay.

## Milestone M - Existing Gameplay Migration
Harvesting, crafting, stations, building, cooking, and terraforming operate correctly in the new architecture.

---

# 98. Important Scope Control

This plan is intentionally ambitious.

Codex should resist the temptation to implement every future feature while building the foundation.

Examples of systems that may remain future work:

- full autonomous NPC economy;
- sophisticated profession progression;
- political/faction simulation;
- complex offline NPC scheduling;
- full water physics;
- scientific tectonics;
- atmospheric circulation simulation;
- advanced erosion during gameplay;
- complex body sculpting;
- dedicated first-person animation library;
- full procedural city economy;
- every conceivable building system.

Architecture should permit them where reasonable without requiring them now.

---

# 99. Decisions That Need Early Validation

Several questions should be resolved early through prototypes/technical evaluation:

### Terrain/caves
Can the current terrain architecture support true natural caves and player excavation? If not, what hybrid/volumetric approach preserves the most existing work?

### Streaming
How should extremely large worlds be partitioned and streamed without visible generation seams?

### Site terrain
How should authored site terrain changes blend into generated terrain while remaining editable?

### Water
How should local water surfaces and gradual flooding integrate with the chosen terrain representation?

### Resource deposits
How should veins, seams, pockets, and shallow deposits be represented so they can intersect caves, cross streaming boundaries, and later be mined persistently?

### Save data
How should player terrain edits, generated-site states, and persistent mineral-deposit depletion persist efficiently?

These are worth solving before large amounts of content depend on a fragile assumption.

---

# 100. What Codex Should NOT Do

Do not:

- restart Thornwake from an empty project;
- delete working crafting/building/harvesting systems without need;
- rebuild every system just to match this document;
- force everything into new terminology;
- make chunk borders define biome borders;
- generate the entire world from a single noise field;
- make all terrain out of reusable presets;
- paste World Sites onto terrain without blending;
- make every site identical;
- make all water sea-level;
- hardcode copper/iron/tin directly into terrain-generation code in a way that prevents easy future resources;
- represent every underground resource only as unrelated collectible nodes;
- generate fake cave-only ore that ignores the persistent deposit layer;
- implement full fluid simulation;
- instantly flood large caves on breach;
- create separate first-person and third-person gameplay systems;
- create separate player and NPC appearance pipelines;
- lock future content creation behind code changes.

---

# 101. What Codex SHOULD Do

Do:

- inspect the existing project first;
- preserve proven systems;
- produce incremental migrations;
- keep the game testable;
- document major architectural decisions;
- build editor/debug tools alongside procedural systems;
- use data-driven definitions for expandable content;
- provide a shared World Asset Library used by world generation, biomes, World Sites, and manual placement;
- make harvestable tree yield respond to physical tree size;
- make World Sites blend naturally;
- use smooth biome weighting;
- make natural caves meaningful;
- generate persistent data-driven mineral/resource deposits that can be buried, exposed, and intersect caves;
- make ordinary new mineral definitions addable without rewriting World Generation;
- treat water as gameplay without turning it into fluid simulation;
- create content templates for Tom;
- reuse the current good art assets;
- design for very large streamed worlds;
- keep future living NPCs in mind without implementing everything now.

---

# 102. Final Vision

The intended result is a Thornwake world that can be regenerated into dramatically different geography while remaining recognizably the same game.

A world might contain:

- several large continents;
- island chains;
- long mountain ranges;
- rolling countryside;
- deep valleys;
- lakes at many elevations;
- rivers and creeks;
- swamp regions;
- natural cave networks;
- buried ore veins and mineral seams;
- cave-wall and cliff outcrops;
- gems, salt, coal, clay, and other discoverable deposits;
- deserts;
- taiga;
- forests;
- volcanic regions;
- fantasy biomes;
- towns;
- castles;
- ruins;
- magical ponds;
- mines;
- villages;
- farms;
- caves;
- unique lore landmarks.

Some content is completely procedural.

Some is procedurally generated from curated terrain/settlement recipes.

Some is handmade.

Some is a hybrid of all three.

A major lore town may always exist but appear on a different continent or beside a different river in every world.

A magical lake may preserve its shrine and distinctive terrain while its surrounding trees, rocks, monsters, and approach differ each time.

Natural terrain should remain organic outside those curated locations.

Biomes should blend instead of forming visible cells.

Caves should exist naturally underground.

Players should be able to gather, craft, build, and reshape the world.

Vegetation should feel like part of that world rather than decoration: a newly added Ash, Oak, Pine, or fantasy tree should be able to spawn through biome/world-generation rules, appear in World Sites, and yield physically sensible amounts of logs plus secondary resources such as branches, bark, leaves, seeds/acorns, resin, or sap where appropriate.

Water should react when players breach it.

Characters should visibly wear what they equip.

The camera should move naturally from wide third person into first person.

The current playable Thornwake loop should survive and become part of this much larger foundation.

And over time, the same world should be capable of supporting inhabitants who live in it, work in it, travel through it, and continue their lives whether or not the player is standing beside them.

That is the purpose of this rework:

> **Do not replace Thornwake. Give the Thornwake that already works the world, tools, characters, and architecture it needs to grow into the game it was meant to become.**
