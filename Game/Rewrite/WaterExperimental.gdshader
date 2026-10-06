// EXPERIMENTAL water (round 9, Tom: "an experimental alternative, not a replacement"). Selected by the setting
// Lighting and effects → Water = experimental; the classic Water.gdshader is untouched and stays the default.
// Same uniforms and vertex data as the classic shader (the material swaps shaders in place), plus CUSTOM0.
//
// What is different:
//  * Smooth at any distance: the swells move the geometry where the mesh can carry them, but the lighting normal of
//    every wave is evaluated per pixel from the analytic wave sum (at the undisplaced position), so far, coarse water
//    never looks faceted.
//  * Oceans: the Gerstner sea (same waves as OceanWaves.cs) plus SHORE WAVES: crest lines follow the depth contours,
//    so near land the waves always run along the shore and roll in toward it. The phase is the shallow-water travel
//    time to the point (φ = 2ω√d / (s√g) for bed slope s), so crests bunch up and steepen as the water shallows; their
//    height grows by shoaling (Green's law) until they break (H > ~0.75 d) into foam that trails behind the crest. The
//    open-sea waves fade out over the shallows; exposed coasts get the full surf, sheltered bays a fraction; steep rocky
//    coasts churn (turbulence foam, rough chop) instead of rolling breakers.
//  * Lakes: the same waves cut to the lake's size (fetch): small lakes only carry short ripples, big lakes approach a
//    small sea; calmer near the shore.
//  * Rivers: no swells. Three layers of ripples carried downstream at the current (two-phase flow advection), longer
//    and faster in strong current, calm and glassy in slow pools; whitewater where the flow is fast and shallow.
//  * Foam: a moving cell pattern revealed by a foam amount (crests, breakers, rocks, rapids, contact), so it forms,
//    stretches and breaks apart instead of a painted outline.
//  * Wind and weather: sea state (storms) scales and steepens everything; the wind steers the small chop and lake
//    waves and roughens reflections.
// Biome tint (UV2), depth absorption, refraction, caustics, plume and reflections work as in the classic shader.
//
// CUSTOM0 = uphill bed gradient x, z (toward the shore; length = slope), exposure 0 sheltered … 1 open, fetch (m).
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
uniform int ssr_steps : hint_range(0, 64) = 32;
uniform vec4 waves[12];
uniform float wave_q[12];
uniform float wave_height = 0.3;
uniform float sea_state = 0.25;
uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);
uniform vec3 sun_color : source_color = vec3(1.0);
uniform vec3 crest_glow : source_color = vec3(0.10, 0.55, 0.50);
const float wave_phase[12] = { 0.0, 1.7, 4.1, 2.6, 5.3, 0.9, 3.3, 5.9, 1.1, 4.6, 2.2, 0.4 };
global uniform float ocean_time;
global uniform vec4 wind; // dir.x, speed/8, dir.z, gust
uniform vec3 sky_horizon : source_color = vec3(0.70, 0.76, 0.78);
uniform vec3 sky_zenith : source_color = vec3(0.32, 0.47, 0.62);

varying float vertex_depth;
varying float body;
varying vec3 world_position;
varying vec2 rest_xz;     // undisplaced world position (waves are evaluated here per pixel)
varying vec2 current;
varying float plume;
varying vec4 biome_tint;
varying vec2 bed_grad;    // uphill bed gradient (toward the shore)
varying float exposure;
varying float fetch;
varying float edge_fade; // 1 on water, fading over the sheet's extension under the beach (COLOR.a)

