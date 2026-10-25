// Raindrops on the lens (D217; Tom: "a spray, maybe optional, kind of like a screen effect of raindrops on the camera
// lens — not too big"): a full-screen pass after everything else. The screen is cut into cells; in each a drop appears
// at its own random moment, beads for a few seconds — some slide down a little and leave a faint trail — and dries
// away. A drop is a small lens: the picture behind it shows through upside-down-ish (sampled from the other side of
// its centre, blurred a little), with a dark rim and a highlight. How many: Atmosphere (the rain, cover, which way you
// look). Drawn only while drops are on the lens.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform float amount = 0.0;  // 0 none … 1 heavy rain on the lens
uniform float clock = 0.0;   // s, runs while drops are on
uniform float size = 1.0;    // drop size scale

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// One layer of drops: cells of `cell` (in screen heights); returns the offset to sample at (uv) and coverage.
vec3 layer(vec2 uv, float aspect, float cell, float seed, float rate) {
	vec2 p = vec2(uv.x * aspect, uv.y) / cell;
	vec2 c = floor(p);
	vec2 f = fract(p);
	float h = hash(c + seed);
	// each cell's drop: a life of 3–7 s, starting at its own phase; only `rate` of the cells hold one
	float life = 3.0 + 4.0 * hash(c + seed + 1.7);
	float t = clock / life + h * 7.0;
	float k = fract(t);
	float cycle = floor(t);
	float on = step(hash(c + seed + cycle * 3.1), rate);
	// where in the cell, how big; a third of them slide down a little as they grow heavy
	vec2 centre = vec2(0.25 + 0.5 * hash(c + seed + cycle), 0.3 + 0.4 * hash(c + seed + cycle + 5.3));
	float slide = step(0.66, hash(c + seed + cycle + 9.1)) * smoothstep(0.35, 0.9, k) * 0.35;
	centre.y += slide;
	float r = (0.18 + 0.14 * hash(c + seed + cycle + 2.9)) * size * (1.0 - smoothstep(0.75, 1.0, k) * 0.6);
	vec2 d = (f - centre) * vec2(1.0, 0.85 + 0.15 * (1.0 - slide * 2.0)); // (sliding drops a little longer)
	float dist = length(d) / r;
	float body = (1.0 - smoothstep(0.85, 1.0, dist)) * on * smoothstep(0.0, 0.08, k) * (1.0 - smoothstep(0.8, 1.0, k));
	// a faint trail above a sliding drop
	float trail = on * step(0.66, hash(c + seed + cycle + 9.1)) * smoothstep(0.0, 0.05, f.y - (centre.y - slide)) * step(f.y, centre.y)
		* (1.0 - smoothstep(0.0, r * 0.35, abs(f.x - centre.x))) * smoothstep(0.35, 0.9, k) * (1.0 - smoothstep(0.8, 1.0, k)) * 0.35;
	// the lens: sample across the centre, pulled in
	vec2 offset = -d * cell / vec2(aspect, 1.0) * 1.4 * body;
	return vec3(offset, max(body, trail));
}

void fragment() {
	if (amount <= 0.001) discard;
	vec2 uv = SCREEN_UV;
	float aspect = VIEWPORT_SIZE.x / VIEWPORT_SIZE.y;
	vec3 a = layer(uv, aspect, 0.075, 1.3, amount * 0.35);
	vec3 b = layer(uv, aspect, 0.045, 7.9, amount * 0.25);
	vec3 l = a.z >= b.z ? a : b;
	if (l.z <= 0.002) discard;
	vec3 behind = textureLod(screen_tex, uv + l.xy, 1.5).rgb;
	vec3 here = textureLod(screen_tex, uv, 0.0).rgb;
	// rim darker, a small highlight up-left
	float rim = smoothstep(0.5, 1.0, length(l.xy) * 40.0);
	vec3 drop = behind * (1.0 - 0.25 * rim) + vec3(0.06) * smoothstep(0.6, 1.0, dot(normalize(l.xy + 1e-5), vec2(0.7, 0.7)));
	ALBEDO = mix(here, drop, l.z);
	ALPHA = 1.0;
}
