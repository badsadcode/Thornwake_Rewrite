# THORNWAKE — Living World, Simulation Scope & Emergent Story Design

**Status:** Design philosophy / scope guardrail  
**Date:** 2026-10-01  
**Implementation authority:** No. This captures current design intent for later review. It does not override the active Master Rework or Supplement V1.

## 1. Core principle

Thornwake should feel like a **world to live in, not a world to beat**.

That does **not** mean simulating reality at maximum detail.

The goal is:

> **Deep emergent stories, meaningful consequences, and a believable world without microscopic simulation.**

Thornwake is a sandbox. The world provides rules, places, professions, threats, opportunities and consequences. Players decide what matters and create their own adventure.

A good philosophical reference is Project Zomboid: there is no required grand campaign, the world gives you systems and situations, and the player creates the story.

## 2. Deep algorithm does not mean deep simulation

Sophisticated algorithms are welcome where the output matters directly:

- terrain generation
- mountain morphology
- drainage
- lakes and rivers
- caves
- world streaming
- biome layout
- routes
- attractive water presentation

That does not imply every generated concept becomes a continuously simulated gameplay variable.

Thornwake does not need realism for its own sake, such as:

- moisture stored in every soil voxel
- per-voxel fertility chemistry
- continuous nutrient simulation
- physical thermal conductivity of individual cooking pots
- global per-NPC economic simulation
- every distant wolf, merchant, villager or caravan physically existing at all times

**Rule:** Spend complexity on what players can see, experience, or create stories from. Fake the rest aggressively.

## 3. Gameplay has veto power over realism

Climate, geology, moisture, altitude, slope and biome data should usually act as **weighted influences**, not absolute laws.

A pine may strongly prefer cool, wet terrain without being forbidden everywhere else. A swamp herb may also appear beside rivers or in wet forest pockets. Copper may follow geological preferences while the generator still ensures reasonable access for gameplay.

Unusual pockets, overlaps and transitions are desirable.

**Rule:** World rules create plausibility. Gameplay gets veto power.

## 4. Generate lots, remember little

Not everything that happens needs persistence.

### Ephemeral events

These exist only because players are near enough for them to matter.

Examples:

- random goblin pack
- wasp swarm
- roadside ambush
- wandering monster
- wolves near a carcass
- injured traveler
- small roadside fight
- temporary monster nest

Lifecycle:

```text
player enters activity region
↓
valid event is generated
↓
player may discover it or completely miss it
↓
all players leave
↓
event disappears
```

No history entry, offscreen continuation or explanation is required.

### Persistent local situations

Some situations can remain relevant longer:

- bandit camp threatening a village
- blocked mine
- dangerous bridge
- recurring wolf problem
- missing caravan
- local shortage
- persistent monster lair

These can keep lightweight state without full continuous simulation.

### Historical events

Only important events should enter long-term history:

- settlement destroyed
- settlement saved
- major monster killed
- important faction leader defeated
- settlement founded
- town abandoned
- town resettled
- major bridge constructed or destroyed

**Rule:** Generate lots. Remember little. Remember what matters.

## 5. Actors can be ephemeral; places can remember

A reusable bandit/goblin camp can be a preset World Site asset.

Possible flow:

1. Players enter the area.
2. A camp is placed and blended into the terrain.
3. Bandits/goblins materialize, patrol and fight.
4. Players may find the camp or miss it.
5. Players leave.
6. Expensive NPC actors disappear.
7. The physical camp can remain as an abandoned site.

Possible site states:

```text
Occupied
ClearedByPlayers
Abandoned
Reused
Decayed
```

Returning players may find an empty camp they never saw while inhabited.

Not every event leaves a footprint. Site/event policy can be:

```text
NoFootprint
TemporaryFootprint
PersistentSite
```

A wasp swarm leaves nothing. A roadside battle may leave temporary debris. A proper camp may remain.

## 6. Procedural narrative is not simulated truth

Thornwake can have a huge authored library of:

- notes
- letters
- orders
- diaries
- manifests
- rumors
- wanted posters
- jokes
- warnings
- journals
- treasure clues
- work logs
- names
- faction names
- settlement references
- local landmarks

