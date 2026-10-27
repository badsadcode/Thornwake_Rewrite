// Rain-mist gusts (D221; Tom and Noor's "Rain 2.1" §3: "occasional patches of soft, foggy, moisture-filled air drifting
// around the player during rainfall … wind-driven sheets of saturated air or shredded rain haze, rather than steam,
// smoke or ground fog"): a volumetric fog box around the player, lit like the rest of the haze. Sheets stretched along
// the wind and carried at its speed (much faster than the ground mist), shredded at their edges, in patches that come
// and go; low (the first ~14 m) and only near you. Atmosphere sets how much (heavy rain, gusts, cover).
shader_type fog;

uniform float amount = 0.0;        // 0 … 1
uniform float ground = 0.0;        // the ground under the player (scene y)
uniform vec2 origin = vec2(0.0);   // floating-origin offset: world xz of scene (0, 0)
uniform vec2 drift = vec2(0.0);    // how far the air has moved with the wind (m)
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform vec3 tint : source_color = vec3(0.86, 0.88, 0.9);
// the haze's own light (Atmosphere: the fog light colour): wet air glows pale like the haze around it — lit only by the
// sun and sky, under a rain deck it came out as a darkening veil
uniform vec3 glow : source_color = vec3(0.5);
uniform sampler3D noise3d : repeat_enable, filter_linear;

float tex(vec3 p, int c) { return textureLod(noise3d, p, 0.0)[c]; }

void fog() {
	// (no `return` in fog(): Godot rejects it, as in fragment())
	DENSITY = 0.0;
	ALBEDO = tint;
	if (amount > 0.001) {
		vec2 xz = mod(WORLD_POSITION.xz + origin, 8192.0) - drift;
		vec2 perp = vec2(-wind_dir.y, wind_dir.x);
		// in the wind's frame, stretched along it: long sheets, not round puffs
		float along = dot(xz, wind_dir), across = dot(xz, perp);
		float h = WORLD_POSITION.y - ground;
		float t = TIME;
		vec3 p = vec3(along / 3.2, h * 0.8, across);
		// patches: ~40 m sheets that form and dissolve over tens of seconds
		float patch = smoothstep(0.64, 0.72, tex(vec3(p.x / 45.0, p.y / 30.0 + t * 0.012, p.z / 30.0), 0)); // (sharper: separate sheets)
		if (patch > 0.0) {
			// shredded: finer, faster structure tearing the sheets into strands
			float shred = tex(vec3(p.x / 9.0 + t * 0.05, p.y / 7.0, p.z / 6.0), 2) * 0.6 + tex(vec3(p.x / 3.5, p.y / 3.0 - t * 0.08, p.z / 2.5), 1) * 0.4;
			shred = smoothstep(0.42, 0.62, shred);
			// (D224, Tom: "a little bit lower to the ground": mostly within ~3 m, thinning out by ~7 m)
			float layer = smoothstep(-1.0, 0.5, h) * (1.0 - smoothstep(2.0, 7.0, h));
			// only around the player: soft edges of the box
			vec3 e = min(UVW, 1.0 - UVW);
			float edge = smoothstep(0.0, 0.3, min(e.x, e.z));
			DENSITY = amount * 0.3 * patch * shred * layer * edge; // (0.05 hardly showed in the volumetric grid)
			// (D224: a slight colour variation from sheet to sheet — warmer, cooler, a little brighter or duller)
			float hue = tex(vec3(p.x / 70.0 + 0.4, 0.3 + t * 0.004, p.z / 50.0), 3);
			vec3 tintv = mix(vec3(0.93, 0.96, 1.04), vec3(1.05, 1.0, 0.94), smoothstep(0.35, 0.62, hue)) * mix(0.92, 1.08, smoothstep(0.4, 0.6, shred));
			ALBEDO = tint * tintv;
			EMISSION = glow * tintv * DENSITY * 4.0;
		}
	}
}
