# Macro Algorithm 4 mountain experiment — review checkpoint

2026-09-30 historical experiment report. **Algorithm 4 has now been formally accepted by the user.** See [the current acceptance checkpoint](MASTER_A4_HYDROLOGY_ACCEPTANCE.md) for freezing, default policy and hydrology progress. The experiment results below preserve the evidence and recommendations at review time; their former awaiting-review/default statements are historical.

## Original result and recommendation (historical)

The experiment recommended accepting the **morphology direction** after visual review, then freezing dedicated Algorithm 4 terrain fixtures before deriving drainage baselines. That review and acceptance have now occurred; the original recommendation was not itself permission to promote. It substantially reduces the repeated raised tubes, rounded caps and regular rib branches identified in `MASTER_MOUNTAIN_QUALITY_AUDIT.md`, while retaining some relatively straight belts. Some presets now have less extensive high-mountain coverage, and macro relief still needs later local terrain detail. The user subsequently accepted that balance.

The comparison survey covers **20 matched pairs**: Balanced, Continental, Archipelago, Mountainous and Vast World, with seeds **-9, 0, 42, 7319**. Every pair has whole-world elevation, first-mainland elevation and mountain-contribution images. Bounds, sampling and colour scales match; no backbone lines are added. Six pairs also have oblique renders of actual sampled elevation, with a shared camera and **12x vertical exaggeration**. These are diagnostic heightfields, not gameplay meshes or erosion simulations. The 192 x 192 oblique grids cannot resolve all finer relief.

## Implementation and compatibility

- `MacroWorldManifest`: explicitly accepts versions 3 and 4, rejects others, and records the chosen version in the immutable saved manifest. At this historical checkpoint, `Create` defaulted to 3 and version 4 required an explicit argument. Existing manifest format/profile data remain intact. There is no world migration.
- `MacroWorldGenerator`: retains the version-3 geography/relief stream and original version-3 mountain sampling branch. Version 4 dispatches only the mountain contribution to `MacroMountainsV4`. Continent/island descriptors, coastlines, land ownership, ocean depths and base hill fields remain identical for matched inputs. Mountains can still be coastal where the unchanged land mask permits them.
- `MacroMountainsV4`: immutable, deterministic descriptors and an allocation-free sampled field. Shape randomness uses separate version-4 streams, local world-origin coordinates and canonical construction order. Feature identities never depend on query order. No world-sized simulation grid or resident chunk cache is introduced.
- `MacroWorldLab`: an explicit stable/experimental selector, version-labelled output and restored selection on Load. Algorithm 3 retains its existing save path; Algorithm 4 uses `macro-world-previews/algorithm-4/<profile>/<seed>/manifest.json`. Save uses the generated world's manifest even if controls contain a draft. At review time Algorithm-4 hydrology views/refinement were disabled pending acceptance; Algorithm-3 tooling is unchanged in capability.
- New `MacroMountainsV4Tests`, registered in the main suite and available through `--mountain-v4`.

The existing 15 coarse and 23 regional fixture files are **unchanged**. No Algorithm-4 drainage, regional or terrain fixture had been pinned at this experimental checkpoint. The current acceptance checkpoint adds those separate baselines. Its current source snapshot/hashes and rendered input manifests are retained in the experiment artifacts. Any further morphology revision must explicitly address experimental saved-world compatibility; do not silently reinterpret accepted saved worlds.

## Morphology and bounds

Each mainland receives a primary belt and zero or more secondary belts within the existing `RangesPerContinent` budget (a maximum in version 4, rather than an exact count). Irregularly spaced controls are sampled with Catmull-Rom curves. Seeded styles include modestly bent belts, stronger curves, broken belts, occasional parallel ridges, and shorter/wider massif envelopes. Secondary branches attach at irregular interior positions, with varied angles and lengths.

Width and amplitude vary along each belt. Smooth end tapering reduces rounded capsules; coherent domain warping changes the footprint. Broad and finer ridged relief produce internal ridges/valleys; broad shoulders provide foothills. One to three localized depressions create passes/saddles, with deeper interruptions for broken belts. Overlapping belts combine through a bounded smooth union. Projection attributes blend near competing path segments, avoiding nearest-segment jumps in width/height. A smooth compact outer support removes hard cutoff steps.

Costs are bounded by at most **8 continents x 6 belts**, with at most **24 segments per current belt** (32-segment scratch capacity) and three passes per belt. Point queries use bounding boxes before path evaluation and reuse the shared relief sample across overlapping belts. No tectonics, erosion, terrain carving, climate or water simulation was added.

## Visual observations

