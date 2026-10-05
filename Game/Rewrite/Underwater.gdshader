// Underwater view (camera below a water surface): a full-screen pass drawn after everything else.
// Per pixel, the view ray either hits the ground/objects below the surface (depth buffer) or the surface from
// below (a horizontal plane `surface_depth` metres above the camera). Light fades with the distance travelled in
// water (red first, then green: blue-green water) and with the camera's depth. Looking up, inside Snell's window
// (rays within ~49° of vertical) the world above shows through a rippling surface; outside it the surface
// reflects the dark water below (total internal reflection).
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float active = 0.0;            // 0 above water, 1 below
uniform float surface_depth = 1.0;     // metres from the camera up to the surface
uniform vec3 water_color : source_color = vec3(0.05, 0.25, 0.30); // in-scattered light near the surface
uniform vec3 absorption = vec3(0.45, 0.12, 0.09); // per metre (red is absorbed first)
uniform float density = 0.06;          // scattering per metre (turbidity)
uniform float daylight = 1.0;          // sun/sky brightness

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
	if (active < 0.5) discard;
	float t = TIME;
	// Gentle wobble of the whole view (refraction by moving water).
	vec2 uv = SCREEN_UV + vec2(sin(SCREEN_UV.y * 40.0 + t * 1.7), cos(SCREEN_UV.x * 33.0 + t * 1.3)) * 0.0018;
	float depth = textureLod(depth_tex, uv, 0.0).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(uv * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	float scene_dist = depth < 1e-6 ? 1e6 : length(view.xyz);
	vec3 ray = normalize((INV_VIEW_MATRIX * vec4(normalize(view.xyz), 0.0)).xyz);
	float to_surface = ray.y > 1e-4 ? surface_depth / ray.y : 1e6;

	vec3 deep = water_color * daylight * exp(-absorption * (surface_depth + 4.0)); // colour of the water itself
	vec3 c;
	float dist;
	if (to_surface < scene_dist) {
		// The surface seen from below.
		dist = to_surface;
		float cos_up = ray.y;
		float window = smoothstep(0.62, 0.68, cos_up); // Snell's window edge at ~48.6° from vertical
		vec2 ripple = vec2(sin(ray.x * 60.0 + t * 2.3), cos(ray.z * 55.0 - t * 2.0)) * 0.012;
		vec3 above = textureLod(screen_tex, uv + ripple, 0.0).rgb * 1.1;
		vec3 mirror = deep * 0.8;
		c = mix(mirror, above, window);
		// Bright, flickering edge of the window.
		c += vec3(0.6, 0.8, 0.85) * daylight * 0.25 * smoothstep(0.55, 0.65, cos_up) * (1.0 - window);
	} else {
		dist = scene_dist;
		c = textureLod(screen_tex, uv, 0.0).rgb;
		// Sunlight reaching the ground fades with its depth below the surface.
		float ground_depth = surface_depth - ray.y * dist;
		c *= exp(-absorption * max(ground_depth, 0.0) * 0.5);
	}
	// Along the ray: light is absorbed (per channel) and replaced by the water's own scattered colour.
	vec3 transmit = exp(-(absorption + density) * dist);
	c = c * transmit + deep * (1.0 - transmit);
	c += (hash(FRAGCOORD.xy + t) - 0.5) / 255.0; // dither
	ALBEDO = c;
	ALPHA = 1.0;
}
