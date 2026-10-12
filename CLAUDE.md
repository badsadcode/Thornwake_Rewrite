# Working on Thornwake Rewrite

Standing rules from Tom for any Claude session in this repository (cloud or local).

## Repositories
* **This repository (`badsadcode/Thornwake_Rewrite`, branch `main`) is the only one to change.**
* `badsadcode/Thornwake` is the ORIGINAL: never push, commit, open PRs or change anything there.
* `badsadcode/GangTheGAME` is read-only reference: never push to it.
* Other repositories Tom mentions are sources of ideas only, never code to copy in.

## How to work
* Decide without asking; log every decision with its reason in `docs/DECISIONS.md` (next number after the last `## D…`).
* Commit small, push often. No pull requests unless Tom asks.
* Keep the headless tests passing: `dotnet run --project Tests/WorldGen/Thornwake.WorldGen.Tests.csproj -c Release`
  (`-- --filter=<text>` for some tests, `-- --record` re-records the current terrain fingerprints: generator changes only).
* **Never change existing world types or the terrain of existing worlds.** A generator change gets a new terrain revision
  (`WorldStyle.Revision`), the previous one stays selectable in *Terrain version*, and its fingerprints are frozen
  (see D89, D91 and `Tests/WorldGen/CoreTests.cs`).
* **Every visual change must be switchable or keep a return point** (a setting, or the old shader kept beside the new).
* Pause and ask Tom before significant artistic decisions (looks, palettes, new styles); technical fixes need no pause.
* Shaders: Godot does not allow `return` in `fragment()`/`sky()`. Check a changed shader compiles before pushing
  (run the game or a capture and look for `SHADER ERROR` in the log).
* Each build Tom gets: version bump, row in `docs/SNAPSHOTS.md` (commit = return point), notes in `docs/PLAYTEST_REWRITE.md`.
  Windows packages are split into 25 MB parts with a `JOIN.cmd` (`tools/make_join_cmd.py`) for sending through chat.
* Tom's debug dumps (F-key dump in game) carry a `Reproduce:` line: paste its arguments after `--` to rebuild the exact
  world, place, time and settings.

## Where things are
* Game: `Game/Rewrite/` (entry scene `Game/Rewrite/WorldLab.tscn`), world generation: `Game/WorldGen/`, simulation:
  `Game/Simulation/`, data: `Content/` (biomes, light profiles, benchmarks), tests: `Tests/WorldGen/`.
* Current state and what is in flight: `docs/HANDOFF.md`. Benchmarks and capture suites: `docs/BENCHMARK.md`.
* Atmosphere 2.0 plan and references: `docs/ATMOSPHERE_2_0_*.md`.
