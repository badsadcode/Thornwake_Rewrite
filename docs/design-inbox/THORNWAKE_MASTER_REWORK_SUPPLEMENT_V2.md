# THORNWAKE MASTER REWORK SUPPLEMENT V2
## World Detail, Geology, Underground Biomes, Caves, and Data-Driven Environment Authoring

### Status
Draft implementation supplement for the Thornwake Master Rework.

### Purpose
This supplement extends the world-generation foundation established by Supplement V1.

Supplement V1 is responsible for the large-scale world foundation: terrain, mountains, valleys, hydrology, drainage, climate inputs, and the biome foundation.

Supplement V2 is responsible for turning that generated terrain into a believable, varied fantasy world by adding:

- data-driven biome definitions and biome variants
- environmental scatter and world dressing
- micro-biomes and local variation
- geology and resource distribution
- caves and underground generation
- underground biome families and variants
- cave decoration
- weather tendencies by climate and biome
- atmospheric and environmental effects such as fog, mist, blowing snow, haze, spray, spores, and local particles
- cold-region, arctic, tundra, glacial, frozen-water, and ice-cave support
- terrain traversal character and naturally difficult navigation regions
- dynamic world-route network hooks for existing and future player-built roads
- wildlife, domestic animals, and monster population definitions
- creature territories, nests, lairs, and spawn policies
- sandbox/server population and respawn controls
- virtual offscreen populations and traveling groups
- lightweight offscreen settlement/world events
- World Site Editor support for creating and editing all of the above

The goal is not scientific simulation for its own sake. The goal is a world that feels coherent, varied, explorable, and easy to expand with new content.

---

# 1. Core Design Philosophy

## 1.1 Build systems that are fed with data

The generator and runtime systems should understand generic concepts such as:

- biome definitions
- spawn rules
- environment assets
- resource deposits
- cave biome definitions
- weather profiles
- decoration sets
- placement constraints

They should not contain large amounts of hard-coded logic for specific content.

Bad direction:

```text
if biome == Swamp:
    spawn_reeds()
    spawn_moss()
    spawn_swamp_rocks()
```

Preferred direction:

```text
BiomeDefinition
    -> EnvironmentSets
    -> SpawnRules
    -> ResourceSets
    -> WeatherProfile
```

The generator evaluates the supplied definitions.

The same system should be able to generate a normal forest, a fantasy fungal forest, a dry highland, or a sulfur cavern without requiring a new generator for each one.

---

## 1.2 Deep underneath, simple for the player

World generation may use:

- moisture
- temperature
- altitude
- slope
- geology
- depth
- drainage
- proximity to water
- regional climate
- biome adjacency

These values exist primarily to create believable generation.

They should not automatically become player-facing management systems.

Example:

Humidity can help determine where moss, wetlands, fungi, wet caves, and certain vegetation appear.

The player does not need to manage a visible "humidity percentage" while farming.

For farming, the intended player experience should remain closer to:

```text
plant seed
-> water if needed
-> optionally fertilize
-> notice obvious problems
-> treat disease or pests if they appear
-> harvest
```

Do not turn ordinary gameplay into an environmental science simulator.

### Principle

> Simulate enough to create interesting outcomes. Expose only the decisions that are fun.

---

## 1.3 Fantasy is allowed to override realism

The world should be coherent, but not imprisoned by real-world geology or ecology.

Examples that should remain possible:

- glowing crystal caverns
- giant underground roots
- strange fungal caves
- magical mineral formations
- unnatural ancient ruins
- fantasy storms
- unusual resource pockets
- corrupted forests
- ash valleys
- impossible but visually compelling cave chambers

The generation rules exist to provide structure and consistency, not to prevent fantasy.

---

# 2. Relationship to Supplement V1

Supplement V2 should begin only after the important Supplement V1 world-generation foundation is stable enough to depend upon.

Supplement V1 should expose or preserve clean hooks for:

```text
BiomeEvaluation
EnvironmentScatter
GeologyDistribution
ResourceDistribution
CaveGeneration
UndergroundBiomeEvaluation
WeatherProfiles
AtmosphereProfiles
ColdRegionFeatures
TraversalCharacter
WorldRouteNetwork
CreaturePopulation
VirtualWorldPopulation
TravelingGroups
WorldEvents
WorldSitePlacement
```

Supplement V1 does not need to fully implement these systems.

It must simply avoid architecture that prevents them.

---

# 3. Data-Driven Biome System

Biomes should be definitions, not hard-coded generator branches.

A biome definition may contain:

```text
BiomeDefinition

Id
DisplayName
BiomeFamily
Tags

GenerationRules
    TemperatureRange
    MoistureRange
    AltitudeRange
    SlopeRange
    TerrainTags
    GeologicalTags
    DistanceFromWater
    RegionalClimateTags
    Rarity
    MinimumPatchSize
    MaximumPatchSize
    Clustering
    NeighborPreferences
    NeighborRestrictions

EnvironmentSets
VegetationSets
ResourceSets
WeatherProfile
GroundMaterialSet
AmbientProfile
PossibleUndergroundBiomeLinks
```

Not every field should be mandatory.

Definitions should have sensible defaults.

The editor should make simple biome creation easy while allowing advanced rules when needed.

---

# 4. World Site Editor: Biome Authoring

The World Site Editor should be expanded so new biomes can be created without editing generator code.

The user should be able to:

- create a new biome definition
- assign biome family and tags
- define where it may generate
- select vegetation sets
- select grass and ground-cover sets
- select rock and boulder sets
- assign environmental scatter
- assign resource distribution sets
- assign weather tendencies
- define neighboring-biome preferences
- define rarity
- define patch-size ranges
- define terrain preferences
- preview/test the biome on generated terrain
- save it as reusable data

Example:

```text
Rocky Pine Highlands

Altitude:
    Medium to High

Moisture:
    Low to Medium

Slope:
    Moderate

Vegetation:
    Pine
    Sparse bushes
    Hardy grass

Environment:
    Granite boulders
    Loose stones
    Fallen pine logs

Resources:
    Iron uncommon
    Copper rare

Weather:
    Windy
    Moderate rain
    Occasional storms
```

Adding this biome should not require a new generator class.

---

# 5. Environment Asset Definitions

Environmental content should use reusable asset definitions.

Example:

```text
EnvironmentAssetDefinition

Id
DisplayName
Category
Tags

VisualAsset
CollisionProfile
InteractionProfile

PlacementRules
    ValidBiomeTags
    InvalidBiomeTags
    AltitudeRange
    MoistureRange
    TemperatureRange
    SlopeRange
    DistanceFromWater
    SurfaceTags
    UndergroundOnly
    CaveSurfaceType
    DepthRange

ScatterRules
    Density
    MinSpacing
    ClusterChance
    ClusterSize
    ScaleVariation
    RotationVariation

Gameplay
    DecorativeOnly
    Harvestable
    ResourceDefinition
    YieldRules
```

The goal is one generic system capable of placing many different types of environmental content.

---

# 6. Environmental Scatter System

The world needs a dedicated scatter layer.

Examples include:

## Ground vegetation
- grass
- tall grass
- flowers
- ferns
- reeds
- moss
- mushrooms
- small shrubs
- brambles
- saplings
- ground-cover plants

## Forest clutter
- fallen branches
- dead trees
- fallen logs
- stumps
- exposed roots
- leaf litter
- bark pieces
- cones
- acorns

## Geological clutter
- pebbles
- stones
- small rocks
- large rocks
- boulders
- scree
- rubble fields

## Water-edge clutter
- reeds
- driftwood
- mud patches
- wet stones
- marsh plants

## Fantasy clutter
- strange crystals
- magical plants
- corrupted growth
- bones
- ancient debris
- unusual fungi

