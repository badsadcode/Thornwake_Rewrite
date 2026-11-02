// The volcano's column of ash and gas (D258, D262, VolcanoSmoke.cs): Tom's smoke puff, billboarded, lit by the scene.
// * Each puff its own shade (Tom: "bigger, denser, and have more color variance"; his pictures: brown-grey ash billowing
//   up, white steam among it): ash brown, grey or dark, a few puffs of white steam.
// * Lit as a rounded billow (a normal bulging toward the camera, turned with the puff's spin): sunlit on one side, in
//   shadow on the other, darker underneath.
// * Each puff fades in as it leaves the crater, thins as it rises and fades away before it ends (the particles' colour
//   ramp never reached COLOR here, so build 126's puffs stayed whole and vanished at once).
// * At dusk and night its underside glows dull red from the lava below, strongest as a puff leaves the lake, dimming up
//   the column. Soft where it meets the ground.
// Build 126's plume: VolcanoSmokeBuild126.gdshader.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D puff : source_color, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
global uniform vec4 sun_true; // w: the real sun's elevation in degrees (sun_dir_world follows the moon at night)
uniform float glow = 0.55;

varying float age;  // 0 … 1 through the puff's life (the particles give a fraction, not seconds)
varying float spin; // the puff's turn (radians)
varying vec3 shade; // the puff's own colour

float hash(float n) { return fract(sin(n * 12.9898 + 4.1) * 43758.5453); }

void vertex() {
	// (Godot's particle billboard, keeping the particle's scale and spin)
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0),
		vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	age = clamp(INSTANCE_CUSTOM.y, 0.0, 1.0);
	spin = INSTANCE_CUSTOM.x;
	// (each particle slot keeps its shade through its lives: 240 of them mix well enough)
	float id = float(INSTANCE_ID);
	float k = hash(id), b = hash(id + 17.0);
	vec3 ash = mix(vec3(0.24, 0.19, 0.16), vec3(0.6, 0.5, 0.43), b);   // brown ash, dark to light
	vec3 grey = mix(vec3(0.22, 0.21, 0.21), vec3(0.62, 0.6, 0.58), b); // grey ash
	shade = k < 0.2 ? vec3(0.95, 0.95, 0.96) : k < 0.65 ? ash : grey;  // (a few white puffs of steam)
}

void fragment() {
	vec4 t = texture(puff, UV);
	// (warmer where it leaves the lava)
	ALBEDO = mix(shade, shade * vec3(1.1, 0.95, 0.85), 1.0 - smoothstep(0.0, 0.2, age)) * t.rgb;
	// The billow's roundness: a normal bulging toward the camera from the puff's middle (view space), turned with its spin.
	vec2 l = vec2(UV.x - 0.5, 0.5 - UV.y) * 2.0;
	vec2 v2 = l.x * vec2(cos(spin), -sin(spin)) + l.y * vec2(sin(spin), cos(spin));
	NORMAL = normalize(vec3(v2 * 0.85, max(0.3, sqrt(max(0.0, 1.0 - dot(l, l))))));
	AO = mix(0.55, 1.0, 0.5 + 0.5 * NORMAL.y); // (darker underneath)
	AO_LIGHT_AFFECT = 0.6;
	// Denser (the texture's soft edge kept), fading in over its first twentieth and away over its last half.
	float fade = smoothstep(0.0, 0.05, age) * mix(0.95, 0.85, smoothstep(0.05, 0.4, age)) * (1.0 - smoothstep(0.45, 1.0, age));
	float a = fade * pow(t.a, 0.75);
	// soft against the ground (proximity fade over 30 m; the sky behind, at infinite depth, leaves it whole — dividing by
	// its zero w made the whole puff vanish)
	float d = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 v = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0);
	if (abs(v.w) > 1e-6) a *= clamp((-v.z / v.w + VERTEX.z) / 30.0, 0.0, 1.0);
	ALPHA = a;
	// (dim and red: at 5× it read as a ball of fire floating over the summit; and only at dusk and night — by day the sun
	// outshines it, and left on it tinted the white plume orange. Red underneath, dimming up the column, gone by three
	// quarters of a puff's life: a fifth, as first meant in D258, left the night plume almost unseen.)
	float base = pow(1.0 - smoothstep(0.0, 0.75, age), 1.5) * mix(0.6, 1.0, 0.5 - 0.5 * NORMAL.y);
	float night = 1.0 - smoothstep(-4.0, 10.0, sun_true.w);
	EMISSION = vec3(0.85, 0.22, 0.07) * glow * base * night * t.a * smoothstep(0.0, 0.05, age);
}
