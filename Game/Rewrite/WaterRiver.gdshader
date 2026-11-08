// River water (D286, the water rework's third step; docs/WATER_REWORK_PLAN.md): Experimental 4 (Tom's style) with the
// river's own changes (RIVER2), drawn on the river part of each tile's water (D284). Setting *River water*: this, or the
// river as the Water style draws it. Everything else is WaterSea.gdshaderinc (FFT_OCEAN, EXP3A, EXP4, RIVER2).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;
#define FFT_OCEAN
#define EXP3A
#define EXP4
#define RIVER2
#include "res://Game/Rewrite/WaterSea.gdshaderinc"
