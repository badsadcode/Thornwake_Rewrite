# THORNWAKE MASTER REWORK SUPPLEMENT v1

**Status:** Authoritative supplement to `THORNWAKE_MASTER_REWORK_PLAN_v3.md`  
**Purpose:** Clarify and override selected design decisions discovered during source review and subsequent design discussion.  
**Precedence rule:** Where this supplement conflicts with `THORNWAKE_MASTER_REWORK_PLAN_v3.md`, **this supplement takes precedence**. Where there is no conflict, the Master Rework Plan remains authoritative.

This document is intentionally much smaller than the main Master Rework plan. It does not replace the plan and should not be treated as permission to rewrite working systems. It exists to lock down several decisions before world-generation work continues.

---

# 1. Core Implementation Rule

Preserve existing working architecture wherever it can reasonably support the revised design.

Do **not** replace terrain, water, drainage, world generation, save systems, streaming, collision, or other working systems merely because this supplement describes a cleaner target.

Prefer:

- incremental adaptation;
- explicit interfaces between systems;
- focused migration;
- bounded technical spikes;
- regression tests around known problem areas;
- keeping the project compiling and playable.

The current interrupted drainage/basin work is a valid foundation and should be continued from its present state unless a concrete technical problem is demonstrated.

---

# 2. World Generation: Interesting Worlds Before Perfect Realism

Thornwake should create worlds that feel coherent and believable, but **world-generation variety takes priority over strict Earth-like climate realism**.

The generator must not be designed so rigidly that similar latitude, elevation, moisture, and wind inputs repeatedly produce large worlds dominated by one moderate climate or one family of visually similar regions.

Real-world rules are useful as constraints and influences. They are not absolute laws.

The design goal is:

> **Plausible, coherent, varied fantasy geography.**

Not:

> **A climate simulator that happens to contain a game.**

World-generation profiles should eventually be able to influence things such as:

- biome diversity;
- climate contrast;
- region scale;
- frequency of extreme environments;
- mountainousness;
- wetness/dryness;
- island frequency;
- degree of regional visual contrast.

The generator may deliberately bias or rebalance climate/biome distribution when necessary to produce a more interesting world.


## 2.1 Azgaar Fantasy Map Generator as a World-Generation Reference

Azgaar Fantasy Map Generator is an intentional design and algorithmic reference for Thornwake's macro world generation.

Thornwake should **study and adapt useful ideas**, not depend on Azgaar, embed it, or attempt to port the whole application.

Useful areas of inspiration include:

- large-scale landmass composition;
- continent and island distribution;
- heightmap shaping;
- mountain ranges;
- broad drainage and river-network logic;
- climate/environmental layering;
- biome distribution;
- region coherence;
- deterministic seeded generation;
- world-generation templates/presets.

The principle is:

> **Take the brains, not the body.**

Thornwake remains its own 3D, explorable, cave-capable, terraformable world architecture.

### 2.1.1 World Shape Presets / Templates

Players should eventually be able to choose a broad **world shape preset** before generation.

A preset should influence macro geography without defining the exact map. The seed still produces a different world each time.

Examples include:

- one massive mainland;
- one major continent with surrounding islands;
- two major continents;
- several continents;
- scattered islands / archipelago;
- island-heavy seas;
- many medium landmasses;
- mountain-dominated world;
- inland-sea / broken-continent layouts;
- balanced/default world.

These are not handcrafted maps. They are parameter profiles for procedural generation.

For example:

`Scattered Islands preset + Seed A -> unique archipelago`

`Scattered Islands preset + Seed B -> different unique archipelago`

The system should allow additional presets to be added later without rewriting the macro generator.

A preset may influence parameters such as:

- expected number and scale of major landmasses;
- land-to-water ratio;
- continent compactness;
- fragmentation;
- island frequency and size distribution;
- mountain tendency;
- coastline complexity;
- large inland-water tendency;
- broad world-scale elevation shaping.

Presets should guide generation rather than rigidly guarantee an exact count where doing so would create unnatural results.

### 2.1.2 Advanced Customization Later

The first player-facing version may expose only clear presets and a seed.

Later, an advanced world-generation interface may expose selected parameters behind those presets for players who want more control.

The underlying generator should therefore distinguish between:

- the procedural generation algorithms;
- a data-driven generation profile/preset;
- the seed.

Avoid hardcoding every world type as a separate generator.


---

# 3. Generated Landforms and Terrain Features

Thornwake world generation should produce recognizable **landforms / terrain features**, not only generic noise-based hills and mountains.

The goal is for players to be able to look at the landscape and recognize meaningful forms such as:

- valleys;
- river valleys;
- ravines;
- gullies;
- gorges;
- canyons;
- chasms;
- plateaus;
- mesas;
- buttes;
- ridges;
- mountain ridges;
- highlands;
- lowlands;
- basins;
- depressions;
- hollows;
- cliffs;
- bluffs;
- escarpments;
- saddles;
- mountain passes;
- spurs;
- terraces;
- rolling hills;
- badlands;
- natural arches;
- rock bridges;
- fissures;
- sinkholes;
- karst-like terrain.

Water-related and coastal landforms may include:

- floodplains;
- meanders;
- deltas;
- estuaries;
- waterfalls;
- rapids;
- river gorges;
- wetlands;
- marshes;
- swamps;
- bays;
- coves;
- inlets;
- peninsulas;
- capes/headlands;
- isthmuses;
- archipelagos;
- lagoons;
- beaches;
- dunes;
- sea cliffs;
- sea stacks;
- fjord-like coastlines where appropriate.

Volcanic and cold-region landforms may eventually include:

- volcanoes;
- calderas;
- craters;
- lava fields;
- lava tubes;
- volcanic cones;
- volcanic plateaus;
- glacial valleys;
- cirques;
- moraines;
- ice fields;
- fjords.

This list is not a requirement to implement every named feature immediately. It defines the kinds of terrain vocabulary the architecture should be capable of supporting over time.

## 3.1 Landforms Should Emerge From World Structure

Do not treat every landform as an isolated prefab stamped randomly into terrain.

Where practical, landforms should arise from or be constrained by the broader world-generation layers.

Examples:

`mountain structure + drainage + erosion-style shaping -> gorge / canyon`

`elevated broad terrain + flattened high area -> plateau`

`plateau + strong erosion breakup -> mesas / buttes`

`mountain range + selected low saddle -> mountain pass`

`river + broad low terrain -> floodplain`

`coastline + steep elevation transition -> sea cliffs`

`volcanic world feature + local terrain shaping -> crater / caldera / lava field`

The generator may still use authored profiles, procedural feature generators, masks, splines, signed-distance fields, or other practical techniques. The requirement is visual and structural coherence, not strict geological simulation.

## 3.2 Landform Library / Data-Driven Definitions

Where useful, regional landforms should eventually be represented through configurable definitions or parameter profiles rather than hardcoded one-off algorithms.

A landform definition may eventually describe things such as:

- scale range;
- height/depth range;
- width;
- slope preference;
- elongation;
- roughness;
- edge steepness;
- erosion/noise profile;
- compatible elevation;
- compatible climate/biome tendencies;
- relation to rivers/water;
- relation to mountains;
- rarity;
- preferred region size;
- whether it can overlap or combine with other terrain features.

