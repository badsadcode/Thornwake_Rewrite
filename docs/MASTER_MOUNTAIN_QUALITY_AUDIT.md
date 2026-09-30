# Mountain-range quality audit — unchanged macro algorithm 3

2026-09-30. Read-only generator inspection before further hydrology refinement.

## Finding and recommendation

**The repeated stripe-like ranges are present in actual macro elevation, not merely in a diagnostic backbone overlay.** There is genuine curvature and small-scale peak variation, but the same long, fairly uniform-width, rounded-ended ridge construction recurs across all five presets. Several presets produce conspicuous trunk-and-branch shapes resembling raised ribs. This is a macro terrain quality issue; it is not caused by drainage or chunk boundaries.

A focused mountain-shape improvement is advisable **before stronger terrain-dependent hydrology commitments**. Keep algorithm 3 and all existing frozen worlds intact. Evaluate a separate, opt-in terrain version, compare it against these captures, and pin its terrain outputs before giving it new drainage/refinement baselines. This audit does not implement or select a replacement algorithm.

## Inspection scope and evidence

- 20 worlds: **balanced, continental, archipelago, mountainous and vast-world**, each with seeds **-9, 0, 42 and 7319**.
- Every world has a 512 x 512 whole-world elevation render, a 512 x 512 elevation render around its first mainland, and a matched mountain-influence render at identical bounds. Choosing mainland zero consistently avoids selecting only the worst continent in each world. Whole-world views include the other continents.
- The images use the existing `MacroWorldMap.Build` raster routine and current `MacroWorldGenerator.Sample` directly, through an isolated temporary .NET harness. They are the preview's actual map output, without added ridge lines or a changed shader. They are not screenshots of a newly loaded Godot scene.
- Two additional oblique surface renders use 192 x 192 samples of actual macro elevation. They are independently rendered heightfields, **not production game meshes**. Both clearly label **12x vertical exaggeration** and included bathymetry. They corroborate the raised-band geometry; their exaggerated slopes should not be read as gameplay-scale slopes.
- Measured all **216 range descriptors**, including **76 main trunks**, and 843 eligible interior trunk cross-sections against final sampled elevation.

All artifacts, frozen input manifests, measurements and reproducible harness/rendering sources are under `artifacts/mountain-quality-audit/`. The temporary harness is outside the project and the retained C# sources have `.cs.txt` extensions, so they do not enter the game build.

## Preset findings

| Preset | Observation across the four seeds | Median trunk chord/path ratio | Longest single backbone segment sampled |
| --- | --- | ---: | ---: |
| Balanced | Broad main ridge plus an attached spur; L/T/fork shapes recur. Seed 7319 is more bent, but remains a continuous raised band. | 0.966 | 34 km |
| Continental | Particularly long, almost straight central spines with short attached branches. | 0.988 | 47 km |
| Archipelago | Lower relief and some visibly curved examples, especially seed 42. Still mostly one continuous central ridge on each major landmass; weaker visual severity, same construction. | 0.978 | 17 km |
| Mountainous | Strongest raised-rib appearance: broad high ridges, abrupt-looking branch junctions and repeated multi-pronged arrangements. | 0.961 | 39 km |
| Vast world | Repeated narrow-looking backbones at world scale and long branched bands when zoomed. Seed 42 is a clear representative. | 0.981 | 80 km |

The ratio is end-to-end trunk distance divided by polyline length; 1 means a perfectly straight polyline. It measures backbone geometry, not geological realism. The median across all 76 trunks is **0.977**. Individual trunks do bend; this is not a claim that every ridge is perfectly straight or that real mountains cannot form linear belts.

For the final-elevation check, each eligible trunk interior location was sampled across a perpendicular transect extending one range-width parameter to either side. **693 of 843 transect maxima (82.2%)** remained within a quarter-width of the backbone. Coast-adjacent samples were excluded. These are local crest measurements, not a proof of global watershed quality; branches can shift a maximum. Together with the elevation renders, they show that the backbone remains expressed in the numerical terrain.

## Representative previews

The matched comparisons place **actual macro elevation on the left** and **mountain contribution on the right**. No line skeleton is drawn into either image.

- [Vast world, seed 42 — strong repeated band/branch structure](../artifacts/mountain-quality-audit/comparison-vast-world_42.png)
- [Mountainous, seed 42 — broad raised ridge network](../artifacts/mountain-quality-audit/comparison-mountainous_42.png)
- [Archipelago, seed 42 — less straight counterexample, still a continuous ridge band](../artifacts/mountain-quality-audit/comparison-archipelago_42.png)
- [Balanced, seed 7319 — bent ridge with attached branch](../artifacts/mountain-quality-audit/comparison-balanced_7319.png)
- [Oblique sampled surface: mountainous, seed 42, 12x vertical exaggeration](../artifacts/mountain-quality-audit/surface-mountainous_42.png)
- [Oblique sampled surface: vast world, seed 42, 12x vertical exaggeration](../artifacts/mountain-quality-audit/surface-vast-world_42.png)

