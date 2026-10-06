# Biomes — how they work and how to make your own

Phase 1 of `docs/BIOMES_PLAN.md` (decision D42). Biomes are data: no code change is needed to add, change or
remove one.

## Where the files are
* Built-in biomes: `Content/biomes.json` (a list). Built-in asset sets: `Content/asset_sets.json`.
* Your own: the user folder (Windows: `%APPDATA%\Thornwake\`)
  * `biomes/*.json` — each file holds one biome or a list of biomes;
  * `asset_sets/*.json` — each file holds one asset set or a list.
* A custom biome with the same `Id` as a built-in one **replaces** it (that is how you tweak a built-in biome).
  Set `"InPool": false` to keep a biome defined but never generated.
* Files are read when the game starts. A broken file is skipped on its own (the log names it and why); the other files still load.

## In game
* **F4** tints the ground by biome; the HUD shows the biome under you (and the one it blends into), temperature
  and rainfall.
* New world → **Show biomes** draws the biome map in the preview. The Climate settings (latitude, latitude span,
  warmth, wetness) shift where biomes go.
* Terrain version "Build 9" (or older) turns biomes off: the world looks exactly like build 9.

## A biome file
Every field is optional except `Id` and `Name`. Example — a red autumn forest that grows in cool, wet hills:

```json
{
  "Id": "red-autumn-forest", "Name": "Red Autumn Forest", "Tags": ["temperate", "forest"],
  "Description": "Maples and birches in permanent autumn colours.",
  "Placement": { "Weight": 0.6, "Temperature": [3, 12], "Moisture": [700, 3000], "Elevation": [100, 1200],
                 "RegionSize": [20, 120] },
  "Appearance": { "Ground": "TemperateGround", "MapColor": "c0582a" },
  "Vegetation": { "Trees": { "AutumnTrees": 1, "BroadleafTrees": 0.2 }, "TreeDensity": 1.2 }
}
```

### Placement (where it generates)
| Field | Meaning |
|---|---|
| `Weight` | rarity: 0.2 rare, 1 normal, 2 common |
| `Temperature` | [min, max] annual mean °C (edges blend over ~3 °C) |
| `Moisture` | [min, max] rain, mm/year (edges blend over ~25 %) |
| `Elevation` | [min, max] metres above sea level |
| `Slope` | [min, max] average ground slope over ~1 km (0.1 = 10 %) |
| `Mountain` | [min, max] 0 lowland … 1 mountain core |
| `Latitude` | [min, max] degrees |
| `Coast`, `River` | −1 avoid … +1 seek the sea coast / rivers and lakes |
| `RegionSize` | [min, max] km — the size of this biome's regions |
| `Patch` | `{ "Within": ["temperate-forest"], "Coverage": 0.15 }` — scattered patches inside host biomes (empty `Within` = any land that suits it) |
| `PreferNeighbours`, `ForbiddenNeighbours` | biome ids it likes next to it / must never touch |

The best three biomes at each spot blend, so borders are soft and organic.

### Appearance, vegetation, props
* `Appearance.Ground` — a ground asset set (colour palette now, real materials later); `RockExposure` 0…1 bare
  rock; `Darkness` 0…1 moodier ground; `MapColor` hex colour on maps and the F4 view.
* `Vegetation.Trees` — tree **sets** with weights; `TreeDensity` (0 none, 1 normal, 2 very dense); `TreeSize`
  [min, max]; `Grass` (0 bare … 1.5 lush).
* Wetland plants: `WaterTrees` (0 … 1: trees standing in the pools, up to ~1.2 m deep — bayou cypress),
  `FloatingPlants` (0 … 1 on the pools; `AquaticPlants` `"Hyacinth"` for water hyacinth mats, otherwise lily pads),
  `Shrubs` (0 … 1 low plants between the pools) with `ShrubKind` `"bog"` (bog shrubs and sedge) or `"tussock"`
  (pale grass clumps).
* `Props.Rocks`, `Props.Logs` — multipliers.

### Rock look
Ground sets name `RockTexture`: `layered`, `granite`, `basalt`, `sandstone`, `mossy` or `icy` (default layered). It is
used on cliffs, crags, rocky ground and boulders, tinted with the set's `Rock` colour.

### Water
`Water.WaterTint` (hex) and `Water.WaterTintStrength` (0 … 1, default 0.65) colour rivers, lakes and pools in the
biome. `Water.Wetness` (0 … 1) darkens and muddies the ground; ≥ ~0.45 gives pools peat beds and muddy margins.

Real standing water (terrain version "current"): `Water.Pools` — share of flat ground under water (0 none, 0.3 a
swamp, 0.45 a marsh, 0.6 a bayou; at most ~0.85); `PoolDepth` — deepest water, m (default 1); `PoolSize` — typical
pool size, m (default 16); `PoolChannels` — 0 round pools … 1 long winding channels. Pools are carved into the
ground with a flat surface, reeds along the edges; they keep away from rivers, lakes, the coast, mountains and slopes.
`Terrain.Flatten` (0 … 1) flattens the walking-scale relief in wetland biomes (and more where there are pools).

### Layout per world
New world → *Biome layout*: realistic (climate) or patchwork (random organic patches, *Patch size*); *Biomes in this
world* toggles leave built-in biomes out. In patchwork, biomes with a high `Elevation` minimum or `Mountain` range go to
mountains, patch biomes keep their rarity (their `Patch.Coverage`).

### Biome skies (option "Biome skies" in Settings → Sky)
`Atmosphere.SkyTint` (hex, multiplies the sky colours; near white, e.g. `f6e8cc` dusty desert) and
`Atmosphere.Haze` (0 … 1: horizon veil and denser distance fog). Off by default (realistic sky).

### Landforms (terrain version "current")
`Terrain.Features` with strengths (0 none … 1 full):
* `"dunes"` — transverse sand dunes, 4–12 m high, ~90 m apart, steep slip faces (desert);
* `"mesas"` — flat-topped uplands 40–110 m high stepped into cliff-sided tiers; rivers cut canyons through them
  (badlands);
* `"spires"` — needle rocks 12–55 m tall (up to 1.5 for a dense forest of needles: Needle Rocks).
Features blend across biome borders. `Terrain.Flatten` (0 … 1) flattens the walking-scale relief.

### Defined now, used in later phases
`Terrain` (Roughness, Microterrain, Crags, Features `"volcano"`, `"canyons"`, `"ravines"`), `Water` (Wetness, WaterTint …), `Atmosphere` (Fog, Mist, AmbientTint, Wind …), `Gameplay`
(Creatures, Monsters, Resources, SettlementAffinity, RoadAffinity, Poi), and `Vegetation` extras (Bushes, Flowers,
Vines, Moss, AquaticPlants, DeadVegetation). Writing them now is fine; they start working as the phases land.

## An asset set file
```json
{ "Id": "RedMaples", "Kind": "trees", "Items": { "Maple_Autumn_A": 3, "Birch_Autumn_A": 1 } }
```
```json
{ "Id": "RedForestGround", "Kind": "ground",
  "Grass": "6b5a35", "Dry": "8a6a40", "Soil": "5a3f2c", "ForestFloor": "4a3220", "Rock": "77766a", "Snow": 0 }
```
Tree item names are the tree models in `Assets/` (Oak_A/B/C, Birch_Green_A/B/C, Birch_Autumn_A/B, Maple_Autumn_A,
Poplar_A, Pine_A/B/C, Spruce_A/B, Willow_A, Sparse_A) and the placeholder plants (Palm_A/B, Cactus_A/B, Dead_A/B,
Acacia_A, Jungle_A, Cypress_A, Shrub_Dry, Bald_Cypress_A/B). Biomes refer to sets, never to single models, so adding a
model to a set updates every biome that uses it.

## The 18 built-in biomes
Temperate grassland, Temperate forest, Ancient forest (patches in temperate forest and taiga), Taiga, Tundra,
Arctic/Glacier, Alpine, Swamp (wooded), Blighted swamp (patches in swamp), Marsh (open grey bog, Dead Marshes
style), Fen (northern bog with winding channels), Bayou (Louisiana: cypress with Spanish moss in brown water), Jungle,
Desert (dunes), Badlands (mesas), Needle Rocks (patches in badlands, desert and savanna), Savanna, Volcanic (patches in
mountains). Missing assets (palms, cacti, dead trees, vines, swamp plants) use the closest existing
set until art exists.