const float G = 9.81;
const float PI2 = 6.2831853;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
vec2 hash2(vec2 p) { return vec2(hash(p), hash(p + 17.31)); }
float noise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
// Value noise with its analytic gradient (xy) — detail normals without finite-difference taps.
vec3 noised(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f), du = 6.0 * f * (1.0 - f);
	float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
	float v = a + (b - a) * u.x + (c - a) * u.y + (a - b - c + d) * u.x * u.y;
	vec2 g = du * vec2(b - a + (a - b - c + d) * u.y, c - a + (a - b - c + d) * u.x);
	return vec3(g, v);
}
// Gradient noise with analytic derivatives (quintic; smooth slopes, unlike value noise's blocky ones). xy = gradient,
// z = value (about ±0.7).
vec2 grad2(vec2 i) { float h = hash(i) * PI2; return vec2(cos(h), sin(h)); }
vec3 gnoised(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	vec2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);
	vec2 ga = grad2(i), gb = grad2(i + vec2(1.0, 0.0)), gc = grad2(i + vec2(0.0, 1.0)), gd = grad2(i + vec2(1.0, 1.0));
	float va = dot(ga, f), vb = dot(gb, f - vec2(1.0, 0.0)), vc = dot(gc, f - vec2(0.0, 1.0)), vd = dot(gd, f - vec2(1.0, 1.0));
	float v = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);
	vec2 g = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd) + du * (u.yx * (va - vb - vc + vd) + vec2(vb, vc) - va);
	return vec3(g, v);
}
float cells(vec2 p, float t) {
	vec2 i = floor(p); vec2 f = fract(p);
	float d1 = 8.0, d2 = 8.0;
	for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec2 g = vec2(float(x), float(y));
		vec2 o = 0.5 + 0.42 * sin(t + PI2 * hash2(i + g));
		float d = length(g + o - f);
		if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
	}
	return d2 - d1; // 0 on cell edges
}
float caustics(vec2 p, float t) {
	vec2 warp = vec2(noise(p * 0.8 + t * 0.15), noise(p * 0.8 + 5.2 - t * 0.15)) - 0.5;
	p += warp * 0.9;
	float a = 1.0 - smoothstep(0.0, 0.32, cells(p * 1.3, t * 0.8)), b = 1.0 - smoothstep(0.0, 0.32, cells(p * 1.7 + 3.7, -t * 0.95));
	return pow(a * b, 1.5) * 1.6;
}
vec3 view_from_depth(vec2 uv, float raw, mat4 inv_projection) {
	vec4 v = inv_projection * vec4(uv * 2.0 - 1.0, raw, 1.0);
	return v.xyz / v.w;
}

// ---- body classification (COLOR.g): sea 0, pond .375, lake .5, river 1
float is_sea(float b) { return 1.0 - step(0.25, b); }
float is_pond(float b) { return step(0.25, b) * (1.0 - step(0.45, b)); }
float is_lake(float b) { return step(0.45, b) * (1.0 - step(0.75, b)); }
float is_river(float b) { return step(0.75, b); }

// Amplitude factor of wave i: shallow-water fade, lakes cut to their size, sheltered coasts calmer.
float wave_factor(int i, float lambda, float depth, float sea, float lake, float fetch_m, float expo) {
	float f = smoothstep(0.0, 1.0, depth / (lambda * 0.12 + 0.3));
	if (sea > 0.5) {
		// Long waves give way to the shore waves over the shallows; bays and coves are calmer.
		f *= mix(0.3, 1.0, expo) * mix(0.35, 1.0, smoothstep(3.0, 14.0, depth));
	} else if (lake > 0.5) {
		// Lakes: only waves shorter than ~1/25 of the fetch; amplitude grows with the lake's size; no swell.
		float longest = clamp(fetch_m * 0.04, 2.5, 45.0);
		f *= (1.0 - smoothstep(longest, longest * 1.6, lambda)) * clamp(0.18 + fetch_m / 6000.0, 0.18, 0.85) * (i >= 10 ? 0.0 : 1.0);
		f *= smoothstep(0.0, 2.5, depth); // calmer along the shore
	} else f = 0.0;
	return f;
}

// Gerstner sum at rest position p. Geometry: only waves longer than min_lambda (what the mesh can carry);
// lighting: all (min_lambda 0, called per pixel). Returns displacement; accumulates the normal and crest data.
vec3 gerstner(vec2 p, float depth, float sea, float lake, float fetch_m, float expo, float min_lambda, float t,
		inout vec3 n, inout float crest, inout vec3 jac) {
	vec3 offset = vec3(0.0);
	for (int i = 0; i < 12; i++) {
		float k = waves[i].z;
		float lambda = PI2 / k;
		float amp = waves[i].w * wave_factor(i, lambda, depth, sea, lake, fetch_m, expo) * smoothstep(min_lambda, min_lambda * 2.0, lambda);
		if (amp <= 0.0) continue;
		vec2 d = waves[i].xy;
		float theta = k * dot(d, p) - sqrt(G * k) * t + wave_phase[i];
		float c = cos(theta), s = sin(theta);
		float q = wave_q[i];
		offset += vec3(q * amp * d.x * c, amp * s, q * amp * d.y * c);
		n.x -= d.x * k * amp * c; n.z -= d.y * k * amp * c; n.y -= q * k * amp * s;
		crest += amp * s;
		float qa = q * k * amp * s;
		jac += vec3(qa * d.x * d.x, qa * d.y * d.y, qa * d.x * d.y);
	}
	return offset;
}

