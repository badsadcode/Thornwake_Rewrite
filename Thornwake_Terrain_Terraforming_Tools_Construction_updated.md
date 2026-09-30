# Thornwake Terrain, Terraforming, Tools & Construction Systems

## Purpose

This document defines the next major foundation for **Thornwake**:
procedural terrain generation, persistent terraforming, meaningful
physical tools, and a blueprint-based construction system.

The goal is not simply to add editable terrain. Terrain should become
part of the gameplay loop and connect exploration, gathering, tools,
construction, settlement development, NPC labour, roads, fortifications,
and eventually caves/mines.

The implementation should be designed so that future systems such as
world chunks, streaming, underground voxel excavation, procedural
villages, towns, castles, roads, and large construction projects can be
added without replacing the terrain foundation.

------------------------------------------------------------------------

# 1. Core Direction

Thornwake should use a **hybrid authored/procedural world**.

The wilderness and geography should be procedurally generated from a
deterministic world seed. Important content such as houses, village
modules, castles, ruins, caves, workshops, bridges, and other POIs can
remain authored/premade assets.

The procedural system decides **where those things make sense** and
adapts the environment around them.

Conceptually:

``` text
World Seed
    ↓
Macro Terrain
    ↓
Terrain Shaping / Erosion
    ↓
Water / Hydrology
    ↓
Biomes / Terrain Classification
    ↓
Road Candidates
    ↓
Settlement / POI Site Analysis
    ↓
Villages / Towns / Castles / Ruins / Cave Entrances
    ↓
Vegetation
    ↓
Resources
    ↓
Environmental Detail
```

Do not build this as one giant `GenerateWorld()` method. Generation
should consist of explicit, separable passes with data that can be
inspected and debugged.

------------------------------------------------------------------------

# 2. Procedural Terrain

## 2.1 Deterministic generation

World generation should be seed-based.

The same:

-   world seed
-   generator version
-   generation settings

should produce the same untouched terrain.

This allows saves to store the seed plus persistent world changes rather
than requiring every untouched part of the generated world to be stored.

The generator version should also be tracked so future changes to the
algorithm do not silently corrupt existing worlds.

------------------------------------------------------------------------

## 2.2 Heightfield terrain

The initial overworld terrain should use a heightfield.

Avoid simple single-layer noise that produces endless random rolling
hills. Terrain generation should combine several scales and shaping
rules to produce recognizable geography such as:

-   broad lowlands
-   hills
-   ridges
-   valleys
-   plateaus
-   flatter settlement areas
-   river valleys
-   rocky slopes
-   natural passes

Terrain should look like geography rather than visible noise.

------------------------------------------------------------------------

## 2.3 Separate terrain data from rendered geometry

The authoritative terrain representation must not be the rendered mesh
itself.

Conceptually:

``` text
BaseGeneratedHeight
        +
TerraformModification
        =
FinalTerrainHeight
```

The base procedural terrain remains reproducible.

Player, NPC, road, settlement, and other modifications are stored
separately.

This is important for:

-   persistent terraforming
-   compact saves
-   procedural regeneration
-   future chunk streaming
-   terrain LOD
-   settlement preparation
-   roads
-   debugging

------------------------------------------------------------------------

## 2.4 Terrain data resolution

Do **not** tie authoritative terrain resolution directly to visual mesh
resolution.

The persistent heightfield should have a defined world-space sample
spacing. A starting experiment around **0.5--1 metre per sample** is
reasonable, but this should be tested rather than hardcoded as a final
design decision.

The rendered mesh may later use different resolutions depending on
distance and LOD while the underlying terrain data remains consistent.

------------------------------------------------------------------------

# 3. Terrain Classification

Generated terrain should expose useful information beyond height.

Possible terrain data includes:

-   elevation
-   slope
-   surface normal
-   moisture
-   temperature
-   terrain/soil type
-   rockiness
-   distance from water
-   biome
-   fertility
-   drainage/flood risk
-   accessibility

These values should be queryable by other systems.

