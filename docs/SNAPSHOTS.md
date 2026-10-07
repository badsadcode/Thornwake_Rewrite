# Snapshots to go back to

Every snapshot is a commit on `rewrite/main` (history is never rewritten, so these stay valid forever).

| Snapshot | Commit | Windows build | How to get it back |
|---|---|---|---|
| **Build 31** (round 24, water focus: underwater light shafts and caustics; experimental 3 without far shimmer, smooth wind changes, windy lakes) | `63c8738` | Thornwake-Rewrite-Preview-31 (v0.9.31) | Underwater light: off = build 30 underwater |
| **Build 30** (round 23: natural look — richer colour, real shade, no spilling glow; water experimental 3 — GPU FFT ocean, choppy crests, fold foam) | `ecb25b8` | Thornwake-Rewrite-Preview-30 (v0.9.30) | Look: classic = build 29 look; Water: experimental 2 = build 29 sea |
| **Build 29** (round 22 follow-ups: underwater plants no longer vanish, fuller tufts, sprite wetlands) | `f24d2f0` | Thornwake-Rewrite-Preview-29 (v0.9.29) | |
| **Build 28** (round 22: plants as populations — patches and communities, reeds, lilies, underwater plants; Noor's sprites) | `40e0752` | Thornwake-Rewrite-Preview-28 (v0.9.28) | Grass and plants: classic tufts = build 27 grass |
| **Build 27** (round 21: rolling ground mist — banks, lumpy tops, billows; mist height after teleports) | `8c310e9` | Thornwake-Rewrite-Preview-27 (v0.9.27) | |
| **Build 26** (round 20, fixes from Tom's dumps: river foam arcs, gentler fog, wetland channels — terrain revision 14, estuary seams) | `ba41cce` | Thornwake-Rewrite-Preview-26 (v0.9.26) | Build 25 terrain: snapshot `cd98f74` |
| **Build 25** (round 19: rain fronts visible from afar with a rain wall, mountain cloud, overcast mood, cloud contrast and colour) | `cd98f74` | Thornwake-Rewrite-Preview-25 (v0.9.25) | |
| **Build 24** (fixes: black fog in shadow, sea seams from the coastal heading turn; F9 debug dump) | `9a0c314` | Thornwake-Rewrite-Preview-24 (v0.9.24) | |
| **Build 23** (round 18: volumetric clouds rebuilt — 3D noise shapes, light through the cloud, sky-coloured shadows, sunset colours, crisp in calm / smudged in wind) | `c021e6e` | Thornwake-Rewrite-Preview-23 (v0.9.23) | Cloud quality: layered = build 22 look |
| **Build 22** (round 17: cloud types from the weather and the land — afternoon cumulus towers, distant thunderstorms with anvils, rain shafts and lightning, lenticulars over peaks, mackerel sky, textured rain deck) | `66c3d89` | Thornwake-Rewrite-Preview-22 (v0.9.22) | |
| **Build 21** (round 15–16: rivers as moving water — current profile, standing waves, wakes, mouth plumes, rock wakes; water reflects real clouds; twilight, rainbows, halos, auroras) | `27d24d0` | Thornwake-Rewrite-Preview-21 (v0.9.21) | |
| **Build 20** (round 14: terrain revision 13 — no perched tributaries; rain films no longer redraw rivers) | `d9491a0` | Thornwake-Rewrite-Preview-20 (v0.9.20) | Revision 12 terrain = terrain-build19.json fixture |
| **Build 19** (round 13: dug water joins rivers softly with tint; water reflects the night sky; no aliasing rings; patchy rapids) | `fd8ede8` | Thornwake-Rewrite-Preview-19 (v0.9.19) | |
| **Build 18** (round 12: experimental 2 as a system — weather-scaled foam, broader swell, coastal turn; Noor's ground textures switchable; ragged rock edges) | `5f1282e` | Thornwake-Rewrite-Preview-18 (v0.9.18) | Settings → Ground textures: current / Noor |
| **Build 17** (round 11: experimental 2 sea rebuilt — smooth calm→huge storms, regional wave directions; rock collision hulls) | `614db3a` | Thornwake-Rewrite-Preview-17 (v0.9.17) | Experimental 2 of build 16 = `defef2c` |
| **Build 16** (water experimental 2: spectrum ocean, layered foam; classic default) | `defef2c` | Thornwake-Rewrite-Preview-16 (v0.9.16) | Settings → Water: classic / experimental / experimental 2 |
| **Build 15** (water experiment, switchable; classic default) | `56c4d7b` | Thornwake-Rewrite-Preview-15 (v0.9.15) | Settings → Water: classic / experimental |
| **Water baseline** (before the water experiment: the "classic" water) | `3511e4b` | build 14 | Game/Rewrite/Water.gdshader, OceanWaves.cs, TileBuilder.BuildWater as of this commit. The experiment adds a separate shader and a setting; classic stays the default |
| **Build 14** (round 9: dunes, mesas, needle rocks) | `68c103a` | Thornwake-Rewrite-Preview-14 (v0.9.14) | Terrain revision 12; build 13 terrain = revision 11 with fixtures/biomes-build13.json |
| **Build 13** (round 8: real swamp water, Marsh/Fen/Bayou, light beams, silver lining) | `b53e852` | Thornwake-Rewrite-Preview-13 (v0.9.13) | Terrain revision 11; build 12 terrain = revision 10 (fixture terrain-build12.json) |
| **Build 12** (round 7: time, weather, wind, night sky, rock looks, map, collision, lake fix) | `aab8f0c` | Thornwake-Rewrite-Preview-12 (v0.9.12) | Terrain version "Build 9" for the old terrain; weather off, sun classic for the build 11 look |
| **Backup before round 7** (Tom: "I like what we have now, we should save that") — same as build 11 | `0963d58` | Thornwake-Rewrite-Preview-11 | `git checkout 0963d58`; realistic biome placement stays the default in later builds |
| **Build 11** (placeholder plants, leaf translucency, layered/volumetric clouds, biome skies, mist, DoF, cliff dressing) | `0963d58` | Thornwake-Rewrite-Preview-11 (v0.9.11) | settings: Cloud quality "one layer", Biome skies off, Ground fog off, Leaf translucency 0 give the build 10 look |
| **Build 10** (biomes phase 1: 14 data-driven biomes, climate) | `562cf6a` | Thornwake-Rewrite-Preview-10 (v0.9.10) | also: Terrain version "Build 9" turns biomes off |
| **Build 9** (before biomes; Tom: "so far fantastic", 2026-10-05) | `f868ef5` | Thornwake-Rewrite-Preview-9 (v0.9.9) | also: New world → Terrain version "Build 9" in any later build (biomes off, terrain bit-exact) |
| **Biomes phase 1** (14 data-driven biomes, Noor's architecture, before biomes shape terrain) | `5a35e9e` | — | `git checkout 5a35e9e` |
| Backup before biome phase 1 (build 9 + biome v0 notes + plan) | `bdb55b3` | — | `git checkout bdb55b3` |
| **Biomes v0** (first climate + 12-biome pass, before Noor's biome spec) | `4e45acf` | — (not built) | `git checkout 4e45acf`; idea in `docs/BIOMES_V0.md`; its 12-biome table was replaced by the data files in phase 1 |
| **Preview 7** (Tom: "really happy with this state", 2026-10-05) | `6cf6d18` | Thornwake-Rewrite-Preview-7 (v0.9.6) | see below |
| Build 5 (before natural banks / steeper shores) | `cdb2e31` | Thornwake-Rewrite-Preview-5 (v0.9.4) | also: New world → "Build 5 terrain" in any later build |

**Going back to a snapshot**
* Code: `git checkout 6cf6d18` (look around), or make a branch from it: `git switch -c back-to-preview-7 6cf6d18`.
  On GitHub: open the commit `6cf6d18`, then "Browse files" → branch/tag menu → create a branch or tag there
  (this cloud session may only push to `rewrite/main`, so it could not create the tag itself).
* Terrain: the terrain fingerprints of each snapshot are frozen in `Tests/WorldGen/Fixtures/`
  (`terrain-build5.json`, `terrain-preview7.json`, `terrain-build8.json`, `terrain-build9.json`). Generation changes after a snapshot are added as parameters whose
  old values reproduce the snapshot; a test proves it bit for bit (decision D25).
