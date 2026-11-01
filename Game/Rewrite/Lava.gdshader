// The lava sheet (D257; Tom: lava "like water is, just with different properties and different flow speed ... different
// vertex shader, so it actually flows like rivers"). A mesh lying on the ground over an active volcano's lava, built per
// tile like the water's (TileBuilder.BuildLava). COLOR = (molten, crust, cover, 1), UV = which way the lava moves (its
// length the speed). Its look carries on from the terrain shader's lava (D250–D254): churning melt, a dark crust in
// islands and rifts, fine glowing cracks, shear, surges — now drawn per pixel on a surface of its own, with:
// * heavy, slow swells rolling down a flow and welling up on the lake (the vertex shader);
// * a ragged, lobed edge (no straight triangle edges), the sheet tucked under the ground there;
// * close up, grain in the crust, flakes and streaks on the melt, and relief that catches the light; the crust's fine
//   cracks ride with it, broken into segments (D258).
// Drawn only while lava_frame.w > 0 (the Lava setting on, near an active volcano).
shader_type spatial;
render_mode cull_back, diffuse_burley;

global uniform vec4 lava_frame; // xy: the volcano's middle in scene coordinates, w: 1 when there is lava to draw

varying vec2 lp;      // metres from the volcano's middle (world-anchored: the scene origin moves)
varying float swell;

float hash(vec2 p) { p = mod(p, 1000.0); vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float lava_fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	mat2 turn = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int o = 0; o < 4; o++) { v += a * (vnoise(p) * 2.0 - 1.0); p = turn * p * 2.03 + 7.1; a *= 0.5; }
	return v;
}
// x = churn (how hot the melt is), y = the rift field (rifts near 0), z = the fine crack field (cracks near 0).
vec3 lava_fields(vec2 p, vec2 drift) {
	float churn = vnoise(p * 0.04 + vec2(TIME * 0.015, -TIME * 0.01)) * 0.65 + vnoise(p * 0.13 - vec2(TIME * 0.04, TIME * 0.03)) * 0.35;
	vec2 wobble = 4.0 * vec2(lava_fbm(p * 0.05 + vec2(TIME * 0.03, 1.7)), lava_fbm(p * 0.05 + vec2(4.1, -TIME * 0.025)));
	vec2 q = p + wobble + 14.0 * vec2(lava_fbm(p * 0.02 + drift), lava_fbm(p * 0.02 + 5.2 - drift));
	// (the fine cracks ride with the crust: a fixed warp, no time — warped with the rest they reshaped every second,
	// boiling; and they are cut into segments, so no closed rings: whole they read as contour lines on a map)
	vec2 r = p + 3.0 * vec2(lava_fbm(p * 0.07 + 11.0), lava_fbm(p * 0.07 + 23.0));
	float crack_n = min(abs(lava_fbm(r * 0.22 + 3.3)), abs(lava_fbm(r * 0.13 + 7.7)) * 1.3) // (two sets crossing: junctions)
		+ 0.25 * smoothstep(0.42, 0.58, vnoise(r * 0.5 + 5.0));
	return vec3(churn, abs(lava_fbm(q * 0.035 + drift * 2.0)), crack_n);
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	lp = w.xz - lava_frame.xy;
	// Swells: broad, slow bulges carried down a flow (a fraction of its speed: lava is heavy), welling on the lake.
	vec2 f = UV;
	swell = vnoise(lp * 0.035 - f * TIME * 0.25) * 0.6 + vnoise(lp * 0.09 - f * TIME * 0.45 + 3.1) * 0.4;
	VERTEX.y += (COLOR.r * 0.45 + COLOR.g * 0.12) * (swell - 0.35);
	if (lava_frame.w <= 0.0) VERTEX.y -= 60.0; // (off: under the ground; the fragment discards it too)
}

