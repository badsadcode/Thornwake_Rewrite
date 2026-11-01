// Heat shimmer over lava (D255; Noor: "heat distortion shimmering above the flow"): a full-screen pass that wobbles the
// picture where the air above hot lava would be. For each pixel the ground point is rebuilt from the depth and looked up
// in the lava map (LavaMap.cs); the pixels a little above lava on screen (the hot air rising in front of what lies
// behind) take it too, fading upward. The wobble is small rising noise — strongest close by, gone past 1.5 km.
// Setting fx.heat_shimmer; drawn only near an active volcano.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
global uniform sampler2D lava_map : filter_linear, repeat_disable;
global uniform vec4 lava_frame;
uniform float strength = 1.0;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float h2(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
	return mix(mix(h2(i), h2(i + vec2(1.0, 0.0)), u.x), mix(h2(i + vec2(0.0, 1.0)), h2(i + vec2(1.0, 1.0)), u.x), u.y);
}

// How hot the ground is at a screen point (0 … 1), faded with its distance.
float heat_at(vec2 uv, mat4 inv_proj, mat4 inv_view) {
	float depth = textureLod(depth_tex, uv, 0.0).r;
	vec4 view = inv_proj * vec4(uv * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	float dist = length(view.xyz);
	if (dist > 1500.0) return 0.0;
	vec3 world = (inv_view * vec4(view.xyz, 1.0)).xyz;
	vec2 m = (world.xz - lava_frame.xy) / lava_frame.z + 0.5;
	if (m.x <= 0.0 || m.y <= 0.0 || m.x >= 1.0 || m.y >= 1.0) return 0.0;
	vec2 lv = textureLod(lava_map, m, 0.0).rg;
	return max(lv.x, lv.y * 0.6) * (1.0 - smoothstep(250.0, 1500.0, dist));
}

void fragment() {
	if (lava_frame.w <= 0.0) discard;
	float heat = heat_at(SCREEN_UV, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX);
	// the hot air rising in front of what lies above the lava on screen
	// (8 steps, the pixel's own sub-step jittered: 4 steps drew bands)
	float jit = h2(FRAGCOORD.xy + fract(TIME) * 61.0);
	for (int k = 1; k <= 8; k++) {
		float t = (float(k) - jit) / 8.0;
		vec2 below = SCREEN_UV + vec2(0.0, t * 0.08);
		if (below.y < 1.0) heat = max(heat, heat_at(below, INV_PROJECTION_MATRIX, INV_VIEW_MATRIX) * (1.0 - t * 0.85));
	}
	if (heat < 0.02) discard;
	// rising, wavering ripples (screen space: as heat haze looks)
	vec2 q = SCREEN_UV * vec2(VIEWPORT_SIZE.x / VIEWPORT_SIZE.y, 1.0);
	vec2 wobble = vec2(n2(q * 55.0 + vec2(0.0, TIME * 2.6)) - 0.5, n2(q * 48.0 + vec2(13.1, TIME * 3.1)) - 0.5)
		+ 0.5 * vec2(n2(q * 130.0 + vec2(5.0, TIME * 5.0)) - 0.5, n2(q * 120.0 + vec2(9.0, TIME * 4.4)) - 0.5);
	vec2 off = wobble * 0.0045 * heat * strength;
	ALBEDO = textureLod(screen_tex, SCREEN_UV + off, 0.0).rgb;
	ALPHA = smoothstep(0.02, 0.15, heat);
}
