# Game vision (Tom, 2026-10-06)

Tom's own words, condensed. The direction for every later decision; it does not fix any feature yet.

## Order of work
World first. Once the world exists, Tom will know what camera controls and camera view he wants, and then the
gameplay follows. Tom has far more ideas than can be built; the environment comes first, then the decisions.

## The game
* **A true RPG sandbox revival**: players write their own stories. In the spirit of Project Zomboid: a world to live
  in, and what players do in it is their choice. Nothing forces them down one path.
* **Examples of ways to live**: hunting animals, sailing and trading goods overseas, working as a lumberjack, making
  furniture, and many more trades.
* **Quests**: yes, ideally procedurally generated, some simple. A little story, mostly told through the environment.
* **Points of interest**: pre-made scenes ("presets") placed randomly around the world. Players work out, guess or
  interpret what happened there; the game does not explain.
* **References for camera and feel**: Valheim, Conan Exiles, Soulmask.

## The journey is the reward
* **Multiplayer** is the goal: fully playable alone, up to ~12 players together in an enormous land and sea.
* The views, the travel and the struggle are rewards in themselves: a caravan crossing a swamp, wheels stuck in the mud.
* Random events on the road: bandits attack; arriving in a village whose people have their own troubles (a bandit band
  raiding it). Helping them pays: goodwill, cheaper prices.
* Trade quests between villages (one produces what another lacks), feeding a simple economy simulated in the background.
* **Lore**: glimpses and hints of Thornwake's lore exist; ideally most of it is generated (in the spirit of Caves of Qud)
  from what Tom writes.
* **Next after the terrain**: placing premade villages and points of interest.
* The docs were written while ideas were flowing (Noor wrote many of them down); they will be reconciled once the world
  is clearer. Where they disagree, ask rather than assume.

## The world generator is a framework
Thornwake comes first, but the world generator should become a framework for worlds for other kinds of games too:
large-scale battles (an FPS), a hunting game (in the spirit of theHunter), a colony manager on a very large scale.
For the engineering this means: keep `Game/WorldGen` free of Godot and of Thornwake's gameplay (it already builds and
tests without Godot); game-specific content (biomes, asset sets, points of interest) stays data passed in, not code
inside the generator.

## What matters most in the environment
* **Weather and environment effects** are very important: storms, blizzards, rain, hail, snow, thunderstorms, sea
  storms, dust storms.
* **Water and sailing**: sailing will be a big part of the game (not the main part). It does not need to be
  realistic; Rosewind- or Valheim-level sailing is enough. It has to be pleasant, not complex.
* **Water style**: experimental 2 is the current favourite; one style will be chosen in the end.

## Performance (Tom's machine, build 23)
Stable 60 FPS with VSync almost everywhere at maximum settings; ~30 FPS in dense forest at maximum quality; short
stutters when flying very fast in debug mode (recovers when stopping). Moving animals and NPCs are not in yet.

## Consequences for the engineering
* The sea must stay readable and steady for boats: the CPU wave height (SpectrumWaves.Height) must keep matching the
  shader, so boats can float on what the player sees.
* Weather must be visible from far away (storm systems on the horizon, walls of rain at sea).
* World generation needs hooks for randomly placed points of interest (presets) later.
* Multiplayer: everyone must see the same world, weather and sea. The world generator is already deterministic
  (DetMath, seeded noise); weather, wind and waves are already functions of the seed and the game clock, so a host only
  has to share seed + time. Keep it that way: no unseeded randomness in anything that affects play.
