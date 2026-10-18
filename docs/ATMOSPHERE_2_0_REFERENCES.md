# Atmosphere 2.0 — reference images (Tom, 2026-10-12)

Nine images: six Valheim screenshots (forest in low sun, green forest at sunrise, plains at dusk, storm at sea, night
forest, mountain in snow) and three painted references (a six-weather sheet, cumulus over a lake land at midday,
cumulus at golden hour). The images are not stored in the repository (third-party screenshots); the colours below
were measured from them and are what the curve tables in `AtmosphereLight.cs` are tuned against.

## What they say, measured

| Reference | Region | Colour | Hue / sat / light | Taken as |
|---|---|---|---|---|
| Valheim forest, low sun | shade under the trees | `#1e4e61` | 197° / .52 / .25 | shade is **teal**, saturated, not grey-blue |
| | sunlit crowns | `#804e39` | 18° / .38 / .36 | sun is orange, strong warm/cool split |
| | haze toward the sun | `#e2b491` | 26° / .59 / .73 | the air glows **warm toward the sun** |
| Valheim forest, sunrise | foreground in shade | `#203523` | 129° / .25 / .17 | dark but readable, green bounce |
| | trunk in shade | `#1d373a` | 185° / .33 / .17 | teal again |
| | distance | `#9a9c73` | 63° / .17 / .53 | warm bright haze behind the trees, rays through the canopy |
| Valheim plains, dusk | sky | `#b0a6d9` | 252° / .40 / .75 | dusk is **lavender**… |
| | far water and haze | `#b292b5` | 294° / .19 / .64 | …to pink-violet away from the sun |
| | lit grass | `#aa7a40` | 33° / .45 / .46 | golden ground |
| Valheim storm | sky | `#417087` | 200° / .35 / .39 | storm is teal-blue, not grey |
| Valheim night | forest / mist | `#010d10` / `#042a3d` | 193–200° / .83–.86 | night is **deep saturated teal**; silhouettes, a moon halo |
| Valheim mountain | sky haze / rock shade | `#7dabca` / `#4b5f64` | 204° / 191° | cold blue-white, teal shade |
| Painted, midday | zenith / horizon | `#3771b0` / `#b9c9da` | 211° | deep blue zenith, pale hazy horizon |
| | cumulus top / base | `#d2d1d1` / `#aeb6c8` | — / 221° | bright tops, **blue-grey flat bases** |
| | far forest | `#58717e` | 201° / .18 | strong aerial perspective, teal-blue |
| Painted, golden hour | sky by the sun | `#eeba7e` | 32° / .77 | saturated gold around the sun |

## By milestone

*These are examples of moods, not one palette (Tom: the colours depend on the biome, the time of day and the weather).
They are the starting point of the light profiles in `Content/light_profiles.json` (D83): the temperate forest profile
carries the values below; plains take the lavender dusk and golden ground, taiga the cold blue-white, desert a warm
dusty haze, swamp an olive-green haze.*

* **M1 light (now, light model 1).** Shade fill teal-cyan by day (hue ~195), lavender at dusk (~250), deep teal at
  night (~195, a bigger share of the ambient); haze lavender/blue-teal away from the sun and the sun's colour scattered
  toward it (fog sun scatter 0.42 at a low sun, 0.2 at noon, was 0.15 for all). Night haze deep teal instead of
  near-black blue. Storm stays near neutral for now (teal storm grading comes with the overcast tuning).
* **M1 bloom (pause point 3).** Valheim's glow is large, soft and *warm-coloured* around the sun, and a halo around
  the moon; not the grey screen-wide blob of rounds 23–24. Plan: glow only from the sun, moon and lit haze around
  them, tinted by the light.
* **M2 rays.** The sunrise forest: soft beams through the canopy into bright haze, the background lifted. Both
  approaches (native volumetric fog in forests vs screen-space) are compared on this scene type.
  *Tom, 2026-10-18 (D142):* the target quality is **Unity HDRP's volumetric lighting** (volumetric fog lit by the sun
  through its shadow map: crepuscular rays / god rays with every trunk and branch casting its shadow into the air).
  Tom's HDRP screenshots go here when he sends them (`docs/references/hdrp-*.png`).
* **M3 clouds.** The painted references: cauliflower cumulus with bright tops and flat, blue-grey bases; cirrus
  streaks above; a cloud bank on the horizon; low cloud clinging to mountains and lying in valleys; warm-lit edges and
  dark-grey cores at golden hour; towering storm cells with rain shafts.
  *Tracked (Tom, dumps 115949 / 120402, cloud quality volumetric v2 in Needle Rocks):* the clouds drawn in front of
  far land did not match the same clouds in the sky — white (fixed in 1cafa04: the biome haze now applies to both),
  and still a flat, featureless patch with hard vertical edges under the towers. The two halves of the v2 renderer
  (half-resolution sky pass, full-resolution front pass) disagree along near-horizontal views; M3's single cloud pass
  removes the seam rather than patching it.
* **M4 haze and distance.** Strong aerial perspective (far forest teal-blue, mountains fading to the horizon colour),
  valley mist, warm haze toward the sun.