Do not require the future World Site Editor to author every landform unless that becomes useful later. The immediate requirement is that the world-generation architecture is not locked to a small hardcoded set of terrain shapes.

## 3.3 Landforms and World Presets

World-shape presets may influence landform frequency and style.

Examples:

- `Mountain Kingdoms` may favor ridges, deep valleys, passes, cliffs, and high plateaus.
- `Island Seas` may favor sea cliffs, coves, headlands, beaches, volcanic islands, and archipelagos.
- `Great Mainland` may allow broader plains, large river valleys, mountain chains, and major inland basins.
- `Broken Continents` may favor escarpments, inland seas, peninsulas, island chains, and dramatic coastlines.
- `Badlands`-leaning profiles may favor mesas, buttes, ravines, gullies, and exposed cliffs.

These are weighting tendencies, not guarantees.

## 3.4 Biome and Landform Interaction

Biomes should not own the geometry of the world, but they may prefer or react to certain landforms.

Examples:

- alpine biomes may favor ridges, high valleys, cliffs, and plateaus;
- swamp biomes may favor low basins, wetlands, and floodplains;
- desert biomes may favor dunes, badlands, mesas, canyons, and dry basins;
- dark forest biomes may occupy ravines, hollows, misty valleys, or dense rolling terrain;
- coastal biomes may react to beaches, cliffs, coves, estuaries, and headlands.

The relationship should remain flexible:

`world structure -> landforms -> climate/environment suitability -> biome placement/refinement`

rather than:

`biome -> invent arbitrary terrain`

This keeps the world coherent while still allowing strongly distinct regional identity.

## 3.5 Current Scope Boundary

Do **not** stop the current drainage/world-generation work to implement the entire landform library immediately.

The current requirement is to preserve and extend the macro terrain architecture so recognizable regional landforms can be added incrementally.

Avoid overengineering full geological erosion, plate tectonics, or physically exact geomorphology.

The guiding rule remains:

> **Believable landforms, not geological simulation.**

---

# 4. Climate and Biomes Are Related, But Climate Does Not Dictate Biomes

Climate should provide broad environmental plausibility and suitability.

It may consider:

- temperature;
- moisture;
- precipitation tendency;
- elevation;
- terrain exposure;
- proximity to major water;
- prevailing wind or broad atmospheric influences.

However, biome selection must **not** be a one-to-one deterministic mapping such as:

`temperature + moisture = exactly one biome`

Instead:

`environmental conditions -> compatible biome candidates -> weighted world-generation choice`

Several different biome identities may be compatible with broadly similar environmental conditions.

For example, similar cool/wet conditions might support:

- temperate forest;
- old-growth forest;
- mist forest;
- dark woodland;
- wet pine forest;
- haunted woodland;
- marshy forest.

This is intentional.

The generator should consider:

- biome compatibility with environmental conditions;
- existing nearby biomes;
- desired region size;
- rarity;
- adjacency preferences;
- world-level variety targets;
- seed;
- authored weights.

The guiding rule is:

> **Biomes should be plausible, not inevitable.**

---

# 5. Biomes Are Thornwake's Main Regional Identity Layer

Do not introduce a separate mandatory "regional theme" or "zone identity" system unless a concrete need appears later.

A biome may carry much more identity than a narrow ecological label.

A future biome definition may control or influence:

- compatible temperature/moisture/elevation ranges;
- terrain materials;
- vegetation families;
- vegetation density;
- tree age/size tendencies;
- rocks and ground clutter;
- water/wetland tendencies;
- fog frequency and intensity tendencies;
- atmospheric character;
- weather tendencies;
- ambient sound sets;
- visual wetness tendency;
- world-site preferences;
- settlement/environment compatibility;
- future ecology/creature tendencies.

This is how Thornwake can support regions with strongly different "vibes" while keeping the architecture relatively simple.

Examples may include ordinary and strongly stylized identities:

- temperate woodland;
- bright woodland;
- old-growth forest;
- dark woodland;
- swamp;
- mist marsh;
- savanna;
- rocky badlands;
- sand desert;
- alpine;
- tundra;
- tropical jungle;
- desolate wasteland;
- magical or horrory forest.

Not every neighboring biome should be dramatically different. Some regions should feel related and transition gradually. Strongly distinctive biomes should be able to appear more rarely and create memorable changes in atmosphere.

Avoid making the world feel like a theme park where every few kilometres are unrelated.

---

# 6. Biome Definitions Must Be Data-Driven

The world generator should consume **data-driven biome definitions** rather than hardcoding all biome behavior directly in C#.

For now, temporary/test biome definitions are acceptable.

Later, the planned World Site Editor will include a **Biome Maker** that creates and edits these same definitions.

The intended architecture is:

`Biome Maker (later) -> BiomeDefinition data -> World Generator`

The Biome Maker itself should **not** be implemented until the underlying world-generation and biome-definition architecture is sufficiently stable.

The editor is a future front end for an already functioning data model.

## 6.1 Biome Inhabitants, Species, Races, Tribes, and Creatures

Biome definitions must also be able to store editable information about the kinds of inhabitants that naturally belong in that biome.

This is future-facing data architecture. It does **not** mean the current world-generation phase should implement full population simulation, settlement simulation, migration, ecology, or creature spawning.

The purpose is to ensure that the biome data model is not limited to terrain, vegetation, and weather.

A biome should eventually be able to define or reference natural inhabitants such as:

- playable or non-playable races;
- species;
- peoples;
- cultures;
- tribes or factions;
- wildlife;
- monsters;
- supernatural creatures;
- other inhabitant categories added later.

The future Biome Maker must allow these relationships to be edited without changing C# code.

For example, an authored biome might conceptually contain:

`Dark Woodland`

- Wood Elf clans: strong natural affinity
- Goblin tribes: medium natural affinity
- Wolves: strong natural affinity
- Giant spiders: medium natural affinity
- Humans: low natural affinity

These values describe **natural suitability and likelihood**, not hard exclusion.

The core rule is:

> **Native does not mean exclusive.**

A race, species, tribe, creature, traveller, merchant, army, refugee, adventurer, migrant, or settlement may appear outside its preferred biome when another system gives a reason for it.

Biomes should influence where inhabitants are naturally likely to:

- originate;
- establish settlements or camps;
- maintain territory;
- spawn or reproduce;
- be encountered as local wildlife;
- appear as common regional inhabitants.

They should not normally create invisible biome walls.

### 6.1.1 Editable Affinity Data

The exact data structure may evolve, but the biome model should leave room for editable inhabitant-affinity entries.

A future entry may reference an inhabitant definition and contain values such as:

- inhabitant/race/species/tribe/creature ID;
- natural affinity or suitability weight;
- rarity/commonness;
- settlement suitability;
- encounter or wildlife suitability;
- optional required or avoided environment tags;
- optional explicit overrides.

Do not require every field above during the current phase. The important requirement is that the biome definition can be extended cleanly and that these associations are data-driven.