Scatter should use rules rather than uniform random placement.

---

# 7. Visual Scatter vs Interactive Resources

Not every decorative object should become a full gameplay entity.

This distinction is important for performance.

## Visual Scatter

Cheap, mostly cosmetic content.

Examples:

- tiny pebbles
- grass blades
- moss
- tiny mushrooms
- leaf litter
- small rubble
- decorative cave growth

Prefer efficient instancing and lightweight rendering.

## Interactive Scatter

Gameplay objects that can be harvested or manipulated.

Examples:

- berry bushes
- branches
- fallen logs
- harvestable vines
- large rocks
- resource nodes
- ore outcrops

## Landmark Scatter

Large memorable formations or unusual objects.

Examples:

- giant boulders
- ancient trees
- rock arches
- unusual cave formations
- enormous roots
- magical crystal formations

These can be much rarer and may participate in POI or world-site generation.

---

# 8. Micro-Biomes and Local Variation

Large biomes should not look uniform.

Supplement V2 should support local environmental patches inside a parent biome.

Examples:

```text
Temperate Forest
    -> Fern Hollow
    -> Mossy Boulder Field
    -> Wet Depression
    -> Fallen Tree Pocket
    -> Dense Bramble Patch
    -> Sparse Old-Growth Pocket
```

Micro-biomes may modify:

- scatter density
- vegetation sets
- ground materials
- resource chances
- ambient effects
- moisture appearance
- local landmark chances

They should not require completely separate world-generation systems.

A micro-biome should behave like a local modifier or sub-definition.

---

# 9. Geology System

The world should have a lightweight geology layer used mainly for:

- resource distribution
- rock appearance
- cave variation
- terrain dressing
- biome variation
- mining

The system does not need to simulate real geology in detail.

It needs enough structure to produce consistent and interesting results.

Possible geological tags:

```text
Granite
Basalt
Limestone
Sandstone
ClayRich
Volcanic
Sedimentary
Crystalline
MixedRock
```

These are gameplay/generation tags, not a scientific model.

---

# 10. Resource and Deposit System

A resource material is not the same thing as a deposit.

Example:

```text
CopperVein
    -> harvesting produces CopperOre

CopperOre
    -> smelting produces Copper
```

This distinction should exist from the beginning.

---

## 10.1 Generic Deposit Definition

All mineral deposits should use one generic framework.

```text
DepositDefinition

Id
DisplayName
Category
Tags

YieldResource
YieldRange

GenerationRules
    ValidGeologyTags
    ValidBiomeTags
    SurfaceChance
    UndergroundChance
    CaveExposureChance
    DepthRange
    AltitudeRange
    Rarity

Formation
    DepositType
    SizeRange
    LengthRange
    ThicknessRange
    ClusterRules

Gameplay
    RequiredToolClass
    MinimumToolTier
    Hardness
```

Possible deposit types:

```text
Loose
Node
Cluster
Vein
Layer
Pocket
Outcrop
```

Do not build separate systems for copper, sulfur, iron, quartz, and other materials.

They are data using the same deposit system.

---

# 11. Initial Geological Resource Set

Keep the starting set reasonably small but versatile.

Recommended initial resources:

## Metals and ores
- iron ore
- copper ore
- tin ore

These support:

- iron
- copper
- bronze
- later steel

## Minerals and industrial materials
- sulfur
- coal
- salt
- limestone
- quartz
- clay
- sand

## Stone families
Stone types may exist primarily as geological/building materials rather than "ores".

Examples:

- granite
- basalt
- limestone
- sandstone

The exact stone list can remain small initially.

## Optional later resources

The generic system should allow easy addition of:

- silver
- gold
- lead
- gems
- magical minerals
- rare crystals
- fantasy ores

These should not be required for Supplement V2 completion.

---

# 12. Sulfur

Sulfur should be included in the initial data model now rather than retrofitted later.

Possible uses later:

- alchemy
- chemical crafting
- preservation
- specialized crafting
- explosives if the design eventually requires them
- fantasy recipes

Example generation profile:

```text
Sulfur Deposit

Preferred:
    volcanic geology
    dry mineral caves
    deep rocky caverns

Possible:
    cave walls
    cave floors
    rare surface deposits

Formation:
    pocket
    cluster
    vein
```

The exact gameplay uses can be implemented later.

---

# 13. Cave Generation System

Caves should be treated as a real part of world generation, not merely decorative holes.

The cave system should generate:

- entrances
- tunnels
- chambers
- vertical connections where appropriate
- cave networks
- depth variation
- water interaction
- exposed geology
- resource exposure

The system should support both small caves and larger networks.

Do not create a separate generator for every cave type.

Preferred structure:

```text
CaveGenerator
    +
CaveBiomeDefinitions
    +
DecorationSets
    +
Geology
    +
ResourceDistribution
```

---

# 14. Underground Biome System

Caves should use an underground biome family.

Examples:

- shallow root cave
- wet limestone cave
- dry limestone cave
- deep stone cavern
- sulfur cavern
- volcanic cave
- crystal cavern
- fungal cave
- flooded cave
- ancient-root cavern

These should use data definitions.

Example:

```text
UndergroundBiomeDefinition

Id
DisplayName
Tags

GenerationRules
    DepthRange
    GeologyTags
    MoistureRange
    TemperatureRange
    WaterProximity
    Rarity

ShapeProfile
DecorationSets
ResourceSets
CreatureSets
AmbientProfile
LightingProfile
WaterProfile
```

---

# 15. Cave Regions and Biome Transitions

One cave network should be allowed to contain several underground biome regions.

Example:

```text
Root Cave
    ->
Wet Limestone Cavern
    ->
Deep Stone Cave
    ->
Sulfur Pocket
```

This creates meaningful underground exploration without requiring multiple disconnected cave systems.

Biome transitions should be visually readable where practical.

Possible transition cues:

- rock material changes
- increasing moisture
- different formations
- new fungi
- mineral staining
- roots disappearing
- ambient sound changes
- different light behavior

---

# 16. Cave Decoration System

Cave decoration should use placement categories.

## Floor
- rocks
- rubble
- stalagmites
- mushrooms
- bones
- puddles
- mineral deposits
- fallen roots

## Wall
- exposed ore veins
- mineral staining
- moss
- fungi
- cracks
- roots
- crystals
- vines

## Ceiling
- stalactites
- roots
- hanging vines
- fungi
- dripping formations

## Large formations
- columns
- crystal clusters
- rock arches
- collapsed chambers
- giant roots
- fantasy formations

Decoration definitions should reuse the same general environment-asset system wherever practical.

---

# 17. Caves and Resource Discovery

Caves should naturally expose underground resource systems.

An ore vein may exist below ground independently of caves.

If a generated cave intersects the vein, part of the vein becomes exposed.

Example:

```text
Copper vein generated underground
    ->
cave tunnel intersects vein
    ->
visible copper appears in cave wall
```

This allows caves to become natural discovery routes into mining systems.

It also makes geology and exploration feel connected.

---

# 18. Underground Water

Supplement V2 should support basic underground water relationships.

Possible features:

- wet cave floors
- puddles
- underground pools
- flooded chambers
- cave streams where feasible
- dripping-water decoration
- connections to surface hydrology where architecture reasonably allows

Do not implement full fluid simulation unless already justified by another system.

Water should remain practical and performance-conscious.

---

# 19. Weather Profiles

Biomes and regional climates may influence weather tendencies.

Weather should not be rigidly tied to a biome.

Preferred hierarchy:

```text
RegionalClimate
    ->
BiomeWeatherModifiers
    ->
CurrentWeatherSystem
```

Example tendencies:

## Temperate forest
- moderate rain
- occasional storms
- fog possible

