# World creation

How a Thornwake world is chosen and generated (rewrite, build 8+). Inspired by the *ideas* in Azgaar's Fantasy
Map Generator (world types as a general direction, a 2D map before committing, lots of optional knobs). No Azgaar
code, data or tables are used — see [MASTER_AZGAAR_REFERENCE.md](MASTER_AZGAAR_REFERENCE.md) for what was
looked at and what is deliberately different. The 3D terrain (rivers, banks, coasts, objects) is the same engine
as before; world creation only decides the large-scale shape it starts from.

## The flow (Esc → New world)

1. **World type** — the general direction: *Balanced continents*, *Great mainland*, *Archipelago*, *Mountain
   kingdoms*, *Many small continents*, *Islands + mainland*, *Vast continents (classic)*, plus any types you saved.
2. **Seed** — any number; same seed + same choices = the same world, on every PC.
3. **World size** — 32 × 32 km … 4096 × 4096 km. Size is independent of the type (see *Recipes* below).
4. **Optional parameters** — all start on **Auto** (the type decides). Override only what you care about:
   - World shape: continents, islands, continent size ×, coastline roughness ×, mountain ranges per continent,
     mountain height ×, mountain width ×, broad hills ×.
   - Terrain: hills, plains, rock, coast detail, erosion, shores.
   - Rivers and lakes: rivers, river width, meanders, wander, width variation, banks, lakes, deltas.
   - Forests and objects: forest cover, forest density, rocks, props (these are world data, not graphics options).
   - Terrain version: *current*, *Preview 7*, *Build 5* (old shapes kept for comparison, see SNAPSHOTS.md).
5. **Preview map** — a 2D map of the whole world (sea depth, land height with hill shading, mountains, snow,
   lakes, rivers) with land share and counts. A few seconds for most sizes.
6. **Generate this world** — reuses the previewed plan when nothing changed, so it starts straight away.

**Reset all to auto** puts every parameter back.

## Recipes: world types are relative (D35)

A world type is a *recipe*, not a fixed size. Each built-in type is anchored on a tuned preset at its native size;
at that size with everything on Auto it gives **exactly** that preset (existing worlds and seeds are unchanged —
tested by digest). At other sizes:

- Counts follow **coverage**, not scale: continents and islands grow as size^0.8, and their radii shrink so the land
  share stays roughly the same (tested at 128 km, native and 4096 km). A bigger *Archipelago* gets more islands, not
  giant ones.
- *Mainland* types (Great mainland, Islands + mainland) always keep **one** dominant landmass.
- Mountain ranges narrow (and lower a little) on small worlds so slopes stay walkable; hills and ocean depth scale
  gently.
- Limits: 16 continents, 400 islands, 6 ranges per continent.

Land share of the current presets is about 13–19 %, below the 45–60 % Tom suggested for "balanced". It was left as
is so existing worlds stay identical; *Continent size ×* raises it (1.6 ≈ double the land). A future
"land share" parameter could solve for the radius directly (question in QUESTIONS_FOR_TOM.md).

## Your own world types (D36)

Set up a world the way you like (type, size, parameters), type a name next to **Save as world type** and press it.
The new type appears at the end of the list and behaves like the built-in ones: its saved size becomes its native
size, and at other sizes it adapts with the same recipe rules.

Saved types are JSON files, one per type, in the user folder:

- Windows: `%APPDATA%\Godot\app_userdata\Thornwake\world_types\<id>.json`

They are meant to be edited by hand for deeper tweaks. Format:

```json
{
  "Id": "shattered-isles",
  "Name": "Shattered isles",
  "Description": "Custom world type based on Archipelago, 512 km.",
  "Mainland": false,
  "Profile": {
    "WorldSize": 512000,
    "Continents": 5, "ContinentRadius": 21000,
    "Islands": 150, "IslandRadius": 4100,
    "CoastRoughness": 0.26,
    "RangesPerContinent": 2, "MountainHeight": 1900, "MountainWidth": 6000,
    "HillHeight": 180, "HillScale": 9000,
    "OceanDepth": 900,
    "...": "every field of WorldProfile (see Game/WorldGen/WorldProfile.cs)"
  },
  "Settings": { "gen.rivers": 1.4, "gen.meanders": 1.2, "gen.forest_cover": 0.8 }
}
```

- `Profile` is the full large-scale description (validated on load; a broken file is skipped with a warning).
- `Settings` are the creation-screen parameter values the type starts with (keys as in the settings file:
  `gen.rivers`, `gen.lakes`, `gen.forest_density`, …). World-shape keys are part of the profile instead.
- `Mainland: true` keeps one dominant landmass at every size.
- Delete the file to remove the type.

## Next: landform templates (planned, D36)

Azgaar's heightmap templates are a list of steps ("add a high range", "a few hills", "a trough", "a strait", "mask
the edges") that give each map type its character. Thornwake will get its own version as an optional `Landforms`
list in a world type, applied to the large-scale land/height layer before erosion and rivers — an empty list
changes nothing, so all current types stay identical. Every step is relative to the world size, deterministic per
seed, and bounded so the 3D terrain rules (walkable slopes, coast shapes, river routing) still hold.

Planned step kinds (all positions/sizes as fractions of the world, ranges like `[0.2, 0.4]` pick per seed):

| Step | Effect |
| --- | --- |
| `Range` | A mountain chain between two areas (count, length, width, height, ruggedness). |
| `Hills` | Rounded uplands in an area (count, radius, height). |
| `Basin` | A low inland area that collects a lake (radius, depth). |
| `Trough` | A long valley or rift, can hold a river or a long lake. |
| `Strait` | Cuts a sea channel through land (direction, width). |
| `Raise` / `Lower` | Lifts or sinks land in an area (makes a plateau or a shallow sea). |
| `Mask` | Fades land towards the world edge or the centre (a single continent, or a ring of islands). |
| `Islands` | An island chain along a line (count, radius). |

Example (sketch, not final):

```json
"Landforms": [
  { "Step": "Mask", "Shape": "edges", "Strength": 0.6 },
  { "Step": "Range", "Count": [1, 2], "From": [0.3, 0.5], "To": [0.7, 0.5], "Height": 1.2 },
  { "Step": "Trough", "Count": 1, "Width": 0.02, "Depth": 0.4 },
  { "Step": "Islands", "Count": [8, 14], "Along": "coast", "Radius": 0.004 }
]
```

A **world type editor** (list of steps with sliders and a live preview map) comes after the steps work in JSON.

## Later: biomes

Many biomes are planned (see DECISIONS "Planned — Biomes"): climate from latitude, elevation, distance to the sea
and rain shadows picks a blended biome per region; each biome sets terrain style, plants, ground materials, water
colour and atmosphere. Custom biomes will use the same idea as world types: JSON definitions in the user folder,
documented here, with a Thornwake editor later. World types will be able to set a climate range (e.g. a cold
archipelago).
