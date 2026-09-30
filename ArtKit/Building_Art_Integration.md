# Building art: Blender to Thornwake

Thornwake 0.6.1 separates **physical shape**, **construction material** and **cosmetic finish**. Finish choices can use a procedural palette or an authored model. They never change snapping, collision, supports, costs or dismantling refunds. Save files store stable tool/variant/finish IDs, not asset filenames, so replacing a model updates existing pieces on the next launch.

## Try it in the game

Equip the hammer, press **B**, and choose **Roof** or **Wall**. **U / Shift + U** cycles cosmetic finishes. **Z** toggles the finish brush: aim at a matching existing piece and left-click to repaint it. Z returns to building. The options panel has buttons for both actions. Alt + wheel selects the target storey consistently with placement and dismantling.

Roof finishes: cedar shingles, terracotta, golden thatch and charcoal slate. Wall finishes: limewash, oak boards and fieldstone. **Original** restores the construction material's appearance. **N** still chooses the construction material/recipe; the existing slate recipe retains its stone cost. Repainting is free, cosmetic and reversible. Most new finishes use temporary procedural art; the full fieldstone wall already uses the bundled GLB example.

Supported older unconfigured walls can acquire a finish without changing their pose or paid costs. The earliest generic `roof` pieces, with no matching variant identity, remain unchanged. Shape, pitch and rotation editing of existing structures is a separate future feature.

## Export a model

1. Model in metres. Apply object rotation and scale; keep the assembly origin at the agreed piece origin. Use separate objects/empties for the optional groups below.
2. Export a **glTF 2.0 binary (.glb)** with meshes, materials and embedded textures. Verify the resulting orientation in Godot; coordinates in this document refer to the **imported Godot scene**, with Y up. Keep the editable `.blend` outside the runtime asset folder.
3. Copy the GLB into `Assets/Buildings/`. Import it with the Godot editor or run `tools/build.ps1`. Use letters, numbers, underscores, hyphens and subdirectories in filenames.
4. Add an exact model mapping to an appearance in `Content/building_tools.json` (example below). Rebuild the game. Replacing an existing model file at the same path requires no save migration.
5. Test placement, joins, interior cutaway, the ghost and any moving door before distributing the standalone ZIP. Players do not need Blender or Godot.

Godot recommends glTF 2.0; GLB can package the meshes and textures together, and `.blend` import would require Blender on the development machine. See the [Godot 4.5 import guide](https://docs.godotengine.org/en/4.5/tutorials/assets_pipeline/importing_3d_scenes/available_formats.html).

## Dimensions and origins

| Piece | Imported local coordinates |
|---|---|
| Solid wall | Origin at bottom centre. X from -1.5 to +1.5 m; Y from 0 to 2.6 m; standard panel thickness 0.18 m along Z. |
| Diagonal wall | Same origin, width 3 × sqrt(2) m. Author its actual width; the imported override is not automatically stretched. |
| Doorway | Same wall frame. Opening centred on X, roughly 1.3 m wide and 2.24 m high. Keep the moving leaf separate. |
| Roof module | Origin at the module's base. Nominal X/Z square from -1.5 to +1.5 m. Match one exact pitch. |
| Floor | Three-metre square; top at local Y=0.28 m. A future floor appearance can map separate full/triangle models. |
| Gable fill | Lies along local X=-1.5 m (unmirrored), or +1.5 m (mirrored), extending along Z. Use an explicit mapping for each side. |

Roof top surface formulas (metres; `t = tan(pitch)`):

- Slope: `Y = 0.17 + (Z + 1.5) * t`.
- Ridge: `Y = 0.17 + (1.5 - abs(Z)) * t`.
- Hip: `Y = 0.17 + min(X + 1.5, Z + 1.5) * t`.
- Valley: `Y = 0.17 + max(X + 1.5, Z + 1.5) * t`.

Match the hip/valley diagonal crease at X=Z. The roof skin is approximately 0.10 m thick. Keep shared edges flush to the three-metre grid. Fixed gable models are used only where their bottom is local Y=0; raised fills with a lower bottom use the procedural shape to close the whole opening safely. Gable panel tops follow `RoofGeometry.GableTop` (the roof underside, not the roof top). The executable geometry in `Game/Core/RoofGeometry.cs` is the reference for unusual shapes.

## Optional named groups

- **EdgeNorth**: overhang/trim outside Z=-1.5.
- **EdgeEast**: overhang/trim outside X=+1.5.
- **EdgeSouth**: overhang/trim outside Z=+1.5.
- **EdgeWest**: overhang/trim outside X=-1.5.

Each entire group is hidden when that edge joins another roof. Put all optional edge geometry under these exact names; the main mesh should stop at the grid line. The temporary procedural exposed overhang is 0.18 m. Joined corner details should be authored to look correct with either adjacent group hidden. Placement ghosts show exposed edges until placed.

**DoorLeaf** is required for door model overrides. Put its pivot at the left hinge, local (-0.65, 0, 0), with the closed leaf extending along +X. The renderer rotates this group by -90 degrees around local Y when open. Do not bake an already-open pose into the source. Regular placement previews are closed; repainting previews preserve the placed door's open state.

Meshes, transforms, visibility and surface materials are copied from the imported scene into plain visual nodes. Cameras, lights, collision bodies, animation players and scripts are not copied into the live world. Static building art is supported; skeleton-driven or animated building models need a later extension. Authoring errors in visible dimensions are still possible: the importer deliberately does not infer or modify the physical profile from arbitrary artwork.

## Map the art

Within the relevant tool's `appearances` array:

```json
{
  "id": "clay",
  "name": "Terracotta",
  "colorHex": "b76647",
  "pattern": "tile",
  "models": [
    {
      "variant": "slope",
      "slope": 45,
      "mirrored": false,
      "scene": "res://Assets/Buildings/Roof_Slope_45_Terracotta.glb"
    }
  ]
}
```

Mappings match **variant + pitch + mirror** exactly. Rotation is applied to the placed piece, so do not export compass-direction copies. Material-specific models are represented by separate cosmetic finishes. Other pitch/shape combinations keep their procedural visual until their own model is added. No mesh is deformed to fit another pitch. Cosmetic definitions have no cost field.

An empty `models` array means procedural art is intentional. A configured missing file produces a diagnostic and falls back to the same physical shape. Invalid IDs, unsupported pitch/mirror combinations, duplicate mappings and paths outside `Assets/Buildings` are rejected during content validation. Keep existing finish IDs when replacing assets so saved houses continue to resolve.

## Bundled working example

`Assets/Buildings/Wall_Solid_Fieldstone.glb` is actively mapped to Wall → Solid → Fieldstone. Its editable Godot source is `Wall_Solid_Fieldstone.tscn`; `tools/export-art-example.gd` regenerates this particular example using Godot's GLB exporter. It demonstrates the complete import, placement, ghost, repaint, save and standalone-export path; it is temporary art, not a final Blender model.

Future art families can extend this same appearance mapping. Only roofs and walls currently expose multiple cosmetic finishes in the player UI. This update does not add new physical wall/window/beam shapes or arbitrary object dimensions.