This is a **procedural story grammar**, not a requirement to simulate every sentence.

A note can say:

> We lost three men near the old tower. Something lives underneath it.

That does not require three real bandits to have died or a monster to actually exist.

Internally, narrative information may be:

```text
Canonical
Contextual Fiction
Rumor
```

Canonical means it reflects real world state. Contextual fiction can borrow real names and places without being true. Rumor may be true, distorted, outdated or completely false.

**Rule:** Generated narrative may borrow reality without being required to represent reality.

## 7. The generated world is narrative vocabulary

Procedural text should be able to use real generated information such as:

- NPC names
- settlement names
- roads
- rivers
- mountains
- ruins
- caves
- factions
- local landmarks
- professions
- seasons
- goods
- creature types
- major historical events

Example:

```text
Boss,

The last wagon from {ORIGIN_SETTLEMENT} carried mostly {CARGO}.
Not worth the trouble.

{NAME} says the next one should leave for {DESTINATION_SETTLEMENT}.
We'll wait near {LOCAL_LANDMARK}.

- {BANDIT_NAME}
```

The story does not need a fully simulated supply chain to feel rooted in the world.

## 8. World history and knowledge

Thornwake should distinguish:

> **What actually happened**

from:

> **Who knows or believes something happened**

Important events can enter a lightweight World History/Event Ledger.

Example:

```text
Event: BanditCampDestroyed
Location: Redwater Valley
Actors: player group / Red Knives
Outcome: leader killed, prisoners freed
Importance: Regional
```

Knowledge may spread abstractly through settlements, inns, merchants, caravans, guards, travelers, bards or factions.

There is no need to simulate every conversation.

NPC knowledge can combine:

```text
SettlementKnowledge
+
PersonalMemories
+
RoleKnowledge
```

## 9. Settlement economy is descriptive state

Thornwake should not simulate an economy at Civilization/strategy-game depth.

Settlements may store cheap abstract information:

```text
Greyford

Food: 72
Materials: 55
Metal: 18
Tools: 24
Medicine: 31
Wealth: 46

Specialties:
- grain
- hides
- timber

Needs:
- metal
- tools
```

Exact categories can change later.

The point is that settlement economy supports:

- merchant stock
- price tendencies
- generated quests
- caravan contracts
- settlement condition
- scarcity and abundance
- local identity
- player-driven consequences

It does not require per-worker production, wages, individual daily consumption, continuous global supply chains or simulated caravans everywhere.

## 10. Needs and specialties are tendencies

A farming village may usually have more food, grain and hides.

A mining town may usually have more ore and stone.

A settlement needing medicine may stock less, pay more, and generate medicine-related requests more often.

These are **weights**, not hard restrictions.

## 11. Lightweight settlement production

Production is abstract.

A farming village may slowly regenerate Food. A mining town may regenerate Metal/Raw Materials. A logging settlement may regenerate Timber/Materials.

Updates can happen once per in-game day or lazily when the settlement becomes relevant.

The settlement stores the **result**, not the invisible process.

## 12. Player-driven caravan economy

Settlement state can generate physical transport contracts.

Example:

```text
Stonebridge:
Metal surplus

Greyford:
Metal shortage

Valid route exists
```

The game may create a delivery contract.

Only when players accept does the abstract economy become physical:

```text
settlement state
↓
contract generated
↓
player accepts
↓
goods become physical cargo
↓
wagon/cart journey
↓
delivery / theft / destruction / abandonment
↓
settlement state updated
```

Cargo can be represented by crates, sacks and barrels rather than hundreds of individual economic units.

## 13. Caravan gameplay

A caravan mission may involve:

- employer-provided cart/wagon
- player-owned wagon for greater profit
- player driving
- simple NPC driver
- follow / stop / go-here commands
- food and water preparation
- hunting/scavenging during travel
- route choice
- repairs
- danger management

Possible journey events:

- bandit ambush
- goblin attack
- wolves
- monster
- fallen tree
- broken wheel
- blocked bridge
- flooded crossing
- wounded traveler
- peaceful journey

Not every journey needs an event.