### 6.1.2 Species and Tribe Preferences May Differ

Broad species/race preferences and specific cultural/tribal preferences should not be forced to be identical.

For example, several human cultures may prefer very different environments:

- river people may favor fertile valleys, rivers, and wetlands;
- highland people may favor hills, mountains, and colder regions;
- desert tribes may favor steppe, badlands, and arid terrain.

This allows one race/species to inhabit many parts of the world without making every culture feel environmentally identical.

### 6.1.3 Biome Affinity May Work With Habitat Tags

To avoid requiring every biome to hardcode every possible future creature, the architecture may also support habitat/environment tags.

Example:

`Biome tags: forest, cool, wet, dense`

`Creature preferences: forest, cool`

This can provide broad automatic compatibility.

However, the Biome Maker must still support explicit authored inhabitant entries and overrides where strong regional identity is desired.

For example:

`Haunted Forest -> Wraiths: strongly favored`

The intended long-term approach is therefore:

`environment/habitat tags + editable biome affinities + inhabitant preferences + authored overrides`

without turning the current Master Rework into a full ecology simulator.

### 6.1.4 World-Site and Settlement Integration Later

When settlement and population generation are developed later, they may query biome inhabitant data to help answer questions such as:

- which peoples are naturally suited to this region;
- which tribes commonly establish camps here;
- which races are likely to found settlements here;
- what wildlife commonly inhabits the surrounding area;
- which creatures are rare, common, or unusual here.

This information should be available to later World Site, settlement, ecology, and encounter systems.

Do **not** implement those later systems merely because the biome data model supports them.

## 6.2 Aquatic Habitats and Water-Life

Water bodies are habitats and must be able to participate in the same future inhabitant/ecology architecture as land biomes.

Thornwake should eventually support natural aquatic life in:

- rivers;
- streams;
- lakes;
- ponds;
- wetlands;
- coastal waters;
- seas;
- oceans;
- deep ocean;
- underwater caves;
- special or magical water bodies.

Possible inhabitants include both real-world-inspired and fantasy life, for example:

- freshwater fish;
- saltwater fish;
- jellyfish;
- crustaceans;
- amphibious creatures;
- sharks;
- whales and other large marine animals;
- eels;
- cave fish;
- magical aquatic creatures;
- sea monsters;
- leviathans;
- corrupted or supernatural water life.

The current Master Rework does **not** need to implement full aquatic ecology, fish AI, migration, spawning, food chains, or underwater creature simulation.

The requirement is architectural: water bodies and aquatic habitat definitions must be able to store enough editable information for those systems later.

### 6.2.1 Water-Body Habitat Identity

Aquatic suitability should not depend only on the surrounding terrestrial biome.

A single forest biome might contain:

- a cold fast stream;
- a shallow pond;
- a large freshwater lake;
- a deep cave pool.

Those bodies may support completely different aquatic life.

A future water-body or aquatic-habitat definition may therefore expose properties such as:

- freshwater / saltwater / brackish / special;
- river / stream / pond / lake / coastal / sea / open-ocean / deep-ocean / cave-water identity;
- depth range;
- current or flow strength;
- temperature tendency;
- water clarity/turbidity;
- light availability;
- connection to ocean or river systems;
- surrounding biome/region;
- magical, poisonous, corrupted, volcanic, or other special tags.

Not all properties need to exist immediately. The important point is that the architecture does not reduce all water to a single generic habitat.

### 6.2.2 Aquatic Inhabitant Affinities

Aquatic creatures should be able to declare habitat preferences in the same general spirit as land inhabitants.

Examples:

`Trout`
- freshwater
- cool
- stream / river / lake
- shallow to medium depth

`Shark`
- saltwater
- coastal / sea / open ocean
- medium to deep water

`Whale`
- saltwater
- open ocean
- deep water
- large connected water body

`Swamp Eel`
- freshwater
- warm
- wetland / swamp / slow water
- shallow

`Fantasy Leviathan`
- saltwater
- deep ocean
- very rare

`Crystal Jellyfish`
- magical or cave water
- low light
- lake / underground pool

These are illustrative examples only, not required content.

The same core rule applies:

> **Preferred habitat does not mean exclusive habitat.**

Aquatic creatures may appear outside their normal habitat when another system gives a reason, including:

- migration;
- displacement;
- World Sites;
- magical effects;
- authored encounters;
- breeding or stocking;
- portals;
- corruption;
- narrative events.

### 6.2.3 Editable Aquatic Ecology Data

The future World Site Editor / Biome Maker ecosystem must be able to edit aquatic habitat associations without requiring C# changes.

The exact UI may be decided later, but the data model should support relationships between:

- biome definitions;
- water-body or aquatic-habitat definitions;
- creature/species definitions;
- inhabitant affinity/rarity weights;
- explicit authored overrides.

This does not require every aquatic creature to be listed manually in every biome.

Habitat tags and creature preferences may provide broad automatic compatibility, while authored overrides provide strong regional identity when needed.

The intended long-term relationship is approximately:

`BiomeDefinition -> terrestrial habitat context`

`WaterBody/AquaticHabitat -> aquatic habitat context`

`Creature/SpeciesDefinition -> habitat preferences`

`authored affinities/overrides -> regional identity`

### 6.2.4 Water Generation Should Preserve Habitat-Relevant Metadata

When practical, generated water bodies should preserve stable metadata that later ecology systems can query.

Examples may include:

- water-body ID;
- water type;
- approximate size;
- approximate depth class;
- river/lake/ocean classification;
- connectivity;
- flow class;
- biome/region;
- special environmental tags.

Do **not** add expensive ecology simulation during current drainage work merely to populate these fields.

Preserve the ability to add this information cleanly later.

---

# 7. Weather: Deep Atmosphere, Selective Gameplay Consequences

Weather is a high-priority presentation and atmosphere system for Thornwake.

The target is not a simple random preset switcher. Weather may be simulated in the background with meaningful state, regional variation, and believable transitions.

Potential atmospheric state may include:

- cloud cover;
- cloud type/density;
- temperature;
- humidity;
- wind direction and strength;
- fog potential;
- storm intensity;
- precipitation state;
- lightning/thunder state;
- regional weather-cell movement;
- broad fronts or weather regions.

Weather should be capable of convincing transitions such as:

`clear -> cloud build-up -> wind increase -> drizzle -> heavy storm -> passing rain -> misty wet aftermath -> clearing`

Weather should drive presentation systems such as:

- sky;
- clouds;
- lighting;
- sun/shadow strength;
- fog and haze;
- rain/snow particles;
- wind;
- vegetation movement;
- surface wetness appearance;
- lightning;
- thunder delay;
- environmental audio;
- visibility;
- ocean/lake surface appearance and wave state where appropriate.

Regional weather variation is desirable. It should eventually be possible for a player to see weather affecting a distant region before it reaches the player's position.

Biome definitions may influence weather tendencies and probabilities.

The guiding distinction is:

> **Weather may matter to gameplay without becoming a full physical hydrology simulation.**

## 7.1 Weather May Affect Gameplay Where It Makes Sense

Weather should be allowed to have selected, understandable gameplay consequences.

