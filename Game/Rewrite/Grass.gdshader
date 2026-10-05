// Grass tufts and reeds in the rewrite world: the valley tuft's vertex colours (sRGB), lit on both faces, leaf
// translucency against the sun, and a light breeze bending the tips (more with the wind setting).
shader_type spatial;
render_mode cull_disabled, specular_disabled, diffuse_burley;

global uniform float leaf_translucency;
uniform float breeze = 1.0;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float w = clamp(VERTEX.y / 0.5, 0.0, 1.0);
	w *= w;
	float phase = origin.x * 0.21 + origin.z * 0.17;
	VERTEX.x += sin(TIME * 1.7 + phase) * 0.05 * breeze * w;
	VERTEX.z += sin(TIME * 1.3 + phase * 1.3) * 0.035 * breeze * w;
}

void fragment() {
	vec3 c = COLOR.rgb;
	ALBEDO = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
	ROUGHNESS = 1.0;
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
	BACKLIGHT = ALBEDO * vec3(0.9, 1.08, 0.55) * leaf_translucency;
}
