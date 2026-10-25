# Atmosphere ideas backlog

Ideas Tom and Noor raised that are not scheduled yet: kept here so they are not lost. Each becomes a decision (D…) and
a switchable draft when it is picked up. Source notes: `DOCS/Thornwake_Visual Atmosphere and Environmental Details_Ideas.md`
(Tom and Noor's brainstorm, 2026-10-25).

## Living world: small, cheap effects around the player (client-side, no multiplayer sync)
* **Wind you can see** (Tom): now and then a trail or streak through the air along the wind, near the player; a few
  small flying particles. Frequency and strength from the wind speed and gusts; rare in calm air.
* **Biome particles** (Tom, Noor): dust devils and dust wisps in desert and badlands; ash and embers in volcanic land;
  drifting snow in cold biomes; pollen and spores in meadows and forests; leaves carried by gusts; moving mist near
  lakes and swamps; insects and small swarms. Each biome its own profile, driven by wind, weather and time of day.
* Cheap GPU particles and simple shaders, only around the player; intermittent, never filling the screen.

## Colour grading (Tom, Noor)
* A setting with presets (Natural, Cinematic, Fantasy, Dreamy, Rustic, Dark fantasy, Vintage, Vibrant) and custom
  brightness, contrast, saturation, temperature, tint, gamma, exposure; shadows / midtones / highlights; vignette,
  film grain, sharpening; LUT support; before / after view.
* Optional subtle grading by weather, time of day and biome (storms cooler and less saturated, sun warmer) —
  complementing the lighting, not overriding it. (The look presets of D164 are a start.)

## Clouds (in progress)
* **Flat cloud bottoms without detail** (Tom, 2026-10-25, after build 103): next in line — see `docs/HANDOFF.md`.
* Layers: mackerel softer at night, cirrus visible, a middle grey sheet (altostratus) before fronts — Tom's order.
* "Shaving foam" clouds: finer detail noise; Tom's dumps when it happens (D177).

## Rain (Tom, 2026-10-25)
* (done, D217) **Raindrops on the camera lens** (Tom: "a spray, maybe optional, kind of like a screen effect of raindrops on the
  camera lens — not too big"): an optional screen effect in heavy rain, small drops that bead, slide and fade; more
  when facing into the wind, none under shelter. A player setting (off by default). Not in Noor's Weather 2.0 notes;
  after them.
* Weather 2.0 (Tom and Noor, `docs/Thornwake_Weather_2_0_Suggestions.md`): done — rain by strength, slanted by the
  wind, splashes, a test tab (D206–D209), rain light and veil (D210). Next: shelter, wetness over time by material,
  dripping trees, splash kinds by surface, sound layers.

## Direction (Noor)
Visual cohesion and a living world over more rendering complexity: Thornwake's own recognisable look.
