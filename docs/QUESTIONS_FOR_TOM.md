# Questions and checks for Tom

Things that need your eyes, taste or a real GPU. I keep working on sensible defaults meanwhile (see
[DECISIONS.md](DECISIONS.md)); answer whenever convenient. Newest at the bottom.

## Q1 — Separate repository (optional)
GitHub refused to let the cloud session create `badsadcode/Thornwake-Rewrite` (403). Everything is on branch
`rewrite/main` in Thornwake instead. If you still want a separate repo, create an empty private
`Thornwake-Rewrite` and add it to the session; I can push the full history there.

## Q2 — Things the cloud cannot judge (ongoing list)
* Real-GPU frame times and hitches (the cloud renders with a software rasterizer at a few FPS).
* Final visual taste: terrain colours, water colour/clarity per body type, foam amount, how "stylized vs real"
  the water should look next to the valley game's low-poly pixel-texture art.
* Blender work (none planned yet).

## Q3 — Please run the new tests once on Windows (cross-platform check)
`dotnet run -c Release --project Tests/WorldGen`. The last test compares terrain fingerprints recorded in the
cloud on Linux. If it passes on your machine, world generation is proven identical across platforms (the old A4
fingerprints were not: they fail on Linux). If it fails, tell me; that is a bug I need to fix.

## Q4 — Terrain feel (later, once you can walk it in M4)
Defaults now: lowland "hill country" up to ~±30 m rolling relief at 0.1–1.6 km scale fading to ~0.2 m bumps at
a few metres, alternating every ~28 km with flatter plains (25 % roughness); mountains get rocky ridged detail
up to ~5 % of their height. All are single numbers in `TerrainStyle` — say "flatter", "rougher", "bigger
plains" etc. and I will tune.
