# Master Rework B1: streaming foundation

> **2026-10-04 playable follow-up:** the [A4 visual-validation lab](PLAYABLE_A4_WORLD_VALIDATION.md) is implemented. Use `TEST_PLAYABLE_WORLD.cmd` and the [playable guide](PLAYABLE_WORLD_TEST_GUIDE.md). Desktop now uses Forward+. Historical findings below remain unchanged; [current status](CURRENT_WORLD_GENERATION_STATUS.md) owns continuation. Next: Tom walks/flies through both validation worlds and reports locations.

2026-09-28. Status: baseline/audit complete; first streaming foundation implemented. Final terrain backend decision and large-world gameplay integration remain pending.

## Delivered

- Core/WorldCoordinates.cs: signed 64-bit horizontal region addresses, floor-correct negative coordinates, explicit supported domain (+/-1 billion region indices), global double positions and guarded local float conversion. An origin must be within 8192 m before conversion. This does not migrate existing actors or physics to floating origins yet.
- GenerationIdentity: culture-independent feature IDs with seed, algorithm, profile revision, layer, owner and ordinal; specified FNV-1a stream seeds. Full IDs retain authority; hashes do not identify saved state. Feature-owner placement policy and world manifests follow later.
- Core/RegionStreaming.cs: pure multi-observer union, nearest-first loading, count budget, retention hysteresis, finite-domain filter and deterministic ordering. Every call replaces demand; teleports cannot retain an old pending queue. No global world enumeration or unbounded explored-region cache.
- World/ValleyStreaming.cs: opt-in adapter reusing existing TerrainData, region meshes and grass. Mesh removal does not mutate WorldState. Regions returning after edits resample current authority. The normal game still loads all 64 regions.
- Development/WorldStreamingLab.tscn: isolated finite terrain harness with pan/teleport, residency counters and F9 overlays. It never loads or saves a player world. Uses generator 2 with an existing-format radial edit and sparse earthwork sample crossing a border.

The development lab is intentionally terrain-only. Entity streaming, background jobs, collision residency, navigation, distant LOD, production rebasing, manifest/region persistence, continents, caves, water connectivity and deposits are not claimed by B1. Maximums bound active residency and requested work; queued Godot frees drain at frame end. Mesh build budget is a count, not a millisecond guarantee. Debug overlay changes still rebuild resident meshes synchronously.

## Run

From the project root in PowerShell:

```powershell
dotnet run --project Tests/Thornwake.Core.Tests.csproj --no-restore
dotnet build Thornwake.csproj --no-restore
& '.tools/godot/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64_console.exe' --path . res://Game/Development/WorldStreamingLab.tscn
```

Use WASD/arrows to move the test focus; buttons jump between the edited west, east corner and center. F9 cycles terrain overlays. Escape closes the lab. Existing main scene and controls are unchanged. The visible outer edges in this laboratory are the deliberately exposed residency window, not finished world LOD/seam handling.

## Validation

- Baseline: 1,636,611 core assertions; all prior engine smoke markers passed.
- With B1: **1,639,105 core assertions passed** (2,494 additional checks); build **0 warnings / 0 errors**.
- Core scenarios: negative/exact boundaries; fractional offsets at distant origins; coordinate JSON roundtrip; invalid/overflow guards; stable identity under culture changes; observer union/order; budgets; hysteresis; teleport cancellation; 400 movement/teleport steps; finite clipping; empty-observer release.
- Engine marker `TERRAIN_STREAMING_TEST_PASS`: two builds per call, old nodes retained while stationary, unload/reconstruction, updated authority while unloaded, matching vertex heights and border normals, existing save roundtrip, normal 64-region presentation restored.
- Full existing rendered gameplay smoke suite passed after implementation; `artifacts/master-streaming-game.log`, `master-streaming-game-errors.log` and `master-streaming-game.png`. Gameplay error log is empty. Rendered game and lab captures visually inspected.
- Lab evidence: `artifacts/master-streaming-lab.log` and `master-streaming-lab.png`. Initial lab shutdown exposed an unattached, eagerly allocated Valley building container. The harness now attaches it for scene-owned teardown and follows the existing game's managed-resource cleanup discipline. Final lab run exited 0 with an empty error log.

No player save schema changed. No packaged release was rebuilt. Next milestone: B2 hybrid/volumetric entrance, collision, editable seam and persistence comparison; see the architecture decision record for acceptance gates.
