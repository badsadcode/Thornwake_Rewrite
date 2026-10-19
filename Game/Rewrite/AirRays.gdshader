// Atmosphere 2.0 M2, rays in open air (D148, Tom's references: shafts from a gap in the clouds over a fjord; shafts
// past mountain peaks at sunrise). Godot's volumetric fog only reaches a few hundred metres and the sun's shadow map
// less than that, so mountains and cloud gaps kilometres away cut no rays into it, and Godot's distance fog glows
// toward the sun whatever stands in the way. This pass works on that haze (Godot's colours and its aerial perspective
// stay): each pixel's view ray is marched through the air (up to the scene, or far into the sky), with the haze's
// opacity as Godot's fog has it, and
// * the haze in a mountain's or a cloud's shadow is darkened (the scene is drawn first; this quad mixes over it),
// * sunlit haze gets the sun's light scattered toward the eye — so shadowed air is darker than sunlit air: rays.
// (A first draft replaced Godot's fog with its fog light colour: at dawn most of that fog's colour is the sky's own,
// so long views went black.)
// The sun reaches a point when it is above the terrain's sun-shadow height there (a map around the player,
// WorldLab.AirRays) and not under a cloud toward the sun (the deck at its real height, as LightBeams.gdshader).
// Haze is thickest low down and close above the ground (valleys, lakes), thinning with height, so a clear peak stays
// crisp while the valleys below fill with shafts.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D air_map : filter_linear, repeat_disable; // r: ground height, g: sun-shadow height (scene metres)
uniform vec4 map_frame = vec4(0.0, 0.0, 1.0, 0.0); // xy: the camera's offset from the map's centre (m), z: map size (m)
uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform float cloud_period = 6000.0;
uniform float cloud_base = 1500.0; // scene height of the cloud bases
uniform float cloud_shade = 0.62;  // how much a cloud dims the sun, as the cloud shadows on the ground (setting)
uniform vec3 light_direction = vec3(0.0, 1.0, 0.0); // world space, toward the sun
uniform vec3 light_color : source_color = vec3(1.0, 0.95, 0.85); // includes the light's strength
uniform float density = 0.0001;    // haze at the camera's height, per metre (Godot's distance fog density)
uniform float sun_scatter = 0.0;   // extra glow toward the sun in sunlit haze (Godot's fog already has one)
uniform float side_scatter = 0.5;  // sunlit haze seen from the side: rays across the view
uniform float shade = 0.6;         // how much of the haze's light a mountain's or cloud's shadow takes away
uniform float gain = 1.0;          // sunlight in the haze (developer tuning)
uniform int steps = 40;
uniform float max_distance = 60000.0;
uniform float sea_level = 0.0;
uniform float haze_height = 1400.0; // the haze falls to a third over this height
uniform float valley_haze = 3.0;    // extra haze close above the ground (valleys, lakes, forests)
uniform int debug = 0;              // 1: the sunlit share of each view ray's haze, grey (developer tool)
global uniform vec4 cloud_state;
global uniform sampler2D weather_map : filter_linear, repeat_disable;
global uniform vec4 weather_frame;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// The sky's main deck (as LightBeams.gdshader): from a blurrier mip, a crisper edge than the sky's.
float cloud_density(vec2 xz, float coverage) {
	if (weather_frame.w > 0.5) coverage = mix(coverage, texture(weather_map, weather_frame.xy + (xz - cloud_state.xy) * weather_frame.z).r, 0.85);
	vec2 uv = xz / cloud_period;
	float n = textureLod(cloud_tex, uv, 2.0).r * 0.8 + textureLod(cloud_tex, uv * 4.0 + vec2(0.31, 0.17), 2.0).g * 0.2;
	float edge = 1.0 - coverage;
	return smoothstep(edge - 0.02, edge + 0.1, n);
}

float haze_shape(float y, float ground) {
	return exp(-max(y - sea_level, 0.0) / haze_height) * (1.0 + valley_haze * exp(-max(y - ground, 0.0) / 250.0));
}

