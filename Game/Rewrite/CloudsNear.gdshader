// Cloud renderer v2 (round 29, docs/SKY_IDEAS.md "Ideas from other Godot cloud projects", item 1): the cloud volume
// in front of the scene. The sky can only draw clouds behind everything, so a cloud could never hang in front of a
// mountain, wrap a summit or swallow the player; this full-screen pass marches the same volume (SkyCommon.gdshaderinc)
// from the camera to the scene's depth, over geometry only — the sky pixels keep the sky's own half-resolution march.
// Rays that never reach the cloud layer (most of them, under a high deck) cost one slab test.
shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
// The sun light as the sky sees it (LIGHT0): colour (rgb) and energy (a); direction toward it (xyz).
global uniform vec4 cloud_light;
global uniform vec4 cloud_light_dir;
#define SKY_LIGHT0_COLOR cloud_light.rgb
#include "res://Game/Rewrite/SkyCommon.gdshaderinc"

uniform int max_steps = 128;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	if (depth < 1e-6 || quality != 3) discard; // sky pixels: the sky draws them
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	float scene = length(view.xyz);
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(normalize(view.xyz), 0.0)).xyz);
	vec3 sun_dir = normalize(cloud_light_dir.xyz);
	vec3 sun_col = cloud_light.rgb * cloud_light.a;
	float day = clamp(sun_dir.y * 4.0 + 0.35, 0.08, 1.0);
	vec4 v = volumetric_v2(dir, scene, cloud_jitter(FRAGCOORD.xy, TIME), max_steps, sun_dir, sun_col, day, dot(dir, sun_dir), cloud_state.z, cloud_state.w);
	if (v.a < 0.002) discard;
	ALBEDO = v.rgb;
	ALPHA = v.a;
}
