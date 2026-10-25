// Wind wisps as swooshes (D204; Tom on build 99's ribbons: "maybe we should fix the wind wisps"). Build 99 drew each
// wisp as a 6 cm cross-shaped ribbon trail ~10 m long: a hard, straight line, scratched across the screen up close and
// a thin wire edge-on. Here each particle is a short, soft stroke that draws itself along the wind and fades from its
// tail: a camera-facing strip bent into a gentle S, some ending in a curl rolling over, tapered at both ends.
// The mesh (AmbientLife.SwooshMesh) is a strip: UV.x = position along the stroke (0..1), UV.y = side (0 / 1).
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, world_vertex_coords;

uniform vec3 wind_dir = vec3(1.0, 0.0, 0.0); // horizontal, normalized
uniform float stroke_len = 9.0;               // m
uniform float width = 0.3;                    // m, at the thickest
uniform vec4 colour : source_color = vec4(0.95, 0.95, 0.95, 0.25); // (AmbientLife: the light's colour)

varying float v_alpha;
varying float v_side;

float hash(float n) { return fract(sin(n * 127.1 + 311.7) * 43758.5453); }

// The stroke's centre line at s (0..1), relative to its start. r: per-wisp random numbers.
vec3 path(float s, vec4 r) {
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 n = normalize(cross(up, wind_dir));
	float len = stroke_len * (0.7 + 0.6 * r.x);
	vec3 p = wind_dir * len * s
		+ n * (0.35 + 0.5 * r.y) * sin(s * 3.14159 * (1.2 + r.z) + r.w * 6.28)
		+ up * (0.25 + 0.3 * r.z) * sin(s * 3.14159 + r.x * 3.0);
	// About half end in a curl rolling over (up and back), ~0.6–1.1 m across: an open hook (a full turn read as a ring).
	float curl_from = 0.68;
	if (r.w > 0.5 && s > curl_from) {
		float rad = 0.3 + 0.25 * r.y;
		float th = (s - curl_from) / (1.0 - curl_from) * 3.9;
		float s0 = curl_from;
		vec3 p0 = wind_dir * len * s0
			+ n * (0.35 + 0.5 * r.y) * sin(s0 * 3.14159 * (1.2 + r.z) + r.w * 6.28)
			+ up * (0.25 + 0.3 * r.z) * sin(s0 * 3.14159 + r.x * 3.0);
		p = p0 + wind_dir * rad * sin(th) + up * rad * (1.0 - cos(th)) + n * 0.15 * th;
	}
	return p;
}

void vertex() {
	// (ParticleProcessMaterial: CUSTOM.y counts up to CUSTOM.w, which the lifetime randomness shortens)
	float life = clamp(INSTANCE_CUSTOM.y / max(INSTANCE_CUSTOM.w, 0.01), 0.0, 1.0);
	float id = float(INSTANCE_ID) + INSTANCE_CUSTOM.w * 17.0;
	vec4 r = vec4(hash(id), hash(id + 1.7), hash(id + 3.1), hash(id + 5.3));
	vec3 origin = MODEL_MATRIX[3].xyz;
	float s = UV.x;
	vec3 c = origin + path(s, r);
	vec3 t = normalize(path(min(s + 0.01, 1.0), r) - path(max(s - 0.01, 0.0), r));
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - c);
	vec3 side = normalize(cross(t, to_cam) + vec3(1e-4));
	// Drawn on, then wiped from the tail: the head leads over the first 60 % of the life, the tail follows.
	float head = clamp(life / 0.6, 0.0, 1.0), tail = clamp((life - 0.3) / 0.7, 0.0, 1.0);
	float span = max(head - tail, 1e-3);
	float k = clamp((s - tail) / span, 0.0, 1.0);
	float shown = step(tail, s) * step(s, head);
	float w = width * (0.6 + 0.8 * r.y) * sin(k * 3.14159) * shown;
	VERTEX = c + side * w * (UV.y - 0.5);
	// Fade near the camera (never a stroke across the screen) and far away.
	float d = distance(c, CAMERA_POSITION_WORLD);
	v_alpha = shown * smoothstep(0.0, 0.25, k) * smoothstep(1.0, 0.6, k) * smoothstep(4.0, 9.0, d) * (1.0 - smoothstep(30.0, 45.0, d));
	v_side = UV.y;
}

void fragment() {
	float edge = 1.0 - abs(v_side * 2.0 - 1.0);
	ALBEDO = colour.rgb;
	ALPHA = colour.a * v_alpha * smoothstep(0.0, 0.8, edge);
}
