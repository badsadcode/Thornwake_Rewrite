// The rain streak (D207), shared by RainStreak.gdshader (transparent) and RainStreakTaa.gdshader (hashed, for TAA).

uniform vec3 fall_dir = vec3(0.0, -1.0, 0.0); // the rain's velocity now, normalized (only a fallback, D208)
uniform float streak_len = 0.5;               // m
uniform float width = 0.01;                   // m
uniform vec4 tint : source_color = vec4(0.78, 0.83, 0.88, 0.2);

varying float v_alpha;

// Shelter (D211): the cover map around the camera (Atmosphere, 1 m cells over 40 m, toroidal): r = the first surface
// rain lands on (scene y), g, b = the cell it holds (scene x, z, floored), a = 1 once written.
uniform sampler2D cover_tex : filter_nearest, repeat_enable;
uniform int cover_on = 0;
float cover_at(vec2 xz) {
	vec2 c = floor(xz);
	vec4 v = texelFetch(cover_tex, ivec2(mod(c, 40.0)), 0);
	return v.a > 0.5 && v.g == c.x && v.b == c.y ? v.r : -1e9;
}
// Whether this drop came through something: along its path back up (against its velocity) at rises of 0, 1, 2.5 and
// 5 m, is there a surface above where it was? Straight down under a roof: yes; blown in under the windward edge: no; in
// the lee just outside a wall: yes (the rain shadow).
bool sheltered(vec3 o, vec3 axis) {
	float k = 1.0 / max(-axis.y, 0.2);
	for (int i = 0; i < 4; i++) {
		float rise = i == 0 ? 0.0 : i == 1 ? 1.0 : i == 2 ? 2.5 : 5.0;
		vec3 q = o - axis * rise * k;
		if (cover_at(q.xz) > o.y + rise + 0.05) return true;
	}
	return false;
}

float hash(float n) { return fract(sin(n * 91.3 + 17.1) * 43758.5453); }

void vertex() {
	vec3 o = MODEL_MATRIX[3].xyz;
	float id = float(INSTANCE_ID);
	float h = hash(id);
	vec3 to_cam = CAMERA_POSITION_WORLD - o;
	float d = length(to_cam);
	// Each streak along its own drop's velocity (D208, Tom: "the lines are always oriented the same way"): the particle's Y
	// axis follows its velocity (ParticleProcessMaterial align Y). At first every streak took the wind of this frame from
	// one uniform, while each drop keeps the velocity it was born with: all lines stayed parallel and turned together with
	// each gust, not with the drops' own motion.
	vec3 axis = MODEL_MATRIX[1].xyz;
	axis = dot(axis, axis) > 1e-8 ? normalize(axis) : fall_dir;
	vec3 side = normalize(cross(axis, to_cam / max(d, 1e-3)) + vec3(1e-5));
	// (a pixel is ~d × 0.0014 at 1080p: never thinner than ~1.2 pixels — thinner, they flickered; fainter instead)
	float w0 = width * (0.7 + 0.6 * h), w = max(w0, d * 0.0017);
	float len = streak_len * (0.75 + 0.5 * hash(id + 3.7));
	VERTEX = o + axis * len * (0.5 - UV.y) + side * w * (UV.x - 0.5);
	bool hidden = cover_on == 1 && sheltered(o, axis);
	if (hidden) VERTEX = o; // (a degenerate quad: nothing drawn)
	// Fades: in and out over the particle's life (no popping), close to the camera (no poles through the view), far away.
	float life = INSTANCE_CUSTOM.y;
	v_alpha = sqrt(w0 / w) * sin(clamp(life, 0.0, 1.0) * 3.14159) * smoothstep(1.0, 3.5, d) * (1.0 - smoothstep(16.0, 22.0, d))
		* (0.6 + 0.4 * hash(id + 9.1)) * (hidden ? 0.0 : 1.0);
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