Hard timers should not be universal. Ordinary contracts can have no deadline or soft delivery bonuses. Perishables or genuinely urgent situations can justify stronger time pressure.

## 14. Random road life is separate

Random caravans, traveling merchants, pilgrims, guards, refugees, hunters and travelers seen on roads do not need to be tied to the settlement economy.

They may simply be ambient events generated near active players.

Every visible wagon does not need economic paperwork.

## 15. Why deliveries matter to settlements

Delivery effects should become visible gameplay consequences.

### Tools
- better tool availability
- better repair/crafting services
- improved prices
- fewer tool-shortage requests

### Food
- fuller market stalls
- lower food scarcity
- fewer desperation requests
- possible feast/festival/market opportunities

### Medicine
- better healer/apothecary stock
- lower scarcity
- improved resilience during crises

### Building materials
- damaged structures can recover
- settlement visuals improve
- services can reopen

The exact system can remain lightweight, but abstract values should lead to concrete player-visible results.

## 16. Settlements may decline and die

Settlements should be allowed to suffer real consequences.

Possible lifecycle:

```text
Healthy
↓
Struggling
↓
Critical
↓
Abandoned / Destroyed
↓
Repopulating
↓
Healthy
```

Players may contribute through theft, violence, helping threats, destroying infrastructure, or repeated interference with vital supply.

However, settlements should not be absurdly fragile. One missed grain cart should not kill a town. Severe outcomes should require meaningful, persistent bad conditions.

## 17. Players may steal caravan cargo

Quest cargo should be real enough to steal.

Possible flow:

```text
player accepts delivery
↓
cargo leaves origin settlement
↓
player steals/sells/uses cargo
↓
destination receives nothing
↓
contract fails
↓
trust/reputation/crime consequences
```

Possible consequences:

- lower trust
- worse pay
- contract refusal
- deposits required
- guards assigned
- bounty
- rumor spread

Sandbox behavior should create consequences rather than invisible restrictions.

## 18. Settlement death should not create chores

If a village dies, players are not automatically responsible for restoring it.

An abandoned settlement may later contain:

- bandits
- goblins
- refugees
- wildlife
- travelers
- nobody

The place itself preserves history.

Players may choose to help repopulate it, but this is optional.

## 19. Slow background repopulation

Dead settlements may have a cheap, low-frequency chance to recover.

Possible factors:

- farmland quality
- water access
- road/trade access
- nearby resources
- intact buildings
- safety
- nearby threats
- nearby active settlements
- time abandoned
- player-cleared threats

Example:

```text
State: Abandoned
Population: 0
Attractiveness: High
Safety: Medium
RoadAccess: Good

background check succeeds
↓
Population: Small
State: Repopulating
```

No settlers need to be physically simulated walking there while nobody is nearby.

When players return, the settlement materializes according to the new state.

Players may accelerate recovery by clearing threats, repairing access, delivering supplies, or helping NPCs move there.

Automatic recovery should remain possible so players do not become responsible for maintaining the entire world.

## 20. Specialists without colony management

A settlement may care about a few useful roles:

- blacksmith
- healer
- innkeeper
- merchant
- farmer
- carpenter

A player may occasionally help a useful NPC move into a recovering village.

A blacksmith moving in can reopen the forge. A healer can reopen an apothecary.

Players should not need to assign houses, schedules, wages, food rations or work shifts.

**Goal:** Rebuild a place, not manage a colony.

## 21. Population can be abstract

A settlement with population 37 does not require 37 background AI agents.

Far from players, population can be an abstract value or band.

Near players, the game materializes an appropriate visible population.

Only important/named NPCs require richer persistence.

## 22. Multiplayer performance principle

Target scale:

> **1–16 players**

Conceptual existence should be cheap.

Nothing becomes expensive merely because it exists somewhere in the world.

Useful model:

```text
World State
important persistent facts

Regional / Virtual State
lightweight abstract information

Player Activity Areas
actual AI, combat, physics, encounters and detailed simulation
```

Players together share active regions. Players spread across the world create several active regions.

Important future budgets include:

- active NPC count
- pathfinding requests
- physics objects
- encounter density
- terrain streaming work
- network relevance
- AI update rates
- persistence writes

