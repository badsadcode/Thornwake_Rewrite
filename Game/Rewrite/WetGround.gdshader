// Ground that simulated water has run over: darker for a few minutes while it dries (multiplied over the terrain).
// Per vertex: COLOR.a = wetness 0..1 (already softened over neighbouring cells); a world-space noise threshold
// breaks the 0.5 m simulation grid so patches have natural, irregular edges.
shader_type spatial;
render_mode unshaded, blend_mul, depth_draw_never, cull_back, fog_disabled, shadows_disabled;

varying float wet;
varying vec2 world_xz;

float hash(vec2 q) { vec3 p3 = fract(vec3(q.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 q) {
	vec2 i = floor(q); vec2 f = fract(q); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	wet = COLOR.a;
	world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment() {
	float n = vnoise(world_xz * 1.7) * 0.6 + vnoise(world_xz * 5.3) * 0.4;
	float w = smoothstep(0.15 + 0.35 * n, 0.45 + 0.35 * n, clamp(wet, 0.0, 1.0));
	ALBEDO = mix(vec3(1.0), vec3(0.6, 0.57, 0.53), w * 0.85);
}
