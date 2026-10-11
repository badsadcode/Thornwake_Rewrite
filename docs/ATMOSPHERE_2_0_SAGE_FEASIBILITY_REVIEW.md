# Atmosphere 2.0: feasibility review (Sage, 2026-10-11)

Review of `THORNWAKE_ATMOSPHERE_2_0_V1_1_SAGE_REVIEW.md` (vision v1.1). Document only: no code, shader or setting
was changed for this review. Facts below come from the current `Thornwake_Rewrite/main` code (build 46/47) and from
Tom's benchmarks of builds 42–46 on his RTX 3080 (`docs/PERFORMANCE_TREES.md`, `docs/BENCHMARK.md`).

## 1. Executive verdict

**GO WITH REVISED SCOPE.** The vision (section 1 of the brief) is right and achievable in Godot 4.7 for this game.
The plan around it is too big and in one place aimed at the wrong target:

* **The problem is not mainly missing techniques; it is that nobody owns the picture.** Ten-plus effects each add
  light (screen-space shafts, cloud beams, volumetric fog, glow, leaf emission, sky tint, haze, two tonemappers…) and
  the player compensates with sliders. Tom's own benchmark settings show it: *Distance haze 0*, *Brightness 0.5*,
  *Glow 1.45*, *Saturation 1.35*, *Leaf translucency 1.95*, *Sun glare 0.3*. That is the "push shafts, push haze,
  compensate with contrast, cut bloom" loop the brief describes, in numbers. So "start from darkness" is the right
  instinct; but it is a **calibration** job (one light budget, one exposure model, each effect with a defined job),
  not a renderer rewrite.
* **Most of the renderer should stay.** Volumetric clouds v2 (sky march + depth-aware `CloudsNear`), the weather and
  wind fields, the night sky, the water, Godot's own volumetric fog: these work and are already world-space. What is
  missing is coherence between them and a light baseline.
* **The flagship (forest rays) is cheaper than feared.** Godot's shadowed volumetric fog already exists in the game,
  costs ~0 ms in Tom's benchmark (it is on in his settings: −0.3 to +0.1 ms), and is the one technique that gives
  world-space, tree-occluded rays that do not need the sun on screen. It is currently tuned as a thin haze (density
  0.002–0.0035, 96³ froxels over 180 m). The first proof of concept is a *tuning and data* job, not new code.
* **Cut or postpone about half of the spec**: per-biome everything (start with 5 profiles), the full test matrix
  (start with 6 scenes), constellations/aurora (already postponed), storm walls beyond what exists, accessibility
  extras beyond a mist slider, a "data-driven editor-ready profile system" (a JSON file per biome is enough now).
* **The money is in clouds** (2.2–3.4 ms in every scene, the biggest single item left) and shadows (3.3–3.7 ms in
  forests), not in fog or shafts (< 1 ms together).

## 2. Facts vs hypotheses

### Verified in the code (build 46)

| Topic | Fact | Where |
|---|---|---|
| Pass structure | Sky shader (half-res pass, radiance cubemap) → scene → full-screen quads on the camera, each its own spatial shader with `RenderPriority`: rain curtains 98, `LightBeams` 99, `CloudsNear` 99, `SunShafts` 100, underwater 127; then Godot's post (fog, volumetric fog, SSAO/SSIL/SSR, glow, tonemap, adjustments). No CompositorEffects. | `Atmosphere.cs` `AttachCamera` (≈ l.158–190) |
| Clouds | 4 qualities: one layer, layered, volumetric (sky only, half res), volumetric v2 (also `CloudsNear.gdshader` marching from camera to scene depth). One shared march in `SkyCommon.gdshaderinc` (`cloud_march`, `volumetric_v2`). Cloud positions come from global uniforms so the radiance map is not re-baked every frame. | `Sky.gdshader` header, `SkyCommon.gdshaderinc` l.460, 558 |
| Sun shafts | Screen-space: march toward the sun's screen position, gather unobstructed sky (depth = far plane). Only works with the sun on or near the screen; trees cut rays only as silhouettes against sky. | `SunShafts.gdshader` |
| Cloud beams | Full-screen pass that ray-marches the view ray below the deck and tests the cloud texture toward the light: world-space, but only clouds occlude, not terrain or trees. | `LightBeams.gdshader` |
| Volumetric fog | Godot native, `VolumetricFogLength 180`, froxels 96×96×96 (`project.godot`), density 0.002 (natural) / 0.0035, anisotropy 0.65–0.75, temporal reprojection 0.9. **Default off** (`fx.volumetric_fog` = 0) but forced on when ground-fog patches are active. Sun `LightVolumetricFogEnergy` tied to *God ray contrast*. | `Atmosphere.cs` l.123, 358, 1015–1018 |
| Ground mist | One 420 × 70 × 420 m `FogVolume` box, re-centred on the camera each frame at the sampled ground height + 15 m; density from 3D noise in world coordinates (wrapping every 8 km). The noise is world-anchored; the volume (and so where mist can exist) is camera-anchored. | `Atmosphere.cs` l.995–1006, `Mist.gdshader` |
| Haze | Godot depth fog: `FogDensity ≈ 0.00009 × Haze`, aerial perspective 0.26–0.35 × Haze, scaled up to ×4 in storms and by biome mood. | `Atmosphere.cs` l.309–311, 460 |
| Shadows | Directional, 4 cascades, max distance 90 / 200 / 450 m by *Shadows* (default 200 m), atlas 2k/4k/8k. Near trees (tree-field cells within the full-detail range, 140 m) cast shadows, by default through the simplified LOD2 proxy. | `Atmosphere.cs` l.300; `TreeField.cs` l.251–275 |
| Two looks | *Look* classic vs natural switches tonemapper (Filmic/ACES), white point, glow mode and threshold, SSIL, contrast. | `Atmosphere.cs` l.314–332 |
| Engine | Godot 4.7.2, `use_legacy_blending = true` (4.5 fog blending). | `project.godot` |

