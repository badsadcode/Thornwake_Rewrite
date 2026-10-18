# Gameplay design (Tom, 2026-10-13)

The gameplay direction, agreed with Tom in conversation (planning only, no code yet). It replaces the step order of
`GAMEPLAY_PLAN.md` (its roadmap after the prototype — roads, settlement sites, premade POIs — still stands) and sits on
top of `GAME_VISION.md`. The Design Bible (`Persistent_Fantasy_Life_RPG_Game_Design_Bible_v0.1.md`) stays a source of
ideas; where it disagrees with this document, this document wins. Open questions are listed at the end.

## 1. Principles

* **Survival like Project Zomboid**: hunger, cold, sickness and injuries are real and can kill you.
* **Tools and opportunities, not goals.** Tom: "I don't want to give players goals that I think they should do. I want
  to give them tools and opportunities. As many game mechanics as possible that will allow them to live in the world of
  Thornwake as they want." Lumberjack, merchant, fisherman, blacksmith, hunter, builder, mercenary, adventurer,
  carpenter, leatherworker, farmer, bandit, mage — all valid lives. Nothing pushes the player down one path.
* **The player sets the game up**, as in Project Zomboid's sandbox settings: every rule that shapes difficulty or pace
  is a world setting, with presets.
* **Expandable.** Tom: "it's hard to think of everything that would be nice for the game" — new ideas must be cheap to
  add. Hence the building blocks below: most new things are data, not code.

## 2. Building blocks, not professions

Professions are not systems. They emerge from a few generic blocks, built once in code; everything else is data
(JSON in `Content/`, as biomes and light profiles already are), and later moddable.

| Block | What it is |
|---|---|
| **Items with tags and properties** | An axe is `cutting`, `chopping`; a log `wood`, `fuel`; raw fish `food`, `raw`, `spoils`. Rules ask for tags, not items. Items carry properties (materials, damage types, alchemical properties). |
| **Actions and recipes** | One pipeline for everything made or done: inputs + tool tags + station + skill + time (+ conditions such as weather, light) → outputs. Chopping, sawing, smelting, tanning, cooking, brewing. |
| **Stations** | Campfire, workbench, forge, anvil, tanning rack, loom, alchemy table: where certain recipes work. |
| **Skills** | Grow by doing. Better results, speed and fewer failures; never a hard lock. |
| **Body and needs** | Hunger, thirst, warmth (from the real weather, wind and wetness the game already simulates), fatigue, injuries, illness, encumbrance. |
| **Effects** | One system for everything that acts on a body or the world: heal, warm, chill, poison, bleed, speed, night vision, burning… Food, potions, spells, poisons, traps and weather all produce effects. |
| **World changes** | Felled trees, mined rock, crops, placed objects, buildings: edits keyed by world position, like dug ground already is. Saved, shared in multiplayer, optionally fading with time (regrowth). |
| **Creatures** | Animals first, people (NPCs, companions, bandits) later. |
| **Trade and reputation** | Item value, prices per settlement, factions and reputation, bounties. |
| **Written knowledge** | Books, journals, recipe books, grimoires, maps: knowledge as objects in the world (section 5). |

**Ways to live, from the blocks**

| Life | Uses | New to add |
|---|---|---|
| Lumberjack | trees, axe, felling, forestry skill | mostly data |
| Carpenter / shipwright | workbench, wood recipes, boats | data (+ boats, §10) |
| Blacksmith | ore, forge, anvil, recipes | data + ore in the world |
| Fisherman | rod, water kinds, fish by weather and hour | one new action (casting) |
| Hunter / leatherworker | creatures, skinning, tanning rack | creatures |
| Farmer | crops as world changes, seasons | growth over time |
| Alchemist | ingredients with properties, alchemy table | §7 |
| Mage | runes, spell parts, mana | §8 |
| Merchant | trade, price differences, boats and caravans | economy, settlements |
| Mercenary / adventurer | contracts, combat, points of interest | combat, quests |
| Bandit | reputation, bounties, robbing | faction rules |

No classes. A character is what they spend their time doing.

## 3. Sandbox settings

