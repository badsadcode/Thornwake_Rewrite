// Spray and mist over falling and fast water (D292, Tom: "the waterfalls should use some nice particle effects, like a mist
// of water", "both for river and for waterfalls", after a reference of billowing, sunlit spray clouds). Each particle is a
// billboard drawn as a soft, billowing puff — a cauliflower edge that churns as it ages — lit as a ball of droplets: brighter
// toward the sun, shaded beneath, glowing where the light comes through it; fading where it meets water or rock.
// Particle COLOR (the process material's ramp): rgb tint, a = how dense over its life.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_disabled, diffuse_lambert_wrap;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float density = 1.0;
uniform float soft_distance = 1.5;   // metres over which a puff fades into what lies behind it
uniform float churn = 1.0;           // how much its edge billows (0 a plain soft disc)
// Tom's mist and spray sprites (D295): the puff's shape and inner detail from the picture instead of the drawn billow.
uniform sampler2D sprite : source_color, filter_linear_mipmap, repeat_disable;
uniform bool use_sprite = false;

varying float seed;
varying float age;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p)
{
	vec2 i = floor(p), f = fract(p);
	vec2 w = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), w.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), w.x), w.y);
}
float fbm(vec2 p) { return 0.5 * noise(p) + 0.3 * noise(p * 2.07 + 5.3) + 0.2 * noise(p * 4.13 + 11.7); }

void vertex()
{
	// Facing the camera, keeping the particle's scale and its spin.
	float scale = length(MODEL_MATRIX[0].xyz);
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]) * scale, normalize(INV_VIEW_MATRIX[1]) * scale, normalize(INV_VIEW_MATRIX[2]) * scale, MODEL_MATRIX[3]);
	float a = INSTANCE_CUSTOM.x;
	world = world * mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	seed = fract(float(INSTANCE_ID) * 0.6180339);
	age = INSTANCE_CUSTOM.y;
}

void fragment()
{
	vec2 q = UV * 2.0 - 1.0;
	float r = length(q);
	// The billow: lumps round the edge that roll as the puff ages.
	float ang = atan(q.y, q.x);
	float lumps = fbm(vec2(ang * 1.6 + seed * 40.0, age * 1.8 + seed * 9.0)) - 0.5;
	float inner = fbm(q * 2.2 + vec2(seed * 31.0, age * 1.3)) - 0.5;
	float edge = 0.78 + churn * (0.35 * lumps + 0.12 * inner);
	float body = 1.0 - smoothstep(edge - 0.32, edge, r);
	body *= 0.7 + 0.6 * (fbm(q * 3.5 + seed * 17.0 + age * 0.9) - 0.2);
	// A ball of droplets: its normal bulges toward the camera, so the sun lights one side and the other falls into shade.
	vec3 n = normalize(vec3(q * 0.9, sqrt(max(0.0, 1.0 - min(r, 1.0) * min(r, 1.0))) + 0.25));
	NORMAL = normalize(mix(NORMAL, n, 0.85));
	ALBEDO = COLOR.rgb;
	BACKLIGHT = COLOR.rgb * 0.6;         // light through the thin spray
	ROUGHNESS = 1.0;
	EMISSION = COLOR.rgb * 0.16;         // never grey in shade: droplets scatter the sky's light every way
	if (use_sprite) {
		// (the sprite rotated with the particle by its UV; a little of the drawn churn on top keeps each puff alive)
		vec4 sp = texture(sprite, UV);
		body = sp.a * (0.85 + 0.3 * (fbm(q * 2.0 + seed * 13.0 + age * 0.7) - 0.5));
		ALBEDO = COLOR.rgb * mix(0.8, 1.0, dot(sp.rgb, vec3(0.333)));
	}
	float alpha = clamp(body, 0.0, 1.0) * COLOR.a * density;
	// Soft where it meets the water or rock behind it.
	float d = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0);
	behind.xyz /= behind.w;
	alpha *= clamp(1.0 - smoothstep(behind.z + soft_distance, behind.z, VERTEX.z), 0.0, 1.0);
	ALPHA = alpha;
}
