# Noor's biome spec (received from Tom, 2026-10-05, verbatim)

> Tom: "If that makes any sense and doesn't break world generation we have too much .. Maybe (first let me know what
> You think) we could use that informations from Noor?"  — then: "I will rely on Your wisdom for now".
> Merged plan: `docs/BIOMES_PLAN.md`. My earlier idea: `docs/BIOMES_V0.md`.

---

I want to introduce the initial biome system for Thornwake, but this needs to be designed as a reusable, data-driven system rather than hardcoded biome logic.

IMPORTANT:
Do not treat biomes as the primary owner of world shape.
The world generator should still generate the large-scale geography:
- continents / islands
- mountains
- hills
- valleys
- rivers
- lakes
- coastlines
- erosion
- major terrain structure

Biomes should be layered onto that geography and may influence LOCAL terrain character, but should not independently rebuild the world.

The long-term goal is that the World Site Editor will eventually allow me to create entirely new custom biomes, save them as definitions, and optionally add them to the procedural generation pool WITHOUT changing code.

So please design the biome system around reusable biome definition data/resources.

INITIAL THORNWAKE BIOMES

Please support these as the initial biome set:

1. Temperate Grassland
- rolling meadows
- open terrain
- scattered trees
- farmland-friendly
- gentle hills

2. Temperate Forest
- classic green woodland
- mixed clearings
- streams
- moderate tree density
- gentle to moderately rough terrain

3. Ancient / Dark Forest
- dense old-growth forest
- darker canopy
- moss
- large trees
- atmospheric / mysterious
- less open ground

4. Taiga
- conifer forests
- rocky ground
- cold lakes
- mist
- sparse undergrowth
- colder climate

5. Tundra
- low vegetation
- exposed rock
- windswept open land
- cold
- rolling terrain

6. Arctic / Glacier
- snow
- ice
- glaciers
- frozen lakes
- exposed mountain rock
- very sparse vegetation

7. Alpine
- high elevation
- rocky slopes
- sparse trees
- mountain meadows
- snow possible at higher elevations
- cliffs / steep terrain more common

8. Swamp
- wet lowlands
- mud
- shallow water
- reeds
- islands
- twisted vegetation
- flat or gently rolling terrain

9. Blighted / Horror Swamp
- fantasy / corrupted swamp
- dead trees
- strange water
- fog
- hostile / unsettling vegetation
- dark atmosphere

10. Jungle
- very dense vegetation
- large trees
- vines
- wet valleys
- rivers
- steep ridges / cliffs possible
- strong microterrain variation

11. Desert
- sand
- dunes
- rocky outcrops
- dry riverbeds / wadis
- sparse vegetation
- open visibility

12. Badlands / Barrens
- dry cracked terrain
- mesas
- canyons
- exposed rock
- scrub
- more dramatic erosion

13. Savanna / Dry Grassland
- tall dry grass
- scattered trees
- broad open terrain
- warm and relatively dry

14. Volcanic
- black / dark rock
- ash
- lava-compatible regions
- sharp formations
- sparse hostile vegetation
- dramatic cliffs / ridges

IMPORTANT:
Do NOT make coast, riverbank, lakeshore, floodplain, cliffs, caves, etc. into standalone biomes.

Those should eventually work as environmental overlays / modifiers.

Examples:

Jungle + Coast
Taiga + Coast
Desert + Riverbank
Temperate Forest + Floodplain
Volcanic + Alpine

This is important because I do not want us ending up with hundreds of combinations such as JungleBeach, JungleMountain, JungleRiver, etc.

BIOME DEFINITION STRUCTURE

Please design a generic BiomeDefinition or equivalent data/resource structure.

Conceptually it should be able to contain:

IDENTITY
- unique ID
- display name
- description
- tags

PLACEMENT RULES
- rarity / procedural weight
- minimum region size
- maximum region size
- temperature preference
- moisture preference
- elevation preference
- slope preference
- latitude bias
- coastal bias
- river bias
- neighbouring biome preferences
- forbidden neighbours if needed

TERRAIN INFLUENCE
- local height influence
- roughness
- erosion strength
- hill influence
- mountain influence
- cliff frequency
- microterrain strength
- local noise profile

