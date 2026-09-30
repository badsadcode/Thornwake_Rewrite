# Woodland asset pack v1 — complete

Delivered in VegetationPack/: 17 distinct variants across nine species/styles, 51 GLBs at three LODs, 20 small original pixel texture maps, editable Blender 5.1 source, actual rendered previews and a Godot review project.

- Usage and import guide: VegetationPack/README.md
- Blender: VegetationPack/Blender/Thornwake_Woodland.blend
- Species preview: VegetationPack/Previews/Species_Contact_Sheet.png
- Measured dimensions: VegetationPack/asset_dimensions.csv
- Verification: VegetationPack/Reports/VALIDATION.md
- Portable archive: dist/Thornwake-Woodland-Asset-Pack-v1.zip
- Archive checksum: dist/WOODLAND-SHA256SUMS.txt

The authoring pack is excluded from the main game's import through .gdignore. Game 0.6.3 integrates copies of all 51 exported GLBs under Assets/Vegetation, with stable species mapping, distance LODs, camera canopy cutaways and matching stumps. Resource identities and save schema 8 remain unchanged. The standalone review project dynamically loads the exported meshes and supports LOD selection and camera orbit.

Native and GLB checks passed for all 51 assets. All 51 loaded in Godot 4.5.1; import dimensions matched within 0.08 mm. Overview, close, gameplay and representative LOD renders were reviewed. Closest/nearest sampling, packed maps, alpha masking, clean single-tree exports and variation color factors are verified.

Before this work: backups/Thornwake-source-before-vegetation-pack-20260927-010639.zip (149 source files verified).

Authoring tools are in tools/ and copied into VegetationPack/Source/. Original textures were created with the built-in image generation tool; exact prompts and original outputs are retained in VegetationPack/Textures/Source/. Geometry was built with installed Blender 5.1 tooling, with deterministic seeds available for future variants.

Runtime integration is implemented in Game/Core/Woodland.cs, Game/World/WoodlandAssets.cs and Game/World/ValleyForest.cs. The in-engine smoke suite verifies all 51 imports and the normal planting/harvesting/stump lifecycle. Future work includes additional species seedlings, seasonal foliage, wind and felling animation, and extended performance profiling.