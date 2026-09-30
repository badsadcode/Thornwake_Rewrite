# THORNWAKE POST-MASTER REWORK ROADMAP
## Items, Crafting, Tools, and Terraforming

---

## 0. Purpose

This document defines the next major development phase for Thornwake **after** the work in `THORNWAKE_MASTER_REWORK_PLAN.md` is complete, stable, integrated, and playable.

This phase should **not begin** until the Master Rework has produced a stable foundation including:

- world generation;
- world streaming;
- hydrology;
- natural cave support;
- deterministic subsurface/mineral resource generation, including persistent ore/mineral deposits and surface outcrops;
- World Content Editor;
- biome authoring;
- terrain feature authoring;
- World Sites;
- modular characters;
- modular visual equipment;
- character creation;
- camera and controls;
- first-person / third-person integration;
- shared world targeting / interaction;
- adaptation of the current playable Thornwake systems to those foundations.

Once that is working, the next focus should be:

```text
ITEM + MATERIAL FOUNDATION
        ↓
CRAFTING + STATION FOUNDATION
        ↓
TOOLS
        ↓
TERRAFORMING
```

Terraforming remains the first major gameplay system to deepen.

However, proper terraforming depends on tools.

Tools depend on items/materials.

Tools also need to be created through crafting.

Therefore the item, crafting, and tool foundations must be defined first, but only deeply enough to support terraforming and future expansion.

The goal is **not** to design every item, weapon, armor set, crop, recipe, profession, or magical artifact before terraforming can work.

This phase should build a clean, reusable foundation and then use terraforming as its first major vertical slice.

---

# 1. Development Philosophy

The main rule for this phase is:

> Build the smallest complete item/crafting/tool foundation that is strong enough for terraforming and future systems.

Avoid two opposite mistakes:

### Too shallow

Do not keep the current pattern where every crafting station is effectively the same recipe window with a different 3D model.

Do not make tools purely cosmetic stat items.

Do not make terraforming a magical editor brush disconnected from the character, inventory, tools, crafting, stamina, or durability.

### Too broad

Do not delay terraforming by fully designing:

- every weapon family;
- magical weapons;
- every armor tier;
- every clothing type;
- backpacks;
- farming;
- animal husbandry;
- advanced cooking;
- enchanting;
- alchemy;
- every trade profession.

Those systems come later.

This phase should create foundations that they can reuse.

---

# 2. High-Level Roadmap

The intended order is:

## Phase 1 - Item and Material Foundation

Define what an item is and what information it can carry.

Add enough material logic for tools and crafting.

## Phase 2 - Crafting and Station Foundation

Differentiate crafting stations by capability and purpose.

Recipes should require appropriate processes, stations, tools, and materials.

## Phase 3 - Tool System

Implement actual functional tools with meaningful differences.

The first tool set should be chosen around terraforming and basic resource processing.

## Phase 4 - Terraforming

Rebuild/deepen terraforming as a real gameplay system driven by tools, terrain properties, water, caves, protected areas, stamina, durability, and the world architecture from the Master Rework.

## Phase 5 - Stabilization

Verify that this vertical slice works cleanly and can be extended later.

## Phase 6 - Future Gameplay Expansion

Only after terraforming is stable should the same item/crafting/tool foundations expand into:

- weapons;
- armor;
- clothing;
- bags;
- farming;
- animal husbandry;
- fishing;
- advanced cooking;
- specialized trades;
- magical equipment;
- broader profession-style gameplay.

---

# 3. Item Foundation

The item system should become the common foundation for objects a player can:

- carry;
- equip;
- use;
- craft;
- consume;
- place;
- store;
- drop;
- trade;
- repair;
- process;
- combine.

The system should be data-driven.

Adding a new ordinary item should mainly involve content/data setup rather than new gameplay code.

---

# 4. Item Categories

The architecture should support multiple broad item families.

Examples include:

- raw resources;
- processed materials;
- tools;
- weapons;
- armor;
- clothing;
- bags;
- food;
- ingredients;
- seeds;
- crafting components;
- placeable objects;
- furniture;
- quest/special items;
- magical items.

Not all categories need to be fully implemented in this phase.

The immediate focus is:

```text
raw resources
processed materials
woodland resources
mineral resources
tools
basic crafting components
```

---

# 5. Shared Item Properties

The item foundation should be capable of representing information such as:

- identity;
- display name;
- description;
- category;
- weight;
- stackability;
- maximum stack;
- durability where relevant;
- quality where relevant;
- value;
- world model;
- inventory icon;
- material composition;
- equipment slot where relevant;
- usable actions;
- crafting tags;
- repairability;
- container behavior where relevant;
- future ownership/faction restrictions if needed.

The exact implementation is left to Codex.

The important point is that different gameplay systems should reuse the same item representation rather than create parallel item concepts.

---

# 6. Item Type Versus Material

The architecture should distinguish the **kind of object** from the **material it is made from** where that distinction is useful.

For example:

```text
Axe
+
material
```

rather than treating every material variant as an unrelated gameplay system.

However, this should not become an unrestricted procedural item generator.

Each item family can define which materials are valid.

Example:

```text
Axe
compatible materials:
- stone
- copper
- bronze
- iron
- steel
```

A shirt would support different materials than an axe.

Unique/special items can still have custom definitions.

---

# 7. Material Foundation

Materials should matter mechanically.

The initial implementation only needs enough depth to support:

- tools;
- basic resource processing;
- mining/extraction;
- terraforming;
- durability;
- crafting requirements.