## 23. Distant simulation should be slow or lazy

Prefer:

```text
Last evaluated: Day 32
Current day: Day 39

when relevant:
evaluate seven days of coarse change
```

instead of updating every distant settlement every frame.

This applies to production, population, repopulation, abstract trade and regional threats.

Store outcomes, not continuous invisible activity.

## 24. Economy should create adventures

The economy is not a strategy game.

Its purpose is to produce:

- reasons to travel
- merchant variety
- scarcity
- opportunities
- caravan journeys
- consequences
- settlement identity
- player professions
- emergent stories

A player may decide to keep Greyford supplied with metal because Greyford is useful to them.

The game does not need a GDP model.

## 25. Consequences without maintenance chores

Players may help, ignore, exploit, rob, save, destroy or rebuild settlements.

They should not be forced to maintain every place they have ever touched.

Settlements need enough autonomous/background behavior to continue without constant player attention.

> **Consequences without maintenance chores.**

## 26. Scope test for future systems

Before adding simulation, ask:

1. **What player-visible experience does this create?**
2. **At what resolution does the player actually need it?**
3. **When does it run?** World creation, first discovery, region activation, coarse periodic update, active gameplay, every frame?
4. **Can generation or abstraction create the same experience more cheaply?**
5. **Does the game need to remember it?**
6. **Does realism make the game more fun?**

If the answer to the last question is no, bend realism.

## 27. Working definition of a living Thornwake world

A living world does not mean everything is continuously simulated.

For Thornwake:

> **The world can create plausible situations, react to important events, remember meaningful consequences, change over time, and materialize enough local detail that players feel they are living inside it.**

That is enough.

## 28. Short design rules

1. Deep stories, not maximum simulation depth.
2. Generate lots; remember little.
3. Actors may be ephemeral; places can remember.
4. Not every story has to be true.
5. Context is more valuable than microscopic realism.
6. Biome/climate guide content; they do not imprison it.
7. Gameplay may override realism.
8. Settlement economy is abstract state, not Civilization.
9. Physical economy appears mainly when players engage with it.
10. Caravan gameplay should create adventures, not accounting.
11. Settlements can genuinely decline, die and recover.
12. Repopulation can happen slowly without player intervention.
13. Players can influence the world without becoming its managers.
14. Distant things should be cheap. Nearby things can become detailed.
15. Nothing gets expensive merely because it exists.
16. Consequences without maintenance chores.
17. Start with the smallest state that can create the experience.
18. Only add another variable when we know what fun it enables.
19. Weather can strongly change presentation without continuously recalculating hydrology.

## 29. Weather and hydrology guardrail

Ordinary weather may strongly affect presentation without physically simulating a full rainfall/runoff/water-level system.

Rain, storms, fog and other weather may change:

- lighting and sky
- fog and atmospheric visibility
- wind
- rain/snow particles
- ambient sound
- terrain/material wetness
- water ripples, roughness, foam and other surface presentation

Ordinary rain should **not** continuously recalculate river, lake or sea levels. Thornwake does not need to simulate rainfall volume, watershed runoff, soil absorption, evaporation and downstream discharge merely to make rainy weather believable.

Meaningful water-level changes may still exist when they create useful gameplay or world consequences, but they should normally come from explicit coarse states or events, for example:

- flood
- drought
- seasonal water state
- dam or obstruction changes
- special/magical world events

These may switch or interpolate a water body toward a defined state rather than requiring continuous physical hydrology.

**Playable-world validation rule:** visual weather should be present in representative form when testing the real 3D world, movement and camera. Full physical hydrological response to weather is not part of that milestone.

**Rule:** Simulate the interesting state change, not all of the physics that could theoretically produce it.

## 30. Current implementation scope

This document is meant to guide later discussions about:

- Supplement V2
- virtual populations
- settlements
- NPC persistence
- economy
- generated quests
- world history
- rumors
- caravans
- professions
- farming
- crafting
- multiplayer
- long-term save performance

It should **not** expand the current Supplement V1 world-generation milestone.

The active Master-Rework implementation order remains unchanged.
