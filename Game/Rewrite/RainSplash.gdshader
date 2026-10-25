// Raindrop splashes on water (round 28, Tom: "that splash effect, drops bouncing off the water surface"): each
// particle is a tiny crown at the surface — a ring spreading out and a few droplets thrown up and falling back —
// over a third of a second. Upright billboards (they turn with the camera around the vertical only).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

uniform vec3 tint : source_color = vec3(0.75, 0.8, 0.85);
// (D207: splashes on the ground, roofs and leaves: a weaker ring — spray, not a ring spreading on water — and smaller)
uniform float ring_k = 1.0;
varying float age;
varying float seed;
varying float hard; // (D213: 1 a hard surface — roof, rock: a bright crown; ~0.35 ground, ~0.1 leaf litter: small and dull)

void vertex() {
	age = INSTANCE_CUSTOM.y;
	hard = ring_k < 0.99 ? COLOR.r : 1.0; // (on water, ring_k 1: as before)
	seed = fract(sin(dot(MODEL_MATRIX[3].xz, vec2(12.9898, 78.233))) * 43758.5453);
	vec3 right = normalize(vec3(INV_VIEW_MATRIX[0].x, 0.0, INV_VIEW_MATRIX[0].z) + vec3(1e-4, 0.0, 0.0));
	vec3 back = cross(right, vec3(0.0, 1.0, 0.0));
	mat4 world = mat4(vec4(right, 0.0), vec4(0.0, 1.0, 0.0, 0.0), vec4(back, 0.0), MODEL_MATRIX[3]);
	VERTEX.y += 0.5 * 0.16; // the crown stands on the surface
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 uv = vec2(UV.x * 2.0 - 1.0, 1.0 - UV.y); // x −1..1, y 0 bottom .. 1 top
	float a = clamp(age, 0.0, 1.0);
	// The ring on the water, seen at a slant (an ellipse), spreading and fading.
	float r = 0.15 + 0.75 * a;
	float ring = exp(-pow((length(vec2(uv.x, (uv.y - 0.06) * 5.0)) - r) * 10.0, 2.0)) * (1.0 - a) * step(uv.y, 0.2);
	// Droplets thrown up and falling back (a parabola each).
	float drops = 0.0;
	for (int i = 0; i < 5; i++) {
		float fi = float(i);
		float h = fract(seed * 7.13 + fi * 0.37);
		float dx = (fi / 4.0 - 0.5) * (0.9 + 0.4 * h) * (0.25 + 0.75 * a);
		float up = (1.6 + 1.2 * h) * a - 2.6 * a * a; // rises, then falls
		vec2 p = vec2(dx, 0.05 + up * 0.6);
		vec2 d = uv - p;
		drops += exp(-dot(d, d) * (380.0 - 120.0 * h)) * step(0.0, up);
	}
	float alpha = clamp(ring * 0.8 * ring_k + drops * 1.3 * mix(0.35, 1.0, hard), 0.0, 1.0) * (1.0 - smoothstep(0.7, 1.0, a));
	alpha *= mix(0.45, 1.0, hard);
	if (alpha < 0.02) discard;
	ALBEDO = tint * (1.15 + drops * 0.5);
	ALPHA = alpha * 0.85;
}
