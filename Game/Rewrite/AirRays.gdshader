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
// Environment contrast (D154, Noor's notes): the pass carries the haze itself, shaped by height and terrain (valleys
// hazy, ridges and the foreground clearer), lit by the sky everywhere and by the sun only where it reaches; Godot's
// even distance fog is turned down to a thin remainder (Atmosphere).
uniform int own_haze = 0;
uniform vec3 fog_color = vec3(0.5, 0.55, 0.6); // the sky's light in the haze (Godot's fog light, linear)
uniform float own_far = 10000.0;  // where the own haze has handed the distance over to Godot's fog (m)
uniform float sky_affect = 0.25;   // the sky's own veil of haze (Godot's fog sky affect): the glow round the sun there
uniform float gain = 1.0;          // sunlight in the haze (developer tuning)
uniform int steps = 40;
uniform float max_distance = 60000.0;
uniform float sea_level = 0.0;
uniform float haze_height = 1400.0; // the haze falls to a third over this height
uniform float valley_haze = 3.0;    // extra haze close above the ground (valleys, lakes, forests)
uniform int debug = 0;              // 1: the sunlit share of each view ray's haze, grey (developer tool)
// Sharper, stronger shafts, closer to Tom's references than to physics (D150): a ridge's real skyline has teeth, trees
// and notches the 39 m map cannot hold, so its shadow edge is roughened by noise that stays the same along the sun's
// direction — each ridge casts a comb of parallel lanes into the haze near the edge of its shadow.
uniform float ray_noise = 150.0;   // how far (m) the shadow edge is moved up and down by the noise
uniform float ray_size = 70.0;     // the lanes' width (m, across the sun's direction)
uniform float shadow_edge = 8.0;   // the shadow edge's softness (m)
uniform float shaft_glow = 0.6;    // a broad glow of sunlit haze toward the sun (the shafts' brightness looking at them)
uniform vec2 world_origin = vec2(0.0); // the scene origin's world position (the lanes stay put when the origin moves)
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

float hash12(vec2 p) {
	vec3 q = fract(vec3(p.xyx) * 0.1031);
	q += dot(q, q.yzx + 33.33);
	return fract((q.x + q.y) * q.z);
}

float value_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash12(i), hash12(i + vec2(1.0, 0.0)), f.x), mix(hash12(i + vec2(0.0, 1.0)), hash12(i + vec2(1.0, 1.0)), f.x), f.y);
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
	// Godot's fog glow toward the sun (pow 8), the shafts' broad glow toward it (pow 3) and a part seen from the side.
	float toward = max(mu, 0.0);
	vec3 sun_light = light_color * (sun_scatter * pow(toward, 8.0) + shaft_glow * pow(toward, 3.0) + side_scatter * (0.5 + 0.5 * mu)) * gain * sun_up;
	// Across the sun's direction: the noise's two coordinates (constant along the light, so the lanes are rays).
	vec3 across_a = normalize(abs(l.y) > 0.99 ? vec3(1.0, 0.0, 0.0) : cross(l, vec3(0.0, 1.0, 0.0)));
	vec3 across_b = cross(across_a, l);
	vec3 origin3 = vec3(world_origin.x, 0.0, world_origin.y);
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
		// Terrain: lit above the sun-shadow height, its edge roughened into lanes (the map's cells are tens of metres).
		vec3 pw = p + origin3;
		vec2 lane = vec2(dot(pw, across_a), dot(pw, across_b)) / ray_size;
		float rough = (value_noise(lane) * 0.65 + value_noise(lane * 2.7 + 17.0) * 0.35 - 0.5) * 2.0 * ray_noise;
		float sun = smoothstep(hs.g + rough - shadow_edge, hs.g + rough + shadow_edge, p.y);
		// Clouds: the deck above this point toward the sun (none once the point is above the deck).
		if (clouds > 0.0 && p.y < cloud_base) {
			vec2 xz = cloud_state.xy + (p.xz - cam.xz) + l.xz / lift * (cloud_base - p.y);
			sun *= 1.0 - cloud_shade * clouds * cloud_density(xz, coverage);
		}
		// (own haze for the near and middle distance only: Godot's fog, which fades into the sky's colour, keeps the far
		// distance; a far haze of plain fog colour went dark violet along distant ridges)
		float sigma = scale * haze_shape(p.y, hs.r) * (own_haze == 1 ? (inside ? 1.0 - smoothstep(own_far * 0.6, own_far, t) : 0.0) : 1.0);
		float step_t = exp(-sigma * dt);
		float seen = transmit * (1.0 - step_t); // this stretch's share of what the pixel shows
		light += (own_haze == 1 ? fog_color + sun_light * sun : sun_light * sun) * seen;
		shadowed += (1.0 - sun) * seen;
		transmit *= step_t;
		lit += sun * sigma * dt; all_haze += sigma * dt;
	}
	// The sky: Godot's fog veils it whatever the distance (sky affect) and that veil carried the glow round the sun,
	// which this pass took over: it comes back by how much of the air toward that point of the sky is sunlit, so a
	// peak's shadow runs through the glow as a darker ray (Tom's reference 2).
	if (depth < 1e-6) {
		light += light_color * (sun_scatter * pow(toward, 8.0) + shaft_glow * pow(toward, 3.0)) * gain * sun_up * sky_affect * (lit / max(all_haze, 1e-6));
	}
	// scene × (1 − a) + light: the shadowed haze's share darkened, the sunlit haze's light added.
	float alpha = clamp(shade * shadowed, 1e-3, 1.0);
	if (own_haze == 1) alpha = clamp(1.0 - transmit, 1e-3, 1.0); // (scene × transmittance + the haze's light)
	if (debug == 1) { ALBEDO = vec3(lit / max(all_haze, 1e-6)); ALPHA = 1.0; }
	else if (debug == 3) { ALBEDO = vec3(1.0 - transmit, shadowed, 0.0); ALPHA = 1.0; } // red: haze opacity, green: shadowed part
	else { ALBEDO = max(light / alpha, vec3(0.0)); ALPHA = alpha; }
}