Examples may include:

- reduced visibility in fog, heavy rain, snow, or dust;
- wind influencing sailing;
- storms making sailing more difficult or dangerous;
- large storm waves affecting boats;
- lightning creating environmental danger where appropriate;
- cold/heat influencing future survival systems if those systems are enabled;
- rain or snow affecting selected movement surfaces if configured;
- weather influencing creature/NPC behavior later if that system exists.

These effects should remain practical, legible, and configurable.

Do not add a gameplay penalty merely because it would be realistic. Weather consequences should improve atmosphere, decision-making, exploration, or survival gameplay rather than become constant annoyance.

## 7.2 Sailing, Wind, and Storm Waves

Future sailing should be able to respond to weather.

A storm at sea may involve:

- stronger and less forgiving wind;
- changing wind direction;
- reduced visibility;
- heavy rain;
- lightning;
- larger waves;
- stronger boat motion;
- reduced stability or steering control;
- greater risk for small or unsuitable boats.

Very severe weather may eventually create genuine travel risk.

This does **not** require full real-time ocean-fluid simulation.

Large waves may be represented using a practical procedural wave surface and boat-response model. The ocean does not need to gain or lose physical stored water volume because a storm occurs.

Wave behavior may vary by water-body context. An exposed ocean may produce much larger waves than a protected lake or narrow river.

Exact sailing mechanics remain deferred to the later traversal/vehicle phase.

## 7.3 Runtime Weather Does Not Drive Physical Hydrology

This remains an explicit design boundary.

Runtime rain/snow/weather does **not** need to:

- fill lakes;
- increase river stored volume;
- fill player-dug holes with simulated rainfall;
- create persistent physical puddles;
- alter groundwater;
- erode terrain;
- change terrain geometry;
- dynamically recalculate watershed discharge;
- permanently raise ocean level because of a storm.

Wet surfaces, temporary puddle visuals, snow cover, darker materials, dripping roofs, splashes, and similar effects may be visual only.

Climate may influence world generation, but runtime weather does not need to feed the physical water-volume model.

The guiding rule is:

> **Deep atmospheric simulation, selective gameplay coupling, no mandatory weather-to-hydrology simulation.**

## 7.4 Surface and Travel Effects

Terrain and constructed surfaces may eventually provide mild movement modifiers where doing so improves the feel of travel.

Possible surface categories include:

- paved or improved roads;
- dirt roads;
- open grass;
- rough wilderness;
- dense undergrowth;
- mud;
- swamp;
- loose sand;
- snow;
- rock;
- other authored terrain surfaces.

A dirt road, for example, may be configured as neutral, slightly slower, or slightly faster than ordinary open terrain depending on the desired ruleset.

Rain may optionally make selected dirt/mud surfaces somewhat slower or more difficult without physically changing terrain geometry or simulating soil saturation.

These effects should normally be subtle rather than punishing.

Players/hosts should be able to reduce or disable such movement penalties through sandbox settings.


---

# 8. Water Surface Quality, Waves, Shorelines, and Boat Interaction

Water presentation is a major visual-quality target for Thornwake.

Oceans, seas, large lakes, rivers, ponds, and cave pools should not all look or behave like the same flat animated material.

The long-term target is **high-quality, semi-realistic water presentation** that is appropriate for close first-person and third-person viewing while remaining practical for a large procedural world.

This is primarily a **surface-state, rendering, and interaction problem**, not a requirement for full fluid simulation.

The intended separation is approximately:

`hydrology / water-body data -> water surface state -> renderer + boat interaction`

The hydrology system remains responsible for things such as:

- water level;
- stored volume where relevant;
- connectivity;
- flow;
- sources/outlets;
- local flooding.

The water-surface system may then derive visual and physical surface behavior from:

- water-body type;
- water-body size;
- wind;
- weather/storm state;
- approximate exposure/fetch;
- local flow;
- depth;
- shoreline proximity;
- obstacles such as rocks.

Do not require the hydrology solver itself to simulate every visible wave.

## 8.1 Dynamic Sea State

Oceans and large exposed bodies of water should support a changing **sea state**.

Examples:

- calm weather -> small ripples and gentle swell;
- moderate wind -> noticeable wind waves;
- strong wind -> rougher water, chop, stronger swell;
- storms -> large waves, heavy motion, spray, reduced visibility, and dangerous sailing.

Wave intensity should not remain permanently high.

Weather and wind should influence the state of the water over time.

Large lakes may also become rough in strong wind, but their maximum wave behavior may differ from open ocean.

Very small water bodies should remain appropriately calm.

The implementation may use practical procedural techniques such as layered wave functions, Gerstner-style waves, spectral techniques, or another suitable method discovered during later prototyping.

This supplement does **not** mandate a specific algorithm.

The quality target is:

> **Convincing, semi-realistic water motion that scales appropriately from small water bodies to stormy open seas.**

## 8.2 Shared Wave Field for Rendering and Boats

Visible waves and boat physics should not disagree.

Avoid situations where:

- the shader shows large waves but boats move across an invisible flat plane;
- the visual crest is in one place while buoyancy uses a different unrelated surface;
- storm waves look dangerous but have no physical effect on boats.

The water-surface system should eventually expose a consistent queryable surface representation suitable for both rendering and gameplay.

Conceptually it may provide information such as:

- surface height at a world position and time;
- surface normal/slope;
- local wave motion or velocity;
- local wave strength;
- dominant wave direction.

Exact API design is deferred.

Boats should be able to sample the same effective wave field used to represent the visible water surface.

This allows boats to:

- rise and fall with waves;
- pitch;
- roll;
- react to wave direction;
- respond differently to calm water and storms;
- become harder to control in severe sea states;
- experience greater risk when the vessel is poorly suited to the conditions.

Full rigid-body boat/sailing implementation remains deferred to the later traversal/sailing phase.

## 8.3 Water-Body-Specific Surface Profiles

Different water-body classes should be able to use different surface behavior.

Examples:

**Ocean / open sea**
- broad swell;
- wind-driven chop;
- strong storm waves;
- breaking-wave and foam potential;
- large-scale motion.

**Large lake**
- moderate swell;
- wind-driven waves;
- rough storm state when sufficiently exposed;
- lower maximum wave character than open ocean where appropriate.

**Small lake / pond**
- mostly gentle ripples;
- limited wave height;
- little or no broad ocean-style swell.

**River**
- directional flow;
- ripples;
- turbulence;
- rapids where flow/terrain causes them;
- interaction with rocks and banks.

**Cave pool**
- generally calm;
- subtle ripples;
- local disturbance near inflow/outflow.

The system should reuse common technology where practical while allowing distinct parameter profiles.

## 8.4 Shoreline Quality

Shorelines are a high-priority visual area.

The transition between land and water should avoid looking like a simple flat plane intersecting terrain.

Future water rendering should support high-quality shoreline treatment where practical, including:

- shallow-water color changes;
- depth-based transparency;
- visible submerged terrain and rocks in shallow water;
- shoreline foam;
- wave-breaking effects where appropriate;
- wet shoreline materials;
- smooth depth transitions;
- underwater refraction/distortion;
- reflections appropriate to the rendering budget;
- localized turbulence;
- spray during rough weather.

