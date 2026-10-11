# Atmosphere 2.0 — M1 plan: light and identity (2026-10-12)

M1 replaces how the game decides *how bright and what colour* the light is. It is the base every later milestone is
tuned on (`ATMOSPHERE_2_0_SAGE_FEASIBILITY_REVIEW.md` §10). Baseline: build 47 (branch `pre-atmosphere-2.0`), Look
natural (ACES). Captures: benchmark suite `atmosphere` (`docs/BENCHMARK.md`).

## What the light model does today (`Game/Rewrite/Atmosphere.cs`)

| Quantity | Today | Problem |
|---|---|---|
| Sun energy | `clamp(elevation / 25°, 0.05, 1.1)`, × (1 → 0.3) under cloud | Flat from ~28° up: 9:00 and 15:00 look like noon; no "high-energy noon" vs "soft morning". |
| Sun colour | white-warm `fff3df` → orange `ffc58a` by a single "warm" factor at low sun, then × biome tint | One step from day to sunset; no golden hour of its own, no blue hour. |
| Sky (ambient) light | from the sky's radiance map, energy 0.42 (natural) → 0.3 under cloud | Same share of sky light at noon, dawn and dusk; shade is either too grey (noon) or too bright (dusk). |
| Exposure | fixed: ACES, white 6, 0.95 × 1.15 × *Brightness*; no eye adaptation | Bright scenes clip, dim forests stay dim: players turn *Brightness* down and *Glow* up (Tom: 0.5 and 1.45). |
| Glow | natural: HDR threshold 2.6, levels 0–4 | Too little real HDR in the scene for a threshold to separate highlights; at high intensity it lifts everything (the grey blob family). |
| Shade colour | whatever the radiance map gives | No deliberate warm sun / cool shade split, which is most of the "storybook" look. |

## What M1 changes

1. **One light state per frame** (`AtmosphereLight`: sun colour and energy, sky colour and energy, ambient tint,
   exposure, glow threshold), computed from the sun elevation, cloud cover/storm, time of night and (later) the biome
   profile. Every system that uses the light (terrain, water, clouds, mist, beams, underwater, canopy) reads it from
   here instead of re-deriving its own. No look change by itself; it is what makes the next steps controllable.
2. **Sun curve by phase**, not one ramp: night → blue hour (−6°…0°) → golden hour (0°…10°, warm, low energy, long
   shadows) → morning/afternoon (10°…35°, energy rising) → high noon (above 35°, white, strongest). Values come from a
   small curve table, so Tom and Noor's references can be matched by editing numbers, not code.
3. **Warm light, cool shade**: the sky light keeps the sky's blue (stronger when the sun is high and the air clear),
   the sun warms toward low elevations; overcast pulls both toward neutral grey-blue and flattens the ratio.
4. **Exposure as a target, not a slider fight**: a gentle, slow exposure adaptation between a darker and a brighter
   limit (bright plains vs a forest interior), much narrower than Godot's auto exposure (which washed out overcast,
   per the old note in `Atmosphere.cs`). *Brightness* stays as a personal offset.
5. **Bloom from real highlights only**: the sun disk, sunlit cloud edges, glints and lit water get genuine HDR values;
   glow threshold ~1.0 above the exposure target with a soft knee, intensity default lower. The sun's glare becomes a
   separate, small, art-directed element (the blob fix lives here).
6. **Tracked artefacts in M1's systems**: the grey blob around the sun; water looking semi-transparent above the
   horizon (fog applied to the transparent water but not equally to the sky behind it: one controlled capture, then the
   fix); sky seams (one controlled capture each).

**Kept as is in M1:** the effects themselves (shafts, beams, fog, mist, clouds) — only re-tuned so they sit on the new
light; *Look: classic* stays available for comparison (not retuned); every setting keeps working.

## How it will be judged

* Tom and Noor's reference images (forest noon, forest morning, plains evening, mountain, storm, night) next to the
  captures of the same scene types, build 47 vs M1.
* The `atmosphere` suite: stills of all ten scenes, and the scripted moves recorded on Tom's PC.
* With *Sun shafts*, *Light beams* and *Glow* off, the forest at 10:00 and the plains at 17:30 must already look good
  (the review's "no crutches" test).
* Benchmark: no measurable cost (it is constants and one small adaptation step).

## Pause points (Tom's approval)

1. After the light-state refactor: captures show *no* change (proves the refactor is neutral).
2. After the curves, warm/cool and exposure: first visible comparison, judged against the references.
3. After bloom/glare and the artefact fixes: M1 done, then M2 (forest rays shoot-out).
