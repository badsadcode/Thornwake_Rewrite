// Rewrite world water: ported from GangTheGAME Assets/Shaders/water.gdshader (refraction through the screen
// texture, depth-based absorption from the depth buffer, animated caustics on the submerged ground, screen-space
// reflections with sky fallback, shoreline/contact foam), adapted for clear natural water at world scale:
//  - colour/absorption per body (COLOR.g: 0 sea, 0.5 lake, 1 river) instead of silty flood water;
//  - a gentle multi-direction swell on the open sea, calm lakes, faster ripples on rivers;
//  - no simulation wave map yet (that arrives with the local shallow-water simulation, milestone M6).
// COLOR.r = water depth / 20 m at the vertex (from the terrain sampler).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;

uniform vec3 sea_scatter : source_color = vec3(0.05, 0.22, 0.30);
uniform vec3 lake_scatter : source_color = vec3(0.06, 0.20, 0.17);
uniform vec3 river_scatter : source_color = vec3(0.07, 0.15, 0.12);
uniform vec3 absorption = vec3(0.55, 0.16, 0.12);
uniform vec3 foam_color : source_color = vec3(0.93, 0.95, 0.95);
uniform float refraction : hint_range(0.0, 0.1) = 0.03;
uniform float caustic_strength : hint_range(0.0, 2.0) = 0.8;
uniform float reflection_strength : hint_range(0.0, 1.0) = 0.9;
uniform float swell_height = 0.35;
uniform vec3 sky_horizon : source_color = vec3(0.70, 0.76, 0.78);
uniform vec3 sky_zenith : source_color = vec3(0.32, 0.47, 0.62);

varying float vertex_depth;
varying float body;
varying vec3 world_position;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
vec2 hash2(vec2 p) { return vec2(hash(p), hash(p + 17.31)); }
float noise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float ripples(vec2 p, float t, float speed) {
	vec2 flow = vec2(0.12, 0.05) * speed;
	return noise(p * 0.9 + t * flow * 6.0) * 0.55 + noise(p * 2.1 - t * flow.yx * 9.0) * 0.3 + noise(p * 5.0 + t * vec2(0.3, -0.5)) * 0.15;
}
float caustic_layer(vec2 p, float t) {
	vec2 i = floor(p); vec2 f = fract(p);
	float d1 = 8.0; float d2 = 8.0;
	for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec2 g = vec2(float(x), float(y));
		vec2 o = 0.5 + 0.42 * sin(t + 6.2831 * hash2(i + g));
		float d = length(g + o - f);
		if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
	}
	return 1.0 - smoothstep(0.0, 0.32, d2 - d1);
}
float caustics(vec2 p, float t) {
	vec2 warp = vec2(noise(p * 0.8 + t * 0.15), noise(p * 0.8 + 5.2 - t * 0.15)) - 0.5;
	p += warp * 0.9;
	return pow(caustic_layer(p * 1.3, t * 0.8) * caustic_layer(p * 1.7 + 3.7, -t * 0.95), 1.5) * 1.6;
}
vec3 view_from_depth(vec2 uv, float raw, mat4 inv_projection) {
	vec4 v = inv_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
	return v.xyz / v.w;
}
// Sum of a few directional sine waves (open sea only).
float swell(vec2 p, float t) {
	return sin(dot(p, vec2(0.021, 0.013)) + t * 0.55) * 0.5 + sin(dot(p, vec2(-0.016, 0.027)) + t * 0.71) * 0.3
		+ sin(dot(p, vec2(0.043, -0.031)) + t * 1.1) * 0.2;
}

void vertex() {
	vertex_depth = COLOR.r * 20.0;
	body = COLOR.g;
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float open_sea = (1.0 - step(0.25, body)) * smoothstep(1.0, 8.0, vertex_depth);
	VERTEX.y += swell(world_position.xz, TIME) * swell_height * open_sea;
}

