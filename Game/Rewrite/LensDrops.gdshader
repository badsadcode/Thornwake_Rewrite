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
	return vec4(-n * r * (1.6 + 1.2 * dist * dist), body, dist);
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
	float r = cell * (0.12 + 0.2 * hash(c + seed + cycle + 1.9)) * size * (1.0 - 0.7 * k) * step(0.02, k);
	return lens(f * cell, centre, max(r, 1e-4), 1.0);
}

// One runner of lane column `col`, run `cycle` (the current one or the one before, whose trail may still be drying).
// Returns the drop or its trail beads (lens) and, in `smear`, the wet trail's strength at q.
vec4 runner(vec2 q, float lane, float col, float cycle, float progress, float period, float seed, float rate, out float smear) {
	smear = 0.0;
	if (hash1(col * 3.7 + cycle * 1.9 + seed) > rate) return vec4(0.0, 0.0, 0.0, 9.0);
	float h = hash1(col * 7.1 + cycle + seed);
	float spd = 0.9 + 0.5 * h;
	// stick-slip: the run pauses and slips (a ripple on the progress)
	float run = progress * spd;
	float y = -0.15 + 1.35 * (run + 0.011 * sin(run * 90.0 + h * 6.0));
	float cx = (col + 0.5) * lane;
	// the path wanders sideways with the height (fixed in place: the trail follows the path)
	float wander = lane * 0.28 * (sin(q.y * 9.0 + h * 6.3) * 0.6 + sin(q.y * 23.0 + h * 2.1) * 0.4);
	float wanderAt = lane * 0.28 * (sin(y * 9.0 + h * 6.3) * 0.6 + sin(y * 23.0 + h * 2.1) * 0.4);
	float r = lane * (0.16 + 0.06 * h) * size;
	vec4 drop = tear(q, vec2(cx + wanderAt, y), r);
	// how long ago the drop passed this height (s): the trail dries over trail_fade
	float pq = ((q.y + 0.15) / 1.35) / spd;
	float age = (progress - pq) * period;
	// (D219, Tom: the trail "thinner and not cut off … smoother, the transition between the trail and the drop"): a thin
	// trail with soft edges (a gaussian across), widening only in the drop's neck, narrowing as it dries; it fades in
	// from inside the drop's tail instead of starting at a hard line
	float behind = y - q.y;
	float fresh = smoothstep(-r * 0.5, r * 1.5, behind) * smoothstep(-0.02, 0.0, age) * (1.0 - smoothstep(0.0, trail_fade, age));
	float neck = 1.0 - smoothstep(r * 1.5, r * 3.5, behind);
	float tw = r * mix(0.14, 0.32, neck) * (0.55 + 0.45 * fresh);
	float dx = q.x - (cx + wander);
	float onPath = exp(-dx * dx / (tw * tw));
	smear = fresh * onPath * 0.5;
	// (D219, Tom: "the trail … should also be refracting and dispersing the light a little"): the wet line is a thin
	// cylindrical lens — the picture from across it, flipped sideways, with the colour split at its edges
	vec4 line = vec4(-dx * 2.2, 0.0, fresh * onPath * 0.9, abs(dx) / (tw * 1.6));
	vec4 trail = vec4(0.0, 0.0, 0.0, 9.0);
	float bcell = r * 1.1;
	float bi = floor(q.y / bcell);
	if (fresh > 0.0 && neck < 1.0 && hash1(bi + col * 13.0 + cycle) < 0.45) {
		// (tiny beads left behind; they shrink as they dry)
		float br = r * (0.12 + 0.14 * hash1(bi * 3.1 + col + cycle)) * (0.35 + 0.65 * fresh) * (1.0 - neck);
		vec2 bc = vec2(cx + wander + (hash1(bi + 5.0 + cycle) - 0.5) * r * 0.3, (bi + 0.5) * bcell);
		trail = lens(q, bc, br, 1.0);
	}
	return pick(pick(drop, line), trail);
}

// Runner lanes `lane` screen heights wide; each lane runs a drop now and then. smear: the wet trails at q.
vec4 runners(vec2 q, float lane, float seed, float rate, out float smear) {
	float col = floor(q.x / lane);
	float period = 5.0 + 6.0 * hash1(col + seed);
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
	vec4 bead = pick(beads(q, 0.035, 2.9, amount * 0.45 * (1.0 - wiped)), beads(q + vec2(0.017, 0.011), 0.022, 6.1, amount * 0.35 * (1.0 - wiped)));
	vec4 l = pick(run, bead);
	if (l.z <= 0.002 && smear <= 0.002) discard;
	vec3 here = textureLod(screen_tex, uv, 0.0).rgb;
	// the wet smear: a little blur and a touch of light
	vec3 smeared = mix(here, textureLod(screen_tex, uv, 1.5).rgb * 1.05, smear);
	vec2 off = l.xy / vec2(aspect, 1.0);
	// dispersion: each colour refracted by its own amount (red least, blue most), the split growing toward the rim
	vec2 ca = vec2(0.001), cb = vec2(0.999);
	float split = 0.12 + 0.22 * smoothstep(0.3, 1.0, l.w);
	vec3 behindDrop = vec3(
		textureLod(screen_tex, clamp(uv + off * (1.0 - split), ca, cb), 1.0).r,
		textureLod(screen_tex, clamp(uv + off, ca, cb), 1.0).g,
		textureLod(screen_tex, clamp(uv + off * (1.0 + split), ca, cb), 1.0).b);
	// lit on the upper edge (light from above), darker toward the lower rim; a faint spectral fringe at the rim
	vec2 nrm = normalize(-l.xy + vec2(1e-5));
	float rim = smoothstep(0.55, 0.92, l.w) * (1.0 - smoothstep(0.92, 1.0, l.w));
	float topEdge = smoothstep(0.4, 1.0, -nrm.y) * 0.08;
	float lowEdge = smoothstep(0.4, 1.0, nrm.y) * 0.18;
	vec3 fringe = spectrum(fract(atan(nrm.y, nrm.x) / 6.2832 + 0.15)) * rim * 0.09 * (0.6 + 0.4 * dot(here, vec3(0.33)));
	vec3 drop = behindDrop * (1.0 - lowEdge) + vec3(topEdge) + fringe;
	ALBEDO = mix(smeared, drop, l.z);
	ALPHA = 1.0;
}