However, the material architecture should be broad and data-driven enough that later categories can be added without redesigning the item system.

Important long-term families include:

### Wood / Lumber
Start with roughly 3-4 useful wood families rather than dozens.

Example families/species may include:
- Pine / softwood;
- Oak / hardwood;
- Ash / hardwood;
- Birch / lighter wood.

The exact species can change.

Their largest early distinction may be visual/cosmetic for:
- buildings;
- floors;
- walls;
- furniture;
- decorative woodworking.

Avoid tiny stat differences unless they produce meaningful gameplay.

Typical processing:

```text
Tree
→ Species Log
→ Species Plank
→ Beam / Shaped Lumber
```

Examples:

```text
Ash Tree
→ Ash Log
→ Ash Plank
```

```text
Pine Tree
→ Pine Log
→ Pine Plank
```

The World Generation / World Asset system from the Master Rework already defines which tree species exist physically and how large individual trees are.

Harvest yield should therefore come from the real tree instance rather than a fixed arbitrary count.

General rule:

```text
physical tree size
→ amount of logs / branches

wood hardness
→ chopping difficulty

wood species
→ material identity / appearance
```

A huge Pine can yield more timber than a small Oak even if Oak is harder wood.

Secondary tree resources should also be supported:

- Branch;
- Bark;
- Leaves;
- Seeds;
- Acorns/nuts where appropriate;
- Resin;
- Sap;
- Fruit where appropriate;
- fantasy-specific tree materials later.

Not all secondary resources need to be species-specific.

A sensible default is:

```text
species-specific:
- Logs
- Planks
- major lumber products

often generic:
- Branch
- Bark
- Leaves
- Resin
```

while special cases may remain specific:

```text
Oak Acorn
Willow Bark
Pine Resin
Magical Tree Sap
```

### Stone / Earth
Examples:
- stone;
- hard stone;
- gravel;
- sand;
- clay;
- limestone.

These later support:
- cut stone;
- bricks;
- mortar/lime;
- glass;
- pottery;
- roads;
- roofing and construction.

### Ores / Metals
Initial world resources from the Master Rework include:
- copper ore;
- tin ore;
- iron ore.

The crafting system should later support processed metals and alloys such as:
- copper;
- bronze;
- iron;
- steel.

### Fuels / Industrial Minerals
Examples:
- coal;
- charcoal;
- salt;
- peat later;
- sulfur later.

Salt should be treated as a normal extensible material/resource because it can later support cooking, preservation, animal husbandry, trade, alchemy, and other systems.

### Gems / Crystals
Examples:
- quartz;
- diamond;
- emerald;
- ruby;
- sapphire;
- amethyst.

These may later support:
- jewelry;
- trading;
- magical crafting;
- alchemy;
- decorative construction;
- equipment sockets if such a system is ever added.

### Plants
This will eventually become a very broad family:
- seeds;
- grains/wheat;
- fruits;
- berries;
- vegetables;
- mushrooms;
- herbs;
- flowers;
- reeds;
- straw;
- hay;
- fibers;
- medicinal plants;
- poisonous plants;
- magical plants.

These later connect to cooking, farming, animal feed, alchemy, potions, building materials such as thatch/hay roofs, textiles, and magic.

### Animal / Organic Materials
Examples:
- hide;
- leather;
- fur;
- wool;
- feathers;
- bones;
- horns;
- fat;
- meat;
- eggs;
- milk.

### Fibers / Textiles
Examples:
- flax/hemp-like fibers;
- thread;
- yarn;
- cloth;
- rope.

### Liquids / World Substances
Examples:
- water;
- oils;
- milk;
- extracts;
- potion bases.

World water should remain a world substance that can be collected into containers rather than being represented as millions of inventory units.

Lava should primarily remain a world/environmental substance and hazard, with special crafting uses only where deliberately designed.

### Magical / Special Materials
Examples may later include:
- magical crystals;
- monster parts;
- essences;
- fantasy ores;
- enchanted plants.

The exact final list can evolve.

Do not implement every category during the terraforming slice.

The important requirement is:

> Ordinary new materials such as a new gem, ore, wood type, herb, or fantasy resource should be addable mainly through content/data rather than new core item code.

---

# 8. Material Influence

Materials may influence properties such as:

- durability;
- weight;
- tool effectiveness;
- crafting difficulty;
- repair requirements;
- damage later;
- armor later;
- value;
- appearance;
- processing requirements.

For terraforming tools, material should be able to influence:

- how quickly the tool works;
- what terrain hardness it can handle;
- durability;
- stamina efficiency;
- precision;
- effective action strength.

---

# 9. Raw and Processed Resources

The item/crafting foundation should support meaningful processing chains.

Examples:

```text
Tree
→ Species Log
→ Species Plank
→ Beam
```

```text
Tree / Deadwood
→ Branch
→ Primitive Tool / Handle / Fuel
```

```text
Tree
→ Bark / Leaves / Seed / Resin where supported
→ later cooking / alchemy / farming / animal feed / binders / special crafts
```

```text
Ore
→ Ingot
→ Tool Component
```

```text
Hide
→ Leather
```

```text
Fiber
→ Thread
→ Cloth
```

Only chains required for the first tool/crafting slice need to be implemented immediately.

The broader system should allow later expansion.

---



# 9A. Tree and Woodland Resource Chains

The Master Rework should already provide registered tree species that can be spawned by World Generation, biomes, and World Sites.

The Post-Master phase defines what players can do with those trees and their outputs.

A tree can produce several resource categories.

Possible chain:

```text
Tree
├── Species Logs
├── Branches
├── Bark
├── Leaves
├── Seeds / Nuts / Acorns
├── Resin / Sap
└── Fruit / Special Resource where relevant
```

The actual output depends on species and physical tree size.

---

## 9A.1 Logs

Logs are the main timber resource.

Keep approximately 3-4 wood/lumber families initially.

Species-specific logs are useful because they allow:

- different building textures;
- different furniture appearance;
- regional visual identity;
- forestry/trade identity later.

The first implementation does not need major balance differences between every wood type.

---

## 9A.2 Branches

Branches are an important early-game material.

They may be obtained from:

- cutting/felling trees;
- small trees;
- fallen branches in the world;
- deadwood scatter;
- pruning/harvesting later.

Branches can support:

- primitive tools;
- tool handles;
- fire/kindling;
- cooking spits;
- stakes;
- arrows/shafts later;
- traps later;
- crude fencing;
- simple shelters;
- torches;
- other early survival recipes.

Prefer a generic `Branch` resource initially unless species-specific branches provide real gameplay value.

This helps avoid inventory clutter.

---

## 9A.3 Bark

Bark may support:

- primitive fire starting;
- tanning processes later;
- medicine/herbal recipes;
- dyes;
- fibers/cordage for some species;
- roofing or primitive containers in special cases;
- alchemy;
- species-specific special recipes.

Bark can usually remain generic initially, with special species-specific bark added later where useful.

---

## 9A.4 Leaves

Leaves may support:

- compost later;
- animal bedding/feed for certain species;
- herbalism/alchemy;
- dyes;
- decoration;
- primitive roofing/insulation where appropriate;
- magical recipes.

Do not force all leaf litter into inventory by default.

Leaves should become an item only when harvested or when a recipe/system needs them.

---

## 9A.5 Seeds, Nuts, and Acorns

Trees may provide reproduction/food resources such as:

- acorns;
- nuts;
- pine cones;
- seeds;
- fruit pits.

These may later support:

- forestry;
- farming/tree cultivation;
- cooking;
- animal feed;
- trade;
- alchemy.

Species-specific identity matters more here than for generic branches.

Example:

```text
Oak Tree
→ Oak Acorn
```

---

## 9A.6 Resin and Sap

Some trees can provide:

- resin;
- sap;
- pitch-like products;
- syrup-like food products for suitable species;
- magical sap.

Potential uses later include:

- glue;
- waterproofing;
- torches;
- fire starters;
- bow/furniture crafting;
- alchemy;
- medicine;
- magical recipes.

Resin/sap harvesting may eventually use dedicated tools or tapping mechanics.

It does not need to be fully implemented during the first terraforming slice.

The material architecture simply needs to support it cleanly.

---

## 9A.7 Yield From Physical Tree Size

The harvesting system should use the physical tree instance created by the Master Rework.

A physically larger tree should generally yield:

- more logs;
- more branches;
- potentially more bark/leaves.

Do not use one fixed yield for every model of a species.

The calculation can be an approximation using:

- trunk dimensions;
- model scale;
- maturity category;
- species yield factor.

The goal is plausibility and consistency, not forestry simulation accuracy.

---

## 9A.8 Early-Game Progression

Small scattered woodland resources provide a good primitive progression:

```text
find fallen branches
↓
craft primitive tool / fire
↓
harvest small trees
↓
obtain more branches + first logs
↓
build/process timber
↓
make proper tools/stations
```

This avoids requiring a proper metal axe before the player can interact meaningfully with wood.

---

# 9B. World Deposits Already Exist After the Master Rework

The Post-Master phase should **not generate ore veins or mineral deposits from scratch**.

By the time this roadmap begins, the Master Rework should already provide:

- buried veins;
- seams;
- pockets/clusters;
- shallow beds;
- surface outcrops;
- cave/deposit intersections;
- stable deposit identity;
- persistent deposit state capability.

This phase consumes that world data.

Its responsibility is to define:

- how the player recognizes/extracts the resource;
- which tools work;
- what items are produced;
- how deposits become depleted;
- how raw materials are processed;
- how those processed materials become tools and later other equipment.

This separation prevents world generation and crafting from duplicating each other's responsibilities.

---

# 9C. Initial Metal Processing and Alloys

The crafting foundation should be capable of processing world-generated ores into usable materials.

The first useful chains may include concepts such as:

```text
Copper Ore
→ Copper
```

```text
Tin Ore
→ Tin
```

```text
Copper + Tin
→ Bronze
```

```text
Iron Ore
→ Iron
→ Steel
```

The exact historical/metallurgical realism is not the goal.

The recipes/processes should be believable, understandable, and useful for gameplay.

Steel may require an appropriate carbon/fuel source and more advanced processing than basic iron.

The first terraforming vertical slice only needs whichever metal tiers are required for the initial tool progression.

The architecture should permit later expansion into weapons, armor, fittings, nails, decorative metalwork, and other crafts.

---

# 10. Avoid Material Explosion

Do not create unnecessary complexity simply because materials exist.

For example, the game does not need twenty nearly identical timber species with microscopic stat differences unless that provides real gameplay value.

Material distinctions should exist when they affect:

- crafting;
- availability;
- visual identity;
- performance;
- specialization;
- world ecology.

The system should support depth without requiring pointless granularity.

---

# 11. Crafting Foundation

Crafting should stop behaving as a generic recipe list attached to differently shaped stations.

Stations should represent different **capabilities/processes**.

A recipe should be craftable because the required process exists.

Not because it happens to appear in a station's UI list.

