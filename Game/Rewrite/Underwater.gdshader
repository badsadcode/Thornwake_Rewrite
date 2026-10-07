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
// Light shafts and caustics (round 24, Tom's diving photos): sunlight enters through the moving surface, each wave a
// lens that focuses it into bright lines; seen through the water those lines are shafts fanning down from the
// surface (brightest looking toward the sun: water scatters light mostly forward) and a dancing net on the bed.
uniform int shaft_steps = 24;          // 0: off
uniform float shaft_strength = 1.0;
uniform vec3 sun_light = vec3(1.0);    // sun colour × energy
global uniform vec3 sun_dir_world;
global uniform float ocean_time;
// Experimental 3: the light pattern comes from the simulated surface itself (OceanFft: slopes per cascade).
uniform float use_fft = 0.0;
uniform sampler2DArray fft_deriv : filter_linear, repeat_enable;
uniform vec3 fft_size = vec3(480.0, 59.0, 7.9);
uniform vec3 fft_offset_x;
uniform vec3 fft_offset_z;
uniform float fft_amp = 1.0;           // the sea's wave height share here (sheltered water: weaker lenses)

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// Curvature (∇²h) of one FFT cascade at s, over a footprint e (wider deeper down: a lens's focus blurs with depth).
float fft_curvature(float layer, vec2 s, float size, float ox, float oz, float e) {
	vec2 uv = (s + vec2(ox, oz)) / size + 0.5 / 256.0;
	float d = e / size;
	float gx = texture(fft_deriv, vec3(uv + vec2(d, 0.0), layer)).x - texture(fft_deriv, vec3(uv - vec2(d, 0.0), layer)).x;
	float gz = texture(fft_deriv, vec3(uv + vec2(0.0, d), layer)).y - texture(fft_deriv, vec3(uv - vec2(0.0, d), layer)).y;
	return (gx + gz) / (2.0 * e);
}

// How much sunlight reaches depth d below the surface point s where it came in (1 = flat water): a wave crest is a
// converging lens (∇²h < 0), bright lines form beneath it. Fine ripples focus near the surface, longer waves deeper.
float light_pattern(vec2 s, float d, float blur) {
	float lap;
	if (use_fft > 0.5) {
		float e = 0.12 + d * 0.03 + blur;
		float near = fft_curvature(2.0, s, fft_size.z, fft_offset_x.z, fft_offset_z.z, e);
		float far = fft_curvature(1.0, s, fft_size.y, fft_offset_x.y, fft_offset_z.y, e * 2.5);
		lap = mix(near, far, smoothstep(1.0, 8.0, d)) * fft_amp;
	} else {
		// Without the simulated surface: a drifting caustic net (ridged noise: thin bright lines where two ridge
		// fields cross), coarser and fainter deeper down, so it projects into shafts along the light.
		float t = ocean_time;
		vec2 q = s * (0.9 / (1.0 + d * 0.04 + blur * 0.3));
		q += vec2(vnoise(q * 0.5 + t * 0.05), vnoise(q * 0.5 + 7.3 - t * 0.05)) * 1.5;
		float r1 = 1.0 - abs(2.0 * vnoise(q + vec2(t * 0.31, t * 0.19)) - 1.0);
		float r2 = 1.0 - abs(2.0 * vnoise(q * 1.7 - vec2(t * 0.23, -t * 0.37)) - 1.0);
		float ridge = pow(r1 * r2, 4.0);
		return 0.5 + ridge * 3.0 * mix(1.0, 0.4, smoothstep(2.0, 25.0, d)) / (1.0 + blur);
	}
	// Focusing with depth (thin-lens: the light from a curved patch converges ~1/(curvature·(1-1/1.33)) below it).
	float focus = 1.0 + lap * d * 0.25;
	return clamp(1.0 / max(focus, 0.2), 0.0, 4.0);
}

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

	// Colour of the water itself: darker and bluer with depth, but never black (light scatters down from above).
	vec3 deep = water_color * daylight * (exp(-absorption * 0.2 * (surface_depth + 4.0)) * 0.85 + 0.15 * vec3(0.2, 0.45, 0.8) * exp(-surface_depth * 0.01));
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
	// Sunlight in the water: refracted into it (Snell), fading with the path it travelled down.
	vec3 sun = normalize(sun_dir_world);
	float sun_up = clamp(sun.y * 4.0, 0.0, 1.0) * daylight;
	if (shaft_steps > 0 && sun_up > 0.0) {
		vec2 sun_flat = sun.xz / 1.33;                          // horizontal part shrinks by the refractive index
		vec3 refr = normalize(vec3(sun_flat.x, sqrt(max(1.0 - dot(sun_flat, sun_flat), 0.05)), sun_flat.y)); // toward the sun, in water
		vec3 cam = CAMERA_POSITION_WORLD;
		float reach = min(dist, 70.0);
		// (white noise: the interleaved-gradient pattern drew a fine diagonal hatching through the shafts)
		// (partial jitter: full white noise left heavy grain in a single frame; none at all drew bands)
		float jitter = 0.2 + 0.6 * hash(FRAGCOORD.xy * 0.731 + fract(t * 7.13) * 91.7);
		vec3 sigma = absorption + density;
		vec3 shafts = vec3(0.0);
		// Henyey–Greenstein forward scattering (g 0.75) plus a little isotropic light.
		float cos_t = dot(ray, refr);
		float phase = 0.0119 / pow(1.5625 - 1.5 * cos_t, 1.5) * 4.0 + 0.15;
		for (int i = 0; i < shaft_steps; i++) {
			// Samples crowd near the camera (most of what reaches the eye scatters there): st ∝ u².
			float u = (float(i) + jitter) / float(shaft_steps);
			float st = reach * u * u;
			float step_len = reach * 2.0 * u / float(shaft_steps);
			vec3 p = cam + ray * st;
			float d = max(surface_depth - ray.y * st, 0.0); // depth below the surface here
			vec2 entry = p.xz + refr.xz / refr.y * d;          // where this light came through the surface
			float light = light_pattern(entry, d, st * 0.008); // farther samples see a blurrier pattern (bigger pixels)
			vec3 atten = exp(-sigma * (st + d / refr.y));
			shafts += atten * (light - 0.55) * step_len;       // the shafts: the focused light minus the even glow
		}
		c += max(shafts, vec3(0.0)) * density * phase * sun_light * sun_up * shaft_strength * 2.0;
		// The bed: the same lenses draw a dancing net of light on it.
		if (to_surface >= scene_dist) {
			vec3 bed = cam + ray * dist;
			float d = max(surface_depth - ray.y * dist, 0.0);
			float net = light_pattern(bed.xz + refr.xz / refr.y * d, d, dist * 0.01);
			c += textureLod(screen_tex, uv, 0.0).rgb * transmit * exp(-sigma * d / refr.y) * max(net - 0.7, 0.0) * 0.9 * sun_up * shaft_strength;
		} else {
			// The sun seen through the surface from below (inside Snell's window): a bright, flickering spot.
			float spot = pow(clamp(dot(ray, refr), 0.0, 1.0), 400.0) * (0.7 + 0.3 * vnoise(ray.xz * 80.0 + t * 3.0));
			c += sun_light * spot * 3.0 * sun_up * transmit;
		}
	}
	c += (hash(FRAGCOORD.xy + t) - 0.5) / 255.0; // dither
	ALBEDO = c;
	ALPHA = 1.0;
}
