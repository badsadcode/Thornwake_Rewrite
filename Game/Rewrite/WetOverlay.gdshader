// Wet characters (D215; Noor's Weather 2.0 notes §9: "Alden and other players look progressively wet; clothing and
// equipment darken somewhat"): an overlay on the figure's parts that multiplies what is drawn under it — cloth and hair
// darker as they soak, skin barely (Character.ApplyWet sets darken per part and wet per figure).
shader_type spatial;
render_mode unshaded, blend_mul, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform float darken = 0.4; // at full wetness
instance uniform float wet = 0.0;

void fragment() {
	ALBEDO = vec3(1.0 - darken * clamp(wet, 0.0, 1.0));
}
