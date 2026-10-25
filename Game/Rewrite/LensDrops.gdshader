// Raindrops on the lens (D217, D218; Tom: "a spray, maybe optional, of raindrops on the camera lens, not too big", then
// "more realistic, dropping down the camera lens in a natural way", "refraction and the light dispersion, so I can see
// the basic colours", "leaving trails that disappear slowly"): a full-screen pass after everything else.
// * beads: small drops land at random, sit, and slowly evaporate (shrinking);
// * runners: now and then a bigger drop breaks loose and runs down the lens — in stick-slip jerks, wandering a little
//   sideways — wiping the beads off its path and leaving a trail of tiny beads and a wet smear that linger and fade
//   slowly (~7 s) after it has gone (also after it has left the screen);
// * each drop is a small lens: the picture from across its centre (flipped), a little blurred, lit on its upper edge and
//   darker at the lower rim; it disperses the light — red, green and blue bend by different amounts — with a faint
//   spectral fringe at the rim (warm on one side, cool on the other).
// How many: Atmosphere (the rain, cover, which way you look). Drawn only while drops are on.
shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform float amount = 0.0;  // 0 none … 1 heavy rain on the lens
uniform float clock = 0.0;   // s, runs while drops are on
uniform float size = 1.0;    // drop size scale
uniform float trail_fade = 7.0; // s until a runner's trail has dried

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float hash1(float n) { return fract(sin(n * 91.345 + 3.17) * 43758.5453); }

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

// A drop as a lens: q and centre in screen heights, r its radius, stretch > 1 longer vertically.
// Returns (offset to sample at xy, coverage, distance from the centre in radii).
vec4 lens(vec2 q, vec2 centre, float r, float stretch) {
	vec2 d = (q - centre) / vec2(1.0, stretch);
	float dist = length(d) / r;
	float body = 1.0 - smoothstep(0.82, 1.0, dist);
	if (body <= 0.0) return vec4(0.0, 0.0, 0.0, 9.0);
	// (spherical cap: the picture from across the centre, pulled harder toward the rim)
	vec2 n = d / r;
	return vec4(-n * r * (2.2 + 1.6 * dist * dist), body, dist);
}

vec4 pick(vec4 a, vec4 b) { return a.z >= b.z ? a : b; }

// A running drop (D219): a teardrop — round below, drawn out above into a tail that narrows into the trail.
vec4 tear(vec2 q, vec2 centre, float r) {
	vec2 d = q - centre;
	float up = clamp(-d.y / (r * 2.6), 0.0, 1.0); // 0 at the centre … 1 at the tail's end
	// above the centre the width shrinks smoothly (a neck), so the drop flows into the trail without a step
	float w = r * mix(1.0, 0.22, smoothstep(0.0, 1.0, up));
	float dy = d.y < 0.0 ? d.y / 2.6 : d.y;
	vec2 e = vec2(d.x * r / max(w, 1e-5), dy);
	float dist = length(e) / r;
	float body = 1.0 - smoothstep(0.8, 1.0, dist);
	if (body <= 0.0) return vec4(0.0, 0.0, 0.0, 9.0);
	vec2 n = vec2(d.x / max(w, 1e-5), dy / r);
	return vec4(-n * r * (1.5 + 1.1 * dist * dist), body, dist);
}

// Beads: cells of `cell` screen heights; a drop lands at its own moment, sits and evaporates.
vec4 beads(vec2 q, float cell, float seed, float rate) {
	vec2 c = floor(q / cell);
	vec2 f = q / cell - c;
	float life = 6.0 + 8.0 * hash(c + seed);
	float t = clock / life + hash(c + seed + 4.1) * 9.0;
	float cycle = floor(t), k = fract(t);
	if (hash(c + seed + cycle * 1.37) > rate) return vec4(0.0, 0.0, 0.0, 9.0);
	vec2 centre = vec2(0.2 + 0.6 * hash(c + seed + cycle + 2.3), 0.2 + 0.6 * hash(c + seed + cycle + 7.7)) * cell;
	float r = cell * (0.16 + 0.22 * hash(c + seed + cycle + 1.9)) * size * (1.0 - 0.7 * k) * step(0.02, k);
	return lens(f * cell, centre, max(r, 1e-4), 1.0);
}

