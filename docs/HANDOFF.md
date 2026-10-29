# Handoff — START HERE (2026-10-27, after build 114)

Written for the next conversation (the previous one ran out of context). Everything is on `main`, pushed.

## Where things stand
* **Last build Tom has: 121** (v0.9.121, return point `770a9e4`, `dist/Preview-121`): fjord coasts (D245, terrain
  revision 27: a cold northern coast raised into a range in the plan, 3–4 fjords with side arms, skerries), single
  fjords (D244, rev. 26), troughs without falls from rivers rising just above them (D243, rev. 25); launchers "Go to -
  glacial trough / hanging valley's fall / down a fjord to the sea / on a fjord, toward its mouth (Balanced, seed 7319)".
  Open on fjords: calmer water in them, ledges on the walls, tree heights; Tom's voxel landform idea (LANDFORMS.md).
  Build 120 (`2b75ae6`): glacial troughs with hanging valleys (D242), terrain revision 24.
  Build 119 (`2039f26`): cirques with a tarn (D241), canyon
  false cliffs and pools on cut ground fixed (D240), terrain revisions 22 and 23; launcher "Go to - cirque with a tarn".
  Build 118 (`863fda0`): waterfalls (D238), gorges and
  canyons (D239) as terrain revisions 20 and 21, wet sheen broken up (D236); launchers "Go to - waterfall (Balanced /
  Mountain kingdoms)", "Go to - canyon (Balanced)". Build 117 (`ff1cfe9`): escarpments fixed from his dumps,
  terrain revision 19 (D235); launcher "Go to - where the comb was". Build 116 (`6c530c5`): escarpments and plateaus,
  terrain revision 18 (D232-D234), Land shape (D230). Build 115
  (`9377656`): terrain revision 17 (D229) - New world Landforms, worlds keep their terrain version. Build 114 (`6473368`): strong rain fixed
  (D225). Rain is parked ("better than it was, maybe go back to it later"). Build 113 (`9948255`): rain tuning (D224).
  Build 112 (`c6b7cdd`, `dist/Preview-112`): refractive rain streaks (D223),
  rain in depth without arcs or a circle, mist gusts in grey rain (D222). Tom's saved Rain look is still 1 (dithered).
  Build 111 (`9572e13`, `dist/Preview-111`): Rain 2.1 (D221) — smooth streaks,
  rain in depth, rain-mist gusts (draft); launcher "Go to - windy downpour (Rain 2.1)". Build 110 (`da955fd`, `dist/Preview-110`): lens drops (D217–D220, off by
  default; launcher "Go to - lens drops in a downpour"), animals take cover (D217). Build 109 (`dc501e5`, `dist/Preview-109`): trees in the rain (D213), dry
  under cover and mist after rain (D214), wet characters and footstep splashes (D215), sun showers, roof-edge drips, lit
  rain (D216); launcher "Go to - forest in the rain". Build 108 (`c320f05`): rain light, shelter, wet look (D210–D212). Build 107 (`9933046`): rain and wind swooshes fixed in the world (D209). Build 106 (`f848430`): each rain streak along its own drop (D208). Build 105 (`5008995`, `dist/Preview-105`): wind wisps with strands (D204,
  D205), Esc → Weather test (D206), rain by strength with splashes (D207); launcher "Go to - rain test spot". Build 104
  (`98ec6a9`): draft *Cloud bases → shaped* (D203, off by default). Note: Tom's settings file has `fx.wind_wisps=1`
  (plain swooshes, written by a capture while that was the default) — the strands need option 2. Builds 76–103 in the previous session:
  Atmosphere 2.0 M2 (rays) and M3 (clouds), moonlight, Living air, colour grading, many fixes. Decisions D143–D202 in
  `docs/DECISIONS.md` (next: **D228**). Ideas not yet scheduled: `docs/ATMOSPHERE_IDEAS_BACKLOG.md`.
* **Tom's settings were reset to a clean baseline** (D200, D201; `tools/settings_baseline.py`; backup
  `%APPDATA%/Thornwake/rewrite_settings.before-baseline-20261025.cfg`). Since then he chose Colour grading *Fantasy* and
  Rays strength 2.5 himself. Earlier his "hazy, bland, constantly in fog" look came from Wind strength 3.9 (a 20 m/s gale
  full of dust) and Cloud base *above the sea* (inside the clouds in the mountains) — check his settings first when
  something looks wrong.
* Capture and benchmark helpers: `tools/capture/` (README there). Dumps: `%APPDATA%/Thornwake/debug`, each with a
  `Reproduce:` line.