## Swamp
- frequent rain
- frequent fog
- thunderstorms more likely

## Dry grassland
- less rain
- stronger dry winds
- occasional intense storms

## Mountains
- stronger wind
- colder conditions
- snow more likely where appropriate
- fast weather changes

## Coast
- windier
- sea fog
- rain fronts more likely

The weather system should use probabilities and modifiers.

Avoid scripted assumptions such as "swamps always rain".

---

# 20. World Site Editor: Weather Profiles

The editor should eventually allow biome/weather profile editing.

Example fields:

```text
WeatherProfile

RainWeight
HeavyRainWeight
StormWeight
FogWeight
SnowWeight
WindWeight
ClearWeight

IntensityRange
DurationRange
TransitionSpeed
SeasonModifiers
```

These values should remain authoring data.

Players should not need to understand these numbers.

---

# 21. World Site Editor: Environment and Resource Authoring

The editor should support creating and editing:

## Environment assets
- models
- categories
- scale variation
- rotation
- collision
- scatter density
- placement rules
- biome tags
- harvestability

## Resource deposits
- resource output
- deposit type
- rarity
- valid geology
- depth
- surface/cave/underground rules
- vein size
- tool requirements

## Surface biomes
- generation rules
- environment sets
- resources
- weather profile
- neighboring biome behavior

## Underground biomes
- depth
- geology
- cave shape profile
- decorations
- resources
- water
- ambience

## Micro-biomes
- parent biome compatibility
- patch size
- local modifiers
- replacement/addition rules

---

# 22. Preview and Validation Tools

The editor should eventually provide test tools so content can be tuned quickly.

Useful preview options:

- generate a sample biome chunk
- generate several random seeds using one biome
- preview scatter density
- preview resource distributions
- preview cave biome decoration
- preview underground transitions
- visualize placement-rule failures
- show why an asset is or is not valid in a selected area

The editor should help author content without requiring code changes.

---

# 23. Simplicity Requirements

The data-driven system must not become painful to use.

Rules:

1. Most fields must have sensible defaults.
2. Simple assets should require only a few settings.
3. Advanced controls should be optional.
4. Reusable presets should be supported.
5. Related rules should be grouped visually.
6. Content definitions should support inheritance or reusable sets where useful.
7. Avoid requiring dozens of mandatory parameters just to place a bush.

Example:

A basic bush should be creatable with something close to:

```text
Model
Category = Bush
BiomeTags = TemperateForest
Density = Medium
Harvestable = No
```

Everything else can default.

---




# 24. Terrain Materials and Biome Texturing

Terrain texturing must be treated as a reusable data-driven system rather than as one hardcoded texture per biome.

The visual terrain system should combine:

- reusable base terrain materials
- biome/regional material palettes
- procedural blending rules
- slope/elevation/environment inputs
- wetness and snow overlays
- large-scale color variation
- road/path influence
- terraforming/exposed-surface state

Avoid hard texture borders between biomes.

A biome should reference a reusable terrain-material set rather than owning one unique monolithic texture.

Example:

```text
TemperateForestMaterialSet

BaseMaterials:
    Grass
    Dirt
    Mud
    Moss
    Granite
    Gravel

Rules:
    FlatOpenGround -> Grass
    NearWater -> Mud / WetSoil
    MoistShadedGround -> Moss
    SteepSlope -> ExposedRock
    RoadInfluence -> Dirt / Gravel
```

The exact blend should emerge from terrain and environmental data.

---

## 24.1 Base terrain material definitions

Each reusable terrain material should support physically useful surface data.

Example:

```text
TerrainMaterialDefinition

Id
DisplayName

Textures:
    Albedo
    Normal
    Height
    Roughness

Optional:
    DetailNormal
    DetailMask
    SpecialMask

Properties:
    TilingScale
    NormalStrength
    HeightStrength
    RoughnessBias
    WetnessResponse
    SnowResponse
    TintResponse
```

Not every material needs every optional map.

The system should provide sensible defaults.

---

## 24.2 Texture packing and arrays

Do not assume every terrain material requires a completely separate shader/material instance.

Prefer texture arrays and packed channels where practical.

A recommended conceptual packing scheme is:

```text
Texture Array A:
    RGB = Albedo
    A   = Height

Texture Array B:
    RGB = Normal
    A   = Roughness
```

The exact runtime implementation may be adapted to the existing Godot terrain architecture.

The important goals are:

- reduce sampler pressure
- reduce material switching
- keep terrain materials easy to batch
- allow many reusable terrain-material definitions
- avoid loading a unique shader/material for every biome

All textures placed in the same array should use compatible dimensions, formats, and mipmap settings.

---

## 24.3 Normal maps

Terrain materials should support proper normal maps.

Use a consistent project convention.

Preferred convention:

```text
NormalMapConvention = OpenGL / +Y
```

Asset validation should catch incompatible normal-map orientation where practical.

The system should allow per-material normal strength.

Normal maps are especially important for:

- stone
- gravel
- dirt
- mud
- bark-like ground debris
- ice
- snow crust
- compacted paths
- rocky terrain

---

## 24.4 Height / bump information

Height information may be used for:

- height-aware material blending
- improved grass/dirt/rock transitions
- breaking up hard blend edges
- local surface relief
- optional parallax/detail effects if later justified

Height maps should not replace actual terrain geometry.

Terrain shape remains controlled by Thornwake's real terrain system.

Do not use texture height data as a substitute for mountains, cliffs, digging, or terraforming geometry.

---

## 24.5 Roughness and surface response

Roughness should be part of terrain-material data.

Examples:

- dry dirt -> relatively rough
- wet mud -> lower roughness / stronger reflections
- polished ice -> low roughness
- dry stone -> medium/high roughness
- wet stone -> reduced roughness
- snow -> soft diffuse response with material-specific variation

Where practical, roughness should be packed with another terrain map rather than stored as an additional standalone texture.

---

## 24.6 Dynamic wetness

Wetness should usually be a dynamic environmental state rather than a completely separate material for every wet variant.

Possible wetness inputs:

- active rain
- recent rain
- water proximity
- swamp/wetland state
- puddles
- flooding/submersion
- cave dripping zones
- river/lake shoreline proximity

Wetness may modify:

- albedo darkness
- roughness
- specular response
- normal/detail intensity
- optional localized wet masks

Example:

```text
BaseMaterial = Dirt
Wetness = 0.0 -> dry dirt
Wetness = 0.8 -> dark wet dirt / reduced roughness
```

Individual materials should define how strongly they respond to wetness.

Example:

```text
WetnessResponse:
    Grass = Moderate
    Dirt = High
    Mud = VeryHigh
    Granite = Medium
    Sand = LowToMedium
```

Dedicated wetness masks may exist where useful, but should not be mandatory for every terrain material.

---

## 24.7 Material identity vs appearance profile

Terrain material identity and regional appearance should be separate concepts.

Example:

```text
MaterialIdentity:
    Grass

AppearanceProfile:
    TemperateGreen
```

The same base material may appear differently across climates or regions.

Possible appearance controls:

- hue shift
- saturation
- brightness/value
- tint
- roughness bias
- normal-strength bias
- wetness-response modifier
- snow-response modifier

This allows one grass material to support several looks without requiring a separate full texture set for each biome.

---

## 24.8 Controlled hue and value variation

Use controlled tinting and macro variation to create visual diversity.

Examples:

### Temperate forest grass
- medium green
- moderate saturation
- slightly darker in shade

### Dry grassland
- warmer/yellower hue
- lower saturation
- slightly brighter dry patches

### Cold tundra
- cooler/desaturated hue
- gray-green tendency

### Swamp grass
- darker green
- slightly muted
- stronger wetness response

