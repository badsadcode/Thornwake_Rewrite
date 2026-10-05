// Light beams under the clouds (crepuscular rays; Tom, round 8): sunlight — or moonlight at night — falling through
// gaps in the cloud deck, seen from the side even when the sun itself is hidden or off screen. Each pixel marches its
// view ray through the air below the deck (up to the scene depth); at every step the cloud above that point toward
// the light says whether the air there is lit (the same cloud texture, plane and drift as the sky and the cloud
// shadows). Lit air scatters light toward the eye, most strongly looking toward the light (forward scattering), so
// beams appear under every gap with dark lanes where the clouds shade the air. Drawn on a full-screen quad after the
// scene (additive). Only with broken clouds: a clear sky has nothing to cut beams, an overcast one no gaps.
shader_type spatial;
render_mode unshaded, blend_add, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform float cloud_period = 6000.0;
uniform float cloud_height = 1600.0;
uniform vec3 light_direction = vec3(0.0, 1.0, 0.0); // world space, toward the sun or the moon
uniform vec3 light_color : source_color = vec3(1.0, 0.95, 0.85); // includes the light's strength
uniform float intensity = 0.7;
uniform float max_distance = 7000.0;
global uniform vec4 cloud_state;

const int STEPS = 40;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

// The sky's main deck (Sky.gdshader cloud_density), from a blurrier mip (soft shaft edges) but a crisper threshold.
float cloud_density(vec2 xz, float coverage) {
	vec2 uv = xz / cloud_period;
	float n = textureLod(cloud_tex, uv, 2.0).r * 0.8 + textureLod(cloud_tex, uv * 4.0 + vec2(0.31, 0.17), 2.0).g * 0.2;
	float edge = 1.0 - coverage;
	return smoothstep(edge - 0.02, edge + 0.1, n); // a crisper edge than the sky's: gaps cut clear shafts
}

void fragment() {
	float coverage = cloud_state.z;
	// Beams need broken cloud: none in a clear sky, fading as the deck closes.
	float broken = smoothstep(0.12, 0.35, coverage) * (1.0 - smoothstep(0.82, 0.97, coverage));
	if (intensity <= 0.0 || broken <= 0.0 || light_direction.y < 0.03) discard;

	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, max(depth, 1e-7), 1.0);
	view.xyz /= view.w;
	float scene = depth < 1e-6 ? max_distance : min(length(view.xyz), max_distance);
	vec3 dir = normalize((INV_VIEW_MATRIX * vec4(normalize(view.xyz), 0.0)).xyz);
	vec3 cam = CAMERA_POSITION_WORLD;
	// Stop at the cloud base (beams start there).
	float to_base = dir.y > 0.001 ? (cloud_height - 40.0) / dir.y : max_distance;
	float end = min(scene, to_base);
	if (end <= 1.0) discard;

	// Cloud above an air point at height a over the camera: shifted along the light as for the cloud shadows.
	vec3 l = normalize(light_direction);
	// The true slant (not the cloud shadows' clamp): with a low sun the shafts are long and slanted, and looking toward
	// the sun the view ray runs along them — that alignment is what makes beams visible.
	float lift = max(l.y, 0.05);
	float mu = dot(dir, l);
	// Henyey-Greenstein (forward, g = 0.72) plus a little isotropic light, so beams also show sideways.
	float g = 0.72;
	float phase = (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * mu, 1.5) * 0.08 + 0.025;

	// Interleaved gradient noise: an even, fine dither (no visible grain with or without TAA).
	float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy + float(int(TIME * 60.0) % 16) * 5.588, vec2(0.06711056, 0.00583715))));
	float lit = 0.0, total = 0.0, prev = 0.0;
	for (int i = 0; i < STEPS; i++) {
		float u = (float(i) + jitter) / float(STEPS);
		float t = end * u * u; // denser steps near the camera
		float dt = t - prev; prev = t;
		vec3 p = cam + dir * t;
		float a = clamp(p.y - cam.y, -400.0, cloud_height);
		vec2 xz = cloud_state.xy + (p.xz - cam.xz) + l.xz / lift * (cloud_height - a);
		float shade = 1.0 - 0.92 * cloud_density(xz, coverage);
		// Haze thins with height and dims with distance (the air in front hides the air behind).
		float haze = exp(-max(a, 0.0) / 900.0) * exp(-t / 9000.0);
		lit += shade * haze * dt;
		total += haze * dt;
	}
	// Only the contrast between lit and shaded air reads as beams; the even part is the scene's own haze.
	float beams = max(0.0, lit - total * (1.0 - 0.92 * coverage) * 0.75);
	float strength = beams / 1200.0 * phase * intensity * broken * smoothstep(0.03, 0.1, l.y);
	ALBEDO = light_color * strength;
}
