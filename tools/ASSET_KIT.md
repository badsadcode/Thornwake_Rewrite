# Regenerating the Blender template kit

These tools write generated files under `ArtKit/`. Save artist working copies elsewhere before regeneration. The playable game scene and its shipped models are not changed by this process.

1. Build `Thornwake.csproj` so `AssetTemplateExporter.cs` is available.
2. Launch the bundled Godot engine with `--headless --path <project> tools/AssetTemplateExporter.tscn`.
3. Launch Blender with `--background --factory-startup --python tools/build_blender_kit.py -- <project>`.
4. Launch Blender with `--background --factory-startup --python tools/verify_blender_kit.py -- <project>`.
5. Launch Godot with `--headless --path <project> --script tools/verify-art-roundtrip.gd`.

The exporter measures actual mesh vertices rather than transformed bounding boxes. This avoids inflated extents for rotated parts. The Blender builder checks the axis mapping (Godot X/Y/Z → Blender X/-Z/Y), saves templates in metres, and creates a separate overview scene for each family. The verifier reopens native files and exports five representative MODEL collections back to GLB; the Godot check compares their original bounds and excludes all guides/reference copies.

`ArtKit/README.md` and `Building_Art_Integration.md` are the user-facing instructions. This folder's scripts are authoring tools and are excluded by the Windows game's export preset. The generated kit is not part of the player download.
