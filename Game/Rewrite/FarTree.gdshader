// Distant tree card (0.4–6 km): a camera-facing quad (around the vertical axis) drawn with a simple pixel-stepped
// silhouette in the tree's crown colour: a stepped cone for conifers, a lumpy round crown on a trunk otherwise.
// Per instance (MultiMesh custom data): rgb = crown colour (linear), a = 1 for conifers.
shader_type spatial;
render_mode skip_vertex_transform, cull_disabled, diffuse_burley, specular_disabled;

varying vec3 crown;
varying float conifer;
varying float seed;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }

void vertex() {
	crown = INSTANCE_CUSTOM.rgb;
	conifer = INSTANCE_CUSTOM.a;
	vec3 origin = MODEL_MATRIX[3].xyz;
	seed = fract(origin.x * 0.137 + origin.z * 0.291);
	float height = length(MODEL_MATRIX[1].xyz);
	float width = height * mix(0.62, 0.42, conifer);
	vec3 to_camera = CAMERA_POSITION_WORLD - origin;
	vec3 right = normalize(vec3(to_camera.z, 0.0, -to_camera.x) + vec3(1e-4, 0.0, 0.0));
	vec3 world = origin + right * VERTEX.x * width + vec3(0.0, VERTEX.y * height, 0.0);
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	// Lighting: mostly up, a little toward the camera, so cards read as rounded crowns.
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(vec3(0.0, 1.0, 0.0) + normalize(to_camera) * 0.6), 0.0)).xyz);
}

void fragment() {
	// Pixel-stepped UV keeps the low-res look of the valley's pixel-textured trees.
	vec2 uv = floor(UV * 24.0) / 24.0;
	float x = abs(uv.x - 0.5) * 2.0, y = 1.0 - uv.y; // y: 0 at the ground, 1 at the top
	float inside;
	float trunk = step(x, 0.09) * step(y, 0.22);
	if (conifer > 0.5) {
		float tiers = fract(y * 4.0 + seed);
		float edge = (1.0 - y) * (0.9 + 0.25 * tiers);
		inside = step(x, edge) * step(0.12, y);
	} else {
		vec2 c = vec2(x, (y - 0.6) * 1.35);
		float lump = 0.42 + 0.06 * hash(floor(uv * 6.0) + seed * 7.0);
		inside = step(length(c), lump);
	}
	float speckle = hash(floor(UV * 24.0) + seed * 13.0);
	vec3 colour = crown * (0.7 + 0.5 * y + 0.25 * speckle);
	if (inside < 0.5) { colour = vec3(0.18, 0.13, 0.08); }
	ALPHA = max(inside, trunk);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALBEDO = colour;
	ROUGHNESS = 0.95;
}