---

# 12. Crafting Processes

Examples of crafting/process capabilities include:

- cutting;
- sawing;
- shaping wood;
- carving;
- cooking;
- boiling;
- roasting;
- smelting;
- forging;
- grinding;
- tanning;
- sewing;
- assembling;
- repairing.

Only a subset needs implementation now.

The first subset should support:

- wooden handles/components;
- planks;
- metal tool heads;
- basic tool assembly;
- repairs.

---

# 13. Crafting Stations Should Be Meaningfully Different

Examples:

### Cooking Station
Suitable for:
- cooking;
- boiling;
- roasting;
- food preparation.

Not suitable for:
- sawing logs into planks.

### Woodworking / Sawing Station
Suitable for:
- cutting logs;
- creating planks;
- beams;
- shaped wood components.

### Carpenter's Bench
Suitable for:
- handles;
- furniture components;
- wooden assemblies;
- tool shafts.

### Forge / Smithing Station
Suitable for:
- metal tool heads;
- metal components;
- future weapons;
- future armor.

### Smelting Station
Suitable for:
- processing ore into usable metal.

These distinctions should become part of station behavior/data.

---

# 14. Station Capabilities

A station should communicate what it is capable of.

Recipes should check for appropriate station/process capability.

This means future stations can share some capabilities without being identical.

For example:

- a simple campfire can roast;
- a kitchen stove can roast and boil;
- an advanced kitchen may support more cooking processes.

Likewise:

- a simple hand saw setup may process basic timber;
- an advanced saw bench may work faster or support larger beams.

This provides progression without duplicating entire systems.

---

# 15. Crafting Tools

Some crafting processes may require tools in addition to stations.

Examples:

- saw;
- hammer;
- tongs;
- knife;
- chisel.

A station may provide some tools intrinsically, or the player may need them.

This can be expanded later.

The first implementation should avoid excessive friction.

The goal is meaningful crafting logic, not turning every recipe into five prerequisite menus.

---

# 16. Recipe Structure

Recipes should be able to express:

- inputs;
- outputs;
- required process/capability;
- required station type/capability;
- required tools where relevant;
- time/work amount where relevant;
- future skill requirements if added;
- byproducts where useful.

Recipes should be reusable by player and future NPC crafting systems.

This is important for the long-term living-world goal.

---

# 17. Manual Crafting Versus Station Crafting

Some simple items may be craftable directly by the player.

Examples could include:

- primitive bindings;
- simple stone tools;
- crude handles;
- basic camp items.

More advanced items should require appropriate stations.

This allows survival progression without making every early action station-dependent.

The exact balance can be tuned later.

---

# 18. Crafting Progression

The first crafting progression should be intentionally small.

A possible early path:

```text
gather wood/stone
↓
make primitive tools
↓
process timber
↓
build basic workstation
↓
process metal
↓
make better tools
```

This phase does not need a complete technology tree.

The purpose is to support tool progression for terraforming.

---

# 19. Tool System

Tools should be functional gameplay objects.

A tool should define what actions it can perform and how well it performs them.

The first major tool families should support terrain work and basic resource processing.

---

# 20. Initial Tool Families

Recommended initial tool set:

### Shovel
Primary use:
- dig soil;
- move loose terrain;
- fill;
- raise;
- some leveling.

### Pickaxe
Primary use:
- break hard terrain;
- excavate rock;
- mine stone;
- future ore extraction.

### Hoe / Rake
Primary use:
- level;
- smooth;
- prepare paths;
- future farmland preparation.

### Axe
Primary use:
- tree harvesting;
- wood-related work;
- possible emergency weapon use later.

### Saw
Primary use:
- timber processing.

### Hammer
Primary use:
- construction;
- repair;
- future crafting/assembly.

Not every tool needs all final behaviors immediately.

---

# 21. Tools Should Have Action Compatibility

Each tool should know which kinds of work it is intended for.

Examples:

```text
Shovel:
- good for soil
- poor for hard rock

Pickaxe:
- good for rock
- poor for loose soil shaping

Hoe:
- good for leveling/smoothing
- not suitable for deep excavation
```

This makes tool choice meaningful.

---

# 22. Tool Material Progression

Tool families may exist in several material tiers.

Examples:

```text
Stone
Copper
Bronze
Iron
Steel
```

The actual progression can be adjusted later.

Material/tier can influence:

- durability;
- effectiveness;
- stamina cost;
- action speed;
- what hardness can be worked;
- repair requirements;
- weight;
- precision.

---

# 23. Better Tools Should Not Simply Make Bigger Holes

Tool progression should feel better without turning advanced tools into uncontrollable terrain-destruction devices.

Different concepts should be separable:

- speed;
- strength;
- radius;
- precision;
- stamina efficiency;
- durability;
- terrain compatibility.

A high-tier shovel may dig faster and more efficiently without automatically excavating a giant crater.

---

# 24. Tool Durability

Terraforming tools should be able to wear down.

Durability should:

- decrease from meaningful use;
- be influenced by tool material;
- be influenced by terrain hardness;
- allow repair where appropriate.

Avoid excessive durability micromanagement.

Tools should not break every few minutes unless the game deliberately wants primitive early tools to feel fragile.

---

# 25. Repair

The crafting foundation should support repairing tools.

Repair may require:

- appropriate station;
- compatible material;
- some fraction of original resources;
- future repair skill or quality considerations.

The first implementation can remain simple.

---

# 26. Tool Quality

The item architecture should allow future quality.

Quality can later affect:

- durability;
- effectiveness;
- value;
- precision;
- visual rarity.

