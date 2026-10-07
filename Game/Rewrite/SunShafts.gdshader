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
// Build 33 (Tom: "make god rays stand out a bit more without losing what we have"): 0 = the build 32 rays exactly;
// toward 1 the rays carry farther from the sun (slower falloff, longer march, more samples) and stay bright away from
// the sun, so they read as streaks across the scene instead of a glow around the disc. The per-sample cap is kept,
// so the sun itself does not grow into a blob.
uniform float reach = 0.0;

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

	int samples = reach > 0.0 ? 48 : 32;
	float decay = mix(0.955, 0.984, reach);
	vec2 uv = SCREEN_UV;
	vec2 delta = (sun_uv - uv) / float(samples) * mix(0.85, 1.0, reach);
	uv += delta * hash(FRAGCOORD.xy); // dither: no banding
	float light = 0.0, weight = 1.0;
	for (int i = 0; i < samples; i++) {
		uv += delta;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) break;
		float depth = textureLod(depth_tex, uv, 0.0).r;
		if (depth < 1e-6) { // sky (reverse-Z: the far plane is 0)
			vec3 c = textureLod(screen_tex, uv, 0.0).rgb;
			// Only very bright sky casts rays: the sun disc, its halo and sunlit cloud edges. Clouds (dimmer) block it,
			// so the sun breaks through gaps in rays; a clear sky gives rays past trees and mountains.
			// (round 24: relative to the threshold 0.9, a hazy low-sun sky was "bright" for a quarter of the screen and
			// gathered into a huge white disc — Tom: "the sun is like a huge blob"; now only the disc and the inner halo,
			// each sample capped)
			light += min(max(0.0, dot(c, vec3(0.3, 0.59, 0.11)) - 1.8), 3.0) * weight;
		}
		weight *= decay;
	}
	light /= float(samples);
	// The slower falloff gathers more in total; scale it back so reach lengthens the rays rather than just
	// brightening the whole screen (weight sums: 32 samples at 0.955 ~ 16.9, 48 at 0.984 ~ 33.4).
	light *= mix(1.0, 0.75, reach);
	// Stronger toward the sun, and only while the sun is above the horizon.
	float near_sun = 1.0 - smoothstep(0.0, 0.9, distance(SCREEN_UV, sun_uv));
	float elevation = smoothstep(-0.02, 0.12, sun_direction.y);
	float spread = mix(0.6, 1.1, reach); // reach: the rays keep more of their strength away from the sun
	ALBEDO = sun_color * light * intensity * on_screen * elevation * (spread + (2.0 - spread) * near_sun) * 2.6;
}
