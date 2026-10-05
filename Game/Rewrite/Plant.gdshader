// Placeholder plants (palms, cacti, dead trees, acacia, jungle giants, swamp cypress, dry shrubs, moss, vines):
// crisp pixel textures like the woodland pack, alpha-scissor cut-outs, both faces lit, gentle wind sway for leaves
// and hanging strands, and leaf translucency (light shining through leaves seen against the sun).
// Vertex COLOR.r = sway weight (0 at the trunk base … 1 at frond tips / strand ends).
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_burley;

uniform sampler2D pixel_map : source_color, filter_nearest_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float leaves = 0.0;  // 1: foliage (translucent)
uniform float sway = 0.0;    // wind sway amplitude (m at weight 1)
global uniform float leaf_translucency;
global uniform vec4 sun_glow;
global uniform vec3 sun_dir_world;
varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (sway > 0.0) {
		vec3 origin = MODEL_MATRIX[3].xyz;
		float phase = origin.x * 0.31 + origin.z * 0.23;
		float w = COLOR.r * COLOR.r;
		VERTEX.x += sin(TIME * 1.1 + phase) * sway * w;
		VERTEX.z += sin(TIME * 0.83 + phase * 1.7) * sway * 0.6 * w;
		VERTEX.y += sin(TIME * 2.3 + phase + VERTEX.x) * sway * 0.15 * w;
	}
}

void fragment() {
	vec4 pixel = texture(pixel_map, UV) * tint;
	ALBEDO = pixel.rgb;
	ROUGHNESS = 0.93;
	ALPHA = pixel.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	// Translucency: thin leaves pass a warm-green share of the light reaching their back side.
	BACKLIGHT = pixel.rgb * vec3(0.9, 1.08, 0.55) * leaf_translucency * leaves;
	{
		vec3 view_ray = normalize(world_position - CAMERA_POSITION_WORLD);
		float forward = pow(clamp(dot(view_ray, sun_dir_world), 0.0, 1.0), 5.0);
		EMISSION += pixel.rgb * vec3(0.95, 1.1, 0.5) * sun_glow.rgb * forward * leaf_translucency * 0.32 * leaves;
	}
}