### Measured (Tom, RTX 3080, 1080p, build 46 defaults except his sliders)

| Cost (GPU ms) | forest | lake | coast | mountains |
|---|---:|---:|---:|---:|
| volumetric clouds (vs flat) | **3.38** | **3.32** | 2.42 | 2.23 |
| all shadows | 3.69 | 3.26 | 0.90 | 0.67 |
| — of which tree shadows (simplified) | 1.95 | 1.27 | 0.02 | 0.31 |
| light beams | 0.49 | 0.87 | 0.22 | 0.31 |
| sun shafts | 0.24 | 0.05 | ≈0 | ≈0 |
| volumetric fog | ≈0 | ≈0 | ≈0 | ≈0 |
| ground fog | ≈0 | ≈0 | ≈0 | ≈0 |
| SSIL / SSAO / SSR | 0.3 / 0.1 / 0.2 | 0.3 / 0 / 0 | | |
| water reflections | | 2.1 | 0.2 | |

### Hypotheses (not verified)

* Forest rays will read well with native volumetric fog once density is local (inside forests/hollows) and the light
  energy into the fog is raised. **Needs experiment** (test B below).
* The intermittent diagonal sky seam (build 41 dump 205856) is a sky half-res / cubemap or reprojection boundary,
  not the engine upgrade. Unreproduced; I would not spend time on it until it recurs in a controlled scene.
* The "semi-transparent water above the horizon" is most likely the water's distance fade meeting the sky fog colour
  (water is a transparent pass, depth fog applies to it, the sky behind is not fogged the same way). Needs one
  controlled capture to confirm before it is assigned to anyone.
* Lower-end GPUs (GTX 1060/1650): the volumetric cloud march is the item most likely to break 30 FPS; everything
  else scales with existing settings. Unmeasured: no data from Tom's friends' machines yet.

## 3. Goal-by-goal verdict

