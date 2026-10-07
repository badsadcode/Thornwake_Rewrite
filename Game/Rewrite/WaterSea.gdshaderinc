// The water of experimental 2 and 3 (round 23: shared, so the FFT sea of experimental 3 keeps every shore, river,
// lake, foam and reflection feature). Experimental 3 defines FFT_OCEAN: the open-sea and lake waves come from the GPU
// FFT cascades (OceanFft.cs) instead of the 64 Gerstner waves; everything else is the same code.


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
uniform vec4 waves2[64];      // spectrum sea: dir.x, dir.z, k, base amplitude (SpectrumWaves.Waves; 52 wind sea + 12 swell)
uniform float wave2_phase[64]; // per wave: phase including the float origin (SpectrumWaves.ShiftedPhase)
uniform float wave2_hs = 1.0; // significant wave height (m)
uniform float wave2_chop = 0.8; // Σ q k A at a point (SpectrumWaves.Chop)
uniform vec2 world_origin;    // world position of scene (0, 0): regional wave headings and groups are world-anchored
uniform vec4 wake_points[24]; // wakes (Wakes.cs): scene x, z, birth time (ocean_time), strength (relative speed)
uniform vec4 wake_flow[24];   // per wake point: velocity relative to the water x, z; the local current x, z
uniform vec2 wake_centre = vec2(1e9); // the wakes' source now (rings are only looked for near it)
// The sky's clouds (Sky.gdshader, shared by Atmosphere.cs), so the water reflects the real sky instead of a gradient.
uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform float cloud_period = 6000.0;
uniform float cloud_height = 1600.0;
global uniform vec4 cloud_state;  // camera world xz + wind drift (wrapped), coverage, storm
global uniform vec4 cloud_layers; // the cirrus and low layers' offsets
global uniform vec4 sun_true;     // the real sun direction and elevation (deg), also below the horizon (twilight)
#include "res://Game/Rewrite/CloudShapes.gdshaderinc" // far cloud types (towers, distant storms, lenticulars)
uniform int debug_foam = 0;   // 1: show the foam amount (tests)
uniform vec3 sss_color : source_color = vec3(0.06, 0.62, 0.56); // light scattered through thin wave tops
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
varying float rest_level; // undisplaced water level (bed depth per pixel is measured from here)
varying vec2 current;
varying float plume;
varying vec4 biome_tint;
varying vec2 bed_grad;    // uphill bed gradient (toward the shore)
varying float exposure;
varying float fetch;
varying float edge_fade; // 1 on water, fading over the sheet's extension under the beach (COLOR.a)
varying vec4 wave_heads;  // local headings: wind sea xy, swell zw (vary over kilometres: per vertex is enough)
varying vec3 wave_norms;  // direction-weight renormalisation (wind sea, swell) and the crest-sharpness sum
varying float wave_coast; // 0 open water … 1 coastal shallows: headings turned toward the shore, narrower spread

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

// Body (COLOR.g, TileBuilder.Kind): sea 0, river ⅓, lake ⅔, pond 1 — as smooth weights that sum to 1, so a river
// opening into the sea or a lake blends across the triangles between them instead of switching at a hard line.
float is_sea(float b) { return 1.0 - smoothstep(0.0, 0.3333, b); }
float is_river(float b) { return smoothstep(0.0, 0.3333, b) * (1.0 - smoothstep(0.3333, 0.6667, b)); }
float is_lake(float b) { return smoothstep(0.3333, 0.6667, b) * (1.0 - smoothstep(0.6667, 1.0, b)); }
float is_pond(float b) { return smoothstep(0.6667, 1.0, b); }

// Amplitude factor of wave i: shallow-water fade, lakes cut to their size, sheltered coasts calmer.
float wave_factor(int i, float lambda, float depth, float sea, float lake, float fetch_m, float expo) {
	float f = smoothstep(0.0, 1.0, depth / (lambda * 0.12 + 0.3));
	if (sea > 0.5) {
		// Long waves give way to the shore waves over the shallows; bays and coves are calmer.
		// They keep running at their own angle well into the shallows, over the shore-facing waves (Noor: not every
		// crest should hug the beach).
		f *= mix(0.3, 1.0, expo) * mix(0.4, 1.0, smoothstep(4.0, 19.0, depth));
	} else if (lake > 0.5) {
		// Lakes: only waves shorter than ~1/25 of the fetch; amplitude grows with the lake's size; no swell.
		float longest = clamp(fetch_m * 0.04, 2.5, 45.0);
		f *= (1.0 - smoothstep(longest, longest * 1.6, lambda)) * clamp(0.18 + fetch_m / 6000.0, 0.18, 0.85);
		f *= smoothstep(0.0, 2.5, depth); // calmer along the shore
	} else f = 0.0;
	return f;
}

// Wave groups: sets of higher waves drifting with the wind (SpectrumWaves.Group: same formula, world coordinates).
float group(vec2 p, float t, vec2 wd) {
	vec2 x = p + world_origin;
	float n = noise(vec2(x.x * 0.0035 - wd.x * t * 0.012, x.y * 0.0035 - wd.y * t * 0.012));
	return 0.65 + (0.45 + 0.35 * sea_state) * n * 1.2 - 0.15;
}

// Coastal factor (SpectrumWaves.Coastal): 0 in open water, 1 in the coastal shallows of seas (not lakes, not steep
// rocky coasts), from the bed depth and slope.
float coastal(float depth, vec2 grad, float lake) {
	float s = length(grad);
	return (1.0 - lake) * smoothstep(20.0, 4.0, depth) * smoothstep(0.003, 0.012, s) * (1.0 - smoothstep(0.22, 0.45, s));
}

// Turn heading a a share k of the way to target, blending directions as vectors (SpectrumWaves.TurnToward): by angle
// it jumped by 2πk where the shore lay straight behind the wind — a seam across the sea (Tom, build 23).
float turn_toward(float a, float target, float k) {
	vec2 v = vec2(cos(a), sin(a)) * (1.0 - k) + vec2(cos(target), sin(target)) * k;
	return dot(v, v) < 1e-10 ? target : atan(v.y, v.x);
}

// Local headings (SpectrumWaves.Headings): the wind turned by a slow regional field (lakes: their own valley winds),
// and the swell's heading from far storms (independent of the wind). Toward the coast both turn toward the shore
// (Noor: open-water direction blended into the shore direction as the water shallows) — only wave weights change, so
// the turn is smooth however quickly it happens.
vec4 headings(vec2 p, vec2 wd, float lake, vec2 grad, float coast) {
	vec2 x = p + world_origin;
	float wind_a = atan(wd.y, wd.x);
	float turn = lake > 0.5
		? 2.4 * (noise(x / 3000.0 + vec2(57.1, -13.7)) - 0.5) + 0.8 * (noise(x / 900.0 + vec2(5.3, 41.9)) - 0.5)
		: 2.0 * (noise(x / 8000.0) - 0.5) + 0.7 * (noise(x / 2500.0 + vec2(31.7, 8.9)) - 0.5);
	float swell = 1.5 + 5.2 * (noise(x / 20000.0 + vec2(91.3, -27.1)) - 0.5);
	float wa = wind_a + turn;
	if (coast > 0.0) {
		float shore = atan(grad.y, grad.x);
		wa = turn_toward(wa, shore, coast * 0.85);
		swell = turn_toward(swell, shore, coast * 0.95);
	}
	return vec4(cos(wa), sin(wa), cos(swell), sin(swell));
}

// Direction weight of wave i (SpectrumWaves.Weight): narrow around the heading in calm weather, wide in storms.
// Negligible weights fade to exactly 0 (continuously), so about half the waves are skipped at every point.
float dir_weight(int i, vec4 h, float lake, float coast) {
	vec2 d = waves2[i].xy;
	float c = i >= 52 ? (lake > 0.5 ? 0.0 : dot(d, h.zw)) : dot(d, h.xy);
	if (c <= 0.0) return 0.0;
	// Narrower toward the coast: the waves organise into sets running toward the shore.
	float w = pow(c, i >= 52 ? 24.0 : mix(10.0 - 8.4 * sea_state, 16.0, coast * 0.6));
	return max(0.0, w - 0.002) / 0.998;
}

// Renormalisation of the weighted waves (same height everywhere) and the crest-sharpness sum (SpectrumWaves.Local).
vec3 wave_norm(vec4 h, float lake, float coast) {
	float ea = 0.0, ew = 0.0, sa = 0.0, sw = 0.0;
	for (int i = 0; i < 64; i++) {
		float w = dir_weight(i, h, lake, coast), a2 = waves2[i].w * waves2[i].w;
		if (i < 52) { ea += a2; ew += a2 * w; } else { sa += a2; sw += a2 * w; }
	}
	float nw = sqrt(ea / max(ew, 1e-12)), ns = sqrt(sa / max(sw, 1e-12));
	float crisp = 0.0;
	for (int i = 0; i < 64; i++) {
		float w = dir_weight(i, h, lake, coast);
		if (w <= 0.0) continue;
		float a = waves2[i].w * sqrt(w) * (i < 52 ? nw : ns), k = waves2[i].z;
		crisp += k * k * a * a * a;
	}
	return vec3(nw, ns, max(crisp, 1e-12));
}

