// Rocks, boulders and cliff slabs: flat-shaded facets (vertex colour: stone, moss on top) with the tileable pixel rock
// texture projected in world space from three sides, tinted per instance with the biome's rock colour (MultiMesh
// colour), so rocks match the cliffs and ground around them.
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2DArray rock_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform float tile = 4.0;        // metres per repeat of the texture
uniform float second_tile = 0.0; // above 0: a second layer, turned, at this many metres (rocks tens of metres tall, D268)
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
	// Colours are sRGB like the terrain palette (which converts at the end too).
	ALBEDO = pow(clamp(COLOR.rgb * t, 0.0, 1.0), vec3(2.2));
	ROUGHNESS = 0.9;
	if (rain_now.y > 0.5) {
		float k = smoothstep(0.35, 0.65, rain_wetness.x + (t.r - 0.5) * 0.25) * mix(0.55, 1.0, max(world_n.y, 0.0));
		ALBEDO *= 1.0 - 0.45 * k;
		ROUGHNESS = mix(0.9, 0.3, k);
	}
}
