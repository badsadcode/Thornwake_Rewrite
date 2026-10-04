// Screen-space sun shafts ("god rays"): from each pixel, march toward the sun's screen position and gather the
// bright, unobstructed sky (depth = far plane) on the way. Mountains, trees and clouds in front of the sun cut the
// light into rays. Drawn on a full-screen quad after the scene (additive).
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0); // world space, toward the sun
uniform vec3 sun_color : source_color = vec3(1.0, 0.95, 0.85);
uniform float intensity = 0.6;

const int SAMPLES = 32;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
	vec3 sun_view = (VIEW_MATRIX * vec4(sun_direction, 0.0)).xyz;
	if (sun_view.z >= -0.05 || intensity <= 0.0) discard; // sun behind the camera
	vec4 clip = PROJECTION_MATRIX * vec4(sun_view, 0.0);
	vec2 sun_uv = clip.xy / clip.w * 0.5 + 0.5;
	// Fade out as the sun leaves the screen.
	vec2 off = max(vec2(0.0), max(-sun_uv, sun_uv - 1.0));
	float on_screen = 1.0 - smoothstep(0.0, 0.45, length(off));
	if (on_screen <= 0.0) discard;

	vec2 uv = SCREEN_UV;
	vec2 delta = (sun_uv - uv) / float(SAMPLES) * 0.85;
	uv += delta * hash(FRAGCOORD.xy); // dither: no banding
	float light = 0.0, weight = 1.0;
	for (int i = 0; i < SAMPLES; i++) {
		uv += delta;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) break;
		float depth = textureLod(depth_tex, uv, 0.0).r;
		if (depth < 1e-6) { // sky (reverse-Z: the far plane is 0)
			vec3 c = textureLod(screen_tex, uv, 0.0).rgb;
			light += max(0.0, dot(c, vec3(0.3, 0.59, 0.11)) - 0.45) * weight;
		}
		weight *= 0.955;
	}
	light /= float(SAMPLES);
	// Stronger toward the sun, and only while the sun is above the horizon.
	float near_sun = 1.0 - smoothstep(0.0, 0.9, distance(SCREEN_UV, sun_uv));
	float elevation = smoothstep(-0.02, 0.12, sun_direction.y);
	ALBEDO = sun_color * light * intensity * on_screen * elevation * (0.6 + 1.4 * near_sun) * 2.2;
}