| Goal (brief §) | Verdict | Why | Visual impact | Risk |
|---|---|---|---|---|
| §4 Start from darkness | **Modify** → "calibrate from neutral" | Right instinct. Do it as one week of controlled captures: all optional effects off, fix sun/sky/ambient/exposure, then add effects one at a time with a written job each. Not a new pipeline. | High (everything after builds on it) | Low |
| §5 Sun, skylight, shadows | **Keep (P0)** | Biggest perceived win per hour: exposure model, ambient vs sun energy, warm/cool split, twilight. Mostly `Atmosphere.cs` constants and curves. | High | Low |
| §5 Shadow coverage for forests | **Keep, small** | 200 m of tree shadows is enough for rays near the player (volumetric fog length is 180 m). No new proxies needed. | Medium | Low |
| §6 Forest god rays | **Needs experiment → likely keep** | Native volumetric fog with local density is the candidate; keep screen-space shafts as the "sun in view" accent at a lower strength; cloud beams stay for open land. | Very high | Medium (froxel resolution, temporal smear) |
| §7 Aerial perspective | **Modify** | Keep Godot depth fog + sky colour matching; add per-biome clarity. No new system. | Medium | Low |
| §7 Ground mist anchored to terrain | **Modify** | Keep the FogVolume + world noise; feed density from terrain data (hollows, water, wetness) instead of a camera-height plane. Several smaller volumes (or a density texture) around the player instead of one box at one height. | High in valleys/swamps | Medium |
| §8 Clouds | **Keep v2, optimise** | v2 already does depth-aware near clouds and summits. Spend on cost: lower res + upsample for `CloudsNear`, temporal accumulation for the far sky, fewer light steps. Target: halve 3.4 ms. | Medium (look), high (perf) | Medium |
| §8 Clouds change character / build and dissipate | **Defer** | Weather field already moves cells; real growth/decay is a cloud-system project of its own. | Medium | High |
| §9 Biome atmosphere identity | **Keep, cut down** | 5 profiles first (temperate forest, plains, taiga/cold, desert, swamp), each: sun/ambient tint, haze clarity, mist frequency, cloud preference, grading. Blend by the existing biome sampler. The rest later. | High | Low |
| §9 Data-driven, editor-ready profiles | **Modify** | A JSON per profile in `Content/` (like `benchmarks.json`), validated by the headless tests. The editor comes when the editor comes. | — | Low |
| §10 Day/night/twilight | **Keep** | Mostly part of §5 calibration; night sky stays as is. | High | Low |
| §10 Constellations etc. | **Defer** (as the brief says) | | | |
| §11 Weather moods | **Keep, via profiles** | Weather already drives fog/clouds/rain; add "post-rain" and "overcast flat light" as states of the light model. | Medium | Low |
| §11 Rain crown splashes | **Defer** | Rain on water exists; crown splashes are polish. | Low | Low |
| §12 Water/horizon | **Needs experiment** | One controlled capture first (see tests). Do not change water. | Medium | Low |
| §13 Bloom/tonemap/grading | **Keep (P0, part of §5)** | Pick one tonemapper and one glow model; retire *Look: classic* as default only after Tom compares. | High | Low |
| §14 Presets for friends' GPUs | **Keep** | Build presets from measured costs; needs one low-end data point before budgets. | High (for them) | Low |
| §15 Full test matrix | **Modify** | 6 scenes now (below); grow when phases need them. | — | — |
| §16 Diagnostics | **Keep, reuse** | The benchmark already solos effects and times them; add "freeze clouds/wind" and a fog-density view. | — | Low |
| §17 Phases A–H | **Modify** | Reorder and merge (section 6). | — | — |

## 4. Architecture options

**A. Calibrate and consolidate (recommended).** Keep the passes; give each one job; replace the light model's
constants with one place (an "atmosphere state" computed per frame from time, weather and biome profile) that all
shaders read through the existing global uniforms. New work only where measured: local fog density, cloud cost.
*Pros:* lowest risk, every step A/B-able against today, keeps what works (clouds v2, water, night). *Cons:* screen-
space quads stay (fine; their outputs are world-space where it matters).

**B. Modular hybrid with CompositorEffects.** Move clouds, beams and shafts into Godot 4 `CompositorEffect`s (compute
shaders at chosen points in the frame: before transparents, half resolution, temporal history buffers). *Pros:*
proper half-res + reprojection for clouds (the biggest cost), correct ordering with transparents (water), shared
depth/history. *Cons:* C#/GDShader compute plumbing, harder debugging, more code to maintain; only worth it for the
cloud pass. **Use for clouds only, and only if option A's cheaper cloud fixes miss the target.**

**C. Pipeline redesign (custom sky/atmosphere model, e.g. physically based scattering LUTs + froxel fog of our
own).** *Pros:* full control. *Cons:* weeks of work before the first improvement, re-creates what Godot already does
(volumetric fog, sky), high regression risk across water and night. **Reject** for this game.

## 5. Contradictions, blind spots, pitfalls

* **Dense shadowed forests vs low-end GPUs.** Rays need shadows; shadows are 3.7 ms in a forest on a 3080. Low preset:
  90 m shadows, no plant shadows, rays from fog only near the player. Accept that Low rays are weaker.
* **Clear visibility vs dramatic fog.** Rays need particles in the air; clear air shows none. Solve with *local*
  density (forest interiors, mornings, hollows) instead of global density. The brief's "not dense everywhere" rule is
  exactly right.
* **Large view distance vs volumetric resolution.** Froxel fog covers 180 m; beyond, depth fog. Rays only exist near
  the player; that is normal and fine.
* **Temporal reprojection vs swaying trees and fast flight.** Volumetric fog reprojection (0.9) smears moving shadow
  edges; flight at 200 m/s ghosts. Lower the amount with camera speed.
* **Coherent weather vs biome skies.** Weather is world-wide and continuous; biome tints are regional. Rule: weather
  sets *what* (cover, rain, wind), the profile sets *how it looks* (tint, clarity), and weather wins when they
  disagree (a storm over a desert is grey).
* **Floating origin.** The world rebases every 1.5 km. Every world-space noise (mist, clouds, beams) must use world
  coordinates passed in, not node positions; current shaders already wrap at 8 km. New fog density textures must
  follow the same rule.
