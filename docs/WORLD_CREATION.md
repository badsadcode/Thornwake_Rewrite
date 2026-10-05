# World creation

How a Thornwake world is chosen and generated (rewrite, build 8+). Inspired by the *ideas* in Azgaar's Fantasy
Map Generator (world types as a general direction, a 2D map before committing, lots of optional knobs). No Azgaar
code, data or tables are used — see [MASTER_AZGAAR_REFERENCE.md](MASTER_AZGAAR_REFERENCE.md) for what was
looked at and what is deliberately different. The 3D terrain (rivers, banks, coasts, objects) is the same engine
as before; world creation only decides the large-scale shape it starts from.

## The flow (Esc → New world)

1. **World type** — the general direction: *Balanced continents*, *Great mainland*, *Archipelago*, *Mountain
   kingdoms*, *Many small continents*, *Islands + mainland*, the template types *Inland sea*, *Shattered lands*,
   *Rift continent*, *Twin continents*, *Great peninsula* (see *Landform templates*), *Vast continents (classic)*,
   plus any types you saved.
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

## Landform templates (D36)

Azgaar's heightmap templates are a list of steps ("add a high range", "a few hills", "a trough", "a strait", "mask
the edges") that give each map type its character. Thornwake has its own version (own code, own step set): an
optional `Landforms` list in a world type's profile, applied to the large-scale land/height layer **before**
erosion and rivers, so rivers, lakes, coasts and the 3D terrain all follow it. An empty or missing list changes
nothing — the original types are bit-identical (tested).

Built-in template types (pick them in the World type list):

| Type | Template |
| --- | --- |
| **Inland sea** | One big raised landmass, a centre mask makes the inner sea, a strait opens it to the ocean, ranges, an island chain inside. |
| **Shattered lands** | One great landmass cut by 6–9 curving sea channels, faded edges, ranges and hills. |
| **Rift continent** | A continent split by a rift trough with basins (long lakes), walled by two parallel ranges. |
| **Twin continents** | Two raised continents facing each other, a range on each, an island chain between. |
| **Great peninsula** | A mainland with a long, bending, mountainous arm reaching into the ocean, islands off its tip. |

Every value is relative: positions, sizes and lengths are fractions of the world (0 … 1, x to the east, z to the
south), so a template keeps its look from 32 km to 4096 km (tested at 64 and 512 km). Two numbers `[a, b]` are a
range the seed picks from, one number `[a]` is fixed. Lines bend smoothly and vary in width; coasts get noise so
nothing looks geometric. Same seed + same template = same world.

### Steps

| Step | Shape | Default effect |
| --- | --- | --- |
| `Hills` | blobs (`Count`, `Size`) | a little land, rounded uplands (relief 1.6 × hill height) |
| `Raise` | blob, or a long arm if `Length` is set | adds land (0.7) and some height — use `Land` ≈ 2–3 to make a continent from open sea |
| `Lower` | blob / arm | removes land (−0.8), lowers ground |
| `Basin` | blobs | a deep hollow (−2.5 × hill height): rivers fill it as a lake |
| `Range` | bending line; `Size` = × the type's mountain width | mountains (share 1 of the mountain height) with peaks and passes |
| `Trough` | bending line | a long valley or rift (−2.2 × hill height) — long lakes, a river valley |
| `Strait` | bending line | cuts a shallow sea channel through land (never deepens open sea) |
| `Islands` | a chain along a bending line | islands of varied size (`Size` = island radius) |
| `Mask` | `Shape`: `Edges`, `Centre`, `North`, `South`, `East`, `West`; `Size`; `Strength` | fades land away there |

Fields of every step (all optional): `Count`, `X`, `Z` (start of a line / centre of a blob), `Size`, `Length`,
`Angle` (degrees, 0 = east, 90 = south), and effects `Land` (land influence: about 1 = continent interior, below 0 =
sea), `Relief` (× the type's hill height) and `Mountain` (share of the type's mountain height). Unset fields use the
step's defaults. Limits: 32 steps, 1200 shapes, counts up to 400; out-of-range files are rejected with a message.
A type may set `"Continents": 0` when the template builds all the land itself.

Example — a custom world type file with a template (put it in `world_types`, or save any template type and edit it):