Quality does not need a deep procedural crafting system during the first terraforming slice.

The architecture simply should not block it.

---

# 27. Terrain Material / Hardness

Terraforming needs some concept of what the player is digging.

The terrain system should be able to distinguish meaningful categories such as:

- loose soil;
- normal soil;
- clay-like ground;
- sand;
- gravel;
- soft rock;
- hard rock;
- special/magical terrain;
- protected terrain.

These do not necessarily need to correspond one-to-one with rendered ground textures.

The point is to give tools meaningful compatibility.

---

# 28. Terrain Hardness Should Remain Playable

Do not turn terrain into a geology spreadsheet.

Players should understand the result through:

- tool feedback;
- action speed;
- sound;
- animation;
- UI hints where needed.

Example:

- shovel quickly digs normal soil;
- shovel struggles against rock;
- pickaxe works effectively against rock.

This is enough.

---

# 29. Terraforming - Core Goal

Terraforming should become a real gameplay system rather than a developer-style terrain editor exposed to the player.

The player:

- equips a tool;
- targets terrain;
- performs an action;
- uses stamina/time;
- wears the tool;
- modifies the world;
- receives clear visual/audio feedback.

The system should integrate with:

- the terrain representation selected during Master Rework;
- natural caves;
- player-dug cavities if supported;
- hydrology;
- building;
- roads;
- protected areas;
- World Sites;
- saving;
- multiplayer.

---

# 30. Core Terraforming Actions

The initial action set should include:

### Dig
Remove terrain downward.

### Fill / Raise
Add terrain using appropriate material or an abstraction of moved earth.

### Level
Bring terrain toward a chosen/reference height.

### Smooth
Reduce harsh terrain changes.

### Rock Excavation
Remove harder terrain using appropriate tools.

### Path Work
Create or improve walkable/path terrain.

The exact UI can be refined after playtesting.

---

# 31. Digging

Digging should:

- require an appropriate tool;
- target the terrain under the player's current aim;
- respect terrain hardness;
- respect protected areas;
- interact with caves/underground representation;
- interact with nearby water.

The digging action should feel physical and readable.

---

# 32. Fill / Raise

Raising terrain should not necessarily be free.

Possible approaches include:

- using excavated soil as a resource;
- using loose-earth inventory material;
- abstracting local material movement.

The final design can be decided after prototyping.

The system should avoid unlimited magical terrain creation unless that is an intentional design choice.

---

# 33. Leveling

Leveling should help:

- prepare house sites;
- prepare roads;
- prepare farms later;
- create usable courtyards;
- repair rough terrain.

The action should preserve natural-looking transitions around the edges.

Avoid creating obvious hard square platforms unless the player deliberately makes one.

---

# 34. Smoothing

Smoothing should:

- soften harsh player-created terrain;
- blend raised/lowered areas;
- create more natural slopes;
- help integrate construction sites;
- help repair ugly terrain artifacts.

It should be a controlled player action rather than an automatic system that undoes deliberate work.

---

# 35. Path Creation

Path tools should be able to create practical paths.

Potential effects:

- flatten small irregularities;
- change surface material;
- remove dense small vegetation;
- create a visual path;
- optionally improve movement speed later.

Path creation should connect naturally with the road/terrain systems from Master Rework.

---

# 36. Player-Dug Caves and Tunnels

If the Master Rework terrain architecture supports true player excavation underground, terraforming should allow:

- tunnels;
- chambers;
- mine shafts;
- underground rooms;
- cave connections.

This must follow the technical constraints chosen during the terrain/cave architecture phase.

Do not fake player-dug caves with visuals if the underlying collision/terrain cannot support them reliably.

---

# 37. Natural Cave Interaction

Players should be able to modify natural caves where permitted.

Examples:

- widen a narrow section;
- mine through a wall;
- connect two cave passages;
- create a new exit;
- accidentally break into underground water;
- build underground where construction rules allow.

Important lore World Sites may protect specific cave geometry.

---

# 38. Water Interaction

Terraforming must reuse the water rules from the Master Rework.

Digging near water:

- does not instantly create water;
- only matters when a real connection exists.

If a breach is created:

- determine the connected floodable area;
- determine the local source water level;
- begin gradual flooding;
- scale practical inflow to source type/opening size;
- allow the player time to react.

Flooding should be dangerous but understandable.

---

# 39. Flooding and Tool Actions

Terraforming actions can create or prevent water connections.

Examples:

- dig through lake bank;
- dig into river channel;
- open cave ceiling beneath lake;
- create drainage trench;
- raise earth barrier;
- repair a breach.

The system should work through terrain connectivity rather than true real-time fluid physics.

---

# 40. Protected Terrain

Existing protected-area behavior should be preserved and integrated.

Examples of potentially protected content:

- roads;
- important buildings;
- lore-critical World Sites;
- settlement cores;
- key quest structures;
- special terrain features.

Protection can have different strengths.

Possible policies:

### Fully Protected
No terrain editing.

### Foundation Protected
Terrain immediately supporting the structure is protected.

### Limited
Small smoothing allowed, deep excavation blocked.

### Unprotected
Full player freedom.

The exact implementation should reuse World Site and structure metadata where possible.

---

# 41. Player Buildings

Player buildings should interact safely with terrain editing.

The system should avoid:

- floating foundations;
- unsupported structures due to tiny terrain edits;
- accidental destruction of an entire house from minor smoothing;
- terrain clipping through floors.

Possible behavior may include:

