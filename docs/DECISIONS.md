# Rewrite decisions

Each entry: the decision, why, and what would make us revisit it. Newest at the bottom. Tom can overrule any of
these; questions that genuinely need him are in [QUESTIONS_FOR_TOM.md](QUESTIONS_FOR_TOM.md).

## D1 — Work on `rewrite/main` in Thornwake, not a new repository (2026-10-04)
Tom asked for a new private repo `badsadcode/Thornwake-Rewrite`. The GitHub integration available to the cloud
session refused it: `POST /user/repos: 403 Resource not accessible by integration`. Per Tom's fallback, all work
goes to the branch `rewrite/main` in `badsadcode/Thornwake`. No other branch is touched (including `main` and the
session's default `claude/nifty-feynman-ebsbs1`). Revisit: if Tom creates the empty repo himself, the whole
branch history can be pushed there unchanged.

## D2 — Keep Godot 4.5.1 .NET and C# (2026-10-04)
Same engine family as GangTheGAME, all existing gameplay is C#, Tom's toolchain is pinned to 4.5.1. A rewrite in
another engine would throw away working gameplay and art integration. "Rewrite" here means new world, water and
streaming foundations under the existing game, in line with Master plan §2.1 ("preserve what works").

## D3 — Docs live in the existing lowercase `docs/` folder (2026-10-04)
Tom said `Docs/…`. The repo already has `docs/`; adding `Docs/` would create two folders that collide on
Windows (case-insensitive). On Windows, `Docs\REWRITE_PLAN.md` opens the same file anyway.

## D4 — New engine-independent world core in `Game/WorldGen/` beside the old `Game/Core/` (2026-10-04)
The old core keeps compiling, so the valley game and the old labs keep running at every commit. New code has
its own namespace (`Thornwake.WorldGen`) and its own tests (`Tests/WorldGen`) and benchmarks (`Tests/Benchmarks`).
Old code is archived only after its replacement is proven.

## D5 — Cross-platform deterministic math (2026-10-04)
Measured: the original frozen fingerprints fail on Linux because `Math.Sin/Cos/Exp/Atan2/Pow` differ between C
libraries. The new generator uses only IEEE-exact operations (`+ − × ÷`, `Math.Sqrt`, `Math.Floor`) plus our own
polynomial `DetMath` functions. .NET's JIT does not fuse multiply-adds on its own, so results are bit-identical
across x64/ARM64 Windows/Linux. Needed for multiplayer (design bible §48, §60) and for tests that pass in CI.
Revisit only if profiling shows DetMath is a bottleneck (it is not on the hot path; noise uses only + − ×).

## D6 — Reproduce A4 macro layout instead of inventing a new one (2026-10-04)
Tom has looked at and accepted A4's continents/islands/mountain belts. The new `WorldLayout` ports the A4
placement logic (same seeds, same RNG streams), so a given profile + seed yields recognisably the same
continents and ranges. The new generator adds what A4 lacks (erosion, rivers, lakes, walking-scale detail)
and is a **new generator version**: it does not claim to match A4 heights bit-for-bit, and old A4 manifests
remain readable by the old code.

## D7 — Rasterize global structure once per world (2026-10-04)
A4 recomputes continent and mountain-belt geometry at every sample (1.4–2.2 µs). The new plan rasterizes
smooth global fields (land influence, mountain envelope, eroded base relief) into 1024×1024 float grids over
the whole world (0.5–2 km cells, ~4 MB per channel), built in parallel once. Point queries are a bicubic lookup
plus noise octaves. Fine detail that a 0.5–2 km raster cannot hold (coast wiggles, ridged rock relief, hills)
stays procedural per sample, so there is no resolution cap at walking scale. Raster-scale erosion and hydrology
(M2) become simple grid algorithms instead of bounded certified searches.

## D8 — Replace the hydrology research chain with standard grid algorithms (2026-10-04)
Priority-flood depression filling (Barnes 2014), D8 flow and accumulation, implicit stream-power erosion
(Braun & Willett 2013) on the macro raster, then river polylines and lakes as vector features carved into the
sampler. These are linear or n·log n on the raster, well understood, and produce the visible rivers, lakes and
valleys the Master plan asks for. The old chain's useful policies (ocean outlets only, lakes have spill levels
and outlets, macro is a skeleton refined locally, finite lakes with rate-limited river inflow) are kept as rules.
