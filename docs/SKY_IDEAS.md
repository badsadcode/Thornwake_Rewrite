# Sky ideas (round 15, for after the rivers)

What the sky has now: day/night with a real sun path, sky gradient with sunrise/sunset warmth, natural or classic sun,
layered clouds (cirrus, main deck, low fast clouds, each with its own wind) or experimental volumetric clouds, cloud
shadows on the ground, light beams through cloud gaps (sun and moon), silver linings, weather fronts with rain, snow
and lightning, biome sky moods, and a night sky with stars, moon phases and planets.

## Ideas, roughly by how much they would change the feel
1. **Real twilight.** Proper scattering colours instead of a gradient: deep blue overhead at noon, golden hour, the
   red-orange band at the horizon at sunset, then the pink **Belt of Venus** opposite the sun with the dark blue
   Earth's shadow rising under it, and the blue hour. Clouds **lit from below** in orange and pink after the sun has
   set. A darker, deeper blue high in the mountains.
2. **Optics after rain and in thin cloud.** **Rainbows** opposite the sun after showers (with the faint second bow and
   the darker band between them), **sun halos** and **sun dogs** in high cirrus, a ring around the moon, a
   **fogbow** in mist.
3. **Clouds that belong to the weather and the land.** Cumulus towers building up on warm afternoons; far
   **thunderstorms** on the horizon with anvil tops, **rain shafts** hanging under them and lightning flickering
   inside at night; a mackerel sky before a front; flat grey decks in steady rain; **lenticular "UFO" clouds**
   standing still over mountain ridges in strong wind; fog banks rolling off the sea.
4. **The night sky.** The **Milky Way** turning with the stars, **auroras** in the cold north (the world has
   latitudes), shooting stars (now and then a bright fireball), moonlight silvering cloud edges.
5. **Clouds that live.** Clouds that grow, merge and dissolve over minutes instead of only drifting; storms that build
   during the day and clear at evening.
6. **Biome skies, physically.** Heat shimmer and pale dusty sky over deserts, humid haze over swamps, a clean dark
   sky over high mountains, ash and red glow near volcanoes (with the volcano landforms).
7. **Rare events.** A comet visible for a few nights, an eclipse, planets lining up.

## Cloud experiments ("for fun and science")
* Volumetric clouds driven by a weather map (coverage, type and height per region), so different cloud types sit in
  different places and move with the fronts.
* Cumulus shapes from a few erosion layers (billowy tops, flat bases at the condensation level).
* Light inside the cloud: soft light through thin edges, darkness inside thick clouds, the bright rim toward the sun.

Suggested start: 1 (twilight) and 2 (rainbows, halos), which are cheap and spectacular, then 3 (cloud types and distant
storms), then 4 (the Milky Way and auroras).

**Status (build 22):** 1 (twilight), 2 (rainbows, halos, sun dogs), 3 (cloud types: afternoon towers, distant
thunderstorms with anvils, rain shafts and lightning, lenticulars over peaks, mackerel sky before fronts, textured rain
deck) and the night part of 4 (auroras, shooting stars) are in; the water reflects the clouds and the far cloud types.
Round 19 (build 25): approaching rain fronts with a rain wall visible from far away, mountain cloud, overcast mood. Not yet from 3: fog banks rolling off the sea; clouds wrapping slopes in front of the player (needs fog volumes). Next: the Milky Way, then the cloud experiments (the far towers are a
first step toward them: shaded puffs, not yet a volume).

## Ideas from other Godot cloud projects (2026-10-08, review only; no code taken)
Tom asked for a look at three projects (all MIT, used as **ideas only**): Bonkahe/SunshineClouds2,
clayjohn/godot-volumetric-cloud-demo-v2 and MMqd/godot-nishita-sky-with-volumetric-clouds. They are general
cloud renderers with one global coverage (or a painted mask). Thornwake's clouds are already driven by a simulated
world (weather map, fronts, storm cells, mountain clouds, lenticulars, curtains, lightning, matching shadows and
reflections). What they do better is **how** the clouds are drawn:
1. **Clouds as a compositor effect with depth** (SunshineClouds2): a compute pass after the scene, not the sky
   shader, so clouds can sit **in front of** mountains, wrap around summits, and you can fly into them. Ours are
   drawn behind everything (the sky), which is why the cloud base is kept ≥ 0.8 km above the camera. Biggest visual
   gain for a mountain world. Low resolution plus a depth-aware upscale (furthest depth per block) keeps it cheap.
2. **Amortised far clouds** (clayjohn v2): the whole sky dome rendered into a texture a 1/64 part per frame and
   blended between two copies, about 20× cheaper. It suits far clouds that change slowly; near clouds stay per frame.
   The same texture could feed the water's sky reflection and the radiance map.
3. **Physical sky from small lookup tables** (clayjohn v2, Hillaire 2020 transmittance and sky-view LUTs): correct
   sunset colours, a darker sky from high summits, and the sun's colour for the light, at almost no per-pixel cost.
   Ours is hand-tuned gradients plus twilight effects.
4. **Better step noise and stepping**: blue-noise / interleaved-gradient dither instead of white noise (less grain
   at the same step count, so fewer steps), coarse steps until cloud is found, then fine (Schneider 2015).
5. Small ones: curl-noise distortion for wispy edges; reading cloud density back at points (we can do this on the
   CPU from the weather field, e.g. for the sun's warmth in survival); a moon lit by the sun with eclipses (Nishita).
