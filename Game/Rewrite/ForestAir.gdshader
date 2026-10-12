// Atmosphere 2.0 M2, forest rays candidate A ("volumetric forest air"): a thin haze under the canopy around the camera.
// The volumetric fog lights it with the sun's shadow map, so the sun shows as shafts through the gaps in the crowns.
// Density from the canopy around the player (Atmosphere.cs); thickest near the ground, gone above the crowns, soft
// toward the edges of the box, with slow 3D noise so it is not an even grey.
shader_type fog;

uniform float density = 0.0;
uniform float ground = 0.0;      // world Y of the ground under the camera
uniform float top = 28.0;        // metres above the ground where the haze ends (about the crown tops)
uniform vec3 albedo : source_color = vec3(0.92, 0.9, 0.86);
uniform sampler3D noise3d : repeat_enable;
uniform vec3 drift = vec3(0.0);

void fog() {
	float h = WORLD_POSITION.y - ground;
	float shape = smoothstep(-3.0, 1.5, h) * (1.0 - smoothstep(top * 0.55, top, h));
	vec3 e = abs(UVW - 0.5) * 2.0;
	float edge = 1.0 - smoothstep(0.6, 1.0, max(e.x, e.z));
	float n = texture(noise3d, (WORLD_POSITION + drift) * 0.015).r;
	DENSITY = density * shape * edge * mix(0.55, 1.45, n);
	ALBEDO = albedo;
}