- protected foundation zones;
- support recalculation;
- warnings;
- restricted excavation immediately beneath critical structure parts.

The final solution should match Thornwake's existing building architecture.

---

# 42. Terrain Feedback

Players need clear feedback while terraforming.

Useful feedback includes:

- visible target area;
- current action;
- whether terrain can be modified;
- protected-area indication;
- wrong-tool feedback;
- hard-terrain feedback;
- water-danger feedback where appropriate;
- tool durability feedback;
- stamina feedback.

Avoid excessive UI clutter.

World-space feedback is preferred where practical.

---

# 43. Terraforming Visual Brush

Even though the player is using a physical tool, it is acceptable to show a world-space influence preview.

The preview may show:

- affected area;
- intended level height;
- smooth radius;
- protected boundary;
- invalid target.

This helps players make precise terrain edits without making the system feel like a developer editor.

---

# 44. Tool Animation and Terrain Action

Terraforming should visually use the equipped tool.

Examples:

- shovel digs;
- pickaxe strikes rock;
- hoe/rake shapes ground.

The actual terrain edit can be synchronized with animation timing.

Gameplay responsiveness is more important than perfect simulation.

---

# 45. Stamina

Terraforming actions should be able to consume stamina.

Factors may include:

- tool type;
- material/tier;
- terrain hardness;
- size of action;
- player skill later.

Do not tune stamina so aggressively that ordinary building preparation becomes tedious.

---

# 46. Action Speed

Tool effectiveness may influence action duration.

Examples:

- primitive shovel takes longer;
- iron shovel works faster;
- steel shovel works efficiently.

Large terrain modifications should require proportionally more work than tiny corrections.

---

# 47. Precision

Some tools may be better for precision than others.

For example:

- hoe/rake may provide excellent leveling control;
- shovel may move more earth;
- pickaxe may precisely remove rock.

This helps tools remain distinct beyond speed.

---

# 48. Removed Terrain / Material Accounting

A design decision is needed for excavated material.

Possible approaches:

### Full Material Accounting
Digging creates soil/stone resources.

Advantages:
- strong physical consistency;
- supports construction/filling.

Disadvantages:
- inventory spam;
- balance complexity.

### Abstract Earth Movement
Digging creates an abstract local terrain reserve or simply permits nearby fill.

Advantages:
- less inventory clutter.

### Hybrid
Rock excavation creates useful stone resources.
Normal soil uses simplified earth handling.

This should be prototyped.

For Thornwake, the hybrid approach may provide the best balance.

Codex should not commit to a complicated material-volume economy without testing playability.

---

# 49. Mining Relationship

Rock excavation and resource mining should share compatible foundations.

The Master Rework already generated persistent mineral deposits in the world.

A pickaxe can:

- break ordinary terrain rock;
- harvest ordinary stone;
- expose a previously buried deposit;
- extract material from an exposed ore/mineral deposit.

An ore visible in a cave wall should represent the same persistent deposit that continues into the surrounding rock where applicable.

Mining into that wall may therefore allow the player to follow the vein rather than harvesting an unrelated standalone node.

Generic rock terrain must not generate valuable ore merely because it was repeatedly dug and replaced.

Valuable mineral output should come from actual generated deposits or deliberate resource objects.

Deposit depletion must persist.

The resource system should remain controllable and game-friendly rather than attempting to simulate individual mineral grains.

---

# 50. Terraforming and Generated Deposits

Terraforming should not become an infinite resource exploit.

The world already contains generated mineral/resource deposits.

Terrain excavation should reveal or remove those deposits where they physically intersect the modified terrain.

Generic terrain must not spontaneously create new copper, iron, tin, gems, salt, or other valuable deposits.

If digging produces usable resources, limits/logic should prevent:

- infinite valuable ore from generic terrain;
- arbitrary duplication through raise/dig cycles;
- unlimited valuable stone/mineral output from recycled fill;
- resetting a depleted deposit by smoothing or raising terrain.

Ordinary fill material and valuable generated resources should be treated differently where necessary.

---

# 51. Multiplayer Considerations

Terraforming changes world state.

The system must support:

- authoritative terrain modification;
- synchronization;
- persistent edits;
- protection checks;
- water breach/flood-state synchronization;
- building interaction.

The exact multiplayer implementation depends on Thornwake's existing/network architecture.

The important requirement is that terrain changes are not client-only visual effects.

---

# 52. Saving Terrain Changes

Player terrain edits must persist efficiently.

A saved world should remember:

- modified terrain;
- tunnels/caves where supported;
- raised/leveled areas;
- paths;
- protected-zone interactions;
- water breaches;
- persistent flood state where required.

Do not save a full duplicate of the entire generated world if delta-based persistence is more appropriate.

Use the terrain architecture chosen during Master Rework.

---

# 53. Undo Is Not a Gameplay Requirement

The editor/dev tools may support undo.

Normal player terraforming does not need a magical undo button.

Players should repair terrain through gameplay actions.

However, accidental destructive changes near protected content should be prevented rather than relying on undo.

---

# 54. Terraforming and World Sites

World Sites should define how editable they are.

Examples:

- ordinary wilderness ruin: editable;
- town outskirts: mostly editable;
- important road: protected;
- unique magical pond: perhaps partially protected;
- legendary castle foundation: protected;
- procedural camp: fully editable.

World Site rules from Master Rework should provide this information.

---

# 55. Terraforming and Biomes

Terraforming changes terrain shape.

It may also affect local surface presentation.

Examples:

- path action changes ground material;
- digging exposes soil/rock;
- deep excavation may reveal different subsurface material;
- smoothing restores local ground presentation.

