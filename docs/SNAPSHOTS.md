# Snapshots to go back to

Every snapshot is a commit on `rewrite/main` (history is never rewritten, so these stay valid forever).

| Snapshot | Commit | Windows build | How to get it back |
|---|---|---|---|
| **Preview 7** (Tom: "really happy with this state", 2026-10-05) | `6cf6d18` | Thornwake-Rewrite-Preview-7 (v0.9.6) | see below |
| Build 5 (before natural banks / steeper shores) | `cdb2e31` | Thornwake-Rewrite-Preview-5 (v0.9.4) | also: New world → "Build 5 terrain" in any later build |

**Going back to a snapshot**
* Code: `git checkout 6cf6d18` (look around), or make a branch from it: `git switch -c back-to-preview-7 6cf6d18`.
  On GitHub: open the commit `6cf6d18`, then "Browse files" → branch/tag menu → create a branch or tag there
  (this cloud session may only push to `rewrite/main`, so it could not create the tag itself).
* Terrain: the terrain fingerprints of each snapshot are frozen in `Tests/WorldGen/Fixtures/`
  (`terrain-build5.json`, `terrain-preview7.json`). Generation changes after a snapshot are added as parameters whose
  old values reproduce the snapshot; a test proves it bit for bit (decision D25).
