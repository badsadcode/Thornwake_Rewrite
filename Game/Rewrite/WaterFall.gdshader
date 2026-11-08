// Falling water at waterfalls (D285, the water rework's second step; Waterfalls.cs).
// The curtain: UV = (metres across the fall, metres down its path from the lip); COLOR = (speed / 25 m/s, near the pool,
// sliding on rock, edge fade). Streaks run down it, stretching and speeding as the water falls; it turns white with the
// drop (air churned in), thins and breaks up at its edges, churns where it meets the pool.
// The foam disc (foam_disc): white water spreading on the pool round the foot; UV = metres from the foot (xz), COLOR.a =
// 1 at the foot fading to 0 at its rim.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_schlick_ggx;

uniform bool foam_disc = false;
uniform vec3 deep_color : source_color = vec3(0.16, 0.30, 0.33);
uniform vec3 white_color : source_color = vec3(0.90, 0.94, 0.96);

// Pulled toward the camera a little, more with distance: the curtain lies 0.3 m off the exact ground, but far from the
// camera the terrain's coarser mesh across a steep wall stands metres above it and buried the curtain.
void vertex()
{
	vec3 view = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float bias = foam_disc ? 0.3 + 0.004 * length(view) : 0.6 + 0.012 * length(view);
	view += normalize(-view) * min(bias, length(view) * 0.5);
	POSITION = PROJECTION_MATRIX * vec4(view, 1.0);
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p)
{
	vec2 i = floor(p), f = fract(p);
	vec2 w = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), w.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), w.x), w.y);
}

float fbm(vec2 p)
{
	return 0.5 * noise(p) + 0.3 * noise(p * 2.03 + 7.1) + 0.2 * noise(p * 4.1 + 13.7);
}

void fragment()
{
	vec3 albedo;
	float alpha, rough;
	if (foam_disc)
	{
		// Spreading out from the foot: churned water boiling up in clumps and drifting outward, breaking into patches.
		float r = length(UV), ang = atan(UV.y, UV.x);
		float outward = fbm(vec2(ang * 4.0, r * 0.55 - TIME * 0.9));
		float boil = fbm(UV * 0.45 + vec2(TIME * 0.35, -TIME * 0.27));
		float fine = noise(UV * 2.3 - vec2(TIME * 0.6, TIME * 0.4));
		float cover = COLOR.a;
		float churn = 0.45 * outward + 0.35 * boil + 0.2 * fine;
		float white = smoothstep(0.58, 0.42, churn - 0.6 * cover + 0.42) * smoothstep(0.0, 0.3, cover);
		albedo = white_color;
		alpha = clamp(white * 0.95, 0.0, 0.95);
		rough = 0.8;
	}
	else
	{
		float speed = COLOR.r * 25.0;
		float down = UV.y;
		// Strands: long along the path, narrow across, at the water's speed; clumps of white falling faster over them.
		float s1 = fbm(vec2(UV.x * 1.1, down * 0.08 - TIME * speed * 0.08));
		float s2 = fbm(vec2(UV.x * 4.0 + 5.0, down * 0.25 - TIME * speed * 0.24));
		float clumps = noise(vec2(UV.x * 0.45 + 2.0, down * 0.12 - TIME * speed * 0.14));
		float streak = 0.5 * s1 + 0.3 * s2 + 0.2 * clumps;
		// Clear and glassy at the lip, white once air is churned in (with the drop, sliding on rock), churning at the pool.
		float aeration = clamp(COLOR.r * 4.0 + COLOR.b * 0.4, 0.0, 1.0);
		float white = smoothstep(0.35, 0.6, streak + 0.35 * aeration - 0.2 + COLOR.g * 0.5);
		white = max(white, COLOR.g * smoothstep(0.3, 0.7, s2 + 0.3));
		// Strands of white and gaps of darker, glassier water between them (D290: an even pale sheet read as a plank).
		float strands = smoothstep(0.42, 0.62, s2 * 0.6 + s1 * 0.4);
		white = clamp(white * (0.55 + 0.6 * strands), 0.0, 1.0);
		albedo = mix(deep_color * 0.8, white_color, white);
		// Ragged edges: the sides fray where the strands part, moving down with the water.
		float fray = smoothstep(0.0, 0.55, COLOR.a + 0.55 * (streak - 0.55) + 0.25 * (clumps - 0.5));
		float body = smoothstep(0.25, 0.55, streak + 0.4 * (COLOR.a - 0.5));
		alpha = clamp((0.4 + 0.6 * max(body, white)) * fray, 0.0, 0.97);
		rough = mix(0.05, 0.7, white);
		// Relief from the strands and clumps for the light to catch (bump from screen-space derivatives: the mesh has no
		// tangents).
		float height = 0.35 * streak + 0.25 * white;
		vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
		vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
		float det = dot(dpdx, r1);
		vec3 grad = sign(det) * (dFdx(height) * r1 + dFdy(height) * r2);
		NORMAL = normalize(abs(det) * NORMAL - grad * 1.5);
	}
	ALBEDO = albedo;
	// White water lets light through and scatters it (in a cliff's shade it was lit by the blue sky alone: a blue strip).
	BACKLIGHT = vec3(0.35) * albedo;
	EMISSION = albedo * 0.05;
	ALPHA = alpha;
	ROUGHNESS = rough;
	SPECULAR = 0.5;
}