// Spectrum Gerstner sum at rest position p. Geometry: only waves longer than min_lambda (what the mesh can carry);
// lighting: all down to a pixel (called per pixel). Returns displacement; accumulates normal, crest height and Jacobian.
// Choppiness goes mostly to the biggest steep waves (share ∝ k²A³): crisp crests on the waves that matter.
#ifdef FFT_OCEAN
// Experimental 3 (round 23, Tom: "the Blender ocean modifier waves are choppy, crisp… aiming for Sea of Thieves"): the
// open sea and lakes from three FFT cascades computed on the GPU every frame (OceanFft.cs: 480, 59 and 7.9 m tiles,
// 256² each, a JONSWAP wind sea plus a swell, deterministic from the seed and the ocean clock). Choppy: the horizontal
// displacement pinches the crests (it may fold: that is where foam is born), and a lingering foam field (whitecaps that
// stay and trail behind the crests) comes with each cascade. Same outputs as gerstner() below, so the rest of the water
// (shore waves, rivers, foam, lighting) is shared with experimental 2.
#ifdef EXP3A
// (anisotropic: low over the water a pixel is long and thin; isotropic mipmaps blurred the far sea flat)
uniform sampler2DArray fft_disp : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray fft_deriv : filter_linear_mipmap_anisotropic, repeat_enable;
#else
uniform sampler2DArray fft_disp : filter_linear_mipmap, repeat_enable;  // per cascade: λDx, Dy, λDz, foam (mipmapped)
uniform sampler2DArray fft_deriv : filter_linear_mipmap, repeat_enable; // per cascade: dDy/dx, dDy/dz, λdDx/dx, λdDz/dz
#endif
uniform vec3 fft_size = vec3(480.0, 59.0, 7.9);
uniform vec3 fft_longest = vec3(480.0, 12.0, 1.6); // longest wavelength each cascade carries (fades when under a pixel)
uniform vec3 fft_offset_x; // the float origin modulo each tile (double precision on the CPU): world-anchored waves
uniform vec3 fft_offset_z;
uniform float fft_crest_scale = 1.0; // wind-sea Hs / total Hs: the crest measure (glow, foam) is scaled by the wind sea

// spacing > 0 (vertices): the mipmap level for that vertex spacing; 0 (pixels): the hardware picks it.
void fft_cascade(float layer, vec2 p, float size, float ox, float oz, float amp, float spacing,
		inout vec3 offset, inout vec4 deriv, inout float foam) {
	vec3 uv = vec3((p + vec2(ox, oz)) / size + 0.5 / 256.0, layer); // (texel centres)
	vec4 d, g;
	if (spacing > 0.0) {
		float lod = log2(max(spacing * 256.0 / size, 1.0));
		d = textureLod(fft_disp, uv, lod);
		g = textureLod(fft_deriv, uv, lod);
	} else {
		d = texture(fft_disp, uv);
		g = texture(fft_deriv, uv);
	}
	offset += d.xyz * amp;
	deriv += g * amp;
	foam = max(foam, d.w * min(amp, 1.0));
}

vec3 gerstner(vec2 p, float depth, float sea, float lake, float fetch_m, float expo, float min_lambda, float t,
		vec4 h, vec3 norms, float coast, inout vec3 n, inout float crest, inout vec3 jac) {
	// Big waves die toward the shore (the shore waves take over) and in sheltered bays; lakes carry only the short
	// cascades, scaled by their fetch.
	float a0 = sea * mix(0.25, 1.0, expo) * smoothstep(0.5, 14.0, depth);
	// Lakes: fetch-limited chop, growing with the wind (only the sea's short waves reach them, and those barely grow
	// with the sea state: a windy lake looked like a calm one).
	float lake_amp = lake * clamp(fetch_m / 1500.0, 0.15, 0.75) * (0.6 + 2.4 * pow(sea_state, 1.5));
	float a1 = sea * mix(0.5, 1.0, expo) * smoothstep(0.2, 3.0, depth) + lake_amp;
	float a2 = sea * smoothstep(0.05, 1.0, depth) + lake_amp;
	// A cascade fades once its longest waves are only a few pixels (vertices) long: its detail would only shimmer.
#ifdef EXP3A
	// (later: the mipmaps already band-limit the detail; the far sea went smooth too soon — Noor)
	a1 *= smoothstep(fft_longest.y * 1.2, fft_longest.y * 0.5, min_lambda);
	a2 *= smoothstep(fft_longest.z * 1.2, fft_longest.z * 0.5, min_lambda);
#else
	a1 *= smoothstep(fft_longest.y * 0.5, fft_longest.y * 0.25, min_lambda);
	a2 *= smoothstep(fft_longest.z * 0.5, fft_longest.z * 0.25, min_lambda);
#endif
	vec3 offset = vec3(0.0);
	vec4 deriv = vec4(0.0); // dDy/dx, dDy/dz, dDx/dx, dDz/dz (choppiness included)
	float foam = 0.0;
	// The vertex pass calls this with min_lambda = 5 × spacing; the pixel pass lets the texture unit pick the level.
	float spacing = t < 0.0 ? min_lambda * 0.2 : 0.0; // (t < 0: the vertex pass, see vertex())
	if (a0 > 0.0) fft_cascade(0.0, p, fft_size.x, fft_offset_x.x, fft_offset_z.x, a0, spacing, offset, deriv, foam);
	if (a1 > 0.0) fft_cascade(1.0, p, fft_size.y, fft_offset_x.y, fft_offset_z.y, a1, spacing, offset, deriv, foam);
	if (a2 > 0.0) fft_cascade(2.0, p, fft_size.z, fft_offset_x.z, fft_offset_z.z, a2, spacing, offset, deriv, foam);
	// Slopes of the displaced surface (the chop compresses the crests: steeper there).
	float sx = 1.0 + deriv.z, sz = 1.0 + deriv.w;
	n.x -= deriv.x / max(sx, 0.25);
	n.z -= deriv.y / max(sz, 0.25);
	crest += offset.y * fft_crest_scale; // (the swell lifts the whole surface: a calm sea glowed everywhere)
#ifdef EXP3A
	// The foam field is read where the water is shown (rest position + displacement), not at its rest position: the
	// field drifts with the wind on the GPU, and read at the rest position it swung back and forth with every wave
	// (Tom and Noor: "streaks oscillate around the same place"). At a crest the horizontal displacement is ~0, so
	// foam born there still sits on the crest. jac.z carries it to the fragment (fold uses x and y only).
	if (t < 0.0) { jac += vec3(-deriv.z, -deriv.w, 0.0); return offset; } // (vertex pass: no foam)
	vec2 q = p + offset.xz;
	float f = 0.0;
	if (a0 > 0.0) f = max(f, texture(fft_disp, vec3((q + vec2(fft_offset_x.x, fft_offset_z.x)) / fft_size.x + 0.5 / 256.0, 0.0)).w * min(a0, 1.0));
	if (a1 > 0.0) f = max(f, texture(fft_disp, vec3((q + vec2(fft_offset_x.y, fft_offset_z.y)) / fft_size.y + 0.5 / 256.0, 1.0)).w * min(a1, 1.0));
	if (a2 > 0.0) f = max(f, texture(fft_disp, vec3((q + vec2(fft_offset_x.z, fft_offset_z.z)) / fft_size.z + 0.5 / 256.0, 2.0)).w * min(a2, 1.0) * 0.5);
	jac += vec3(-deriv.z, -deriv.w, f);
#else
	// As gerstner(): fold = 1 - Jacobian; the lingering foam joins it (whitecaps trailing behind the crests).
	jac += vec3(-deriv.z + foam * 0.9, -deriv.w, 0.0);
#endif
	return offset;
}
#else
vec3 gerstner(vec2 p, float depth, float sea, float lake, float fetch_m, float expo, float min_lambda, float t,
		vec4 h, vec3 norms, float coast, inout vec3 n, inout float crest, inout vec3 jac) {
	vec3 offset = vec3(0.0);
	float grp = group(p, t, normalize(vec2(wind.x, wind.z) + vec2(1e-4)));
	float chop = wave2_chop / max(grp, 1.0) / norms.z; // higher wave sets must not fold either
	for (int i = 0; i < 64; i++) {
		float k = waves2[i].z;
		float lambda = PI2 / k;
		float cut = smoothstep(min_lambda, min_lambda * 2.0, lambda);
		if (cut <= 0.0) continue;
		float w = dir_weight(i, h, lake, coast);
		if (w <= 0.0) continue;
		float base = waves2[i].w * sqrt(w) * (i < 52 ? norms.x : norms.y);
		if (base <= 1e-5) continue;
		float amp = min(base, 0.34 / k) * grp * wave_factor(i, lambda, depth, sea, lake, fetch_m, expo) * cut;
		if (amp <= 0.0) continue;
		vec2 d = waves2[i].xy;
		float theta = k * dot(d, p) - sqrt(G * k) * t + wave2_phase[i];
		float c = cos(theta), s = sin(theta);
		float q = chop * k * base * base;
		offset += vec3(q * amp * d.x * c, amp * s, q * amp * d.y * c);
		n.x -= d.x * k * amp * c; n.z -= d.y * k * amp * c; n.y -= q * k * amp * s;
		crest += amp * s;
		float qa = q * k * amp * s;
		jac += vec3(qa * d.x * d.x, qa * d.y * d.y, qa * d.x * d.y);
	}
	return offset;
}
#endif