### Regional dirt
The same dirt family may become:

- warmer/redder
- colder/gray
- pale and dry
- dark and wet

Do not use hue shifting to fake fundamentally different surface structures.

Tinting may vary grass, dirt, sand, rock, moss, and similar families, but mud should not merely be "brown grass" and gravel should not merely be "gray dirt".

Use new base material textures when the physical surface structure genuinely differs.

---

## 24.9 Macro variation and anti-tiling

Large terrain must not look like a small texture repeated thousands of times.

The terrain shader should support large-scale breakup.

Possible techniques include:

- macro color noise
- large-scale tint variation
- low-frequency roughness variation
- randomized UV rotation where practical
- detail-scale blending
- distance-aware detail reduction
- biome/regional tint variation
- slope/environment-driven breakup

Conceptually:

```text
FinalTerrainAppearance =
    BaseMaterial
    + DetailNormal
    + MacroColorVariation
    + BiomeTint
    + Wetness
    + Snow
    + LocalSurfaceInfluence
```

The implementation should remain performance-conscious.

---

## 24.10 Biome material sets and regional palettes

The project may contain dozens of reusable terrain materials globally.

A single biome or region should normally use only a limited subset.

Example:

```text
GlobalTerrainMaterialLibrary
    ->
BiomeMaterialSet
    ->
RegionalActivePalette
    ->
TerrainShader
```

Example biome palettes:

```text
TemperateForest:
    Grass
    Dirt
    Mud
    Moss
    Granite
    Gravel

Glacier:
    Snow
    Ice
    Granite
    Gravel

DryHighlands:
    DryGrass
    Dirt
    Gravel
    ExposedRock
    Sandstone
```

This keeps the system scalable without requiring every terrain draw to sample every material in the project.

---

## 24.11 Local blend limits

Avoid blending large numbers of materials at every pixel.

Prefer a limited local blend.

Conceptually:

```text
BaseMaterial
    +
SecondaryMaterial
    +
EnvironmentalOverlay
```

Examples:

```text
Grass + Dirt + Snow
Mud + Moss + Wetness
Rock + Gravel + Snow
Sand + Stone + Wetness
```

More complex blends may be allowed when justified, but the default should remain small and predictable.

The exact number should be determined by profiling the real Godot terrain implementation rather than by an arbitrary hardcoded theoretical maximum.

---

## 24.12 Roads, paths, and disturbed ground

Roads and paths should participate in the same terrain-material system.

Possible road/path influences:

- dirt
- packed dirt
- gravel
- stone
- mud
- snow-covered road
- wet road

Road material should blend naturally into neighboring terrain rather than appearing as a disconnected decal where possible.

Future player-built roads should be able to modify local terrain-material state.

---

## 24.13 Terraforming compatibility

Terrain texturing must respect mutable terrain.

When terrain is dug, cut, raised, flattened, or otherwise modified, exposed surfaces should receive sensible materials.

Examples:

```text
Dig through grass:
    top surface -> grass
    newly exposed shallow soil -> dirt
    deeper cut -> rock/geology-dependent material
```

Possible inputs:

- depth below original terrain surface
- local geology
- soil layer
- moisture
- water exposure
- slope
- biome

Do not assume the original surface material remains valid after excavation.

Do not tie surface appearance so tightly to the original generated biome mask that player-modified terrain cannot update correctly.

---

## 24.14 Snow and ice overlays

Snow should preferably behave as an environmental overlay or state where practical.

Possible inputs:

- temperature
- elevation
- season later
- current weather
- persistent cold-region rules
- slope
- shelter/exposure

This allows:

```text
Grass + Snow
Rock + Snow
Road + Snow
Mud + Snow
```

without requiring a unique full material for every snow-covered combination.

Ice should remain a distinct material where its physical surface structure differs.

---

## 24.15 Initial terrain-material vocabulary

Keep the first implementation deliberately small.

Recommended initial families:

- grass
- dry grass
- dirt
- mud
- wet soil
- sand
- gravel
- generic stone
- granite
- limestone
- basalt
- moss
- snow
- ice
- clay
- ash if volcanic regions are present

Not all of these require unique implementation logic.

They should be data using the same material system.

Additional materials can be added later through the editor/data layer.

---

## 24.16 Terrain material authoring in the World Site Editor

The editor should eventually allow:

- creating a terrain material definition
- assigning texture maps
- selecting normal-map convention
- setting tiling scale
- setting normal/height/roughness strength
- setting wetness response
- setting snow response
- defining allowed tint range
- creating biome material sets
- assigning regional appearance profiles
- previewing blends
- previewing wetness
- previewing snow
- validating texture-array compatibility

The editor should provide sensible defaults so adding one new terrain material does not require configuring dozens of fields.

---

## 24.17 Performance and technical limits

Do not define a fixed global terrain-texture-count limit purely from theory.

The practical limit depends on:

- Godot renderer behavior
- texture-array implementation
- texture resolution
- texture compression
- mipmaps
- number of samples per pixel
- number of simultaneously blended layers
- target GPU memory
- shader complexity
- terrain draw strategy

The architecture should therefore optimize for:

- reusable material definitions
- texture arrays where appropriate
- packed maps
- limited active regional palettes
- limited local blends
- mipmaps
- suitable GPU texture compression
- profiling on real target hardware

World size by itself should not require duplicating base material textures.

A grass material may repeat across a continent while remaining one reusable texture resource.

The major scaling concerns are control data, streamed terrain state, geometry, vegetation, and shader sampling cost, not the physical world area covered by a texture.

---

## 24.18 Terrain texturing scope guardrail

Supplement V2 should establish a robust terrain-material architecture.

It should not become a research project into physically perfect material simulation.

The goal is:

- visually rich terrain
- smooth biome transitions
- convincing wetness
- good normal/detail response
- believable snow/ice
- low visible tiling
- compatibility with terraforming
- scalable material authoring

Prefer a small, versatile material library with controlled variation over hundreds of nearly identical bespoke terrain textures.

---

# 25. Atmosphere and Environmental Effects

Weather rules describe large-scale conditions, but the world also needs a separate atmosphere/environmental-effects layer that makes regions feel distinct.

The system should support three related sources.

## 25.1 Regional and biome atmosphere

Examples:

- ground fog
- morning mist
- swamp haze
- sea fog
- mountain cloud
- dust haze
- ash haze
- pollen
- drifting spores
- persistent magical motes
- cold vapor
- wind-blown particles

Biome and regional climate definitions should specify which atmospheric effects are possible and how common they are.

Example:

```text
SwampAtmosphereProfile

GroundMist = Common
DenseFog = Occasional
Insects = Common
RainHaze = Common
Pollen = Rare
```

## 25.2 Weather-driven effects

Current weather may modify or add effects such as:

- rain streaks
- heavy-rain haze
- thunderstorm darkness
- wind-blown leaves
- snow
- blowing snow
- blizzard visibility loss
- whiteout
- hail if later desired
- stronger cloud/fog transitions

Weather should modify the atmosphere rather than replacing the biome identity.

## 25.3 Local environmental effects

World features may add localized effects independently from regional weather.

Examples:

### Waterfalls
- spray
- mist
- wet surfaces
- localized ambient sound

### Rivers and lakes
- morning surface mist
- shoreline haze

### Caves
- dripping water
- dust
- spores
- steam
- localized fog
- sulfur haze
- cave particles

### Forests
- pollen
- drifting leaves
- fireflies
- floating motes

### Volcanic or sulfur regions
- steam vents
- ash
- sulfur fumes
- heat haze where appropriate

### Fantasy regions
- magical particles
- glowing motes
- unnatural fog
- corruption haze

The atmosphere system should use generic effect definitions rather than separate bespoke systems for every biome.

