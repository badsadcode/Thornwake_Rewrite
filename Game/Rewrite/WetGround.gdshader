// Ground that simulated water has run over: darker for a few minutes while it dries (multiplied over the terrain).
// Per vertex: COLOR.a = wetness 0..1.
shader_type spatial;
render_mode unshaded, blend_mul, depth_draw_never, cull_back, fog_disabled, shadows_disabled;

varying float wet;

void vertex() {
	wet = COLOR.a;
}

void fragment() {
	ALBEDO = mix(vec3(1.0), vec3(0.6, 0.57, 0.53), clamp(wet, 0.0, 1.0) * 0.85);
}