// Shore waves (sea): crests along the depth contours rolling in toward the shore (see header). Returns displacement;
// accumulates the normal; breaking 0..1 and since (0 at the crest … 1 one period behind it) for the foam.
vec3 shore_wave(vec2 p, float depth, vec2 grad, float expo, float min_lambda, float t, inout vec3 n, out float breaking, out float since, out float k_out) {
	breaking = 0.0; since = 1.0; k_out = 1.0;
	float s = length(grad);
	// Hand-over from 19 m (experimental: 14 m): the waves visibly swing round to face the shore further out.
	float active = smoothstep(19.0, 9.0, depth) * smoothstep(0.003, 0.012, s) * (1.0 - smoothstep(0.22, 0.45, s));
	if (active <= 0.0) return vec3(0.0);
	vec2 dir = grad / s; // toward the shore
	float period = 7.0 + 4.0 * sea_state;
	float omega = PI2 / period;
	float d = max(depth, 0.05);
	// Travel-time phase over a reference bed slope: a function of depth only, so crests (θ = const) are depth contours
	// moving shoreward with time. (Dividing by the local slope made the phase jump by whole cycles between vertices
	// where the slope varies — blocky patches on coarse tiles.) The wavenumber is the phase's true gradient.
	const float S_REF = 0.03;
	float theta = -(2.0 * omega * sqrt(d) / (S_REF * sqrt(G)) + omega * t) + 3.0 * noise(p * 0.004) // slow wobble along the coast
		+ 1.5 * noise(p * 0.013 + 7.7) + 0.9 * noise(p * 0.035 + 2.3); // crest segments run ahead or lag
	float k = omega / sqrt(G * d) * clamp(s / S_REF, 0.15, 4.0);
	k_out = k;
	float lambda = PI2 / k;
	// Height: open-coast swell, shoaling (Green's law) until it breaks at ~0.75 d.
	float h0 = (0.12 + 1.15 * sea_state) * expo * (0.75 + 0.5 * noise(p * 0.01 + t * 0.02));
	float h = h0 * pow(10.0 / max(d, 0.4), 0.25);
	float limit = (0.62 + 0.2 * noise(p * 0.02 + 3.1) + 0.12 * noise(p * 0.06 + 9.4)) * d + 0.03; // some crest segments break sooner (irregular foam line)
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
	rest_level = world_position.y;
	float sea = is_sea(body), lake = is_lake(body);
	vec3 offset = vec3(0.0);
	wave_heads = vec4(1.0, 0.0, 1.0, 0.0); wave_norms = vec3(1.0); wave_coast = 0.0;
	if (sea + lake > 0.0) {
		wave_coast = coastal(vertex_depth, bed_grad, lake);
		wave_heads = headings(rest_xz, normalize(vec2(wind.x, wind.z) + vec2(1e-4)), lake, bed_grad, wave_coast);
		wave_norms = wave_norm(wave_heads, lake, wave_coast);
		// The mesh carries waves at least ~5 vertex spacings long (per-pixel lighting does the rest).
		float spacing = max(1.0, length(world_position - CAMERA_POSITION_WORLD) / 40.0);
		vec3 n = vec3(0.0, 1.0, 0.0); float crest = 0.0; vec3 jac = vec3(0.0);
#ifdef FFT_OCEAN
		float wave_t = -1.0; // (the FFT sea has no clock here; a negative time marks the vertex pass: explicit mip level)
#else
		float wave_t = ocean_time;
#endif
		offset = gerstner(rest_xz, vertex_depth, sea, lake, fetch, sea > 0.5 ? exposure : 1.0, 5.0 * spacing, wave_t, wave_heads, wave_norms, wave_coast, n, crest, jac);
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
// footprint: metres of water one pixel covers along the view (grows at grazing angles).
vec2 ripple_slope(vec2 p, float t, vec2 wdir, float strength, float dist, float footprint) {
	vec2 slope = vec2(0.0);
	float gust = 0.55 + 0.9 * (gnoised(p * 0.012 + wdir * t * 0.05).z + 0.5); // cat's paws: rougher patches
	for (int i = 0; i < 6; i++) {
		float fi = float(i);
		float lambda = 7.0 * pow(0.68, fi);
		float k = PI2 / lambda;
		float ang = (hash(vec2(fi, 3.7)) - 0.5) * 2.4 + (fi - 2.5) * 0.15;
		vec2 d = vec2(wdir.x * cos(ang) - wdir.y * sin(ang), wdir.x * sin(ang) + wdir.y * cos(ang));
		float fade = (1.0 - smoothstep(lambda * 25.0, lambda * 80.0, dist)) * (1.0 - smoothstep(lambda * 0.2, lambda * 0.45, footprint));
		if (fade <= 0.0) continue;
		float warp = 2.2 * gnoised(p * (0.04 / lambda) + vec2(fi * 7.1, fi * 3.3)).z;
		float theta = k * dot(d, p) - sqrt(G * k) * t + fi * 1.7 + warp;
		float a = lambda * 0.014 * fade * (0.6 + 0.8 * hash(vec2(fi, 9.1)));
		slope += d * (k * a * cos(theta));
	}
	slope *= strength * gust;
	float near = (1.0 - smoothstep(10.0, 40.0, dist)) * (1.0 - smoothstep(0.08, 0.2, footprint));
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
// Noise carried by a current that varies from place to place (round 20, Tom: "circles or thingies on the rivers… not
// moving"). noise(p - current * TIME) shears without bound: where the current turns or slows, two points a metre apart
// end up hundreds of metres apart in the noise after an hour, and the pattern winds into frozen arcs and stripes. Two
// copies scrolled over a short cycle and cross-faded (as flow_slope does) keep the drift without the shear.
float flow_noise(vec2 p, vec2 f, float t, float cycle) {
	float p0 = fract(t / cycle), p1 = fract(t / cycle + 0.5);
	float w0 = 1.0 - abs(2.0 * p0 - 1.0);
	return mix(noise(p - f * p1 * cycle + vec2(7.3, 1.9)), noise(p - f * p0 * cycle), w0);
}
vec2 flow_gnoise(vec2 p, vec2 f, float t, float cycle) {
	float p0 = fract(t / cycle), p1 = fract(t / cycle + 0.5);
	float w0 = 1.0 - abs(2.0 * p0 - 1.0);
	return mix(gnoised(p - f * p1 * cycle + vec2(7.3, 1.9)).xy, gnoised(p - f * p0 * cycle).xy, w0);
}

float sq(float x) { return x * x; } // pow(x, 2.0) is undefined for negative x in GLSL (NaN)

// Water around a rock in a current (RIVERS_PLAN step 5), as a height in rock radii: a, c = downstream / across of the
// rock in radii. A pressure bulge upstream, two V-wake arms opening downstream (narrower in fast water), and a turbulent
// core behind with alternating eddies drifting away (a von Kármán street, cheaply).
float obstacle_h(float a, float c, float fs, float t) { // t: a short cycled phase, never raw TIME
	float bulge = exp(-((a + 1.3) * (a + 1.3) * 1.6 + c * c * 1.1));
	float spread = mix(0.65, 0.32, smoothstep(0.5, 2.5, fs));
	float arm = smoothstep(-0.2, 0.3, a) * exp(-sq(abs(c) - (1.0 + max(a, 0.0) * spread)) * 3.0) * exp(-max(a, 0.0) * 0.11);
	float core = smoothstep(0.4, 1.2, a) * exp(-c * c / (0.5 + max(a, 0.0) * 0.12)) * exp(-max(a, 0.0) * 0.09);
	float eddy = sin(a * 1.5 - t * fs * 1.5) * sin(c * 2.2 + a * 0.7);
	return (bulge * 0.12 + arm * 0.05 * sin(a * 4.0 - t * fs * 2.5) + core * 0.06 * eddy) * (1.0 - smoothstep(7.0, 11.0, a));
}

// The sky in a reflected direction (round 16, Tom: "the reflection is a bit boring, bland when there's a lot"): the
// sky's gradient plus its high cirrus, main and low cloud layers along the ray, from where this water is (the same
// texture, heights and drift as Sky.gdshader, so the clouds in the water are the clouds overhead), lit from the sun's
// side and darkening at night / in storms with the sky colours (Atmosphere.UpdateWaterSky).
float sky_cloud(vec2 xz, float coverage) {
	vec2 uv = xz / cloud_period;
	float n = texture(cloud_tex, uv).r * 0.8 + texture(cloud_tex, uv * 4.0 + vec2(0.31, 0.17)).g * 0.2;
	float edge = 1.0 - coverage;
	return smoothstep(edge - 0.05, edge + 0.2, n) * step(0.001, coverage);
}
// Twilight as in Sky.gdshader (same formulas): sunset band toward the sun, Belt of Venus and Earth's shadow away from
// it, blue hour; clouds lit from below.
vec3 twilight_sky(vec3 dir, vec3 col) {
	float e = sun_true.w, h = max(dir.y, 0.0);
	vec2 sd = normalize(sun_true.xz + vec2(1e-4)), dd = normalize(dir.xz + vec2(1e-4));
	float az = dot(sd, dd);
	float gw = smoothstep(9.0, 1.0, e) * smoothstep(-7.0, -1.0, e);
	float band = exp(-h * h / mix(0.004, 0.02, smoothstep(-4.0, 6.0, e)));
	col += mix(vec3(1.0, 0.62, 0.25), vec3(0.95, 0.3, 0.12), smoothstep(3.0, -3.0, e)) * band * gw * (0.08 + 0.92 * max(az, 0.0) * max(az, 0.0)) * 0.65;
	float bw = smoothstep(4.0, 0.0, e) * smoothstep(-7.0, -2.0, e);
	float shadow_top = clamp(-e * 0.012, 0.0, 0.09), bx = (h - shadow_top - 0.06) / 0.05;
	col += vec3(0.62, 0.36, 0.45) * exp(-bx * bx) * bw * pow(max(-az, 0.0), 1.5) * 0.45;
	col += vec3(0.035, 0.07, 0.17) * smoothstep(-11.0, -4.0, e) * smoothstep(2.0, -2.0, e) * (0.55 + 0.45 * smoothstep(0.0, 0.5, h));
	return col;
}
vec3 cloud_underlight(vec3 dir) {
	float e = sun_true.w, w = smoothstep(-7.0, -1.0, e) * smoothstep(10.0, 2.0, e);
	vec2 sd = normalize(sun_true.xz + vec2(1e-4)), dd = normalize(dir.xz + vec2(1e-4));
	float toward = dot(sd, dd) * 0.5 + 0.5;
	return mix(vec3(1.0, 0.55, 0.25), vec3(0.95, 0.42, 0.52), smoothstep(1.0, -4.0, e)) * w * (0.25 + 0.75 * toward * toward);
}
vec3 reflected_sky(vec3 dir, vec2 offs, vec3 base) { // offs: this water's xz minus the camera's
	if (dir.y <= 0.0) return base;
	base = twilight_sky(dir, base);
	float up = dir.y, coverage = cloud_state.z, storm = cloud_state.w;
	float daylight = clamp(dot(sky_horizon, vec3(0.333)) / 0.72, 0.03, 1.2); // the sky colours carry day, night and storm
	vec3 sun_tint = mix(vec3(1.0), sun_color / max(max(sun_color.r, max(sun_color.g, sun_color.b)), 0.05), 0.5);
	vec2 to_sun = normalize(sun_direction.xz + vec2(1e-4));
	float mu = max(dot(dir, normalize(sun_direction)), 0.0);
	vec3 col = base;
	// High cirrus: thin streaks.
	{
		vec2 xz = cloud_layers.xy + offs + dir.xz * (7000.0 / max(up, 0.015));
		vec2 uv = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * (xz / (cloud_period * 2.5));
		float n = texture(cloud_tex, uv * vec2(1.0, 4.5)).g * 0.7 + texture(cloud_tex, uv * vec2(3.0, 9.0) + 0.37).r * 0.3;
		float edge = 1.0 - (0.1 + coverage * 0.45);
		float d = smoothstep(edge, edge + 0.3, n) * 0.35 * smoothstep(0.02, 0.15, up) * (1.0 - storm);
		col = mix(col, vec3(1.0, 0.99, 0.97) * daylight * sun_tint + cloud_underlight(dir) * 1.1, d);
	}
	// Main deck.
	{
		vec2 xz = cloud_state.xy + offs + dir.xz * (cloud_height / max(up, 0.015));
		float d = sky_cloud(xz, coverage);
		if (d > 0.0) {
			float toward = sky_cloud(xz + to_sun * 260.0, coverage);
			vec3 shade = mix(vec3(0.46, 0.5, 0.56), vec3(1.0, 0.99, 0.97), clamp(1.0 - toward * 0.65, 0.0, 1.0));
			vec3 cloud = shade * (0.2 + 0.85 * daylight) * sun_tint + sun_color * pow(mu, 6.0) * (1.0 - d) * 0.5;
			vec3 ul = cloud_underlight(dir);
			cloud = cloud * (1.0 - 0.45 * clamp(dot(ul, vec3(0.5)), 0.0, 0.8)) + ul * (0.45 + 0.55 * d) * 0.9;
			col = mix(col, cloud, d * smoothstep(0.015, 0.12, up));
		}
	}
	// Far clouds standing on the horizon (towers, distant storms, a lens over the mountains): low in the sky, so they
	// are just what calm water mirrors at a glancing angle.
	if (up < 0.5 && (cloud_types.x + cloud_types.y + cloud_lens.w) > 0.001) {
		vec3 ambient = (sky_horizon * 0.55 + sky_zenith * 0.45) * 1.1;
		vec4 far = ct_far(dir, normalize(sun_direction), sun_color, ambient, base, cloud_underlight(dir), true, true);
		col = col * (1.0 - far.a) + far.rgb;
	}
	return col;
}

// Foam reveal: a drifting, warped bubbly pattern shown where the foam amount is high, with soft edges, so foam
// thickens where the source is strong and thins into lace and holes as it fades (never a painted outline).
// Foam in three layers (Tom: "foam lacks detail"): x = the surface foam — dense where fresh, a lace of thin bright
// threads along bubble cells as it thins, fine speckle; y = milky aerated water just under the surface (wider, shines
// through); z = shading (creases a little darker). Pulled into streaks along the drift (wind, current). The lace fades
// to its average far away (cells smaller than a pixel would alias).
// foam_at takes the already-drifted position q and the streak frame (dn, stretch): only a frame that is the same
// everywhere (the wind's) may be applied to q, since dot(q, dn) with dn turning from place to place winds q into arcs.
vec3 foam_at(vec2 q, vec2 dn, float stretch, float t, float amount, float dist) {
	if (amount <= 0.001) return vec3(0.0, 0.0, 1.0);
	float near = 1.0 - smoothstep(12.0, 70.0, dist);
	float far = smoothstep(35.0, 160.0, dist);
	vec2 sd = vec2(-dn.y, dn.x);
	q += (vec2(noise(q * 0.21), noise(q * 0.21 + 5.7)) - 0.5) * 3.0; // swirl
	vec2 qs = vec2(dot(q, dn) / stretch, dot(q, sd));
	float fbm = noise(qs * 0.55) * 0.5 + noise(qs * 1.6 + t * 0.1) * 0.3 + mix(0.5, noise(qs * 4.4 - t * 0.2), near) * 0.2;
	float cover = smoothstep(1.0 - amount * 1.5, 1.0 - amount * 1.5 + 0.3, fbm);
	// Below breaker strength, foam comes in streaks and fragments along the drift, not big continuous masses.
	float pieces = smoothstep(0.32, 0.6, noise(vec2(qs.x * 0.22, qs.y * 1.1) + vec2(9.1, 2.7)) + 0.25 * amount);
	cover *= mix(pieces, 1.0, max(smoothstep(0.75, 1.0, amount), smoothstep(0.7, 1.0, sea_state) * 0.6)); // storms: denser
	// Lace: threads along the edges of bubble cells at two scales; fresh foam has wide threads that fill in.
	float thread = mix(0.08, 0.3, amount);
	float e1 = cells(qs * 1.5 + vec2(3.1, 0.0), t * 0.25);
	float e2 = cells(qs * 4.0 + vec2(0.0, 7.7), t * 0.45);
	float lace = max(1.0 - smoothstep(0.0, thread, e1), (1.0 - smoothstep(0.0, thread * 0.8, e2)) * near);
	// Body: strong foam is mostly solid, holed where the bubble cells open up; weak foam is only lace.
	float storm_f = smoothstep(0.7, 1.0, sea_state); // storms: solid white water again
	float body_f = smoothstep(mix(0.3, 0.12, storm_f), mix(0.8, 0.55, storm_f), amount) * smoothstep(0.25, 0.6, fbm + 0.3 * (1.0 - smoothstep(0.0, 0.45, e1)) - 0.1);
	float speck = smoothstep(0.78, 0.95, noise(qs * 13.0 + t)) * near;
	float top = cover * max(max(body_f, lace * (0.55 + 0.45 * fbm)), speck * 0.6);
	top = smoothstep(0.0, 0.7, top);
	top = mix(top, cover * mix(0.5, 0.9, amount), far);
	float under = smoothstep(1.0 - amount * 1.6, 1.0 - amount * 1.6 + 0.55, fbm) * min(1.0, amount * 1.5);
	float shade = 0.74 + 0.26 * mix(fbm * 0.5 + lace * 0.5, 0.75, far);
	return vec3(clamp(top, 0.0, 1.0) * 0.95, under, shade);
}
#ifdef EXP3A
// Open-sea foam pattern of experimental 3A (round 24; Tom: the smeared, combed patches "look weird"): broken patches
// whose inside is made of bubble-cell threads (lace), a solid core only in fresh foam. No soft far-away cover (it
// smudged into paint strokes from 35 m on) and no wide stretching: the lace gets coarser with the pixel footprint
// instead, so it stays texture, not a smear. Returns (coverage 0..1, structure 0..1: bubble threads bright, cell
// centres dim — the inside of the foam is never one flat white, Tom: "like a blob of paint").
vec2 foam3a(vec2 q, vec2 dn, float t, float amount, float fresh, float footprint) {
	if (amount <= 0.001) return vec2(0.0);
	vec2 sd = vec2(-dn.y, dn.x);
	q += (vec2(noise(q * 0.13), noise(q * 0.13 + 5.7)) - 0.5) * 5.0; // warp: no straight edges
	vec2 qs = vec2(dot(q, dn) / 1.6, dot(q, sd));                     // a little longer along the drift
	float fbm = noise(qs * 0.32) * 0.5 + noise(qs * 0.85 + 3.1) * 0.3 + noise(qs * 2.2 - 1.7) * 0.2;
	float edge = 1.0 - amount * 1.15;
	float mask = smoothstep(edge, edge + 0.1, fbm);                   // where there is foam at all: ragged patches
	float sc = 1.0 / max(1.0, footprint * 2.0);                       // coarser lace far away (no aliasing, no smear)
	// Cells on warped coordinates of varying size, threads broken into fragments and of varying width, dense parts
	// filled with bubble clumps: an even cell net read as fishing nets lying on the water (round 24 renders).
	vec2 cw = vec2(noise(qs * 0.5 + 2.3), noise(qs * 0.5 + 8.1)) - 0.5;
	float e1 = cells((qs + cw * 2.5) * 1.1 * sc + vec2(3.1, 0.0), t * 0.2);
	float e2 = cells((qs - cw * 1.5) * 2.9 * sc + vec2(0.0, 7.7), t * 0.35);
	float vary = 0.55 + 0.9 * noise(qs * 0.9 + 4.4);
	float thread = mix(0.09, 0.32, fresh) * (1.0 + 0.6 * (1.0 - sc)) * vary;
	float broken = smoothstep(0.28, 0.55, noise(qs * 1.6 + 11.7)); // gaps in the threads
	float lace = max(1.0 - smoothstep(0.0, thread, e1), (1.0 - smoothstep(0.0, thread * 0.8, e2)) * 0.75) * mix(0.25, 1.0, broken);
	float clump = smoothstep(edge + 0.12, edge + 0.32, fbm) * smoothstep(0.45, 0.75, noise(qs * 1.3 + 6.6)) * 0.85;
	lace = max(lace, clump * (0.6 + 0.4 * smoothstep(0.0, 0.3, e1)));
	float core = smoothstep(0.35, 0.85, fresh) * smoothstep(edge + 0.06, edge + 0.3, fbm); // fresh: solid inside
	// Structure: the threads, plus the bubble cells showing faintly through even a solid core, plus a slow blotchy
	// density variation (thick and thin foam).
	float cellsc = smoothstep(0.0, 0.45, e1);                         // 0 on threads, 1 in cell centres
	float structure = clamp(lace * 0.6 + (1.0 - cellsc) * 0.25 + noise(qs * 0.6 + 9.3) * 0.35, 0.0, 1.0);
	return vec2(mask * max(core, lace), structure);
}
#endif
// Foam drifting with one even drift (the wind over open water): carried and pulled into streaks along it.
vec3 foam_layers(vec2 p, vec2 drift, float t, float amount, float dist) {
	float speed = length(drift);
	vec2 dn = speed > 1e-3 ? drift / speed : vec2(1.0, 0.0);
	return foam_at(p + drift * t, dn, 1.3 + 1.4 * smoothstep(0.1, 1.5, speed), t, amount, dist);
}
// Foam carried by a current that varies from place to place (rivers, plumes; round 20): two copies scrolled over a short
// cycle and cross-faded (no shear, see flow_noise), unrotated; the streaks come from smearing taps up the local current.
vec3 foam_flow(vec2 p, vec2 drift, float t, float amount, float dist) {
	if (amount <= 0.001) return vec3(0.0, 0.0, 1.0);
	const float cycle = 7.0;
	float p0 = fract(t / cycle), p1 = fract(t / cycle + 0.5);
	float w0 = 1.0 - abs(2.0 * p0 - 1.0);
	vec3 a = foam_at(p + drift * p0 * cycle, vec2(1.0, 0.0), 1.0, t, amount, dist);
	vec3 b = foam_at(p + drift * p1 * cycle + vec2(31.7, 12.3), vec2(1.0, 0.0), 1.0, t, amount, dist);
	vec3 m = mix(b, a, w0);
	// Two half-faded masks average to a grey wash: restore the contrast at mid-fade.
	m.x = mix(m.x, clamp((m.x - 0.2) * inversesqrt(w0 * w0 + (1.0 - w0) * (1.0 - w0)) + 0.2, 0.0, 1.0), 0.5);
	return m;
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
	// From the rest level: the displaced surface fed the breaker's own height back into its phase, and the coarse
	// mesh's straight edges showed as a zigzag foam line.
	float bed_depth = max(0.0, rest_level - bed0.y);
	// Close range only: further out the bed behind is a coarse, faceted terrain tile, and the shore-wave phase is so
	// sensitive to depth that every facet showed as a blocky patch.
	float use_px = smoothstep(18.0, 10.0, bed_depth) * (1.0 - smoothstep(25.0, 55.0, dist)) * step(length(behind0), 3000.0);
	float px_depth = mix(vertex_depth, bed_depth, use_px);

	float graze = max(abs(normalize(world_position - CAMERA_POSITION_WORLD).y), 0.02);
	float footprint = dist * 0.0012 / graze; // ~1 mrad per pixel, stretched by the grazing angle
	// Per-pixel wave normal from the full wave sum (smooth at every distance).
	vec3 n = vec3(0.0, 1.0, 0.0); float crest = 0.0; vec3 jac = vec3(0.0);
	float breaking = 0.0, since = 1.0, shore_k = 1.0;
	vec3 woff = vec3(0.0); // (3A: the open-water displacement here: foam patterns are drawn where the water is shown)
	if (sea + lake - pond > 0.5) {
		// Waves far shorter than a pixel only add sparkle: fade them with distance.
		// Nyquist on the pixel footprint along the view, not just distance: low over the water a pixel spans metres,
		// and shorter waves aliased into rings and bands of fine lines around the camera.
		float min_lambda = max(dist / 300.0, footprint * 2.5);
		// Interpolated headings shrink between vertices that differ: back to unit length (the weights are powers of the
		// cosine, so a shorter vector would fade every wave there).
		vec4 heads = vec4(normalize(wave_heads.xy + vec2(1e-5)), normalize(wave_heads.zw + vec2(1e-5)));
		woff = gerstner(p, vertex_depth, sea, lake, fetch, sea > 0.5 ? exposure : 1.0, min_lambda, ocean_time, heads, wave_norms, wave_coast, n, crest, jac);
		if (sea > 0.5) shore_wave(p, px_depth, bed_grad, exposure, min_lambda, ocean_time, n, breaking, since, shore_k);
	}
#ifdef FFT_OCEAN
	else if (river > 0.5 && fetch > 40.0) {
		// Wide rivers get the simulated lake chop too, by their width (round 24, Tom's dump 172621: in a storm a broad
		// river lay dark and glassy between choppy, whitecapped water — the wind sea of lakes and the sea stopped at
		// the river's edge). Normals and foam only: the river surface itself stays where the current puts it.
		float wide = smoothstep(40.0, 250.0, fetch);
		float min_lambda = max(dist / 300.0, footprint * 2.5);
		vec3 rn = vec3(0.0, 1.0, 0.0); float rc = 0.0; vec3 rj = vec3(0.0);
		gerstner(p, vertex_depth, 0.0, wide, fetch, 1.0, min_lambda, ocean_time, vec4(1.0, 0.0, 1.0, 0.0), vec3(1.0), 0.0, rn, rc, rj);
		n += vec3(rn.x, 0.0, rn.z);
		jac += rj * vec3(1.0, 1.0, 0.7);
	}
#endif
	crest /= max(wave2_hs * 0.5, 0.01); // ~1 at a typical big crest
#ifdef EXP3A
	float fold = 1.0 - (1.0 - jac.x) * (1.0 - jac.y);
	float field_foam = jac.z; // the simulated foam (1 just born … fading toward 0), drifting with the wind
#else
	float fold = 1.0 - ((1.0 - jac.x) * (1.0 - jac.y) - jac.z * jac.z);
#endif
	vec2 slope = -n.xz / max(n.y, 0.2);

	// Wakes (Noor: directional, not radial): every disturbance spreads as a ring of ripples while the current carries it
	// downstream and it fades; a source moving relative to the water lays its rings along its path (a V-shaped wake),
	// standing in a current the rings drift downstream (a downstream V).
	float wake_foam = 0.0;
	if (length(p - wake_centre) < 45.0) {
		for (int i = 0; i < 24; i++) {
			vec4 w = wake_points[i];
			if (w.w <= 0.0) continue;
			float age = ocean_time - w.z;
			if (age < 0.0 || age > 4.5) continue;
			vec2 d = p - (w.xy + wake_flow[i].zw * age);
			float r = length(d), x = r - (0.25 + 0.85 * age);
			float env = exp(-x * x / (0.12 + 0.3 * age)) * exp(-age * 0.85) * w.w;
			if (env < 0.002) continue;
			float k = 10.0 / (1.0 + age * 0.7);
			slope += d / max(r, 0.05) * env * k * cos(k * x) * 0.03;
			wake_foam = max(wake_foam, w.w * exp(-age * 2.2) * (1.0 - smoothstep(0.0, 0.45 + age * 0.9, r)));
		}
	}
	vec2 big_slope = slope; // the waves without the small ripples (reflection strength, below)
	// Small-scale detail: wind ripples on seas and lakes (stronger with wind and storms), flow ripples on rivers.
	float bed_slope = length(bed_grad);
	float rocky = sea * smoothstep(0.12, 0.35, bed_slope) * (1.0 - smoothstep(2.0, 9.0, vertex_depth)) * (0.4 + sea_state) * exposure;
	float standing = 0.0, bank_foam = 0.0, obstacle_foam = 0.0; // river: standing-wave crests and bank turbulence (foam, below)
	float plume_flow = 0.0, mouth_meet = 0.0, dbg_obstacle = 0.0; // river mouths: the outgoing current's share, waves meeting it
	if (river > 0.5) {
		// Rivers are water with a destination (Noor; docs/RIVERS_PLAN.md): every layer follows the current.
		float fs = length(current);
		vec2 fdir = fs > 0.01 ? current / fs : wdir;
		vec2 fside = vec2(-fdir.y, fdir.x);
		// Flow layers at different speeds and scales (never one scrolling texture): the fine pair carried at the
		// current's speed, a broad slow one at about half of it.
		slope += flow_slope(p, current, TIME, dist) * (1.0 + 0.6 * sea_state);
		slope += flow_slope(p * 0.4 + 17.0, current * 0.4 * 0.55, TIME, dist * 0.4) * 0.4 * 1.6;
		// Standing waves in fast, shallow water (riffles, rapids): crests across the flow that stay where they are
		// while the water runs through them, wavelength from the speed (λ = 2π v²/g), in patches.
		// (not in the last film of water at the bank: that drew a white strip along the water's edge)
		float riffle = smoothstep(0.8, 2.0, fs) * (1.0 - smoothstep(0.5, 2.2, px_depth)) * smoothstep(0.08, 0.35, px_depth)
			* (1.0 - smoothstep(0.6, 2.0, footprint));
		if (riffle > 0.0) {
			// In local frames anchored to a 20 m grid and blended between its corners: the crests' phase must not be
			// the flow direction times the (large) scene position — where a river bends, that swung the crests into
			// big frozen arcs broken at triangle edges (Tom, build 25: "circles on the rivers").
			float lam = clamp(PI2 * fs * fs / G, 0.8, 4.0), kk = PI2 / lam;
			const float CELL = 20.0;
			vec2 g = (p + world_origin) / CELL, g0 = floor(g), gf = g - g0;
			gf = gf * gf * (3.0 - 2.0 * gf);
			float wave = 0.0, crest_s = 0.0;
			for (int ci = 0; ci < 4; ci++) {
				vec2 corner = g0 + vec2(float(ci & 1), float(ci >> 1));
				float wgt = (ci & 1) == 1 ? gf.x : 1.0 - gf.x;
				wgt *= (ci >> 1) == 1 ? gf.y : 1.0 - gf.y;
				vec2 local = (g - corner) * CELL; // (small numbers: no float precision lost)
				float along = dot(local, fdir), across = dot(local, fside);
				float th = kk * along + PI2 * hash(corner) + 2.0 * noise(vec2(along * 0.12, across * 0.3) + corner * 7.1)
					+ 0.25 * sin(TIME * 1.7 + across * 0.5);
				wave += wgt * cos(th);
				crest_s += wgt * smoothstep(0.55, 1.0, sin(th));
			}
			float patches = smoothstep(0.35, 0.7, noise(p * 0.08 + 5.3));
			slope += fdir * (0.11 * riffle * patches * wave);
			standing = riffle * patches * crest_s * 0.7;
		}
		// Bank turbulence: shallow margins of moving water break into small irregular ripples, now and then foam.
		// (depth per pixel: per vertex, the margin followed the triangle edges as straight white strips — build 25)
		float margin = (1.0 - smoothstep(0.1, 0.6, px_depth)) * smoothstep(0.15, 0.6, length(current))
			* (1.0 - smoothstep(0.3, 1.0, footprint));
		slope += flow_gnoise(p * 2.4 + 9.0, current * 0.4, TIME, 2.0) * 2.4 * 0.025 * margin;
		bank_foam = margin * smoothstep(0.55, 0.8, flow_noise(p * 0.9, current * 0.3, TIME, 3.0)) * 0.55;
		// Rocks standing in the current (TileBuilder.ObstacleWakes: CUSTOM0 = downstream, across in rock radii, weight).
		float oweight = exposure;
		if (oweight > 0.01 && fs > 0.15) {
			float a = bed_grad.x, c = bed_grad.y, e = 0.1;
			float strength = smoothstep(0.15, 1.2, fs) * oweight;
			// TIME * fs sheared across the wake where the speed varies (rings that never move): two short cycles, cross-faded.
			const float ocycle = 4.0;
			float op0 = fract(TIME / ocycle), op1 = fract(TIME / ocycle + 0.5), ow0 = 1.0 - abs(2.0 * op0 - 1.0);
			float t0 = op0 * ocycle, t1 = op1 * ocycle + 2.1;
			float ha = mix((obstacle_h(a + e, c, fs, t1) - obstacle_h(a - e, c, fs, t1)), (obstacle_h(a + e, c, fs, t0) - obstacle_h(a - e, c, fs, t0)), ow0) / (2.0 * e);
			float hc = mix((obstacle_h(a, c + e, fs, t1) - obstacle_h(a, c - e, fs, t1)), (obstacle_h(a, c + e, fs, t0) - obstacle_h(a, c - e, fs, t0)), ow0) / (2.0 * e);
			slope += (fdir * ha + fside * hc) * strength;
			float bulge = exp(-((a + 1.3) * (a + 1.3) * 1.6 + c * c * 1.1));
			float spread = mix(0.65, 0.32, smoothstep(0.5, 2.5, fs));
			float arm = smoothstep(-0.2, 0.3, a) * exp(-sq(abs(c) - (1.0 + max(a, 0.0) * spread)) * 3.0) * exp(-max(a, 0.0) * 0.15);
			float core = smoothstep(0.4, 1.2, a) * exp(-c * c / (0.5 + max(a, 0.0) * 0.12)) * exp(-max(a, 0.0) * 0.12);
			dbg_obstacle = strength * (bulge + arm + core);
			// Foam breaks up and drifts downstream (no standing white arcs).
			float drift = mix(noise(vec2(a * 0.9 - t1 * fs * 0.7 + 5.1, c * 1.7)), noise(vec2(a * 0.9 - t0 * fs * 0.7, c * 1.7)), ow0);
			obstacle_foam = strength * (bulge * 0.7 * smoothstep(0.6, 2.0, fs) + arm * 0.35 * smoothstep(0.8, 2.0, fs) * smoothstep(0.45, 0.8, drift)
				+ core * 0.6 * smoothstep(0.45, 0.8, drift));
		}
		// Wide rivers: wind waves too (fetch = the river's width), steeper where the wind blows against the current.
		float wide = smoothstep(25.0, 150.0, fetch);
		if (wide > 0.0) {
			float against = max(0.0, -dot(wdir, fdir)) * smoothstep(0.3, 1.5, fs);
			slope += ripple_slope(p, TIME, wdir, (0.15 + 0.2 * wind_speed + 0.6 * sea_state) * wide * (1.0 + 0.5 * against), dist, footprint);
		}
	}
	else {
		// Small lakes stay calm (little fetch for the wind to raise waves); big ones approach the sea.
		float lake_calm = mix(1.0, clamp(0.3 + fetch / 4000.0, 0.3, 0.75), lake);
		float strength = (0.2 + 0.22 * wind_speed + 0.8 * sea_state) * lake_calm * mix(1.0, 0.25, pond) + rocky * 1.5;
		slope += ripple_slope(p, TIME, wdir, strength, dist, footprint);
		// Wind sea: mid-scale choppy waves running with the wind on the swell, much stronger in storms (sharpened
		// by folding the noise), fading with distance into the swell's own lighting.
		float chop = (0.03 + 0.35 * sea_state * sea_state + 0.05 * wind_speed) * (sea + lake * 0.35 - pond * 0.3) / (1.0 + dist / 250.0)
			* (1.0 - 0.7 * smoothstep(300.0, 1500.0, dist)) // far away it only shimmered
			* (1.0 - smoothstep(1.5, 5.0, footprint));      // and aliased into bands at grazing angles
		vec2 side = vec2(-wdir.y, wdir.x);
		vec3 c1 = gnoised(p * 0.07 + wdir * TIME * 0.25);
		vec3 c2 = gnoised(p * 0.17 - (wdir * 0.6 + side * 0.4) * TIME * 0.4 + 3.7);
#ifdef EXP3A
		chop *= 1.0 - 0.6 * sea; // (the simulated waves carry the sea's detail; the drawn chop only blurred it)
#endif
		slope += (c1.xy * 0.07 + c2.xy * 0.17 * 0.6) / 0.15 * chop;
		// River mouths (RIVERS_PLAN step 3): the river's current fans out into the lake or sea and weakens; its
		// flow-carried ripples blend in by the current's strength, and where waves run against the outgoing current
		// (the wind sea, or the shore waves rolling in) the water gets confused and choppy and breaks up — messy, as a
		// real mouth: dramatic in rough seas, gentle on a calm lake.
		float fm = length(current);
		if (fm > 0.03) {
			plume_flow = smoothstep(0.05, 0.6, fm);
			vec2 cdir = current / fm;
			slope += flow_slope(p, current, TIME, dist) * plume_flow * 0.8;
			vec2 shore_dir = length(bed_grad) > 0.003 ? normalize(bed_grad) : wave_heads.xy;
			mouth_meet = plume_flow * max(max(0.0, -dot(wave_heads.xy, cdir)) * (0.3 + 0.7 * sea_state),
				max(0.0, -dot(shore_dir, cdir)) * sea * smoothstep(19.0, 6.0, vertex_depth));
			slope += flow_gnoise(p * 0.6 + 2.0, current * 0.2, TIME, 4.0) * 0.6 * 0.09 * mouth_meet * (0.5 + sea_state)
				* (1.0 - smoothstep(1.0, 4.0, footprint));
		}
	}
	vec3 n_world = normalize(vec3(-slope.x, 1.0, -slope.y));
	vec3 n_view = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);
	NORMAL = n_view;
	vec3 to_eye = normalize(-VERTEX);
	// Reflection strength from a softened normal (half the small ripples): ripple facets turned toward the horizon
	// mirrored the bright sky as hard white streaks all over windy lakes. Glints and refraction keep the full normal.
#ifdef EXP3A
	const float RIPPLE_REFLECT = 0.7; // (3A: small facets catch more of the sky — less matte)
#else
	const float RIPPLE_REFLECT = 0.45;
#endif
	vec3 n_soft = normalize((VIEW_MATRIX * vec4(normalize(vec3(-mix(big_slope, slope, RIPPLE_REFLECT).x, 1.0, -mix(big_slope, slope, RIPPLE_REFLECT).y)), 0.0)).xyz);
	float fresnel = 0.02 + 0.98 * pow(1.0 - clamp(dot(n_soft, to_eye), 0.0, 1.0), 5.0);

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
#ifdef EXP3A
	vec3 transmittance = exp(-absorption * path * mix(1.0, 2.2, river) * mix(1.0, 0.8, sea)); // (clearer sea shallows)
#else
	vec3 transmittance = exp(-absorption * path * mix(1.0, 2.2, river));
#endif
	vec3 scatter = mix(sea * sea_scatter + lake * lake_scatter + river * river_scatter, river_scatter * vec3(1.15, 1.1, 0.9), plume * 0.85);
	transmittance = mix(transmittance, exp(-absorption * path * 2.2), plume * 0.6);
	// Sea of Thieves palette: deep saturated blue-teal open sea, greyer and darker in storms.
	scatter = mix(scatter, vec3(0.02, 0.15, 0.27), sea * 0.6);
	scatter = mix(scatter, vec3(0.05, 0.09, 0.10), sea * smoothstep(0.55, 1.0, sea_state) * 0.55);
	// Masses: troughs darker and denser, crests lighter (the contrast that makes big waves read as moving water).
	scatter *= mix(1.0, mix(0.55, 1.45, smoothstep(-1.0, 1.0, crest)), sea);
	scatter = mix(scatter, biome_tint.rgb * 0.35, biome_tint.a);
	transmittance = mix(transmittance, transmittance * transmittance * biome_tint.rgb, biome_tint.a * 0.8);
	// Stirred-up shallows (breakers, rapids) are cloudier.
	float stirred = clamp(breaking * 0.7 + rocky * 0.5 + river * smoothstep(1.5, 3.5, length(current)) * 0.4, 0.0, 0.8);
	transmittance *= 1.0 - stirred * 0.5;
	float murk = 1.0 - dot(transmittance, vec3(0.333));

	// Screen-space reflection, sky as fallback (rougher, flatter sky in storms).
	vec3 ray = normalize(reflect(VERTEX, n_view));
	vec4 hit = vec4(0.0);
	// (round 25, Tom: mountains across an alpine lake were not reflected — the march reached only ~280 m (high) or
	// ~180 m (medium) and fell back to the sky; now ~8 km in 48 steps or ~2.4 km in 24, with a short refinement on a hit)
	int steps = ssr_steps <= 16 ? 24 : 48;
	float step_length = ssr_steps <= 16 ? 0.4 : 0.25;
	float growth = ssr_steps <= 16 ? 1.38 : 1.2;
	vec3 march = VERTEX + ray * 0.1;
	for (int i = 0; i < steps && ssr_steps > 0; i++) {
		vec3 before = march;
		march += ray * step_length;
		step_length *= growth;
		vec4 clip = PROJECTION_MATRIX * vec4(march, 1.0);
		if (clip.w <= 0.0) break;
		vec2 uv = clip.xy / clip.w * 0.5 + 0.5;
		if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) break;
		float scene_z = view_from_depth(uv, texture(depth_tex, uv).r, INV_PROJECTION_MATRIX).z;
		float gap = scene_z - march.z;
		if (gap > 0.0 && gap < step_length * 2.5 + 0.3) {
			// Refine between the last two points (long steps far out would otherwise smear the shoreline).
			vec3 a = before, b = march;
			for (int r = 0; r < 4; r++) {
				vec3 m = 0.5 * (a + b);
				vec4 mc = PROJECTION_MATRIX * vec4(m, 1.0);
				vec2 muv = clamp(mc.xy / mc.w * 0.5 + 0.5, 0.0, 1.0);
				if (view_from_depth(muv, texture(depth_tex, muv).r, INV_PROJECTION_MATRIX).z > m.z) b = m; else a = m;
			}
			vec4 bc = PROJECTION_MATRIX * vec4(b, 1.0);
			uv = clamp(bc.xy / bc.w * 0.5 + 0.5, 0.0, 1.0);
			vec2 edge = smoothstep(vec2(0.0), vec2(0.08), uv) * smoothstep(vec2(0.0), vec2(0.08), 1.0 - uv);
			// Fade only the last few steps (the old linear fade left distant mountains at a fifth of their strength).
			hit = vec4(textureLod(screen_tex, uv, 0.0).rgb, edge.x * edge.y * (1.0 - smoothstep(0.8, 1.0, float(i) / float(steps))));
			break;
		}
	}

	// ---- foam amount from every source, revealed through the drifting pattern
	vec2 drift = river > 0.5 ? current * 0.9 : mix(wdir * (0.15 + 0.2 * wind_speed), current * 0.9, plume_flow);
	// (rivers: only the last few cm; over a shallow bank shelf a 14 cm band was a wide white strip along the terrain's
	// triangle edges — Tom's dumps, round 20)
	float contact = 1.0 - smoothstep(0.0, mix(0.04, 0.16, sea) + 0.02 * river, contact_depth);
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
		// Ragged and broken: the generous foam reveal would otherwise fill the band solid and show its smooth outline.
		float tumble = 0.25 + 0.75 * noise(p * 0.3 + vec2(TIME * 0.4, 0.0)) + 0.4 * (noise(p * 1.1 - TIME * 0.6) - 0.5);
		float band = exp(-(wrap / bw) * (wrap / bw)) * tumble;
		// Trailing foam thins into lace (broken by noise) instead of a solid sheet.
		float trail = exp(-since * 7.0) * smoothstep(0.0, 0.06, since) * smoothstep(0.35, 0.75, noise(p * 0.3 + vec2(0.0, TIME * 0.1)));
		amount = max(amount, breaking * along * smoothstep(0.15, 0.5, px_depth) * (band * 0.8 + trail * 0.7));
#ifndef EXP3A
		// Open-water white water follows the weather (Noor: calm → breezy → rough → storm): none when calm, a few
		// small short-lived caps in a breeze, many in rough seas, dense only in storms. Caps form and fade (life) and
		// break into fragments along the wind, never one continuous sheet.
		vec2 side = vec2(-wdir.y, wdir.x);
		vec2 wq = vec2(dot(p, wdir), dot(p, side));
		float life = smoothstep(0.42 - 0.3 * sea_state, 0.7 - 0.2 * sea_state,
			noise(p * 0.045 + vec2(TIME * 0.07, -TIME * 0.05)) * 0.7 + noise(wq * vec2(0.06, 0.2) - vec2(TIME * 0.25, 0.0)) * 0.3);
		float frag = smoothstep(0.3, 0.62, noise(wq * vec2(0.12, 0.5) + vec2(-TIME * 0.3, 3.3)) + 0.3 * sea_state);
		float whitecaps = life * mix(frag, 1.0, smoothstep(0.6, 1.0, sea_state) * 0.75);
		// Folding crests, the foam spilling down the back of the wave as the crest moves on.
		float cap = smoothstep(0.22 - 0.16 * sea_state, 0.6, fold) * smoothstep(0.25, 0.6, sea_state) * (0.85 + 0.3 * smoothstep(0.7, 1.0, sea_state));
		amount = max(amount, cap * whitecaps);
		amount = max(amount, smoothstep(0.5, 1.0, crest) * smoothstep(0.5, 0.9, sea_state) * 0.5 * whitecaps);
		// Breaking tops: high AND steep crests break into white water (with many mixed waves the surface rarely folds,
		// so this is the main whitecap source); stronger inside wave groups.
		float steep = length(-n.xz / max(n.y, 0.2));
		float tops = smoothstep(0.38, 0.95, crest) * smoothstep(0.2 - 0.08 * sea_state, 0.5, steep) * smoothstep(0.28, 0.65, sea_state);
		amount = max(amount, tops * (0.55 + 0.55 * sea_state + 0.35 * smoothstep(0.7, 1.0, sea_state)) * whitecaps);
		// Storm foam fields: old foam drifting in broken patches (storms only).
		float fields = smoothstep(0.58, 0.85, noise(p * 0.018 + wdir * TIME * 0.02) * 0.6 + noise(p * 0.07 - wdir * TIME * 0.05) * 0.4);
		amount = max(amount, fields * frag * smoothstep(0.75, 1.0, sea_state) * 0.45);
		// Wind streaks: narrow foam lanes along the wind in strong winds and storms, thickest on the wave tops.
		vec2 sq = vec2(wq.x * 0.025, wq.y * 0.35) + vec2(-TIME * 0.12, 0.0);
		float lanes = noise(sq) * 0.65 + noise(sq * 2.3 + 4.1) * 0.35;
		float streaks = smoothstep(0.58, 0.8, lanes) * smoothstep(0.55, 0.95, sea_state) * (0.5 + 0.5 * smoothstep(-0.3, 0.8, crest));
		amount = max(amount, streaks * 0.55);
#endif
		// Rocks and steep coasts churn.
		amount = max(amount, rocky * (0.4 + 0.5 * noise(p * 0.2 + TIME * 0.3)));
	} else if (river > 0.5) {
		float fs = length(current);
		// Whitewater where the current is fast, more so over shallow beds (rocks, riffles).
		float rapids = smoothstep(1.0, 2.8, fs) * (0.6 + 0.4 * (1.0 - smoothstep(0.3, 1.5, vertex_depth)));
		// Whitewater comes in pulses and patches, not a uniform sheet.
		float pulse = 0.65 + 0.35 * flow_noise(p * 0.08, current * 0.05, TIME, 12.0);
		// In patches drifting with the current (a whole fast river turned into one white sheet).
		float patches = smoothstep(0.3, 0.7, flow_noise(p * 0.22, current * 0.35, TIME, 5.0) * 0.7 + noise(p * 0.6 + 4.2) * 0.3 + 0.15 * smoothstep(2.0, 3.5, fs));
		amount = max(amount, rapids * pulse * patches * 0.85);
		amount = max(amount, smoothstep(0.6, 2.2, fs) * 0.25); // streaks in moving water
		amount = max(amount, standing * 0.7);  // white tops on the standing waves
		amount = max(amount, bank_foam * 0.6);    // foam where the margins churn
		amount = max(amount, obstacle_foam);     // around and behind rocks in the current
		amount = max(amount, smoothstep(0.45, 0.9, fold) * smoothstep(0.5, 0.9, sea_state) * 0.5); // wind-sea caps on wide rivers (FFT styles)
	} else {
		amount = max(amount, smoothstep(0.45, 0.9, fold) * smoothstep(0.5, 0.9, sea_state) * 0.6 * lake);
	}
	amount = max(amount, wake_foam * 0.6); // splashes and churned water where something moves through
	// Where waves meet a river's outgoing current: broken, foamy patches carried out with the plume.
	amount = max(amount, mouth_meet * (0.12 + 0.6 * sea_state) * smoothstep(0.4, 0.75, flow_noise(p * 0.15, current * 0.15, TIME, 6.0)));
	float amt = clamp(amount, 0.0, 1.0);
	bool flowing = river > 0.5 || plume_flow > 0.01;
	vec3 layers = flowing ? foam_flow(p, drift, TIME, amt, dist) : foam_layers(p, drift, TIME, amt, dist);
	float foam = layers.x, under_foam = layers.y, foam_shade = layers.z;
	float foam_opacity = 1.0;
#ifdef EXP3A
	if (sea > 0.5) {
		// Open-sea foam from the simulated surface (Noor's life cycle: crest forms → cap → breaks → fresh foam spreads →
		// fragments → stretched streaks → fades), drawn where the water is shown and carried by the wind:
		//   the breaking lip — bright and nearly solid, on the folding crest itself;
		//   fresh foam — irregular patches the crest leaves behind;
		//   old foam — thin, fragmented streaks pulled out along the wind, translucent, fading.
		vec2 pq = p + woff.xz;
		float cap = smoothstep(0.32, 0.8, fold) * smoothstep(0.15, 0.5, sea_state) * smoothstep(0.25, 0.6, field_foam + 0.3);
		float fresh = smoothstep(0.35, 0.85, field_foam);
		float old = smoothstep(0.03, 0.4, field_foam) * (1.0 - fresh);
		vec2 fd = wdir * (0.25 + 0.25 * wind_speed);
		vec2 fq = pq + fd * TIME;
		vec2 lip2 = foam3a(fq, wdir, TIME, max(cap * 0.95, fresh * 0.8), max(cap, fresh), footprint);
		vec2 streak2 = foam3a(fq * 0.85 + vec2(37.1, 11.3), wdir, TIME, old * 0.72, 0.0, footprint);
		float lip = lip2.x;
		float top = max(lip, streak2.x * 0.85);
		float structure = lip2.x >= streak2.x * 0.85 ? lip2.y : streak2.y;
		float age_mix = clamp(max(cap, fresh) * 1.3, 0.0, 1.0);
		foam = max(foam, top);
		// Partly see-through: the water shows between the bubbles (fresh crest foam nearly opaque, older foam thin),
		// and thinner where the structure is thin.
		float see = mix(mix(0.55, 0.92, age_mix), 1.0, structure * 0.35);
		foam_opacity = mix(foam_opacity, see, step(layers.x, top));
		// Bubbles under the breaking crest only (old foam drew a milky smear around itself).
		under_foam = max(under_foam, lip * max(cap, fresh) * 0.6);
		// Shading inside the foam: brighter threads and thick foam, dimmer cell centres (definition, not flat white).
		foam_shade = mix(foam_shade, mix(0.7, 1.0, structure) * mix(0.9, 1.0, age_mix), step(layers.x, top));
	}
#endif
	if (river > 0.5 && length(current) > 0.15) {
		// Streaks along the current: the same foam smeared downstream (faded in with the speed: a hard switch drew a
		// straight foam edge along the triangles where the current crossed the threshold).
		vec2 fdir = normalize(current);
		float smear = smoothstep(0.15, 0.5, length(current));
		foam = mix(foam, (foam + foam_flow(p - fdir * 2.4, drift, TIME, amt, dist).x) * 0.5 * 1.25, smear);
	}
#ifdef EXP3A
	foam = foam * (1.0 / (1.0 + dist / 1500.0) * 0.4 + 0.6) * smoothstep(0.2, 0.9, edge_fade) // (whitecaps stay readable far out)
#else
	foam = foam * (1.0 / (1.0 + dist / 400.0) * 0.5 + 0.5) * smoothstep(0.2, 0.9, edge_fade)
#endif
		* smoothstep(0.01, 0.12, contact_depth); // the thinnest swash film carries no solid foam

	// Aerated crests in rough weather: milky streaks over the wave tops (softer than foam, not masked).
	float aerated = (sea + lake * 0.4) * smoothstep(0.15, 1.0, crest + 0.4 * (noise(p * 0.12 + wdir * TIME * 0.3) - 0.5))
		* smoothstep(0.4, 0.9, sea_state) * 0.65 * (0.55 + 0.45 * noise(p * 0.8 - TIME * 0.2)) / (1.0 + dist / 800.0);
#ifdef EXP3A
	aerated *= 0.6;
#endif
	under_foam = max(under_foam, aerated * 1.3); // aerated crests are milky water, not surface foam
	under_foam *= smoothstep(0.1, 0.9, edge_fade);

	vec3 transmitted = ground * transmittance * (1.0 - under_foam * 0.6); // milky water hides the bed
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(ray, 0.0)).xyz);
	vec3 sky = mix(sky_horizon, sky_zenith, smoothstep(0.0, 0.6, ray_world.y));
	sky = mix(sky, (sky_horizon + sky_zenith) * 0.5, sea_state * 0.5);
	// Rough seas scatter the reflection: duller and darker, so the water's own colour carries the storm.
	// Steep wave faces (breakers, storm crests) are rough, churned water: they reflect far less than a mirror, so the
	// water's own colour and the light through it carry them (a mirror-bright face drew a white band on breakers).
	float face_r = clamp(1.0 - n_world.y, 0.0, 0.5) / 0.5;
	// Clouds mirrored crisply on calm water, smeared out on rough water (a rough surface averages many directions).
	sky = mix(reflected_sky(ray_world, world_position.xz - CAMERA_POSITION_WORLD.xz, sky), sky, (sea + lake * 0.5) * smoothstep(0.2, 0.9, sea_state) * 0.7);