---

# 26. Atmosphere Profile Definitions

Example:

```text
AtmosphereProfile

Id
DisplayName

AllowedEffects
DefaultWeights

Conditions
    TimeOfDay
    WeatherTags
    SeasonTags
    TemperatureRange
    MoistureRange
    AltitudeRange

IntensityRange
VisibilityModifier
WindResponse
LocalOnly
```

Not every field should be mandatory.

The player should experience the result visually and audibly.

Do not expose atmospheric simulation values as normal gameplay management data.

---

# 27. Cold, Arctic, and Glacial Region Support

The world-generation system must support genuinely cold-region terrain families rather than treating them as normal biomes with a snow texture.

Possible region/biome types include:

- tundra
- arctic coast
- snowfield
- high-altitude alpine snow
- glacier
- glacial valley
- frozen marsh
- frozen lake
- frozen river
- rocky polar coast
- permafrost region
- ice cave
- glacier cave

These should reuse generic biome, terrain, scatter, cave, resource, weather, and atmosphere systems.

## 27.1 Glacial region features

Possible generation features:

- glacier bodies
- ice ridges
- exposed rock
- snow drifts
- ice chunks
- crevasses
- frozen waterfalls
- snow-covered boulders
- blue-ice formations
- ice caves
- glacier caves
- glacial meltwater where climate permits

Example:

```text
GlacialRegionDefinition

Temperature = VeryLow
PersistentSnow = True
IceCoverage = High
VegetationDensity = VeryLow
RockExposure = Moderate

PossibleFeatures:
    Crevasses
    IceCaves
    SnowDrifts
    BlueIce
    FrozenWater

WeatherProfile:
    Snow
    StrongWind
    Blizzards
    Whiteout
```

## 27.2 Arctic vegetation and scatter

Cold regions may use:

- lichens
- moss
- tundra grasses
- low shrubs
- sparse hardy trees near warmer boundaries
- frozen reeds
- dead branches
- snow-covered rocks
- ice debris
- wind-shaped snow

Cold-region content should still be authorable through the same data-driven systems.

---

# 28. Frozen Water

The architecture should allow lakes, rivers, ponds, marshes, and other water bodies to have frozen variants where climate rules permit.

This does not require a complete ice-physics simulation in Supplement V2.

The system should at minimum allow:

- frozen visual state
- cold-region biome integration
- ice-surface traversal rules later
- frozen-water scatter
- ice cracks/visual variation
- hooks for future breakable/thin ice if gameplay later needs it

Do not add complex ice simulation unless justified by future gameplay.

---

# 29. Terrain Navigation Character

Some regions should naturally be harder to navigate without being literal mazes.

The generator should support terrain that produces indirect travel, constrained sightlines, and winding movement.

Examples:

- swamps with pools, roots, and channels
- rock-spire regions
- badlands with gullies and ridges
- karst terrain
- boulder fields
- ravine networks
- dense ancient forests
- mangrove-like wetlands
- glacial crevasse fields
- canyon regions
- heavily fragmented terrain

These regions should not have an entrance, solution, or maze objective.

The land itself simply creates difficult traversal.

## 29.1 Traversal character parameters

Possible data:

```text
TraversalCharacter

ObstacleDensity
Openness
AverageSightline
PassageWidth
TerrainFragmentation
VerticalObstruction
WaterObstruction
VegetationObstruction
NaturalChokepointFrequency
WindingRouteBias
WagonDifficulty
MountDifficulty
```

These values influence generation and route cost.

They should remain authoring/simulation data rather than player-facing meters.

## 29.2 Example traversal characters

### Open plains
- low obstacle density
- long sightlines
- easy direct travel
- wagon friendly

### Dense swamp
- high water obstruction
- high vegetation obstruction
- short sightlines
- indirect paths
- poor wagon suitability

### Rock-spire region
- large geological barriers
- narrow corridors
- cliffs
- winding routes

### Glacier
- crevasses
- ice ridges
- exposed rock
- difficult wagon travel

### Ancient forest
- giant roots
- fallen logs
- dense undergrowth
- irregular openings

The purpose is exploration character, not puzzle generation.

---

# 30. World Route Network Foundation

## 29.1 Azgaar route-generation reference

Generated roads and routes should build on the Azgaar-inspired approach already identified during Thornwake world-generation planning.

Use Azgaar Fantasy Map Generator as a design reference for the following ideas:

- important settlements and world sites act as route destinations
- routes are selected using weighted travel costs rather than arbitrary straight lines
- travel cost may consider slope, mountains, forests, wetlands, rivers, existing roads, suitable crossings, bridge opportunities, and other traversal difficulty
- settlement/world-site assets should expose sensible road entrance anchors so generated roads connect naturally
- road generation should occur after enough terrain, hydrology, biome, settlement, and site data exists to make sensible decisions

Thornwake should adapt these ideas to its own 3D terrain and existing architecture.

Do not port or reproduce Azgaar's implementation directly.

The intended dependency is approximately:

```text
macro terrain
    ->
hydrology / biome / traversal data
    ->
settlement and world-site placement
    ->
road entrance anchors
    ->
generated roads and routes
    ->
World Route Network
    ->
virtual caravans / patrols / bandits / travelers
```

Generated roads must feed the same World Route Network used by the virtual-world simulation.

This is important because generated roads should not exist only as visual terrain features. They should become usable infrastructure with route cost, wagon suitability, connectivity, and later dynamic interaction with player-built roads, bridges, and causeways.

If earlier Azgaar research material or world-generation documentation is present in the project, read it before implementing road or route generation.

This reference is intentionally repeated here even if similar guidance exists elsewhere. It is better for the route-generation requirement to be visible in the document that defines the virtual route network than to depend on Codex discovering an older note.

The virtual-world simulation needs a higher-level route network for caravans, patrols, traveling bandits, migrating groups, and other offscreen movement.

The route network should be capable of representing:

- roads
- trails
- bridges
- causeways
- mountain passes
- settlement connections
- crossroads
- important regional connectors
- future ferry or port links if later required

Example:

```text
RouteSegment

Id
Type
NodeA
NodeB
Length
TravelCost
TerrainDifficulty
RoadQuality
WagonAllowed
MountAllowed
OwnerOrBuilder
```

Virtual groups should use these route costs rather than expensive physical navigation across unloaded terrain.

---

# 31. Dynamic Route Network

The route network must not assume the generated world is permanently immutable.

It should support changes such as:

- destroyed bridge
- blocked route
- repaired route
- new settlement connection
- newly discovered pass
- future player-built road
- future player-built bridge or causeway

When the route graph changes, affected virtual groups can recalculate their preferred routes.

---

# 32. Player-Built Road Integration Hook

The actual road-building gameplay belongs to the later building/terraforming work.

Supplement V2 should only establish the route-network hooks needed so future player construction can affect virtual-world movement.

Example later flow:

```text
PlayerBuildsRoad
    ->
RoadSystem validates usable connection
    ->
Connects recognized route nodes
    ->
New RouteSegment registered
    ->
Virtual route costs update
    ->
Caravans / patrols / bandits may use new route
```

Not every decorative strip of road should become a global trade route.

A road should become strategically relevant when it creates a meaningful connection, for example:

- connects two existing route sections
- connects settlement to settlement
- connects settlement to resource site
- bridges a difficult terrain region
- creates a sufficiently long usable shortcut
- connects to a major world-site route node

## 32.1 Difficult terrain and road value

Terrain navigation character should influence road usefulness.

Example:

```text
Swamp without road:
    WagonDifficulty = VeryHigh
    TravelCost = VeryHigh

Completed causeway/road:
    WagonDifficulty = Low
    TravelCost = Moderate
```