## Tom's open point (his last message): the bottoms of the clouds look flat, without details
**Draft done (D203, in build 104):** *Sky → Cloud bases → shaped* (`sky.cloud_bases`, off by default), suite
`cloudbases`, captures `artifacts/cloud-bases-draft.jpg`. Waiting for Tom's look; next ideas if he wants more: a darker
core / brighter edge contrast, rolls stronger in wind, sunset tuning. Notes from before the draft — where it comes from in `Game/Rewrite/SkyCommon.gdshaderinc` (Tom plays with Cloud look = reference,
Cloud shape = rounded):
* `vdens`: the base is a near-level plane (`h = (p.y - wx.z) / wx.y`, cut at h ≤ 0 with a short `smoothstep(0, 0.06–0.1, h)`
  onset); the rounded shape only moves the base by ±150–300 m per cloud (`big.g`, `n.b`), so each cloud's underside is
  one flat sheet.
* Detail erosion: with Cloud look *reference*, `df = mix(1 - df, df, smoothstep(0.0, 0.1, h))` — "billowy almost to the
  base, which stays flat" (round 32, from the M3 references: flat blue-grey bases).
* Lighting (`cloud_march`, `if (cloud_look == 1)`): the base's ambient is pulled toward one flat blue-grey
  (`gl * vec3(0.88, 0.94, 1.12) * 1.3`, weight (1 − hb) × 0.75) — every underside the same colour, no shading.
* Ideas to try, as a switchable draft with paired captures (Tom judges looks; today's look stays selectable): a lumpy
  base (3D noise pushing the base up/down by 50–150 m per lump, rolls along the wind for stratocumulus), keep some
  detail erosion at the base, weaker flattening of the base colour plus sky-light occlusion from the cloud above
  (D198 showed it darkens folds; cost was high at full scale — try only for the base), darker centres under thick
  cloud, brighter thin edges.
* Related findings: the clouds' "posterized / foam" look is the lighting, not the noise (D198, D199: finer 128³ noise made
  no visible difference at 5–15 km; deeper self-shadow `--cloud-dev=1,1` looked more volumetric, but Tom chose to keep
  today's shading for now).

## Other open items
* Living air (D190–D195): Tom likes the dust and leaves (leaves now in flurries). Wind wisps: Tom asked to fix them —
  now soft swooshes with curls and braided strands (D204, D205, setting *Wind wisps*; not yet in a build). Also on his list:
  the darkness (not started). Rain: Esc → Weather test (D206), Rain look by strength with splashes (D207), in build 105. Rain light and veil by strength (D210, draft), shelter under roofs (D211), wet look by material (D212).
  After build 108: trees in the rain, splash kinds, sound under leaves (D213). Next from Tom and Noor's
  `docs/Thornwake_Weather_2_0_Suggestions.md`: dry ground under shelter and mist after rain done (D214), wet characters and footstep
  splashes (D215), sun showers, roof-edge drips, lit rain (D216), animals seek cover and lens drops (D217) — Noor's list done.
  "The darkness": Tom likes nights as they are (2026-10-27). Rain 2.1 (D221, after build 110): smooth streaks, rain in
  depth, rain-mist gusts (draft).
* Colour grading presets (D192): Tom will judge them in play.
* Cloud drafts off by default: softness (D197), smooth shading (D198, costly), fine detail (D199). Cloud quality levels
  High/Medium/Low (D196). Sun behind clouds (D201) and glow by the haze (D202) are on.
* Faint stripes low in the sky with a front coming (cloud layers draft, D189); cloud grain with SMAA fixed (D184/D185).
* The agreed overall order (below): after Atmosphere 2.0, world generation tidy-up, settlements and roads.
  **World generation**: plan `docs/WORLDGEN_PLAN.md` (D226), Tom's answers in it; step 1 done (D227: stages, all
  outputs identical, ~1 % slower). Step 2 designed (`docs/WORLDGEN_MODULES_DESIGN.md`, D228) and approved;
  **revision 17 built (D229)**: module frame, New world → Landforms, revision pinning in world arguments and saves, all
  proved identical to 16. In build 115. World types rework part 1
  (D230): *Land shape* apart from the type, grouped type list (not in a build). Tom picked 15 landforms; order and
  catalogue in `docs/LANDFORMS.md` (D231); no. 1, escarpments and plateaus,
  terrain revision 18 (D232-D234), fixed from his dumps in 19 (D235), in build 117. Open:
  outcrop models on cliff edges (Tom to say). Next on the list: waterfalls and rapids. Open: Tom's three design questions (one or two sliders, rework or
  landforms first, which new shapes).

