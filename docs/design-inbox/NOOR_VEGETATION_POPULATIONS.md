# Vegetation as populations (Noor, 2026-10-07)

Noor's notes, condensed, sent with three asset packs (grass and wheat, aquatic plants, underwater plants). Planned as a
later round; see the plan at the end.

## The idea
Don't place plants as independent random dots: place them as populations. The existing rules for where vegetation may
exist stay in charge (roads, buildings, worked ground, slopes, biome rules, water depth). The change is what happens
after a spot is valid.

1. **Validity mask**: the existing hard exclusions (no grass through houses, roads, fresh digging, cliffs; underwater
   only for aquatic species).
2. **Broad regions** (macro, 50–300 m): slow world-space noise making areas sparse, normal or lush.
3. **Clump field** (patch, 3–25 m): organic shapes (blobs, stretched patches, broken islands, crescents, tendrils, gaps),
   never circles. Clump size varies a lot: 0.5–1 m tuft groups, 3–6 m clumps, 10–30 m lush patches; when macro and patch
   agree, a large stretch becomes dense ("suddenly waist-high grass").
4. **Scatter inside patches** (micro, 0.3–2 m): crossed-plane sprites with random position, rotation, size, lean and
   variant, never a visible grid.
5. **Patch identity**: each patch has a character ("mostly short grass + occasional tall", "thick grass + wild wheat",
   "sparse ordinary grass only") instead of rolling one of twenty textures every 50 cm.
6. **Habitat influence**: believable biases, not simulation. More tall grass and reeds near water; little grass under
   dense trees but lush openings; sparse tufts on rocky ground; broad dense patches in meadows; reeds, sedges, lilies in
   marshes.

Nothing is binary except the hard exclusions: bare / very sparse / sparse / normal / dense / very dense, blending through
irregular areas. Dense is not a carpet: dense areas keep small holes.

## Families (the new assets)
Temperate meadow: ordinary grass as the base, now and then tall grass and wild wheat. Wet meadow: grass plus sedges.
Riverbanks grade into reeds. Shallow still water: lilies. Deeper lake beds: underwater grasses, pondweed. Sea shallows:
eelgrass and kelp; rocky underwater ground: red algae, broad seaweed. Never interleaved evenly: runs of one community,
then a transition.

Aquatic plants use the same principle with fuzzy depth bands (likelihoods, not cut-offs):
shore / wet soil → reeds, sedges; 0–0.5 m → reeds, lilies; 0.5–2 m → lily pads, pondweed, aquatic grass;
2–5 m → submerged grasses, broadleaf plants; deeper → sparse algae or kelp (lake vs sea). No compass-drawn rings.

## Cost
Deterministic transforms from world seed + position + biome + noise masks + species weights, fed to MultiMesh.
No plant AI, no soil chemistry. "80 % of the visual believability for 10 % of the complexity."

## Implementation notes (Sage)
* Today: one low-poly tuft mesh, uniform density per cell (thinned by forest, altitude, slope, biome); reeds in 5 m
  patches. This is exactly the "independent dots" pattern.
* Density as thinning of a fixed candidate set (stable hash per candidate), so patches stay put when the grass setting
  or LOD changes; the setting scales the candidate count.
* Patch identity: jittered cells (~6–25 m, size varying by region) with noise-warped borders; each cell draws a community
  from the biome's weighted table; each plant draws its species from its community.
* Noise at fixed frequencies on folded coordinates (round 20 lesson: a varying scale or direction times a large
  position turns into stripes).
* The same density field tints the ground texture (lush darker green where grass is dense), so the patch pattern carries
  on past the grass draw distance.
* Assets: 1254 px each is far more than a tuft needs; downscale to 128–256 px with nearest filtering and pack per family
  into an atlas (one material, atlas index per instance): few draw calls. Floating plants (lily pads, duckweed) are flat
  quads on the water surface; underwater plants sway with the current.
