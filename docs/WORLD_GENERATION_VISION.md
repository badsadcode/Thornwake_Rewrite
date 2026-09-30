# Rich world generation and authored storytelling — design proposal

Date: 28 September 2026. Planning only; no game-code or released-world changes in this pass.

## User direction

The world should have genuinely different geography between seeds: continents/coasts, mountain ranges, valleys, biomes, lakes, rivers, ponds, streams and suitable waterfalls. The creator supplies handmade assets and authored points of interest (POIs), eventually through a dedicated editor. The generator places this content meaningfully and gives locations persistent identities. Layouts may repeat; unique story locations and settlement names must not accidentally duplicate.

The current generator 2 is a finite test valley. Its river, roads, village and broad landform locations are constrained by fixed coordinates. Varying those shapes cannot deliver the intended full-world variation. Retain it for existing saves; develop the larger world under a new generator version.

## Proposed creator workflow

1. Make modular models and props in Blender, with established scale, origins, materials and export conventions.
2. Assemble a reusable location template: a cottage, roadside inn, ruined tower, village layout or story castle. Initially use Godot scenes and a small metadata file; later expose the same format through a focused POI editor.
3. Add gameplay markers: entrances, doors, walkable space, road connections, NPC/loot sockets, quest anchors and interaction points.
4. Describe placement requirements: footprint, height limits, acceptable slopes/biomes, coast/river access, road connections, nearby-location relationships, permitted rotation, foundations/terrain blending and exclusions.
5. Set repetition rules: repeatable, limited per region/world, or unique per world; required or optional story content. Named Castle of Durran is only the user's example, not established game canon.
6. Preview the template on several terrain samples; validate access, collisions, slope, asset references and story markers before publishing it to the content library.
7. Generate a world, inspect its map/validation report, then play. Save its chosen layout and identities so future exploration does not reshuffle content.

The future editor should provide placement masks, terrain fit, connection sockets, spawn/quest markers, uniqueness controls and test-generation previews. It should write the same versioned data used by the initial scene/metadata workflow.

## Geography before local detail

Recommended foundation: a finite seeded world assembled from streamed regions. Decide initial world size from measured memory, generation time and traversal needs. Do not promise an arbitrary continent-scale resolution or seamless infinity yet.

Generate a coarse world atlas first:

1. Continental shapes, sea level and ocean basins. Vary landmass topology and location across seeds.
2. Coherent mountain belts, uplands, lowlands and passes. Evaluate plate-inspired regional structure plus warped multiscale noise; reserve detailed erosion for bounded generation work, not every frame.
3. Climate fields: latitude/altitude temperature and approximate prevailing-wind rainfall/rain shadows.
4. Drainage: watershed/outlet analysis, flow directions and accumulated runoff. Resolve accidental pits while retaining explicitly selected natural lake basins and their spill levels. Climate and water may need a bounded refinement iteration.
5. Water geometry: carve beds and banks from the drainage network. Flow/catchment informs stream versus river width. Lakes have consistent water levels and outlets where appropriate; ponds may be closed basins. Waterfalls need a plausible flowing channel and drop, with geometry/VFX rather than arbitrary decorative placement.
6. Biomes from climate, elevation, moisture and substrate; soften transitions. Place vegetation/resources from these conditions and gameplay supply requirements.
7. Select viable starting areas, settlement/POI sites and a navigable road/bridge network. Reserve footprints and approaches before local vegetation is finalized. Terrain fitting and road grading must revalidate affected drainage and access.

The coarse atlas supplies cross-region rivers, ridges and biomes. Nearby regions refine that shared plan into playable ground. Independently inventing rivers inside each loaded region would cause disconnected drainage and seams.

Water representation should separate terrain bed, water surface/level, flow direction and connectivity. This need not initially be a real-time fluid solver. Define terraform/water limits explicitly until player-made dams, flooding and channel rerouting are implemented.

## Template, instance and story identity

Keep these separate:

- Template ID: reusable physical layout, e.g. village_layout_04.
- Instance ID: one persistent placement in a particular world.
- Story identity: an optional unique role/site, independent of the mesh or template.
- Display name: a saved label assigned through a world-wide name registry.

A world manifest assigns unique/limited sites and required quest roles before regions load. Loading a region only materializes its assigned instances. It never independently rolls for a unique castle or names a settlement.

Allocate mandatory story sites first with bounded constraint solving. If their placement/access requirements cannot be met, retry the affected plan or report an invalid world; never silently omit required content or duplicate a unique site as a fallback. Optional sites may be omitted with diagnostics. Roads and POI selection require a controlled feedback pass, not unlimited retries.

Normalize names for collision checks. Reserve authored names before generating others. Two settlements may use the same layout with different names, inhabitants, decoration and narrative roles. Spacing, regional budgets and repetition cooldowns prevent nearby visual clones. Quest references use persistent IDs, never coordinate guesses or display-name searches.

## Persistence and safety

Save the seed, generator/settings/content-manifest versions, coarse atlas/placement decisions, unique-site/name registries and player changes. Use independent deterministic random streams for geography, ecology and POIs so adding an asset does not unintentionally reroll unrelated systems. Retain manifest allocations across content updates and reject incompatible gameplay changes; cosmetic replacements remain possible within the asset contract.

Move coordinate-specific rules (river collision, bridge crossing, village exclusion, spawn selection, map rendering and resource placement) behind world-data queries before replacing the current geography. Larger terrain alone would leave invisible barriers and incorrectly positioned gameplay rules.

Caves/mines remain separate underground regions, consistent with the existing hybrid terrain proposal. Streaming, terrain LOD and coordinate precision require measured engineering work; a coarse atlas is not a completed playable continent.

## Next practical milestone

Build a separate **world atlas prototype and inspector** before committing to another scenery update:

- Compare several seeds side by side: genuinely different coastlines, range positions, drainage and starts.
- Toggle elevation, rainfall, watersheds, biomes, POI reservations and connectivity.
- Reserve one unique example story site and several repeatable settlement templates; verify unique names/identities.
- Validate water levels/outlets, required-site reachability, biome/resource diversity, generation budgets and deterministic results under different region-load orders.
- Then connect one small streamed playable area to the existing character, construction and earthworks systems, and verify save/reload and region boundaries before expanding.

This is the recommended next design priority in response to the user's feedback. Blueprint construction remains a planned feature; this document does not claim either system is implemented. Digging-strength sandbox tuning is still deferred as requested.

## Primary references considered

- Amit Patel, Red Blob Games, Mapgen2: https://www.redblobgames.com/maps/mapgen2/ — useful reference for connected geography, rivers and biome layers; its intentionally constrained island topology should not be copied as Thornwake's final world model.
- Barnes, Lehman and Mulla, Priority-Flood: https://arxiv.org/abs/1511.04463 — a candidate building block for depression/outlet and watershed analysis, not a complete river/lake generator. Preserve desired lakes rather than filling all basins indiscriminately.
