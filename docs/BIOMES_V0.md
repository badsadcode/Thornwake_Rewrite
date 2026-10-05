# Biomes v0 — my approach before Noor's spec (kept for reference)

Snapshot: commit `4e45acf` on `rewrite/main` (2026-10-05). Code: `Game/WorldGen/Climate.cs`, `Game/WorldGen/Biomes.cs`,
biome colours in `TileBuilder` / `Terrain.gdshader`, forests in `Vegetation`, grass in `GroundCover`, map in
`WorldPreview.RenderBiomes`, climate knobs in `WorldGenOptions` ("Climate and biomes").

## Idea
Biomes follow the **climate**, and the climate follows the **finished geography** (the world generator keeps
continents, mountains, rivers; biomes never change heights in v0).

1. **Temperature** (annual mean, °C): latitude (the world sits on a planet: centre latitude 45° by default, the
   north edge colder; the latitude range grows with world size, 6° at 32 km … 70° at 4096 km) and height
   (−6.5 °C per km, using the real ground height, so mountains get cold tops).
2. **Precipitation** (mm/year): prevailing winds by latitude band (trade winds from the east below 30°, westerlies
   30–60°, polar easterlies beyond). Air picks up moisture over the sea (~30 km), loses it as rain over land (more
   where it is forced up slopes), cold air holds less. Result: wet windward coasts and mountains, dry rain shadows
   and continental interiors (tested: west coast ~1,300 mm vs ~400 mm 80 km inland).
3. **Biome** = best fit in (temperature, log precipitation) space, Whittaker-style, with soft edges (±2.5 °C, ±20 %)
   and regional jitter (±1.5 °C, ±30 % rain over 6–40 km) so borders wander. The two best biomes blend.
4. **Look**: per-vertex biome grass/dry colours, snow from cold, plant cover (bare ground in deserts), forest amount
   (none in deserts/ice, scattered in steppe/savanna/tundra, dense in rainforest), tree mix (conifer / green /
   autumn broadleaf), grass density.
5. **Data**: biomes are JSON-able records (id, name, temperature and precipitation ranges, colours, forest, conifer
   and autumn shares, grassland, snow, map colour) — the start of custom biomes.

v0 biome list: Ice and glaciers, Tundra, Boreal forest (taiga), Steppe and grassland, Temperate forest, Temperate
rainforest, Mediterranean scrub, Savanna, Tropical forest, Subtropical forest, Hot desert, Cold desert.

Creation screen: Latitude of the centre, Climate range (auto), Warmer/colder (°C), Wetter/drier (×); preview map
"Show biomes".

What v0 did not do: rare/fantasy biomes (no climate reason), region sizes, neighbour rules, overlays (coast, river
banks as modifiers), terrain influence, asset sets, atmosphere, gameplay tags. Those come from Noor's spec
(`docs/design-inbox/NOOR_BIOME_SPEC.md`), merged in `docs/BIOMES_PLAN.md`.
