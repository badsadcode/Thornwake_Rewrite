// The rain streak (D207), shared by RainStreak.gdshader (transparent) and RainStreakTaa.gdshader (hashed, for TAA).

uniform vec3 fall_dir = vec3(0.0, -1.0, 0.0); // the drops' velocity, normalized
uniform float streak_len = 0.5;               // m
uniform float width = 0.01;                   // m
uniform vec4 tint : source_color = vec4(0.78, 0.83, 0.88, 0.2);

varying float v_alpha;

float hash(float n) { return fract(sin(n * 91.3 + 17.1) * 43758.5453); }

void vertex() {
	vec3 o = MODEL_MATRIX[3].xyz;
	float id = float(INSTANCE_ID);
	float h = hash(id);
	vec3 to_cam = CAMERA_POSITION_WORLD - o;
	float d = length(to_cam);
	vec3 side = normalize(cross(fall_dir, to_cam / max(d, 1e-3)) + vec3(1e-5));
	// (a pixel is ~d × 0.0014 at 1080p: never thinner than ~1.2 pixels — thinner, they flickered; fainter instead)
	float w0 = width * (0.7 + 0.6 * h), w = max(w0, d * 0.0017);
	float len = streak_len * (0.75 + 0.5 * hash(id + 3.7));
	VERTEX = o + fall_dir * len * (0.5 - UV.y) + side * w * (UV.x - 0.5);
	// Fades: in and out over the particle's life (no popping), close to the camera (no poles through the view), far away.
	float life = INSTANCE_CUSTOM.y;
	v_alpha = sqrt(w0 / w) * sin(clamp(life, 0.0, 1.0) * 3.14159) * smoothstep(1.0, 3.5, d) * (1.0 - smoothstep(16.0, 22.0, d))
		* (0.6 + 0.4 * hash(id + 9.1));
}

void fragment() {
	float across = 1.0 - abs(UV.x * 2.0 - 1.0);
	float along = smoothstep(0.0, 0.3, UV.y) * smoothstep(1.0, 0.7, UV.y);
	ALBEDO = tint.rgb;
	ALPHA = tint.a * v_alpha * across * along;
#ifdef RAIN_HASHED
	// (the core near-solid: at a streak's low alpha the dither left dotted lines that TAA could not fill on drops this fast;
	// a thin solid line with dithered edges reads as rain, and TAA softens the edges)
	ALPHA = clamp(ALPHA * 3.0, 0.0, 1.0);
	ALBEDO *= 0.85;
	ALPHA_HASH_SCALE = 1.0;
#endif
}
