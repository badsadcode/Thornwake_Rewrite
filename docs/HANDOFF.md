# Handoff — where the work stands (2026-10-12)

Written when the work moved from the cloud session (CPU rendering only, ~17 s a frame) to Tom's PC (RTX 3080), so the
heavy Atmosphere 2.0 tests can run on a real GPU. Everything below is on `main`.

## Last build Tom has
Build 53 (v0.9.53, commit 987890d; packaged on Tom's PC, `dist/Preview-53`). Since then: the cloud look investigation (below). What went into build 53:
* **Terrain revision 16** (D91): a sea pocket inside a lake's reach is lake water — the "crater" in the benchmark lake
  (dump 182351). *Terrain version* gains **Build 52**; **Build 51** can now actually be chosen (the range stopped at 4).
* **Forest rays: A** (D92) — Tom's pick. M2 continues with A; B stays selectable until M2 is done.
* **Cloud blend (v2)** — setting `sky.cloud_blend` (0 = build 52, 1 = matched, default 1). Tom's screenshots (cloud
  tower over the sea from a mountain; lake at sunset with a snowy peak): clouds in front of land did not match the same
  clouds in the sky, mountain outlines showed through them, a cloudless band under the horizon. Causes found and fixed:
  1. land beyond ~47 km counted as sky by `CloudsNear` (reversed depth < 1e-6 with the 90 km far plane) — neither pass
     drew clouds there;
  2. the near pass lit clouds with the sun's sRGB colour instead of linear (pale, pink-white at sunset);
  3. Godot's fog (depth fog by sky affect, then volumetric fog) veils every sky pixel, the sky's clouds included, but the
     near pass had `fog_disabled`. Now the near pass has fog on, its fragment sits just past the volumetric fog's length
     (where the sky reads it), and it writes unpremultiplied colour with `PREMUL_ALPHA_FACTOR` = opacity, so Godot
     produces cloud × transmittance + in-scatter × opacity — the same as over the sky. Godot's sky depth fog is
     emulated (`cloud_fog`, `cloud_fog2` globals; sun scatter × π as in Godot's `sky.glsl`).
  4. the cloud map's mip level inside the march now follows distance, not screen derivatives.
  Build 52's near pass is kept as `CloudsNear52.gdshader` (used when Cloud blend = build 52).
  **Verified** with `--cloud-split-test` (left half: near pass over the sky; right half: the sky's own clouds): with all
  effects on, no seam at the middle. **Not yet verified** on a scene with clouds in front of land (Tom's case): see
  "Next" below.
* **Benchmark**: the camera faces the shot's direction before the warm-up, not after (still 00 of each scene had the
  volumetric fog history of the old view — a beige veil; in the rays A/B, only still 00 per scene was affected).

## Next
* **Multiplayer spike (2026-10-13, D101, D102)**: checkpoints 1 (two windows see each other move — Tom) and 2 (digging
  and late joining — automated test) done and merged into `main`; **build 55** (`d2b59b9`, `dist/Preview-55`) has the
  launcher's Multiplayer page and the network log. Checkpoint 3: Tom's line is DS-Lite (D104); first internet test done
  through playit.gg, Poland ↔ Brazil, ~320 ms, ~2 KB/s per player (D105). The host's freezes after teleports were
  rain erosion over a rectangle spanning both places (D106, fixed); also D106: rain (H) shared, map arrows with the
  other players and a go-to list, Esc → Host this world, join-failure crash and send spam fixed — in **build 56**
  (`404a9c5`, `dist/Preview-56`; host and guests must switch builds together, protocol 3). Still open: same weather
  check (§14.0 done-criterion 3), sharing poured water / springs (Tom to decide).
* **Step 1 in progress (2026-10-13, D108–D110)**: playable first slice — 6 axe / LMB chop a tree, I pack and crafting
  (campfire, cook fish), E wood on a fire, needs line (food, warmth, wet, health), all through the host's GameRules,
  also alone; `--gameplay-test`. In **build 57** (`8a1dc3b`, `dist/Preview-57`). **Next**: fishing (a cast action, raw fish from water) so cooking has its fish; E
  gathering (grass → fibre, berries, loose stones; Tom); a fire flame from the torch's particles; death (drop the pack
  where you fall, §4); saving the world (edits, removals, objects, inventories); Tom's storm atmosphere (D109).
  Since build 57: Valheim-like chopping (D111). Open from Tom (dump 221438): the torch lights the ground fog evenly far
  into the night — trees far away stand dark against an orange haze; light sources should shape the fog, but less
  uniformly (look at the torch's `LightVolumetricFogEnergy` / range and the fog's density near the ground; atmosphere
  work, after the mechanics).
* **Parked (2026-10-13) for the multiplayer spike** (`docs/GAMEPLAY_DESIGN.md` §14.0, branch `multiplayer-spike`): the
  cloud drafts of D97 stay behind their settings, off by default — *Cloud caps: dense* works (waiting for Tom's pick),
  *Lens clouds: stacked* reads as hard blades from below (rework: softer, thicker lens shapes), *Cloud turbulence* not
  yet visible (test from inside a cloud). Since build 54 on `main`: dumps carry `--cloud-drift`; Alden's torch (R) and
  first person at full zoom-in (D98) — not yet in a build, Tom has not commented yet.
0. **Done on Tom's PC (2026-10-12):** step 1 below (captures on branch `captures/cloud-blend`; Tom accepted the fix), the tree instance-uniform overflow (D93), build 53. Clouds weak: causes found (D94: Godot fogs the sky's half-resolution clouds twice; wind smear at Wind strength 4; weak self-shadowing); *Sky → Cloud look* added; Tom played it, fixes from his dumps (D95: forest hole under a high camera, horizon line inside clouds, far land cut at 90 km); **reference is the default (D96)**, return point `pre-cloud-look-reference`. Open, Tom to decide: denser cloud caps (a cap should hide the summit), turbulence inside clouds, the lens disk (reshape / interim / M3). All of it is in **build 54** (v0.9.54, `0e69bfd`, packaged in `dist/Preview-54`).
1. **Check the cloud fix on Tom's case** (GPU, minutes instead of an hour). Tom's world from dump 182351 (the
   benchmark lake, ground −2.1 m), the camera 400 m up so clouds stand in front of land, cumulus towers forced, four
   directions, build 52 blend vs matched (`--fly-height` counts from the ground, not the water; the lens peak printed
   in the log is relative to the camera):
   ```
   godot --path . res://Game/Rewrite/WorldLab.tscn -- <Reproduce args of dump 182351 without --at --view --fly-height --day --hour sky.clouds time.paused fx.dof>
     --at=-5669.182,5738.657 --fly-height=400 --view=0,-0.1,8 --extra-yaws=1.57,3.14,4.71 --day=1 --hour=15
     --set=sky.clouds=0.5 --set=time.paused=1 --set=fx.dof=0 --clouds=0.8,0,0,0 --set=sky.cloud_blend=1 --world-shot=peak/blend1.png
   ```
   (and `--set=sky.cloud_blend=0` for build 52). Also a sunset (`--hour=19.5`) — Tom's second screenshot. Then build 53.
2. **Lens cloud** (Tom's second screenshot): `ct_lens` in `CloudShapes.gdshaderinc` draws four perfectly elliptical
   13 × 6.4 km plates; from ~5 km away it fills half the sky with stepped rims. Part of M3 (cloud types in the volume);
   artistic — ask Tom before reshaping.
3. **M2: finish forest rays A** (`Game/Rewrite/ForestAir.gdshader`, `Atmosphere.UpdateForestRays`).
4. Make captures smaller for Tom (downscaled JPGs, one side-by-side image per A/B) — promised.
5. Later milestones: M1 pause point 3 (warm sun glow, moon halo), M3 single-pass cloud model (acceptance shots: dumps
   115949 / 120402), M4 haze and distance, biome light profile tuning (`Content/light_profiles.json`).

## Developer tools added recently
* `--cloud-split-test[=1|2|3]`: 1 = left half clouds by the near pass, right half by the sky; 2 = also a probe strip with
  the sun colour/direction each pass sees; 3 = each pass's cloud opacity as grey.
* `--cloud-split-test=4`: CloudsNear draws every cloud, sky ones included (the sky draws none); `--cloud-fog-off`: CloudsNear from `CloudsNearNoFog.gdshader` (no Godot fog); `--cloud-dev=x,y`: global `cloud_dev` (x aerial perspective scale, y extra self-shadowing) — D94.
* `--probe-water=x,z,r,step` (grid of water kinds and levels, lake/outlet stats), `--clear-spot=x,z`.
* Capture suites: `--bench=<suite> --bench-out=<dir> --bench-scenes=a,b --bench-stills=N --bench-compare=key=v1,v2`
  (see `docs/BENCHMARK.md`).

## Known, not yet fixed
* Three perched lakes in Tom's world step down 8–114 m at the outlet (drawn as a steep sheet, physically a waterfall).
* Grass and scatter still use Godot's Self visibility fade (trees switched to dithering in D86).