// Shore waves (sea): crests along the depth contours rolling in toward the shore (see header). Returns displacement;
// accumulates the normal; breaking 0..1 and since (0 at the crest … 1 one period behind it) for the foam.
vec3 shore_wave(vec2 p, float depth, vec2 grad, float expo, float min_lambda, float t, inout vec3 n, out float breaking, out float since, out float k_out) {
	breaking = 0.0; since = 1.0; k_out = 1.0;
	float s = length(grad);
	float active = smoothstep(14.0, 8.0, depth) * smoothstep(0.003, 0.012, s) * (1.0 - smoothstep(0.22, 0.45, s));
	if (active <= 0.0) return vec3(0.0);
	vec2 dir = grad / s; // toward the shore
	float period = 7.0 + 4.0 * sea_state;
	float omega = PI2 / period;
	float d = max(depth, 0.05);
	// Travel-time phase over a reference bed slope: a function of depth only, so crests (θ = const) are depth contours
	// moving shoreward with time. (Dividing by the local slope made the phase jump by whole cycles between vertices
	// where the slope varies — blocky patches on coarse tiles.) The wavenumber is the phase's true gradient.
	const float S_REF = 0.03;
	float theta = -(2.0 * omega * sqrt(d) / (S_REF * sqrt(G)) + omega * t) + 3.0 * noise(p * 0.004); // slow wobble along the coast
	float k = omega / sqrt(G * d) * clamp(s / S_REF, 0.15, 4.0);
	k_out = k;
	float lambda = PI2 / k;
	// Height: open-coast swell, shoaling (Green's law) until it breaks at ~0.75 d.
	float h0 = (0.12 + 1.15 * sea_state) * expo * (0.75 + 0.5 * noise(p * 0.01 + t * 0.02));
	float h = h0 * pow(10.0 / max(d, 0.4), 0.25);
	float limit = 0.75 * d + 0.03;
	breaking = smoothstep(0.55, 0.95, h / limit) * active;
	h = min(h, limit);
	float amp = 0.5 * h * active * smoothstep(min_lambda, min_lambda * 2.0, lambda);
	float q = clamp(0.35 + 0.65 * h / limit, 0.0, 1.0) / max(k * amp * 1.6 + 1e-4, 1.0); // steeper in the shallows, never folding
	float c = cos(theta), sn = sin(theta);
	// Leaning crest: a second harmonic makes the shoreward face steeper than the back.
	float lean = 0.22 * breaking;
	float y = amp * (sn + lean * sin(2.0 * theta - 0.6));
	n.x -= dir.x * k * amp * c; n.z -= dir.y * k * amp * c; n.y -= q * k * amp * sn;
	since = fract((1.5707963 - theta) / PI2);
	return vec3(q * amp * dir.x * c, y, q * amp * dir.y * c);
}

