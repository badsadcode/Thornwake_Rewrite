// Pixel-art plant sprites (Flora.cs, round 22): crossed quads for grass, reeds and underwater plants, flat quads for
// floating leaves. Per instance: COLOR = tint (biome, patch freshness); CUSTOM = (texture layer, motion: 0 floating,
// 1 in the wind, 2 underwater; phase 0..1; unused). Lit on both faces with leaf translucency against the sun.
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_burley;

uniform sampler2DArray flora_tex : source_color, filter_linear_mipmap, repeat_disable;
global uniform float leaf_translucency;
global uniform vec4 sun_glow;
global uniform vec3 sun_dir_world;
global uniform vec4 wind; // x, z: direction; y: strength (0 calm … 1 fresh … 2.5 storm); w: gust

varying float layer;
varying vec3 world_position;

void vertex() {
	layer = INSTANCE_CUSTOM.x;
	float motion = INSTANCE_CUSTOM.y;
	float phase = INSTANCE_CUSTOM.z * 6.2832;
	vec3 origin = MODEL_MATRIX[3].xyz;
	// Height share of the vertex (quads are 1 m in model space): roots stay put, tips move.
	float w = clamp(VERTEX.y, 0.0, 1.0);
	w *= w;
	if (motion > 1.5) {
		// Underwater: slow swaying, the same way across a bed (a gentle swell), each plant a little out of step.
		float s = sin(TIME * 0.7 + dot(origin.xz, vec2(0.08, 0.05)) + phase * 0.4);
		VERTEX.x += s * 0.12 * w;
		VERTEX.z += cos(TIME * 0.55 + phase) * 0.06 * w;
	} else if (motion > 0.5) {
		// Wind: lean (more in gusts) plus flutter, with gust waves travelling across the field.
		float wave = sin(dot(origin.xz, wind.xz) * 0.15 - TIME * (1.5 + wind.y)) * 0.5 + 0.5;
		float lean = (0.03 + 0.09 * wind.y) * (0.6 + 0.4 * wind.w) * (0.5 + 0.5 * wave);
		vec3 local_wind = (inverse(MODEL_MATRIX) * vec4(wind.x, 0.0, wind.z, 0.0)).xyz;
		VERTEX.xz += (local_wind.xz * lean + vec2(sin(TIME * 1.7 + phase), sin(TIME * 1.3 + phase * 1.3)) * 0.025 * (0.3 + wind.y)) * w;
	} else {
		// Floating: a slight bob.
		VERTEX.y += sin(TIME * 0.9 + phase) * 0.01;
	}
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 c = texture(flora_tex, vec3(UV, layer));
	// (the painted sprites are bright and saturated next to the terrain: brought down to the ground's level)
	// and a quarter desaturated (seed heads and cattails went orange under the game's saturation setting).
	vec3 col = mix(vec3(dot(c.rgb, vec3(0.3, 0.59, 0.11))), c.rgb, 0.75);
	ALBEDO = col * COLOR.rgb * 0.64;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = 1.0;
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	BACKLIGHT = ALBEDO * vec3(0.9, 1.05, 0.6) * leaf_translucency * 0.45;
	vec3 view_ray = normalize(world_position - CAMERA_POSITION_WORLD);
	float forward = pow(clamp(dot(view_ray, sun_dir_world), 0.0, 1.0), 5.0);
	EMISSION = ALBEDO * vec3(0.95, 1.1, 0.5) * sun_glow.rgb * forward * leaf_translucency * 0.12;
}
