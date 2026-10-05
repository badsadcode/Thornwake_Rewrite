# Biomes — how they work and how to make your own

Phase 1 of `docs/BIOMES_PLAN.md` (decision D42). Biomes are data: no code change is needed to add, change or
remove one.

## Where the files are
* Built-in biomes: `Content/biomes.json` (a list). Built-in asset sets: `Content/asset_sets.json`.
* Your own: the user folder (Windows: `%APPDATA%\Godot\app_userdata\Thornwake\`)
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
* `Props.Rocks`, `Props.Logs` — multipliers.

### Defined now, used in later phases
`Terrain` (Roughness, Flatten, Microterrain, Crags, Features such as `"dunes"`, `"spires"`, `"mesas"`,
`"volcano"`), `Water` (Wetness, WaterTint …), `Atmosphere` (Fog, Mist, AmbientTint, Wind …), `Gameplay`
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
Poplar_A, Pine_A/B/C, Spruce_A/B, Willow_A, Sparse_A). Biomes refer to sets, never to single models, so adding a
model to a set updates every biome that uses it.

## The 14 built-in biomes
Temperate grassland, Temperate forest, Ancient forest (patches in temperate forest and taiga), Taiga, Tundra,
Arctic/Glacier, Alpine, Swamp, Blighted swamp (patches in swamp), Jungle, Desert, Badlands, Savanna, Volcanic
(patches in mountains). Missing assets (palms, cacti, dead trees, vines, swamp plants) use the closest existing
set until art exists.
