# Snapshots to go back to

Every snapshot is a commit on `rewrite/main` (history is never rewritten, so these stay valid forever).

| Snapshot | Commit | Windows build | How to get it back |
|---|---|---|---|
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
