# Biomes — plan (2026-10-05)

Merges my first pass (`docs/BIOMES_V0.md`, snapshot `4e45acf`) with Noor's spec
(`docs/design-inbox/NOOR_BIOME_SPEC.md`). Backup point before this work: **Build 9** (`f868ef5`; New world →
Terrain version "Build 9" reproduces its terrain bit for bit, biomes off).

## Principles (agreed)
* The world generator owns geography (continents, mountains, hills, valleys, rivers, lakes, coasts, erosion).
  Biomes are layered on it: placement reads the finished geography + climate.
* Everything is data: biome definitions and asset sets are JSON (built-in + user folder), most fields optional with
  defaults; a biome can be included in or excluded from the procedural pool — no code changes for new biomes.
* Three separate concerns per biome: **placement** (where), **appearance** (looks, plants, props, atmosphere),
  **terrain influence** (local character).
* Coast, riverbank, lakeshore, floodplain, cliffs, caves are **overlays** computed from the geography and applied
  on top of whatever biome is there (Jungle + Coast), never separate biomes.
* Soft, organic transitions: weighted blending of the best few biomes + noise; stronger edges only where nature
  makes them (desert/alpine rock, volcanic, swamp edge, coast) — still noisy.

## Should biomes also shape the terrain? (Tom: ravines, needle rocks like Thousand Needles, canyons, volcanoes…)
**Yes — that is what will make biomes feel like places, not recoloured ground.** But not by letting biomes rebuild
the world: biomes *choose and weight* **landform features**, which are their own reusable, data-driven layer
(like asset sets: a biome says "needle spires 0.6, ravines 0.3"; a custom biome can reuse them). Two kinds,
because they interact with water differently:

1. **Large features that change where water goes** — volcanoes (cones, craters, calderas, lava plateaus), mesas and
   canyons (badlands), glacier-carved U-valleys and cirques (alpine, arctic), broad flat wetlands (swamp). These must
   exist *before* erosion and rivers, or rivers would run through mountains. Pipeline becomes two-pass:
   geography → coarse climate & biome regions → biome landform features on the large-scale layer → erosion,
   rivers, lakes → final climate & biomes. Canyons mostly come from the rivers themselves: in dry, hard-rock biomes
   rivers cut deep narrow channels (stronger incision, steeper walls, rock terracing) instead of carving a shape.
2. **Local features at walking scale that don't change the rivers** — needle spires and hoodoos (Thousand Needles),
   tors, ravines and gullies, dunes, boulder fields, cracked flats, mossy hummocks, karst towers, lava/ash fields.
   Added where the terrain is sampled, masked by the biome's weight, faded out near rivers, coasts and (later) roads.

Limits: the terrain is a height field, so overhangs, arches, real caves and undercut cliffs need the planned
volumetric layer (D31, with caves). Spires work as steep columns up close; far away they need care (LOD).
Safety: every terrain feature is gated by a terrain revision (Build 9 stays bit-exact), has a per-biome strength,
is checked with the map/probe tools and a walkability test (players must be able to cross most terrain).

## Phases
1. **Foundation (now)** — Noor's first-pass scope:
   * `BiomeDefinition` format (identity, placement, appearance, terrain influence, water, surface, vegetation,
     props, atmosphere, gameplay tags), optional fields with defaults; `AssetSet` format (named lists of assets).
   * Registry / pool: built-in definitions (`Content/biomes/*.json`) + user folder, "in pool" flag.
   * Placement: fitness from temperature, moisture, elevation, slope, latitude, coast and river distance × weight;
     region size via noise at the biome's own scale; rare/fantasy biomes (Ancient forest, Blighted swamp, Volcanic)
     as seeded patches inside compatible land; soft neighbour preferences / forbidden neighbours.
   * Smooth blending (best biomes + noise), debug views: 2D biome map, in-game biome tint (key), HUD biome name.
   * The 14 biomes from the spec, appearance wired as far as current assets allow (ground colours, tree density and
     mix, grass, snow, bare rock); missing assets (palms, dead trees, vines, swamp plants) fall back to the closest set.
2. **Overlays** — coast, riverbank, lakeshore, floodplain, cliffs as modifiers (much already exists: beaches,
   banks, reeds, wet ground).
3. **Local terrain influence + first local features** — roughness, flattening (swamp), microterrain; dunes,
   needle spires, ravines, boulder fields.
4. **Large features** — two-pass pipeline; volcanoes, mesas/canyons, glacial valleys, wetland flats.
5. **Atmosphere** — fog, mist, tint, wind, precipitation bias, ambient sound per biome.
6. **Gameplay hooks** — spawn, resource, settlement, road, POI tags used by later systems.
7. **World Site Editor** pages for biomes, asset sets and landform features.