// One runner of lane column `col`, run `cycle` (the current one or the one before, whose trail may still be drying).
// Returns the drop or its trail (lens) and, in `smear`, the wet trail's strength at q.
// (D220: a runner first sits as a bead somewhere in the upper part of the lens, growing as it gathers water, then breaks
// loose and runs down, speeding up — the first version crawled evenly from above the top over 4–12 s)
vec4 runner(vec2 q, float lane, float col, float cycle, float progress, float period, float seed, float rate, out float smear) {
	smear = 0.0;
	if (hash1(col * 3.7 + cycle * 1.9 + seed) > rate) return vec4(0.0, 0.0, 0.0, 9.0);
	float h = hash1(col * 7.1 + cycle + seed);
	float wait = 0.3 + 0.25 * hash1(col + cycle * 2.3 + seed); // the share of the cycle spent gathering
	float y0 = 0.05 + 0.45 * hash1(col * 5.3 + cycle + seed);  // where it gathers (screen heights from the top)
	float len = 1.25 - y0;                                      // to below the bottom
	float run = clamp((progress - wait) / (1.0 - wait), 0.0, 1.0);
	// accelerating, with small stick-slip pauses on the way
	float travel = pow(run, 1.3); // (gentler: at 1.7 most of the run happened in its last moments, gone in ~0.5 s)
	float y = y0 + len * (travel + 0.008 * sin(travel * 70.0 + h * 6.0) * step(0.001, run));
	float cx = (col + 0.5) * lane;
	float wander = lane * 0.26 * (sin(q.y * 9.0 + h * 6.3) * 0.6 + sin(q.y * 23.0 + h * 2.1) * 0.4);
	float wanderAt = lane * 0.26 * (sin(y * 9.0 + h * 6.3) * 0.6 + sin(y * 23.0 + h * 2.1) * 0.4);
	float r = lane * (0.15 + 0.1 * h) * size * mix(0.55, 1.0, smoothstep(0.0, wait, progress)) * step(0.02, progress);
	vec4 drop = run > 0.0 ? tear(q, vec2(cx + wanderAt, y), r) : lens(q, vec2(cx + wanderAt, y), max(r, 1e-4), 1.0);
	// how long ago the drop passed this height (s): the trail dries over trail_fade
	float tq = pow(clamp((q.y - y0) / len, 0.0, 1.0), 1.0 / 1.3);
	float pq = wait + tq * (1.0 - wait);
	float age = (progress - pq) * period;
	float behind = y - q.y;
	float fresh = smoothstep(-r * 0.5, r * 1.5, behind) * step(y0, q.y) * smoothstep(-0.02, 0.0, age) * (1.0 - smoothstep(0.0, trail_fade, age));
	float neck = 1.0 - smoothstep(r * 1.5, r * 3.5, behind);
	float tw = max(r, 1e-4) * mix(0.2, 0.36, neck) * (0.6 + 0.4 * fresh);
	float dx = q.x - (cx + wander);
	float onPath = exp(-dx * dx / (tw * tw));
	smear = fresh * onPath * 0.5;
	// (the trail a thin cylindrical lens: the picture from across it, flipped sideways, dispersed at its edges)
	// (stronger bend than at first: over grass the trail barely showed)
	vec4 line = vec4(-dx * 3.2, 0.0, fresh * onPath, abs(dx) / (tw * 1.6));
	vec4 trail = vec4(0.0, 0.0, 0.0, 9.0);
	float bcell = max(r, 1e-3) * 1.1;
	float bi = floor(q.y / bcell);
	if (fresh > 0.0 && neck < 1.0 && hash1(bi + col * 13.0 + cycle) < 0.45) {
		float br = r * (0.12 + 0.14 * hash1(bi * 3.1 + col + cycle)) * (0.35 + 0.65 * fresh) * (1.0 - neck);
		vec2 bc = vec2(cx + wander + (hash1(bi + 5.0 + cycle) - 0.5) * r * 0.3, (bi + 0.5) * bcell);
		trail = lens(q, bc, max(br, 1e-4), 1.0);
	}
	return pick(pick(drop, line), trail);
}