Do not automatically rewrite broad biome identity just because the player digs a hole.

Biome influence and player surface changes should coexist.

---

# 56. Terraforming and Vegetation

Terrain edits should safely interact with vegetation.

Potential behavior:

- small plants removed when terrain beneath them changes substantially;
- trees may block terrain editing immediately beneath trunks;
- extreme terrain removal may cause tree removal/falling;
- site-authored protected trees may resist editing;
- future regrowth should respect modified terrain.

Avoid floating trees.

---

# 57. Terraforming and Roads

Road protection already exists and is valuable.

Preserve it.

Future road behavior may include:

- hard protection for major generated roads;
- softer protection for small paths;
- player-created paths;
- repairable roads;
- ability to intentionally destroy roads only through specific gameplay if ever desired.

The initial goal is simply not to break roads accidentally.

---

# 58. Developer Terraforming Tools

The World Content Editor needs stronger terrain-editing tools than normal players.

Do not force editor tools to obey player stamina/durability rules.

Separate:

```text
developer/editor terrain tools
```

from:

```text
player terraforming gameplay
```

They may share the same underlying terrain-modification API.

---

# 59. Testing the Item Foundation

Before moving on, validate:

- items load correctly;
- materials influence tool data;
- durability works;
- item saves/loads;
- dropped items work;
- species-specific logs/planks retain material identity;
- generic branch resources avoid unnecessary species clutter;
- tree harvest yield reacts sensibly to physical tree size;
- inventory remains stable;
- existing resource items still work or migrate safely.

---

# 60. Testing Crafting

Validate:

- cooking station does not offer woodworking recipes;
- woodworking station can process species logs into matching species planks;
- branches can participate in appropriate primitive/handle/fuel recipes;
- forge/smithing station can process appropriate world-mined materials and produce metal tool parts;
- copper/tin processing can support bronze where that tier is included;
- recipes correctly enforce required capability;
- stations remain usable with the shared interaction system;
- existing crafting saves/content migrate where practical.

A direct regression test should exist for:

> logs cannot be sawn into planks at a cooking station.

This is a useful simple proof that station specialization works.

---

# 61. Testing Tools

Validate:

- axe harvesting of trees respects tree physical size and wood hardness;
- larger trees produce more timber than small trees of the same species;
- shovel works well on soil;
- shovel performs poorly or not at all on hard rock;
- pickaxe works on rock;
- pickaxe can expose and mine a generated mineral deposit;
- a cave-wall ore exposure and a player-exposed section of the same deposit behave consistently;
- hoe/rake levels/smooths;
- tool material/tier changes meaningful performance;
- durability changes from use;
- repair works;
- equipped visuals match the modular Character System.

---

# 62. Testing Terraforming

Test at minimum:

### Basic
- dig;
- raise/fill;
- level;
- smooth;
- path creation;
- rock excavation.

### Structures
- near player house;
- under foundation;
- near road;
- near generated town;
- near protected World Site.

### Water
- next to dry lake bank;
- create tiny breach;
- create large breach;
- cave below lake;
- riverbank;
- ocean edge;
- drainage/outflow if supported.

### Caves
- natural cave;
- player tunnel if supported;
- cave entrance;
- underground water.

### Streaming
- modify terrain;
- leave region;
- return;
- verify persistence.

### Multiplayer
- second player sees same terrain edit;
- second player sees same flood state.

---

# 63. Terraforming Vertical Slice Completion Criteria

This phase is successful when a player can:

```text
gather fallen branches and basic woodland resources
↓
harvest trees whose size affects timber yield
↓
discover/mine generated mineral deposits
↓
craft/build the appropriate stations
↓
process wood/metal
↓
craft a shovel, pickaxe, and leveling tool
↓
equip them
↓
dig terrain
↓
excavate rock
↓
level a building site
↓
smooth ugly terrain
↓
create a path
↓
repair/maintain tools
↓
interact safely with roads/buildings
↓
accidentally or deliberately breach water
↓
experience gradual flooding
↓
save/reload and keep those world changes
```

At that point Thornwake has a complete gameplay slice tying together:

- gathering;
- items;
- materials;
- crafting;
- stations;
- tools;
- character equipment;
- terrain;
- water;
- caves;
- building;
- persistence.

That is a strong foundation for the rest of the game.

---

# 64. What Comes After Terraforming

Only after the terraforming slice is stable should the broader item/crafting ecosystem expand.

Recommended future sequence can be flexible, but major areas include:

## Weapons
- melee;
- ranged;
- magical;
- shields;
- ammunition;
- repair;
- quality;
- special effects.

## Armor
- modular armor;
- material tiers;
- protection types;
- durability;
- repair.

## Clothing
- non-combat clothing;
- climate/weather value later;
- visual identity;
- profession-style outfits.

## Bags and Backpacks
- carrying capacity;
- weight tradeoffs;
- visible equipment;
- specialty storage later.

## Farming
- soil preparation;
- seeds;
- crops;
- harvesting;
- irrigation only if it provides real gameplay.

## Animal Husbandry
- feeding;
- breeding;
- shelter;
- products;
- livestock management.

## Fishing
- rods/nets/traps where appropriate;
- water-body-specific fish;
- processing/cooking.

## Advanced Crafting
- carpentry;
- furniture;
- smithing;
- tailoring;
- leatherworking;
- advanced cooking;
- specialized stations.

