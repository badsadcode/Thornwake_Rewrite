# Persistent Fantasy Life RPG
## Game Design Bible & Technical Direction
**Version:** 0.1  
**Status:** Pre-production / Living Design Document  
**Primary purpose:** Single source of truth for design, architecture, content direction, and implementation decisions.  
**Intended reader:** Human developers, AI coding agents (Codex), technical artists, modders, content creators.

---

# 0. IMPORTANT INSTRUCTIONS FOR CODEX / AI CODING AGENTS

This document describes the intended game. Treat it as the design and architecture source of truth unless a later document explicitly supersedes it.

## 0.1 Priorities

When making implementation decisions, prioritize in this order:

1. **Finishability**
2. **Stable architecture**
3. **Good game feel**
4. **Moddability and content scalability**
5. **Performance**
6. **Simulation depth**
7. **Visual polish**
8. **Feature quantity**

Do not blindly add features because they sound interesting. A smaller polished system is preferred over a larger unfinished one.

## 0.2 General engineering rules

- Do not hardcode content that can reasonably be data-driven.
- Avoid giant god classes and tightly coupled systems.
- Prefer reusable systems over one-off special cases.
- Prefer deterministic procedural generation where practical.
- Design all major systems with multiplayer and persistence in mind from the start.
- Server-authoritative simulation is the default assumption.
- Do not rewrite working systems without a clear technical reason.
- Add automated tests for procedural generation, world persistence, item definitions, crafting, settlement logic, economy rules, and save compatibility where feasible.
- Keep mod API boundaries explicit.
- Avoid allowing mods to depend on private internal implementation details.
- Favor additive extensibility: a mod should usually be able to add a new item, crop, creature, profession, building, event, recipe, skill, prefab, biome, dungeon room, or NPC archetype without editing base-game code.
- Use comments to explain *why* a design decision exists, not merely what the code does.
- Keep save formats versioned.
- Preserve world seeds and deterministic placement rules wherever feasible.
- No feature is complete until it handles save/load and multiplayer correctly if applicable.

## 0.3 Design philosophy

The game is not intended to be:

- a linear quest RPG,
- a theme park MMO,
- a Minecraft clone,
- a pure survival punishment simulator,
- a pure colony manager,
- a pure action game,
- a pure crafting game,
- or an endless pile of disconnected systems.

The game is intended to be:

> **A persistent multiplayer fantasy sandbox where the world lives without the players, settlements evolve, professions have genuine depth, and players decide what kind of life they want to live.**

---

# 1. HIGH-LEVEL VISION

The game combines the player freedom and persistence of Project Zomboid-style sandbox design, the open-ended progression and settlement-building appeal of Necesse, the grounded stylized atmosphere of Valheim, the emergent world stories of colony/simulation games, and a fantasy RPG structure built around exploration rather than quest-marker spam.

The player is not necessarily "The Chosen One."

The player is a person living inside a generated fantasy world.

That person may become:

- an adventurer,
- a hunter,
- a farmer,
- a blacksmith,
- a carpenter,
- a fisherman,
- a leatherworker,
- a trader,
- a caravan master,
- an innkeeper,
- a dungeon delver,
- a village defender,
- a builder,
- an animal breeder,
- a tailor,
- a forester,
- a miner,
- a sailor,
- or some strange combination of all of the above.

The game should allow several players to spend the same evening doing completely different things while all of them are playing correctly.

Example:

- Player A explores a dungeon.
- Player B spends the evening rebuilding a mill.
- Player C breeds better sheep.
- Player D runs a caravan between two towns.
- Player E improves the village defenses.
- Player F fishes and sells food at the market.

All of these activities should connect to the same world economy and simulation.

---

# 2. CORE DESIGN PILLARS

## 2.1 The world lives without the player

Settlements, NPCs, caravans, bandits, wildlife, production, shortages, trade, and local problems should continue to exist and evolve without direct player involvement.

The world must not feel frozen until the player arrives.

Examples:

- A caravan fails to arrive because bandits attacked it.
- A blacksmith runs short of iron.
- Tool production falls.
- Farmers begin struggling to replace equipment.
- Food production drops.
- Prices change.
- The village sends guards or hires mercenaries.
- Another settlement may intervene.
- The bandits may move, grow stronger, collapse, or be destroyed by another threat.
- The player may hear about the situation naturally, investigate it, exploit it, ignore it, or arrive after it has already resolved.

## 2.2 No mandatory quest spam

The world should create **situations**, not constantly push objective cards into the player's face.

Avoid the default RPG pattern:

> QUEST: Kill 8 Bandits  
> Reward: 450 XP

Prefer diegetic information:

- tavern rumors,
- NPC conversations,
- market shortages,
- smoke on the horizon,
- missing caravans,
- damaged buildings,
- wanted notices,
- road travelers,
- town criers,
- letters,
- local gossip,
- visible military preparations.

Formal quests can exist, but they should not dominate the game.

## 2.3 Players choose their own life

Players should rarely be forced into one gameplay role.

The game must support:

- peaceful play,
- crafting-focused play,
- combat-focused play,
- settlement-focused play,
- exploration,
- trade,
- social play,
- deep progression,
- economic specialization,
- roleplay,
- solo wandering.

## 2.4 Deep systems under approachable controls

The game should be easy to operate but hard to master.

A new player should understand movement, gathering, basic combat, inventory, and simple crafting quickly.

However, mastering a profession should involve knowledge, tools, materials, skill, efficiency, quality, and long-term progression.

## 2.5 The world remembers

World changes should persist.

Examples:

- a house remains built,
- a road remains improved,
- a forest remains cleared until it regrows,
- a village remains damaged,
- an NPC stays dead,
- a new blacksmith can replace an old one,
- a bandit camp stays destroyed,
- a bridge stays rebuilt,
- farmland stays cultivated,
- a pond or trench stays dug,
- a player-owned workshop remains,
- settlement prosperity can rise or fall.

Returning to a familiar place after a long absence should sometimes produce:

> "What happened here?"

That feeling is desirable.

## 2.6 Systems should connect

Professions must not exist as isolated minigames.

Examples:

- Forestry produces timber.
- Carpentry turns timber into structures, furniture, carts, tools, bow components, and trade goods.
- Hunting produces meat, bones, hides, antlers, fat, trophies.
- Skinning quality affects hide quality.
- Tanning processes hides.
- Leatherworking turns leather into armor, bags, belts, harnesses, saddles, boots.
- Animal husbandry produces meat, milk, leather, wool, mounts, draft animals.
- Tailoring turns wool/flax/leather into clothing.
- Mining produces ores.
- Blacksmithing turns metal into tools, weapons, nails, hinges, hardware, horseshoes.
- Farmers need tools.
- Caravans transport goods.
- Bandits threaten caravans.
- Guards protect roads.
- Town prosperity affects construction and demand.

The game should feel like an ecosystem of systems, not a collection of menus.

---

# 3. VISUAL DIRECTION

## 3.1 General look

The game should use **grounded stylization**.

Primary reference mood: **Valheim-like serious stylization**.

The world may use simplified geometry, but should avoid looking childish, overly cartoony, toy-like, or "mobile game" styled.

Avoid:

- chibi proportions,
- giant heads,
- oversized comedy weapons,
- neon fantasy colors everywhere,
- exaggerated Fortnite-like forms,
- glossy plastic materials,
- excessive glowing equipment,
- visual clutter for its own sake.

Prefer:

- believable body proportions,
- rough timber,
- stone,
- leather,
- iron,
- wool,
- mud,
- dirt,
- smoke,
- fog,
- rain,
- snow,
- torchlight,
- warm windows at night,
- dark forests,
- weathered roads,
- simple but convincing materials.

## 3.2 Graphics philosophy

Use relatively simple geometry with strong:

- lighting,
- atmospheric effects,
- shadows,
- particles,
- material definition,
- vegetation,
- sound,
- animation,
- weather,
- environmental motion.

The goal is not AAA realism.

The goal is:

> visually attractive enough that players who reject pixel art still want to play it.

## 3.3 Camera

Preferred direction:

- elevated third-person or isometric-adjacent camera,
- zoomable,
- close enough for physical interactions to feel meaningful,
- far enough to support settlement building and combat awareness.

UNDECIDED:
- fully rotatable camera,
- fixed angle,
- limited rotation,
- shoulder-adjacent zoom option.

## 3.4 World atmosphere

Desired moments:

- morning mist above a river,
- distant chimney smoke,
- heavy rain on a workshop roof,
- dark forests that feel dangerous at night,
- caravan lanterns on a road,
- torchlight between village houses,
- snow accumulating around a cabin,
- an abandoned farmhouse visible through trees,
- a town slowly appearing through fog,
- returning home after days away.

The world should frequently feel beautiful, quiet, serious, and slightly melancholy.

---

# 4. PLAYER EXPERIENCE

## 4.1 Core daily loop

A typical day should be:

1. Wake up or begin play.
2. Decide what the player feels like doing.
3. Perform work, travel, explore, trade, build, hunt, craft, fight, socialize, or manage.
4. Encounter things happening in the world.
5. Decide whether to intervene.
6. Return home or continue traveling.
7. World state changes with or without the player.

The game should avoid forcing an optimized progression path.

## 4.2 Example sessions

### Session A: quiet life

- check crops,
- feed animals,
- repair tools,
- chop wood,
- finish building a barn,
- talk to a trader,
- fish before sunset.

### Session B: adventure

- hear that travelers disappeared near old ruins,
- prepare supplies,
- travel with friends,
- camp overnight,
- explore a procedural dungeon,
- return injured with rare materials.

### Session C: emergent problem

- town market has no iron tools,
- player learns caravans stopped arriving,
- follows road,
- discovers burned wagon,
- tracks bandits,
- decides to attack, negotiate, report, or ignore.

### Session D: multiplayer chaos

- one player expands the workshop,
- one player breeds horses,
- one player runs a caravan,
- one player disappears into the woods,
- a raid occurs,
- everyone runs home badly prepared.

---

# 5. CHARACTER SYSTEM

## 5.1 Identity

Characters should have:

- name,
- appearance,
- race,
- background,
- traits,
- skills,
- equipment,
- reputation,
- injuries,
- personal progression.

## 5.2 Backgrounds instead of restrictive classes

Prefer starting backgrounds over rigid classes.

Examples:

- Hunter
- Farmer
- Blacksmith's Apprentice
- Carpenter
- Mercenary
- Scholar
- Merchant
- Fisher
- Forester
- Miner
- Tailor
- Outcast
- Herbalist
- Sailor

Backgrounds provide:

- starting skill bonuses,
- starting knowledge,
- equipment,
- possible social reputation changes,
- maybe starting contacts.

They should **not** prevent later specialization.

A hunter can become a blacksmith.
A blacksmith can become a mage.
A farmer can become a dungeon crawler.

## 5.3 Races

Races may exist, but should not create ridiculous hard restrictions.

A race may influence:

- base traits,
- environmental tolerance,
- cultural knowledge,
- reputation,
- visual appearance,
- minor skill tendencies.

Avoid:

> "You are an elf, therefore you can never learn blacksmithing."

UNDECIDED:
- exact races,
- fantasy severity,
- racial lifespan mechanics,
- cultural starting regions.

## 5.4 Traits

Traits can be:

- selected at creation,
- earned,
- lost,
- discovered,
- caused by repeated behavior,
- caused by trauma/injury,
- inherited from background.

Examples:

- Strong
- Quick Learner
- Night Owl
- Fear of Deep Water
- Patient
- Reckless
- Skilled Hands
- Heavy Sleeper
- Iron Stomach
- Poor Swimmer
- Animal Affinity

Avoid traits becoming simple "+5%" spreadsheet filler unless justified.

---

# 6. SKILLS & PROGRESSION

## 6.1 Core philosophy

Players should feel satisfying long-term progression.

Numbers should go up.

This is a feature.

However, skill should influence interesting outcomes, not only damage multipliers.

## 6.2 Skills may affect

- speed,
- precision,
- waste,
- stamina cost,
- durability loss,
- quality,
- yield,
- failure chance,
- recovery speed,
- technique access,
- recipe knowledge,
- safe limits,
- advanced interactions.

## 6.3 Example: Carpentry

Low skill:

- slower work,
- more wasted wood,
- rougher construction,
- weaker furniture,
- fewer building options.

High skill:

- better material efficiency,
- faster work,
- stronger structures,
- improved finish,
- advanced joinery,
- decorative options,
- complex mechanisms,
- specialist construction.

## 6.4 Progression components

Potential system:

- **Skill Level** – practical experience.
- **Knowledge** – learned recipes/techniques.
- **Tool Quality** – equipment capability.
- **Material Quality** – input quality.
- **Workmanship** – final crafted outcome.
- **Traits/Background** – modifiers and tendencies.

UNDECIDED:
- exact level caps,
- XP curve,
- soft caps,
- skill decay,
- specialization trees,
- prestige/mastery systems.

---

# 7. PROFESSIONS

Professions should be deep enough that a player can reasonably specialize for many hours.

Initial profession list:

- Farming
- Forestry
- Carpentry
- Hunting
- Skinning
- Tanning
- Leatherworking
- Mining
- Smelting
- Blacksmithing
- Fishing
- Cooking
- Animal Husbandry
- Tailoring
- Weaving
- Herbalism
- Construction
- Masonry
- Trading
- Caravan Management
- Sailing / Boating
- Possibly Alchemy
- Possibly Brewing
- Possibly Enchanting / Magic Craft
- Possibly Engineering / Mechanisms

Not all must be 1.0.

---

# 8. FORESTRY

Forestry should involve more than clicking trees.

Possible mechanics:

- tree species,
- age/size,
- hardness,
- moisture,
- timber quality,
- felling direction,
- tool type,
- regrowth,
- forestry management,
- replanting,
- woodland depletion.

Tree outputs may include:

- logs,
- branches,
- bark,
- sap/resin,
- firewood,
- rare materials.

Forest state should persist and regenerate over time.

---

# 9. CARPENTRY

Carpentry should be one of the major deep professions.

Inputs:

- logs,
- planks,
- beams,
- pegs,
- nails,
- glue/resin,
- fittings.

Outputs:

- furniture,
- walls,
- roofs,
- doors,
- chests,
- barrels,
- tools,
- handles,
- carts,
- wagon parts,
- boat parts,
- bow components,
- scaffolding,
- decorative pieces.

Wood species should have properties such as:

- hardness,
- weight,
- flexibility,
- durability,
- rot resistance,
- appearance.

Tools and skill affect:

- waste,
- speed,
- finish,
- durability,
- structural quality.

---

# 10. HUNTING, SKINNING, TANNING, LEATHERWORKING

## 10.1 Hunting

Hunting should involve:

- tracking,
- signs,
- animal behavior,
- wind/noise where feasible,
- weapon choice,
- hit placement,
- chase,
- recovery.

Animals should not simply be loot boxes.