The exact implementation may combine shaders, procedural masks, depth information, decals, particles, generated shoreline data, or other techniques.

Do not require all shoreline effects to be physically simulated.

## 8.5 Foam Around Rocks and Obstacles

Foam and turbulence around rocks, cliffs, piers, and other water obstacles are desirable visual features.

The amount and intensity may depend on:

- wave strength;
- local flow strength;
- obstacle exposure;
- water depth;
- storm state.

Calm water may show little or no foam.

Rough weather may create stronger foam and spray.

These effects may remain primarily visual and do not need to alter stored water volume.

The system should avoid a permanently painted white outline around every shoreline or rock.

Foam should appear dynamic and context-sensitive where practical.

## 8.6 Wind, Exposure, and Approximate Fetch

Wave generation may use a simplified approximation of how much open water is exposed to the wind.

A practical system may consider:

- wind strength;
- wind direction;
- water-body size;
- approximate open-water distance in the wind direction;
- whether the location is sheltered by land;
- storm intensity.

This does **not** require real oceanographic simulation.

The goal is simply to prevent obviously implausible results such as:

- huge ocean swells in a tiny pond;
- completely flat open ocean during a severe storm;
- identical wave state everywhere regardless of exposure.

## 8.7 Rendering and Physics Must Remain Separable From Hydrology

The water-rendering system, wave model, foam, spray, and boat-wave interaction should be layered on top of the water-body/hydrology model.

Do not rewrite the local water-volume simulation merely to support pretty ocean waves.

Similarly:

- visual foam does not need to be stored water;
- spray does not need to be simulated as hydrological volume;
- storm waves do not need to raise the permanent ocean level;
- wave animation does not need to modify macro drainage.

The systems should communicate through clean data rather than being collapsed into one giant fluid simulation.

## 8.8 Configurability

Water and sailing presentation/gameplay should participate in the sandbox-settings architecture.

Potential future settings may include:

- overall wave intensity multiplier;
- storm-wave severity;
- wind influence on waves;
- sailing wind influence;
- wave influence on boats;
- boat stability/difficulty;
- foam/spray quality as a graphics option;
- water-rendering quality;
- reflection quality;
- shoreline-effect quality.

Simulation-affecting settings should be server/host authoritative in multiplayer.

Pure rendering-quality settings should remain client-local.

## 8.9 Current Scope Boundary

Do **not** interrupt the current drainage/basin/world-generation milestone to implement the final ocean renderer, complete shoreline system, or sailing physics.

For the current phase:

- preserve a clean separation between hydrology and surface rendering;
- preserve water-body identity and size/type metadata where practical;
- avoid architecture that assumes all water surfaces are permanently flat;
- leave a clean way for future wave-surface queries to serve both rendering and boats.

Detailed ocean rendering, storm-wave prototyping, shoreline foam, spray, and boat response belong to a later focused water/sailing implementation phase.

The rule is:

> **Make the water system capable of becoming beautiful and physically meaningful later without turning current hydrology into an ocean simulator now.**

---

# 9. Revised Natural Water Policy

This section overrides the broad "most natural water may simply be infinite/persistent" interpretation in Master Rework v3.

The existing local water solver's separation between stored volume, supplied volume, and drained volume is useful and should be preserved where practical.

The target production behavior is a **hybrid** model.

## 9.1 Oceans and Major Seas

Oceans and very large seas may behave as effectively infinite boundary conditions.

There is no requirement to simulate their complete volume.

## 9.2 Rivers

Rivers should be represented as persistent **rate-limited flow/inflow**, not as infinitely replenishing static water volumes at every local chunk.

A loaded local region may receive an abstract upstream inflow from the macro world.

Water should then move through actual local terrain connectivity.

Downstream export/removal should occur only when water reaches a valid downstream boundary/outlet.

Do not create an independent magical water source in every river chunk.

## 9.3 Springs

Springs may provide persistent, relatively small, rate-limited inflow.

## 9.4 Lakes

Lakes should conceptually have finite stored water volume plus whatever inflow sources feed them.

A lake may therefore:

- remain stable at equilibrium;
- rise when inflow exceeds outflow;
- fall when outflow exceeds inflow;
- drain partially or substantially if the player creates a new lower outlet;
- refill if persistent inflow continues.

Exact large-scale litre simulation is not required.

## 9.5 Ponds and Underground Pools

Small ponds and underground pools are normally finite unless explicitly fed by a source.

## 9.6 Magical Water

Explicit magical or authored water sources may deliberately ignore these rules and remain infinite if required by content.

## 9.7 No Global Real-Time Fluid Simulation

This revised policy does **not** require:

- global conservation of every litre;
- full groundwater;
- evaporation;
- soil infiltration;
- real-time rainfall runoff;
- global fluid pressure;
- constant simulation of unloaded watersheds.

The model should remain practical and hierarchical.

The guiding rule is:

> **Simulate enough to create believable consequences, not enough to require a hydrology degree.**

---

# 10. Macro Drainage Is a Skeleton, Not Final River Geometry

The current coarse macro drainage/basin work should remain a broad world-scale structural layer.

Its responsibilities include concepts such as:

- broad downhill routing;
- catchments;
- basins;
- spill levels;
- coarse contributing area;
- ocean destination;
- upstream/downstream relationships.

At large world sizes, coarse drainage cells may span many kilometres. They must **not** be treated as final river channels.

Later regional/local generation should refine macro drainage into:

- actual river corridor placement;
- riverbed geometry;
- local width/depth;
- lake footprints;
- tributaries;
- crossings;
- local terrain interaction.

World generation should preserve useful stable metadata where practical, such as:

- river/drainage ID;
- upstream/downstream relationships;
- basin/lake relationship;
- source/headwater identity;
- eventual ocean/outlet relationship;
- approximate natural flow category.

The macro network represents the world's natural drainage intent.

Player terrain modification may locally override that natural route.

---

# 11. Macro/Local Hydrology Boundary

The macro system should not attempt to simulate every local water cell across the world.

Loaded regions may receive abstract information from unloaded world areas, for example:

- upstream inflow rate;
- downstream connection;
- expected natural level;
- source identity.

The local water simulation handles real local terrain connectivity.

When water reaches a downstream boundary:

- pass it to a loaded neighboring region when applicable; or
- export it to the abstract downstream macro system when the neighbor is unloaded.

Player-built dams, channels, tunnels, and excavations may change the local route.

The macro drainage map is therefore a **natural baseline**, not an enforced path.

Unloaded modified regions may later use simplified persistent state instead of full continuous local simulation.

---

# 12. Mineral Deposits and Ore Veins

Do not overcomplicate geology.

A mineral deposit may simply be an irregular **3D body/blob in world space**.

The world generator decides:

- deposit type;
- location;
- size;
- orientation;
- shape seed;
- richness;
- broad distribution rules.

A deposit may be:

- completely buried;
- partly exposed at the surface;
- exposed in a cliff;
- intersected by a cave;
- visible in a cave wall;
- exposed later by player excavation.