Every rule below is a world setting, grouped in presets (e.g. *Relaxed*, *Survivor*, *Hardcore*) plus *Custom*, chosen
when the world is created (the host's settings apply to everyone in multiplayer). The game already does this for world
generation, day length and rain erosion. First list, to grow:

* hunger, thirst and fatigue rates; how deadly cold, wetness and illness are; injury depth (§9)
* resource abundance; regrowth speed of trees, grass and bushes (off … fast)
* food spoilage speed; tool and gear wear; quality lost per repair
* skill gain speed; automatic skill carry-over on death; share of skills a journal can store; reading speed
* animal numbers, bandit activity, weather severity
* fantasy level (how strong, common and costly magic is)
* extra hints for players who want them (§5)
* PvP on/off (multiplayer)

## 4. Characters, death and knowledge

* **Traits, no occupations.** Players pick traits at creation; nobody picks a job. Positive traits cost points, negative
  ones give points back (Project Zomboid style), so a character is a set of trade-offs. Traits open possibilities and
  give head starts (*Arcana knowledge* for a would-be mage, *Strong*, *Short-sighted*, *Illiterate*…); skill is still
  earned by doing. Traits are data.
* **Death**: the world goes on; the player makes a new character in the same world.
  * Everything carried **drops where the character died** (a corpse run: if you dare, and if bandits or animals have
    not got there first). The stash at home stays where it is.
  * Skills: the new character inherits only what the player wrote down — **journals** (§5). A sandbox setting can add
    an automatic carry-over (0–100 %, default 0 %).

## 5. Written knowledge

One kind of item, *written knowledge*, with several uses:

* **Journals** (Tom's idea): a player may write a journal — or not. Writing is an action (paper, ink, time, light,
  ideally somewhere quiet) that records a snapshot of the character's skills, up to a share set in the sandbox (e.g.
  50 %). Writing again updates it. A new character who reads it learns back up to the stored share (reading takes time
  and light). Journals are real objects: safe at home while the house stands, lost with the body if carried, can be
  stolen, burnt, given, sold or **copied** (a scribe is a trade). Old journals found in ruins and points of interest
  carry skill and **lore** — a stranger's story in their own words.
* **Books as the tutorial** (Tom): no traditional tutorial. Each trade has books written by people of Thornwake, in
  their own voice — *"Notes on Felling, by Old Berta of the Marsh Woods"*: "Never fell into the wind, lad. The wind
  decides where a tree lands, not you." (true in game: the wind is simulated). Each book has a short practical page or
  margin note with the plain facts (tool, key) so nobody is stuck; a sandbox setting adds extra hints. A new character
  starts with one or two basic books; others are found, bought, traded. Reading gives a small skill gain or unlocks
  recipes; rarer and master books teach more. Every new mechanic comes with its book (a text file Tom can write).
* **Recipe books** (alchemy recipes a player discovered — knowledge to sell), **grimoires** (spells a player built),
  possibly **hand-drawn maps** of what a player explored.

## 5a. Felling a tree (agreed with Tom, 2026-10-13)

Felling should feel like an achievement and real work — a few satisfying steps, not deep, not tedious. Three acts:

1. **Felling — you choose where it lands.** The first hits cut a **notch** on the side you stand; it grows visibly
   with every hit (progress on the tree itself, no bar). The tree falls **toward the notch** — the wind still has a
   say (Old Berta: "the wind decides where a tree lands") — so you notch the side you want it to fall, then finish from
   behind. Near the end it **creaks and leans**, leaves drift down: step aside. A tree coming down on you hurts.
2. **The fall — short and heavy.** It tips over its base, faster and faster (scripted, not rigid-body physics: a
   whole tree with its crown tumbling stiffly looks wrong). On impact the crown **breaks up** — a burst of leaves and
   twigs, dust, a thud, a small camera shake nearby — and leaves a heap of branches; the tree becomes a **bare trunk**
   lying there (its own bark, no branches).
3. **Processing — the work that pays.** The bare trunk is a **physical log**: it rolls down slopes and can be pushed
   with effort (real physics only from here: a log, nothing stiff to hide). A few axe cuts split it into logs, each cut
   visible on the trunk. Logs are heavy (how many one carries, rolling or dragging them home: later, with building).

**Tree size sets the effort and the yield.** Hits to fell grow with the tree; the trunk's logs follow its real volume
(length × girth: a young birch gives a couple, an old oak many) and the crown's branches its size. A better axe and the
felling skill make it faster, never impossible (§2: skills are never a hard lock). Values in code and content are
placeholders to tune.