## 10.2 Skinning

Carcass condition matters.

Factors:

- weapon used,
- hit location,
- damage,
- decay,
- tool quality,
- skinning skill.

Outputs can vary:

- hide,
- meat,
- bones,
- fat,
- sinew,
- antlers/horns,
- organs,
- trophies.

## 10.3 Tanning

Raw hides should require processing.

Possible stages:

- cleaning,
- scraping,
- curing,
- tanning,
- drying,
- finishing.

Different methods may create different leather properties.

## 10.4 Leatherworking

Outputs:

- boots,
- gloves,
- armor,
- belts,
- bags,
- quivers,
- harnesses,
- saddles,
- straps,
- scabbards.

---

# 11. BLACKSMITHING & METALWORKING

Blacksmithing should be deep but not tedious for casual players.

Potential chain:

ore -> smelting -> ingot/bloom -> heating -> shaping -> quenching -> tempering -> finishing.

Inputs:

- ore,
- charcoal,
- flux,
- alloys,
- fuel,
- tools.

Outputs:

- weapons,
- tools,
- armor,
- nails,
- hinges,
- horseshoes,
- fittings,
- cookware,
- mechanisms.

Skill affects:

- material loss,
- quality,
- durability,
- weight balance,
- edge quality,
- repair quality.

UNDECIDED:
- how interactive forging should be,
- whether player timing affects output,
- exact metallurgical simulation depth.

---

# 12. FARMING

Farming should connect directly to settlement life.

Systems may include:

- soil fertility,
- moisture,
- seasons,
- crop suitability,
- irrigation,
- crop rotation,
- fertilizer/manure,
- pests,
- harvesting,
- storage.

Avoid excessive micromanagement by default.

Automation via NPC workers should become possible.

---

# 13. ANIMAL HUSBANDRY

Animals can provide:

- meat,
- milk,
- eggs,
- wool,
- hides,
- transport,
- draft power,
- mounts,
- breeding stock.

Potential mechanics:

- feeding,
- shelter,
- health,
- breeding,
- temperament,
- traits,
- productivity,
- age,
- inheritance.

UNDECIDED:
- genetics depth,
- disease system,
- training,
- exact species list.

---

# 14. FISHING

Fishing should be viable as a peaceful playstyle.

Systems may include:

- species by biome,
- weather,
- time of day,
- season,
- water type,
- bait,
- equipment,
- fishing skill,
- rarity.

Fishing contributes to:

- food supply,
- trade,
- crafting materials,
- local settlement economy.

---

# 15. TAILORING / TEXTILES

Possible inputs:

- wool,
- flax,
- leather,
- fur,
- thread,
- dyes.

Possible pipeline:

fiber -> processing -> thread -> cloth -> garment.

Outputs:

- clothing,
- bags,
- bedding,
- sails,
- banners,
- armor layers.

Skill affects:

- durability,
- insulation,
- fit,
- comfort,
- appearance,
- value.

---

# 16. MATERIAL SYSTEM

Materials should matter.

Each material may define:

- weight,
- hardness,
- durability,
- flexibility,
- insulation,
- rot resistance,
- beauty/value,
- rarity,
- crafting tags.

Examples:

Wood:
- oak,
- pine,
- birch,
- ash.

Metal:
- iron,
- bronze,
- steel,
- rare fantasy metals.

Textile:
- wool,
- linen,
- leather,
- fur.

Materials should allow recipes to be partially generic.

Example:

`wood_plank` may accept multiple wood species and produce different item properties.

This allows content expansion without duplicating every recipe manually.

---

# 17. ITEM SYSTEM

Items should be data-driven.

Potential item properties:

- unique ID,
- display name,
- tags,
- model/icon,
- weight,
- size,
- stack size,
- durability,
- quality,
- material,
- rarity,
- value,
- equip slot,
- damage,
- armor,
- tool capability,
- crafting categories,
- condition,
- spoilage,
- custom modifiers.

Never rely on code such as:

```csharp
if (item == "IronSword")
```

Prefer:

```text
tags: [weapon, sword, metal]
material: iron
damage_profile: longsword_standard
```

---

# 18. CRAFTING SYSTEM

Crafting must support:

- hand crafting,
- workstation crafting,
- queued production,
- NPC production,
- recipes,
- skill requirements,
- tool requirements,
- fuel,
- material substitutions,
- item quality.

Recipes should be data-driven.

Example:

```yaml
id: oak_chair
station: carpentry_bench
skill: carpentry
minimum_skill: 8
inputs:
  - tag: plank
    amount: 5
  - tag: wooden_peg
    amount: 4
output:
  item: chair
quality_formula: carpentry_standard
```

---

# 19. BUILDING & CONSTRUCTION

Players should be able to build:

- homes,
- workshops,
- farms,
- barns,
- storage,
- fortifications,
- docks,
- roads,
- fences,
- bridges,
- decorative structures.

UNDECIDED:
- fully modular piece-by-piece construction,
- prefab + modular hybrid,
- structural integrity depth,
- roof support logic.

Building must feel responsive and polished.

Placement preview should clearly show:

- valid/invalid,
- terrain contact,
- snapping,
- collision,
- ownership,
- resources.

---

# 20. TERRAFORMING

Terraforming is desired.

It should not look like Minecraft block digging.

Preferred operations:

- flatten,
- smooth,
- raise,
- lower,
- dig trench,
- prepare farmland,
- create terraces,
- create pond,
- cut road,
- create embankment,
- paint terrain material,
- remove foliage,
- place foundation pads.

Terraforming should use natural, smooth terrain modification.

Terraforming must:

- persist,
- replicate in multiplayer,
- update collision,
- update navigation,
- interact with structures,
- have sensible limits.

---

# 21. COMBAT

Combat must be engaging.

Avoid MMO-style stationary number trading.

Desired elements:

- responsive movement,
- momentum without sluggishness,
- light attacks,
- heavy attacks,
- block,
- parry,
- dodge or sidestep,
- stamina,
- weapon reach,
- hit reactions,
- knockback,
- hit-stop,
- strong sound feedback,
- readable enemy attacks.

Weapons should feel meaningfully different.

### Spear
- reach,
- space control,
- thrusts.

### Axe
- heavy impact,
- slower commitment,
- strong chopping.

### Sword
- versatile,
- fast recovery,
- balanced.

### Dagger
- fast,
- close range,
- low commitment.

### Bow
- real aiming,
- projectile travel,
- skill influences steadiness/handling.

### Shield
- changes rhythm,
- defensive posture,
- stamina tradeoffs.

Character skill should improve things like:

- stamina efficiency,
- recovery,
- precision,
- handling,
- available techniques,
- block effectiveness.

Player mechanical skill should still matter.

The ideal is:

> player improves + character improves.

---

# 22. DAMAGE, INJURY, DEATH

UNDECIDED and very important.

Possible systems:

- health pool + wound states,
- limb injuries,
- bleeding,
- fractures,
- infection,
- exhaustion,
- armor coverage.

Death models under consideration:

- respawn with penalty,
- corpse retrieval,
- skill loss,
- optional permadeath,
- server-configurable hardcore mode.

This decision strongly affects tone and must be resolved before production combat is finalized.

---

# 23. WORLD GENERATION

The world should be seed-based and procedurally generated.

High-level pipeline:

1. world seed,
2. large-scale terrain,
3. elevation,
4. water systems,
5. biomes,
6. resource distribution,
7. major roads,
8. settlement candidates,
9. settlement generation,
10. POIs,
11. wilderness features,
12. vegetation,
13. wildlife,
14. dungeon entrances,
15. final validation.