Examples:

A village may request:

> A reasonably flat, dry area near water with sufficient buildable
> space.

A castle may request:

> Elevated defensible terrain with room for a keep and outer wall.

A cave entrance may request:

> A sufficiently steep rocky slope.

A farm may request:

> Relatively flat fertile soil with suitable moisture.

This allows procedural content placement to follow authored rules
instead of random coordinates.

------------------------------------------------------------------------

# 4. Terraforming

Terraforming should be a real gameplay system, not a magical editor
brush presented to the player.

Internally, terrain editing operations can still be reusable
mathematical operations, but players access them through physical tools
and gameplay actions.

Core terrain operations should support:

-   lowering
-   raising/filling
-   flattening
-   smoothing
-   grading/sloping
-   shallow trenching
-   material-aware excavation

The same underlying operations should eventually be usable by:

-   players
-   NPC workers
-   road construction
-   settlement generation
-   building-site preparation
-   castle construction

------------------------------------------------------------------------

# 5. Terrain Materials

Terrain should eventually understand what the player is trying to work.

Examples:

-   loose soil
-   clay
-   sand
-   gravel
-   soft rock
-   hard rock

A shovel should not meaningfully excavate solid stone.

A pickaxe should not be the sensible tool for smoothing garden soil.

Terrain material should influence:

-   valid tools
-   work speed
-   stamina cost
-   durability cost
-   resulting resources
-   sound/VFX
-   possible excavation depth/type

The first implementation can be simpler, but the architecture should
leave room for this distinction.

------------------------------------------------------------------------

# 6. Tool Progression

Tools should matter for more than a numerical damage increase.

Relevant tools include:

-   shovels
-   pickaxes
-   axes
-   hammers/building tools
-   possibly hoes, mattocks, saws, and specialized tools later

Material progression may include appropriate stages such as:

``` text
Stone → Copper/Bronze → Iron → Steel
```

Exact tiers should remain a game-design decision.

Better tool materials can affect:

-   which materials can be worked
-   work speed
-   stamina usage
-   durability
-   excavation effectiveness
-   precision
-   resource yield where appropriate

------------------------------------------------------------------------

# 7. Shovels

Shovels should primarily manipulate loose terrain.

Possible actions:

### Dig / Lower

Remove soil from an area.

### Fill / Raise

Use suitable carried/stored material to raise terrain.

### Level

Create a usable building or farming surface around a target height.

### Smooth

Remove harsh terrain artifacts without aggressively changing overall
elevation.

### Grade

Create a controlled slope between elevations, useful for roads, ramps,
entrances, and settlement construction.

### Trench

Create shallow defensive or drainage trenches.

Terraforming should ideally respect material conservation eventually.
Digging earth may produce soil/fill material, while raising substantial
terrain may require fill rather than creating matter from nothing.

------------------------------------------------------------------------

# 8. Pickaxes and Rock Work

Pickaxes should be used where terrain becomes rocky or excavation
involves exposed stone.

They may eventually support:

-   cutting into rocky slopes
-   quarrying
-   preparing foundations
-   mining ore
-   opening/expanding mine entrances
-   underground excavation

Tool quality should affect what rock hardness can reasonably be worked.

This system should later connect to the underground mining system.

------------------------------------------------------------------------

# 9. Axes and Construction Resources

Axes should connect forestry to construction.

Large wooden projects should require physical timber rather than
abstract construction points.

A player constructing a palisade may need to:

1.  mark the project
2.  fell suitable trees
3.  process timber/logs
4.  transport logs
5.  deliver them to the construction project
6.  perform or assign construction work

NPC workers should eventually be capable of participating in this chain.

------------------------------------------------------------------------

# 10. Chunks and Dirty Regions

Full chunk streaming does **not** need to be implemented immediately.

However, terrain code should be designed around regions/chunks from the
beginning so that editing one small area never requires rebuilding the
entire world.

A terrain edit should mark affected regions as dirty.

Depending on the edit, rebuilding may include:

-   terrain mesh
-   collision
-   normals/tangents
-   navigation data
-   vegetation validity
-   water interactions
-   neighbouring chunk borders

Edits touching a region boundary must update the required neighbouring
data without visible seams.

Chunk streaming and distant terrain LOD can be implemented later on top
of this foundation.

------------------------------------------------------------------------

# 11. Underground Caves and Mines

A heightfield cannot represent true overlapping terrain.

At one horizontal position, a heightfield can only provide one surface
height. Therefore it cannot directly represent a cave beneath intact
terrain.

Do **not** convert the entire overworld to voxels solely because caves
may exist later.

Instead, design for a future hybrid system:

``` text
Heightfield Overworld
        +
Separate Underground Volumetric/Voxel Regions
```

The surface remains efficient heightmap terrain.

Caves/mines can later use voxel or other volumetric data and generate
polygonal surfaces around excavated spaces.

Voxel storage does **not** require Minecraft-style cube rendering.
Smooth or low-poly cave meshes can be generated from volumetric data.

------------------------------------------------------------------------

# 12. Player-Made Mines

Long term, players should potentially be able to establish and
physically excavate mines.

A manageable design would be to require a valid **mine entrance** rather
than allowing arbitrary voxel digging anywhere in the overworld.

For example:

1.  locate suitable exposed rock/hillside
2.  designate a mine entrance
3.  construct/support the entrance
4.  activate/create an underground excavation volume
5.  excavate inward with appropriate tools

This could eventually support:

-   player-carved tunnels
-   chambers
-   ore veins
-   natural caverns
-   buried ruins
-   underground water
-   monster nests
-   support beams
-   mine carts/rails
-   lighting
-   lifts
-   collapses or structural stability if desired

This is a later system. Current terrain work should simply avoid
architectural decisions that make it impossible.

------------------------------------------------------------------------

# 13. Building Philosophy

Thornwake does not need one placement method for every construction
type.

Use different interfaces for different kinds of construction.

## Modular construction

Suitable for:

-   houses
-   foundations
-   floors
-   doors
-   windows
-   roofs
-   stairs
-   furniture
-   detailed structures

The existing modular/snapping system should be preserved where useful.

## Linear/infrastructure construction

Suitable for:

-   palisades
-   city walls
-   fences
-   roads
-   trenches
-   irrigation channels
-   possibly bridges and similar infrastructure

These should be much easier to lay out over distance.

The important principle is:

> **Buildings can remain modular. Infrastructure can be drawn/planned
> through the world.**

Do not replace the existing building system before inspecting how it
currently works.

------------------------------------------------------------------------

# 14. Linear Wall Placement

For a palisade or similar structure, the player should be able to choose
a starting point and an ending point.

``` text
A ●════════════════════════● B
```

The game fills the span using suitable modular assets internally.

The player should not have to manually place thirty identical wall
pieces.

Later, intermediate control points can allow walls to follow irregular
boundaries:

``` text
A ●━━━━●━━━━━━●━━━━● B
```

Possible support:

-   straight segments
-   corners
-   gates
-   towers
-   stepped wall sections
-   terrain-following posts
-   snapping to existing fortifications

The player-facing placement can be continuous even if the implementation
still uses modular segments underneath.

------------------------------------------------------------------------

# 15. Blueprint System

A generic **Construction Blueprint System** should become a major
Thornwake foundation.

Placing a blueprint means:

> "I want this built here."

It should **not** mean:

> "Instantly create this object."

Blueprint placement itself should normally be free. Resources and labour
are consumed during actual construction.

------------------------------------------------------------------------

# 16. Blueprint Workflow

Example: palisade construction.

### Step 1 -- Planning

Player selects:

``` text
Build → Defences → Palisade
```

The player selects/draws the desired route.

A ghost/stake representation previews the planned structure.

### Step 2 -- Validation

The game analyses:

-   length
-   terrain slope
-   collisions
-   inaccessible sections
-   required terrain preparation
-   connection to existing structures
-   gates/corners if present