void fragment() {
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	// The direction from a mid depth (at the far plane the division can fail: sky pixels came out black), the distance
	// from the scene's depth.
	vec4 mid = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, 0.5, 1.0);
	vec3 view_dir = normalize(mid.xyz / mid.w);
	float scene = max_distance;
	if (depth > 1e-6) {
		vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		scene = min(length(view.xyz / view.w), max_distance);
	}
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(view_dir, 0.0)).xyz);
	vec3 cam = CAMERA_POSITION_WORLD;
	vec3 l = normalize(light_direction);
	float sun_up = smoothstep(-0.02, 0.04, l.y);

	float coverage = cloud_state.z;
	float clouds = smoothstep(0.05, 0.2, coverage); // a clear sky casts no cloud shadows
	float lift = max(l.y, 0.05);
	float mu = dot(dir, l);
	// Godot's fog glow toward the sun (pow 8) and a broad part, so sunlit haze also shows from the side.
	vec3 sun_light = light_color * (sun_scatter * pow(max(mu, 0.0), 8.0) + side_scatter * (0.5 + 0.5 * mu)) * gain * sun_up;
	// Haze relative to the camera's height: `density` is what Godot's fog had here.
	vec2 mc = map_frame.xy / map_frame.z + 0.5;
	float ground_here = textureLod(air_map, clamp(mc, 0.0, 1.0), 0.0).r;
	float scale = density / max(haze_shape(cam.y, ground_here), 1e-4);

	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy + float(int(TIME * 60.0) % 16) * 5.588, vec2(0.06711056, 0.00583715))));
	float transmit = 1.0, prev = 0.0, lit = 0.0, all_haze = 0.0, shadowed = 0.0;
	vec3 light = vec3(0.0);
	for (int i = 0; i < steps; i++) {
		float u = (float(i) + jitter) / float(steps);
		float t = scene * u * u; // denser steps near the camera
		float dt = t - prev; prev = t;
		vec3 p = cam + dir * t;
		vec2 m = (p.xz - cam.xz + map_frame.xy) / map_frame.z + 0.5;
		bool inside = m.x > 0.0 && m.y > 0.0 && m.x < 1.0 && m.y < 1.0;
		vec2 hs = inside ? textureLod(air_map, m, 0.0).rg : vec2(sea_level, sea_level);
		// Terrain: lit above the sun-shadow height (a soft band: the map's cells are tens of metres).
		float sun = smoothstep(hs.g - 25.0, hs.g + 25.0, p.y);
		// Clouds: the deck above this point toward the sun (none once the point is above the deck).
		if (clouds > 0.0 && p.y < cloud_base) {
			vec2 xz = cloud_state.xy + (p.xz - cam.xz) + l.xz / lift * (cloud_base - p.y);
			sun *= 1.0 - cloud_shade * clouds * cloud_density(xz, coverage);
		}
		float sigma = scale * haze_shape(p.y, hs.r);
		float step_t = exp(-sigma * dt);
		float seen = transmit * (1.0 - step_t); // this stretch's share of what the pixel shows
		light += sun_light * sun * seen;
		shadowed += (1.0 - sun) * seen;
		transmit *= step_t;
		lit += sun * sigma * dt; all_haze += sigma * dt;
	}
	// scene × (1 − a) + light: the shadowed haze's share darkened, the sunlit haze's light added.
	float alpha = clamp(shade * shadowed, 1e-3, 1.0);
	if (debug == 1) { ALBEDO = vec3(lit / max(all_haze, 1e-6)); ALPHA = 1.0; }
	else if (debug == 3) { ALBEDO = vec3(1.0 - transmit, shadowed, 0.0); ALPHA = 1.0; } // red: haze opacity, green: shadowed part
	else { ALBEDO = max(light / alpha, vec3(0.0)); ALPHA = alpha; }
}