#ifdef EXP3A
	// (3A: rough water still mirrors much of the sky — the storm sea looked matte)
	vec3 reflected = mix(sky, hit.rgb, hit.a) * reflection_strength * mix(1.0, 0.75, sea * smoothstep(0.4, 1.0, sea_state))
		* mix(1.0, 0.65, face_r * sea) * mix(1.0, 0.9, sea);
#else
	vec3 reflected = mix(sky, hit.rgb, hit.a) * reflection_strength * mix(1.0, 0.5, sea * smoothstep(0.4, 1.0, sea_state))
		* mix(1.0, 0.45, face_r * sea) * mix(1.0, 0.8, sea);
#endif
	// Light through thin crests (seas, breakers): looking toward the sun, the upper part of waves glows.
	vec3 eye_world = normalize(CAMERA_POSITION_WORLD - world_position);
	vec3 sun_flat = normalize(vec3(sun_direction.x, 0.0, sun_direction.z) + 1e-4);
	float back_lit = pow(clamp(dot(-eye_world, sun_flat), 0.0, 1.0), 2.0) * clamp(sun_direction.y * 3.0, 0.0, 1.0);
	float thin = clamp(smoothstep(0.15, 1.1, crest) * (sea + lake * 0.3) * min(1.0, 0.35 + wave2_hs * 0.6)
		// Breakers glow just ahead of the crest: a soft, wide band that waxes and wanes along the crest (a narrow
		// uniform band drew straight neon lines along the beach).
		+ breaking * exp(-pow(min(1.0 - since, since + 0.2) / 0.24, 2.0)) * 0.45
		* smoothstep(0.15, 0.75, noise(p * 0.07 + vec2(TIME * 0.04, -TIME * 0.03))), 0.0, 1.0);
	// Light through the wave tops: strongest looking toward the sun, still clearly turquoise from the side; steep wave
	// faces (thin water) glow more.
	float face = clamp(1.0 - n_world.y, 0.0, 0.6) / 0.6;
	vec3 glow = sss_color * sun_color * (back_lit * 2.2 + 0.3 + face * 0.5) * thin * 0.8;