The world should feel authored despite procedural generation.

---

# 24. WORLD STREAMING

The overworld should be largely seamless.

Walking between:

- forest,
- road,
- farm,
- village,
- town,
- wilderness

should normally require no loading screen.

The world may be divided internally into streamed regions/chunks.

Only nearby areas receive full simulation/rendering.

Distant regions use simplified simulation.

---

# 25. LOADING SCREENS

Loading screens are acceptable where they help architecture and performance.

Good candidates:

- deep dungeons,
- large cave networks,
- major instanced ruins,
- exceptional interior complexes,
- possibly special realms.

Avoid loading screens for:

- normal houses,
- shops,
- taverns,
- barns,
- workshops.

Opening a farmhouse door should not cause a loading screen.

---

# 26. PROCEDURAL SETTLEMENTS

Procedural settlements are a core feature.

They should not be random collections of buildings.

Each settlement begins with a simulation identity.

Example:

```text
Type: River Village
Population Target: 34
Primary Economy: Fishing + Forestry
Wealth: Poor
Threat: Moderate
Culture: Regional Set A
```

From this identity, the system generates:

- roads,
- lots,
- public areas,
- economic buildings,
- housing,
- farms,
- docks,
- defenses,
- decorations,
- inhabitants.

---

# 27. SETTLEMENT SITE SELECTION

Never simply select coordinates and drop a village.

Candidate sites should evaluate:

- slope,
- elevation variation,
- river/lake access,
- road access,
- biome,
- flood risk,
- usable land,
- strategic position,
- nearby resources,
- distance to other settlements.

If a site is bad, reject it.

Different settlement types have different needs.

Examples:

Fishing village:
- water access,
- dockable shoreline.

Mining village:
- ore resources,
- rough terrain acceptable.

Farming town:
- large flat-ish fertile area.

Fortified town:
- defensible position.

---

# 28. TERRAIN PREPARATION FOR SETTLEMENTS

Settlement generation must use a proper terrain-preparation pass.

Pipeline:

1. choose valid site,
2. reserve settlement footprint,
3. design roads and lots,
4. grade terrain,
5. place foundations,
6. place buildings,
7. blend terrain,
8. place vegetation/clutter,
9. build navigation,
10. validate.

Do not flatten an entire settlement into one giant plateau.

Local grading should preserve natural terrain.

For sloped areas:

- terraces,
- retaining walls,
- stairs,
- raised foundations,
- road switchbacks.

The result should appear intentional.

---

# 29. BUILDING PREFAB METADATA

Each building prefab should include metadata.

Possible fields:

- footprint,
- entrance points,
- road connection points,
- foundation area,
- allowed slope,
- minimum clearance,
- optional attachments,
- garden area,
- nav entrances,
- NPC work positions,
- storage markers,
- interior zones,
- settlement function,
- population capacity,
- wealth variants.

A prefab is not simply a model.

It is a structured gameplay object.

---

# 30. PREFAB-BASED PROCEDURAL CONTENT

Use authored assets placed algorithmically.

Examples:

Village kit:
- cottages,
- smithies,
- inns,
- wells,
- barns,
- workshops,
- markets,
- temples,
- walls,
- watchtowers.

Road kit:
- bridges,
- crossroads,
- camps,
- milestones,
- wrecked wagons,
- shrines.

Wilderness kit:
- ruins,
- caves,
- hunter camps,
- graveyards,
- lumber camps,
- monster nests.

Dungeon kit:
- rooms,
- corridors,
- stairs,
- traps,
- encounter spaces,
- treasure rooms,
- boss rooms.

Human-authored pieces provide quality.
Procedural systems provide variation.

---

# 31. TERRAIN STAMPS

Important locations may use authored terrain stamps.

Examples:

- capital city,
- monastery,
- castle,
- large ruin,
- harbor,
- mine entrance.

A terrain stamp can:

- modify height,
- paint textures,
- reserve roads,
- blend into surrounding generated terrain.

Cheating is allowed if the result looks good.

---

# 32. PROCEDURAL VALIDATION

Generated content must be automatically validated.

Examples:

- door below terrain -> fail,
- floating building -> fail,
- building intersects building -> fail,
- tree through building -> fail,
- impossible road slope -> fail,
- blocked entrance -> fail,
- NPC cannot reach workplace -> fail,
- foundation exposed too far -> warning,
- bridge misses river -> fail,
- settlement has disconnected road graph -> fail.

Development tools should support generating hundreds of settlements/worlds and identifying bad seeds.

Failed seeds should be saved for debugging.

---

# 33. SETTLEMENT SIMULATION

Settlements should have meaningful internal simulation.

Potential variables:

- population,
- food supply,
- wealth,
- safety,
- housing,
- production,
- trade,
- morale,
- health,
- infrastructure.

NPCs should have:

- homes,
- jobs,
- schedules,
- families/relationships if implemented,
- needs,
- social ties,
- local reputation.

UNDECIDED:
- depth of individual NPC psychology,
- reproduction/families,
- aging,
- birth system.

---

# 34. ECONOMY

Settlements should produce and consume goods.

Example chains:

Farm -> grain -> mill -> flour -> bakery -> bread.

Forest -> logs -> sawmill/carpenter -> construction.

Mine -> ore -> smith -> tools.

Sheep -> wool -> weaver -> cloth -> tailor -> clothing.

Trade should respond to:

- local supply,
- local demand,
- disruptions,
- distance,
- danger,
- settlement specialization.

Avoid a fully realistic economy if it harms fun or performance.

Use simulation abstractions where appropriate.

---

# 35. CARAVANS

Caravans are important world entities.

They:

- move goods,
- create trade links,
- carry information,
- create emergent events,
- can be attacked,
- can disappear,
- can hire guards,
- can be operated by players.

Potential player roles:

- guard,
- merchant,
- caravan leader,
- supplier,
- raider if allowed by server rules.

---

# 36. BANDITS & HOSTILE GROUPS

Bandits should exist as world actors, not simply spawn waves.

Bandit groups may:

- form,
- grow,
- move,
- establish camps,
- rob travelers,
- raid farms,
- attack villages,
- demand tribute,
- recruit,
- fight guards,
- fight other groups,
- collapse.

The player may:

- ignore them,
- fight them,
- join them if design permits,
- report them,
- negotiate,
- exploit disruption.

---

# 37. LIVING WORLD EVENTS

Potential events:

- bandit attacks,
- caravan disappearance,
- monster nest,
- livestock disease,
- fire,
- crop failure,
- flood,
- drought,
- food shortage,
- mine collapse,
- missing person,
- road blockage,
- bridge destruction,
- wolf attacks,
- local feud,
- trader arrival,
- festival,
- migration,
- new settlement,
- settlement abandonment.

Events should often emerge from world state rather than pure random dice.

---

# 38. NPC INFORMATION & QUEST DISCOVERY

Players should learn about the world organically.

Information sources:

- tavern rumors,
- NPC dialogue,
- road travelers,
- market prices,
- empty shelves,
- damaged wagons,
- smoke,
- corpses,
- town boards,
- letters,
- local leaders,
- guards,
- merchants.

Formal quests may exist, but should feel like part of the world.

---

# 39. NPC SIMULATION LOD

Full simulation for every NPC in the entire world is unnecessary.

Use simulation levels.

### Active NPCs
Near players:
- full AI,
- navigation,
- animation,
- combat,
- schedules.

### Regional NPCs
Nearby but unloaded:
- simplified job/schedule simulation.

