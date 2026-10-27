// Rain in depth (D221; Tom and Noor's "Rain 2.1": near drops, finer and denser rain further out, a curtain far away;
// shader-based, following the wind, stable when the camera turns): a vertical cylinder around the camera (Atmosphere:
// one at ~22 m and one at ~45 m, beyond the particle rain's ~20 m) drawn with procedural streaks. A streak's place is
// the world angle around the camera and its height, so turning the view never moves the rain; the streaks lean by the
// wind's drift across the line of sight on each side and fall at the rain's speed; the scene hides them (depth test).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_front, shadows_disabled; // (fogged like the scene: further layers sink into the veil)

uniform float amount = 0.0;          // 0 … 1: how much rain this layer shows
uniform float radius = 22.0;         // m (the mesh's own; for the arc length)
uniform float cell = 0.12;           // m between streak columns along the arc
uniform float streak_len = 1.6;      // m
uniform float fall = 9.0;            // m/s
uniform vec2 drift = vec2(0.0);      // the wind's drift (m/s, world x, z)
uniform float clock = 0.0;           // s
uniform vec4 tint : source_color = vec4(0.8, 0.84, 0.9, 0.12);
uniform float seed = 1.0;

varying vec3 world_pos;
varying vec3 centre;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233)) + seed * 17.13) * 43758.5453); }

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	centre = MODEL_MATRIX[3].xyz;
}

void fragment() {
	if (amount <= 0.001) discard;
	vec2 rel = world_pos.xz - centre.xz;
	float ang = atan(rel.y, rel.x);
	vec2 tangent = vec2(-sin(ang), cos(ang));
	// arc length (m) and the height, sheared by the wind's drift along the cylinder on this side
	float lean = dot(drift, tangent) / max(fall, 0.5);
	// (a drop moves along the cylinder by lean per metre it falls: s + lean·y stays the same along its streak)
	float u = ang * radius + world_pos.y * lean;
	float v = world_pos.y + clock * fall;
	float col = floor(u / cell);
	float f = fract(u / cell);
	// each column: a few streaks at their own heights, only `amount` of them
	float h1 = hash(vec2(col, 1.0)), h2 = hash(vec2(col, 2.0));
	float rep = streak_len * (1.6 + 2.4 * h2); // (streaks closer together along a column: denser rain)
	float vv = fract((v + h1 * rep) / rep) * rep;
	float along = smoothstep(0.0, streak_len * 0.3, vv) * smoothstep(streak_len, streak_len * 0.7, vv);
	float on = step(hash(vec2(col, floor((v + h1 * rep) / rep))), amount);
	// across: a thin line at the column's own place, anti-aliased by its screen width
	float x = abs(f - (0.3 + 0.4 * h1));
	float aa = fwidth(u / cell);
	// (at least ~1 px wide: thinner, the temporal filter blended them away)
	float line = 1.0 - smoothstep(0.04, 0.04 + aa * 1.5, x);
	float a = line * along * on;
	// fade where the view is steep (looking up or down the cylinder) and at its top and bottom
	vec3 view = normalize(world_pos - CAMERA_POSITION_WORLD);
	a *= 1.0 - smoothstep(0.6, 0.9, abs(view.y));
	a *= smoothstep(-18.0, -8.0, world_pos.y - centre.y) * (1.0 - smoothstep(18.0, 28.0, world_pos.y - centre.y));
	if (a <= 0.002) discard;
	ALBEDO = tint.rgb;
	ALPHA = a * tint.a;
}