* **Pass order with transparents.** Full-screen quads draw after the opaque scene and before/after water depending on
  priority; fog applies to water but not to the sky the same way: the likely cause of the horizon complaint. Any new
  pass must be checked against water.
* **Two looks.** Every calibration step has to be done twice while *Look* has two tonemappers. Decide one default
  early (question 1).
* **Settings sprawl.** 25+ lighting sliders already; Tom's settings show they are used to fight each other. The
  revised plan should *remove* sliders (keep them under "advanced"), not add.

## 6. Proposed minimum scope and milestones

Each milestone ends with a build, captures of the 6 scenes, the benchmark, and Tom's yes/no.

1. **M0 — baseline (½ day).** Return point: tag + branch of the build before any atmosphere change. Capture the 6
   scenes at fixed time/weather with today's defaults *and* with Tom's sliders.
2. **M1 — light baseline (the "darkness" step).** One exposure model (pick the tonemapper), sun/sky/ambient energies
   per time of day from one curve, glow only above a real HDR threshold, no compensating sliders needed at defaults.
   Effects stay but are re-tuned to fit. *Acceptance:* forest at 10:00 beautiful with shafts, beams and glow off.
3. **M2 — forest rays.** Native volumetric fog: local density in forest interiors (from the tree-field density the
   game already has) and hollows, higher sun fog energy, anisotropy tuned; screen-space shafts reduced to an accent.
   *Acceptance:* Tom walks under the canopy at low sun and sees rays looking sideways and away from the sun.
4. **M3 — clouds cost.** `CloudsNear` at half/quarter resolution with depth-aware upsampling; fewer light steps;
   temporal accumulation for distant sky. Target ≤ 1.7 ms (half of today), same look in A/B captures.
5. **M4 — mist and haze from the terrain.** Mist volumes placed by terrain/water data; per-biome haze clarity.
6. **M5 — 5 biome profiles + weather states.** JSON profiles, smooth blending, overcast and post-rain light.
7. **M6 — presets.** Low/Medium/High from measured costs, one low-end data point (a friend's GPU) before freezing.

Water horizon and the sky seam are investigated with one controlled capture each during M1, fixed only if confirmed.
Everything else in the brief: after M6, by Tom's priority.

## 7. Minimal proof-of-concept tests

Six fixed scenes in the benchmark world (`Content/benchmarks.json`, seed 42), time and weather frozen, same camera:

| Scene | Time / weather | Checks |
|---|---|---|
| Forest B (dense temperate) | 08:30 clear | rays sideways and away from the sun, shade colour |
| Forest B | 12:00 clear | no crutches needed, no glare blob |
| Lake overlook | 17:30 broken clouds | water–sky horizon, beams, cloud shadows |
| Mountain ridge | 10:00 low deck | clouds wrapping summits, cloud cost |
| Swamp | 07:00 after rain | mist banks, local density |
| Coast | 20:30 dusk storm approaching | twilight undersides, rain curtains, exposure |

For each technique decision: captures (still + 10 s turn), GPU time from the benchmark's ablation, and Tom's choice.
The forest-ray test is the first one: **A** today's shafts only, **B** native volumetric fog only (local density),
**C** B + shafts at 30 %, **D** Low (fog off, shafts only). One evening's work to set up.

## 8. Questions for Tom and Noor

1. **One look.** Natural (ACES) or classic (Filmic) as the base everything is calibrated against? (Natural is the
   default today.)
2. **How dark may forests be?** Valheim's forests are genuinely dim at noon; a brighter, readable forest is a
   different target. Pick one reference screenshot.
3. **Rays strength vs clarity.** Rays need visible air. Is a slightly hazy forest interior on clear days acceptable
   (as in the Valheim references), or must clear days stay crystal-clear with rays only at dawn/after rain?
4. **Which 5 biomes first?** Proposed: temperate forest, plains, taiga, desert, swamp.
5. **Low-end target.** Which exact GPU does the weakest friend have? One benchmark run from that PC before M6.

## 9. Final recommendation

**GO WITH REVISED SCOPE**: option A, milestones M0–M6, starting after build 47's baseline. Retire nothing until its
replacement wins an A/B in front of Tom.

**Confidence:** high that calibration (M1) gives the biggest visible improvement for the least risk; medium that
native volumetric fog delivers Valheim-like forest rays at acceptable cost (needs test B; froxel resolution and
temporal smear are the risks); medium that clouds can be halved in cost without CompositorEffects. **Unknowns:**
low-end GPU numbers; whether Tom prefers dim or bright forests (changes M1); the water-horizon cause.
