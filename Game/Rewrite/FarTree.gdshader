// Distant tree card (0.4–6 km): a camera-facing quad (around the vertical axis) drawn with a simple pixel-stepped
// silhouette in the tree's crown colour: a stepped cone for conifers, a lumpy round crown on a trunk otherwise.
// Per instance (MultiMesh custom data): rgb = crown colour (linear), a = 1 for conifers.
shader_type spatial;
render_mode skip_vertex_transform, cull_disabled, diffuse_burley, specular_disabled;

global uniform float leaf_translucency;
global uniform vec4 sun_glow;
global uniform vec3 sun_dir_world;
#define TREE_NO_LOD_ROLE // (no detailed ↔ lighter hand-over here: no instance uniform)
#include "res://Game/Rewrite/TreeFade.gdshaderinc"
varying vec3 card_world;
varying vec3 crown;
varying float conifer;
varying float seed;
varying float steep;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }

void vertex() {
	crown = INSTANCE_CUSTOM.rgb;
	conifer = INSTANCE_CUSTOM.a;
	vec3 origin = MODEL_MATRIX[3].xyz;
	seed = fract(origin.x * 0.137 + origin.z * 0.291);
	float height = length(MODEL_MATRIX[1].xyz);
	float width = height * mix(0.62, 0.42, conifer);
	vec3 to_camera = CAMERA_POSITION_WORLD - origin;
	vec3 view_dir = normalize(to_camera);
	vec3 right = normalize(vec3(to_camera.z, 0.0, -to_camera.x) + vec3(1e-4, 0.0, 0.0));
	// Seen from above, an upright card would be a thin line: tilt it toward the camera and draw a round crown
	// (seen from high up, a forest is a carpet of crowns).
	steep = smoothstep(0.35, 0.8, view_dir.y);
	vec3 up = normalize(mix(vec3(0.0, 1.0, 0.0), cross(view_dir, right), steep));
	vec3 base = origin + vec3(0.0, height * 0.55, 0.0) * steep - up * (width * 0.5 * steep);
	float size = mix(height, width, steep);
	vec3 world = base + right * VERTEX.x * width + up * VERTEX.y * size;
	card_world = world;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	// Lighting: mostly up, a little toward the camera, so cards read as rounded crowns.
	NORMAL = normalize((VIEW_MATRIX * vec4(normalize(vec3(0.0, 1.0, 0.0) + normalize(to_camera) * 0.6), 0.0)).xyz);
}

void fragment() {
	if (tree_fade_in(card_world, FRAGCOORD.xy, tree_ranges.y)) discard; // with the tree field: only beyond the impostors
	// Pixel-stepped UV keeps the low-res look of the valley's pixel-textured trees.
	vec2 uv = floor(UV * 24.0) / 24.0;
	float x = abs(uv.x - 0.5) * 2.0, y = 1.0 - uv.y; // y: 0 at the ground, 1 at the top
	float inside;
	float trunk = step(x, 0.09) * step(y, 0.22);
	if (steep > 0.5) {
		// Crown from above: a lumpy disc.
		vec2 c = vec2(uv.x - 0.5, uv.y - 0.5) * 2.0;
		float lump = 0.86 + 0.12 * hash(floor(uv * 6.0) + seed * 7.0);
		inside = step(length(c), lump);
		trunk = 0.0;
		y = 0.3 - length(c) * 0.25; // darker at the rim: crowns shade each other
	} else if (conifer > 0.5) {
		float tiers = fract(y * 4.0 + seed);
		float edge = (1.0 - y) * (0.9 + 0.25 * tiers);
		inside = step(x, edge) * step(0.12, y);
	} else {
		vec2 c = vec2(x, (y - 0.6) * 1.35);
		float lump = 0.42 + 0.06 * hash(floor(uv * 6.0) + seed * 7.0);
		inside = step(length(c), lump);
	}
	float speckle = hash(floor(UV * 24.0) + seed * 13.0);
	// Matched to the near forest by eye: darker and less saturated, like foliage seen through distance haze.
	vec3 colour = crown * (0.42 + 0.3 * y + 0.15 * speckle);
	colour = mix(colour, vec3(dot(colour, vec3(0.33))), 0.25);
	colour *= mix(1.0, 0.72, steep); // seen from above, lit crowns sit among their own shadows
	if (inside < 0.5) { colour = vec3(0.18, 0.13, 0.08); }
	ALPHA = max(inside, trunk);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALBEDO = colour;
	BACKLIGHT = colour * vec3(0.9, 1.08, 0.55) * leaf_translucency * 0.6;
	{
		vec3 view_ray = normalize(card_world - CAMERA_POSITION_WORLD);
		float forward = pow(clamp(dot(view_ray, sun_dir_world), 0.0, 1.0), 5.0);
		EMISSION += colour * vec3(0.95, 1.1, 0.5) * sun_glow.rgb * forward * leaf_translucency * 0.32 * 0.6;
	}
	ROUGHNESS = 0.95;
}
