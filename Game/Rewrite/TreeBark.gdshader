// Bark, branches and trunks of the 3D trees in the rewrite world (round 30): the woodland pack's bark material
// (albedo texture × colour, pixel-sharp) as a shader so it can hand over to the impostors with the leaves
// (TreeFade.gdshaderinc). The valley game keeps the imported material.
shader_type spatial;
render_mode cull_back, diffuse_burley, specular_disabled;
uniform sampler2D albedo_tex : source_color, filter_nearest_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform bool has_texture = true;
#include "res://Game/Rewrite/TreeFade.gdshaderinc"
varying vec3 world_position;
void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	if (tree_fade_out(world_position, FRAGCOORD.xy, tree_ranges.x)) discard;
	vec4 c = (has_texture ? texture(albedo_tex, UV) : vec4(1.0)) * tint;
	ALBEDO = c.rgb;
	ROUGHNESS = 0.9;
}
