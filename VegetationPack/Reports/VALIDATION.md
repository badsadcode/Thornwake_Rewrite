# Woodland pack validation — 27 September 2026

- 17 distinct tree variants, nine requested species/styles, 51 GLB exports across three LODs.
- Blender 5.1.1 native source reopened and all 51 asset scenes checked.
- Every export has exactly one asset scene, matching mesh/node counts, and no unused material/image entries.
- Applied transforms, ground-level origins, metre dimensions and real UV maps verified.
- No unused vertices, degenerate triangles, collapsed UV faces or hidden asset geometry found.
- All production images are 32 or 64 pixels square, packed in Blender and embedded in GLBs.
- Closest image nodes, nearest GLB samplers, binary masked cards and exported variant color factors checked.
- All 51 files loaded in Godot 4.5.1. Largest measured bound difference was under 0.08 mm. Both overview and close captures exited successfully with empty error logs.
- Triangle counts decrease through LODs. Representative pine, birch, oak and willow LODs rendered with matching cameras.
- Actual mesh renders inspected in the contact sheets, close views, elevated gameplay view and engine captures. Corrected solid-looking deciduous crowns, cropped preview framing, overly cyan spruce foliage and excessive leaf-card texel density during review.
- Final export checks caught and corrected cross-scene contamination before delivery.

See asset_validation.json, texture_validation.json and godot_validation.json for machine-readable evidence. These checks validate this asset delivery, not forest-scale performance. Distance switching, gameplay colliders, wind/felling animation and live forest integration remain separate work.