// Ground mist (volumetric fog volume around the player), round 21 (Tom: "it had the same density everywhere… maybe some
// rolling slow turbulence"). Three scales, all from the tiling 3D cloud noise (CloudNoise3D):
//   banks   — ~300 m patches of mist and clear air drifting with the wind, slowly forming and dissolving;
//   tops    — each bank has its own depth (a few metres to ~18 m) and a lumpy top, never one flat layer;
//   billows — ~25 m rolls inside the mist, warped by a slow larger flow, drifting a little faster than the banks
//             and rising and sinking, so the mist churns and rolls; fine detail frays the thin edges into wisps.
// Positions wrap every 8 km (precision far from the origin); the texture tiles, so the pattern stays seamless.
shader_type fog;

uniform float amount = 0.0;        // 0..1 from time of day, biome, rain and nearby water (Atmosphere.cs)
uniform float ground = 0.0;        // ground height under the player (m)
uniform vec2 origin = vec2(0.0);   // floating-origin offset: world xz of scene (0, 0)
uniform vec2 drift = vec2(0.0);    // wind drift of the banks (m)
uniform vec3 tint : source_color = vec3(0.86, 0.89, 0.9);
uniform sampler3D noise3d : repeat_enable, filter_linear;

float tex(vec3 p, int c) { return textureLod(noise3d, p, 0.0)[c]; }

void fog() {
	vec2 xz = mod(WORLD_POSITION.xz + origin, 8192.0);
	float h = WORLD_POSITION.y - ground;
	float t = TIME;
	// Banks: where there is mist at all (drifting with the wind, slowly changing shape).
	vec2 b = xz + drift;
	float bank = tex(vec3(b.x / 310.0, t * 0.0006, b.y / 310.0), 0) * 0.75 + tex(vec3(b.x / 120.0 + 0.3, 0.5 + t * 0.001, b.y / 120.0), 0) * 0.25;
	bank = smoothstep(0.38, 0.72, bank);
	DENSITY = 0.0;
	ALBEDO = tint;
	if (bank > 0.0) {
		// Billows: a slow large flow warps the rolls, which drift faster than the banks (near-ground shear) and rise and
		// sink over time.
		vec3 p = vec3(xz.x + drift.x * 1.6, h, xz.y + drift.y * 1.6);
		vec3 flow = vec3(tex(p / 90.0 + vec3(0.0, t * 0.004, 0.0), 1), tex(p / 90.0 + vec3(0.37, 0.61 - t * 0.003, 0.11), 2), tex(p / 90.0 + vec3(0.71, 0.23, 0.53 + t * 0.004), 3)) - 0.5;
		vec3 q = p + flow * vec3(22.0, 6.0, 22.0);
		float billow = tex(q / 26.0 + vec3(0.0, -t * 0.006, 0.0), 0);
		float detail = tex(q / 8.0 + vec3(t * 0.01, 0.0, 0.0), 2);
		// Depth of the mist here: per bank, with a lumpy top (the billows push it up and down).
		float top = mix(3.0, 18.0, tex(vec3(b.x / 200.0 + 0.5, 0.2, b.y / 200.0 + 0.5), 1)) * (0.6 + 0.8 * billow);
		float layer = 1.0 - smoothstep(top * 0.35, top, h);
		layer *= smoothstep(-6.0, -1.0, h); // (the volume follows the player's ground; nothing far below it)
		// Density: billows inside the bank, frayed into wisps where it thins out.
		float d = bank * layer;
		d = clamp(d * (0.35 + 1.3 * billow) - (1.0 - detail) * 0.35 * (1.0 - d), 0.0, 1.0);
		// Soft edges of the box so it never shows its shape.
		vec3 e = min(UVW, 1.0 - UVW);
		float edge = smoothstep(0.0, 0.25, min(e.x, e.z));
		DENSITY = amount * 0.04 * d * edge;
	}
}
