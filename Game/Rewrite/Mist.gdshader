// Ground mist patches (volumetric fog volume around the player): low wisps hugging the ground, densest a couple of
// metres up and thinning within ~8 m, in patches that stay put in the world and drift slowly with the wind.
shader_type fog;

uniform float amount = 0.0;        // 0..1 from time of day, biome, rain and nearby water (Atmosphere.cs)
uniform float ground = 0.0;        // ground height under the player (m)
uniform vec2 origin = vec2(0.0);   // floating-origin offset: world xz of scene (0, 0)
uniform vec2 drift = vec2(0.0);    // wind drift (m)
uniform vec3 tint : source_color = vec3(0.86, 0.89, 0.9);

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void fog() {
	// Wrap the world position (keeps precision far from the origin; the pattern repeats every 8 km).
	vec2 xz = mod(WORLD_POSITION.xz + origin + drift, 8192.0);
	float patches = vnoise(xz / 90.0) * 0.6 + vnoise(xz / 31.0 + 7.3) * 0.3 + vnoise(xz / 9.0 + 2.1) * 0.1;
	patches = smoothstep(0.42, 0.75, patches);
	float h = WORLD_POSITION.y - ground;
	float layer = exp(-max(h + 1.0, 0.0) / 5.0);
	// Soft edges of the box so it never shows its shape.
	vec3 e = min(UVW, 1.0 - UVW);
	float edge = smoothstep(0.0, 0.25, min(e.x, e.z));
	DENSITY = amount * 0.022 * patches * layer * edge; // (0.09 hid everything beyond ~15 m: Tom, build 25)
	ALBEDO = tint;
}
