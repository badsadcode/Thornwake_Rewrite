// Rain streaks for TAA and FSR 2 (D207): the same streak drawn in the opaque pass with hashed (dithered) transparency, so it
// writes motion vectors and the temporal filter follows each drop, turning the dither into smooth translucency. As an
// ordinary transparent particle, a drop moving ~0.5 m a frame left almost nothing after TAA blended it with the frames
// before (streaks at ~10 % of their strength: rain hardly visible).
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, world_vertex_coords;

#define RAIN_HASHED
#include "res://Game/Rewrite/RainStreak.gdshaderinc"
