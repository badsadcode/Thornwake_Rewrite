# Earthworks — 0.7.0

## Player workflow

Craft Wooden shovel in C, then equip it in I or assign it to any hotbar slot. The four-mode toolbar appears only with the shovel equipped. T cycles modes; Ctrl + wheel changes the brush; G samples the level height; Alt + wheel changes it; H resets. Existing camera drag and wheel zoom remain available. Left click performs one timed stroke. Move, change equipment or right-click to cancel. Menus/focus loss pause; a saved task resumes with its remaining time.

## Authoritative representation

- Generator 1 and all legacy radial TerrainEdits remain unchanged. Their heights are evaluated first.
- EarthworkTerrain version 1 defines a 1 m lattice. Integer Steps represent 1 cm displacement. Packed signed sample coordinates avoid floating-point dictionary keys.
- The added offset uses triangular interpolation with the same diagonal as the current terrain mesh. Rendering may sample the authority separately; visual region size stays 16 m.
- Each sample's basis integrates to 1 square metre on the complete lattice. Therefore one 1 cm step is 10 litres. Summing signed steps gives the exact integer soil debit/credit; intermediate rounding cannot create soil. Zero samples are removed.
- Brush radius is 1–2.5 m. A 1.5 m halo covers the diagonal interpolation support. The preview draws both radii; checks protect the full footprint. Region invalidation includes an additional normal-sampling halo.
- Dig/fill change up to 6 cm per sample per stroke, tapering toward the brush edge. Level approaches a chosen height; Smooth approaches the four-neighbour mean at half strength. These are prototype tuning values.
- TerrainData.MaterialAt uses generated slope and current depth/cover, shared with the debug material view. A shovel cannot cut bedrock. Adding soil can cover rock, and that added soil can be recovered.

## Atomic work and persistence

StartEarthwork validates equipment, reach, standing ground, full site clearance, stamina, sample changes and soil availability. It stores a 1.6-second job with original position, terrain revision and legacy-edit count. No resources or geometry change at start.

TickEarthwork cancels on movement/tool changes or stale terrain. At completion it recalculates all constraints, then commits sample deltas, soil, stamina and revision together. No partial stroke output is granted. Crafting waits while shoveling; shoveling rejects an active crafting task. The presentation tick runs only during focused gameplay outside menus.

Schema 9 stores the sparse grid and nullable task. Loading schemas 1–8 initializes an empty overlay and preserves old terrain. The first subsequent save preserves the original `.vN` file independently of the rotating `.bak`. Grid bounds/version/spacing/value and job consistency are validated.

## Presentation

EarthworkPresentation adapts pointer input and contextual keys, draws surface-following rings plus a level plane/reference post, drives a simple shovel pose from saved progress, and refreshes only dirty ground/grass regions after a completed stroke. EarthworkHud owns the mode buttons, feedback and progress. The shovel has a prototype held model, inventory icon and character portrait rendering; authored art can replace them later.

## Remaining work

- Generator 2 and pre-game generation settings are delivered in 0.8.0; larger configurable geography remains pending.
- User-requested digging strength/depth controls in sandbox settings; keep the current 0.7.0 tuning until that balancing pass.
- Compare 0.5 m authoritative sampling in actual gameplay; version 1 currently uses 1 m.
- Richer soil strata/tool tiers, rock excavation, durability, better authored motion and work effects.
- Grade/trench tools, navigation and traversal refinements, blueprint/material/labour construction.
- Third-person camera changes from the proposal, independently of this interaction slice.

## Verification

EarthworkTests covers timers, cancellation, soil conservation, inverse fill, mixed level/smooth, missing materials at completion, region borders, exact save/resume, schema-8 preservation, malformed data, hoe cultivation and craft exclusion. EarthworkSmokeTests checks equipped visuals, cursor ownership, context controls, rendered markers, work pose, local rebuilding, actual mesh vertices, save reconstruction and pause presentation. The isolated preview uses only smoke-world.json, never the player's normal world.
