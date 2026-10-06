// Ground mist wisps (MistField.cs): large soft camera-facing sheets hugging the ground. Soft where they cut into the
// terrain (depth fade), faded near the camera (no wall in your face) and far away, noisy edges that drift slowly.
// Per instance (custom data): r = opacity, g = seed.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix, shadows_disabled;

uniform vec3 mist_color : source_color = vec3(0.85, 0.88, 0.9);
uniform float amount = 0.0;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear_mipmap;
varying float opacity;
varying float seed;
varying vec3 world;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void vertex() {
	opacity = INSTANCE_CUSTOM.r;
	seed = INSTANCE_CUSTOM.g;
	// Billboard around the vertical axis, a little flattened: mist lies low.
	vec3 origin = MODEL_MATRIX[3].xyz;
	float size = length(MODEL_MATRIX[0].xyz);
	vec3 to_cam = CAMERA_POSITION_WORLD - origin;
	vec3 right = normalize(vec3(to_cam.z, 0.0, -to_cam.x) + vec3(1e-4, 0.0, 0.0));
	world = origin + right * VERTEX.x * size + vec3(0.0, 1.0, 0.0) * VERTEX.y * size * 0.35;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(world, 1.0);
	// Upright sheets read as streaks from above: fade out as the view steepens and the camera climbs.
	vec3 dir = normalize(CAMERA_POSITION_WORLD - origin);
	opacity *= (1.0 - smoothstep(0.22, 0.5, dir.y)) * (1.0 - smoothstep(25.0, 60.0, CAMERA_POSITION_WORLD.y - origin.y));
}

void fragment() {
	vec2 c = UV * 2.0 - 1.0;
	float shape = 1.0 - smoothstep(0.35, 1.0, length(c * vec2(1.0, 1.25)));
	float n = vnoise(UV * 3.0 + seed * 17.0 + vec2(TIME * 0.02, 0.0)) * 0.65 + vnoise(UV * 7.0 - seed * 5.0 - vec2(0.0, TIME * 0.03)) * 0.35;
	float a = shape * smoothstep(0.25, 0.65, n) * opacity * amount * 0.9;
	// Soft where the sheet meets the ground or other geometry.
	float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
	vec4 scene = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -scene.z / scene.w, z = -(VIEW_MATRIX * vec4(world, 1.0)).z;
	a *= clamp((scene_z - z) / 3.0, 0.0, 1.0);
	// Fade near the camera and far away.
	a *= smoothstep(4.0, 14.0, z) * (1.0 - smoothstep(110.0, 160.0, z));
	ALBEDO = mist_color;
	ALPHA = clamp(a, 0.0, 0.45); // (overlapping sheets added up to a wall)
}
