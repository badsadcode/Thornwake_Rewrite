We need to improve the building system so that complex houses can be created without requiring a separate build-menu entry for every possible mesh variation.

The core idea is:

**A single building object should be able to contain multiple shapes/variants, and the player should be able to toggle between them while placing or editing it.**

For example, instead of having:

* Roof Straight
* Roof Outer Corner
* Roof Inner Corner
* Roof Ridge
* Roof Hip
* Roof Valley
* Roof Left Edge
* Roof Right Edge
* Roof 30 Degrees
* Roof 45 Degrees

as completely separate build-menu objects, we should have one main roof-building tool/object and allow the player to cycle through logical variants.

The system should be generic enough to also be used for walls, floors, foundations, beams, stairs, fences and other modular building pieces.

# 1. Modular Building Piece Definition

Create a reusable building-piece definition/system.

A building piece should be able to define:

* category
* base dimensions
* available shapes
* available rotations
* available heights
* available widths
* available slope angles
* available materials
* optional mirrored versions
* snapping rules
* connection points
* construction cost
* collision
* support requirements
* structural type

Conceptually:

```text
BuildingPieceDefinition

id
displayName
category

variants[]
materials[]
allowedRotations[]

snapPoints[]
connectionTypes[]

widthOptions[]
heightOptions[]
slopeOptions[]

supportsMirroring
supportsShapeCycling
supportsMaterialCycling
supportsDimensionCycling
```

Do not hard-code this only for roofs.

The system should eventually work for all modular construction objects.

# 2. Roof Building System

The roof system needs enough pieces to construct believable houses with:

* simple gable roofs
* hip roofs
* L-shaped houses
* T-shaped houses
* porch roofs
* extensions
* barns
* cottages
* steep fantasy roofs
* shallow roofs
* intersecting roof sections
* different roof heights

The main roof piece should support selectable shapes.

Suggested roof shape enum:

```text
RoofShape

Slope
SlopeEdge
SlopeEndCap

Eave

Ridge
RidgeEnd
RidgeCorner
RidgeTJunction
RidgeCrossJunction

Hip
HipTop
HipBottom

Valley
ValleyTop
ValleyBottom

OuterCorner
InnerCorner

GableEdge
GableFill

SlopeTransition

FillerTriangle
FillerSmall
```

Not every one necessarily needs a completely unique mesh.

Where possible, create one mesh and rotate or mirror it.

For example:

```text
Roof_Hip
```

should normally be reusable at:

```text
0 degrees
90 degrees
180 degrees
270 degrees
```

instead of requiring:

```text
Roof_Hip_NE
Roof_Hip_NW
Roof_Hip_SE
Roof_Hip_SW
```

Use variants only when the geometry genuinely needs to be different.

# 3. Roof Slope Angles

Roof pieces should support several standard slope angles.

Start with:

```text
22.5 degrees
30 degrees
45 degrees
60 degrees
```

The most important ones are:

```text
30 degrees
45 degrees
```

45 degrees should probably be the default.

30 degrees gives us normal houses with wider roofs.

45 degrees works well for cottages, medieval houses and general fantasy construction.

60 degrees allows very steep roofs for towers, northern houses, churches, tall cottages and stylized buildings.

22.5 degrees gives us shallow roofs for sheds, porches, workshops and larger buildings.

All related pieces must use compatible slope sets.

For example, if the player chooses:

```text
Roof
Shape: Slope
Angle: 30 degrees
```

then Ridge, Hip, Valley, Gable Edge and other relevant roof pieces need geometry compatible with the same 30-degree roof system.

Do not allow pieces from incompatible slope families to snap together unless a dedicated transition piece exists.

# 4. Roof Variant Controls

While placing a roof piece, the player should be able to change:

```text
Shape
Slope
Rotation
Mirror
Material
```

Example UI/state:

```text
ROOF

Shape: Slope
Slope: 45°
Material: Thatch
Rotation: 90°
```

The exact keys/buttons can be decided based on the existing construction controls.

Possible interaction:

```text
R = rotate
T = cycle shape
F = cycle slope
G = mirror
Mouse Wheel / UI = variant selection
```

Do not blindly use these exact bindings if they conflict with existing controls.

The important part is that the preview updates immediately.

# 5. Ghost Preview

The placement ghost must accurately show the currently selected:

* shape
* slope
* orientation
* dimensions
* material if applicable

Changing a variant should NOT exit placement mode.

Example:

Player selects Roof.

Then while aiming at the building:

```text
Slope
↓
Outer Corner
↓
Inner Corner
↓
Ridge
↓
Hip
↓
Valley
```

The ghost changes immediately.

The player should be able to build complicated roofs without repeatedly reopening the construction menu.

# 6. Remember Previous Selection

Each build category should remember its most recently used settings during the current building session.

Example:

```text
Roof:
Shape = Slope
Slope = 45°
Material = Wooden Shingles
```

If the player builds ten roof pieces consecutively, they should not need to select 45 degrees every time.

If they change to Ridge and then later select Roof again, remembering the previous configuration would also be useful.

# 7. Roof Snapping

Roof pieces need dedicated roof snapping.

A roof piece should expose semantic connection points rather than relying only on generic positional snapping.

Possible connection types:

```text
RoofSlope
RoofUpperEdge
RoofLowerEdge
RoofLeftEdge
RoofRightEdge

RidgeConnection
HipConnection
ValleyConnection
EaveConnection
GableConnection
```

A roof piece should know which connections it supports.

Example:

```text
RoofSlope45

North = RoofUpperEdge
South = RoofLowerEdge
East = RoofSlope
West = RoofSlope
```

This allows the placement system to identify valid neighboring pieces.

# 8. Optional Automatic Shape Detection

Design the architecture so automatic shape suggestions can be added later.

For example, if a player aims at a position where two roof slopes meet internally, the system could suggest:

```text
Valley
```

If two roof slopes meet externally:

```text
Hip
```

At the top of two opposite slopes:

```text
Ridge
```

Do not make automatic placement mandatory.

Manual shape selection must always be available.

Automatic detection should eventually be an assistance system, not something that prevents creative building.

# 9. Adjustable Roof Overhang

Roof pieces should support a configurable overhang where practical.

Something like:

```text
No Overhang
Small
Normal
Large
```

Alternatively this can initially be baked into a small number of visual variants.

The roof should extend slightly beyond walls by default.

This is important because roofs terminating exactly at wall edges tend to make procedural and player-built houses look like simple boxes.

# 10. Roof Materials

Roof geometry and roof material should ideally be separate concepts.

For example:

```text
Roof Geometry:
Slope 45°

Material:
Thatch
Wooden Shingles
Planks
Slate
Clay Tile
Stone
Metal
```

This means we do not want:

```text
ThatchRoofSlope45
WoodRoofSlope45
SlateRoofSlope45
ClayRoofSlope45
```

as completely separate gameplay objects if the rendering system can avoid it.

Prefer:

```text
RoofSlope45
+
RoofMaterial
```

A material may still require a different mesh in special circumstances.

For example, thick thatch may need substantially different geometry.

The system should support material-specific mesh overrides when necessary.

# 11. Walls Should Use the Same Variant Concept

Apply the same philosophy to walls.

One Wall tool should potentially support:

```text
Wall Solid
Wall Window
Wall Door
Wall Arch
Wall Half Height
Wall Quarter Height
Wall Triangle
Wall Gable Triangle
Wall Sloped Left
Wall Sloped Right
Wall Corner
Wall Pillar
Wall Opening
```

Possible adjustable properties:

```text
Width
Height
Shape
Material
Opening
Rotation
Mirror
```

This avoids filling the construction menu with dozens of wall objects.

# 12. Floors

Floor pieces should support:

```text
Full
Half
Quarter

Triangle
Diagonal
Outer Corner
Inner Corner

Hole
Stair Opening
Hatch Opening
```

Possible dimensions:

```text
1x1
1x2
2x2
```

Only implement dimension scaling if it works safely with grid snapping and structural calculations.

Otherwise use predetermined modular sizes.

# 13. Foundations

Foundation variants:

```text
Full Foundation
Half Foundation
Corner Foundation
Pillar Foundation
Raised Foundation
Foundation Edge
Foundation Ramp
```

Foundation height should eventually support several levels if the terrain/building system allows it.

This is useful for uneven terrain.

# 14. Beams and Structural Pieces

Create a Beam structural category.

Variants could include:

```text
Vertical Post
Horizontal Beam
Diagonal Brace Left
Diagonal Brace Right
Cross Brace
Corner Brace
Roof Support
Short Beam
Long Beam
```

Adjustable length would be ideal if technically reasonable.

Otherwise support modular lengths such as:

```text
1 unit
2 units
3 units
4 units
```

# 15. Stairs

One Stair tool can support:

```text
Straight Stairs
Short Stairs
Wide Stairs
Half Stairs
Corner Stairs
Landing
Ramp
Ladder
```