### Distant NPCs
Far away:
- statistical/event-based simulation.

The system should preserve meaningful outcomes without simulating footstep-by-footstep behavior globally.

---

# 40. WILDLIFE & ECOLOGY

World should include:

- predators,
- prey,
- domestic animals,
- rare creatures,
- fantasy creatures depending on final lore.

Possible systems:

- population pressure,
- hunting depletion,
- migration,
- breeding,
- habitat,
- seasons.

Avoid over-simulation if it threatens performance.

---

# 41. FANTASY LEVEL

UNDECIDED.

Need explicit decision on:

- prevalence of magic,
- commonality of monsters,
- undead,
- goblins/orcs,
- dragons,
- gods,
- magical items,
- spellcasting,
- magical professions,
- magical transport.

Current tone preference suggests:

> grounded fantasy with serious atmosphere rather than constant high-fantasy spectacle.

---

# 42. EXPLORATION

Exploration should reward curiosity.

World features:

- ruins,
- caves,
- abandoned farms,
- shrines,
- watchtowers,
- strange geological features,
- camps,
- hidden valleys,
- ancient roads,
- dungeons,
- rare biomes.

Exploration rewards can include:

- materials,
- lore,
- recipes,
- techniques,
- NPC contacts,
- rare resources,
- treasure,
- world knowledge.

---

# 43. DUNGEONS

Dungeons can be procedural, authored, or hybrid.

Dungeon structure can use modular rooms.

Possible generation:

```text
entrance
-> branching rooms
-> encounters
-> puzzle/trap sections
-> treasure
-> miniboss
-> deeper level
-> boss
```

Dungeons may use loading screens.

They do not need to physically match overworld underground geometry exactly.

---

# 44. BUILDINGS & INTERIORS

Normal buildings should generally exist directly in the overworld.

Interiors should support:

- NPC schedules,
- workstations,
- storage,
- furniture,
- lighting,
- ownership,
- player interaction.

Avoid loading transitions for ordinary buildings.

---

# 45. ROADS

Roads should:

- connect settlements,
- influence travel,
- influence caravans,
- influence settlement placement,
- improve movement,
- support world events.

Road quality may vary:

- trail,
- dirt road,
- maintained road,
- stone road.

Players or settlements may improve roads later.

---

# 46. TRAVEL

Travel options may include:

- walking,
- running,
- horses,
- carts,
- wagons,
- caravans,
- boats.

UNDECIDED:
- fast travel,
- teleportation,
- mounts beyond horses,
- magical transport.

Prefer physical travel as meaningful gameplay.

---

# 47. BOATS

Boats are desired eventually.

Possible uses:

- fishing,
- river travel,
- trade,
- exploration,
- cargo transport.

UNDECIDED:
- sailing physics depth,
- sea/ocean scope,
- shipbuilding,
- naval combat.

Probably not required for early 1.0 unless scope permits.

---

# 48. MULTIPLAYER

The game should support dedicated multiplayer servers.

Target player count is UNDECIDED.

Likely design range:

- small group friendly,
- perhaps 4–16 players initially,
- architecture should avoid unnecessarily preventing larger counts.

Server authoritative by default.

Server owns:

- world time,
- NPC simulation,
- economy,
- animals,
- crops,
- weather,
- events,
- bandits,
- loot,
- building,
- terraforming,
- combat validation.

Clients primarily:

- render,
- interpolate,
- send input/actions,
- show UI.

---

# 49. SERVER PERSISTENCE

Dedicated server world continues between sessions.

Persistent:

- settlements,
- NPC state,
- buildings,
- terrain changes,
- containers,
- crops,
- animals,
- economy,
- bandits,
- roads,
- events,
- player characters,
- inventories.

UNDECIDED:
- whether world simulation continues when no players are online,
- time scaling while server is empty.

---

# 50. PLAYER OWNERSHIP & PERMISSIONS

Need multiplayer-friendly systems for:

- building ownership,
- storage permissions,
- settlement permissions,
- group/clan ownership,
- public/private containers,
- doors,
- land rights,
- anti-griefing.

Server settings should allow flexible control.

---

# 51. PVP

UNDECIDED.

Possible options:

- PvE default,
- optional PvP servers,
- consensual PvP,
- faction warfare,
- full PvP mode.

Do not let PvP architecture corrupt PvE design unless intentional.

---

# 52. MODDING PHILOSOPHY

Modding is a first-class requirement.

Goal:

> Most new content should be addable without changing engine/gameplay code.

Three mod tiers:

## Tier 1: Data Mods

Add:

- items,
- recipes,
- skills,
- traits,
- professions,
- loot tables,
- crops,
- animals,
- events,
- NPC definitions,
- economy definitions.

No compiled code required.

## Tier 2: Asset Mods

Add:

- models,
- textures,
- sounds,
- animations,
- buildings,
- village prefabs,
- dungeon rooms,
- environment assets.

## Tier 3: Code Mods

Add:

- new mechanics,
- custom logic,
- entirely new systems.

Code mods require stronger compatibility/version rules.

---

# 53. MOD FILE STRUCTURE

Example:

```text
/Mods/BetterBlacksmithing/
    mod.json
    items/
    recipes/
    professions/
    skills/
    assets/
    prefabs/
```

Each mod should define:

- ID,
- name,
- version,
- author,
- dependencies,
- load order hints,
- game version compatibility.

---

# 54. CONTENT DEFINITIONS

All content needs stable schemas.

Schemas should exist for:

- items,
- recipes,
- materials,
- skills,
- traits,
- professions,
- races,
- crops,
- animals,
- creatures,
- NPC archetypes,
- buildings,
- settlement templates,
- biomes,
- loot tables,
- world events,
- dungeons,
- room prefabs.

Validation tooling should detect broken definitions before launch.

---

# 55. MOD COMPATIBILITY

Needed eventually:

- dependency system,
- mod versioning,
- server-required mods,
- automatic compatibility checks,
- missing mod warnings,
- load ordering,
- conflict reporting.

UNDECIDED:
- Steam Workshop,
- mod.io,
- custom repository.

---

# 56. TECHNICAL FOUNDATION

Current preferred engine:

> **Godot 4.x .NET / C#**

Reasoning:

- open source,
- free,
- C# support,
- suitable 3D renderer,
- active development,
- dedicated server support,
- runtime resource pack support,
- good content pipeline,
- relatively AI-agent-friendly project structure,
- large ecosystem.

Preferred terrain foundation:

> Terrain3D or equivalent proven Godot terrain system.

This must be validated with prototypes before permanent commitment.

---

# 57. PROGRAMMING LANGUAGE

Primary game logic:

> C#

Use GDScript only where it provides clear benefits or for lightweight tooling.

Native C++ extensions may be introduced for performance-critical tasks if profiling proves necessary.

Do not prematurely move systems to native code.

---

# 58. PROJECT ARCHITECTURE

Suggested high-level structure:

```text
/Game
    /Core
    /World
    /Simulation
    /Characters
    /Combat
    /Skills
    /Professions
    /Crafting
    /Settlements
    /Economy
    /Events
    /Networking
    /Persistence
    /Modding
    /UI

/Content
    /Items
    /Recipes
    /Materials
    /Buildings
    /Creatures
    /Animals
    /Professions
    /Skills
    /Traits
    /Races
    /Biomes
    /WorldEvents
    /SettlementTypes
    /Dungeons

/Prefabs
    /Village
    /Town
    /Wilderness
    /Dungeon
    /Road
    /Farm
    /Bridge

/Assets
    /Models
    /Materials
    /Textures
    /Animations
    /Audio
    /VFX

/Mods
```