This creates meaningful reasons for players to improve the world later.

The generated terrain remains important, but players can gradually change how the world functions.

## 32.2 Virtual groups using player-created routes

Once a player-built route is valid and registered, virtual actors may use it according to normal route selection.

Possible users:

- caravans
- merchants
- patrols
- hunters
- settlers
- refugees
- bandits
- monster groups where appropriate

A player-built shortcut should not be treated as a private player-only path unless later gameplay explicitly marks it that way.

This allows emergent consequences.

A safer/faster route may attract more trade.

More trade may also attract bandits.

This interaction should emerge from existing systems rather than scripted events.

---

# 33. Route System Scope Guardrail

Supplement V2 should NOT implement the full road-construction gameplay.

That belongs with:

- terraforming
- construction blueprints
- bridges
- road materials
- terrain leveling
- clearing vegetation
- construction labor/material delivery

V2 only needs:

- route-network data structures
- route costs
- dynamic updates
- virtual-world integration
- hooks for later player-built routes

Do not expand Supplement V2 into a full road-engineering system.

---

# 34. Fauna and Creature Population Layer

Creatures should be treated as a population layer rather than as arbitrary individual spawners.

The system should support:

## Wild land animals
Examples:

- deer
- boars
- wolves
- rabbits
- foxes
- bears
- other biome-appropriate wildlife

## Aquatic animals
Examples:

- fish
- frogs
- turtles
- other freshwater or sea life where appropriate

## Air animals
Examples:

- birds
- bats
- insects where useful

## Domestic and settlement animals
Examples:

- chickens
- pigs
- cows
- sheep
- goats
- horses
- dogs

Domestic animals should be able to belong to world sites such as:

- farms
- villages
- towns
- stables
- coops
- pens
- pastures

The world-site definition should decide what kinds of domestic populations belong there rather than relying on random global spawns.

---

# 35. Creature Areas, Territories, Nests, and Lairs

Monster and wildlife populations should not rely only on random ambient spawning.

The system should support defined areas such as:

- wolf territory
- boar feeding ground
- bat cave
- spider nest
- goblin camp
- corrupted monster zone
- breeding ground
- creature den
- monster lair
- migration area

These areas should use one generic framework with configurable behavior.

Example:

```text
CreatureAreaDefinition

Id
DisplayName
Tags

CreatureComposition
TargetPopulation
MinimumPopulation
MaximumPopulation

SpawnPolicy
RespawnDelay
RespawnAmount

BiomeRequirements
RegionRequirements
PlayerExclusionRadius

TerritoryRadius
NestOrLairReference
```

Do not create separate systems for every creature type.

---

# 36. Spawn Policies

A creature area should support different policies using the same underlying system.

## One-shot population

Used for encounters or locations that should remain cleared.

Examples:

- abandoned farmhouse with bandits
- ruined tower with skeletons
- scripted ambush
- small monster camp

Once the population is removed, it does not return unless another world event explicitly repopulates it.

## Replenishing population

Used for areas intended to remain part of the living world.

Examples:

- wolf territory
- goblin region
- spider forest
- corrupted valley
- large cave ecosystem

Do not simply spawn a full new group every interval.

Prefer population recovery toward a target.

Example:

```text
TargetPopulation = 8
CurrentPopulation = 3
RespawnAmount = 2-3
RespawnDelay = 2 game days
```

After the delay, the area gradually moves back toward its target population.

This prevents uncontrolled accumulation.

---

# 37. Sandbox and Server Population Controls

Sandbox/server settings should modify global population pressure and activity without replacing the underlying world definitions.

Recommended controls include:

```text
MonsterPopulation:
    VeryLow
    Low
    Normal
    High
    Extreme

MonsterRespawn:
    Off
    Slow
    Normal
    Fast

BanditPopulation:
    None
    VeryLow
    Low
    Normal
    High
    VeryHigh

BanditRespawn:
    Off
    Slow
    Normal
    Fast

BanditGroupSize:
    Small
    Normal
    Large

CaravanFrequency:
    None
    Rare
    Normal
    Frequent

CaravanSize:
    Small
    Normal
    Large

WildlifePopulation:
    VeryLow
    Low
    Normal
    High
    Abundant

TravelingMonsterGroups:
    Off
    Rare
    Normal
    Frequent

SettlementActivity:
    Low
    Normal
    High

WorldEventFrequency:
    Rare
    Normal
    Frequent
```

These settings should mostly act as understandable global modifiers.

Do not expose every internal probability or simulation coefficient.

Example principle:

```text
CreatureAreaDefinition
    says what CAN happen

SandboxSettings
    modify how much of it happens in this world
```

A respawn-capable goblin area should stay permanently cleared if monster respawn is globally disabled.

---

# 38. Virtual World Population System

Far-away populations should not require fully active NPCs, creatures, or AI.

The world should support multiple simulation levels.

## Active simulation

Used near players.

Entities exist physically and may use:

- AI
- movement
- combat
- navigation
- inventories
- animations
- interactions

## Reduced simulation

Used for nearby but non-critical regions where full fidelity is unnecessary.

Updates may occur less often and use simplified behavior.

## Virtual simulation

Used for distant regions.

Populations are represented as lightweight data rather than fully instantiated actors.

Example:

```text
VillageState

Population:
    Villagers = 38
    Guards = 5
    Merchants = 3

Livestock:
    Chickens = 12
    Sheep = 6
    Cows = 2

Resources:
    Food = Good
    Wood = Moderate
    Medicine = Low

Security = Moderate
Prosperity = Normal
```

The game must not simulate every distant villager walking, eating, sitting, or pathfinding.

Remote simulation should operate on aggregate state unless individual identity matters.

---

# 39. Persistent Important NPCs

Important named NPCs may remain persistent records even while virtual.

Examples:

- village leader
- blacksmith
- merchant
- innkeeper
- quest-relevant NPC
- faction representative

Their identity, important inventory, relationships, and history may persist while the physical actor is unloaded.

Generic background villagers may collapse into population statistics when offscreen.

---

# 40. Virtual Wildlife and Monster Populations

The same virtual system should support wildlife and monsters.

Example:

```text
ForestRegion

Deer = 27
Wolves = 6
Boars = 12
Rabbits = Abundant
```

Possible lightweight background effects:

- predators reduce prey populations
- hunting lowers local populations
- populations recover over time
- severe events can reduce populations
- monster territories can grow or shrink
- nearby settlements can suppress threats

These should remain simple abstractions.

Do not build a detailed ecology simulator unless gameplay later clearly requires it.

---

# 41. Traveling Virtual Groups

The virtual-world system should support groups that move through the world without requiring every member to remain physically instantiated.

Examples:

- merchant caravans
- traveling bandits
- patrols
- hunters
- refugees
- migrating animals
- monster groups
- faction warbands
- escorts
- travelers

Example:

```text
TravelingGroup

Type = MerchantCaravan

Members:
    Merchants = 4
    Guards = 3
    Horses = 2

Origin = Oakford
Destination = Thornbridge

Cargo:
    Food
    IronTools
    Cloth

Intent = Trade
Route = NorthernTradeRoad
TravelProgress = 0.42
```

Possible intents include:

```text
Travel
Trade
Raid
Patrol
Hunt
Flee
Migrate
Explore
Escort
```

---

# 42. Virtual Routes and Regional Movement

Virtual groups should not run expensive meter-by-meter pathfinding across unloaded continents.

Use a higher-level route or region graph.

Possible nodes include:

- settlements
- crossroads
- bridges
- mountain passes
- ports
- region connectors
- major road segments
- important world sites

Example:

```text
VillageA
    ->
Crossroads
    ->
Bridge
    ->
MountainPass
    ->
VillageB
```

