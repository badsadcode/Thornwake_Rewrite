// A test look for the lava sheet (D259; setting Lava → "glowing, flow noise (test)"): the flow-noise lava from a
// canvas_item shader Tom found, moved onto the sheet. Its noise is the same, only fed the point's metres from the
// volcano (world-anchored) instead of the pixel's place on the screen, and drawn as emission over a dark crust, dimmed
// where the lava has cooled. Its drift runs down the flow (Tom: "flow in proper direction"; the original drifted one
// fixed way), two copies half a cycle apart cross-fading so the pattern never shears; black crust rafts ride on the melt
// (Tom: "some of those black spots"), more and bigger as it cools, the lake's melt only churning. The sheet's swells and ragged
// edge are Lava.gdshader's.
// (Third-party code: its licence is to be checked before this stays in the game.)
shader_type spatial;
render_mode cull_back, diffuse_burley;

global uniform vec4 lava_frame; // xy: the volcano's middle in scene coordinates, w: 1 when there is lava to draw

uniform sampler2D noise_texture : repeat_enable, filter_linear; // seamless (WorldStreamer makes one)
uniform float time_scale = 0.04;
uniform float pattern_scale = 0.03; // pattern units per metre (the original's screen was ~3 units across)
uniform float strength = 2.0;
uniform float flow_speed = 1.2; // how fast the pattern runs down a flow at full speed (its drift ×; Tom: "slower", 3 before)
uniform float raft_speed = 0.9; // the rafts' speed at full speed, m/s (slower than the melt below them, Tom: "at different speed")

varying vec2 lp;
varying float swell;