The terrain and cave systems reveal whichever portion of the deposit they intersect.

A "vein" may simply be a stretched, rotated, noise-distorted body rather than requiring a separate complex geological system.

Different materials may prefer different broad shape tendencies, for example:

- compact blobs;
- elongated veins;
- flattened seams;
- clustered pockets.

The underlying deposit is the authoritative resource body.

Visual presentation may combine:

- material/texture blending;
- normal/roughness detail;
- selective embedded geometry;
- crystals, nodules, seams, or chunks where appropriate.

Do not require every visible ore fragment to be an independent physics object.

Chunk boundaries must not cause deposits to terminate artificially.

---

# 13. Volumetric Terrain Remains Required

The old playable heightfield-style terraforming remains useful as a **quality reference**, but it cannot provide the final Thornwake terrain architecture because it cannot support arbitrary caves, tunnels, and overhangs.

Thornwake should therefore retain a cave-capable volumetric terrain direction unless source-level testing demonstrates that the architecture itself is unworkable.

However:

> **The current cave-development excavation appearance is not the final gameplay quality target.**

Current development-lab behavior uses coarse volumetric sampling and a reference mesher. Jagged excavation seen in the lab must not automatically be treated as an unavoidable property of the final system.

Before locking the production terrain path, investigate the respective contribution of:

- volumetric sample resolution;
- meshing algorithm;
- surface normals;
- brush shape;
- brush falloff;
- repeated overlapping edits;
- post-edit local smoothing/relaxation;
- collision generation;
- water topology sampling.

External open-source terrain projects may be studied for algorithms and architecture, but Thornwake should not adopt a terrain plugin as a dependency merely because it solves a similar problem.

Use external implementations as **references, not dependencies**.

---

# 14. Gameplay Terraforming Tools May Use Different Influence Kernels

Gameplay tools should not all apply the same generic spherical developer brush.

Tool behavior may differ by:

- influence shape;
- radius;
- depth;
- strength;
- material compatibility;
- falloff;
- smoothing;
- leveling behavior.

Examples:

**Shovel**
- broad;
- relatively shallow;
- soft falloff;
- suitable for soil shaping.

**Pickaxe**
- smaller;
- stronger/localized;
- suitable for rock and deeper cuts.

**Hoe / leveling tool**
- little or no deep excavation;
- flattening, smoothing, or target-height behavior.

Exact gameplay values are deferred until the tool/item design phase.

The terrain system itself must still produce acceptable surfaces. Tool smoothing must not become a hack used to conceal a fundamentally inadequate terrain representation.

---

# 15. Terrain, Water, and Collision Must Agree

Thornwake needs explicit consistency guarantees between:

- rendered terrain;
- physical collision;
- volumetric terrain field;
- local water connectivity.

A player should not see an apparently solid ridge, wall, or cave ceiling while water passes through it because the water topology sampled a materially different representation.

The known development-lab mismatch between visible terrain resolution and fixed water-topology sampling must be investigated before production local/macro hydrology integration.

Add targeted regression scenarios for:

- thin ridges;
- narrow channels;
- low ceilings;
- partially excavated walls;
- openings close to water-cell resolution;
- edits that cross terrain-region boundaries.

Do not merely hide discrepancies with shaders.

---

# 16. Production Terrain Collision Must Be Chunked and Streamed

The current development-lab approach of creating collision from the same rendered triangle mesh is acceptable as a prototype, not as the final large-world requirement.

Production terrain collision should remain:

- chunk-local;
- streamable;
- rebuildable after local terrain edits;
- removable when no active physics observer requires it.

The entire world must never require one enormous high-detail terrain collider.

Visual terrain residency and physics residency may be different.

Collision may eventually use:

- different resolution from rendering;
- independent LOD;
- simplified far collision;
- no collision at all for regions where physics interaction is impossible or unnecessary.

Near players, cave entrances, edited terrain, buildings, and physically interactive areas must remain accurate enough that:

- tunnels do not have invisible blockers;
- cave openings match visuals;
- steep terrain feels correct;
- structures do not float;
- terrain/water/physics do not disagree noticeably.

Multiplayer architecture must eventually support multiple active observer/physics regions rather than assuming one player at one streaming origin.

---


# 17. Future Liquid Types and Volcanic Features

Water is the current reference liquid and remains the only liquid that needs full production implementation during the present world-generation phase.

However, the local liquid architecture should avoid unnecessary assumptions that every liquid is permanently "water". It should leave a clean extension point for future liquid types without forcing the current water system to become a generic full-fluid simulator.

Possible future liquid types include:

- lava;
- acid;
- tar or heavy sludge;
- corrupted or poisonous liquids;
- magical liquids;
- other authored fantasy fluids.

Different liquid types may later define properties such as:

- flow rate;
- effective viscosity;
- damage;
- temperature;
- buoyancy;
- whether characters can swim in them;
- whether they are drinkable;
- source behavior;
- outlet behavior;
- rendering/material;
- transparency;
- light emission;
- fumes or particles;
- cooling, hardening, or other special state changes.

The existing water implementation should remain the production reference implementation. Do **not** generalize or rewrite it prematurely unless doing so is required to preserve a clean extension point.

## 17.1 Liquid Mixing Is Not a Current Requirement

Different liquid types do not need a full physical mixing simulation.

If future liquids meet, interactions may be handled using explicit boundary rules.

Example:

`water + lava contact -> steam / cooling effect / possible solid rock creation`

There is no requirement to simulate arbitrary percentage mixtures or chemistry.

A local fluid region may use one dominant liquid type unless a later gameplay feature demonstrates a real need for something more complex.

## 17.2 Volcanoes and Volcanic Regions

The world-generation architecture should not prevent future volcanic features such as:

- extinct volcanoes;
- dormant volcanoes;
- active volcanoes;
- volcanic islands;
- calderas;
- crater lakes;
- lava fields;
- basalt terrain;
- lava caves or tubes;
- volcanic biome identities.

These features do not require plate tectonics, mantle simulation, or scientifically complete volcanology.

A volcano may be represented as a generated geological/world feature with authored or procedural parameters.

An active volcano may later provide persistent lava source locations, but lava does not need to participate in the normal world-scale river/drainage network.

## 17.3 Special Non-Water Lakes

The architecture should also allow future special bodies such as:

- acid lakes;
- poisonous pools;
- magical lakes;
- corrupted swamps or pools.

These may be associated with:

- special biomes;
- geology;
- World Sites;
- authored world features;
- magical or narrative causes.

They do not need to follow ordinary natural-water hydrology unless explicitly designed to do so.

## 17.4 Current Scope Boundary

Do **not** implement lava, acid, tar, liquid mixing, volcano simulation, or special-liquid gameplay during the current drainage/world-generation milestone unless a very small architectural hook is necessary to avoid hardcoding water-specific assumptions.

The immediate priority remains the current water/drainage/world-generation foundation.

---

# 18. World Naming and Persistent Place Identity

Thornwake should have a data-driven procedural naming system for generated **world places and world features**.

Names should come from editable **dictionary files / naming data files**, not from hardcoded C# string lists.

The naming system should be reusable by any world-generation or later World Site system that creates a persistent named place.