## Player Roles
As mechanics deepen, players naturally gain the ability to live as:
- carpenter;
- furniture maker;
- hunter;
- fisherman;
- farmer;
- miner;
- lumberjack;
- smith;
- cook;
- builder;
- trader;
- bounty hunter;
- explorer;
- other world-supported roles.

Do not force these into rigid classes unless a later design explicitly wants that.

---

# 65. Long-Term Item Philosophy

A useful item should participate in several systems naturally.

Tree resources are a good example: an Ash tree in the generated world can produce Ash logs for visible Ash lumber, generic branches for early crafting, and optional bark/leaves/seeds/resin for later systems without requiring separate disconnected harvesting implementations.

Example:

A steel axe can be:

- an inventory item;
- an equipped item;
- a harvesting tool;
- an emergency melee weapon if allowed;
- a crafted object;
- a repairable object;
- a material-dependent object;
- a visible character asset;
- a dropped world object;
- something an NPC lumberjack can use later.

Avoid duplicating separate concepts of "axe" for every system.

---

# 66. Long-Term Crafting Philosophy

Crafting should become part of the living world.

Eventually:

- players craft;
- NPCs craft;
- workshops matter;
- production chains matter;
- settlements may have real output;
- traders may obtain goods from the world;
- professions emerge from mechanics.

The first post-master phase does not need to implement all of this.

But the item/crafting foundation should be reusable by those future systems.

---

# 67. Long-Term Tool Philosophy

Tools should give players capabilities.

Examples:

- axe gives woodcutting capability;
- saw gives timber-processing capability;
- pickaxe gives rock/mining capability;
- shovel gives excavation capability;
- hoe gives terrain/farming preparation;
- hammer gives construction/repair capability;
- fishing gear gives fishing capability.

This is stronger than tools merely being stat bonuses.

---

# 68. Important Scope Control

Do not allow the first terraforming slice to balloon into every future system.

Do not delay terraforming to implement:

- complete smithing progression;
- hundreds of recipes;
- enchanting;
- legendary weapons;
- full farming;
- animal breeding;
- complex profession trees;
- advanced economy;
- NPC production simulation.

Implement only what is needed to make the terraforming vertical slice coherent and extensible.

---

# 69. What Codex Should Preserve

When this phase begins, preserve all successful work from the Master Rework:

- world generator;
- World Asset Library and registered vegetation/tree species;
- size-aware tree/vegetation resource metadata;
- terrain representation;
- natural caves;
- water;
- persistent ore/mineral/resource deposits generated by the Master Rework;
- World Sites;
- protection metadata;
- characters;
- modular equipment;
- camera;
- world targeting;
- existing gathering;
- existing crafting data where salvageable;
- existing stations where salvageable;
- existing building;
- existing terraforming backend where useful.

Do not restart these systems.

---

# 70. What Codex Should Refactor

Refactor only where the existing system prevents the goals of this phase.

Likely areas include:

- generic crafting-station behavior;
- recipe filtering/capabilities;
- item/material metadata;
- extraction/mining behavior that consumes existing generated deposits;
- tool actions;
- terrain targeting;
- terrain action rules;
- durability/repair if absent.

Changes should remain incremental.

---

# 71. Recommended Codex Workflow

For each major subsystem:

1. inspect existing implementation;
2. document what can be reused;
3. identify minimum required change;
4. implement foundation;
5. create a small playable test;
6. verify old gameplay still works;
7. proceed to the next dependency.

Avoid attempting the entire post-master plan in one untestable pass.

---

# 72. Suggested Milestones

## Milestone A - Item Foundation
Items/materials support the information needed by tools and crafting.

## Milestone B - Crafting Capabilities
Stations are meaningfully specialized and recipes enforce correct processes.

## Milestone C - Tool Set
Shovel, pickaxe, leveling tool, saw, axe, hammer function as real tools.

## Milestone D - Basic Terraforming
Dig, fill, level, smooth, rock excavation, paths.

## Milestone E - Structures and Protection
Terraforming behaves safely around roads, buildings, and World Sites.

## Milestone F - Water Integration
Breaches cause gradual flooding.

## Milestone G - Cave Integration
Natural/player-created underground terrain interacts correctly with tools and water.

## Milestone H - Persistence and Multiplayer
Terrain/tool state persists and synchronizes.

## Milestone I - Terraforming Vertical Slice Complete
The full gameplay loop from resource gathering to crafted tools to meaningful world modification is stable.

---

# 73. Final Target Experience

After the Master Rework, a player should eventually be able to:

```text
walk into a generated forest
↓
cut down trees
↓
gather stone and ore
↓
process logs at a proper woodworking/sawing station
↓
process metal at appropriate stations
↓
craft a shovel and pickaxe
↓
equip the real visible tools
↓
prepare a site for a house
↓
level the ground
↓
dig a cellar
↓
excavate rock
↓
smooth the surrounding terrain
↓
create a path
↓
build the house
↓
repair worn tools
↓
continue digging underground
↓
discover or connect to a natural cave
↓
accidentally break into the bottom of a lake
↓
hear water rushing in
↓
watch the cave gradually flood
↓
run for their life
```

That sequence should feel like one coherent game, not several unrelated systems.

---

# 74. Final Guiding Rule

> **After the Master Rework, deepen Thornwake by building the item, crafting, and tool foundations only as far as necessary to make terraforming a complete, physical, persistent gameplay system. Then expand the same foundations into the rest of the game.**

Terraforming is the first major post-rework gameplay target.

Items, crafting, and tools are its dependencies.

They are not separate detours.

They are the foundation that makes terraforming belong to Thornwake rather than feel like an editor tool attached to it.