void vertex() {
	vertex_depth = COLOR.r * 20.0;
	body = COLOR.g;
	plume = COLOR.b;
	edge_fade = COLOR.a;
	float packed = UV2.x;
	biome_tint = vec4(floor(packed / 65536.0), mod(floor(packed / 256.0), 256.0), mod(packed, 256.0), 0.0) / 255.0;
	biome_tint.a = UV2.y;
	current = UV;
	bed_grad = CUSTOM0.xy;
	exposure = CUSTOM0.z;
	fetch = CUSTOM0.w;
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	rest_xz = world_position.xz;
	float sea = is_sea(body), lake = is_lake(body);
	vec3 offset = vec3(0.0);
	if (sea + lake > 0.0) {
		// The mesh carries waves at least ~5 vertex spacings long (per-pixel lighting does the rest).
		float spacing = max(1.0, length(world_position - CAMERA_POSITION_WORLD) / 40.0);
		vec3 n = vec3(0.0, 1.0, 0.0); float crest = 0.0; vec3 jac = vec3(0.0);
		offset = gerstner(rest_xz, vertex_depth, sea, lake, fetch, sea > 0.5 ? exposure : 1.0, 5.0 * spacing, ocean_time, n, crest, jac);
		if (sea > 0.5) {
			float breaking, since, kw;
			offset += shore_wave(rest_xz, vertex_depth, bed_grad, exposure, 5.0 * spacing, ocean_time, n, breaking, since, kw);
			// Swash: the last of each wave runs up and down the beach (the sheet reaches under the beach), in step with
			// the shore waves.
			float swash = 1.0 - smoothstep(0.0, 2.0, vertex_depth);
			float period = 7.0 + 4.0 * sea_state;
			float run = 0.5 + 0.5 * sin(-PI2 / period * ocean_time + noise(rest_xz * 0.02) * 2.0);
			offset.y += swash * (0.1 + 0.5 * sea_state) * mix(0.4, 1.0, exposure) * (run * run - 0.3);
		}
	}
	VERTEX += offset;
	world_position += offset;
}

// Wind waves (per pixel, lighting only): six short travelling waves fanned around the wind (7 m … 1 m, deep-water
// speeds), each phase wobbled by noise so crests never run straight for long, amplitude gathered in gusty patches;
// then two scales of fine gradient-noise ripples close to the camera. Waves far smaller than a pixel fade out.
vec2 ripple_slope(vec2 p, float t, vec2 wdir, float strength, float dist) {
	vec2 slope = vec2(0.0);
	float gust = 0.55 + 0.9 * (gnoised(p * 0.012 + wdir * t * 0.05).z + 0.5); // cat's paws: rougher patches
	for (int i = 0; i < 6; i++) {
		float fi = float(i);
		float lambda = 7.0 * pow(0.68, fi);
		float k = PI2 / lambda;
		float ang = (hash(vec2(fi, 3.7)) - 0.5) * 2.4 + (fi - 2.5) * 0.15;
		vec2 d = vec2(wdir.x * cos(ang) - wdir.y * sin(ang), wdir.x * sin(ang) + wdir.y * cos(ang));
		float fade = 1.0 - smoothstep(lambda * 25.0, lambda * 80.0, dist);
		if (fade <= 0.0) continue;
		float warp = 2.2 * gnoised(p * (0.04 / lambda) + vec2(fi * 7.1, fi * 3.3)).z;
		float theta = k * dot(d, p) - sqrt(G * k) * t + fi * 1.7 + warp;
		float a = lambda * 0.014 * fade * (0.6 + 0.8 * hash(vec2(fi, 9.1)));
		slope += d * (k * a * cos(theta));
	}
	slope *= strength * gust;
	float near = 1.0 - smoothstep(10.0, 40.0, dist);
	vec2 fine = gnoised(p * 1.7 + wdir * t * 0.8).xy * 1.7 * 0.03 + gnoised(p * 4.1 - wdir.yx * t * 1.3 + 5.0).xy * 4.1 * 0.012;
	return slope + fine * near * (0.5 + 0.5 * strength);
}

// River ripples carried downstream (two phases cross-fading forever, no rotated coordinates: see classic shader),
// smeared along the current by a second tap, so fast water reads as streaks; glassy in slow pools.
vec2 flow_layer(vec2 q, vec2 dir, float stretch, float dist) {
	vec2 s1 = gnoised(q * 0.5).xy * 0.5 * 0.35 + gnoised((q - dir * stretch) * 0.5).xy * 0.5 * 0.35;
	vec2 s2 = gnoised(q * 1.4 + 3.1).xy * 1.4 * 0.12 + gnoised((q - dir * stretch * 0.6) * 1.4 + 3.1).xy * 1.4 * 0.12;
	vec2 s3 = gnoised(q * 3.6 + 7.7).xy * 3.6 * 0.035 * (1.0 - smoothstep(6.0, 25.0, dist));
	return (s1 + s2) * 0.5 + s3;
}
vec2 flow_slope(vec2 p, vec2 f, float t, float dist) {
	float speed = length(f);
	const float cycle = 1.4;
	float p0 = fract(t / cycle), p1 = fract(t / cycle + 0.5);
	float w0 = 1.0 - abs(2.0 * p0 - 1.0);
	vec2 dir = speed > 0.001 ? f / speed : vec2(1.0, 0.0);
	float stretch = 0.3 + smoothstep(0.2, 2.5, speed) * 2.2;
	vec2 sa = flow_layer(p - f * p0 * cycle, dir, stretch, dist);
	vec2 sb = flow_layer(p - f * p1 * cycle + vec2(13.1, 7.7), dir, stretch, dist);
	float rough = 0.15 + 0.85 * smoothstep(0.05, 1.6, speed);
	return (sa * w0 + sb * (1.0 - w0)) * rough / (1.0 + dist / 90.0);
}

