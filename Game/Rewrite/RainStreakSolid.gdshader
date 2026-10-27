// Rain streaks for TAA and FSR 2, solid (D221; Tom on build 110: "the raindrops … are a little bit grainy"): the streak
// drawn in the opaque pass and cut with a scissor (no dither): thin solid lines that write motion vectors, so the
// temporal filter softens their edges instead of leaving the hashed version's grain. A fainter drop is thinner and
// shorter rather than more transparent.
shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, world_vertex_coords;

#define RAIN_SOLID
#include "res://Game/Rewrite/RainStreak.gdshaderinc"
