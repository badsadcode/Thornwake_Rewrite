// Rain streaks as thin water lenses (D223; Tom: "too sharp … refraction … smoothing the trail"): transparent, bending
// the picture behind them, soft-edged, a bright head and a fading tail. Wider than the other streaks (≥ ~3 px).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, world_vertex_coords;

#define RAIN_REFRACT
#include "res://Game/Rewrite/RainStreak.gdshaderinc"
