// The volcano's gas plume as in build 126 (setting Volcano smoke → "on (build 126)"; D262): kept as it was. Its puffs never
// faded (the particles' colour ramp does not reach a shader's COLOR here) and its glow read the age in seconds where the
// particles give a 0–1 fraction, so the whole plume glowed at night. The current plume: VolcanoSmoke.gdshader.
// The volcano's gas plume (D258, VolcanoSmoke.cs): Tom's smoke puff, billboarded, coloured by the particles' ramp, lit by
// the scene; at dusk and night its base glows dull red from the lava below — strongest as a puff leaves the lake, gone a
// fifth of the way up. Soft where it meets the ground.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D puff : source_color, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
global uniform vec4 sun_true; // w: the real sun's elevation in degrees (sun_dir_world follows the moon at night)
uniform float glow = 0.55;
uniform float lifetime = 55.0; // (the particles' age comes in seconds)

varying float age;

void vertex() {
	// (Godot's particle billboard, keeping the particle's scale and spin)
	mat4 world = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	world = world * mat4(vec4(cos(INSTANCE_CUSTOM.x), -sin(INSTANCE_CUSTOM.x), 0.0, 0.0), vec4(sin(INSTANCE_CUSTOM.x), cos(INSTANCE_CUSTOM.x), 0.0, 0.0),
		vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	age = clamp(INSTANCE_CUSTOM.y / lifetime, 0.0, 1.0);
}

void fragment() {
	vec4 t = texture(puff, UV);
	ALBEDO = COLOR.rgb * t.rgb;
	float a = COLOR.a * t.a;
	// soft against the ground (proximity fade over 30 m; the sky behind, at infinite depth, leaves it whole — dividing by
	// its zero w made the whole puff vanish)
	float d = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 v = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0);
	if (abs(v.w) > 1e-6) a *= clamp((-v.z / v.w + VERTEX.z) / 30.0, 0.0, 1.0);
	ALPHA = a;
	// (dim and red: at 5× it read as a ball of fire floating over the summit; and only at dusk and night — by day the sun
	// outshines it, and left on it tinted the white plume orange)
	float base = pow(1.0 - smoothstep(0.0, 0.2, age), 2.0);
	float night = 1.0 - smoothstep(-4.0, 10.0, sun_true.w);
	EMISSION = vec3(0.85, 0.22, 0.07) * glow * base * night * t.a;
}