```json
{
  "Id": "two-seas", "Name": "Two seas", "Description": "A continent with two inland seas.", "Mainland": true,
  "Profile": {
    "Id": "two-seas", "Name": "Two seas", "WorldSize": 512000, "Continents": 0, "Islands": 20,
    "ContinentRadius": 85000, "IslandRadius": 5000, "CoastRoughness": 0.22, "SeaLevel": 0, "OceanDepth": 2200,
    "LowlandHeight": 260, "HillHeight": 180, "HillScale": 8000, "RangesPerContinent": 0,
    "MountainHeight": 2600, "MountainWidth": 9500,
    "Landforms": [
      { "Step": "Raise", "X": [0.5], "Z": [0.5], "Size": [0.46], "Land": 3.0 },
      { "Step": "Lower", "X": [0.35, 0.4], "Z": [0.4, 0.45], "Size": [0.07, 0.09], "Land": -1.6 },
      { "Step": "Lower", "X": [0.6, 0.65], "Z": [0.55, 0.6], "Size": [0.06, 0.08], "Land": -1.6 },
      { "Step": "Range", "Count": [2, 3], "Length": [0.2, 0.3] },
      { "Step": "Hills", "Count": [4, 6] }
    ]
  },
  "Settings": { "gen.lakes": 1.5 }
}
```

(Profile numbers other than `Landforms` are the same as a built-in type's — copy them from a saved type.)

Next: a **world type editor** — the step list with sliders and the live preview map, instead of editing JSON.

## Natural world types (heightmap templates, D38)

Tom: the land shapes of most types were too round ("blobby"), Vast continents the best; Azgaar's are more
interesting. The new **Natural · …** types build land the way Azgaar's generator does, in Thornwake's own code (the
idea read from his open-source generator; no code or templates copied): a hex grid of 160 × 160 cells with heights
0–100, sea level 20, shaped by a short script. A hill has no outline: its height spreads cell by cell, each
neighbour getting the parent's height to the power 0.985, times a random 0.9–1.1, minus 0.35. The randomness
compounds into ragged coasts, bays and peninsulas. Ranges and troughs follow wandering paths. The grid is then
smoothed, warped and fed into the same large-scale layer as before, so rivers, lakes, erosion, coasts and the 3D
terrain work exactly as on the other types. All the existing types are unchanged (tested by digest).

Natural types: Balanced continents, Great mainland, Archipelago, Mountain kingdoms, Many small continents, Islands +
mainland, Inland sea, Shattered lands, Rift continent, Twin continents, Great peninsula. They keep their shape at
every size (land share tested at 64 km and 1024 km).

### Step language

One step per line in the type's `Profile.Heightmap` list. `a-b` is a range the seed picks from; x, y are percent
of the world (x west→east, y north→south); a fractional count is a chance of one more (1.5 = one, and a second half
the time).

| Step | Values | Effect |
| --- | --- | --- |
| `Hill` | count height x y | raised land growing out from a start cell (height 20 above the sea floor = coast) |
| `Pit` | count depth x y | sunk area (bays, inland seas, lakes); starts on land |
| `Range` | count height x y [x2 y2] | mountain chain along a wandering path (toward x2,y2, else a random direction), with spurs |
| `Trough` | count depth x y [x2 y2] | long valley or rift; deep ones split land |
| `Strait` | width vertical\|horizontal | sea channel across the whole map |
| `Mask` | fraction | fade land toward the edges (3 = gentle); negative fades toward the centre |
| `Add` | value land\|all\|a-b | add to heights in a range (e.g. `Add 13 all` lifts the sea floor near the surface) |
| `Multiply` | factor land\|all\|a-b | scale heights (land is scaled above sea level) |
| `Smooth` | fraction | blend with neighbours (2 = half) |
| `Invert` | probability x\|y\|both | mirror the map |

Example (the Natural · Balanced continents script):

```
Hill 1 60-75 18-32 30-70        big continent in the west
Hill 1 55-70 62-80 25-75        another in the east
Hill 1-2 35-50 35-65 15-85      maybe a third in between
Hill 6-9 18-30 10-90 10-90      lobes, peninsulas, large islands
Pit 3-5 15-25 15-85 15-85       bays and inland seas
Range 4-6 25-40 15-85 15-85     mountain chains
Trough 2-4 15-25 15-85 15-85    valleys, gulfs
Hill 10-16 8-14 5-95 5-95       small islands where they stack
Strait 2-3 vertical             channels between the continents
Mask 3                          keep the world's edge ocean
Smooth 2
```

To make your own: save any natural type as a world type and edit its `Heightmap` lines in the JSON. A world type
may combine a heightmap with `Landforms` steps and with placed continents.

## Later: biomes

Many biomes are planned (see DECISIONS "Planned — Biomes"): climate from latitude, elevation, distance to the sea
and rain shadows picks a blended biome per region; each biome sets terrain style, plants, ground materials, water
colour and atmosphere. Custom biomes will use the same idea as world types: JSON definitions in the user folder,
documented here, with a Thornwake editor later. World types will be able to set a climate range (e.g. a cold
archipelago).
