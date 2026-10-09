# Moving to Godot 4.7.2: assessment (2026-10-09)

Tom: "would it be possible to switch to Godot Engine 4.7.2? I believe it has some cool features that we could use."

**Short answer: yes, it's possible and it looks low-risk. The code needs no changes to compile.** It's still a
deliberate step with its own return point: three of the changes alter how our sky, water and god rays look. The
project stays on 4.5.1 until Tom says go.

## What was tried (in a scratch copy, nothing committed)
* Downloaded the official `Godot_v4.7.2-stable_mono_linux_x86_64` (released 2026-08-17; 4.7 came out 2026-06-18).
* Pointed the project at `Godot.NET.Sdk/4.7.2` (one line in `Thornwake.csproj`; still .NET 8): **builds with 0
  errors and 0 warnings**.
* Headless import of the whole project with 4.7.2: **0 errors**. 105 `.import` files get rewritten (normal for an
  engine upgrade); `project.godot` gets its version tag bumped once saved in the editor.
* Render comparison of Tom's lake dump `214317` (same world, place, time and settings):
  [`img/godot472_lake_compare.jpg`](img/godot472_lake_compare.jpg), top to bottom: 4.5.1, 4.7.2 as it is, 4.7.2 with
  `rendering/environment/fog/use_legacy_blending = true`.
  * **4.7.2 as it is** comes out hazier and paler. It is 20 % brighter on average and differs from 4.5.1 by about
    25 levels per pixel (0–255). The new fog blending moves our god rays, haze and mist.
  * **With legacy fog blending**, it is practically the same picture as 4.5.1: same average brightness (±1.5) and
    about 10 levels per pixel of difference, mostly from moving water and leaves. So the upgrade can start with
    the old look and move to the new fog deliberately.

## What 4.6 and 4.7 bring that matters to Thornwake
From the official changelogs (`CHANGELOG.md` at the `4.6-stable` and `4.7.2-stable` tags):

| Change | Version | What it means for us |
|---|---|---|
| Screen-space reflections overhauled (more correct, cheaper, half-resolution mode) | 4.6 | Water uses SSR: likely cleaner reflections and lower cost. **Look changes**: retune. |
| Glow blended before tonemapping, default blend mode changed to "screen" | 4.6 | Our sun, glow and sun-blob tuning depends on glow. **Look changes**: retune. |
| Volumetric fog blending cleaned up; 4.7 adds a project setting to restore the old behaviour; NaN/INF filtering | 4.6 / 4.7 | God rays, light beams and mist run through volumetric fog. Can keep the old look with the setting, then move over deliberately. |
| Directional shadow caster culling made tight per cascade | 4.7 | Fewer trees and rocks drawn into the sun's shadow maps. **Free performance** for forests. |
| TAA ghosting reduced | 4.6 | Helps the cloud v2 temporal mode and swaying foliage. |
| Ubershaders for more material variants in Forward+ | 4.6 | Fewer shader-compile stutters the first time something is seen. |
| AreaLight3D (rectangular lights) | 4.7 | Windows, hearths and forge glow for towns later. |
| HDR display output (Windows) | 4.7 | Optional real HDR on HDR monitors. A nice later setting. |
| Visual profiler: "Setup Shadows" timing | 4.7 | Helps the tree performance measurements. |
| Jolt physics as default | 4.6 | **New projects only.** We keep our current physics unless we choose to switch. |
| Direct3D 12 as default on Windows | 4.6 | **New projects only.** We stay on Vulkan; D3D12 could be tested as an option. |

**What it doesn't fix:** nothing in 4.6 or 4.7 solves the tree problem by itself (`PERFORMANCE_TREES.md`).
Per-cascade shadow culling helps a little, but the main cost is the number of 3D trees.

## Proposed steps (when Tom says go)
1. Put a **return point** in `SNAPSHOTS.md`: the last 4.5.1 commit, with the build 40 EXE as the playable return
   point.
2. Do the upgrade on a branch (`godot-4.7`): the SDK line, `use_legacy_blending = true`, re-import, a `project.godot` save, and the cloud setup
   script fetching 4.7.2. Run all headless tests, then render the standard comparison set (lake, forest, storm,
   ocean, sunset, underwater) on both versions.
3. Retune glow, SSR and fog where they changed, until the comparisons match build 40 or look better. Every retune
   stays behind the existing settings.
4. Export a test build for Tom on Windows (export templates for 4.7.2 are on the same release page), A/B against
   build 40, and FPS-check the forest B pair. Merge into `main` only when Tom is happy.