float hash(vec2 p) { p = mod(p, 1000.0); vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float local_time() { return TIME * time_scale; }

mat2 makem2(float theta) { float c = cos(theta), s = sin(theta); return mat2(vec2(c, -s), vec2(s, c)); }

float tex_noise(vec2 x) { return texture(noise_texture, x * 0.01).x; }

vec2 gradn(vec2 p) {
	float ep = 0.09;
	return vec2(tex_noise(vec2(p.x + ep, p.y)) - tex_noise(vec2(p.x - ep, p.y)), tex_noise(vec2(p.x, p.y + ep)) - tex_noise(vec2(p.x, p.y - ep)));
}

// (drift: down the flow, in pattern units — the original added local_time() to both coordinates, one fixed way)
float flow_noise(vec2 p, vec2 drift) {
	float z = 2.0, rz = 0.0;
	vec2 bp = p;
	for (float i = 1.0; i < 7.0; ++i) {
		p -= drift * 0.6;
		bp -= drift * 1.9;
		vec2 gr = gradn(i * p * 0.34 + local_time());
		gr *= makem2(local_time() * 6.0 - (0.05 * p.x + 0.03 * p.y) * 40.0);
		p += gr * 0.5;
		rz += (sin(tex_noise(p) * 7.0) * 0.5 + 0.5) / z;
		p = mix(bp, p, 0.77);
		z *= 1.4;
		p *= 2.0;
		bp *= 1.9;
	}
	return rz;
}

void vertex() {
	vec3 w = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	lp = w.xz - lava_frame.xy;
	vec2 f = UV;
	swell = vnoise(lp * 0.035 - f * TIME * 0.25) * 0.6 + vnoise(lp * 0.09 - f * TIME * 0.45 + 3.1) * 0.4;
	VERTEX.y += (COLOR.r * 0.45 + COLOR.g * 0.12) * (swell - 0.35);
	if (lava_frame.w <= 0.0) VERTEX.y -= 60.0;
}

void fragment() {
	if (lava_frame.w <= 0.0) discard;
	float edge = COLOR.b + 0.3 * (vnoise(lp * 0.12) - 0.5) + 0.18 * (vnoise(lp * 0.6) - 0.5);
	if (edge < 0.32) discard;
	float rim = 1.0 - smoothstep(0.32, 0.5, edge);
	float molten = COLOR.r * (1.0 - rim), crust = max(COLOR.g, rim * 0.5);
	// Down the flow (UV: which way and how fast; on the lake slowly out from its middle): two copies of the drift half a
	// cycle apart, cross-faded — one drift growing for ever sheared the pattern where neighbouring points move differently.
	float period = 8.0;
	float ph0 = fract(TIME / period), ph1 = fract(TIME / period + 0.5), k = abs(ph0 * 2.0 - 1.0);
	vec2 p = lp * pattern_scale;
	// (the lake's melt only churns: its outward drift, growing from its middle, stretched the noise into rings; its rafts
	// still creep out)
	bool on_flow = length(UV) > 0.31;
	vec2 drift = on_flow ? UV * period * time_scale * flow_speed : vec2(0.0);
	float rz = mix(flow_noise(p, drift * ph0), flow_noise(p, drift * ph1), k);
	rz = max(rz, 0.05);
	vec3 col = pow(vec3(0.2, 0.07, 0.01) / rz, vec3(1.4));
	float heat = mix(0.08, 1.0, smoothstep(0.0, 0.8, max(molten, crust * 0.35)));
	// Black crust rafts riding on the melt (moved like the pattern): jagged, more and bigger where it has cooled, few on the
	// hottest melt. (Turned to lie along a flow they drew stair-steps: the turn, times metres from the volcano, jumped at
	// the mesh's triangle edges.)
	// (their own, longer cycle: at 8 s they faded in and out as the two copies crossed)
	float rp = 30.0, rq0 = fract(TIME / rp), rq1 = fract(TIME / rp + 0.5), rk = abs(rq0 * 2.0 - 1.0);
	vec2 q0 = lp - UV * rq0 * rp * raft_speed, q1 = lp - UV * rq1 * rp * raft_speed;
	vec2 s0 = q0, s1 = q1;
	float r0 = vnoise(s0 * 0.07) * 0.55 + vnoise(s0 * 0.17 + 3.7) * 0.3 + vnoise(s0 * 0.5 + 9.1) * 0.15;
	float r1 = vnoise(s1 * 0.07) * 0.55 + vnoise(s1 * 0.17 + 3.7) * 0.3 + vnoise(s1 * 0.5 + 9.1) * 0.15;
	float raft_n = mix(r0, r1, rk);
	float cool = 1.0 - smoothstep(0.2, 0.95, molten);
	float t0 = on_flow ? mix(0.7, 0.56, cool) : 0.6; // (0.46 cooled crusted the flow's edges over in wide black sheets; the lake: plates)
	float raft = smoothstep(t0, t0 + 0.025, raft_n) * step(0.02, molten + crust);
	float raft_edge = smoothstep(t0 - 0.05, t0, raft_n) * (1.0 - raft); // (the melt glows hotter round a raft's edge)
	col *= 1.0 + 0.6 * raft_edge;
	col *= 1.0 - raft * 0.97;
	ALBEDO = vec3(0.022, 0.019, 0.017) * (1.0 + raft * (0.6 * vnoise(lp * 1.3) + 0.3));
	ROUGHNESS = mix(0.8, 0.95, raft);
	SPECULAR = 0.3;
	float dist = length(VERTEX);
	float far = smoothstep(600.0, 3000.0, dist);
	// The hot middle of a flow (and the lake's hottest melt) bright yellow-orange as in Lava.gdshader (Tom: "mix both ... how
	// the middle of the previous lava was brighter orange"), the pattern still showing through it.
	float core = smoothstep(0.55, 0.95, molten) * (1.0 - raft);
	float pattern = clamp(0.3 / rz, 0.0, 1.6);
	vec3 hot = vec3(1.0, 0.42, 0.07) * core * 0.9 * mix(0.45, 1.15, pattern); // (1.3 burned out close up)
	EMISSION = mix(col * heat * strength + hot, vec3(0.6, 0.12, 0.02) * max(molten, crust * 0.7) * 2.6, far);
}