Different group types may prefer different route classes.

Examples:

- caravans prefer roads and bridges
- patrols prefer known settlement routes
- bandits may prefer remote roads and forests
- monsters may move between lairs and wilderness regions

Normal navigation takes over once the group becomes physically active.

---

# 43. Virtual Encounters and Offscreen Events

Virtual populations and traveling groups should be able to interact offscreen using lightweight event resolution.

Examples:

- bandits attack caravan
- wasp swarm attacks farm
- monsters raid settlement
- patrol attacks bandit group
- predators affect prey populations
- merchants complete trade
- refugees reach another settlement
- monster population expands into neighboring region

Example:

```text
MerchantCaravan enters BanditTerritory

BanditStrength = Medium
CaravanDefense = Low

PossibleResults:
    CaravanEscapes
    CargoStolen
    GuardsInjured
    CaravanDestroyed
    BanditsDefeated
```

Do not run full physical combat when no player can observe it.

Resolve the event abstractly and store meaningful consequences.

---

# 44. Materialization and Dematerialization

When players approach a virtual group, settlement, or encounter, the world should convert appropriate virtual state into physical gameplay entities.

Example:

```text
VirtualCaravan
    ->
Player enters activation range
    ->
Spawn merchants
Spawn guards
Spawn wagon
Apply injuries/cargo/state
    ->
Normal physical simulation
```

If a virtual bandit group and caravan are about to encounter each other while a player is nearby, the system should prefer to materialize the encounter rather than resolve it invisibly.

This allows naturally emerging gameplay:

- hearing combat on the road
- stumbling into a bandit raid
- encountering travelers
- seeing migrating creatures
- discovering a settlement currently under attack

When players leave and the region becomes inactive:

```text
PhysicalActors
    ->
capture meaningful state
    ->
convert back to virtual records
    ->
unload expensive entities
```

Important named actors should preserve identity.

Generic populations may collapse back into aggregate data.

---

# 45. Distant Settlement Simulation

Settlements should be able to undergo simple background changes while no player is present.

Possible abstract state includes:

- population
- security
- food
- medicine
- wealth
- livestock
- basic production
- nearby threats
- current events

Possible events include:

- bandit raid
- monster attack
- crop problem
- wasp or creature infestation
- trade success
- livestock loss
- population growth
- population decline
- damaged buildings
- local recovery

This simulation should remain intentionally lightweight.

It should create history and consequences, not become a full city-management simulator.

When the player later visits, meaningful consequences should be reflected physically where possible.

Examples:

- damaged building
- reduced livestock
- injured NPCs
- fewer guards
- changed stock levels
- graves
- repaired defenses
- nearby creature nest
- new dialogue or ambient comments

---

# 46. Living World Scope Guardrail

The virtual world system is foundational, but Supplement V2 must not attempt to implement an entire civilization simulator.

V2 should establish:

- virtual population data structures
- active/reduced/virtual simulation states
- materialization and dematerialization hooks
- creature-area population integration
- traveling-group representation
- sandbox population controls
- world-site population definitions
- lightweight event interfaces

Deeper systems may be implemented later as a dedicated Living World Simulation system.

Potential later expansions include:

- deeper settlement economies
- faction warfare
- trade networks
- migration
- regional politics
- complex social relationships
- advanced population growth
- detailed production chains

Do not require these for Supplement V2 completion.

---

# 47. Performance Requirements

Environmental detail can become extremely expensive if implemented carelessly.

Supplement V2 should explicitly distinguish:

- instanced visual scatter
- interactive gameplay entities
- persistent world-state objects
- landmark objects

Recommended strategy:

## Visual clutter
Use batching/instancing where practical.

## Interactive resources
Only create full gameplay entities where interaction is required.

## Distance handling
Use appropriate LOD, culling, and activation distances.

## Persistence
Do not save individual cosmetic grass blades, stones, or moss patches.

Only persist information that can meaningfully change.

---

# 48. Save-System Considerations

The existing save system should be reused and adapted.

Generated environment should remain reproducible from:

- world seed
- generator version
- content definitions

Persist only meaningful changes such as:

- harvested resource node state where required
- constructed objects
- destroyed landmarks if supported
- player terraforming
- modified world sites

Do not persist every generated decorative object individually.

---

# 49. Future Systems This Architecture Must Not Block

Supplement V2 does not need to fully implement these systems, but the architecture should not prevent them:

- mining
- metallurgy
- advanced crafting
- alchemy
- farming
- weather gameplay
- creature ecology
- dungeons
- ruins
- abandoned mines
- underground settlements
- creature nests
- magical regions
- rare world events
- biome-specific enemies
- regional factions
- fantasy corruption systems
- procedural world sites
- deeper settlement simulation
- trade networks
- faction warfare
- migration systems
- persistent traveling populations
- player-built road integration
- dynamic trade-route evolution
- route-driven settlement growth
- advanced cold-weather gameplay if later desired

---

# 50. Explicit Non-Goals

Supplement V2 should NOT become:

- a scientific geology simulator
- a realistic atmospheric simulation
- a soil chemistry simulator
- a realistic ecological model
- a full fluid simulation
- an advanced farming simulator
- a complete mining progression system
- a complete crafting overhaul
- a complete weather-survival system
- a full civilization simulator
- a detailed economy simulator
- individual offscreen NPC life simulation

Those systems may use Supplement V2 data later.

The current goal is to create the reusable world and authoring foundation.

---

# 51. Player-Facing Simplicity Rule

Whenever a system developed from this supplement later affects gameplay, apply the following rule:

> Hidden simulation can be complex. Player decisions should remain readable.

Examples:

## Good
"The soil looks dry. Water the crop."

## Avoid
"Soil moisture is 31.2%, relative humidity is 68%, evapotranspiration is above the crop threshold."

## Good
"These yellow mineral caves often contain sulfur."

## Avoid
"Calculate mineral formation from hydrothermal pressure and chemical composition."

## Good
"Storms are common in these mountains."

## Avoid
"Players must manage atmospheric pressure."

---

# 52. Suggested Implementation Order

After Supplement V1 is complete and audited:

## Phase 1: Definitions and architecture
1. Define biome data model.
2. Define environment asset/scatter model.
3. Define geology tags.
4. Define resource/deposit model.
5. Define underground biome model.
6. Define weather-profile model.
7. Add serialization and validation.

## Phase 2: Surface environment
1. Add environmental scatter runtime.
2. Add vegetation/rock placement rules.
3. Add visual vs interactive scatter distinction.
4. Add micro-biome/local variation support.
5. Add initial editor controls.

## Phase 3: Geology and resources
1. Add lightweight geology distribution.
2. Add generic deposit generation.
3. Add initial resource set.
4. Support surface, underground, cave-exposed deposits.
5. Validate with multiple seeds.

## Phase 4: Cave generation
1. Add cave network generation.
2. Add cave entrances.
3. Add depth handling.
4. Add underground biome evaluation.
5. Add biome transitions inside cave networks.

## Phase 5: Cave dressing
1. Add floor decorators.
2. Add wall decorators.
3. Add ceiling decorators.
4. Add large formations.
5. Add underground water features where practical.
6. Connect deposits to cave exposure.

## Phase 6: Terrain materials, atmosphere, cold regions, and traversal
1. Define reusable terrain-material definitions and texture-array/packing strategy.
2. Add terrain material sets and regional palettes.
3. Add normal, height, roughness, wetness, tint, snow, and macro-variation support.
4. Validate biome blending and terraforming/exposed-surface behavior.
5. Add atmosphere/environmental-effect profiles.
6. Add biome and weather hooks for fog, mist, haze, particles, and blowing snow.
7. Validate tundra, arctic, glacier, frozen-water, and ice-cave definitions.
8. Add traversal-character data used by terrain and route cost.
9. Validate difficult-traversal regions such as swamp, ravine, spire, and glacial areas.
10. Add dynamic world-route network foundations.

