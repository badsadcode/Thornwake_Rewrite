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