Architecture may evolve, but responsibilities should remain clearly separated.

---

# 59. WORLD STATE ARCHITECTURE

World state should not depend on currently loaded scenes.

Data model and visual representation should be separable.

Example:

`SettlementState` exists even when the settlement scene is not loaded.

When a region streams in:

- persistent state is loaded,
- visual scene is created,
- NPC actors spawn,
- world objects appear.

When it streams out:

- active actors serialize important state,
- visual scene can be discarded,
- simplified simulation continues.

---

# 60. DETERMINISM

Procedural generation should be deterministic where practical.

Given:

- same world seed,
- same version,
- same mods,

the base world generation should reproduce the same:

- terrain,
- settlements,
- roads,
- major POIs.

Runtime changes are then stored separately.

This helps:

- debugging,
- server sync,
- save size,
- reproducibility,
- automated testing.

---

# 61. SAVE SYSTEM

Saves should be versioned.

Store:

- world seed,
- world generator version,
- active mods and versions,
- changed terrain,
- player structures,
- settlement state,
- NPC state,
- economy,
- containers,
- crops,
- animals,
- events,
- player data.

Avoid serializing every generated static object if it can be reconstructed from seed.

Store deltas where reasonable.

---

# 62. TERRAIN DELTA STORAGE

Player and simulation terrain modifications should be stored as changes from generated terrain.

Potential categories:

- height deltas,
- texture/material changes,
- foliage removals,
- placed roads,
- foundations,
- water edits if supported.

Need efficient chunk/region serialization.

---

# 63. NAVIGATION

Navigation must support:

- generated terrain,
- settlements,
- player buildings,
- terraforming,
- doors,
- bridges,
- roads,
- dynamic obstacles.

Prefer regional navmesh updates rather than rebaking the whole world.

Use simplified navigation geometry where possible.

---

# 64. CONTENT PIPELINE

Preferred model pipeline:

Blender -> glTF -> Godot import -> prefab metadata.

Assets should support:

- consistent scale,
- pivots,
- collision,
- LOD,
- materials,
- sockets/markers,
- animation rig conventions.

Need validation scripts for bad content.

---

# 65. PROCEDURAL ASSET ASSEMBLY

Where possible, visual variety can be created through combinations.

Examples:

House:

- base shell,
- roof variant,
- chimney,
- door,
- shutters,
- clutter,
- material palette,
- garden,
- fence.

NPC:

- body,
- hair,
- beard,
- clothing,
- equipment,
- color variation.

This expands content without requiring thousands of bespoke assets.

---

# 66. GAME FEEL

Polish is not optional.

Every major interaction needs:

- animation,
- sound,
- feedback,
- timing,
- camera response if appropriate,
- particles,
- readable state changes.

Example: chopping a tree

Bad:

> click -> progress bar -> wood appears.

Good:

- character winds up,
- axe hits,
- tree reacts,
- wood chips fly,
- proper impact sound,
- tree progressively shows damage,
- final fall has weight,
- logs remain physical world objects or become clear inventory resources depending on design.

---

# 67. MOVEMENT

Movement should feel responsive.

Need to prototype:

- acceleration,
- stopping,
- turning,
- sprinting,
- slopes,
- jumping if included,
- dodging,
- camera follow.

Avoid floaty controls.

---

# 68. INTERACTION SYSTEM

Prefer one consistent context interaction framework.

Examples:

- gather,
- open,
- talk,
- use,
- pick up,
- repair,
- build,
- skin,
- harvest.

UI should clearly communicate available actions without constant screen clutter.

---

# 69. USER INTERFACE

UI should support deep systems without becoming spreadsheet hell.

Needed:

- inventory,
- equipment,
- crafting,
- skills,
- character sheet,
- building,
- maps,
- settlement info,
- profession progress,
- tooltips.

Advanced information can be expandable.

Casual players should not need to understand every hidden number.

---

# 70. INVENTORY

UNDECIDED.

Possible approaches:

- weight-based slots,
- grid inventory,
- category + weight,
- hybrid.

Inventory must work cleanly with multiplayer and containers.

---

# 71. EQUIPMENT

Potential slots:

- head,
- torso,
- legs,
- feet,
- hands,
- cloak,
- belt,
- backpack,
- weapon,
- offhand,
- accessory.

UNDECIDED:
- exact slot count,
- layered clothing system.

---

# 72. QUALITY SYSTEM

Crafted items may have quality.

Quality can depend on:

- material,
- skill,
- tool,
- workstation,
- recipe,
- condition.

Quality should matter but not produce absurd power gaps.

Possible qualities:

- crude,
- common,
- fine,
- excellent,
- masterwork.

Exact naming undecided.

---

# 73. DURABILITY & REPAIR

Tools, weapons, armor, and structures may wear.

Repair should matter without becoming annoying.

Potential rules:

- repair skill matters,
- materials required,
- poor repairs reduce max condition,
- specialist repairs restore more.

UNDECIDED:
- permanent item degradation.

---

# 74. FOOD & SURVIVAL

Survival should support immersion, not constant punishment.

Possible needs:

- hunger,
- thirst,
- fatigue,
- temperature.

Do not make the player die because they failed to eat every few real-world minutes.

Food should matter because:

> the character lives here.

Not because:

> a meter is screaming constantly.

---

# 75. WEATHER

Weather contributes to:

- atmosphere,
- visibility,
- temperature,
- farming,
- fishing,
- travel,
- combat,
- fires.

Possible types:

- clear,
- rain,
- storm,
- fog,
- snow,
- heat.

Need regional/seasonal behavior.

---

# 76. SEASONS

Potential seasonal effects:

- crop availability,
- animal behavior,
- weather,
- travel difficulty,
- daylight,
- food supply,
- trade.

UNDECIDED:
- season length,
- whether winter can significantly threaten settlements.

---

# 77. SOUND

Sound is a major atmosphere tool.

Needed:

- environmental loops,
- forests,
- rivers,
- storms,
- settlement ambience,
- workshops,
- animals,
- combat,
- footsteps,
- indoor/outdoor transitions.

Crafting should sound satisfying.

Blacksmithing should have strong metal impact audio.
Carpentry should have convincing wood sounds.
Rain on roofs should be noticeable.

---

# 78. MUSIC

Music should support atmosphere rather than constantly dictate emotion.

Possible philosophy:

- sparse exploration music,
- settlement themes,
- combat escalation,
- dungeon ambience,
- night themes.

UNDECIDED:
- dynamic music system depth.

---

# 79. PERFORMANCE TARGETS

Targets are UNDECIDED, but design should assume mid-range gaming PCs, not only high-end hardware.

Need budgets for:

- NPC count,
- active AI,
- draw calls,
- foliage,
- shadows,
- world chunk size,
- network updates,
- physics,
- navigation.

Profile before optimizing.

---

# 80. ACTIVE SIMULATION BUDGET

Need explicit configurable limits.

Examples:

- active NPC radius,
- active animal radius,
- active combat groups,
- detailed crop simulation radius,
- loaded terrain radius,
- LOD transitions.

Distant simulation should use cheap state updates.

---

# 81. ACCESSIBILITY

Eventually support:

- key rebinding,
- controller,
- UI scaling,
- subtitle options,
- color accessibility,
- camera shake toggle,
- motion reduction,
- hold/toggle settings,
- difficulty/server customization.

---

# 82. ADMIN & SERVER TOOLS

Multiplayer server needs proper administration.

