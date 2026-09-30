# Milestone C1: macro geography and frozen worlds

2026-09-28. Follow-on to the bounded B1/B2 streaming, cave and connected-water experiments.

## Scope and reuse

Reuse `WorldRegion`, `WorldPoint` and `GenerationIdentity`. Preserve generators 1/2, the normal valley, all existing saves and the cave lab. Add a separate versioned macro sampler, frozen profile manifest and an inspectable world-map lab. No gameplay migration, Post-Master expansion, biome promises or river simulation in this milestone.

Profiles define world extent, counts/size ranges of land features, coastal roughness, broad relief and mountain backbone settings. Profiles are content JSON, with no profile-name branches in the generator. A world pins a full immutable profile snapshot and its digest. Reload reads that snapshot, not whichever catalogue entry currently has the same name. Unsupported algorithms or altered snapshots are rejected.

Geography uses continuous global double coordinates and a bounded set of seed-derived land features/mountain backbones. The sampler does not read stream residency or allocate one object per terrain region. Feature identity is scoped by the manifest and independent of sample/load order. The outer finite world blends to ocean; region edges have no geographic effect.

## Acceptance

- Multiple major landmasses plus islands, irregular coasts, connected ranges and broad lowlands in a diagnostic preview.
- Different content profiles and many seeds; deterministic reload, culture independence, far-origin precision, region-border agreement and bounded samples.
- Immutable/atomic manifest creation, profile pinning, malformed/unsupported input rejection and no overwrite of an existing world.
- Map preview with seed/profile selection, elevations, sample readout and zoom; bounded background generation and stale-result rejection.
- Existing core suite, clean build, rendered preview and normal gameplay regression.

## Following gates

C2 adds drainage/basins and explicit local water-body descriptors, then climate/weighted biomes. These must compose with the macro sampler rather than replace old worlds. Regional delta storage/transactions, streamed 3D adapters, LOD and dynamic volumetric promotion remain required before activating this world in normal gameplay. Natural caves/deposits follow generated geography and hydrology; the current cave fixture is not their generator.

## Delivered and verified

C1 is stable as an isolated foundation. Five content profiles include a 2,048 x 2,048 km world. Major cores use bounded placement, islands form groups, coastlines have broad bays/peninsulas plus finer variation, and secondary mountain backbones branch from primary ranges. These are prototype shapes, not an erosion model.

Validation on 2026-09-28:

- Full core suite: 3,032,901 assertions passed. Macro coverage includes 80 profile/seed combinations, 62,159 macro assertions and 296 chunk assertions.
- Chunk reconstruction agrees exactly at tested height/normal borders and after unload/rebuild. Large-coordinate tests use origins billions of metres away; render positions subtract the double origin before converting to float.
- The vast preset has 16,384,000,000 potential 16 m addresses. A test keeps 49 nearby chunks, builds at most two per planner step, and teleports between continents without sampling the intervening world. This is a bounded core test, not a production frame-rate benchmark.
- Game build: zero warnings/errors. Rendered macro smoke passes map selection/zoom, frozen manifest reconstruction, single worker and stale-result rejection. Final vast-world and archipelago captures were inspected; error logs are empty.
- Existing normal-game regression passed all smoke markers; `artifacts/macro-baseline-errors.log` is empty. Existing terrain, cave simulation, saves and gameplay remain on their prior paths.

Evidence: `artifacts/macro-vast.png`, `macro-vast.log`, `macro-archipelago.png`, `macro-archipelago.log`, `macro-baseline.log`. Launch instructions: `WORLD_GENERATION_TEST_GUIDE.md`.

## Limits and version boundary

The preview is not a playable 2,048 km 3D world. The legacy streamed terrain adapter and the new macro chunk sampler are separate tested foundations; production integration remains open. The 640-pixel vast overview samples approximately 3.2 km per pixel and must never become terrain/hydrology authority. Region size does not mandate future mesh, persistence or LOD batch size.

Algorithm 3 now identifies this delivered geography. Subsequent landform changes must use a new algorithm version or preserve a version-3 implementation; a profile digest alone cannot protect against changed code. Unsupported versions currently fail explicitly, rather than regenerate. Cross-runtime/CPU bitwise floating-point identity is not certified. Future networking should exchange authoritative feature/delta state where required.

The expanded Azgaar assessment is in `MASTER_AZGAAR_REFERENCE.md`. It records extension boundaries rather than adding unfinished climate, settlement, traversal, clock or material systems to C1.
