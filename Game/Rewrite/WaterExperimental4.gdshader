// EXPERIMENTAL 4 water (round 26, Tom's test): experimental 3A exactly, except that rough and stormy water reflects
// less — the mirror breaks up and dims as the sea state rises, and the water's own colour carries the storm. Calm water
// is identical to 3A. Everything else is WaterSea.gdshaderinc (FFT_OCEAN, EXP3A, EXP4).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;
#define FFT_OCEAN
#define EXP3A
#define EXP4
#include "res://Game/Rewrite/WaterSea.gdshaderinc"
