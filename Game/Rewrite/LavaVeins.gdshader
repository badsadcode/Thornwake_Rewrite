// The veined lava (D260; setting Lava → "glowing", the default; "glowing (build 125)" is Lava.gdshader): our own look for the lava sheet, after what Tom liked in a
// third-party shader he could not keep (D259) — melt laced with bright veins that churn as it flows, black crust rafts
// riding on it at their own pace, the hot middle of a flow bright orange. Written afresh: ridged value noise, each octave
// carried down the flow at its own speed (finer ones faster: the melt shears and churns), warped by a field that sways
// in place (no drift one fixed way); two copies half a cycle apart cross-fade so the pattern never shears apart. The
// sheet's swells, ragged edge, relief and far glow are Lava.gdshader's.
shader_type spatial;
render_mode cull_back, diffuse_burley;

global uniform vec4 lava_frame; // xy: the volcano's middle in scene coordinates, w: 1 when there is lava to draw

uniform float melt_speed = 1.4; // m/s the veins run down a flow at full speed (the finest twice that)
uniform float raft_speed = 0.9; // m/s the crust rafts ride at full speed (slower than the melt's veins)
uniform float strength = 1.0;

varying vec2 lp;      // metres from the volcano's middle (world-anchored: the scene origin moves)
varying float swell;

float hash(vec2 p) { p = mod(p, 1000.0); vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm3(vec2 p) { return vnoise(p) * 0.55 + vnoise(p * 2.07 + 5.3) * 0.3 + vnoise(p * 4.3 + 11.1) * 0.15; }

// The veins (0 … ~1, bright on the ridges): five octaves of ridged noise over metres p, each moved down the flow by
// drift × (1 + its number × 0.35).
float veins(vec2 p, vec2 drift) {
	float sum = 0.0, amp = 0.5, freq = 0.045;
	mat2 turn = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)), m = mat2(1.0);
	for (int o = 0; o < 5; o++) {
		float fo = float(o);
		// (each octave turned its own fixed way: on one grid the ridges lined up into squares)
		vec2 q = m * ((p - drift * (1.0 + 0.35 * fo)) * freq) + fo * 17.3;
		m = turn * m;
		float r = 1.0 - abs(vnoise(q) * 2.0 - 1.0);
		sum += r * r * r * amp;
		amp *= 0.6;
		freq *= 2.13;
	}
	return sum * 1.45;
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	lp = w.xz - lava_frame.xy;
	// Swells: broad, slow bulges carried down a flow, welling on the lake (as Lava.gdshader).
	vec2 f = UV;
	swell = vnoise(lp * 0.035 - f * TIME * 0.25) * 0.6 + vnoise(lp * 0.09 - f * TIME * 0.45 + 3.1) * 0.4;
	VERTEX.y += (COLOR.r * 0.45 + COLOR.g * 0.12) * (swell - 0.35);
	if (lava_frame.w <= 0.0) VERTEX.y -= 60.0;
}