void fragment() {
	if (lava_frame.w <= 0.0) discard;
	// The ragged edge: cover plus noise at two sizes (lobes of 3–10 m), no straight triangle edges.
	float edge = COLOR.b + 0.3 * (vnoise(lp * 0.12) - 0.5) + 0.18 * (vnoise(lp * 0.6) - 0.5);
	if (edge < 0.32) discard;
	float rim = 1.0 - smoothstep(0.32, 0.5, edge); // (the cooled, crusted margin)
	vec2 lv = vec2(COLOR.r, max(COLOR.g, rim * 0.5));
	vec2 drift = vec2(TIME * 0.004, -TIME * 0.003);
	vec2 flow_dir = UV;
	bool on_flow = length(flow_dir) > 0.31; // (a flow moves faster than 0.3 — the lake's creep is at most that)
	float speed = mix(0.2, 3.2, pow(lv.x, 1.5));
	vec2 fdn = on_flow ? normalize(flow_dir) : vec2(0.0);
	float along = dot(lp, fdn);
	float surge = on_flow ? smoothstep(0.55, 1.0, sin(along / 70.0 - TIME * 0.06 + 3.0 * vnoise(lp * 0.004))) * smoothstep(0.2, 0.6, vnoise(lp * 0.008 + 9.0)) : 0.0;
	lv.x = min(1.0, lv.x + surge * (0.35 * lv.y + 0.2 * lv.x)) * (1.0 - rim);
	float ph0 = fract(TIME / 10.0), ph1 = fract(TIME / 10.0 + 0.5);
	vec3 f0 = lava_fields(lp - flow_dir * speed * ph0 * 10.0, drift);
	vec3 f1 = lava_fields(lp - flow_dir * speed * ph1 * 10.0, drift);
	vec3 fld = mix(f0, f1, abs(ph0 * 2.0 - 1.0));
	float churn = fld.x;
	// (rifts on the lake narrower, edged by the field's screen-space change: broad rifts read as fuzzy blobs from above)
	float rift = on_flow ? 1.0 - smoothstep(0.0, 0.05 + 0.1 * churn, fld.y)
		: 1.0 - smoothstep(0.0, 0.02 + 0.05 * churn + fwidth(fld.y), fld.y);
	// (cracks a fixed few pixels wide at any distance, from the field's own screen-space change: a width in field units drew
	// glowing worms a metre or two wide up close)
	float fine = 1.0 - smoothstep(0.0, fwidth(fld.z) * 1.4 + 0.004, fld.z);
	float seam = (1.0 - smoothstep(0.0, on_flow ? 0.22 : 0.07 + 2.0 * fwidth(fld.y), fld.y)) * (1.0 - rift); // (narrow on the lake: 0.22 filled the gaps between its plates)
	float form = 0.22 * (vnoise(lp * 0.015 + vec2(TIME * 0.02, -TIME * 0.017)) - 0.5);
	float isle = smoothstep(-0.06, 0.06, fld.y * 2.2 - 0.25 - (1.0 - lv.x) * 0.55 + form - surge * 0.15);
	// (the lake: plates of crust with melt in the seams between them, and some open pools where it churns hottest)
	float pool = smoothstep(0.82, 0.88, churn + 0.1 * swell); // (rare: from 0.72 the lake turned almost all bright again)
	float open = on_flow ? max(lv.x * (1.0 - isle), lv.x * rift) : lv.x * max(rift, pool);
	// Close up, on the open melt: small flakes of skin drifting with it, and streaks stretched along the flow (moved like the
	// fields: two copies half a cycle apart).
	float dist0 = length(VERTEX);
	float close = 1.0 - smoothstep(40.0, 180.0, dist0);
	vec2 side = vec2(-fdn.y, fdn.x);
	vec2 p0 = lp - flow_dir * speed * ph0 * 10.0, p1 = lp - flow_dir * speed * ph1 * 10.0;
	float k = abs(ph0 * 2.0 - 1.0);
	float flake = mix(vnoise(p0 * 0.55 + 3.0) * 0.6 + vnoise(p0 * 1.6) * 0.4, vnoise(p1 * 0.55 + 3.0) * 0.6 + vnoise(p1 * 1.6) * 0.4, k);
	flake = smoothstep(0.58, 0.66, flake) * close;
	vec2 sp0 = on_flow ? vec2(dot(p0, fdn) * 0.08, dot(p0, side) * 0.9) : p0 * 0.3;
	vec2 sp1 = on_flow ? vec2(dot(p1, fdn) * 0.08, dot(p1, side) * 0.9) : p1 * 0.3;
	float streak = mix(vnoise(sp0), vnoise(sp1), k);
	open *= 1.0 - 0.9 * flake;
	float crust = max(lv.y, lv.x - open);
	float core_heat = clamp(lv.x * 1.15, 0.0, 1.0);
	// (deep orange at its hottest, not yellow-white: by day it burned to white)
	vec3 hot = mix(vec3(0.6, 0.04, 0.003), vec3(1.0, 0.4, 0.06), smoothstep(0.25, 0.95, core_heat * (0.75 + 0.35 * churn) + swell * 0.15));
	// Close up: grain in the crust (clinker a few decimetres across). (Fine glowing veins drew contour-line rings: gone.)
	float dist = dist0;
	float near = 1.0 - smoothstep(60.0, 250.0, dist);
	float grain = vnoise(lp * 1.7) * 0.6 + vnoise(lp * 4.3 + 7.0) * 0.4;
	float shade = (0.6 + 0.8 * churn) * mix(1.0, 0.55 + 0.9 * grain, near) * (1.0 + flake * 0.6);
	ALBEDO = vec3(0.022, 0.019, 0.017) * shade;
	ROUGHNESS = mix(0.9, 0.35, open);
	SPECULAR = 0.3;
	float heat = max(lv.x, lv.y);
	// (open melt at most ~1.6×: 3× burned it to white by day)
	float glow = open * (0.6 + 0.7 * churn) * (0.6 + 0.4 * core_heat) * mix(1.0, 0.7 + 0.6 * streak, close) + seam * lv.x * 0.6
		+ fine * crust * heat * (0.3 + 0.4 * churn) * (0.4 + core_heat);
	glow *= (0.8 + 0.4 * vnoise(lp * 0.025 + vec2(TIME * 0.12, TIME * 0.09))) * (1.0 + 0.6 * surge);
	float far = smoothstep(600.0, 3000.0, dist);
	EMISSION = hot * (glow * (1.0 - far) + max(lv.x, lv.y * 0.7) * 2.6 * far);
	// Relief: the crust stands up from the melt, with grain close by; lights catch the islands' edges.
	if (dist < 500.0) {
		float lh = crust * (0.6 * isle + 0.25) - open * 0.2 + crust * (grain - 0.5) * 0.12 * near;
		vec3 dpx = dFdx(VERTEX), dpy = dFdy(VERTEX);
		float hx = dFdx(lh), hy = dFdy(lh);
		vec3 r1 = cross(dpy, NORMAL), r2 = cross(NORMAL, dpx);
		float det = dot(dpx, r1);
		vec3 b = abs(det) * NORMAL - sign(det) * (hx * r1 + hy * r2);
		if (abs(det) > 1e-12 && dot(b, b) > 1e-24) NORMAL = normalize(mix(NORMAL, normalize(b), 1.0 - smoothstep(250.0, 500.0, dist)));
	}
}
