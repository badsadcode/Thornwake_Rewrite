# Playtest feedback log

Newest at the bottom. Each round: what Tom reported, then how it was handled (filled in as fixes land).

## Round 1 — rewrite preview build (2026-10-04), Balanced continents, seed 75793

**Keep:** overall look and scale; huge lakes; forests; distant mountains you can travel to; mountain shape far
away with more slope detail up close; views from ridges and peaks. Performance good (~60 FPS, 214 tiles).

**To fix / improve**
1. Distant trees stop rendering, leaving empty-looking patches: needs far tree LOD (impostors or similar).
2. Rivers too straight from above: smoother curves, more shape variation.
3. River width too uniform: widening/narrowing, meanders where it makes sense.
4. River mouths at lakes/sea: wider mouths, sometimes deltas, instead of a narrow channel ending.
5. River junctions: should merge naturally, not look like two separate lines meeting.
6. Strange straight lines on the water surface (screenshot 5): find the cause.
7. Ground looks flat up close: grass blades, small stones, rocks, boulders, rock formations.
8. Close-up terrain texture needs more detail when walking.
9. Screenshot 10 ("river strait width"): a stream in a narrow trench/canyon-like cut looked odd.
10. Later: more tree and bush variety; eventually giant fantasy trees (sequoia-size).
11. Not yet present (known): water physics, digging/terraforming in the new world.

More screenshots pending from Tom; plan the fixes after reviewing them.

### Round 1, part 2 (5 more screenshots)
* Screenshot 10 explained: a 7 m stream at the bottom of a straight-walled trench with a flat floor, grey
  rock walls both sides. Cause: river surface comes from the coarse raster (750 m cells, minus freeboard) while
  the detailed terrain around it (bicubic + walking-scale detail) stands much higher; the floodplain blend
  (capped at 140 m) then cuts a steep trench. Planned fix: derive each river's surface from the *detailed*
  terrain sampled along its final course (monotone lower envelope), and carve a slope-limited valley profile
  whose width follows the height difference (no hard cap), so cuts become natural valleys.
* Ridge/peak/mountain-slope views and forested mountainsides: look as intended (keep).
* Visible again: forest edges ending abruptly where tree LOD stops (item 1).

### Round 1, part 3 (4 more screenshots)
* Riverside forest with willows: looks good. Straight lines on the river surface are clearly visible (item 6).
* Coast: blotchy sand/grass mix, a flat sand plateau with a jagged edge above a wide brown shallow bed (item 12, new):
  beach should be a band along the sea, not every low flat area.
* River junction from above: two straight channels meeting at an angle (items 2, 5).
* Wide river: lines on the water again.

### Plan for round 1 fixes (in this order)
1. Rivers: surface from detailed terrain along the course + slope-limited valley sides (fixes trenches); stronger
   meanders where flat; width variation; estuary mouths and simple deltas; blended junctions.
2. Water surface lines: reproduce and fix.
3. Distant forests: far tree LOD.
4. Ground detail: grass blades, stones, rocks, boulders, outcrops; close-up terrain texture.
5. Coast: beach band by distance to the sea.
Then a new Windows build.

### Round 1 — status of fixes
| # | Item | Status |
|---|---|---|
| 1 | Distant trees vanish | Far tree cards for 0.4–6 km (levels 3–5), terrain forest tint beyond |
| 2 | Rivers too straight from above | Fixed root cause (meander amplitude vs wavelength made zigzags); natural meanders + km-scale wander on flat ground |
| 3 | Uniform river width | Width varies along reaches (0.6–1.3×), pools/narrows |
| 4 | River mouths | Estuaries (3× widening over the last 250–1,200 m); simple 2-branch deltas for large rivers on flat coasts |
| 5 | Junctions | Tributaries funnel into the receiving channel; all nearby rivers combine continuously |
| 6 | Lines on water | Caused by MSAA with the screen/depth-reading water shader; new world uses FXAA instead |
| 7 | Flat-looking ground | Grass tufts (valley mesh), stones, rocks, boulders, outcrops on steep ground |
| 8 | Close-up texture | Pixel-style close-up detail in the terrain shader (fades out by 70 m) |
| 9 | River in a trench | River surface from detailed terrain + slope-limited valley sides; verified at Tom's location |
| 10 | More tree/bush variety, giant trees | Waiting for Tom's assets; spec in `docs/ASSET_SPEC.md` |
| 12 | Blotchy sand at the coast | Beach is now a band along the sea, softer edge |

## Round 2 (build 2, 2026-10-04)
Tom's notes (one screenshot at Mountain kingdoms 42, -59499 / 115353, and one at Vast world 52913, -603613 / 507460):
* Grass should be denser.
* Deltas and river ends: rivers stop short of the sea; delta branches are straight lines.
* River courses still too straight; widths still look the same; edges look jagged from a distance.
* Frame rate still good; tree cards better.
* Forests a little sparse; forest density should be a player setting. The world generation and the game will be
  highly customisable: many parameters for players.
* Generation time does not matter much (one-off at world creation).
* Post-processing wishes: sun shafts, clouds, cloud shadows, nice effects, screen-space effects, reflections, all
  tweakable, with view distances per object type.
* Looking upstream, river water disappears a little further away and appears when closer; downstream fine.
* Water on slopes could show the direction it flows.