Potential tools:

- teleport,
- inspect NPC,
- inspect settlement,
- spawn item,
- spawn creature,
- modify time,
- world event controls,
- player permissions,
- ban/kick,
- terrain debug,
- region regeneration,
- nav debug,
- economy debug,
- save backup.

Admin systems should exist early enough to support development and testing.

---

# 83. DEBUG TOOLS

Required development tools:

- world seed viewer,
- biome viewer,
- settlement generator,
- road graph viewer,
- navmesh viewer,
- NPC state viewer,
- economy viewer,
- event viewer,
- item browser,
- recipe browser,
- mod inspector,
- network inspector.

Good debug tools are part of the game development plan, not optional luxuries.

---

# 84. AUTOMATED WORLD TESTING

Development mode should support batch generation.

Example:

Generate 500 worlds or villages and test:

- invalid building placement,
- disconnected roads,
- impossible terrain,
- blocked entrances,
- unreachable workstations,
- nav failures,
- water problems,
- floating objects,
- missing required buildings.

Save:

- seed,
- screenshot,
- validation report.

---

# 85. POLISH STANDARD

The generator may not produce obviously broken scenes.

Reject generation rather than accepting ugly results.

Quality rule:

> "Procedural" is not an excuse for bad placement.

The player should frequently be unable to tell whether a location was handcrafted.

---

# 86. 1.0 SCOPE PHILOSOPHY

The full dream is enormous.

The first commercial/finished version must be smaller.

The goal is not:

> implement every fantasy-life system imaginable.

The goal is:

> implement the smallest complete version that already feels like this world.

---

# 87. PROPOSED 1.0 CORE

Tentative core for first finished version:

## World
- procedural overworld,
- several biomes,
- roads,
- 3–5 settlement archetypes,
- wilderness POIs,
- caves/dungeons.

## Player
- character creation,
- traits/background,
- inventory,
- equipment,
- core skills,
- survival-lite needs.

## Professions
Start with a strong subset:
- forestry,
- carpentry,
- farming,
- hunting,
- skinning,
- leatherworking,
- mining,
- blacksmithing,
- fishing,
- cooking.

## Settlements
- NPC homes/jobs,
- basic production,
- local economy,
- shortages,
- simple schedules.

## Living world
- caravans,
- bandits,
- animal threats,
- shortages,
- several world event types.

## Combat
- melee,
- ranged,
- enemies,
- blocking/dodging,
- satisfying feedback.

## Building
- player housing/workshops,
- storage,
- fences,
- basic defenses.

## Multiplayer
- dedicated server,
- persistence,
- permissions.

## Modding
- data mods,
- asset content packs.

Anything beyond this should compete for scope.

---

# 88. NOT YET LIST

Features that may be cool but must not automatically enter 1.0:

- politics,
- kingdoms,
- marriage,
- children,
- dynasties,
- castle sieges,
- advanced naval combat,
- huge ships,
- brewing,
- beekeeping,
- falconry,
- necromancy,
- complex law systems,
- taxation,
- large-scale warfare,
- procedural languages,
- full genetics,
- advanced religion simulation,
- magical realms,
- dragons,
- player kingdoms,
- fully simulated city construction,
- massive raids,
- deep diplomacy,
- guild politics,
- infinite profession list.

When someone says:

> "Wouldn't it be cool if..."

add it here first.

---

# 89. FIRST PROTOTYPE

The first prototype should not attempt the whole game.

Prototype goal:

> Prove the physical feel of the world.

Suggested prototype:

- one small terrain region,
- one character,
- third-person/elevated camera,
- movement,
- one tree,
- axe,
- chopping,
- one resource,
- one building piece,
- terrain modification,
- one enemy,
- basic combat,
- one small prefab house,
- saving/loading.

If movement, combat, gathering, camera, and building do not feel good, stop and fix them.

---

# 90. SECOND PROTOTYPE

Goal:

> Prove procedural settlement placement.

Include:

- seeded terrain,
- road,
- small village,
- 5–10 building prefabs,
- terrain grading,
- prefab metadata,
- NPC navigation,
- validation.

Generate many villages automatically.

---

# 91. THIRD PROTOTYPE

Goal:

> Prove living-world simulation.

Include:

- one village,
- 10–20 NPCs,
- homes,
- jobs,
- production,
- food consumption,
- simple market,
- caravan,
- bandit disruption,
- player can observe consequences.

This is the heart of the game.

---

# 92. FOURTH PROTOTYPE

Goal:

> Prove multiplayer persistence.

Include:

- dedicated server,
- 2–4 players,
- building,
- inventory,
- NPCs,
- terrain changes,
- save/restart,
- reconnect.

---

# 93. VERTICAL SLICE

A vertical slice should feel like a miniature version of the finished game.

Possible content:

- one biome region,
- one village,
- one town,
- one dungeon,
- roads,
- farms,
- forest,
- river,
- 3–5 professions,
- bandit camp,
- caravan,
- local economy,
- building,
- multiplayer.

The slice must be polished.

---

# 94. DEVELOPMENT PRINCIPLE: BUILD SYSTEMS, THEN CONTENT

Do not manually create hundreds of unique items before underlying systems are stable.

Order:

1. define schema,
2. implement system,
3. create validation,
4. create small content set,
5. test,
6. expand content.

This keeps Codex and human developers from generating mountains of incompatible content.

---

# 95. DEVELOPMENT PRINCIPLE: CONTENT MUST BE CHEAP TO ADD

A new sword should ideally require:

- data definition,
- model/icon,
- optional recipe.

A new crop:

- crop definition,
- visuals,
- growth stages.

A new house:

- scene/prefab,
- metadata.

A new event:

- event definition,
- condition/effect definitions,
- optional custom script.

If every new content item requires custom engine code, the architecture has failed.

---

# 96. DEVELOPMENT PRINCIPLE: GENERATION MUST BE DEBUGGABLE

Every generated object should be traceable to:

- seed,
- generator stage,
- prefab selection,
- rule selection.

Debug UI should answer:

> "Why did this village spawn here?"

> "Why did this road take this path?"

> "Why did this building choose this foundation?"

> "Why did these bandits appear?"

This is vital for procedural systems.

---

# 97. DEVELOPMENT PRINCIPLE: POLISH IS A SYSTEM

Polish should be designed, not applied at the end.

Each interactable should define:

- animation,
- sound,
- VFX,
- timing,
- UI feedback,
- world reaction.

A feature is not complete when it works logically.
It is complete when it feels good.

---

# 98. DEVELOPMENT PRINCIPLE: SIMULATE WHAT MATTERS

Do not simulate detail merely because it is possible.

Ask:

> Will the player see, feel, or care about this?

Simulate full detail where it creates stories or gameplay.

Abstract everything else.

---

# 99. DEVELOPMENT PRINCIPLE: NO MAGIC QUEST GENERATOR

Do not bolt on a generic quest generator that produces meaningless tasks.

World events should generate needs and consequences.

NPCs can then communicate those needs.

The player's objective emerges from the world.

---

# 100. DEVELOPMENT PRINCIPLE: THE VILLAGE TEST

A critical quality test:

> Can a player spend one enjoyable hour living around a small village without entering a dungeon or killing anything?

If yes, the world has a foundation.

If no, combat content is probably masking a shallow world.

---

# 101. DEVELOPMENT PRINCIPLE: THE HOMECOMING TEST

Another critical test:

> After spending several in-game days exploring, does returning home feel meaningful?

The player's home/settlement should provide:

