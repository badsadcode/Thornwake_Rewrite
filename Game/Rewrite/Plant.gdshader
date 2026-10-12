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
global uniform vec4 wind;
#include "res://Game/Rewrite/WindSway.gdshaderinc"
#include "res://Game/Rewrite/TreeFade.gdshaderinc"
varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (sway > 0.0 && wind_sim()) {
		// Round 27: shrubs and leafy plants bend with the shared gusts (WindSway), slower than grass.
		vec3 origin = MODEL_MATRIX[3].xyz;
		float phase = origin.x * 0.31 + origin.z * 0.23;
		float w = COLOR.r * COLOR.r;
		vec3 b = wind_bend(origin.xz, phase, sway * 4.0, 0.4 + 0.15 * fract(phase));
		VERTEX += inverse(mat3(MODEL_MATRIX)) * b * w;
		// Leaves rustle a little on their own.
		VERTEX.y += sin(TIME * 3.1 + phase + VERTEX.x * 2.0) * sway * 0.05 * w * wind_field.y;
	} else if (sway > 0.0) {
		vec3 origin = MODEL_MATRIX[3].xyz;
		float phase = origin.x * 0.31 + origin.z * 0.23;
		float w = COLOR.r * COLOR.r;
		float strength = 0.35 + wind.y * (0.7 + 0.5 * wind.w);
		// In the instance's own frame: turn the world wind into local axes.
		vec3 local_wind = normalize((inverse(mat3(MODEL_MATRIX)) * vec3(wind.x, 0.0, wind.z)) + vec3(1e-4));
		VERTEX += local_wind * sway * strength * w * (0.6 + 0.4 * sin(TIME * 1.1 + phase));
		VERTEX.x += sin(TIME * 1.9 + phase) * sway * 0.3 * strength * w;
		VERTEX.z += sin(TIME * 1.5 + phase * 1.7) * sway * 0.2 * strength * w;
		VERTEX.y += sin(TIME * 2.3 + phase + VERTEX.x) * sway * 0.15 * w;
	}
}

void fragment() {
	if (tree_fade_out(world_position, FRAGCOORD.xy, tree_ranges.x)) discard; // hand-over to impostors (placeholder trees)
	if (tree_lod_out(world_position, FRAGCOORD.xy)) discard; // detailed ↔ lighter model (placeholder trees)
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
