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