Possible named world entities include:

- worlds;
- continents and major islands;
- regions or provinces;
- mountain ranges;
- individual notable peaks;
- highlands;
- forests;
- swamps;
- deserts;
- badlands;
- rivers;
- streams when important enough to name;
- lakes;
- seas;
- bays;
- islands and archipelagos;
- valleys;
- canyons;
- passes;
- roads;
- towns;
- villages;
- settlements;
- ruins;
- World Sites;
- landmarks;
- caves or cave systems when they receive a persistent identity;
- other notable generated geographic features.

Not every minor procedural feature needs a name.

The system should name things that are important enough to be referred to by the map, UI, quests, NPC dialogue, travel systems, save data, or player memory.

## 18.1 Dictionary-Based Name Generation

Name generation should use external editable dictionary resources.

These may contain combinations of:

- complete names;
- prefixes;
- suffixes;
- stems;
- syllables;
- geographic words;
- descriptors;
- cultural words;
- connectors;
- optional naming patterns/templates.

The exact storage format may be chosen during implementation. Suitable formats may include structured text, JSON, Godot resources, or another simple data format.

The important requirements are:

- naming content is editable without recompiling C#;
- new naming dictionaries can be added later;
- different world features can use different dictionaries;
- dictionaries may be shared where appropriate;
- dictionaries may eventually be associated with cultures, peoples, regions, or world styles.

Example conceptual dictionary groups might include:

`WorldNames/Regions`

`WorldNames/Rivers`

`WorldNames/Lakes`

`WorldNames/Mountains`

`WorldNames/Settlements`

`WorldNames/Islands`

These paths are illustrative, not mandatory file-system requirements.

## 18.2 Naming Styles and Cultural Variation

Different parts of the world should eventually be able to use different naming styles.

A culture, people, region, or settlement tradition may reference a particular naming dictionary or naming style.

This makes it possible for nearby places associated with one culture to sound related without requiring every name to be handcrafted.

For example, one culture may favor short compound settlement names while another uses longer names drawn from a different dictionary.

World generation should not require cultural simulation before this feature can work. Early generation may use general-purpose world dictionaries, while culture-specific dictionaries can be connected later.

The naming system should therefore support both:

- general world naming dictionaries;
- optional culture/region-specific naming dictionaries or overrides.

## 18.3 Deterministic Generation and Persistent Identity

A generated geographic entity should have a stable persistent identity independent of its display name.

Conceptually:

`persistent world entity ID -> generated/stored name`

The name may be generated deterministically from:

- world seed;
- entity ID;
- entity type;
- naming-style/dictionary ID;

or may be generated once and then stored in world/save data.

Either approach is acceptable as long as names remain stable.

Loading a different chunk, rebuilding terrain meshes, or revisiting an area must not cause a river, mountain, or region to receive a new name.

The preferred rule is:

> **Generation creates the place. Naming labels the persistent place.**

## 18.4 Duplicate Handling

Generated place names should normally avoid unwanted duplicates within the same world.

A world-level name registry or equivalent deterministic duplicate-avoidance mechanism may be used.

Duplicate policy may vary by entity type.

For example:

- two important towns should normally not accidentally receive the same generated name;
- two major rivers should normally not receive the same name unless intentionally authored;
- minor features may tolerate more repetition if appropriate.

Do not build an excessively complicated global linguistic system merely to guarantee theoretical uniqueness.

## 18.5 Player-Authored or Explicit Names

The architecture should not prevent later systems from assigning explicit names.

A generated settlement, player settlement, discovered landmark, or authored World Site may eventually be renamed or given a fixed authored name.

The persistent world entity should therefore not depend on its display name as its technical identifier.

Changing a display name must not break references to that entity.

## 18.6 NPC Personal Names Are a Separate System

Individual NPC personal-name generation does **not** belong to macro world generation.

NPCs should eventually have their own naming system appropriate to the character/NPC/population architecture.

This supplement does not require world generation to generate personal NPC names.

However, the two systems may later share compatible cultural naming resources.

For example:

`Culture A -> Place-name dictionary A`

`Culture A -> NPC personal-name dictionary A`

Those remain separate dictionaries and separate generation responsibilities even if they share a cultural style.

This distinction prevents the world generator from becoming responsible for every future character while still allowing places and people from the same culture to sound related.

## 18.7 Settlement, Tribe, Culture, and Faction Names

Persistent collective entities such as:

- tribes;
- cultures;
- factions;
- kingdoms;
- clans;
- settlements;

may eventually receive generated names through the same broader naming framework, but only when the system responsible for creating those entities exists.

Their names do not need to be generated during the current drainage/world-generation milestone.

The current requirement is simply that the naming architecture can later be reused by those systems.

## 18.8 Current Scope Boundary

Do **not** interrupt the current drainage/world-generation milestone to build the complete naming UI or every naming dictionary.

When naming becomes necessary for generated world entities, implement the smallest clean data-driven naming foundation required by the current milestone.

Preserve extension points for richer dictionaries, cultural styles, World Site naming, and later collective-entity naming.

Do not implement NPC personal-name generation as part of this world-generation task.

---

# 19. Sandbox and World Configuration Architecture

Thornwake should aim for a high degree of **player/host configurability**, inspired by the philosophy of highly customizable sandbox games such as Project Zomboid.

The goal is not to expose every internal implementation constant.

The goal is:

> **If a rule meaningfully changes how the world or gameplay feels, and exposing it is practical and safe, prefer making it configurable rather than permanently hardcoding it.**

The game should provide good defaults so a new player can simply create a world and play.

Advanced players and multiplayer hosts should be able to customize the experience substantially.

## 19.1 One Authoritative Settings Model

World, simulation, and gameplay rules should use a coherent settings architecture rather than unrelated hardcoded values scattered through systems.

Prefer named configuration values with:

- clear IDs/names;
- safe defaults;
- documented meaning;
- valid ranges;
- sensible clamping/validation;
- versioning or migration support where needed;
- serialization with the world/save when the rule belongs to that world.

Systems should read from the authoritative settings model rather than each inventing separate constants.

This does **not** require every setting to have a player-facing UI immediately.

The architecture should make meaningful settings exposable later without requiring major rewrites.

## 19.2 Settings Categories

Settings should be separated conceptually by what they control.

Possible categories include:

**World generation**
- world size;
- world-shape preset;
- seed;
- land/water balance;
- landmass fragmentation;
- island frequency;
- mountain prevalence;
- landform intensity/variety;
- biome diversity;
- climate contrast;
- cave frequency;
- mineral/ore abundance;
- settlement and World Site frequency later;
- aquatic habitat abundance or related generation options later.

**Weather and atmosphere**
- overall weather frequency;
- storm frequency;
- storm strength;
- fog frequency/intensity;
- precipitation frequency;
- wind strength;
- wave severity;
- weather transition speed;
- lightning frequency;
- strength of gameplay effects caused by weather.

**Water and hydrology**
- local water-flow speed where practical;
- flooding/redistribution speed;
- natural source/inflow multipliers;
- drainage-related gameplay multipliers where appropriate;
- optional simplified/advanced behavior presets if the architecture later supports them.