Potential later additions:

```text
L-shaped staircase
U-shaped staircase
Spiral staircase
```

Do not over-engineer those initially.

# 16. Fences and Railings

Use one generic modular system for:

```text
Fence Straight
Fence Corner
Fence End
Fence Gate
Fence Half
Fence Post

Railing Straight
Railing Corner
Railing End
```

Again, rotate pieces rather than storing four directional versions whenever possible.

# 17. Doors and Windows

Wall openings and inserted objects should ideally be separated.

For example:

```text
Wall_WindowOpening
```

can accept:

```text
Small Window
Large Window
Shuttered Window
Arched Window
Wooden Window
Stone Window
```

Likewise:

```text
Wall_DoorOpening
```

can accept different doors.

Long term, this would allow us to change a window or door without rebuilding the whole wall.

If this is too complicated for the first implementation, wall-with-window variants are acceptable temporarily.

But architecture should not prevent a more modular system later.

# 18. Gable Walls

Gable walls are particularly important because roofs will use several slope angles.

We need compatible wall shapes for:

```text
22.5°
30°
45°
60°
```

Possible shapes:

```text
GableFull
GableHalfLeft
GableHalfRight
GableTriangle
SlopedWallLeft
SlopedWallRight
```

This lets walls cleanly fill the area underneath pitched roofs.

# 19. Structural Compatibility

Do not assume every decorative piece is structurally identical.

Building pieces should have structural roles such as:

```text
Foundation
Support
Wall
Floor
Roof
Decoration
Opening
Connector
```

The structural system should be able to reason about these independently from visual shape.

For example:

```text
Roof_Slope_45
Roof_Hip_45
Roof_Valley_45
```

are visually different but all have:

```text
StructuralRole = Roof
```

# 20. Asset Naming Convention

Use predictable descriptive asset names.

Suggested format:

```text
<Category>_<Shape>_<Slope>_<Size>_<Variant>
```

Examples:

```text
Roof_Slope_45
Roof_Slope_30
Roof_Hip_45
Roof_Valley_45
Roof_Ridge_45
Roof_GableEdge_45
Roof_OuterCorner_45
Roof_InnerCorner_45

Wall_Solid_1x1
Wall_Window_1x1
Wall_Door_1x1
Wall_Gable_45
Wall_SlopeLeft_45
Wall_SlopeRight_45

Floor_Full_1x1
Floor_Half_1x1
Floor_Triangle_1x1

Beam_Vertical_1
Beam_Horizontal_2
Beam_Diagonal_2
```

Do not put rotation into the asset name if the object can simply be rotated.

Bad:

```text
Roof_Hip_NE
Roof_Hip_NW
Roof_Hip_SE
Roof_Hip_SW
```

Better:

```text
Roof_Hip
```

with transform rotation.

# 21. Data-Driven Architecture

This should be data-driven.

Do not create large switch statements like:

```text
if roofType == 0
...
if roofType == 1
...
if roofType == 2
...
```

Instead, building definitions should contain variants.

Conceptually:

```text
RoofDefinition
{
    category: Roof,

    variants:
    [
        {
            id: slope,
            mesh: roof_slope,
            slopes: [22.5, 30, 45, 60]
        },

        {
            id: outer_corner,
            mesh: roof_outer_corner,
            slopes: [30, 45]
        },

        {
            id: valley,
            mesh: roof_valley,
            slopes: [30, 45]
        },

        {
            id: ridge,
            mesh: roof_ridge,
            slopes: [22.5, 30, 45, 60]
        }
    ]
}
```

The exact implementation should fit the current Thornwake architecture.

Do not blindly copy this pseudo-code.

Use existing project conventions where possible.

# 22. Variant Mesh Handling

A variant may resolve to:

```text
same mesh + rotation
same mesh + mirror
different mesh
different mesh based on slope
different mesh based on material
```

Create the system so these are legitimate data choices.

Example:

```text
Roof Slope:
22.5 -> Mesh_RoofSlope_22
30   -> Mesh_RoofSlope_30
45   -> Mesh_RoofSlope_45
60   -> Mesh_RoofSlope_60
```

while:

```text
Roof Ridge
```

could potentially share one mesh between some slopes if geometry allows it.

Do not assume everything must be procedurally distorted.

Correct topology and visually clean joins are more important than reducing asset count at all costs.

# 23. Mesh Placeholder Support

We may not yet have final models for every combination.

The system must support placeholder meshes.

For missing variants:

