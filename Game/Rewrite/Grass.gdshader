// Grass tufts and reeds in the rewrite world: the valley tuft's vertex colours (sRGB), lit on both faces, leaf
// translucency against the sun, and a light breeze bending the tips (more with the wind setting).
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_burley;

global uniform float leaf_translucency;
// Rain (D212, Wet look by material): wet blades a richer, darker green - no shine (no plastic leaves).
global uniform vec2 rain_wetness;
global uniform vec4 rain_now;
// (D214: tufts under a roof or an overhang stay dry — the rain's cover map, as the terrain)
global uniform sampler2D rain_cover : filter_nearest, repeat_enable;
varying float dry_cover;
global uniform vec4 sun_glow;
global uniform vec3 sun_dir_world;
varying vec3 world_position;
global uniform vec4 wind; // x, z: direction; y: strength (0 calm … 1 fresh … 2.5 storm); w: gust
#include "res://Game/Rewrite/WindSway.gdshaderinc"
varying float sheen;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float w = clamp(VERTEX.y / 0.5, 0.0, 1.0);
	w *= w;
	float phase = origin.x * 0.21 + origin.z * 0.17;
	sheen = 0.0;
	if (wind_sim()) {
		// Round 27: gust waves sweep the field, the blades lean with the wind and swing slowly (WindSway).
		vec3 b = wind_bend(origin.xz, phase * 6.0, 0.5, 0.65 + 0.2 * fract(phase * 7.3));
		VERTEX += inverse(mat3(MODEL_MATRIX)) * b * w;
		sheen = wind_gust(origin.xz) * min(1.0, wind_here(origin.xz).z);
	} else {
		// Lean with the wind (more in gusts) plus a flutter; travelling gust waves across the field.
		float wave = sin(dot(origin.xz, wind.xz) * 0.15 - TIME * (1.5 + wind.y)) * 0.5 + 0.5;
		float lean = (0.02 + 0.07 * wind.y) * (0.6 + 0.4 * wind.w) * (0.5 + 0.5 * wave);
		VERTEX.x += (wind.x * lean + sin(TIME * 1.7 + phase) * 0.02 * (0.3 + wind.y)) * w;
		VERTEX.z += (wind.z * lean + sin(TIME * 1.3 + phase * 1.3) * 0.015 * (0.3 + wind.y)) * w;
	}
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	dry_cover = 0.0;
	if (rain_now.z > 0.5 && rain_now.y > 0.5) {
		vec2 c = floor(origin.xz);
		vec4 v = texelFetch(rain_cover, ivec2(mod(c, 40.0)), 0);
		dry_cover = v.a > 0.5 && v.g == c.x && v.b == c.y && v.r > origin.y + 0.3 ? 1.0 : 0.0;
	}
}

void fragment() {
	vec3 c = COLOR.rgb;
	ALBEDO = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
	ALBEDO *= 1.0 + sheen * vec3(0.16, 0.15, 0.08); // bent blades show their paler sides: the gusts read as waves
	if (rain_now.y > 0.5) {
		float wg = smoothstep(0.2, 0.5, rain_wetness.x) * (1.0 - dry_cover);
		float l = dot(ALBEDO, vec3(0.2126, 0.7152, 0.0722));
		ALBEDO = mix(ALBEDO, max(mix(vec3(l), ALBEDO, 1.18), vec3(0.0)) * 0.8, wg);
	}
	ROUGHNESS = 1.0;
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	BACKLIGHT = ALBEDO * vec3(0.9, 1.08, 0.55) * leaf_translucency;
	{
		vec3 view_ray = normalize(world_position - CAMERA_POSITION_WORLD);
		float forward = pow(clamp(dot(view_ray, sun_dir_world), 0.0, 1.0), 5.0);
		EMISSION += ALBEDO * vec3(0.95, 1.1, 0.5) * sun_glow.rgb * forward * leaf_translucency * 0.32 * clamp(COLOR.g * 1.5, 0.3, 1.0);
	}
}