## Phase 7: Creature populations
1. Add creature population definitions.
2. Add one-shot and replenishing spawn policies.
3. Add territory, nest, den, and lair associations.
4. Add domestic/settlement animal population hooks.
5. Add sandbox population and respawn controls.

## Phase 8: Virtual world foundation
1. Define virtual population records.
2. Add active/reduced/virtual simulation states.
3. Add materialization and dematerialization hooks.
4. Add traveling-group representation.
5. Add regional route-node support.
6. Add lightweight virtual encounter/event interfaces.
7. Do not implement full civilization simulation yet.

## Phase 9: Editor expansion
1. Surface biome editor.
2. Terrain-material editor.
2. Underground biome editor.
3. Environment asset editor.
4. Resource/deposit editor.
5. Atmosphere-profile editor.
6. Traversal-character editor.
7. Route-network visualization/debug tools.
8. Creature-area editor.
9. Settlement population profile editor.
10. Weather-profile editor.
11. Preview/test generation tools.

## Phase 10: Audit
Test many seeds and presets for:

- visual repetition
- scatter density
- obvious procedural patterns
- biome variety
- micro-biome variety
- resource fairness
- cave variety
- cave accessibility
- performance
- save/load stability
- creature population stability
- respawn rules
- virtual-to-physical transitions
- traveling-group materialization
- offscreen event consistency
- sandbox setting behavior

---

# 53. Initial Content Target

The implementation should validate the system with a small but representative content set.

Example:

## Surface
- temperate forest
- grassland
- rocky highlands
- swamp
- tundra
- glacial region

## Micro-biomes
- fern hollow
- mossy boulder field
- wet depression
- fallen-log pocket

## Underground
- root cave
- wet limestone cave
- deep stone cave
- sulfur cavern
- ice/glacier cave

## Resources
- iron ore
- copper ore
- tin ore
- sulfur
- coal
- salt
- limestone
- quartz
- clay
- sand

## Environmental assets
- small pebbles
- small stones
- large rocks
- boulders
- several grass and ground-plant types
- several bush types
- ferns
- flowers
- moss
- vines
- ivy or ivy-like plants
- reeds
- lake/river/sea shoreline plants
- underwater plants
- wild edible plants
- wild fruits
- sample cultivated crops
- trees
- branches
- fallen logs
- stumps
- roots
- cave formations
- cave fungi
- cave vines
- snow drifts
- ice chunks
- glacial crevasse decorations
- frozen shrubs
- waterfall mist
- ground fog
- lake/river mist
- blowing snow
- local cave haze/particles

## Traversal/route examples
- one swamp region with high wagon difficulty
- one rock-spire or ravine region with indirect travel
- one glacial region with crevasse obstruction
- one static generated road connection
- one dynamic route-network update test

## Creature examples
- one wild land herbivore
- one wild land predator
- one aquatic animal
- one flying animal
- one domestic farm animal
- one basic monster population
- one one-shot monster area
- one replenishing monster area

## Virtual-world examples
- one village virtual population
- one merchant caravan
- one traveling bandit group
- one abstract offscreen raid/encounter
- materialization of one traveling group near a player

The purpose of this set is to prove the systems.

It is not intended to represent final game content.

---

# 54. Codex Implementation Rules

Before changing code:

1. Read the Master Rework document.
2. Read the Post-Master roadmap.
3. Read Supplement V1.
4. Read this Supplement V2.
5. Inspect the existing project architecture.
6. Identify systems that can be reused.

During implementation:

- preserve working gameplay
- prefer incremental migration
- keep the project compiling
- keep the project playable
- do not rewrite systems merely to match document naming
- reuse existing terrain, harvesting, crafting, interaction, building, cooking, save, and resource systems where reasonable
- avoid unnecessary mass renames
- avoid hard-coded biome-specific logic
- avoid separate generator classes for every biome or cave type
- prefer generic systems plus data
- keep simple content creation simple
- do not expose generator complexity directly to the player
- optimize decorative scatter appropriately
- report architectural conflicts before replacing working systems
- do not keep distant NPCs physically active when aggregate virtual state is sufficient
- do not simulate individual offscreen daily routines unless required by gameplay
- prefer low-frequency/event-driven offscreen updates
- preserve important named NPC identity across virtual/physical transitions
- ensure sandbox settings modify system behavior without hard-coding alternate implementations
- do not create separate fog/mist systems for each biome; use reusable atmosphere/effect definitions
- do not turn cold-region support into separate bespoke world generators when generic biome/terrain systems can express it
- do not implement literal maze-generation logic for traversal-character regions unless a specific world site later requires it
- route-network logic must support dynamic updates and must not assume roads are immutable
- do not implement full player road construction inside Supplement V2

---

# 55. Completion Criteria

Supplement V2 can be considered successful when:

1. New surface biomes can be defined primarily through data.
2. New underground biomes can be defined primarily through data.
3. Biome definitions can select environment, resources, and weather tendencies.
4. Environmental scatter can populate terrain using rule-based placement.
5. Visual clutter and interactive resources are clearly separated.
6. Micro-biomes can create local variation.
7. Geological resources use a generic deposit system.
8. Sulfur and the initial resource set can be distributed without custom code per material.
9. Cave networks can generate and contain multiple underground biome regions.
10. Cave floors, walls, and ceilings can use different decoration rules.
11. Ore veins can become exposed when caves intersect them.
12. World Site Editor support exists or has a clean implementation path for authoring these definitions.
13. Multiple generated seeds show meaningful variety without obvious repetitive patterns.
14. Performance remains acceptable.
15. Save/load remains stable.
16. Wild, domestic, and monster populations can use generic population definitions.
17. Creature areas support both one-shot and replenishing policies.
18. Sandbox/server settings can globally modify monster, bandit, wildlife, caravan, and related population behavior.
19. Distant settlements and populations can exist as lightweight virtual records.
20. Traveling virtual groups can move between regions without full physical simulation.
21. Virtual groups and encounters can materialize when players approach.
22. Important state survives physical-to-virtual transitions.
23. Offscreen events can produce meaningful but lightweight consequences.
24. Atmosphere profiles can drive fog, mist, haze, particles, blowing snow, and local environmental effects.
25. Cold-region definitions can express tundra, arctic, glacial, frozen-water, and ice-cave environments without bespoke generators.
26. Terrain definitions can express naturally difficult navigation without literal maze logic.
27. Traversal difficulty can contribute to route costs and wagon suitability.
28. The virtual-world layer can use a dynamic route network.
29. Route-network architecture can accept future player-built roads, bridges, and causeways without redesign.
30. Terrain materials support reusable albedo, normal, height, and roughness data.
31. Dynamic wetness can modify material appearance without requiring unique wet textures for every surface.
32. Biome/regional appearance profiles can vary hue, saturation, brightness, and related visual properties without duplicating base materials.
33. Terrain blending supports macro variation and avoids obvious repetitive tiling at large scale.
34. Terraforming can assign sensible exposed dirt/rock/geology materials rather than preserving only the original surface appearance.
35. Regional material palettes and limited local blends keep the terrain-material system scalable and performance-conscious.
36. The systems remain suitable for a fantasy survival RPG rather than becoming simulation for simulation's sake.

---

# 56. Final Design Principle

Thornwake should feel like a world with internal logic, not a collection of random props.

However, the player is here to:

```text
explore
build
craft
fight
farm
discover
survive
live in the world
```

They are not here to operate the world generator.

The systems created by this supplement should make the world richer while keeping the actual game readable, intuitive, and fun.