### Step 3 -- Cost calculation

Requirements are calculated from the actual blueprint geometry.

Example:

``` text
Palisade Length: 18.6 m

Required:
31 Logs
6 Rope
Construction Work: 420
```

Numbers above are illustrative only.

### Step 4 -- Material delivery

Players or NPCs bring required materials to the project.

Delivered materials become associated with the blueprint/construction
site.

### Step 5 -- Construction

Construction occurs progressively.

The project should not necessarily require every material before any
work can begin.

If enough materials exist for the first section, construction can start
there.

### Step 6 -- Completion

Finished sections become normal world structures.

------------------------------------------------------------------------

# 17. Blueprint States

A generic blueprint may use states similar to:

``` text
Planned
↓
Needs Site Preparation
↓
Materials Needed
↓
Ready for Work
↓
Under Construction
↓
Completed
```

Individual segments may have their own material and work progress.

Cancelling a project should not magically teleport delivered resources
back to storage. Unused resources should remain recoverable at or near
the construction site.

------------------------------------------------------------------------

# 18. Construction Should Be Progressive

Large structures should visibly appear as work progresses.

For a palisade, this might mean:

-   stakes/markers first
-   holes/prepared ground
-   posts appearing
-   connecting logs
-   final reinforcement

This provides visual feedback and makes construction feel physical.

The exact visual stages can be simplified initially.

------------------------------------------------------------------------

# 19. Terrain and Construction Must Interact

Construction should understand terrain rather than ignoring it.

For example, a long wall across uneven terrain should not simply rotate
every segment until the result resembles a roller coaster.

The blueprint system should analyse the route.

Small elevation differences may be handled automatically by:

-   post depth
-   foundation height
-   small steps
-   wall segment adjustment

Large elevation differences may require:

-   terrain leveling
-   stepped construction
-   retaining foundations
-   a different route

The blueprint preview should communicate these problems before
construction begins.

------------------------------------------------------------------------

# 20. Site Preparation

Construction projects should eventually be capable of requiring terrain
work.

A house might require:

-   clearing vegetation
-   removing rocks
-   flattening a footprint
-   filling a depression

A road might require:

-   clearing
-   grading
-   cutting into a slope
-   filling low ground

A castle wall may require:

-   foundations
-   leveling
-   stone excavation
-   retaining structures

This makes terrain part of construction gameplay rather than scenery.

------------------------------------------------------------------------

# 21. NPC Labour

The blueprint system should be designed so NPC workers can eventually
use it.

Workers may be assigned to:

-   gather required materials
-   transport materials
-   clear vegetation
-   terraform
-   construct
-   repair

A player should be able to build something personally in the early game
and later designate the same type of project for a settlement workforce.

This creates a natural progression:

``` text
Survivor
↓
Builder
↓
Camp
↓
Settlement
↓
Organized Workforce
↓
Village / Town / Fortification
```

The interface does not need to suddenly become a separate RTS game. The
same physical construction rules can scale upward through labour and
logistics.

------------------------------------------------------------------------

# 22. Procedural Settlements Should Use the Same Rules

Where practical, procedural villages/towns should use the same
underlying terrain and placement logic available to players.

A settlement generator can:

1.  find a suitable site
2.  select building locations
3.  create building pads
4.  grade paths
5.  adapt structures to slopes
6.  place walls/fences
7.  connect roads
8.  decorate the finished settlement

This should avoid the appearance that terrain conveniently spawned
perfectly flat beneath every building.

Settlements should look like they were **built into the landscape**.

------------------------------------------------------------------------

# 23. Roads

Roads should eventually use the same linear planning concepts as walls.

A road can be represented as a path/spline with width and construction
properties.

Road generation or player road construction may:

-   clear vegetation
-   smooth small terrain variation
-   grade slopes
-   cut shallowly into hills
-   add fill
-   change terrain material
-   place bridges where required

Procedural settlements and roads should therefore share terrain
modification APIs with player terraforming.