| Preset | Observation across the four seeds |
| --- | --- |
| Balanced | Central belts remain coherent, but tips taper, peaks separate and regular side spurs are reduced. Some broad mountain shoulders are less prominent. |
| Continental | Both almost-linear and bent chains remain possible. Seed -9 retains a relatively straight belt; 42 is a wider, more clustered formation. Several worlds have unbranched ranges. |
| Archipelago | Coast/island silhouettes match exactly. Narrow and clustered mountain footprints replace the repeated smooth central tube; low relief remains appropriately less conspicuous. |
| Mountainous | Strongest improvement in the former rib-like junctions. Seed 42 has a broader, irregular ridge group; 7319 has a bent central chain with subordinate relief. |
| Vast World | The repeated straight trunk with equally recognizable ribs is reduced. Seed 42 is a broken chain; -9 retains a mostly straight coherent segment. Mountains are less conspicuous at full-world scale. |

Remaining tradeoffs: main belts are still seeded around the preserved continent axes; this is not a continental tectonic model. Ridge noise can create sharp crests and locally patterned relief. Passes are terrain features, not guaranteed traversable routes or hydrologically validated sills. Secondary ridges and massif envelopes are procedural approximations. Less mountain coverage changes watershed results in Algorithm 4, as expected; those results were not yet baselined at experiment review time. The known cave/local-water visual consistency issue remains open and untouched.

## Validation

| Check | Result |
| --- | --- |
| New focused A4 suite | **209,365 checks passed** |
| Full core suite | **3,584,886 assertions passed** |
| Existing coarse drainage | **300,985 checks passed**, all 15 frozen worlds retained |
| Existing regional drainage | **41,607 checks passed**, all 23 frozen tiles retained |
| Game build | **0 warnings, 0 errors** |
| Rendered A4 preview | Exit 0; generation, save/load identity, algorithm selection restoration, hydrology guards, zoom and stale-worker rejection passed |
| Rendered default A3 drainage/regional preview | Exit 0; drainage/basin/catchment/region switches, world changes and reload passed |
| Legacy audit comparison | All **60 PNGs + 20 heightfield binaries byte-identical** to the pre-experiment Algorithm-3 audit |
| Change scope | Of 199 existing source/content/test files in the audit, 195 unchanged; only manifest, generator dispatch, lab and test runner changed. Two new source/test files. Profile catalogue and both frozen fixture files hash-identical. |

The new tests include extreme signed seeds, JSON and disk reconstruction, immutable save identity, unsupported versions, query reordering/parallelism, far origins, streaming boundaries, curved-path projection continuity, finite bounded samples, disabled mountains, and maximum supported profile budgets. They check unchanged land/coast/ocean values and base/hill equality with mountains disabled. This is deterministic numerical/visual regression evidence, not a claim that every possible seed is aesthetically accepted.

## Performance observations

Release .NET harness; same first-mainland sample locations, **65,536 queries/pass**, warm median of three passes, then median across the four seeds. Excludes rasterization and file IO. Timings are machine-specific, not an FPS or production streaming guarantee.

| Preset | Algorithm 3 | Algorithm 4 |
| --- | ---: | ---: |
| Balanced | 64.6 ms | 37.8 ms |
| Continental | 31.0 ms | 22.3 ms |
| Archipelago | 82.8 ms | 60.2 ms |
| Mountainous | 118.1 ms | 45.3 ms |
| Vast World | 174.8 ms | 59.6 ms |

Version 4 is faster in this survey despite denser curves: bounded footprint culling, fewer optional belts and squared-distance evaluation reduce work. This does not establish a universal speedup. Warm construction medians were 0.06–0.22 ms. The rendered Debug/Godot A4 Mountainous overview took about **1.06 s** off-thread for a 640 x 640 shaded map; the UI remains responsive while it prepares.

## Historical review material and stop condition

Open [the comparison index](../artifacts/mountain-v4-experiment/README.md). Evidence/logs are in `artifacts/mountain-v4-experiment/`; source snapshots are under its `source/` directory. To test interactively, run `TEST_WORLD_GENERATION.cmd`, select **Algorithm 4 (accepted)**, choose a preset/seed, then Generate. Click/zoom and use Mountain ranges to inspect the contribution. Select Algorithm 3 and Generate for the preserved historical version; normal gameplay is untouched.

**Historical stop condition (superseded by formal acceptance):** after visual/design review, pin version-specific A4 terrain reconstruction fixtures, then create separate A4 coarse/regional hydrology acceptance baselines without overwriting A3 fixtures. Resume the documented regional hydrology acceptance spike (true sills, cross-patch lake connectivity, blocked/downhill corridors and adaptive profiles) only after that terrain decision. Keep finite lake storage and rate-limited river inflow policy. Do not proceed into climate/biomes or local-water realization from this checkpoint automatically.
