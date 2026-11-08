// Rocks, boulders and cliff slabs: flat-shaded facets (vertex colour: stone, moss on top) with the tileable pixel rock
// texture projected in world space from three sides, tinted per instance with the biome's rock colour (MultiMesh
// colour), so rocks match the cliffs and ground around them.
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2DArray rock_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float tile = 4.0;        // metres per repeat of the texture
uniform float second_tile = 0.0; // above 0: a second layer, turned, at this many metres (rocks tens of metres tall, D268)
// Tom's cliff rocks (D273): on the sides of big rocks (their light comes from above: not on tops), every cliff_tile metres
// (0: not used). Six layers (his cliff rock, alpine rock, slate, granite, mossy stone, sandstone); each rock look of the
// biome has two of them, cliff_first and cliff_second, and each rock takes one by where it stands; cliff_means are their
// mean brightness (the texture brings the colour, the tint mostly the brightness).
uniform sampler2DArray cliff_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float cliff_tile = 0.0;
uniform int cliff_first[6];
uniform int cliff_second[6];
uniform float cliff_means[6];
varying flat float cliff_layer;
// Big rocks less bland (D273, Tom: "too dark, too brown ... break the blandness"): most of the biome's tint taken out, a
// little lighter, pale lichen in patches (now and then orange), dark streaks where water runs down.
uniform float big_look = 0.0;

float rock_hash(vec2 p) { p = mod(p, 997.0); vec3 q = fract(vec3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y) * q.z); }
float rock_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
	return mix(mix(rock_hash(i), rock_hash(i + vec2(1.0, 0.0)), u.x), mix(rock_hash(i + vec2(0.0, 1.0)), rock_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
// Rain (D212, Wet look by material): wet rock dark and glossy, the tops most; it sheds water and dries first.
global uniform vec2 rain_wetness;
global uniform vec4 rain_now;
varying flat float look;
varying vec3 world_pos;
varying vec3 world_n;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_n = normalize(mat3(MODEL_MATRIX) * NORMAL);
	look = clamp(round(COLOR.a * 8.0) - 1.0, 0.0, 7.0); // instance colour alpha: rock look + 1, in eighths
	if (cliff_tile > 0.0) {
		// (one texture a rock, picked by a slow noise over where it stands: whole stretches of cliff share one)
		vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		int lk = int(min(look, 5.0));
		cliff_layer = float(rock_noise(origin.xz / 260.0) > 0.58 ? cliff_second[lk] : cliff_first[lk]);
	}
}

void fragment() {
	vec3 w = pow(abs(world_n), vec3(4.0));
	w /= w.x + w.y + w.z;
	vec2 wrap = vec2(4096.0);
	vec3 tx = texture(rock_tex, vec3(mod(world_pos.zy, wrap) / vec2(tile, -tile), look)).rgb;
	vec3 ty = texture(rock_tex, vec3(mod(world_pos.xz, wrap) / tile, look)).rgb;
	vec3 tz = texture(rock_tex, vec3(mod(world_pos.xy, wrap) / vec2(tile, -tile), look)).rgb;
	vec3 t = (tx * w.x + ty * w.y + tz * w.z) / 0.5;
	if (second_tile > 0.0) {
		mat2 turn = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
		vec3 sx = texture(rock_tex, vec3(turn * mod(world_pos.zy, wrap) / second_tile, look)).rgb;
		vec3 sy = texture(rock_tex, vec3(turn * mod(world_pos.xz, wrap) / second_tile, look)).rgb;
		vec3 sz = texture(rock_tex, vec3(turn * mod(world_pos.xy, wrap) / second_tile, look)).rgb;
		t = mix(t, (sx * w.x + sy * w.y + sz * w.z) / 0.5, 0.45);
	}
	if (cliff_tile > 0.0) {
		float sides = w.x + w.z;
		vec3 cx = texture(cliff_tex, vec3(mod(world_pos.zy, wrap) / vec2(cliff_tile, -cliff_tile), cliff_layer)).rgb;
		vec3 cz = texture(cliff_tex, vec3(mod(world_pos.xy, wrap) / vec2(cliff_tile, -cliff_tile), cliff_layer)).rgb;
		vec3 cliff = (cx * w.x + cz * w.z) / max(sides, 1e-4) / cliff_means[int(cliff_layer)];
		cliff = mix(vec3(dot(cliff, vec3(0.333))), cliff, 0.45); // (less colour: the sandstone at full came out a hot orange-red)
		t = mix(t, cliff, smoothstep(0.2, 0.6, sides));
	}
	// Colours are sRGB like the terrain palette (which converts at the end too).
	vec3 base = COLOR.rgb;
	if (big_look > 0.0) {
		float grey = dot(base, vec3(0.333));
		base = mix(base, vec3(grey), 0.85 * big_look) * (1.0 + 0.12 * big_look);
		// (world-space noise, so neighbouring rocks agree; the side planes chosen like the texture)
		vec2 side = abs(world_n.x) > abs(world_n.z) ? world_pos.zy : world_pos.xy;
		float patches = rock_noise(side * 0.28 + world_pos.y * 0.05) * 0.6 + rock_noise(side * 0.9 + 7.0) * 0.4;
		float lichen = smoothstep(0.6, 0.7, patches) * (0.55 + 0.45 * max(world_n.y, 0.0));
		vec3 lichen_col = rock_noise(side * 0.05 + 3.0) > 0.78 ? vec3(0.78, 0.55, 0.3) : vec3(0.68, 0.7, 0.6);
		base = mix(base, lichen_col, lichen * 0.65 * big_look);
		float streak = smoothstep(0.55, 0.8, rock_noise(vec2(side.x * 0.45, world_pos.y * 0.012))) * (1.0 - max(world_n.y, 0.0));
		base *= 1.0 - 0.3 * streak * big_look;
	}
	ALBEDO = pow(clamp(base * t, 0.0, 1.0), vec3(2.2));
	ROUGHNESS = 0.9;
	if (rain_now.y > 0.5) {
		float k = smoothstep(0.35, 0.65, rain_wetness.x + (t.r - 0.5) * 0.25) * mix(0.55, 1.0, max(world_n.y, 0.0));
		ALBEDO *= 1.0 - 0.45 * k;
		ROUGHNESS = mix(0.9, 0.3, k);
	}
}