Important:
These should modify local terrain character rather than replace the primary world-generation system.

Example:
A Jungle biome might increase:
- roughness slightly
- erosion slightly
- ravines
- microterrain

A Swamp might:
- prefer low elevation
- flatten terrain locally
- increase wetness / floodplain affinity

A Badlands biome might:
- increase erosion
- increase exposed rock
- increase canyon / mesa tendency

WATER CHARACTERISTICS
- wetness
- lake affinity
- river affinity
- marsh affinity
- groundwater / saturation bias if useful

SURFACE / MATERIAL RULES
- ground material sets
- rock material sets
- soil material sets
- blending rules

VEGETATION
- tree asset sets
- tree density
- tree size ranges
- bushes
- grass
- flowers
- ground plants
- vines
- moss
- aquatic plants
- dead vegetation

DECORATION / PROPS
- rocks
- boulders
- fallen logs
- stumps
- branches
- biome-specific natural props

ATMOSPHERE
- fog
- mist
- ambient tint
- sky profile
- wind profile
- precipitation bias
- ambient audio profile

GAMEPLAY / ECOLOGY HOOKS
These do not all need implementation now, but the biome format should leave room for them later:
- creature spawn tags
- monster spawn tags
- resource tags
- settlement affinity
- road affinity
- POI tags

CUSTOM BIOMES / WORLD SITE EDITOR

This is critical.

The system should be designed so that later in the World Site Editor I can:

1. Create a new biome
2. Give it a name
3. Define where it likes to generate
4. Define terrain influence
5. Assign ground / rock / soil material sets
6. Assign vegetation sets
7. Assign atmosphere
8. Assign spawn / POI tags
9. Save the biome definition
10. Enable or disable:
   "Include in procedural generation pool"

After that, the world generator should be able to use the new biome without requiring code changes.

BIOME AUTHORING UX

Do not assume every biome needs 50 mandatory parameters.

Most fields should be optional and have sensible defaults.

Eventually the editor should allow a simple authoring flow such as:

Name
Climate
Temperature preference
Moisture preference
Elevation preference
Terrain character
Ground material set
Vegetation set
Vegetation density
Atmosphere
Generation weight

Then an Advanced section can expose the more detailed controls.

ASSET SETS

Very important:
Biome definitions should reference reusable ASSET SETS rather than individual assets wherever possible.

Example:

TemperateForestTrees

That asset set may initially contain:
- 5 tree variants

Later it may contain:
- 20 tree variants

The biome should not need to be edited every time an asset is added to that set.

Same concept for:
- tree sets
- bush sets
- ground plants
- rocks
- boulders
- fallen logs
- flowers
- aquatic plants
- ground materials
- biome props

SYSTEM SEPARATION

Please keep these concerns conceptually separate:

1. BIOME PLACEMENT
Where is this biome allowed or preferred to appear?

2. BIOME APPEARANCE
What makes this biome visually distinct?

3. BIOME TERRAIN INFLUENCE
How does this biome locally modify the terrain?

The world generator remains responsible for the major geography.

The biome layer should add character and variation without destabilising the existing terrain generator.

BIOME TRANSITIONS

Please plan for gradual transitions rather than hard borders.

Biomes should ideally blend using:
- transition masks
- noise
- elevation / moisture interpolation
- vegetation blending
- material blending

I do not want obvious straight biome borders.

However, some biome combinations may naturally create stronger boundaries, such as:
- desert against alpine rock
- volcanic region
- swamp edge
- coastline

But even those should still look organic.

FIRST IMPLEMENTATION SCOPE

For the first pass, please focus on:

- architecture
- biome definition data format
- biome registry / pool
- biome placement system
- smooth biome region generation
- debug visualisation of biome regions
- support for the 14 initial biome definitions
- clean integration with the existing terrain generator

Do NOT try to finish every vegetation, material, gameplay, ecology, weather, or atmosphere feature immediately.

Placeholders / hooks are completely fine.

The goal right now is to establish a clean foundation that can later be expanded through the World Site Editor.

Please also preserve the existing world-generation behaviour as much as practical and avoid rewriting stable terrain-generation systems unless necessary for proper biome integration.
