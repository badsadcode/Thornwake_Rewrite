// EXPERIMENTAL 2 water (round 10, Tom's Sea of Thieves references: "stormy ocean seemed quite calm — big waves, but
// calm"). Selected by Lighting and effects → Water = experimental 2. Classic (Water.gdshader) and experimental
// (WaterExperimental.gdshader) are untouched. Same vertex data and uniforms as experimental, plus the spectrum sea:
//  * Ocean (round 11): 64 fixed Gerstner waves (SpectrumWaves.cs: waves2 / wave2_phase / wave2_hs / wave2_chop) — 52
//    wind-sea waves over the whole compass plus 12 swell waves; only amplitudes follow the sea state (smooth storms,
//    calm long swell … ~14 m seas, stylised steep). Each place weights the waves by its local heading (the wind turned
//    by a regional field, lakes their own, swells from far storms) and renormalises; choppiness goes mostly to the
//    biggest waves (crisp crests). World-anchored (world_origin). Wave groups: sets of higher waves drifting with the wind.
//  * Look: deep saturated blue-teal, bright turquoise light through thin crests (stronger, also from the side), dense
//    sun sparkle, whitecaps on folding crests that streak down the back of the waves along the wind, milky aerated
//    water in storms, greyer stormy sea.
//  * Shore waves, lakes (fetch-limited), rivers, foam, biome tint, refraction, reflections: as experimental.
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;
#include "res://Game/Rewrite/WaterSea.gdshaderinc"
