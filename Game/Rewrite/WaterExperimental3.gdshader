// EXPERIMENTAL 3 water (round 23): experimental 2 with the open sea and lakes from GPU FFT cascades — choppy, crisp
// crests and foam born where the surface folds (WaterSea.gdshaderinc, FFT_OCEAN; OceanFft.cs). Shore waves, rivers,
// foam, lighting and reflections are experimental 2's (the same code).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;
#define FFT_OCEAN
#include "res://Game/Rewrite/WaterSea.gdshaderinc"
