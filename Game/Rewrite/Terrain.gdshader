// Rewrite world terrain. Palette taken from the Harthwood valley game (sRGB values, converted to linear below).
// Vertex data: UV = world xz folded to a 4 km period (noise), UV2.x = height above sea level, UV2.y = terrain edit
// amount (0 untouched … 1 = 0.5 m or more dug/raised),
// COLOR = (forest cover, mountain share, beach, submerged bed: 0.5 + depth below water / 2 m).
shader_type spatial;
render_mode diffuse_burley;

uniform float snow_line = 2700.0;
uniform bool show_grid = false;
// x: surface wetness (rain darkens and glosses the ground), y: cosmetic puddles in flat hollows (Atmosphere.cs).
global uniform vec2 rain_wetness;
varying vec3 world_normal;

float hash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

void vertex() {
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 p = UV;
	float h = UV2.x;
	float forest = COLOR.r, mountain = COLOR.g, wet = COLOR.b, submerged = smoothstep(0.5, 0.8, COLOR.a);
	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	float n1 = vnoise(p * 0.012), n2 = vnoise(p * 0.21), n3 = vnoise(p * 1.7);

	vec3 grass = mix(vec3(0.337, 0.420, 0.247), vec3(0.420, 0.470, 0.300), n1);          // valley 566b3f .. 7b8059
	grass = mix(grass, vec3(0.500, 0.495, 0.330), smoothstep(0.6, 0.95, n2) * 0.35);       // dry patches
	vec3 woods = vec3(0.235, 0.300, 0.170);                                                // forest floor / far canopy
	vec3 c = mix(grass, woods, smoothstep(0.15, 0.75, forest) * 0.85);
	c = mix(c, vec3(0.470, 0.460, 0.350), smoothstep(1300.0, 2300.0, h) * 0.65);           // alpine meadow
	vec3 rock = mix(vec3(0.467, 0.463, 0.416), vec3(0.560, 0.545, 0.500), n2);             // valley 77766a
	c = mix(c, rock, smoothstep(0.30, 0.52, slope + (n2 - 0.5) * 0.10 + mountain * 0.06));
	vec3 sand = mix(vec3(0.800, 0.725, 0.520), vec3(0.700, 0.640, 0.470), n2);             // valley cbb984
	c = mix(c, sand, smoothstep(0.3, 0.65, wet + (n2 - 0.5) * 0.25) * (1.0 - smoothstep(0.25, 0.5, slope)));
	vec3 bed = mix(vec3(0.360, 0.340, 0.260), vec3(0.450, 0.420, 0.330), n2);              // gravel and silt under water
	// River and lake banks (not sea beaches): from the waterline to ~0.5 m above it (wide where the river has a low
	// beach), in patches of dark mud,
	// pale sand or gravel, and plain damp grass, varying along the shore every few tens of metres.
	float damp = smoothstep(0.24, 0.44, COLOR.a + (n3 - 0.5) * 0.08) * (1.0 - submerged) * (1.0 - wet) * (1.0 - smoothstep(0.45, 0.7, slope));
	float patch_kind = vnoise(p * 0.045 + vec2(17.0, 3.0));
	vec3 mud = vec3(0.30, 0.25, 0.18) * (0.9 + 0.2 * n3);
	vec3 shore_sand = mix(vec3(0.66, 0.60, 0.45), vec3(0.55, 0.52, 0.44), n2);
	vec3 gravel = mix(vec3(0.52, 0.50, 0.46), vec3(0.62, 0.60, 0.55), hash(floor(p * 5.0))) * (0.9 + 0.2 * n3);
	vec3 shore = mix(c * vec3(0.62, 0.6, 0.55) + vec3(0.03, 0.025, 0.015), mud, smoothstep(0.26, 0.36, patch_kind));
	shore = mix(shore, gravel, smoothstep(0.46, 0.56, patch_kind));
	shore = mix(shore, shore_sand, smoothstep(0.62, 0.72, patch_kind));
	c = mix(c, shore, damp);
	// Ground the player dug or raised: bare, slightly lumpy soil.
	vec3 soil = mix(vec3(0.36, 0.28, 0.19), vec3(0.45, 0.36, 0.25), n3);
	c = mix(c, soil, smoothstep(0.05, 0.35, UV2.y + (n2 - 0.5) * 0.08));
	c = mix(c, bed, submerged);
	float snow = smoothstep(snow_line - 250.0 + (n1 - 0.5) * 600.0, snow_line + 150.0, h) * (1.0 - smoothstep(0.55, 0.8, slope));
	c = mix(c, vec3(0.93, 0.94, 0.96), snow);
	c *= 0.92 + 0.16 * n3;
	// Close-up detail (fades out by ~45 m): pixel-stepped speckle and small clumps at 6 cm texels, in the spirit
	// of the valley game's nearest-filtered pixel textures. Stronger on grass, finer and greyer on rock.
	float near = 1.0 - smoothstep(15.0, 45.0, length(VERTEX));
	if (near > 0.0) {
		vec2 texel = floor(p * 16.0);
		float speck = hash(texel);
		float clump = vnoise(p * 1.6) * 0.6 + vnoise(p * 4.3) * 0.4;
		float rockiness = smoothstep(0.30, 0.52, slope);
		vec3 grass_detail = mix(vec3(0.94), vec3(1.06, 1.05, 0.96), step(0.72, speck)) * mix(0.95, 1.04, clump);
		grass_detail = mix(grass_detail, vec3(0.86, 0.83, 0.74), step(0.97, speck) * (1.0 - forest * 0.5)); // bare soil flecks
		vec3 rock_detail = vec3(0.93 + 0.12 * hash(floor(p * 12.0)));
		vec3 detail = mix(grass_detail, rock_detail, rockiness);
		detail = mix(detail, vec3(0.92 + 0.12 * speck), max(snow, wet)); // calmer on snow and sand
		c *= mix(vec3(1.0), detail, near);
	}
	if (show_grid) {
		vec2 g = abs(fract(p / 64.0) - 0.5);
		if (max(g.x, g.y) > 0.495) c = vec3(0.1, 0.8, 0.8);
	}
	// Rain: the whole world gets darker and glossier, and small puddles shine in flat spots (purely visual; real
	// standing water is the local simulation's).
	float wetness = rain_wetness.x * (1.0 - snow);
	float flat_ground = 1.0 - smoothstep(0.03, 0.1, slope);
	float puddle_noise = vnoise(p * 0.21) * 0.65 + vnoise(p * 0.93 + 7.1) * 0.35;
	float puddle = smoothstep(0.64, 0.7, puddle_noise) * flat_ground * rain_wetness.y * (1.0 - submerged) * (1.0 - snow) * (1.0 - 0.6 * forest);
	c *= mix(1.0, 0.7, wetness);
	ALBEDO = mix(pow(c, vec3(2.2)), vec3(0.025, 0.03, 0.035), puddle * 0.85);
	ROUGHNESS = mix(mix(mix(0.95, 0.55, snow), 0.45, wetness * 0.7), 0.04, puddle);
	SPECULAR = mix(0.5, 0.9, puddle);
}
