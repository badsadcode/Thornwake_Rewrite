# Building tools — roof phase 2

The full design request is preserved in `BUILDING_DESIGN_NOTES.md`. Version 0.6.0 implements the second roof phase and applies the same selection system to existing floor, wall, stair and fence pieces.

## Place and adjust

Equip the hammer (default hotbar key **2**), open **B**, and select a tool. Its options stay beside the world while placing:

| Control | Action |
|---|---|
| T / Shift + T | Next / previous shape |
| Y / Shift + Y | Next / previous roof pitch |
| N / Shift + N | Next / previous material |
| U / Shift + U | Next / previous cosmetic finish |
| Z | Toggle repainting brush for the selected tool |
| G | Mirror a shape that supports it |
| R / Shift + R | Rotate forward / backward |
| V | Object, Grid or Free snapping |
| Alt + wheel | Change height relative to your current floor |
| H | Return to your current floor |

The panel buttons also change these properties. Changing a property updates the ghost without closing placement. Each tool remembers its last shape, pitch and material for the current session, including when you switch to another tool. F still eats provisions; ordinary scrolling still zooms.

## Roofs

The Roof tool contains Slope, Ridge, Hip, Valley, Gable edge and Eave at **22.5°, 30°, 45° and 60°**, with 45° as the default. Timber shingles and slate are separate finish choices. Slate adds stone to the displayed construction cost.

Build supported floors and walls first. Place a slope, then aim alongside its edge in Object mode. Compatible edges transfer their height to the next section. Rotate the next section to make it climb, descend or continue beside its neighbour. A ridge is a full three-metre roof module with a central peak and two half slopes. Opposing full slopes can also meet directly at a peak.

Gable edge and Eave are trim pieces placed on an existing matching slope. G mirrors the gable edge to the other side. They retain a reference to that roof: remove trim and dependent roof sections before their supports. Incompatible roof pitches do not snap together. Changing material does not change the pitch family.

Exposed roof edges have a small fixed overhang. Joined edges remove that overhang so surfaces meet at their shared grid line. The ghost uses the selected shape, pitch and finish, with a green or red validity tint. Costs shown beside placement are the costs charged on success.

Earlier flat, triangular and shallow roofs remain available as shapes within Roof. Existing placed houses retain their original geometry, position and paid costs. These older shallow pieces retain their original placement rules; they are not pitch-compatible substitutes for the new roof modules.

## Close the gable ends

Use **Wall → Sloped gable** on an existing slope roof, or **Wall → Peaked gable** on a ridge. Choose matching pitch and rotation; point near the roof centre in Object snapping. **G** chooses the opposite side. The fill follows the roof and extends down to the supporting wall top, including raised ridge sections. It has a solid plaster-and-timber panel and blocks movement only inside that profile. Remove fills before the roof they depend on.

Hip and Valley each cover a full three-metre square. Rotate them to join slopes around a corner; **Y** uses the same four pitch families. Matching connections transfer height. The geometry includes explicit diagonal folds, so surface details do not flatten across the join. Legacy shallow corners remain available separately.

## Other tools

- Floor groups full and triangular pieces, automatically choosing foundations or upper floors from placement height.
- Wall groups solid walls, doorways, diagonal walls, Sloped gable and Peaked gable. Gables use Y for pitch and G for the opposite side.
- Stairs and Fence use the same definition and remembered-selection system, with their existing shape available initially.
- Stations and furnishings retain their existing catalogue entries.

## Saves and extension points

Save schema 8 retains each configured piece's tool, variant, pitch, material and mirror state, alongside its existing rotation, position, level, paid costs and support references. A separate roof connection records a supporting roof edge. Older worlds upgrade without changing their pieces; their original file is retained in a permanent version backup, including `.v6` for 0.5.0 worlds.

`Content/building_tools.json` describes the visible tools and variants. The existing building definitions supply predetermined footprints and physical rules. The roof geometry sampler is shared by snapping and rendering. Visual asset overrides are separate from physical profiles: missing artwork produces a diagnostic and an obvious placeholder, while invalid physical definitions are rejected.

This is a single-player system. It does not add a networking authority or remote building commands.

## Next phases

Hip and Valley now turn compatible slopes around outside and inside corners. Their diagonal folds are shared by rendering, snapping and collision. Automatic shape suggestions, transitions between pitches, advanced ridge intersections, adjustable overhangs and editing placed variants remain future work. Existing variants are saved with enough identity to support future editing, but this release does not modify a placed piece in place.

Dimensions remain predetermined modular sizes. New beams, window inserts, wall sizes, stair shapes, gates and railings are not exposed as empty or non-functional choices.

## Cosmetic finishes and Blender models

Version 0.6.1 adds separate cosmetic appearance IDs and exact model overrides. Finishes do not change physical profiles or recipes. See [BLENDER_BUILDING_ART.md](BLENDER_BUILDING_ART.md) for the player controls, asset dimensions, GLB export, model mappings and current limitations. Schema-7 saves retain their original look and receive a permanent .v7 backup on upgrade.
