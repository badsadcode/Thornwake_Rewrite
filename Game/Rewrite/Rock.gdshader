// Rocks, boulders and cliff slabs: flat-shaded facets (vertex colour: stone, moss on top) with the tileable pixel rock
// texture projected in world space from three sides, tinted per instance with the biome's rock colour (MultiMesh
// colour), so rocks match the cliffs and ground around them.
shader_type spatial;
render_mode diffuse_burley;

uniform sampler2D rock_tex : source_color, filter_nearest_mipmap, repeat_enable;
varying vec3 world_pos;
varying vec3 world_n;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_n = normalize(mat3(MODEL_MATRIX) * NORMAL);
}

void fragment() {
	vec3 w = pow(abs(world_n), vec3(4.0));
	w /= w.x + w.y + w.z;
	vec2 wrap = vec2(4096.0);
	vec3 tx = texture(rock_tex, mod(world_pos.zy, wrap) / vec2(4.0, -4.0)).rgb;
	vec3 ty = texture(rock_tex, mod(world_pos.xz, wrap) / 4.0).rgb;
	vec3 tz = texture(rock_tex, mod(world_pos.xy, wrap) / vec2(4.0, -4.0)).rgb;
	vec3 t = (tx * w.x + ty * w.y + tz * w.z) / 0.55;
	ALBEDO = COLOR.rgb * t;
	ROUGHNESS = 0.9;
}
