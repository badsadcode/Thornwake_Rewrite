// Cloud renderer v2 (round 29, docs/SKY_IDEAS.md "Ideas from other Godot cloud projects", item 1): the cloud volume
// in front of the scene. The sky can only draw clouds behind everything, so a cloud could never hang in front of a
// mountain, wrap a summit or swallow the player; this full-screen pass marches the same volume (SkyCommon.gdshaderinc)
// from the camera to the scene's depth, over geometry only — the sky pixels keep the sky's own half-resolution march.
// Rays that never reach the cloud layer (most of them, under a high deck) cost one slab test.
shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
// The sun light as the sky sees it (LIGHT0): colour (rgb, as set on the light: sRGB) and energy (a); direction toward it (xyz).
global uniform vec4 cloud_light;
global uniform vec4 cloud_light_dir;
// Godot's fog as it falls on the sky (Atmosphere.cs): colour (linear, × energy) and sky affect (0 = fog off); sun scatter,
// aerial perspective.
global uniform vec4 cloud_fog;
global uniform vec4 cloud_fog2;
// Cloud blend (setting): 0 = build 52; 1 = matched to the sky's clouds (round 31, Tom: "you can see the mountains' outline
// in the clouds", a band without clouds under the horizon, white clouds in front of the land under grey-blue ones above):
//   * land past ~47 km counted as sky (its reversed depth is below 1e-6 with the 90 km far plane) — neither pass drew
//     clouds over it, a cloudless band under the horizon; now only the cleared depth (0) is sky;
//   * the sun's colour is linear here as in the sky (it was the light's sRGB colour: paler, brighter, pink-white at sunset
//     where the sky's clouds glow orange);
//   * Godot's fog veils the sky, its clouds included, but not this pass: the same veil is applied here.
uniform int cloud_blend = 1;
// Test (--cloud-split-test): this pass also draws the sky's clouds on the left half of the screen, the sky on the right,
// so the two can be compared side by side at the middle.
uniform int split_test = 0;
vec3 srgb_to_lin(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c)); }
vec3 cloud_light_rgb() { return cloud_blend == 1 ? srgb_to_lin(cloud_light.rgb) : cloud_light.rgb; }
#define SKY_LIGHT0_COLOR cloud_light_rgb()
#include "res://Game/Rewrite/SkyCommon.gdshaderinc"

uniform int max_steps = 128;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	bool sky = cloud_blend == 1 ? depth <= 0.0 : depth < 1e-6;
	bool test = split_test == 1 && SCREEN_UV.x < 0.5;
	if ((sky && !test) || quality != 3) discard; // sky pixels: the sky draws them
	// Direction from the near plane (a sky pixel's depth 0 is at infinity); distance to the scene.
	vec4 near = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 1.0, 1.0);
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(normalize(near.xyz / near.w), 0.0)).xyz);
	float scene = 1e9;
	if (!sky) {
		vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		scene = length(view.xyz / view.w);
	}
	vec3 sun_dir = normalize(cloud_light_dir.xyz);
	vec3 sun_col = cloud_light_rgb() * cloud_light.a;
	float day = clamp(sun_dir.y * 4.0 + 0.35, 0.08, 1.0);
	vec4 v = volumetric_v2(dir, scene, cloud_jitter(FRAGCOORD.xy, TIME), sky ? 150 : max_steps, sun_dir, sun_col, day, dot(dir, sun_dir), cloud_state.z, cloud_state.w);
	if (v.a < 0.002) discard;
	// The sky's biome haze over its clouds (Sky.gdshader, "Biome haze"), here too: without it these clouds stayed
	// white in front of distant land while the same clouds in the sky above were veiled in dust, and the land's
	// silhouette showed as a hard seam (Tom, dumps 115949 / 120402, Needle Rocks). Premultiplied colour.
	vec3 haze = horizon_color * mix(vec3(1.0), sky_mood.rgb, 0.8) * day;
	v.rgb = mix(v.rgb, haze * v.a, sky_mood.a * 0.7 * (1.0 - smoothstep(0.0, 0.6, dir.y)));
	if (cloud_blend == 1 && cloud_fog.a > 0.0) {
		// Godot's sky fog (sky.glsl fog_process): the fog light colour, toward the pixel's own colour by the aerial
		// perspective, plus the sun scattered around it; mixed in by the sky affect.
		vec3 own = v.rgb / v.a;
		vec3 fog = mix(cloud_fog.rgb, own, cloud_fog2.y) + sun_col * pow(max(dot(dir, sun_dir), 0.0), 8.0) * cloud_fog2.x;
		v.rgb = mix(v.rgb, fog * v.a, cloud_fog.a);
	}
	ALBEDO = v.rgb;
	ALPHA = v.a;
}
