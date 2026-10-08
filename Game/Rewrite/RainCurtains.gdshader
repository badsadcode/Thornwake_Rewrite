// Distant rain (round 27, Tom: "rain from clouds in the distance should be visible, so I can tell the weather there is
// worse"): each pixel follows its view ray across the weather map (WeatherMap.cs) from ~1.5 km (closer rain is the
// particles') out to 60 km or the scene, and below the cloud base gathers grey rain where it rains there — streaky
// curtains hanging from the cloud, slanted by the wind, darker under thunderstorms. Full-screen quad after the scene.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
global uniform sampler2D weather_map : filter_linear, repeat_disable;
global uniform vec4 weather_frame;
global uniform vec4 wind;
uniform float cloud_base = 1500.0;   // world height of the cloud base (m)
uniform vec3 rain_color : source_color = vec3(0.5, 0.53, 0.57); // lit by the sky (Atmosphere sets it with the daylight)
uniform float strength = 1.0;

const int STEPS = 28;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float h21(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float n2(vec2 p) {
	vec2 i = floor(p), f = fract(p), u = f * f * (3.0 - 2.0 * f);
	return mix(mix(h21(i), h21(i + vec2(1.0, 0.0)), u.x), mix(h21(i + vec2(0.0, 1.0)), h21(i + vec2(1.0, 1.0)), u.x), u.y);
}

void fragment() {
	if (weather_frame.w < 0.5 || strength <= 0.0) discard;
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, max(depth, 1e-7), 1.0);
	view.xyz /= view.w;
	float scene = depth < 1e-6 ? 60000.0 : min(length(view.xyz), 60000.0);
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(normalize(view.xyz), 0.0)).xyz);
	vec3 cam = CAMERA_POSITION_WORLD;
	float start = 1500.0;
	// Only below the cloud base.
	float to_base = dir.y > 0.0005 ? (cloud_base - cam.y) / dir.y : 1e9;
	float end = min(scene, to_base);
	if (end <= start || cam.y > cloud_base + 3000.0) discard;
	vec2 wd = normalize(wind.xz + vec2(1e-4));
	float jitter = h21(FRAGCOORD.xy + fract(TIME) * 37.0);
	float acc = 0.0, storm = 0.0, prev = start;
	for (int i = 1; i <= STEPS; i++) {
		float u = (float(i) - jitter) / float(STEPS);
		float t = mix(start, end, u * u);
		float dt = t - prev; prev = t;
		vec3 p = cam + dir * t;
		float below = cloud_base - p.y;
		if (below < 0.0) continue;
		vec3 w = texture(weather_map, weather_frame.xy + (p.xz - cam.xz) * weather_frame.z).rgb;
		if (w.g < 0.03) continue;
		// Curtains: vertical streaks (columns in x/z), leaning downwind lower down, thicker in the cores.
		vec2 q = p.xz - wd * below * 0.35 * min(wind.y, 2.0);
		float streak = 0.35 + 0.65 * n2(q / 700.0) * (0.6 + 0.4 * n2(q / 180.0 + 7.3));
		float dens = smoothstep(0.03, 0.6, w.g) * streak * (0.75 + 0.25 * smoothstep(0.0, 900.0, below));
		acc += dens * dt / 5500.0;
		storm = max(storm, w.b * dens);
	}
	float alpha = (1.0 - exp(-acc)) * strength;
	if (alpha < 0.004) discard;
	ALBEDO = rain_color * (1.0 - 0.45 * storm);
	ALPHA = clamp(alpha, 0.0, 0.92);
}