#ifdef FFT_OCEAN
	glow *= 0.45; // the FFT crests are broader and higher than the spectrum's (half the sea glowed neon)
#endif
	// Sun glints: a tight highlight that moves over the waves (the engine's specular adds the broad sheen).
	vec3 half_v = normalize(normalize(sun_direction) + eye_world);
	float glint = pow(clamp(dot(n_world, half_v), 0.0, 1.0), 1200.0) * clamp(sun_direction.y * 4.0, 0.0, 1.0) * (1.0 - sea_state * 0.45) * 1.6;
#ifdef EXP3A
	// Small facets catching the sun: a broader sheen under the tight glints (not glitter).
	glint = glint * (1.0 + 0.35 * sea_state) + pow(clamp(dot(n_world, half_v), 0.0, 1.0), 220.0) * clamp(sun_direction.y * 4.0, 0.0, 1.0) * 0.12;
#endif
	// Milky under-foam: pale turquoise-white in the water body, lit a little from inside.
	vec3 milky = vec3(0.55, 0.75, 0.74) * under_foam;
#ifdef EXP3A
	// Less diffuse "paint" in the water body (it is lit through, not on); foam lets some water through, thin foam a
	// little bluish (bubbles over water).
	float fo = foam * foam_opacity;
	vec3 foam_col = foam_color * foam_shade * mix(vec3(0.82, 0.93, 0.96), vec3(1.0), foam_opacity);
	ALBEDO = mix(scatter * murk * (1.0 - fresnel) * 0.75 + milky * 0.45 * (1.0 - fresnel), foam_col, fo);
	EMISSION = (transmitted * (1.0 - fresnel) + reflected * fresnel + glow * (1.0 - fresnel) + sun_color * glint * 3.0
		+ sss_color * sun_color * under_foam * 0.12) * (1.0 - fo);
	SPECULAR = 0.5 * (1.0 - fo);
	ROUGHNESS = mix(mix(0.02, 0.08, sea_state), 0.6, fo);
#else
	ALBEDO = mix(scatter * murk * (1.0 - fresnel) + milky * 0.45 * (1.0 - fresnel), foam_color * foam_shade, foam);
	EMISSION = (transmitted * (1.0 - fresnel) + reflected * fresnel + glow * (1.0 - fresnel) + sun_color * glint * 3.0
		+ sss_color * sun_color * under_foam * 0.12) * (1.0 - foam);
	SPECULAR = 0.3 * (1.0 - foam);
	ROUGHNESS = mix(mix(0.03, 0.12, sea_state), 0.6, foam);
#endif
	METALLIC = 0.0;
	// The swash film running up the beach thins out softly (a hard 4 cm ramp drew its triangle-edged outline).
	float film = sea > 0.5 ? mix(0.22, 0.04, smoothstep(0.3, 1.5, vertex_depth)) : 0.04;
	ALPHA = smoothstep(0.0, film, contact_depth) * smoothstep(0.1, 0.9, edge_fade);
	if (debug_foam == 1) { ALBEDO = vec3(amt, foam, dbg_obstacle); EMISSION = vec3(0.0); ALPHA = 1.0; }
}