// Foam reveal: a drifting, warped bubbly pattern shown where the foam amount is high, with soft edges, so foam
// thickens where the source is strong and thins into lace and holes as it fades (never a painted outline).
float foam_mask(vec2 p, vec2 drift, float t, float amount, float dist) {
	if (amount <= 0.001) return 0.0;
	float near = 1.0 - smoothstep(12.0, 70.0, dist); // fine bubbles only close up (they alias into blocks far away)
	vec2 q = p + drift * t;
	q += (vec2(noise(q * 0.21), noise(q * 0.21 + 5.7)) - 0.5) * 3.0; // swirl
	float fbm = noise(q * 0.55) * 0.5 + noise(q * 1.6 + t * 0.1) * 0.3 + mix(0.5, noise(q * 4.4 - t * 0.2), near) * 0.2;
	float bubbles = noise(q * 6.5 + t * 0.4) * noise(q * 11.0 - t * 0.3); // fine bubbly speckle (no cell outlines)
	float pattern = fbm * 0.85 + mix(0.12, bubbles, near) * 0.3;
	float m = smoothstep(1.05 - amount * 1.05, 1.05 - amount * 1.05 + 0.32, pattern);
	return m * smoothstep(0.0, 0.35, amount) * 0.92;
}

void fragment() {
	vec2 p = rest_xz;
	float sea = is_sea(body), river = is_river(body), lake = 1.0 - sea - river, pond = is_pond(body);
	float dist = length(VERTEX);
	vec2 wdir = normalize(vec2(wind.x, wind.z) + vec2(1e-4));
	float wind_speed = clamp(wind.y, 0.0, 2.5);

	// Bed depth per pixel from the depth buffer (the seabed actually behind this pixel), so shore waves follow the real
	// bed contours instead of the mesh's per-vertex depth (which drew crest lines along triangle edges). Used where the
	// bed is near and seen; the vertex depth elsewhere.
	vec3 behind0 = view_from_depth(SCREEN_UV, texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX);
	vec3 bed0 = (INV_VIEW_MATRIX * vec4(behind0, 1.0)).xyz;
	float bed_depth = max(0.0, (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).y - bed0.y);
	// Close range only: further out the bed behind is a coarse, faceted terrain tile, and the shore-wave phase is so
	// sensitive to depth that every facet showed as a blocky patch.
	float use_px = smoothstep(18.0, 10.0, bed_depth) * (1.0 - smoothstep(25.0, 55.0, dist)) * step(length(behind0), 3000.0);
	float px_depth = mix(vertex_depth, bed_depth, use_px);

	// Per-pixel wave normal from the full wave sum (smooth at every distance).
	vec3 n = vec3(0.0, 1.0, 0.0); float crest = 0.0; vec3 jac = vec3(0.0);
	float breaking = 0.0, since = 1.0, shore_k = 1.0;
	if (sea + lake - pond > 0.5) {
		// Waves far shorter than a pixel only add sparkle: fade them with distance.
		float min_lambda = dist / 300.0;
		gerstner(p, vertex_depth, sea, lake, fetch, sea > 0.5 ? exposure : 1.0, min_lambda, ocean_time, n, crest, jac);
		if (sea > 0.5) shore_wave(p, px_depth, bed_grad, exposure, min_lambda, ocean_time, n, breaking, since, shore_k);
	}
	crest /= max(wave_height, 0.01);
	float fold = 1.0 - ((1.0 - jac.x) * (1.0 - jac.y) - jac.z * jac.z);
	vec2 slope = -n.xz / max(n.y, 0.2);

	// Small-scale detail: wind ripples on seas and lakes (stronger with wind and storms), flow ripples on rivers.
	float bed_slope = length(bed_grad);
	float rocky = sea * smoothstep(0.12, 0.35, bed_slope) * (1.0 - smoothstep(2.0, 9.0, vertex_depth)) * (0.4 + sea_state) * exposure;
	if (river > 0.5) slope += flow_slope(p, current, TIME, dist) * (1.0 + 0.6 * sea_state);
	else {
		// Small lakes stay calm (little fetch for the wind to raise waves); big ones approach the sea.
		float lake_calm = mix(1.0, clamp(0.3 + fetch / 4000.0, 0.3, 0.75), lake);
		float strength = (0.2 + 0.35 * wind_speed + 0.8 * sea_state) * lake_calm * mix(1.0, 0.25, pond) + rocky * 1.5;
		slope += ripple_slope(p, TIME, wdir, strength, dist);
		// Wind sea: mid-scale choppy waves running with the wind on the swell, much stronger in storms (sharpened
		// by folding the noise), fading with distance into the swell's own lighting.
		float chop = (0.03 + 0.35 * sea_state * sea_state + 0.08 * wind_speed) * (sea + lake * 0.35 - pond * 0.3) / (1.0 + dist / 250.0);
		vec2 side = vec2(-wdir.y, wdir.x);
		vec3 c1 = gnoised(p * 0.07 + wdir * TIME * 0.25);
		vec3 c2 = gnoised(p * 0.17 - (wdir * 0.6 + side * 0.4) * TIME * 0.4 + 3.7);
		slope += (c1.xy * 0.07 + c2.xy * 0.17 * 0.6) / 0.15 * chop;
	}
	vec3 n_world = normalize(vec3(-slope.x, 1.0, -slope.y));
	vec3 n_view = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);
	NORMAL = n_view;
	vec3 to_eye = normalize(-VERTEX);
	float fresnel = 0.02 + 0.98 * pow(1.0 - clamp(dot(n_view, to_eye), 0.0, 1.0), 5.0);

	// Behind the surface (depth buffer), refracted; never pull in things above water.
	vec3 behind = view_from_depth(SCREEN_UV, texture(depth_tex, SCREEN_UV).r, INV_PROJECTION_MATRIX);
	float straight_path = max(0.0, length(behind) - dist);
	vec2 ruv = clamp(SCREEN_UV + n_world.xz * refraction * clamp(straight_path * 0.5, 0.0, 1.0), vec2(0.001), vec2(0.999));
	vec3 rbehind = view_from_depth(ruv, texture(depth_tex, ruv).r, INV_PROJECTION_MATRIX);
	if (length(rbehind) < dist) { ruv = SCREEN_UV; rbehind = behind; }
	float path = max(0.0, length(rbehind) - dist);
	vec3 surface_world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 bottom_world = (INV_VIEW_MATRIX * vec4(rbehind, 1.0)).xyz;
	float drop = abs(normalize(VERTEX).y);
	float depth_below = max(max(0.0, surface_world.y - bottom_world.y), path * drop * 0.5);
	float contact_depth = max(max(0.0, surface_world.y - (INV_VIEW_MATRIX * vec4(behind, 1.0)).y), straight_path * drop * 0.5);

	// Transmitted light: the bed through clear shallows, absorbed with depth; biome tint inside the absorption.
	vec3 ground = textureLod(screen_tex, ruv, 0.0).rgb;
	float caustic = caustics(bottom_world.xz * 0.6, TIME * 1.3) * smoothstep(0.0, 0.1, depth_below) * exp(-depth_below * 0.35);
	ground += caustic * caustic_strength * 0.3 * vec3(1.0, 0.97, 0.85) * (0.4 + ground);
	vec3 transmittance = exp(-absorption * path * mix(1.0, 2.2, river));
	vec3 scatter = mix(sea * sea_scatter + lake * lake_scatter + river * river_scatter, river_scatter * vec3(1.15, 1.1, 0.9), plume * 0.85);
	transmittance = mix(transmittance, exp(-absorption * path * 2.2), plume * 0.6);
	scatter = mix(scatter, biome_tint.rgb * 0.35, biome_tint.a);
	transmittance = mix(transmittance, transmittance * transmittance * biome_tint.rgb, biome_tint.a * 0.8);
	// Stirred-up shallows (breakers, rapids) are cloudier.
	float stirred = clamp(breaking * 0.7 + rocky * 0.5 + river * smoothstep(1.5, 3.5, length(current)) * 0.4, 0.0, 0.8);
	transmittance *= 1.0 - stirred * 0.5;
	float murk = 1.0 - dot(transmittance, vec3(0.333));

	// Screen-space reflection, sky as fallback (rougher, flatter sky in storms).
	vec3 ray = normalize(reflect(VERTEX, n_view));
	vec4 hit = vec4(0.0);
	float step_length = ssr_steps <= 16 ? 0.4 : 0.25;
	float growth = ssr_steps <= 16 ? 1.38 : 1.18;
	vec3 march = VERTEX + ray * 0.1;
	for (int i = 0; i < ssr_steps; i++) {
		march += ray * step_length;
		step_length *= growth;
		vec4 clip = PROJECTION_MATRIX * vec4(march, 1.0);
		if (clip.w <= 0.0) break;
		vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) break;
		float scene_z = view_from_depth(uv, texture(depth_tex, uv).r, INV_PROJECTION_MATRIX).z;
		float gap = scene_z - march.z;
		if (gap > 0.0 && gap < step_length * 2.5 + 0.3) {
			vec2 edge = smoothstep(vec2(0.0), vec2(0.08), uv) * smoothstep(vec2(0.0), vec2(0.08), 1.0 - uv);
			hit = vec4(textureLod(screen_tex, uv, 0.0).rgb, edge.x * edge.y * (1.0 - float(i) / float(ssr_steps)));
			break;
		}
	}

	// ---- foam amount from every source, revealed through the drifting pattern
	vec2 drift = river > 0.5 ? current * 0.9 : wdir * (0.15 + 0.2 * wind_speed);
	float contact = 1.0 - smoothstep(0.0, mix(0.04, 0.16, sea) + 0.1 * river, contact_depth);
	float amount = contact * mix(0.45, 0.75, sea);
	if (sea > 0.5) {
		// Breakers: a band on the crest that leaves a fading trail behind it.
		float along = 0.55 + 0.45 * noise(p * 0.07 + vec2(TIME * 0.03, 0.0)); // breakers come and go along the coast
		// The crest band is smooth across the phase wrap (a gaussian in phase distance) and the trail rises from 0 at
		// the crest, so the foam never ends in a hard front line.
		float wrap = min(since, 1.0 - since);
		// At least ~1.6 m wide on the water (a fixed phase width became a thin drawn line where waves bunch up),
		// tumbling: broken along and across by noise.
		float bw = max(0.055, 1.6 * shore_k / PI2);
		float tumble = 0.55 + 0.45 * noise(p * 0.45 + vec2(TIME * 0.4, 0.0)) + 0.25 * (noise(p * 1.3 - TIME * 0.6) - 0.5);
		float band = exp(-(wrap / bw) * (wrap / bw)) * tumble;
		// Trailing foam thins into lace (broken by noise) instead of a solid sheet.
		float trail = exp(-since * 7.0) * smoothstep(0.0, 0.06, since) * smoothstep(0.35, 0.75, noise(p * 0.3 + vec2(0.0, TIME * 0.1)));
		amount = max(amount, breaking * along * smoothstep(0.15, 0.5, px_depth) * (band * 1.0 + trail * 0.85));
		// Whitecaps on compressed, sharp crests in rough seas.
		amount = max(amount, smoothstep(0.3 - 0.15 * sea_state, 0.8, fold) * smoothstep(0.15, 0.6, sea_state) * 0.85);
		amount = max(amount, smoothstep(0.6, 0.95, crest) * smoothstep(0.45, 0.9, sea_state) * 0.5);
		// Rocks and steep coasts churn.
		amount = max(amount, rocky * (0.4 + 0.5 * noise(p * 0.2 + TIME * 0.3)));
	} else if (river > 0.5) {
		float fs = length(current);
		// Whitewater where the current is fast, more so over shallow beds (rocks, riffles).
		float rapids = smoothstep(1.0, 2.8, fs) * (0.6 + 0.4 * (1.0 - smoothstep(0.3, 1.5, vertex_depth)));
		// Whitewater comes in pulses and patches, not a uniform sheet.
		float pulse = 0.65 + 0.35 * noise(p * 0.08 - current * TIME * 0.05);
		amount = max(amount, rapids * pulse * 0.95);
		amount = max(amount, smoothstep(0.5, 2.0, fs) * 0.35); // streaks in moving water
	} else {
		amount = max(amount, smoothstep(0.45, 0.9, fold) * smoothstep(0.5, 0.9, sea_state) * 0.6 * lake);
	}
	float amt = clamp(amount, 0.0, 1.0);
	float foam = foam_mask(p, drift, TIME, amt, dist);
	if (river > 0.5 && length(current) > 0.3) {
		// Streaks along the current: the same foam smeared downstream.
		vec2 fdir = normalize(current);
		foam = (foam + foam_mask(p - fdir * 1.8, drift, TIME, amt, dist) + foam_mask(p - fdir * 3.6, drift, TIME, amt, dist)) / 3.0 * 1.3;
	}
	foam = foam * (1.0 / (1.0 + dist / 400.0) * 0.5 + 0.5) * smoothstep(0.2, 0.9, edge_fade)
		* smoothstep(0.01, 0.12, contact_depth); // the thinnest swash film carries no solid foam

	// Aerated crests in rough weather: milky streaks over the wave tops (softer than foam, not masked).
	float aerated = (sea + lake * 0.4) * smoothstep(0.3, 1.0, crest + 0.4 * (noise(p * 0.12 + wdir * TIME * 0.3) - 0.5))
		* smoothstep(0.3, 0.9, sea_state) * 0.5 * (0.55 + 0.45 * noise(p * 0.8 - TIME * 0.2)) / (1.0 + dist / 800.0);
	foam = max(foam, aerated);

	vec3 transmitted = ground * transmittance;
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(ray, 0.0)).xyz);
	vec3 sky = mix(sky_horizon, sky_zenith, smoothstep(0.0, 0.6, ray_world.y));
	sky = mix(sky, (sky_horizon + sky_zenith) * 0.5, sea_state * 0.5);
	vec3 reflected = mix(sky, hit.rgb, hit.a) * reflection_strength;
	// Light through thin crests (seas, breakers): looking toward the sun, the upper part of waves glows.
	vec3 eye_world = normalize(CAMERA_POSITION_WORLD - world_position);
	vec3 sun_flat = normalize(vec3(sun_direction.x, 0.0, sun_direction.z) + 1e-4);
	float back_lit = pow(clamp(dot(-eye_world, sun_flat), 0.0, 1.0), 2.0) * clamp(sun_direction.y * 3.0, 0.0, 1.0);
	float thin = clamp(smoothstep(0.1, 0.9, crest) * (sea + lake * 0.3) * min(1.0, wave_height * 0.8)
		+ breaking * exp(-pow(min(1.0 - since, since + 0.12) / 0.12, 2.0)) * 0.8, 0.0, 1.0); // glow just ahead of the crest
	vec3 glow = crest_glow * sun_color * (back_lit * 1.2 + 0.25) * thin;
	// Sun glints: a tight highlight that moves over the waves (the engine's specular adds the broad sheen).
	vec3 half_v = normalize(normalize(sun_direction) + eye_world);
	float glint = pow(clamp(dot(n_world, half_v), 0.0, 1.0), 900.0) * clamp(sun_direction.y * 4.0, 0.0, 1.0) * (1.0 - sea_state * 0.6);
	ALBEDO = mix(scatter * murk * (1.0 - fresnel), foam_color, foam);
	EMISSION = (transmitted * (1.0 - fresnel) + reflected * fresnel + glow * (1.0 - fresnel) + sun_color * glint * 3.0) * (1.0 - foam);
	SPECULAR = 0.3 * (1.0 - foam);
	ROUGHNESS = mix(mix(0.03, 0.12, sea_state), 0.6, foam);
	METALLIC = 0.0;
	// The swash film running up the beach thins out softly (a hard 4 cm ramp drew its triangle-edged outline).
	float film = sea > 0.5 ? mix(0.22, 0.04, smoothstep(0.3, 1.5, vertex_depth)) : 0.04;
	ALPHA = smoothstep(0.0, film, contact_depth) * smoothstep(0.1, 0.9, edge_fade);
}
