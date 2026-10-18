// The scan's wave (D134; Tom: "like sonar — the waves grow from the player to the scan's range and change the
// surrounding colour, a small glow"). A screen-filling quad: each pixel's world position from the depth buffer; a bright
// band where its distance (in plan) from the scan's origin is the wave's radius, a faint tint behind it that fades.
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec3 origin;            // scene coordinates of the scan's centre
uniform float radius = 0.0;     // the wave's front (m)
uniform float max_radius = 45.0;
uniform float band = 3.0;       // the front's width (m)
uniform float tint = 0.0;       // the glow left behind it (0 … 1, fading)
uniform float fade = 1.0;
uniform vec4 colour : source_color = vec4(0.45, 0.85, 1.0, 1.0);

void vertex() {
	POSITION = vec4(VERTEX.xy * 2.0, 1.0, 1.0);
}

void fragment() {
	float depth = texture(depth_texture, SCREEN_UV).x;
	vec3 ndc = vec3(SCREEN_UV * 2.0 - 1.0, depth);
	vec4 world = INV_VIEW_MATRIX * INV_PROJECTION_MATRIX * vec4(ndc, 1.0);
	vec3 p = world.xyz / world.w;
	float d = distance(p.xz, origin.xz);
	float sky = step(0.0000001, depth);                     // (no glow on the sky)
	float front = smoothstep(radius - band, radius, d) * (1.0 - smoothstep(radius, radius + 0.4, d));
	float behind = (1.0 - smoothstep(radius - 0.5, radius, d)) * tint;
	float edge = 1.0 - smoothstep(max_radius * 0.85, max_radius, d);
	// (a faint ripple of rings inside, as sonar does)
	float rings = 0.5 + 0.5 * sin(d * 1.3 - radius * 1.3);
	ALBEDO = colour.rgb;
	ALPHA = clamp((front * 0.75 + behind * (0.08 + 0.05 * rings)) * edge * fade * sky, 0.0, 1.0);
}