// Runner lanes `lane` screen heights wide; each lane runs a drop now and then. smear: the wet trails at q.
vec4 runners(vec2 q, float lane, float seed, float rate, out float smear) {
	float col = floor(q.x / lane);
	float period = 3.5 + 3.5 * hash1(col + seed); // (runs of ~1.5–4 s once loose)
	float t = clock / period + hash1(col + seed + 3.3) * 5.0;
	float cycle = floor(t), p = fract(t);
	float s1, s2;
	vec4 now = runner(q, lane, col, cycle, p, period, seed, rate, s1);
	vec4 before = runner(q, lane, col, cycle - 1.0, p + 1.0, period, seed, rate, s2);
	smear = max(s1, s2);
	return pick(now, before);
}

// A soft spectrum by angle (0..1): warm → green-gold → cool, faint.
vec3 spectrum(float a) {
	return clamp(vec3(abs(a * 6.0 - 3.0) - 1.0, 2.0 - abs(a * 6.0 - 2.0), 2.0 - abs(a * 6.0 - 4.0)), 0.0, 1.0);
}

void fragment() {
	if (amount <= 0.001) discard;
	vec2 uv = SCREEN_UV;
	float aspect = VIEWPORT_SIZE.x / VIEWPORT_SIZE.y;
	vec2 q = vec2(uv.x * aspect, uv.y); // screen heights, y down
	float w1, w2;
	vec4 run = pick(runners(q, 0.09, 1.7, amount * 0.55, w1), runners(q + vec2(0.045, 0.0), 0.13, 8.3, amount * 0.35, w2));
	float smear = max(w1, w2);
	float wiped = clamp(smear * 1.8, 0.0, 1.0); // (a runner's path is wiped of beads)
	vec4 bead = pick(beads(q, 0.035, 2.9, amount * 0.28 * (1.0 - wiped)), beads(q + vec2(0.017, 0.011), 0.022, 6.1, amount * 0.2 * (1.0 - wiped)));
	vec4 l = pick(run, bead);
	if (l.z <= 0.002 && smear <= 0.002) discard;
	vec3 here = textureLod(screen_tex, uv, 0.0).rgb;
	// the wet smear: a little blur and a touch of light
	vec3 smeared = mix(here, textureLod(screen_tex, uv, 1.5).rgb * 1.05, smear);
	vec2 off = l.xy / vec2(aspect, 1.0);
	// dispersion: each colour refracted by its own amount (red least, blue most), the split growing toward the rim
	vec2 ca = vec2(0.001), cb = vec2(0.999);
	float split = 0.05 + 0.1 * smoothstep(0.4, 1.0, l.w); // (D220: softer — the runners showed harsh red/green bands)
	vec3 behindDrop = vec3(
		textureLod(screen_tex, clamp(uv + off * (1.0 - split), ca, cb), 0.5).r,
		textureLod(screen_tex, clamp(uv + off, ca, cb), 0.5).g,
		textureLod(screen_tex, clamp(uv + off * (1.0 + split), ca, cb), 0.5).b);
	// lit on the upper edge (light from above), darker toward the lower rim; a faint spectral fringe at the rim
	vec2 nrm = normalize(-l.xy + vec2(1e-5));
	float rim = smoothstep(0.55, 0.92, l.w) * (1.0 - smoothstep(0.92, 1.0, l.w));
	// (edge light only toward the rim: at the centre the direction is undefined and drew a pinched cross)
	float edgeK = smoothstep(0.35, 0.9, l.w);
	float topEdge = smoothstep(0.4, 1.0, -nrm.y) * 0.08 * edgeK;
	float lowEdge = smoothstep(0.4, 1.0, nrm.y) * 0.18 * edgeK;
	vec3 fringe = spectrum(fract(atan(nrm.y, nrm.x) / 6.2832 + 0.15)) * rim * 0.045 * (0.6 + 0.4 * dot(here, vec3(0.33)));
	// (D220: a thin dark outline — light bent away at the rim — and a small bright highlight up-left: over a busy
	// forest the drops had melted into the picture)
	float outline = smoothstep(0.78, 0.96, l.w) * (1.0 - smoothstep(0.96, 1.0, l.w)) * 0.35;
	vec2 hp = -l.xy / max(length(l.xy), 1e-5) * min(l.w, 1.0); // (where on the drop, -1..1, from its offset)
	float spec = smoothstep(0.22, 0.0, length(hp - vec2(-0.38, -0.42))) * 0.5 * step(l.w, 0.9);
	vec3 drop = behindDrop * (1.0 - lowEdge) * (1.0 - outline) + vec3(topEdge + spec) + fringe;
	ALBEDO = mix(smeared, drop, l.z);
	ALPHA = 1.0;
}
