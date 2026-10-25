// Rain streaks by strength (D207; Tom: "the direction of raindrops should be determined by the wind direction … small rain
// should look different"). Build 104 drew every drop as an upright billboard 0.55 m long: the drops moved slanted but
// stood vertical, and drizzle was the same streak as a downpour, only fewer. Here each drop is a streak along its own
// velocity (the fall plus the wind's drift: slanted, its length the distance it moves in ~1/18 s), facing the camera
// around that axis; Atmosphere sets the speed, length, width and opacity by the rain's strength — drizzle short, fine
// and slow, a downpour long, heavier and fast.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, world_vertex_coords;

#include "res://Game/Rewrite/RainStreak.gdshaderinc"
