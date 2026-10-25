// Dust devil (D194): a whirling column of dust, wider and thinner toward the top, leaning a little with the wind. Each
// particle orbits the column's axis at its own height; the column's base is the emitter (local coordinates).
shader_type particles;

uniform float height = 30.0;      // column height (m)
uniform float base_radius = 2.2;  // radius at the ground (m)
uniform float top_radius = 9.0;   // radius at the top (m)
uniform float spin = 2.2;         // turns per second at the base (radians/s ÷ 2π roughly)
uniform vec3 lean = vec3(0.0);    // the top's offset downwind (m)
uniform float strength = 1.0;     // 0..1: building up and dying away

float hash(float n) { return fract(sin(n) * 43758.5453); }

void start() {
	float id = float(INDEX) + float(RANDOM_SEED % 1000u) * 0.001;
	CUSTOM.x = hash(id * 1.7) * 6.2832;          // angle
	CUSTOM.y = pow(hash(id * 3.1), 1.6);         // height fraction (more near the ground)
	CUSTOM.z = 0.7 + 0.6 * hash(id * 5.3);       // radius jitter
	CUSTOM.w = 0.0;
	TRANSFORM = mat4(1.0);
}

void process() {
	float h = CUSTOM.y;
	// faster low down, the column's dust rising slowly
	CUSTOM.x += DELTA * spin * mix(1.0, 0.45, h) * 6.2832 / max(1.0 + 3.0 * h, 1.0);
	CUSTOM.y = min(h + DELTA * 0.04, 1.0);
	float r = mix(base_radius, top_radius, h * h) * CUSTOM.z;
	vec3 p = vec3(cos(CUSTOM.x) * r, h * height, sin(CUSTOM.x) * r) + lean * h;
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	float life = LIFETIME > 0.0 ? clamp(CUSTOM.w / LIFETIME, 0.0, 1.0) : 0.0;
	CUSTOM.w += DELTA;
	float fade = smoothstep(0.0, 0.15, life) * (1.0 - smoothstep(0.75, 1.0, life));
	COLOR.a = fade * strength * mix(0.9, 0.2, h);
	// bigger, softer puffs higher up
	float s = mix(1.2, 3.0, h);
	TRANSFORM[0].x = s; TRANSFORM[1].y = s; TRANSFORM[2].z = s;
}