------------------------------------------------------------------------

# 24. World Size

Do not lock Thornwake into "infinite world" or a specific final map size
yet.

World size should be configurable.

During development, use a manageable finite world that makes iteration,
visual inspection, debugging, and regeneration fast.

The architecture should nevertheless avoid assumptions that prevent much
larger worlds or streamed regions later.

------------------------------------------------------------------------

# 25. Debugging and Development Tools

World generation and terraforming need strong debugging tools.

Useful debug visualizations include:

-   height
-   slope
-   moisture
-   biome
-   soil/material type
-   settlement suitability
-   road suitability
-   cave entrance suitability
-   chunk boundaries
-   dirty regions
-   terraform delta
-   procedural base height vs modified height
-   blocked construction areas

Individual generation passes should ideally be toggleable.

This will make procedural-generation problems explainable rather than
requiring blind parameter tweaking.

------------------------------------------------------------------------

# 26. Important Architectural Rule

Do not tightly couple these systems.

Prefer reusable services/data such as:

``` text
TerrainData
TerrainGenerator
TerrainModifier
TerrainMaterialData
TerrainQuery
TerrainRegion / TerrainChunk
ConstructionBlueprint
ConstructionSegment
ConstructionRequirement
ConstructionWorkerTask
LinearStructurePlanner
SitePreparation
WorldGenerationPass
```

Names are illustrative rather than mandatory.

The important point is separation of responsibility.

Avoid creating a monolithic world/terrain/building manager that
eventually controls everything.

------------------------------------------------------------------------

# 27. Implementation Order

The recommended implementation order is:

1.  **Inspect the existing terrain and building architecture.**
2.  Document what can be preserved and what must change.
3.  Implement deterministic procedural heightfield data.
4.  Generate terrain mesh/collision from authoritative height data.
5.  Implement persistent terraform-delta data.
6.  Implement dirty-region rebuilding.
7.  Add developer/debug terraforming brushes first.
8.  Connect terrain modification to actual player tools.
9.  Add terrain material/tool restrictions.
10. Add terrain query/classification APIs.
11. Add generic construction blueprint architecture.
12. Adapt a simple existing structure to blueprint construction.
13. Implement linear palisade/fence planning as the first major test.
14. Connect resource delivery and progressive construction.
15. Prepare interfaces for NPC construction labour.
16. Add roads/site preparation.
17. Add chunk streaming/terrain LOD when world size requires it.
18. Add underground volumetric mining as a separate later phase.

Do **not** attempt to implement caves, settlement simulation, chunk
streaming, roads, procedural cities, and advanced NPC labour
simultaneously.

Build the foundation so those systems can be added cleanly.

------------------------------------------------------------------------

# 28. First Practical Milestone

A good first playable milestone would be:

> Generate a seeded landscape, walk around it, use a shovel to
> physically lower/raise/flatten/smooth the ground, save the game,
> reload it, and see exactly the same modified terrain.

Then:

> Place a palisade blueprint between two points, see its calculated
> resource requirements, deliver logs, perform construction work, and
> watch the palisade progressively appear while correctly adapting to
> the terrain.

If those two loops are solid, a very large part of the future Thornwake
world system has its foundation.

------------------------------------------------------------------------

# 29. Long-Term Gameplay Loop

These systems should ultimately reinforce one another:

``` text
Explore
  ↓
Discover Resources
  ↓
Gather / Mine / Log
  ↓
Craft Better Tools
  ↓
Terraform
  ↓
Prepare Sites / Roads
  ↓
Create Blueprints
  ↓
Transport Materials
  ↓
Construct
  ↓
Develop Camp
  ↓
Attract / Employ NPCs
  ↓
Expand Settlement
  ↓
Build Fortifications / Infrastructure
  ↓
Explore Further
```

The objective is depth through interconnected systems rather than
isolated mechanics.

------------------------------------------------------------------------

# 30. Later Visual Atmosphere

Atmospheric rendering is intentionally **not** part of this immediate
implementation phase.

