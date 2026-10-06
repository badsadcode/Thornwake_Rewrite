# Rivers: Noor's direction and the plan (round 14)

Noor's guiding sentence: *rivers should feel like moving bodies of water, not narrow oceans. Their visual identity
comes from directional flow; surface detail, foam, wakes, turbulence and obstacle interaction inherit the local current.
At a mouth the current spreads into the larger body, weakens, and blends with its waves — messy where strong flow meets
incoming waves.* Behaviour should follow quantities (current, depth, distance to banks, wind, exposure, obstacles,
weather, water-body scale), not labels.

## Where Thornwake is now
* Every water vertex already carries a **current** (from the river surface's slope) and rivers are drawn from it:
  ripples carried downstream in two cross-fading phases and stretched along the flow, glassy slow pools, rough fast
  reaches, whitewater in fast shallow water (patches drifting downstream since build 19), foam streaks along the
  current.
* Rivers already shape the terrain: channels, banks with variation, estuaries at mouths, river water colouring the sea
  or lake near a mouth (plume, colour only).
* Lakes respond to fetch (size), seas to exposure, waves to depth — the "quantities, not labels" idea is partly there.
* Missing: the current is uniform across the channel (no slow banks / fast middle, no bend asymmetry); no standing
  waves; wide rivers get no wind waves; the current stops at the mouth (no plume of moving water, no wave–current
  meeting zone); no wakes from rocks, logs, the player or boats.

## Assessment
Agree with all of it. Two notes:
* "Relative velocity = object velocity − water velocity" is the right single rule for every wake (rock, standing
  player, swimmer, boat, log), and it also works on lakes and the sea (water velocity ≈ 0 there).
* No fluid simulation for these effects — the local shallow-water simulation stays for dug/rain water only. Everything
  below is data baked per water vertex (cheap) plus a small per-frame list of moving disturbances.

## Plan (in order of value for effort)
1. **Better current field (data, TileBuilder).** Speed across the channel from depth (Manning: v ∝ depth^⅔ —
   slower and more irregular at the banks, fastest over the deep line), narrows faster (continuity: v ∝ 1 / (width ·
   depth)), outside of bends deeper and faster (carve the outer bank a little deeper). The shader gets it for free.
2. **River surface layers (shader).** Three flow-advected detail layers at different speeds (no single scrolling
   texture); **standing waves** in fast shallow reaches (stationary crests that do not move downstream — the key
   visual cue of rapids); bank turbulence (small irregular ripples, occasional foam) from distance to the bank; wind
   waves on wide rivers scaled by width (fetch) and shortened/steepened against the current.
3. **Mouths.** The current continues into the lake/sea as a fanning, weakening plume (current written on lake/sea
   vertices near mouths); the shader blends river behaviour into open-water waves by current strength; where waves run
   against the outgoing current they steepen, break up and foam (messy meeting zone; dramatic in rough seas, gentle in a
   calm lake).
4. **Dynamic wakes.** A ring buffer of recent disturbances (player, later creatures, boats, floating debris): position,
   relative velocity, time. Points drift with the local current; the shader draws a V-wake and ripples for each,
   strength from relative speed — a swimmer upstream makes a long wake, a boat drifting with the current almost none.
5. **Obstacle wakes.** Rocks, roots and logs standing in rivers baked per tile into the water vertices (nearest
   obstacle upstream, offset, size): a pressure bulge upstream, faster sides, a downstream V-wake with alternating
   eddies, foam when the current is strong.

Suggested start: 1 + 2 (biggest visible change, cheap), then 4 (makes the water react to the player), 3, 5.