void fragment() {
	if (lava_frame.w <= 0.0) discard;
	// The ragged edge and the cooled, crusted margin (as Lava.gdshader).
	float edge = COLOR.b + 0.3 * (vnoise(lp * 0.12) - 0.5) + 0.18 * (vnoise(lp * 0.6) - 0.5);
	if (edge < 0.32) discard;
	float rim = 1.0 - smoothstep(0.32, 0.5, edge);
	float molten = COLOR.r * (1.0 - rim), crust = max(COLOR.g, rim * 0.5);
	bool on_flow = length(UV) > 0.31; // (a flow moves faster than 0.3 — the lake's creep is at most that)

	// The churn: a warp that sways in place (its offset in noise space circles slowly; no drift one way).
	vec2 sway = vec2(sin(TIME * 0.05), cos(TIME * 0.041)) * 2.5;
	vec2 wp = lp + 9.0 * vec2(fbm3(lp * 0.02 + sway) - 0.5, fbm3(lp * 0.02 - sway.yx + 7.7) - 0.5);
	// Down the flow (UV: which way and how fast); the lake's melt only churns (a drift growing out from its middle
	// stretched a pattern into rings, D259).
	float period = 10.0;
	float ph0 = fract(TIME / period), ph1 = fract(TIME / period + 0.5), k = abs(ph0 * 2.0 - 1.0);
	vec2 v = on_flow ? UV * melt_speed * period : vec2(0.0);
	float vein = mix(veins(wp, v * ph0), veins(wp, v * ph1), k);

	// Black crust rafts at their own pace (their own 30 s cycle: at 8 s they faded in and out, D259): jagged, more where it
	// has cooled, plates on the lake; the melt glows hotter round their edges.
	float rp = 30.0, rq0 = fract(TIME / rp), rq1 = fract(TIME / rp + 0.5), rk = abs(rq0 * 2.0 - 1.0);
	vec2 rv = UV * raft_speed * rp;
	vec2 q0 = lp - rv * rq0, q1 = lp - rv * rq1;
	float raft_n = mix(fbm3(q0 * 0.07) + 0.12 * (vnoise(q0 * 0.6) - 0.5), fbm3(q1 * 0.07) + 0.12 * (vnoise(q1 * 0.6) - 0.5), rk);
	float cool = 1.0 - smoothstep(0.2, 0.95, molten);
	float t0 = on_flow ? mix(0.7, 0.56, cool) : 0.6;
	float raft = smoothstep(t0, t0 + 0.02, raft_n) * step(0.02, molten + crust);
	float raft_edge = smoothstep(t0 - 0.06, t0, raft_n) * (1.0 - raft);

	// The melt's colour: dark red between the veins, orange on them, yellow on the brightest; the hot middle of a flow (or
	// the lake's hottest melt) bright orange throughout, the veins still showing (Tom: "the middle ... brighter orange").
	float core = smoothstep(0.55, 0.95, molten);
	float heat = mix(0.1, 1.0, smoothstep(0.0, 0.8, max(molten, crust * 0.35))); // (the cooler margins keep dim veins)
	// (contrast, not a raised floor: lifting the whole middle drew a flat yellow glare with no veins in it)
	float e = pow(clamp(vein, 0.0, 1.5), 2.0) * (1.0 + 0.5 * raft_edge);
	vec3 col = mix(vec3(0.5, 0.05, 0.008), vec3(1.0, 0.4, 0.07), smoothstep(0.1, 0.6, e));
	col = mix(col, vec3(1.0, 0.68, 0.28), smoothstep(0.6, 1.2, e) * core);
	float bright = (0.12 + 1.1 * e) * mix(0.7, 1.5, core) * heat;
	vec3 glow = col * bright * (1.0 - raft * 0.97) * strength;

	float dist = length(VERTEX);
	float near = 1.0 - smoothstep(60.0, 250.0, dist);
	float grain = vnoise(lp * 1.7) * 0.6 + vnoise(lp * 4.3 + 7.0) * 0.4;
	ALBEDO = vec3(0.022, 0.019, 0.017) * mix(1.0, 0.55 + 0.9 * grain, near) * (1.0 + raft * 0.4);
	float open = (1.0 - raft) * smoothstep(0.08, 0.4, molten);
	ROUGHNESS = mix(0.92, 0.4, open);
	SPECULAR = 0.3;
	float far = smoothstep(600.0, 3000.0, dist);
	vec3 hot = mix(vec3(0.6, 0.04, 0.003), vec3(1.0, 0.4, 0.06), smoothstep(0.25, 0.95, molten));
	EMISSION = glow * (1.0 - far) + hot * max(molten, crust * 0.7) * 2.6 * far;
	// Relief: the rafts stand up from the melt, with grain close by (as Lava.gdshader's crust).
	if (dist < 500.0) {
		float lh = raft * 0.7 - open * 0.15 + raft * (grain - 0.5) * 0.12 * near;
		vec3 dpx = dFdx(VERTEX), dpy = dFdy(VERTEX);
		float hx = dFdx(lh), hy = dFdy(lh);
		vec3 r1 = cross(dpy, NORMAL), r2 = cross(NORMAL, dpx);
		float det = dot(dpx, r1);
		vec3 b = abs(det) * NORMAL - sign(det) * (hx * r1 + hy * r2);
		if (abs(det) > 1e-12 && dot(b, b) > 1e-24) NORMAL = normalize(mix(NORMAL, normalize(b), 1.0 - smoothstep(250.0, 500.0, dist)));
	}
}