### Round 2 — status
| Item | Status |
|---|---|
| Straight rivers | Root causes: the D8 grid path was only lightly smoothed (kinks every cell) and meander wavelengths could collapse (zigzags). Now: grid path resampled and Gaussian-smoothed; sine meanders with natural bend radii; valley bends (few hundred m) and km-scale wander |
| Same widths | Pools, narrows and occasional broad stretches (0.4–3.5×), wider on flat ground; never below the minimum width |
| Rivers stop before the sea | Sea mouths now continue ~900 m straight out past the detailed coastline; lake outlets start inside the lake |
| Straight delta branches | Delta branches are meandered like rivers |
| Jagged edges at distance | Coarse tiles use a channel edge that is linear in distance, so the interpolated waterline is smooth; narrow channels stay at least one vertex spacing wide far away; the bed tint follows the real waterline |
| Denser grass | 2× by default; slider 0–3× and distance setting |
| Sparse forests | Forest cover 1.15× and density 1.2× by default; both sliders; tree cards seen from above become crowns (no thin lines) |
| Customisation | Settings tab (detail, view distances, effects, sky, camera/performance) and New world tab (world shape, terrain, rivers and lakes) |
| Post-processing | Clouds, cloud shadows matching the clouds, sun shafts, glow, SSAO, SSIL, SSR, volumetric fog, haze, exposure, saturation, water reflection quality, AA mode, render scale (FSR), FOV, V-Sync, FPS limit |
| Upstream water vanishing | The water faded where the bed behind it (seen up a rising river) was higher than the surface point: depth now never below the sight line's drop through the water |
| Flow direction | Ripples move downstream and streak along the current; foam streaks on fast water |

## Round 3 (build 5, 2026-10-05)
* Smoother rivers liked, but they look too clean (a Bézier curve with a fixed width). Keep the course smooth; vary
  the banks: broad width changes, smaller shoreline wobble, left and right independent; occasional bulges, cuts,
  shallow shelves, muddy/sandy patches, reed pockets, irregular edges. Nothing extreme.
* Digging next to a river (and a lake): water does not flow in.
* Sea shores: the shallow part is very large at this scale; waves look stretched. Love the scale.
* Performance fine (office PC; playable with fewer trees).
* Trees must not be a graphics option: generated trees stay (unless gameplay changes them); same for logs, rocks
  and other objects. They are not decoration.
* Keep the current generation as a backup to go back to.

### Round 3 — status (build 6)
| Item | Status |
|---|---|
| Too-clean rivers | Each bank varies on its own (slow ±30 % width, wobble, bulges with shallow shelves, cuts); course unchanged; mud/sand/gravel patches; reed pockets (build 5) |
| Digging by rivers/lakes | Generated water now feeds lower dug ground (tested on a real river and lake) |
| Sea shores | Beach profile: sea a few metres deep within ~150 m, 20 m within ~1 km; narrower beach band |
| Trees not an option | Forest/rock/prop amounts are New-world parameters; Settings keep draw distances |
| Backup | Build 5 = commit `cdb2e31`; New world → "Build 5 terrain" reproduces it bit for bit (tested) |

## Round 4 (build 7, 2026-10-05)
* Digging near a river (in the bed) leaves gaps in the water. → D34
* Hard step where rivers enter lakes/oceans; should join the bed smoothly; shader should show the mixing. → D32
* Waves washing back and forth on the beach. → D33 (swash, breakers)
* Ocean waves: important — boats like Valheim / Sea of Thieves; calm, storm and other variants. → D33
* Many biomes planned (terrain, plants, atmosphere per biome). → planned
* Keep this state to come back to. → `docs/SNAPSHOTS.md` (Preview 7 = `6cf6d18`), Terrain version option, tests

## Rain and world creation (after build 7, 2026-10-05)
* Rain water vanishes instantly when rain stops; ponds should stay, but rain must not flood the world. → D37
* Cosmetic (non-simulated) puddles too. → D37 (`rain_wetness`)
* Wet soil looks squarey. → soft noise-thresholded wet edges (D37)
* World creation like Azgaar's (inspiration, no code): world type → parameters (Auto) → size 32–4096 km → seed →
  2D preview → generate; size independent of geography; counts follow coverage. → D35, docs/WORLD_CREATION.md
* Heightmap templates as the general direction of world types, and a way to make new types and tweak them deeper.
  → D36: landform templates, five template types, saved custom types as JSON
* Custom biomes later, documented, with an editor. → planned (WORLD_CREATION.md "Later: biomes")

### Status (build 8)
| Item | Status |
|---|---|
| Rain water persists | Soil saturates, then water stands; slow drain + evaporation; puddle 2.3 of 3 m³ left after 10 min (tested) |
| Cosmetic puddles | Ground darkens and glosses in rain; puddles in flat spots; dry over minutes |
| Squarey wet soil | Blurred, noise-edged wetness |
| Creation screen | Done: type, seed, size, Auto parameters, preview map, plan reused on Generate |
| Templates / own types | Done: step list (hills, raise, lower, basin, range, trough, strait, islands, mask); 5 template types; Save as world type |
| Land share | Presets 13–19 % land, kept for identical worlds; question Q5 |
