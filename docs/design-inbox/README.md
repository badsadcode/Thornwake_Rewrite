# Thornwake Design Inbox

This folder is a handoff area for design documents developed outside the local Codex workspace, including supplements created during planning/review sessions with Noor.

## Authority rule

Files in this folder are **not automatically active implementation authority**.

Codex should read or implement a document from this folder only when the user's prompt explicitly points to that document or explicitly promotes it into the current Master Rework plan.

This prevents draft/future design work from silently changing the current implementation sequence.

## Workflow

1. Noor/user develops or updates a design supplement.
2. The latest complete document is committed here.
3. Codex continues the currently approved Master Rework work unless told otherwise.
4. When the user says a document is ready, Codex should:
   - read the existing Master Rework plan;
   - read the currently authoritative supplements/checkpoints;
   - read the explicitly named inbox document;
   - inspect the current source before changing anything;
   - identify conflicts and reuse opportunities;
   - integrate incrementally rather than rewriting working systems.
5. Once a document becomes established implementation authority, it may be promoted/merged into the normal project documentation so there is one clear current source of truth.

## Current inbox documents

### THORNWAKE_MASTER_REWORK_SUPPLEMENT_V2.md

Status: **Draft / future implementation supplement**

Focus:
- data-driven biomes and biome variants
- environment scatter and world dressing
- geology and mineral/resource distribution
- caves and underground biomes
- weather/atmosphere tendencies
- arctic/glacial environments
- terrain materials and regional visual variation
- traversal character and route-network hooks
- wildlife/monster/domestic population definitions
- virtual offscreen populations, caravans and traveling groups
- World Site Editor authoring hooks

Do **not** begin Supplement V2 implementation merely because this file exists. Continue the currently approved Supplement V1 / Master Rework milestone until the user explicitly instructs otherwise.

### THORNWAKE_LIVING_WORLD_SIMULATION_SCOPE.md

Status: **Future design reference / scope guardrail**

Focus:
- simulation-depth limits and performance philosophy
- ephemeral events versus persistent sites/history
- procedural narrative, rumors and contextual fiction
- lightweight world history and knowledge
- abstract settlement economy and production
- player-driven caravan contracts
- settlement decline, abandonment and recovery
- low-cost background repopulation
- abstract/offscreen populations
- multiplayer 1–16 player performance principles
- weather/hydrology scope guardrails

This document exists for future reference when these systems are actually designed or implemented.

Do **not** implement systems from this file merely because it exists in the repository. Codex should leave it untouched unless the user explicitly asks to read, revise, promote, or implement it.

## Prompt examples

To ask Codex to consider the draft without implementing it:

```text
Read docs/design-inbox/THORNWAKE_MASTER_REWORK_SUPPLEMENT_V2.md as future architecture context.
Do not implement Supplement V2 yet.
Continue the current Master Rework milestone, but avoid decisions that would unnecessarily block the V2 architecture.
```

To begin using it later:

```text
Supplement V2 is now approved for the next implementation phase.
Read:
- THORNWAKE_MASTER_REWORK_PLAN_v3.md
- THORNWAKE_MASTER_REWORK_SUPPLEMENT_v1.md
- THORNWAKE_POST_MASTER_REWORK_ROADMAP_v3.md
- docs/design-inbox/THORNWAKE_MASTER_REWORK_SUPPLEMENT_V2.md
- the latest relevant checkpoint documents

Then inspect the current code before changing anything and propose the safest incremental implementation step.
```
