# World generation preview

Start **TEST_WORLD_GENERATION.cmd**. This opens the isolated macro map, not a replacement for the current playable valley. The default Vast world preset is **2,048 x 2,048 km**.

- New macro worlds default to **Algorithm 4 (accepted)**. Choose **Algorithm 3 (preserved)** to inspect historical terrain. Choose a seed/profile, then **Generate**. Five JSON profiles cover mainland, several continents, island seas, mountain-heavy and vast worlds.
- Click the map to inspect real metre coordinates, elevation and chunk address. **Zoom in** centres on the selected point. **Zoom out** and **Whole world** restore context; WASD/arrows pan.
- Switch between **Elevation**, **Land ownership** and **Mountain ranges**. Elevation colours are not biome assignments.
- **Save world** creates an immutable preview manifest. **Load world** reads the slot for the seed/profile selected in the controls. Saved geography uses its embedded profile, even after catalogue tuning. Existing slots are never overwritten: change the seed/profile to create another world.
- Algorithm 4 preview saves are under `%APPDATA%/Thornwake/macro-world-previews/algorithm-4/<profile>/<seed>/manifest.json`. Algorithm 3 retains `macro-world-previews/<profile>/<seed>/manifest.json`; choose its algorithm/seed/profile before Load. They never replace `world.json` or the cave test save.

Large worlds are sampled by address. The map is only a bounded overview; zooming rerenders the same geography. The core chunk test keeps 49 nearby regions and builds at most two per planner step while jumping between continents. This does not certify production streaming performance: the new 3D renderer, distant LOD, physics, entity loading, region saves, generated rivers, climate, biomes and natural caves remain to be integrated.

Current geography is a prototype: landforms use bounded seeded shapes, irregular coasts, grouped islands, mountain backbones and broad relief. Drainage-aware valleys/erosion and finer geological variety come next. Small islands/straits can disappear at overview resolution; the reported component count depends on sampling.

## Drainage and regional diagnostics (2026-09-29)

The updated source build is ready through the same **TEST_WORLD_GENERATION.cmd** launcher. Close any older running preview first.

1. Choose **Drainage routes**, **Catchments** or **Basin candidates** in the layer menu. Yellow links show major coarse routes; cyan marks receiving ocean cells; purple/pink show basin candidates/spill cells. Click the map to inspect its coarse metadata; zoom in to see direction arrows and coarse-resolution artifacts.
2. Select a land/basin cell, then **Refine selected cell**, or choose **Regional refinement**. Green markers are incoming ports and cyan is the downstream handoff. Yellow/red lines show a proposed terrain-sampled corridor and bed rises. Purple cells are candidate lake interiors. These are development diagnostics, not rendered water or carved riverbeds.
3. Regional inspection shows one fixed tile. WASD/arrows move to neighbouring tiles; **Whole world** returns to the coarse routes. Scroll the sidebar for selection and candidate details. World/layer switches keep the previous image until the new worker result is ready.
4. **Save world / Load world** retain the existing immutable macro manifest behavior. Regional diagnostics reconstruct from that world; the lab does not save fluid state or migrate the normal game. A future production world must explicitly pin the additional refinement recipe/version.

Focused checks: `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --drainage-preview` and `dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore -- --regional-drainage`. The latter writes `artifacts/regional-drainage.json`.

The vast-world view refines 16 km cells at 250 m sample spacing, so paths and shores remain visibly grid-shaped. Bed-rise diagnostics are not water-flow predictions, and lake storage is an estimate at the coarse candidate level. See [MASTER_A4_HYDROLOGY_ACCEPTANCE.md](MASTER_A4_HYDROLOGY_ACCEPTANCE.md) for current validation, finer inspection results and the exact next step. The additional engine-free inspector is exercised with `--hydrology-inspection`; the lab still shows the original regional proposals. The existing cave water/terrain issue remains open.