- familiarity,
- people,
- storage,
- work,
- safety,
- visible changes,
- consequences,
- emotional attachment.

---

# 102. DEVELOPMENT PRINCIPLE: THE THREE-PLAYER TEST

The game should support this simultaneously:

Player 1:
- dungeon crawling.

Player 2:
- farming/building.

Player 3:
- crafting/trading.

All three must contribute to the same world.

---

# 103. DEVELOPMENT PRINCIPLE: NO "MINECRAFTY" VISUAL IDENTITY

Although terrain or simulation may internally use grids, chunks, voxels, or cells, the visual presentation should not look block-based.

Terrain should appear:

- smooth,
- natural,
- organic.

Buildings can use snapping internally without appearing like cubes.

---

# 104. DEVELOPMENT PRINCIPLE: SERIOUS BUT NOT GRIMDARK

Tone should be grounded and believable.

The game may be dangerous and melancholy but should still allow:

- cozy homes,
- beautiful landscapes,
- humor,
- friendship,
- peaceful work,
- satisfying routines.

Avoid nonstop misery.

---

# 105. DEVELOPMENT PRINCIPLE: PLAYER FREEDOM WITHOUT CHAOS

Freedom should come from coherent systems.

The game should not become a sandbox where everything is technically possible but nothing matters.

Actions should connect to:

- economy,
- settlements,
- environment,
- progression,
- relationships,
- world state.

---

# 106. QUESTIONS THAT MUST BE ANSWERED BEFORE FULL PRODUCTION

## World
- How large is a standard world?
- Continuous continent or islands?
- How many settlements?
- How many biomes?
- How long should crossing the world take?

## Fantasy
- What races exist?
- How common is magic?
- What monsters exist?
- Are gods active?
- How common are supernatural items?

## Character
- What happens on death?
- Are classes only backgrounds?
- How many skills?
- Are traits permanent?

## Multiplayer
- Target player count?
- PvP?
- Offline progression?
- Server-empty simulation?

## Building
- modular pieces or prefabs?
- structural integrity?
- ownership rules?

## Economy
- how simulated are prices?
- do NPCs carry real inventories?
- how detailed are production chains?

## NPCs
- relationships?
- families?
- aging?
- reproduction?
- permanent death?

## Combat
- lock-on or free aim?
- dodge roll or grounded sidestep?
- directional attacks?
- friendly fire?

## Survival
- hunger?
- thirst?
- temperature?
- sleep?
- disease?

## Travel
- horses?
- carts?
- boats in 1.0?
- fast travel?

---

# 107. CURRENT RECOMMENDED TECH STACK

**Engine:** Godot 4.x .NET  
**Language:** C#  
**Terrain:** Terrain3D or equivalent after validation  
**3D asset pipeline:** Blender -> glTF  
**Content:** JSON/YAML/resources depending final tooling  
**Networking:** Godot multiplayer foundation with server-authoritative architecture  
**Server:** headless dedicated server  
**World generation:** deterministic seed-based pipeline  
**Persistence:** versioned region-based save format  
**Modding:** data -> assets -> code tiers  
**Source control:** Git  
**Automation:** validation scripts + batch world generation + tests  

---

# 108. SUGGESTED REPOSITORY DOCUMENTS

This design bible should eventually be split into separate documents.

Recommended:

```text
/docs/
    GAME_DESIGN_BIBLE.md
    TECHNICAL_ARCHITECTURE.md
    WORLD_GENERATION.md
    SETTLEMENT_SIMULATION.md
    COMBAT_DESIGN.md
    PROFESSIONS.md
    MODDING.md
    NETWORKING.md
    SAVE_FORMAT.md
    ART_DIRECTION.md
    CONTENT_PIPELINE.md
    SCOPE_1_0.md
    NOT_YET.md
```

This file remains the high-level master document.

---

# 109. CODING AGENT WORKFLOW

When Codex is asked to implement a feature:

1. Read this document.
2. Read the relevant subsystem document if it exists.
3. Inspect existing architecture.
4. Identify reusable systems.
5. Propose minimal changes.
6. Implement.
7. Add tests/validation.
8. Verify save/load.
9. Verify multiplayer implications.
10. Update documentation.

Do not solve every request by adding another singleton or static global manager.

---

# 110. FEATURE DEFINITION OF DONE

A gameplay feature is not complete until:

- functional,
- documented,
- data-driven where appropriate,
- save/load safe,
- multiplayer safe if applicable,
- has debug visibility,
- has basic polish,
- has validation,
- does not obviously break existing mods,
- tested in at least one realistic gameplay scenario.

---

# 111. CONTENT DEFINITION OF DONE

New content is not complete until:

- valid definition,
- visual asset,
- collision,
- correct scale,
- metadata,
- gameplay tags,
- recipe/drop integration if needed,
- icon/UI representation,
- multiplayer consistency,
- mod compatibility consideration.

---

# 112. SETTLEMENT DEFINITION OF DONE

A generated settlement is acceptable when:

- terrain fits buildings,
- no major intersections,
- roads make sense,
- NPCs can navigate,
- doors are reachable,
- buildings fit settlement economy,
- terrain blending looks intentional,
- vegetation does not clip badly,
- settlement has readable structure,
- it looks plausible from multiple camera angles.

---

# 113. PROCEDURAL CONTENT PRINCIPLE

Use the following formula:

> **Authored quality + procedural composition + simulation meaning**

Not:

> **Randomness + more randomness + hope**

---

# 114. LONG-TERM VISION

If the project succeeds, it can grow into:

- larger worlds,
- more professions,
- richer settlement politics,
- more races,
- more monsters,
- advanced magic,
- sea travel,
- player-run towns,
- diplomacy,
- wars,
- kingdoms,
- guilds,
- more sophisticated ecology,
- richer social simulation.

But those are extensions.

The first responsibility is to create:

> **A polished world that feels alive, supports meaningful player lives, and is fun before the giant feature list arrives.**

---

# 115. FINAL NORTH STAR

When uncertain whether a feature belongs, ask:

> Does this help players live interesting lives inside a world that continues to exist without them?

If yes, it probably fits.

When uncertain whether a technical solution is acceptable, ask:

> Does this make the game easier to finish, easier to extend, easier to mod, and less likely to break later?

If yes, it is probably the right direction.

When uncertain whether procedural generation is good enough, ask:

> Would a player believe someone intentionally built this place?

If no, reject or improve it.

When uncertain whether the game is becoming too complicated, ask:

> Can a new player still move, fight, build, gather, and understand what is happening without reading a wiki?

If no, simplify presentation without necessarily simplifying the underlying systems.

And above all:

> **Finish the game.**

A smaller finished world is infinitely more valuable than the perfect fantasy-life simulator that exists only in design documents.

---

# 116. CURRENT ONE-SENTENCE PITCH

**A persistent multiplayer fantasy life RPG set in a procedurally generated living world, where settlements evolve without the player, professions have real depth, combat feels satisfying, and every player decides what kind of life they want to live.**

---

# 117. CURRENT SHORT PITCH

Explore a procedurally generated fantasy world of villages, towns, wilderness, roads, ruins, and dungeons. Build a home, master deep professions, trade, hunt, farm, craft, fight, travel, and live alongside settlements that continue to produce, struggle, recover, and change without waiting for you. Problems emerge naturally from the simulation rather than being constantly presented as quest markers. Play alone or on a persistent dedicated server where players can become adventurers, craftsmen, farmers, traders, builders, hunters, or anything in between.

---

# 118. PROJECT MOTTO

> **The world does not wait for you. It simply gives you somewhere worth living.**