**Assets (Tom, later)**: Tom will make handmade tree assets; a guide to what each tree needs for this (the standing
model and its LODs, the bare trunk log, the stump, cut faces) and what can be generated comes when he starts.

## 5b. Fishing (Tom, 2026-10-14: Enshrouded style for the final game)

Fishing must be engaging. Enshrouded's model as the base:
* **Bait is required** and decides what can bite (simple bait crafted by hand, better ones at a station, wild bait found).
* **Cast** (hold to throw farther), **wait** for a bite, **strike** when the bobber goes under.
* **Reel in — a tug-of-war**: the fish runs left or right; pull the other way (A/D) and switch the moment it stops. The
  fish's stamina must run out before the line's does; the rod's **strength** tires the fish faster, its **endurance**
  keeps the line longer (better rods are crafted or found).

**The final version (Tom, 2026-10-14, after trying step 1's)**: keep step 1's tug-of-war for now; the final one plays
more like Enshrouded's: the bite shows in the water — **splashes and the bobber wobbling** before it goes under;
then you **tire the fish by turning the rod against its runs** (it goes left, you turn right), **reel the line in**
(the fish comes closer: a real distance to win back) and **watch the tension** — reeling while it runs hard, or
holding against it too long, raises it until **the line snaps**; easing off lets it run (line paid out again). Tiring
it, the line's length and its tension are the three things on screen.

Thornwake on top, from what the world already simulates: **which fish** by the water (river, lake, sea, swamp pool,
pond dug by players), the biome and water temperature, the **hour** (dawn and dusk bites) and the **weather**; a river's
current makes a fish pull harder downstream; a fishing book teaches it (§5). Step 1 builds a small version of exactly
this minigame (cast, strike, A/D, one bait, two or three fish), so it grows into the final one rather than being
replaced. The rod needs plant fibre: E-gathering (grass, berries, loose stones; Tom) comes first.

## 5c. Ideas for later (Tom)

* **Physical stones** (2026-10-18): loose stones that can be kicked and roll down slopes. Options noted: solid only
  (they block, never move); physical when moved (a dropped or thrown stone rolls and bounces — few at a time, cheap);
  every loose stone near a player physical (most alive, costliest: synced like felled trunks). For now picked-up stones
  simply disappear (D113).

## 6. Survival, the world's memory, crafting quality

* **Needs** as in §2; survival feeds everything else: hungry, cold, wet or exhausted means less stamina and slower
  recovery (also in combat and climbing).
* **Regrowth**: felled trees, cut grass, picked bushes grow back slowly (sandbox speed; off possible). A world change
  carries a time stamp and fades.
* **Spoilage**: food rots; cooking, drying, salting, cold (winter, cellars) slow it.
* **Wear and repair**: tools, weapons, armour and boats lose durability; repair restores it but lowers the maximum a
  little each time (sandbox), so an old axe is eventually replaced.
* **Quality from the crafter**: every crafted item has a quality from the maker's skill, the material, the tools and a
  little luck. Quality scales its stats (damage, armour, speed, durability). A master's work is worth more: a reason to
  trade with skilled players.

## 7. Alchemy

Ingredients have **properties** (warming, healing, poison, calming, volatile, lasting…). **Preparation** changes them
(boil, grind, distil, ferment, dry). The result follows from the properties combined: effects with strength and
duration; skill and equipment improve purity. Properties are **discovered** (tasting — risky —, experiments, skill).
Players write recipes into recipe books, which can be sold. New ingredients are data and combine with all existing ones.

## 8. Magic

* **Open to everyone**, learned by doing — even a lumberjack. *Arcana knowledge* is a head start, not a gate.
* **Spells are built by the player** from parts: **source** (fire, frost, water, earth, air, life, force…), **shape**
  (touch, bolt, area, ward, aura, rune carved into an object), **modifiers** (stronger, longer, delayed, triggered —
  traps —, split). Cost and risk grow with complexity against skill; overreach can **backfire**.
* **Learning**: runes, spell parts and techniques come from books and places found in the world (ruins, POIs).
* **Cost: mana**, a pool that grows with skill and refills with rest (perhaps faster at places of power — old stones,
  ley lines, a type of POI); **reagents** for powerful or complex spells, tying magic to gathering, alchemy and trade.
* **Magic acts on the world's simulations**: earth reshapes the ground (terrain edits), water adds to or drains the
  local water simulation, frost freezes water, air pushes the wind (and a sail), fire ignites wood.
* The sandbox *fantasy level* scales it all.

## 9. Combat (Valheim, Conan Exiles, Soulmask)

* **Controls (Tom, 2026-10-13)**: what you hold decides what the left mouse does — a weapon or an axe swung at an enemy
  attacks it; the same axe swung at a tree in front of you chops it (with its animation). **E gathers and interacts**:
  grass for plant fibre, berries, loose stones on the ground, putting wood on a fire. (Step 1 has the axe and the fire;
  attacking and gathering come with enemies and gatherables.)

* Third-person, physical, **stamina-driven**: light and heavy attacks, short combos, block, a **timed parry** that
  staggers, a **dodge roll**. Each weapon type has its own moveset (sword, axe, mace, spear, bow, shield, two-handers).
  Bows with real arrow flight and drop.
* **Damage types** (slash, pierce, blunt; fire, frost, poison from magic and alchemy) against resistances — properties
  and effects again.
* Crafter quality and wear apply; skills grow by use (an axe in a fight also trains the lumberjack's axe); survival
  state affects stamina; weather, snow, mud and darkness matter (a torch in one hand).

## 10. Climbing (Conan Exiles, Soulmask) — important to Tom

Climb almost any rock face, cliff or steep slope, at a stamina cost; out of stamina, you fall. The world becomes
vertical: a cliff is a route, an escape, a way into places others cannot reach. The terrain already has cliffs, rock
walls, rocky shores, spires and mesas. Rain makes rock slippery; cold, wet hands and wind on a ridge make it harder;
skill lowers the cost; gear (climbing picks, rope, grappling hook) helps; heavy armour and a full pack hinder.
(Technical: climbing terrain and rock walls is straightforward; player buildings must be designed for it.)

## 11. Sailing (Valheim style)

Simple and satisfying: sail angle against the wind decides speed; half or full sail; rowing in calm or head wind; a
rudder. Boats grow from a raft to a small sailboat to a cargo ship. The **regional wind field** drives the sail (real
wind changing with fronts and storms), the boat rides the **same waves the water shader draws** (`GAME_VISION.md`
requirement), **storms at sea are dangerous**, **river currents** carry small boats. Shipbuilding is carpentry (a
**shipwright**); boats wear and break on rocks and in storms. **Cargo** carries the merchant's trade overseas.

## 12. The living world: settlements, economy, caravans, bandits, events

Tom: "caravans, wandering merchants, bandits, random road and world events — villages can be raided by bandits or
animals whether the player is there or not; players can help but don't have to. The world should feel alive."
The detailed design already exists in `docs/design-inbox/THORNWAKE_LIVING_WORLD_SIMULATION_SCOPE.md` (sections 4–25)
and the Design Bible (sections 33–39); it fits these principles and is adopted as the basis. In short:

* **Village economy as a few numbers** (food, materials, metal, tools, medicine, wealth; specialties and needs as
  tendencies), refilled slowly by abstract production, evaluated lazily (once a day, or when someone comes near). They
  drive merchant stock, prices, requests and contracts — no per-worker simulation, no GDP.
* **Caravan contracts**: a surplus in one village and a shortage in another, with a route between them, create a
  delivery contract. Accepted, the goods become real cargo (crates, sacks, barrels) on the employer's or the player's
  own wagon (more profit), driven by the player or an NPC driver. The journey: food and water, route choice, repairs;
  events such as ambushes, wolves, a fallen tree, a broken wheel, a blocked bridge, a flooded crossing, a wounded
  traveller — or nothing. No hard timers except for perishable or urgent goods. Delivery changes the village visibly
  (fuller stalls, better repairs, a stocked healer, houses repaired). **Stealing the cargo is possible** and has
  consequences: lower trust, worse pay, deposits, guards, bounties, rumours. The same by sea with cargo ships (§11).
* **Carts and draft animals are separate** (Tom): the cart is a vehicle with hitch points; the horse or ox is a
  creature with its own health, needs, behaviour and **owner**. Hitched, it pulls; unhitched, it can be led on a rope,
  ridden (horses), sold — or **stolen** (reputation and bounties). The hard part is wagon physics on rough ground (slopes,
  mud, rivers — the caravan stuck in the swamp). Stepwise: a hand cart the player pulls → animals that pull carts →
  riding.
* **Wandering merchants, pilgrims, refugees, hunters and travellers** on the roads: light road life near players, not
  tied to the economy.
* **Bandits are groups in the world**, not spawn waves: they form, grow, make camps, rob travellers, raid farms and
  villages, demand tribute, and collapse. Players may ignore, fight, negotiate with or join them (bandit is a valid
  life, with reputation and bounties).
* **Events happen whether the player is there or not**: raids by bandits or animals, fire, crop failure, flood,
  drought, disease, a blocked road, festivals, migration. Near players they play out live and players can help — or
  not. Far away they are resolved coarsely in the background, and players arriving find the result: burnt houses, a
  struggling village, refugees, a new camp nearby. Events grow from the world's state (a hungry village, a bandit camp
  nearby, the real weather) more than from dice.
* **Villages can decline and die** (healthy → struggling → critical → abandoned) and slowly **repopulate**; a dead
  village may later hold bandits, refugees, wildlife or nobody. Players are never obliged to save or maintain one.
* **Players learn by noticing**: tavern rumours, notice boards, empty shelves and high prices, damaged wagons, smoke
  on the horizon, travellers' talk.
* **"Generate lots, remember little"**: small events exist only near players; important outcomes (a village destroyed
  or saved, a leader killed) enter the world's memory. NPCs are simulated in full only near players (simulation levels).
* Sandbox settings: event frequency, bandit activity, how fragile villages are, repopulation speed.

## 13. Engineering consequences (for when code starts)

* Content as data (`Content/*.json`: items, tags, recipes, stations, skills, traits, effects, ingredients, runes,
  books' texts); code only for the blocks. Moddable later (the Bible's modding tiers).
* Everything that changes the world is an edit keyed by world position, saved and shared; deterministic where it
  affects play (the world, weather and waves already are), so a multiplayer host shares seed, time, settings and edits.
* Sandbox settings live with the world (saved with it), like the world-generation parameters.
* Gameplay code stays separate from rendering and from `Game/WorldGen` (which must stay a framework free of
  Thornwake's gameplay — `GAME_VISION.md`).

## 14. Order of work (Tom, 2026-10-13)

Atmosphere 2.0 continues alongside (rendering; it blocks none of these steps and they do not block it).

| Step | What | Why |
|---|---|---|
| **0. Multiplayer spike** | Two players in the same world: seed, time and weather in sync, positions, one shared world change (a dug hole) | Fixes the shape of the code (who owns what, how a change reaches other players) before mechanics pile up; building single-player mechanics first and adding multiplayer later would mean rewriting them. Small: a few builds. |
| **1. Proof-of-concept mechanics** — placeholders, not final | Needs (hunger, warmth), items and inventory, the action pipeline, chopping, campfire, fishing, cooking, a placeholder animal to hunt, a raft or small boat; the first books | Proves the building blocks and the feel; each multiplayer-ready because of step 0. The first slice below. |
| **2. Real multiplayer test** | 4–12 players with these mechanics: connection, performance, synchronisation | Tom's checkpoint: does it meet expectations? |
| **3. Settlements and roads** — placeholders | Village, town and castle sites, then roads between them (`GAMEPLAY_PLAN.md`) | Roads need the sites first. |
| **4. Living world** | Village economy, caravans (hand carts, then animals), bandits, events (§12) | Needs villages and roads. |

### 14.0 Step 0 in detail — the multiplayer spike (agreed with Tom, 2026-10-13)

* **Hosting**: one player hosts, others join (as in Valheim). Inside, the code keeps the *server part* (the true state:
  time, world changes, who is where; it checks what clients send) separate from the *client part* (drawing, input,
  sound); the host runs both. Later a **dedicated server** is the same game started headless, with a **dashboard**
  (Tom's wish: a small web page for sandbox settings, players, saves, bans, restarts).
* **Connecting**: Godot's built-in high-level networking (ENet, UDP). Not only LAN: Tom forwards one UDP port (e.g.
  7777) on his home router and friends anywhere join with his IP and port (Windows Firewall asks once). First check
  that the router's WAN address equals the public IP a "what is my IP" site shows (otherwise the provider uses CGNAT
  and port forwarding cannot work; then a tunnel such as playit.gg, Tailscale or ZeroTier for testing, and Steam
  networking later — which also gives invites and joining without router setup).
* **Where**: Tom's PC session (two game windows side by side, then a friend over the internet), on a short-lived branch
  `multiplayer-spike`, started after its current Atmosphere 2.0 task and merged into `main` as soon as the first
  version works (both touch `WorldLab.cs`).
* **What is synchronised**
  * on joining: the world (seed and world parameters), the host's sandbox and world-affecting settings (time, weather,
    wind, sea state, cloud cover and speed…) — graphics settings stay each player's own — and the current time;
  * time: the host's clock; clients correct gently toward it;
  * players: positions and facing ~20 times a second, interpolated so others move smoothly;
  * world changes (digging first): a client asks, the host applies and broadcasts; a player joining later receives
    the list of changes made before;
  * weather, wind and waves are computed from the seed and the clock, so they should match by themselves — verify it.
    Anything *accumulated* frame by frame rather than computed from the clock must come from the host: at least the
    clouds' drift (dumps already carry it: `--cloud-drift`), possibly eased storm and mood values.
