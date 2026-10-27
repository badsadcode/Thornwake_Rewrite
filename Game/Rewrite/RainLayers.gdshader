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
uniform float depth_scale = 25.0;    // m of rain behind the layer that make it full (D222)
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

varying vec3 world_pos;
varying vec3 centre;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233)) + seed * 17.13) * 43758.5453); }

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	centre = MODEL_MATRIX[3].xyz;
}

// One octave of streaks: columns `c` m apart, streaks `len` m long; `width` of a column; returns coverage.
float streaks(float u, float v, float c, float len, float width, float amt, float salt) {
	float col = floor(u / c);
	float f = fract(u / c);
	float h1 = hash(vec2(col, 1.0 + salt)), h2 = hash(vec2(col, 2.0 + salt));
	float rep = len * (1.6 + 2.4 * h2);
	float vv = fract((v + h1 * rep) / rep) * rep;
	float along = smoothstep(0.0, len * 0.3, vv) * smoothstep(len, len * 0.7, vv);
	float on = step(hash(vec2(col, floor((v + h1 * rep) / rep) + salt)), amt);
	float x = abs(f - (0.3 + 0.4 * h1));
	float aa = fwidth(u / c);
	float line = 1.0 - smoothstep(width, width + aa * 1.5, x);
	// (D222: columns closer than ~5 px fade out — packed thinner, the lines beat against the pixel grid in curved
	// moiré bands, Tom's dumps 184925 / 185035)
	return line * along * on * (1.0 - smoothstep(0.15, 0.3, aa));
}

void fragment() {
	if (amount <= 0.001) discard;
	vec2 rel = world_pos.xz - centre.xz;
	float ang = atan(rel.y, rel.x);
	vec2 tangent = vec2(-sin(ang), cos(ang));
	// arc length (m) and the height, sheared by the wind's drift along the cylinder on this side
	float lean = dot(drift, tangent) / max(fall, 0.5);
	// (a drop moves along the cylinder by lean per metre it falls: s + lean·y stays the same along its streak)
	// (the shear from the camera's height: from the world's zero, high up the columns were sheared tens of metres and
	// drew long arcs over the sky, Tom's dumps 184925 / 185035)
	float yrel = world_pos.y - centre.y;
	float u = ang * radius + yrel * lean;
	float v = world_pos.y + clock * fall;
	// fine streaks, and coarse soft ones (a rain curtain) that stay where the fine ones are too dense to draw
	float a = max(streaks(u, v, cell, streak_len, 0.04, amount, 0.0), 0.35 * streaks(u, v, cell * 6.0, streak_len * 1.2, 0.25, amount, 5.0));
	// (D222, Tom: "the border of the background rain is kind of abrupt … the circle is visible"): the layer stands for
	// the rain between it and what is behind — none against the ground just past it, more the further the land, full
	// against the sky; no edge where the ground crosses the cylinder
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 sv = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene = depth <= 0.0 ? 1e6 : length(sv.xyz / sv.w);
	a *= 1.0 - exp(-max(scene - length(VERTEX), 0.0) / depth_scale);
	// fade where the view is steep (looking up or down the cylinder)
	vec3 view = normalize(world_pos - CAMERA_POSITION_WORLD);
	a *= 1.0 - smoothstep(0.65, 0.92, abs(view.y));
	// and above ~10–25 m over the eye: the sky's own rain veil takes over (looking down a slope the layer goes on)
	a *= 1.0 - smoothstep(10.0, 25.0, yrel);
	if (a <= 0.002) discard;
	ALBEDO = tint.rgb;
	ALPHA = a * tint.a;
}