**Travel and movement**
- road movement modifiers;
- dirt-road modifiers;
- rough-terrain penalties;
- mud/swamp penalties;
- snow penalties;
- swimming speed;
- wind influence on sailing;
- wave influence on boats;
- other traversal difficulty modifiers.

**World life and ecology later**
- wildlife abundance;
- hostile creature abundance;
- aquatic-life abundance;
- rare-creature frequency;
- population density;
- settlement abundance;
- migration/ecology intensity if such systems later exist.

**Gameplay systems later**
- harvesting yields;
- resource abundance;
- crafting times;
- durability;
- repair;
- experience/progression;
- farming;
- animal husbandry;
- loot;
- survival difficulty;
- other systems where a meaningful sandbox knob improves player control.

This list establishes architectural intent. It is not a requirement to implement every setting during the current world-generation milestone.

## 19.3 Defaults, Presets, and Advanced Settings

Thornwake should provide strong default settings.

A player should not need to understand every system before starting a world.

The eventual experience may offer:

- a recommended/default preset;
- a small number of clear high-level presets;
- an Advanced Sandbox Settings interface;
- the ability to save custom presets;
- the ability to load/reuse custom presets;
- the ability to share preset/configuration files where practical.

Possible future presets might include:

- Standard;
- Exploration;
- Harsh Survival;
- Island World;
- Mountain World;
- Custom.

World-shape presets described elsewhere in this supplement are part of this same broader philosophy.

High-level presets should map onto the same underlying settings model rather than use separate code paths.

## 19.4 World-Locked, Runtime, and Presentation Settings

Not all settings can or should change at the same time.

The architecture should distinguish between:

**World-generation settings**
- used when a world is created;
- changing them later may require regeneration and therefore may be locked for an existing world.

**World/gameplay rules**
- stored with the world/save;
- may be changeable later if the relevant system safely supports it.

**Runtime/session rules**
- may be adjusted while playing or between sessions when safe.

**Client presentation settings**
- graphics, audio, accessibility, camera, UI, and other local preferences;
- these do not belong to server-authoritative world simulation.

The UI should eventually communicate when changing a setting requires a new world, restart, regeneration, or no special action.

## 19.5 Multiplayer Authority

In multiplayer, world-generation and gameplay simulation settings should be **host/server authoritative**.

Clients must not independently apply different simulation rules for systems such as:

- weather gameplay effects;
- water behavior;
- movement modifiers;
- creature abundance;
- resource rules;
- crafting/progression rules;
- other shared-world mechanics.

The authoritative configuration should be stored with or associated with the world/save.

Client-only presentation settings remain local to each player.

## 19.6 Configurability Should Not Destroy System Coherence

High configurability does not mean every hidden algorithm coefficient belongs in the sandbox menu.

Avoid exposing values that:

- are purely technical implementation details;
- can easily corrupt saves;
- would make deterministic reconstruction unreliable;
- have no understandable gameplay meaning;
- produce obviously invalid world states;
- exist only for internal numerical stability.

Prefer meaningful player-facing controls or grouped multipliers.

For example, expose:

`Storm wave severity`

rather than fifteen unrelated internal wave-equation constants.

Expose:

`Ore abundance`

rather than every implementation probability unless advanced authoring genuinely benefits from it.

## 19.7 Data-Driven Rules Before UI

During development, prioritize making gameplay-relevant values **data/config driven** before building a giant settings interface.

The sequence should generally be:

`system behavior -> named configurable rule -> validated settings data -> player-facing UI later`

This lets Codex avoid unnecessary hardcoding now without interrupting current work to create a complete settings menu.

## 19.8 Current Scope Boundary

Do **not** interrupt current drainage/world-generation work to implement the complete sandbox-settings interface.

For the present phase:

- avoid unnecessary hardcoded gameplay constants;
- introduce clean configuration points when a current system genuinely needs them;
- preserve sensible defaults;
- keep deterministic world-generation settings reproducible;
- document settings that materially affect generated output.

Full sandbox UI, preset management, and broad gameplay configuration can be implemented at the appropriate later milestone.

---

# 20. Immediate World-Generation Continuation

After this supplement is reviewed and accepted, the next Codex phase should remain focused on the **Master Rework world-generation foundation**.

Do not start the full Post-Master gameplay roadmap.

Do not start the World Site Editor or Biome Maker yet.

The intended near-term progression is approximately:

1. finish and validate current coarse drainage/basin groundwork;
2. establish clean macro drainage metadata and reconstruction;
3. refine river/lake prerequisites without prematurely coupling to local water;
4. continue macro climate foundations under the revised variety rules;
5. add data-driven biome definition and biome-distribution foundations;
6. continue natural cave-generation architecture;
7. add simple world-scale geology/mineral-deposit foundations;
8. integrate and validate these layers;
9. only after the world-generation foundation is stable, proceed to the World Site Editor / Biome Maker phase defined by the Master Rework roadmap.

The exact implementation order may change if source dependencies require it, but Codex should not jump ahead simply because a later feature is described here.

---

# 21. Deferred Work / Non-Goals for This Phase

The following remain deferred unless required as small architectural hooks:

- full item/material gameplay redesign;
- weapon progression;
- armor/clothing/equipment systems;
- detailed tool progression;
- advanced crafting/station progression;
- farming;
- animal husbandry;
- fishing;
- production settlement simulation;
- full autonomous NPC economy;
- NPC personal-name generation and NPC naming UI;
- full population/ecology simulation;
- full aquatic ecology and fish/sea-creature simulation;
- biome-driven migration or settlement simulation;
- full hydrological weather coupling;
- real-time world-scale fluid simulation;
- geological plate simulation;
- erosion simulation;
- World Site Editor implementation;
- Biome Maker implementation.
- full sailing/boat physics and storm-navigation gameplay;
- final ocean/lake water renderer, shoreline foam/spray, and production wave physics;
- complete sandbox-settings UI and preset-management interface;


These systems may influence architecture, but they should not be implemented merely because this supplement mentions future compatibility.

---

# 22. Design Principles Locked by This Supplement

The following short rules should guide interpretation:

> **Believable, not scientifically compulsory.**

> **Biomes should be plausible, not inevitable.**

> **World variety outranks strict climate realism.**

> **Deep atmospheric weather, selective gameplay coupling, no mandatory weather-to-hydrology simulation.**

> **Macro systems describe the world; local systems realize the nearby consequences.**

> **Simple individual models, deep interactions.**

> **External projects are references, not dependencies.**

> **Preserve working systems and migrate incrementally.**

> **Meaningful gameplay rules should be configurable where practical.**

> **A world to live in, not a world to beat.**

---

# 23. Review Gate Before Coding Resumes

Before Codex resumes implementation, verify that this supplement reflects the intended design.

If accepted, Codex should read:

1. `THORNWAKE_MASTER_REWORK_PLAN_v3.md`
2. `THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md`
3. current source/checkpoint documentation

The supplement should be treated as the newer authority only where it explicitly changes or clarifies the Master Rework plan.

No large rewrite should be initiated solely because of this supplement.