void fragment() {
	vec2 p = world_position.xz;
	float sea = 1.0 - step(0.25, body), river = step(0.75, body), lake = 1.0 - sea - river;
	float speed = mix(1.0, 3.0, river);
	float e = 0.05;
	float h = ripples(p, TIME, speed);
	// Ripples fade with distance so far water does not sparkle (they are far smaller than a pixel there).
	float amp = (mix(0.05, 0.09, river) + sea * 0.03) / (1.0 + length(VERTEX) / 120.0);
	vec2 slope = vec2(ripples(p + vec2(e, 0.0), TIME, speed) - h, ripples(p + vec2(0.0, e), TIME, speed) - h) / e * amp;
	// Swell slope on the open sea.
	float open_sea = sea * smoothstep(1.0, 8.0, vertex_depth);
	slope += vec2(swell(p + vec2(0.5, 0.0), TIME) - swell(p - vec2(0.5, 0.0), TIME), swell(p + vec2(0.0, 0.5), TIME) - swell(p - vec2(0.0, 0.5), TIME)) * swell_height * open_sea;
	vec3 n_world = normalize(vec3(-slope.x, 1.0, -slope.y));
	vec3 n_view = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);
	NORMAL = n_view;
	vec3 to_eye = normalize(-VERTEX);
	float fresnel = 0.02 + 0.98 * pow(1.0 - clamp(dot(n_view, to_eye), 0.0, 1.0), 5.0);

	// What lies behind the surface (depth buffer), refracted by the ripples; never pull in things above water.
	vec3 behind = view_from_depth(SCREEN_UV, texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX);
	float straight_path = max(0.0, length(behind) - length(VERTEX));
	vec2 ruv = clamp(SCREEN_UV + n_world.xz * refraction * clamp(straight_path * 0.5, 0.0, 1.0), vec2(0.001), vec2(0.999));
	vec3 rbehind = view_from_depth(ruv, texture(depth_tex, ruv).r, INV_PROJECTION_MATRIX);
	if (length(rbehind) < length(VERTEX)) { ruv = SCREEN_UV; rbehind = behind; }
	float path = max(0.0, length(rbehind) - length(VERTEX));
	vec3 surface_world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 bottom_world = (INV_VIEW_MATRIX * vec4(rbehind, 1.0)).xyz;
	float depth_below = max(0.0, surface_world.y - bottom_world.y);
	float contact_depth = max(0.0, surface_world.y - (INV_VIEW_MATRIX * vec4(behind, 1.0)).y);

	// Transmitted light: the ground through the water, with caustics in the shallows.
	vec3 ground = textureLod(screen_tex, ruv, 0.0).rgb;
	float caustic = caustics(bottom_world.xz * 0.6, TIME * 1.3) * smoothstep(0.0, 0.1, depth_below) * exp(-depth_below * 0.35);
	ground += caustic * caustic_strength * 0.3 * vec3(1.0, 0.97, 0.85) * (0.4 + ground);
	vec3 transmittance = exp(-absorption * path * mix(1.0, 2.2, river));
	vec3 scatter = sea * sea_scatter + lake * lake_scatter + river * river_scatter;
	float murk = 1.0 - dot(transmittance, vec3(0.333));

	// Screen-space reflection, sky as fallback.
	vec3 ray = normalize(reflect(VERTEX, n_view));
	vec4 hit = vec4(0.0);
	float step_length = 0.25;
	vec3 march = VERTEX + ray * 0.1;
	for (int i = 0; i < 32; i++) {
		march += ray * step_length;
		step_length *= 1.18;
		vec4 clip = PROJECTION_MATRIX * vec4(march, 1.0);
		if (clip.w <= 0.0) break;
		vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) break;
		float scene_z = view_from_depth(uv, texture(depth_tex, uv).r, INV_PROJECTION_MATRIX).z;
		float gap = scene_z - march.z;
		if (gap > 0.0 && gap < step_length * 2.5 + 0.3) {
			vec2 edge = smoothstep(vec2(0.0), vec2(0.08), uv) * smoothstep(vec2(0.0), vec2(0.08), 1.0 - uv);
			hit = vec4(textureLod(screen_tex, uv, 0.0).rgb, edge.x * edge.y * (1.0 - float(i) / 32.0));
			break;
		}
	}

	// Foam: a broken line where the water meets the shore or anything standing in it; more on the sea and rivers.
	float bubbles = 0.55 + 0.45 * noise(p * 1.5 + TIME * 0.4);
	float contact = 1.0 - smoothstep(0.0, mix(0.03, 0.12, sea), contact_depth);
	float foam = clamp(contact * mix(0.3, 0.6, sea) * smoothstep(0.5, 0.8, bubbles), 0.0, 1.0);

	vec3 transmitted = ground * transmittance;
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(ray, 0.0)).xyz);
	vec3 sky = mix(sky_horizon, sky_zenith, smoothstep(0.0, 0.6, ray_world.y));
	vec3 reflected = mix(sky, hit.rgb, hit.a) * reflection_strength;
	ALBEDO = mix(scatter * murk * (1.0 - fresnel), foam_color, foam);
	EMISSION = (transmitted * (1.0 - fresnel) + reflected * fresnel) * (1.0 - foam);
	SPECULAR = 0.3 * (1.0 - foam);
	ROUGHNESS = mix(0.03, 0.6, foam);
	METALLIC = 0.0;
	ALPHA = smoothstep(0.0, 0.04, contact_depth);
}