Complete four-seed comparison sheets: [balanced](../artifacts/mountain-quality-audit/survey-balanced.png), [continental](../artifacts/mountain-quality-audit/survey-continental.png), [archipelago](../artifacts/mountain-quality-audit/survey-archipelago.png), [mountainous](../artifacts/mountain-quality-audit/survey-mountainous.png), [vast world](../artifacts/mountain-quality-audit/survey-vast-world.png). All five sheets and the representative comparisons/surfaces were visually inspected.

## Why this happens in the current source

`Game/Core/MacroWorldGenerator.cs`, lines 71–91: a main trunk uses five points at regular longitudinal spacing along the land feature's major axis, with modest lateral offsets. Branches start at a small fixed set of trunk vertices. Width is constant per range; branches use a fixed fraction of the main width. This limits large-scale variation and repeats a recognizable branch arrangement.

Lines 127–137 convert distance to those straight segments into a Gaussian ridge envelope, `exp(-(distance / width)^2)`. Noise mainly modulates its height through `.78 + .22 * Noise(...)`; it does not substantially bend the mountain footprint. Contributions combine with `max`, and the result is added to hills/base land elevation. There is no independent longitudinal endpoint taper, so segment endpoints produce rounded high caps, and no developed foothill/valley network breaks up the broad ridge envelope. Coast warping elsewhere in the sampler does not warp the mountain-distance field.

`Game/Core/MacroWorldMap.cs`, line 50: **Mountain ranges** colours `sample.MountainInfluence`; it is not a line drawing of the descriptor. The regular elevation view colours actual sampled elevation and adds hill shading. Colour thresholds and shading make some edges more conspicuous, but do not create the underlying band. Raising mesh/map resolution or hiding the diagnostic layer would not remove it.

## Improvement worth evaluating before deeper hydrology

Prefer a bounded, deterministic mountain-morphology pass over a broad rewrite or mandatory tectonic/erosion simulator:

1. More varied large-scale curvature and branching, avoiding the same central trunk/prong arrangement on each continent.
2. Smooth variation in width and amplitude along a range, tapered terminations, and better transitions at junctions.
3. Secondary ridges, foothills, passes/saddles and occasional massifs so a mountain belt is not just one continuous raised tube. Preserve varied plausible linear ranges where appropriate; the goal is not to ban straight mountains.
4. Keep parameters content-driven, global sampling deterministic and work bounded. Additional height noise alone is unlikely to solve the repeated footprint.

Evaluate the change with matched multi-seed elevation/surface previews and objective continuity/determinism checks. Natural-looking appearance is a design judgement, so the ratios above should not become a single pass/fail realism score.

## Fingerprint and hydrology implications

These ridges already shape drainage divides, spill heights, contributing area and regional route costs. Changing mountain morphology will generally change macro, drainage and regional outputs. It cannot honestly preserve the same terrain fingerprints while producing different terrain.

Do not overwrite the 15 existing coarse fixtures or the 23 regional fixtures. Do not simply increment `CurrentAlgorithm` and leave only the new version accepted: current manifest validation only supports the current algorithm. Any implementation should deliberately retain algorithm-3 reconstruction and introduce separate version dispatch/opt-in worlds for revised terrain. Preserve the existing hydrology architecture and recompute/version derived layers only for the new terrain version.

The current drainage/refinement milestone remains useful and stable; it need not be discarded. The best timing for a mountain-quality decision is now, before final channel carving, lake realization or persistent local hydrology increase migration costs. After a new terrain variant is accepted and pinned, resume the regional hydrology acceptance spike. If version 3 is intentionally kept for future worlds, accept its banded mountain morphology explicitly rather than expecting later rendering polish to cure it.

## Changes and verification

No generator, source, tests, content, project configuration or gameplay files were changed. SHA-256 verification found **0 changes across 199 existing files** in Game, Tests, Content and the two main project/config files; evidence is `source-verification.json`. No saves were loaded or migrated. Only inspection artifacts and audit/checkpoint documentation were written.

The isolated inspection harness compiled and completed all 20 renders successfully. No gameplay build or broad regression suite was rerun because no game code changed. Existing frozen fixture files are byte-identical. Hydrology development and generator modification remain paused at this audit, as requested.