---

# Earlier handoff — where the work stood (2026-10-12)

Written when the work moved from the cloud session (CPU rendering only, ~17 s a frame) to Tom's PC (RTX 3080), so the
heavy Atmosphere 2.0 tests can run on a real GPU. Everything below is on `main`.

## Last build Tom has
Build 53 (v0.9.53, commit 987890d; packaged on Tom's PC, `dist/Preview-53`). Since then: the cloud look investigation (below). What went into build 53:
* **Terrain revision 16** (D91): a sea pocket inside a lake's reach is lake water — the "crater" in the benchmark lake
  (dump 182351). *Terrain version* gains **Build 52**; **Build 51** can now actually be chosen (the range stopped at 4).
* **Forest rays: A** (D92) — Tom's pick. M2 continues with A; B stays selectable until M2 is done.
* **Cloud blend (v2)** — setting `sky.cloud_blend` (0 = build 52, 1 = matched, default 1). Tom's screenshots (cloud
  tower over the sea from a mountain; lake at sunset with a snowy peak): clouds in front of land did not match the same
  clouds in the sky, mountain outlines showed through them, a cloudless band under the horizon. Causes found and fixed:
  1. land beyond ~47 km counted as sky by `CloudsNear` (reversed depth < 1e-6 with the 90 km far plane) — neither pass
     drew clouds there;
  2. the near pass lit clouds with the sun's sRGB colour instead of linear (pale, pink-white at sunset);
  3. Godot's fog (depth fog by sky affect, then volumetric fog) veils every sky pixel, the sky's clouds included, but the
     near pass had `fog_disabled`. Now the near pass has fog on, its fragment sits just past the volumetric fog's length
     (where the sky reads it), and it writes unpremultiplied colour with `PREMUL_ALPHA_FACTOR` = opacity, so Godot
     produces cloud × transmittance + in-scatter × opacity — the same as over the sky. Godot's sky depth fog is
     emulated (`cloud_fog`, `cloud_fog2` globals; sun scatter × π as in Godot's `sky.glsl`).
  4. the cloud map's mip level inside the march now follows distance, not screen derivatives.
  Build 52's near pass is kept as `CloudsNear52.gdshader` (used when Cloud blend = build 52).
  **Verified** with `--cloud-split-test` (left half: near pass over the sky; right half: the sky's own clouds): with all
  effects on, no seam at the middle. **Not yet verified** on a scene with clouds in front of land (Tom's case): see
  "Next" below.
* **Benchmark**: the camera faces the shot's direction before the warm-up, not after (still 00 of each scene had the
  volumetric fog history of the old view — a beige veil; in the rays A/B, only still 00 per scene was affected).

## Next
* **The agreed order from build 69** (Tom, 2026-10-18; `docs/GAMEPLAY_DESIGN.md` §14.a): finish the multiplayer proof of
  concept (diagonal walls and triangle pieces; animals steering around trees, rocks and buildings) → the real multiplayer
  test with 4–12 players → Atmosphere 2.0 (M2–M4) and shader fixes → world generation tidied up and modular (Tom's
  brainstorm summary to come; plan the settlement and road stages and their terrain revision into it) → settlements and
  roads as placeholders. **Build 70** (`24c41c9`, `dist/Preview-70`, 2026-10-18): diagonal walls, triangle foundations and floors, animals going round trees and
  buildings (D135) — the proof of concept's last items; next is the real multiplayer test. For the world-generation step:
  Tom's `docs/TERRAIN_LANDFORMS_PLAN.md` (from a cloud brainstorm, written without knowing the latest work: a guide to
  check against the code, not a spec). **Build 71** (`6717ae6`, `dist/Preview-71`, 2026-10-18): walls and stairs attach to edges, the edge picking the size (straight,
  narrow, diagonal); diagonal window and doorway; stairs onto triangles' long sides (D136). **Build 72** (`4b0c9c6`, `dist/Preview-72`): vertical half and quarter walls, fences, stairs
  on floors, floors over rooms, an edge crash fixed (D137). **Build 73** (`13d6865`, `dist/Preview-73`, 2026-10-18): **the real multiplayer test** (D138)
  — 12 players (bots: `--bots`, `--bot-spread`, `--net-run`, `--net-lag`, `TEST_MP_BOTS.cmd`) together and in 12 places:
  bandwidth halved (compact positions and animals, protocol 11), the host's animal time cut ~7× (trees cached for trunk
  checks), placed objects in a grid, joined PCs no longer slowed by many pieces; host 60 fps, sync exact. Waiting for
  Tom's test with people (both ends' net logs). **Next: Atmosphere 2.0 (M2–M4) and shader fixes**, the torch lighting
  the ground fog evenly (dump 221438).
  **Build 74** (`2323405`, `dist/Preview-74`, 2026-10-18): smoke from Tom's own texture for torches and campfires (D140);
  *Torch in mist* draft — halo at the flame (D139), build 72's even glow still the default: **waiting for Tom's pick**.
  Then M2 (forest rays A), M1 pause point 3, the cloud drafts, M3, M4 — all need Tom's look decisions.
  **Build 75** (`b983a4f`, `dist/Preview-75`, 2026-10-18): Tom's notes on 74 (D141) — caustics by the sun, scan wave
  gone by 3 s with items glowing, crouching bends the waist; the smoke's backlight had hidden the scan wave.
  **Build 76** (`653ddfc`, `dist/Preview-76`, 2026-10-18): forest rays *A2* draft and *Volumetric fog quality* (D143) for
  Tom to try at different sun angles (he liked the captures; he has no HDRP screenshots, so his eye is the reference).
  **Build 77** (`c44b4bb`, `dist/Preview-77`): A2's shafts vanished away from the sun (anisotropy 0.75) — slider *Forest
  rays A2: forward scattering* (D144). **Waiting for Tom**: the value he likes; then A2's defaults and M2's next step.
  **Build 78** (`b29f017`, `dist/Preview-78`, 2026-10-19): the time slider froze Tom to death ("teleported back to spawn") —
  setting the clock passes no time for the body; test runs never autosave (D145).
  **Build 79** (`6550c2a`, `dist/Preview-79`, 2026-10-19): A2 eased at a high sun (noon was milky white), suite `rays3`
  (A2 through the day); benchmarks never save settings (D146). Capture runs: use a hang watchdog (one froze, cause unknown).
  **Waiting for Tom**: his forward scattering value and how A2 looks through the day.
  **Build 80** (`abc5fcb`, `dist/Preview-80`, 2026-10-19): *Rays in open air* (D148–D150) — a full-screen pass through a
  512² terrain sun-shadow map (20 km), lanes along ridge shadows; mountains block the sun (fog glow, screen shafts,
  near fog, terrain shadows to ~3 km). Off by default; Tom to try. Next: lanes for cloud-gap shafts (his reference 1),
  defaults after his notes. Scene `air-cliff`, suite `airrays` (cost); tools `--ray-vantage[=edge]`, `--air-preview`.
  **Build 81** (`c41f3c7`, `dist/Preview-81`, 2026-10-19): *Forest rays: sun glare* softened (D151). Godot sometimes
  does not quit after an export (the pack is complete: check the log ends with "DONE savepack"). Tom asked for more
  detailed, more varied cloud noise over the mountains (M3, a look decision: draft as an option, captures first).
  **Build 82** (`2196de3`, `dist/Preview-82`, 2026-10-19): *Rays and haze: by the weather* (D152) — an air load
  (moisture, dust) drives every ray effect; default *always*. Open: forest haze tied to places rather than the player
  (proposed to Tom), Tom's pillar casting no shaft, his pick of defaults after trying.
  **Build 83** (`964aa6f`, `dist/Preview-83`, 2026-10-19): Noor's notes as *Light and shadow in the haze* (D154: far
  mountain shadows on the terrain through `TerrainAirShadow.gdshader`, height-shaped near haze in the air pass),
  stronger after-rain haze and *Rays strength* (D153). Capture windows take no input. Waiting for Tom's and Noor's look.
  **Build 84** (`15f0ce2`, `dist/Preview-84`): play starts at a dump's place; "Go to - … .cmd" launchers for the test
  spots beside `JOIN.cmd` (D155; written from Tom's dumps by a script, not in git). Tom asked whether M3 changes it all:
  the structure stays, the final strengths are best set after the cloud work.
  Since build 84: settings presets on the Settings tab (D157; built-in Low–Ultra and the player's own, named); the
  atmosphere's look options stay player settings (D156); a Codex prompt for the UI kit from Tom's mockup
  (`docs/UI_KIT_CODEX_PROMPT.md`, D158) — when Codex delivers, build a Godot Theme from `Assets/UI/ui_manifest.json`.
  **Build 85** (`7d880a9`, `dist/Preview-85`, 2026-10-19): M3 drafts *Cloud shape → rounded* and *Cloud base → above the
  land* (D159–D161; suite `clouds`), settings presets. Open in M3: thin sunset clouds with the rounded shape, the
  horizon slices (stacked layers at sunset), the cost toward the horizon (45 ms at noon), detail and movement,
  Tom's office dumps 115949 / 120402.
  **Build 86** (`aba92e5`, `dist/Preview-86`): built-in presets keep the look, the ray passes' dither holds still without
  TAA (D162); Tom's seven look settings reset to defaults at his request (backup `rewrite_settings.before-look-reset-20261019.cfg`).
  **Build 87** (`604f125`, `dist/Preview-87`): look presets Clear / Natural / Atmospheric / Dramatic
  (`Content/look_presets.json`, D164), *Wind waves on grass* (D163). **Next in M3**: the v2 blue wall on Tom's ridge
  (3,062 m, dump 100445 at 08:48) with air rays off (D164, `artifacts/v2-blue-wall-ridge.jpg`).
  **Build 88** (`ff0dc7d`, `dist/Preview-88`): the "blue wall" is the cloud sea; clouds now hide the air rays (D165,
  D166). **Next in M3**: the cloud sea seen from above is flat and even (no billowed, sunlit tops).
  **Build 89** (`abf3506`, `dist/Preview-89`): the ridge stood in the bottom of the cloud layer (D169); draft *Sky →
  Cloud sea → under the high ridges* puts a thin broken layer below the ridges (`artifacts/ridge-cloud-sea.jpg`). Tom:
  "this is the direction". Open: shafts under the gaps from a valley, cost looking down onto it, tuning after his play.
  **Build 90** (`787dddc`, `dist/Preview-90`): the fast fog waves were the air rays' lanes turning about the world's
  centre as the sun moved (D170); now steady. `--shot-clock` + `--extra-every` capture bursts with the clock running.
  **Build 91** (`b0a7f34`, `dist/Preview-91`): *Cloud sea → broken from below* (D172–D173): a broken sky from the valleys,
  closed at ~90 %, still a sea from the ridge; light follows the gaps but shafts are subtle. Test spots: valley
  -214638,84223 (floor 1,690 m), basin -210238,87823 (880 m); `--lows=` probe in Tests/WorldGen. Open: stronger shafts
  (wide valley, high sea), hole edges as curtains, cloud cost.
  **Build 92** (`344ebfe`, `dist/Preview-92`): see-through clouds in the mountains were the two cloud passes' seam;
  *Clouds around you → one pass in the layer* and *Cloud body → denser* drafts (D176). Measured cloud cost (suite
  cloudcost, D174: 3–19 ms, mostly CloudsNear in the mountains); cheaper march and crisper billows tried and dropped
  (D175, D177). Tom's rule: today's cloud look always stays selectable (no blocky clouds). Open: "shaving foam"
  (needs finer detail noise; ask for dumps), cloud layers (low/mid/high), half-res CloudsNear as an option.
  **Build 93** (`b3862f7`, `dist/Preview-93`): grain on near things in thin cloud fixed — the cloud march's last step
  ends at the scene (D178).
  **Build 94** (`2c512d5`, `dist/Preview-94`): one pass faded over height (no jump at the base), cloud-sea holes wander
  with height (no curtains) (D179). Open: grain in the sky pass's clouds with SMAA (TAA hides it), faint horizon line
  where the passes blend, cloud layers.
  **Build 95** (`c927386`, `dist/Preview-95`): ground noise precision (D180) and the 4 km UV-fold strips (D181) fixed;
  draft *Moonlight → rays and shadows* (D182): the main light follows the moon; haze boost ×2, open air ×1.5. Open:
  forest shafts by moonlight, the look of nights (Tom's call), the sky pass's cloud grain with SMAA.
  **Build 96** (`b4b624c`, `dist/Preview-96`): damp nights + canopy boost give moon shafts (D183); SMAA cloud grain fixed
  with two samples per step (D184). Next: cloud layers (low / middle / high by the weather), captures first.
  **Build 97** (`c90cde0`, `dist/Preview-97`): D184 had banded overcast into a "marble" (fixed, D185); rain no longer
  brightens the night (D186). Ideas backlog: `docs/ATMOSPHERE_IDEAS_BACKLOG.md`. Next: mackerel at night, cirrus,
  middle grey sheet before fronts.
  **Build 98** (`2bc1d9b`, `dist/Preview-98`): high layers dark at night; draft *Cloud layers → layered* (fair cirrus,
  grey sheet before fronts) (D187). Tom: "this direction is good". Tom's own settings now have Cloud base → above the sea
  (clouds at 1.5 km, mountain valleys inside them): launchers set what they need. Open: sheet stripes near the horizon.
  **Build 99** (`0c93671`, `dist/Preview-99`): moon glow backs off in dense air (D188), cirrus in patches (D189), draft
  *Living air* — wind ribbons and biome particles (D190, `AmbientLife.cs`, test flag `--wind=`). Next: colour grading
  (backlog), Tom's feedback on Living air in motion.
  **Build 100** (`1bad637`, `dist/Preview-100`): leaves in random flurries (D191); draft *Colour grading* — 8 LUT presets +
  strength (D192, `ColorGrading.cs`). Tom checks grading himself (left for later); wind trails not judged yet.
  **Build 101** (`8a97481`, `dist/Preview-101`): perf check — recent drafts cost ~nothing (D193); Living air gains dust devils
  (D194, `DustDevil.gdshader`, `--dust-devil`) and embers, fireflies, midge swarms (D195, `AmbientLife.Accents.cs`).
  **Build 102** (`cc79355`, `dist/Preview-102`): sun dims behind clouds (D201); cloud quality levels (D196). Tom's settings
  reset to a clean baseline (D200, D201; `tools/settings_baseline.py`; backup in %APPDATA%/Thornwake). Cloud look: Tom keeps
  today's shading (D199); softness / smooth shading / fine detail drafts off.
  **Build 103** (`1a75bac`, `dist/Preview-103`): the air pass's sky glow follows the haze (D202) — the cream-white sky near
  the sun in clear air is gone.
* **Multiplayer spike (2026-10-13, D101, D102)**: checkpoints 1 (two windows see each other move — Tom) and 2 (digging
  and late joining — automated test) done and merged into `main`; **build 55** (`d2b59b9`, `dist/Preview-55`) has the
  launcher's Multiplayer page and the network log. Checkpoint 3: Tom's line is DS-Lite (D104); first internet test done
  through playit.gg, Poland ↔ Brazil, ~320 ms, ~2 KB/s per player (D105). The host's freezes after teleports were
  rain erosion over a rectangle spanning both places (D106, fixed); also D106: rain (H) shared, map arrows with the
  other players and a go-to list, Esc → Host this world, join-failure crash and send spam fixed — in **build 56**
  (`404a9c5`, `dist/Preview-56`; host and guests must switch builds together, protocol 3). Still open: same weather
  check (§14.0 done-criterion 3), sharing poured water / springs (Tom to decide).
* **Step 1 in progress (2026-10-13, D108–D110)**: playable first slice — 6 axe / LMB chop a tree, I pack and crafting
  (campfire, cook fish), E wood on a fire, needs line (food, warmth, wet, health), all through the host's GameRules,
  also alone; `--gameplay-test`. In **build 57** (`8a1dc3b`, `dist/Preview-57`). **Next**: fishing (a cast action, raw fish from water) so cooking has its fish; E
  gathering (grass → fibre, berries, loose stones; Tom); a fire flame from the torch's particles; death (drop the pack
  where you fall, §4); saving the world (edits, removals, objects, inventories); Tom's storm atmosphere (D109).
  Build 58 (`31d172d`, `dist/Preview-58`): Valheim-like chopping (D111). Since then: felling in three acts (D112, §5a) — notch
  and fall side, impact, a physical trunk cut into logs; **build 59** (`bc32f36`, `dist/Preview-59`).
  Since build 59: E-gathering (D113: stones, berries that regrow, grass → plant fibre), fishing (D114: rod 7, berry
  bait, cast, strike, A/D tug-of-war, fish by water and hour). Then death (D115: the pack dropped where you
  fall), a lean-to (shelter for a cold rainy night), saving (D116). **The first slice (§15) is complete.** **Build 60** (`a5f4be8`, `dist/Preview-60`). **Build 61** (`b2cd143`): fishing as Tom plays it, lean-to fixed (D117). **Build 62** (`e28d621`): bare grass patches (D119), action bar, longer fish runs (D118). **Build 63** (`93785c0`, `dist/Preview-63`, 2026-10-18):
  hunting (D120: deer, stag, fox, wolf from the Quaternius pack; spear 8, bow 9, sword 0, arrows; butchering) and
  building (D121: B, blueprints then materials with E, categories with shapes cycled by V and materials by X, snapping,
  swapping a piece's shape; log foundations, floors, walls, windows, doorways, doors, stairs, log and thatch roofs in five
  shapes and two pitches; campfire and lean-to placed the same way; models from `tools/build_gameplay_models.py`);
  over-the-shoulder aim (D122). Tests: `--hunt-test`, `--build-test`, `--spawn=deer,wolf…`, `--guest-check=s`. **Waiting
  for Tom**: how the pieces should look (they are placeholders; the shape contract is §5e), costs, animal numbers.
  Next ideas: structural strength, animals avoiding trees and buildings, tanning hides, stone and plank pieces.
  **Build 64** (`e6a53a2`, `dist/Preview-64`, 2026-10-18), after Tom's first try (dumps 130414/130944): walls no longer snap under a foundation (D123),
  foundations level on slopes, raise keys, stilts (D123), a build menu with rendered pictures (D124), right-mouse bow
  aiming (D125), animals more common and in swamps, a HUD line for the nearest (D126; build 63's tests had saved
  *Animals: none* into Tom's settings — reset). `--census=s` counts animals where `--at` puts you.
  **Build 65** (`edbd7c9`, `dist/Preview-65`, 2026-10-18): Tab picks pieces with the mouse, right-click on a placed piece changes / turns / takes it
  down, campfire and lean-to can be aimed at, ground pieces stand on decks, arrows found again with an E prompt (D127).
  **Build 66** (`9eea9ed`, `dist/Preview-66`, 2026-10-18): testing tools (D128), denser snap points, the player's turn decides a snapped piece's
  facing (R 90°), steep stairs, picking no longer places, a tree being chopped keeps its collision, dumps with building
  data (D129).
  **Build 67** (`14ac82e`, `dist/Preview-67`, 2026-10-18): snap points shown, P (D130); the bow allows for the drop to the crosshair, animals under the
  crosshair are the target, arrows fly the exact arc, fading trails (D131).
  **Build 68** (`1485e52`, `dist/Preview-68`, 2026-10-18): scan (Z) with animal tracks, blood, sneaking (C), stamina, slower while aiming, shoulder
  switch (V), bow in the right hand (D132); smaller pieces, walls on upper floors (D133). `--scan-test=s`.
  **Build 69** (`5cde63f`, `dist/Preview-69`, 2026-10-18): blockers named and flashed, blueprint shadows, no bites through floors, gathering on your
  level, sonar scan and animal glow (D134). **Next (Tom): diagonal walls** — 45° snapping and turning, diagonal wall,
  triangle foundations and floors (D134). Open from Tom (dump 221438): the torch lights the ground fog evenly far
  into the night — trees far away stand dark against an orange haze; light sources should shape the fog, but less
  uniformly (look at the torch's `LightVolumetricFogEnergy` / range and the fog's density near the ground; atmosphere
  work, after the mechanics).
* **Parked (2026-10-13) for the multiplayer spike** (`docs/GAMEPLAY_DESIGN.md` §14.0, branch `multiplayer-spike`): the
  cloud drafts of D97 stay behind their settings, off by default — *Cloud caps: dense* works (waiting for Tom's pick),
  *Lens clouds: stacked* reads as hard blades from below (rework: softer, thicker lens shapes), *Cloud turbulence* not
  yet visible (test from inside a cloud). Since build 54 on `main`: dumps carry `--cloud-drift`; Alden's torch (R) and
  first person at full zoom-in (D98) — not yet in a build, Tom has not commented yet.
0. **Done on Tom's PC (2026-10-12):** step 1 below (captures on branch `captures/cloud-blend`; Tom accepted the fix), the tree instance-uniform overflow (D93), build 53. Clouds weak: causes found (D94: Godot fogs the sky's half-resolution clouds twice; wind smear at Wind strength 4; weak self-shadowing); *Sky → Cloud look* added; Tom played it, fixes from his dumps (D95: forest hole under a high camera, horizon line inside clouds, far land cut at 90 km); **reference is the default (D96)**, return point `pre-cloud-look-reference`. Open, Tom to decide: denser cloud caps (a cap should hide the summit), turbulence inside clouds, the lens disk (reshape / interim / M3). All of it is in **build 54** (v0.9.54, `0e69bfd`, packaged in `dist/Preview-54`).
1. **Check the cloud fix on Tom's case** (GPU, minutes instead of an hour). Tom's world from dump 182351 (the
   benchmark lake, ground −2.1 m), the camera 400 m up so clouds stand in front of land, cumulus towers forced, four
   directions, build 52 blend vs matched (`--fly-height` counts from the ground, not the water; the lens peak printed
   in the log is relative to the camera):
   ```
   godot --path . res://Game/Rewrite/WorldLab.tscn -- <Reproduce args of dump 182351 without --at --view --fly-height --day --hour sky.clouds time.paused fx.dof>
     --at=-5669.182,5738.657 --fly-height=400 --view=0,-0.1,8 --extra-yaws=1.57,3.14,4.71 --day=1 --hour=15
     --set=sky.clouds=0.5 --set=time.paused=1 --set=fx.dof=0 --clouds=0.8,0,0,0 --set=sky.cloud_blend=1 --world-shot=peak/blend1.png
   ```
   (and `--set=sky.cloud_blend=0` for build 52). Also a sunset (`--hour=19.5`) — Tom's second screenshot. Then build 53.
2. **Lens cloud** (Tom's second screenshot): `ct_lens` in `CloudShapes.gdshaderinc` draws four perfectly elliptical
   13 × 6.4 km plates; from ~5 km away it fills half the sky with stepped rims. Part of M3 (cloud types in the volume);
   artistic — ask Tom before reshaping.
3. **M2: finish forest rays A** (`Game/Rewrite/ForestAir.gdshader`, `Atmosphere.UpdateForestRays`). **Target (Tom,
   D142): Unity HDRP's volumetric lighting** — sharp crepuscular rays, each trunk shadowing the air. Plan:
   (1) a sunrise forest scene beside Tom's HDRP screenshots, captures of rays A as they are now;
   (2) tune Godot's own volumetric fog first — froxel grid finer than 96 × 96 × 96 (`project.godot`
   `environment/volumetric_fog/volume_size` / `volume_depth`), a shorter fog length and detail spread so the cells sit
   near the camera, more sun energy into the forest air, stronger forward scattering; benchmark the cost (`docs/BENCHMARK.md`);
   (3) if that cannot reach it: our own ray pass (a CompositorEffect marching each pixel's view ray through the sun's
   shadow map), the feasibility review's second option. Every new quality level is a **player setting** (D142).
   **Progress (D143, 2026-10-18)**: (1) done — suite `rays2` (sunrise, first person), captures in `artifacts/rays/`;
   (2) done as a draft — *Forest rays → A2* and *Volumetric fog quality* (off … very high). **Waiting for Tom**: his
   HDRP screenshots in `docs/references/` and a look at A vs A2; then either tune A2 further or start step 3.
   **M2 closed for now (2026-10-19, builds 76–84, D143–D156)**: forest rays A2 with forward scattering, sun glare,
   volumetric fog quality; rays in open air (terrain sun-shadow map, ridge lanes, mountains block the sun); rays and
   haze by the weather (air load); Noor's light and shadow in the haze; Rays strength. All player settings (D156).
   Final strengths and defaults after M3 (the clouds change the light under them and the cloud-gap shafts). Optional
   leftovers: forest haze tied to the place instead of the player (proposed, Tom to say), the rock pillar's shaft.
4. Captures for Tom: downscaled JPGs, side by side — done since D143 (`artifacts/rays/`).
5. **Next: M3, the new cloud model** (started 2026-10-19). Acceptance (feasibility review): six cloud captures (close
   cumulus, a cloud at a summit, the horizon at noon and at sunset, broken cover, a storm deck) against the current
   build, a 10 s camera turn without ghosting, the cost reported; Tom's dumps 115949 / 120402; Tom's asks: more
   detailed and more varied cloud noise and movement, the deck no longer at valley height in mountain worlds; the
   cloud drafts of D97 (caps, turbulence, lens) to fold in. Then M4 haze and distance, M1 pause point 3 (warm sun
   glow, moon halo), biome light profiles (`Content/light_profiles.json`).

## Developer tools added recently
* `--cloud-split-test[=1|2|3]`: 1 = left half clouds by the near pass, right half by the sky; 2 = also a probe strip with
  the sun colour/direction each pass sees; 3 = each pass's cloud opacity as grey.
* `--cloud-split-test=4`: CloudsNear draws every cloud, sky ones included (the sky draws none); `--cloud-fog-off`: CloudsNear from `CloudsNearNoFog.gdshader` (no Godot fog); `--cloud-dev=x,y`: global `cloud_dev` (x aerial perspective scale, y extra self-shadowing) — D94.
* `--probe-water=x,z,r,step` (grid of water kinds and levels, lake/outlet stats), `--clear-spot=x,z`.
* Capture suites: `--bench=<suite> --bench-out=<dir> --bench-scenes=a,b --bench-stills=N --bench-compare=key=v1,v2`
  (see `docs/BENCHMARK.md`).

## Known, not yet fixed
* Three perched lakes in Tom's world step down 8–114 m at the outlet (drawn as a steep sheet, physically a waterfall).
* Grass and scatter still use Godot's Self visibility fade (trees switched to dithering in D86).