* **Not shared in the spike**: the local water simulation (puddles, poured water, springs) runs only around each player
  and is not deterministic; each player keeps their own for now. Shared simulated water is a later problem.
* **The player is separated from the world**: `WorldLab.cs` runs the world and one player together; the spike splits
  out a player that can exist several times (local and remote), the largest part of the work.
* **Done means**: (1) two windows on Tom's PC see each other move; (2) a hole dug in one appears in the other, and for a
  player who joins afterwards; (3) the same time of day, weather and clouds in both; (4) the same with a friend over the
  internet, with ping and bandwidth measured and noted.

### 14.1 Players far apart in a huge world (Tom's question, 2026-10-13)

* **Each player's own PC draws their own surroundings** (terrain, trees, grass, water, clouds). The host never draws a
  distant player's area.
* **The host needs only the skeleton around each player**: the ground's shape and nearby trees and rocks for collision,
  and the animals, NPCs and events there. The world comes from the seed, so the host computes that shape without
  graphics (`Game/WorldGen` already runs without Godot) — far cheaper than drawing it.
* **Cost grows with the number of separate places, not the size of the world**: players together share one active
  area; twelve players in twelve places are twelve active areas (physics, animals, events) on the host. This is what
  step 2's test measures.
* **If that is too heavy — the proven fix (Valheim's)**: whoever is in an area simulates its animals and physics and
  sends the results; the host keeps the final say over what lasts (world changes, inventories, deaths). The work then
  spreads over all players' PCs. Start with the host doing everything; switch only if the test shows the need. Keep the
  spike's code open to it (simulation of an area should not assume it runs on the host).
* **Places with nobody in them do not run**: when a player arrives, the place catches up at once ("seven days passed:
  harvest in, one raid, the village struggling") — lazy evaluation, as in §12.
* **Shared weather and time**: one weather system for everyone, seen from different places (a storm on one player's
  mountain, sun in another's swamp). Time is shared: skipping the night needs a rule — Valheim's "everyone in bed" by
  default, as a sandbox setting.

## 15. First playable slice (proposal)

Not one mechanic, but the smallest loop that proves the blocks: **needs** (hunger, warmth), **items and inventory**,
**the action pipeline** and **world changes** — chop a tree → logs → build a campfire → catch and cook a fish → get
through a cold, rainy night; plus the first **book** (felling or fire-making) as the tutorial. If that feels right,
most of what follows is content. Order after it to be decided with Tom (climbing, combat, sailing are the big ones).

## 16. Open questions

* **Injury depth**: a health bar only (Valheim) or wounds, bleeding, fractures, infection, bandages and splints (PZ)?
  Proposal: health plus an injuries layer, depth as a sandbox setting.
* **Followers**: hired companions or mercenaries fighting beside the player (Conan's thralls, Soulmask's tribesmen)?
* **Combat camera**: over the shoulder with a target lock? Combat in first person too?
* How much of the settlement economy comes before real villages exist (the settlement sites of `GAMEPLAY_PLAN.md`).
