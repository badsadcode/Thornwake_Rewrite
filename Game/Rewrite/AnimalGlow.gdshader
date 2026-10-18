// A scanned animal's glow (D134): a rim of light around its outline, seen through grass and trees, fading out.
// Laid over the animal's own materials (material_overlay).
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_back, fog_disabled, shadows_disabled;

uniform vec4 colour : source_color = vec4(1.0, 0.62, 0.25, 1.0);
uniform float strength = 1.0;

void fragment() {
	float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float rim = pow(1.0 - facing, 2.0);
	ALBEDO = colour.rgb;
	ALPHA = clamp((rim * 0.95 + 0.1) * strength, 0.0, 1.0);
}