Later, Thornwake should receive a unified atmospheric system where
lighting, fog, weather, seasons, sky, time of day, shadows, and local
light sources dramatically change the mood of the same environment.

Valheim is a useful reference for the **role** atmosphere can play, not
a target to copy visually.

For now:

> **Gameplay systems first. Atmospheric presentation later.**

The terrain architecture should simply avoid preventing future weather,
seasonal, water, fog, and lighting systems from querying world/terrain
information.

------------------------------------------------------------------------

# 31. Codex Instruction

Before implementing this design, **inspect the current Thornwake
terrain, building, snapping, save/load, navigation, and world-generation
code**.

Do not replace working systems simply because this document proposes a
new capability.

For each existing system:

-   identify what can be reused
-   identify what needs extension
-   identify actual architectural conflicts
-   preserve existing behaviour where possible
-   prefer adapters/extensions over rewrites
-   make incremental, testable changes
-   keep the game runnable throughout the work

In particular, determine whether the current modular building system can
coexist with:

1.  construction blueprints
2.  progressive material delivery
3.  linear structures such as palisades
4.  terrain-aware placement

The intended direction is to **extend the current building system, not
casually replace it**.

------------------------------------------------------------------------

# 32. Character Movement and Camera Direction

The terrain/world work should also preserve the intended future
direction for **third-person movement and camera controls inspired by
World of Warcraft-style character navigation**.

This does **not** mean copying WoW combat or interaction systems.
Thornwake should keep its own interaction, survival, construction,
gathering, and combat design. The reference is specifically for making
movement and exploration feel more natural in a larger 3D world.

The desired direction is:

-   third-person character movement suitable for exploration
-   smooth mouse-controlled camera orbit around the character
-   comfortable zoom/distance control
-   camera-relative movement where appropriate
-   responsive turning and movement rather than stiff grid-like
    navigation
-   sensible camera collision so terrain, trees, walls, roofs, and caves
    do not constantly block the view
-   pitch limits and smoothing that keep the camera controllable on
    steep terrain
-   good behaviour when moving uphill/downhill and across heavily
    terraformed ground
-   a camera that works both in open wilderness and inside settlements
-   controls that remain compatible with combat, gathering, building,
    and blueprint placement

The camera should make Thornwake's larger trees, terrain elevation,
villages, castles, and future atmospheric environments enjoyable to
explore.

## Building / Terraforming Camera Behaviour

Construction and terraforming may need temporary camera behaviour
layered on top of normal character controls.

For example, while placing a long palisade blueprint the player may need
to:

-   orbit freely around the proposed structure
-   zoom farther out than during normal exploration
-   clearly see terrain slope and elevation
-   place/edit control points
-   inspect invalid sections
-   confirm the project without fighting the normal character camera

Likewise, terrain leveling and grading should provide a clear view of
the affected ground.

This does **not** necessarily require a separate top-down building mode.
First test whether the normal third-person camera can be extended with
context-sensitive construction controls.

## Implementation Note

Before changing movement or camera code, inspect the existing Thornwake
controller and interaction systems.

The goal is to improve the **movement/camera layer** without breaking
existing:

-   interactions
-   combat
-   targeting
-   gathering
-   construction
-   UI
-   multiplayer assumptions

Treat this as a related control-system direction rather than part of the
first terrain implementation milestone. Terrain architecture should
nevertheless be tested with this future camera style in mind, especially
slopes, collision, vegetation density, caves, walls, and large
structures.

------------------------------------------------------------------------

## Design Summary

The core philosophy can be condensed to:

> **Procedural wilderness. Handmade content. Authored placement rules.
> Persistent player terraforming. Physical tools. Planned construction.
> Real materials. Progressive building. NPC labour later.**

And:

> **Houses are modular. Infrastructure can be drawn. Blueprints connect
> survival building to settlement building.**

Terrain should not merely be something the player walks across.

It should become something the player can **understand, work, reshape,
build into, defend, and eventually dig beneath**.