* use a clearly recognizable primitive/placeholder
* log which asset is missing
* do not crash
* do not silently substitute an incorrect shape if that could cause building problems

This allows the building system to be completed before all art assets exist.

# 24. Asset Validation

Add validation for building definitions.

At startup or in an editor/debug mode, report things such as:

```text
Roof Valley 45° has no mesh
Roof Hip 30° has missing collision
Wall Gable 60° has no snap definition
Roof OuterCorner references invalid material set
```

This will become extremely useful as the asset library grows.

# 25. Construction Menu

Do NOT display every mesh variation as a separate icon.

Prefer categories such as:

```text
Structure
    Foundation
    Floor
    Wall
    Roof
    Stairs
    Beam

Openings
    Door
    Window

Exterior
    Fence
    Gate
    Railing

Decoration
```

Selecting:

```text
Roof
```

opens or exposes the roof options.

This keeps the UI manageable even when we eventually have hundreds of building assets.

# 26. Editing Existing Pieces

Ideally, placed pieces should be editable.

For example, target an existing roof and change:

```text
30° -> 45°
Slope -> Hip
Wood -> Thatch
```

Only allow this where doing so will not cause invalid geometry or structural problems.

At minimum, architecture should preserve enough information about the placed piece to allow this later.

Store things such as:

```text
BuildingDefinitionID
VariantID
Slope
Material
Rotation
Mirror
Dimensions
```

Do not store only the final mesh reference.

# 27. Important Roof Construction Goal

The system needs to support buildings like:

```text
        /\
       /  \
      /    \
 ____/______\____
|                |
|                |
|________________|
```

but also:

```text
        /\
   ____/  \____
  /           /\
 /___________/  \
 |              |
 |      ___     |
 |_____/   \____|
```

Meaning roofs must work over:

* rectangles
* L shapes
* T shapes
* intersecting structures
* extensions
* porches
* towers
* multi-height buildings

Therefore Hip and Valley pieces are not optional decorative pieces.

They are fundamental roof topology pieces.

# 28. Initial Implementation Priority

Do not attempt every possible combination immediately.

Implement this incrementally.

Phase 1:

```text
Roof Slope
Roof Ridge
Roof Gable Edge
Roof Eave

30°
45°

Rotation
Shape cycling
Ghost preview
Snapping
```

Phase 2:

```text
Hip
Valley
Outer Corner
Inner Corner

22.5°
60°

Gable wall pieces
```

Phase 3:

```text
roof intersections
automatic shape suggestions
overhang variants
slope transitions
advanced fillers
```

Then apply the same variant architecture to:

```text
Walls
Floors
Foundations
Beams
Stairs
Fences
Railings
Doors
Windows
```

# 29. Design Rule

The important design rule is:

**The build menu represents what the player wants to build, not every individual mesh that happens to exist.**

The player chooses:

```text
ROOF
```

and then chooses its properties.

Not:

```text
Roof45LeftCornerWood01
Roof45LeftCornerWood02
Roof45RightCornerWood01
Roof30RightCornerWood01
...
```

Internally there may eventually be hundreds of meshes.

The player should never have to navigate that mess.

# 30. Do Not Damage Existing Construction

Before modifying the building system:

1. Inspect how building placement currently works.
2. Inspect existing snapping logic.
3. Inspect save serialization for placed objects.
4. Inspect resource-cost handling.
5. Inspect destruction/removal.
6. Inspect networking implications if applicable.
7. Preserve current functional construction behavior.
8. Refactor only where necessary.

Do not replace a functioning building system unnecessarily.

Extend it with a reusable variant layer.

Existing simple pieces should continue to work.

# End Goal

We want a building system where I can select something like:

```text
Roof
Shape: Hip
Slope: 45°
Material: Wooden Shingles
```

place it, then press one control and change it into:

```text
Roof
Shape: Valley
Slope: 45°
Material: Wooden Shingles
```

without reopening menus.

Then I should be able to switch to:

```text
Roof
Shape: Slope
Slope: 30°
```

and build a lower extension or porch.

The same philosophy should eventually allow:

```text
Wall
Shape: Window
Height: Full
Material: Plaster
```

or:

```text
Floor
Shape: Triangle
Material: Wood
```

or:

```text
Beam
Shape: Diagonal
Length: 2
Material: Oak
```

The result should be a flexible, data-driven modular construction system capable of producing attractive cottages, houses, workshops, barns, towers, villages and more complex handcrafted settlements without creating an unusable construction menu.
