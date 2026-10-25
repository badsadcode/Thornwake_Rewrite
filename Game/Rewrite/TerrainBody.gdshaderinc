// Rewrite world terrain. Palette taken from the Harthwood valley game (sRGB values, converted to linear below).
// Vertex data: UV = world xz folded to a 4 km period (noise), UV2.x = grass cover from the plant fields (round 22;
// the height above sea comes from the world position), UV2.y = terrain edit
// amount (0 untouched … 1 = 0.5 m or more dug/raised),
// COLOR = (forest cover, mountain share, beach, submerged bed: 0.5 + depth below water / 2 m).

uniform float snow_line = 2700.0;
uniform bool show_grid = false;
// x: surface wetness (rain darkens and glosses the ground), y: cosmetic puddles in flat hollows (Atmosphere.cs).
global uniform vec2 rain_wetness;
global uniform vec4 rain_now; // y: the wet look by material (D212); z: the cover map is valid (D214)
// Dry ground under cover (D214): the rain's cover map around the camera (Atmosphere, 1 m cells, 40 x 40, toroidal):
// r = the first surface rain lands on (scene y), g, b = the cell it holds, a > 0.5 written.
global uniform sampler2D rain_cover : filter_nearest, repeat_enable;
float covered_at(vec2 xz, float y) {
	vec2 c = floor(xz);
	vec4 v = texelFetch(rain_cover, ivec2(mod(c, 40.0)), 0);
	return v.a > 0.5 && v.g == c.x && v.b == c.y && v.r > y + 0.3 ? 1.0 : 0.0;
}
global uniform vec4 wind;
global uniform float wind_waves; // the setting Wind waves on grass (D163)
#include "res://Game/Rewrite/WindSway.gdshaderinc"
varying vec3 world_normal;
varying vec4 biome_a; // biome grass colour, snow cover (D42)
varying vec4 biome_b; // biome dry-ground colour, plant cover
varying vec4 biome_c; // r: packed map colour, g: rock looks (lo + 8 hi), b: weight of hi, a: rock exposure
varying flat vec2 rock_pair; // rock looks lo, hi (flat: indices must not interpolate)
varying flat float map_packed;
varying vec4 biome_d; // biome rock colour, wetness
uniform sampler2DArray rock_tex : source_color, filter_nearest_mipmap, repeat_enable; // rock looks (RockTextures order)
uniform float biome_debug = 0.0; // 1: tint the ground with the biome map colours (F4)
// Ground style (fx.ground_textures): 0 current, 1 Noor's pixel-art textures (GroundTextures.cs order). Sampled as raw
// sRGB like the palette here; ground_mean = each layer's mean colour, so a texture can be detail over the biome colour.
uniform int ground_style = 0;
uniform float sea_level = 0.0;
uniform float flora_tint = 1.0; // ground under the plant patches: richer where grass is dense (0: classic grass)
varying float world_height;
// (D181) The scene xz of this point; with terrain_fold (the scene origin's world xz folded to 65,536 m, Atmosphere) the
// fragment folds its own world position. The mesh UVs folded per vertex to 4,096 m: the row of triangles across a fold
// line ran from ~4,092 back to 0 and squeezed the whole ground pattern into one long stretched strip, every 4 km in x and
// z (Tom's dump 133310, x = −204,802, 2 m from −204,800 = 50 × 4,096); and the ground's patches (not periodic) met in a
// straight seam there. Per pixel there is no strip; at 65,536 m a seam is 16× rarer, floats still exact to < 1 cm.
varying vec2 scene_xz;
global uniform vec2 terrain_fold;
#ifdef AIR_SHADOW
// Far mountain shadows (D154, Noor's notes through Tom): the sun's shadow map reaches ~450 m (3 km with air rays), so
// beyond it mountains did not shade each other and whole ranges were lit evenly. The air map (WorldLab.AirRays, 20 km)
// says where a mountain hides the sun: that ground loses the sun's direct light (light() below), the sky's light stays,
// so shaded slopes keep their darkness without going black.
global uniform sampler2D air_map : filter_linear, repeat_disable;
global uniform vec4 air_frame; // xy: the map's centre in scene coordinates, z: size (m), w: how much of the sun a far shadow takes
varying float air_sun;
#endif
uniform sampler2DArray ground_tex : filter_nearest_mipmap, repeat_enable;
// The same textures filtered smoothly: heights for the relief (nearest texels would give stair-step bumps).
uniform sampler2DArray ground_tex_lin : filter_linear_mipmap, repeat_enable;
// Round 25 (Tom: "biome = one ground texture… mix some dirt/soil with grass… some tint on patches… no materials on
// terrain"), both 0 = build 33, Noor ground style only:
uniform float ground_variety = 1.0; // bare-ground patches in the biome's character + warm/cool tint patches
uniform float ground_relief = 1.0;  // close-up bump relief, per-material roughness and darker crevices
uniform float ground_parallax = 1.0; // round 26: close-up parallax depth from the textures (0 = build 34)
uniform vec3 ground_mean[16];
const int G_GRASS = 0, G_DIRT = 1, G_SAND = 2, G_ROCK = 3, G_RIVER_BED = 4, G_SEA_BED = 5, G_FOREST = 6, G_GRAVEL = 7;
const int G_MUD = 8, G_WET_MUD = 9, G_MOSS = 10, G_SCREE = 11, G_SNOW = 12, G_PEAT = 13;

// (D180: lattice cells wrapped to 0–1000 — the folded UVs reach 4096 m and the finer noises cells up to ~7,000, where
// float rounding gave neighbouring cells different values for the same corner: the noise and every ground colour built
// on it broke along straight lines, Tom's dumps 001338 / 001407. Cells below 1000 hash exactly as before.)
float hash(vec2 p) { p = mod(p, 1000.0); vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }
float vnoise(vec2 p) {
	vec2 i = floor(p); vec2 f = fract(p); vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// A ground texture at ~8 m per tile, two differently rotated and scaled copies blended by slow noise so the tiles
// do not repeat visibly over open ground.
vec3 gtex(int layer, vec2 p) {
	vec2 u1 = p / 8.0;
	vec2 u2 = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * p / 7.1 + vec2(0.37, 0.71);
	vec3 a = texture(ground_tex, vec3(u1, float(layer))).rgb;
	vec3 b = texture(ground_tex, vec3(u2, float(layer))).rgb;
	return mix(a, b, smoothstep(0.35, 0.65, vnoise(p * 0.045 + vec2(float(layer) * 7.3, 1.9))));
}
// The texture as detail over a biome colour: its light and shade around 1 (the layer's mean divided out), with only a
// hint of its own hues (full per-channel ratios turned Noor's yellow/green grass into confetti on other biomes), and
// its contrast eased.
vec3 gdetail(int layer, vec2 p) {
	vec3 t = gtex(layer, p), m = max(ground_mean[layer], vec3(0.02));
	float shade = dot(t, vec3(0.3, 0.55, 0.15)) / max(dot(m, vec3(0.3, 0.55, 0.15)), 0.02);
	vec3 d = mix(vec3(shade), t / m, 0.2);
	return 1.0 + (d - 1.0) * 0.7;
}

// Height of a ground texture around 0 (its brightness against the layer's mean), sampled like gtex.
float gheight(int layer, vec2 p) {
	vec2 u1 = p / 8.0;
	vec2 u2 = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * p / 7.1 + vec2(0.37, 0.71);
	float m = max(dot(ground_mean[layer], vec3(0.333)), 0.02);
	float a = dot(texture(ground_tex_lin, vec3(u1, float(layer))).rgb, vec3(0.333));
	float b = dot(texture(ground_tex_lin, vec3(u2, float(layer))).rgb, vec3(0.333));
	return mix(a, b, smoothstep(0.35, 0.65, vnoise(p * 0.045 + vec2(float(layer) * 7.3, 1.9)))) / m - 1.0;
}

// gheight at an explicit mip level (for loops that may stop at different steps per pixel).
float gheight_lod(int layer, vec2 p, float lod) {
	vec2 u1 = p / 8.0;
	vec2 u2 = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8)) * p / 7.1 + vec2(0.37, 0.71);
	float m = max(dot(ground_mean[layer], vec3(0.333)), 0.02);
	float a = dot(textureLod(ground_tex_lin, vec3(u1, float(layer)), lod).rgb, vec3(0.333));
	float b = dot(textureLod(ground_tex_lin, vec3(u2, float(layer)), lod).rgb, vec3(0.333));
	return mix(a, b, smoothstep(0.35, 0.65, vnoise(p * 0.045 + vec2(float(layer) * 7.3, 1.9)))) / m - 1.0;
}

void vertex() {
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	world_height = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
	scene_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
	biome_a = CUSTOM0; biome_b = CUSTOM1; biome_c = CUSTOM2; biome_d = CUSTOM3;
	rock_pair = vec2(mod(CUSTOM2.g, 8.0), floor(CUSTOM2.g / 8.0 + 0.01));
	map_packed = CUSTOM2.r;
}

void fragment() {
	vec2 p = mod(scene_xz + terrain_fold, 65536.0); // (was UV: folded per vertex to 4,096 m, D181)
	float h = world_height - sea_level;
	float cover = UV2.x;
	float forest = COLOR.r, mountain = COLOR.g, wet = COLOR.b, submerged = smoothstep(0.5, 0.8, COLOR.a);
	float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0);
	// Ground parallax (round 26, Tom: "parallax on ground — cheap depth"): within ~30 m on gentle, dry ground, the
	// textures' dark parts sink a few centimetres — the lookup steps along the view ray until it meets the height
	// field (grass, or soil where the variety pass will lay bare ground), and everything below reads the shifted spot.
	if (ground_style == 1 && ground_parallax > 0.0) {
		float fade = (1.0 - smoothstep(12.0, 30.0, length(VERTEX))) * (1.0 - smoothstep(0.2, 0.4, slope))
			* (1.0 - smoothstep(0.45, 0.6, COLOR.a));
		// (mip level from the unshifted lookup, outside the per-pixel branch; the loop samples with explicit levels)
		vec2 dp = max(abs(dFdx(p)), abs(dFdy(p)));
		float lod = max(0.0, log2(max(dp.x, dp.y) * 256.0 / 8.0));
		if (fade > 0.001) {
			vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz); // toward the camera, world space
			vec2 shift = -v.xz / max(v.y, 0.25) * 0.05 * ground_parallax * fade; // at the deepest layer (5 cm)
			float bare0 = smoothstep(0.62, 0.76, vnoise(p * 0.028 + vec2(3.0, 7.0)) * 0.5 + vnoise(p * 0.13 + vec2(8.0, 1.0)) * 0.3
				+ vnoise(p * 1.7) * 0.2 + (0.55 - cover) * 0.35 + (0.7 - biome_b.a) * 0.35) * ground_variety;
			const int LAYERS = 8;
			vec2 q = p;
			for (int i = 0; i <= LAYERS; i++) {
				float layer = float(i) / float(LAYERS);
				q = p + shift * layer;
				float gh = mix(gheight_lod(G_GRASS, q, lod), gheight_lod(G_DIRT, q, lod), bare0);
				if (layer >= 1.0 - clamp(0.5 + gh * 0.8, 0.0, 1.0)) break; // reached the surface
			}
			p = q;
		}
	}
	float n1 = vnoise(p * 0.012), n2 = vnoise(p * 0.21), n3 = vnoise(p * 1.7);
	bool noor = ground_style == 1;
	float dist = length(VERTEX);
	float g_near = 1.0 - smoothstep(50.0, 200.0, dist);   // Noor's materials up close, the usual colours far away
	float g_detail = 1.0 - smoothstep(150.0, 600.0, dist); // pattern over the biome colours (mipmaps average it anyway)

	// Ground colours from the biome (temperate: valley 566b3f .. 7b8059 as before).
	vec3 grass = mix(biome_a.rgb, biome_b.rgb, n1);
	grass = mix(grass, biome_b.rgb * vec3(1.08, 1.04, 0.95), smoothstep(0.6, 0.95, n2) * 0.35); // dry patches
	// Sparse plant cover (deserts, cold deserts, tundra): bare, dry ground shows through in patches.
	grass = mix(biome_b.rgb * vec3(1.05, 1.0, 0.92), grass, smoothstep(0.0, 1.0, biome_b.a * 1.3 + (n2 - 0.5) * 0.5));
	// Plant patches (round 22): dense grass shades and enriches the ground under it, sparse ground is a little drier,
	// so the patch pattern reads to the horizon (beyond the grass sprites) and lines up with them up close.
	grass = mix(grass, mix(grass * vec3(1.03, 1.01, 0.94), biome_a.rgb * vec3(0.84, 0.9, 0.8), smoothstep(0.2, 0.9, cover)),
		0.4 * flora_tint * biome_b.a);
	// Ground variety (round 25): bare patches and tint patches, so a biome is not one ground everywhere.
	float bare = 0.0;
	if (noor && ground_variety > 0.0) {
		// Warm and cool patches tens of metres across (sun-dried and lusher grass), subtle.
		float hue = vnoise(p * 0.017 + vec2(11.0, 5.0)) * 0.7 + vnoise(p * 0.06 + vec2(2.0, 9.0)) * 0.3;
		grass *= mix(vec3(1.0), mix(vec3(0.9, 1.0, 0.95), vec3(1.08, 1.03, 0.86), smoothstep(0.3, 0.7, hue)), 0.7 * ground_variety);
		// Bare ground where the plants thin out (the plant fields' cover and the biome's plant amount), in patches
		// from a few metres to ~40 m with ragged edges; not on cliffs (rock) or under water.
		float patches = vnoise(p * 0.028 + vec2(3.0, 7.0)) * 0.5 + vnoise(p * 0.13 + vec2(8.0, 1.0)) * 0.3 + n3 * 0.2;
		bare = smoothstep(0.62, 0.76, patches + (0.55 - cover) * 0.35 + (0.7 - biome_b.a) * 0.35)
			* ground_variety * (1.0 - smoothstep(0.35, 0.5, slope)) * (1.0 - submerged);
		{ // (no per-pixel branch: textures and derivatives must run in uniform control flow)
			// The biome's character picks the bare ground: soil, gravel up in cold mountains, sand where it is dry,
			// moss where it is wet; tinted toward the biome's dry colour.
			float dryness = 1.0 - clamp(biome_b.a, 0.0, 1.0);
			float cold = clamp(mountain * 1.4 + biome_a.a * 1.5, 0.0, 1.0);
			float w_gravel = smoothstep(0.35, 0.8, cold) * 0.75, w_sand = smoothstep(0.55, 0.95, dryness) * 0.55;
			float w_moss = smoothstep(0.3, 0.7, biome_d.a) * 0.6;
			// Muted earthy colours (the raw textures with the biome tint came out rust-orange — first render), with
			// the textures as light and shade only, like the grass and sand above.
			vec3 base = mix(vec3(0.40, 0.33, 0.25), biome_b.rgb * vec3(0.8, 0.76, 0.7), 0.3);
			vec3 detail = gdetail(G_DIRT, p);
			base = mix(base, vec3(0.50, 0.48, 0.45), w_gravel); detail = mix(detail, gdetail(G_GRAVEL, p), w_gravel);
			base = mix(base, vec3(0.70, 0.63, 0.48), w_sand);   detail = mix(detail, gdetail(G_SAND, p), w_sand);
			base = mix(base, vec3(0.28, 0.33, 0.19), w_moss);   detail = mix(detail, gdetail(G_MOSS, p), w_moss);
			vec3 b = base * mix(vec3(1.0), detail, 0.9) * (0.92 + 0.16 * n3);
			grass = mix(grass, b, bare);
		}
	}
	// Round 27: gusts sweeping the grass show as paler moving patches on the ground's grass colour too, so the
	// waves carry on beyond the grass sprites (simulated weather only).
	// (wind_waves: the setting Wind waves on grass, 1 = build 86; Tom read the bands sweeping over a meadow as moving fog)
	if (wind_sim()) grass *= 1.0 + vec3(0.075, 0.07, 0.035) * wind_waves * wind_gust_global(p) * min(1.0, wind_field.y) * smoothstep(0.1, 0.6, cover) * (1.0 - bare);
	vec3 woods = biome_a.rgb * vec3(0.70, 0.71, 0.69);                                     // forest floor / far canopy
	if (noor) {
		grass *= mix(vec3(1.0), gdetail(G_GRASS, p), 0.85 * g_detail * (1.0 - bare)); // (bare patches have their own)
		// Forest floor: Noor's leaf litter, a little of the biome's colour kept.
		woods = mix(woods, gtex(G_FOREST, p) * mix(vec3(1.0), woods / max(ground_mean[G_FOREST], vec3(0.02)), 0.35), 0.9 * g_near);
	}
	vec3 c = mix(grass, woods, smoothstep(0.15, 0.75, forest) * 0.85);
	c = mix(c, vec3(0.470, 0.460, 0.350), smoothstep(1300.0, 2300.0, h) * 0.65 * min(1.0, biome_b.a)); // alpine meadow (where plants grow)
	vec3 rock = mix(biome_d.rgb, biome_d.rgb * vec3(1.2, 1.18, 1.2), n2);                   // valley 77766a (biome rock)
	float rock_relief = 0.0; // brightness of the rock texture around 1: stones show first at the rock's ragged edge
	// All bare rock (cliffs, crags, rocky ground) uses the bitmap rock texture, projected from the two sides and from
	// above by the surface direction (top-down UVs alone would stretch into streaks on steep faces).
	{
		vec3 wn = pow(abs(world_normal), vec3(4.0));
		wn /= wn.x + wn.y + wn.z;
		// Strata wander up and down along the face (no straight bands repeating every tile).
		float hs = h + (vnoise(vec2(p.x + p.y, 0.0) * 0.012) - 0.5) * 9.0 + (vnoise(vec2(p.x - p.y, 3.0) * 0.05) - 0.5) * 2.5;
		vec2 ux = vec2(p.y, -hs) / 10.0, uz = vec2(p.x + 3.7, -hs) / 10.0, uy = p / 10.0;
		vec3 lo = texture(rock_tex, vec3(ux, rock_pair.x)).rgb * wn.x + texture(rock_tex, vec3(uz, rock_pair.x)).rgb * wn.z + texture(rock_tex, vec3(uy, rock_pair.x)).rgb * wn.y;
		vec3 side = lo;
		if (rock_pair.y != rock_pair.x && biome_c.b > 0.01) {
			vec3 hi = texture(rock_tex, vec3(ux, rock_pair.y)).rgb * wn.x + texture(rock_tex, vec3(uz, rock_pair.y)).rgb * wn.z + texture(rock_tex, vec3(uy, rock_pair.y)).rgb * wn.y;
			side = mix(lo, hi, smoothstep(0.3, 0.7, biome_c.b + (vnoise(p * 0.3) - 0.5) * 0.3));
		}
		side /= 0.5;
		rock_relief = dot(side, vec3(0.333)) - 1.0;
		rock = biome_d.rgb * side * (0.9 + 0.2 * vnoise(vec2(p.x + p.y, h) * 0.05));
	}
	// Noor: rocky ground on gentle slopes uses Noor's cobbled rock in the biome's rock colour; cliffs keep theirs.
	if (noor) rock = mix(rock, gtex(G_ROCK, p) * biome_d.rgb / max(dot(ground_mean[G_ROCK], vec3(0.333)), 0.02),
		(1.0 - smoothstep(0.3, 0.5, slope)) * 0.85 * g_near);
	// Bare rock: on steep slopes everywhere, and on gentler ground in rocky biomes (patchy).
	float exposure = biome_c.a * (0.25 + 0.75 * smoothstep(0.35, 0.75, n2 + (n1 - 0.5) * 0.4));
	// The edge follows the stones of the rock texture and a few-metre noise, not the triangles (the rock amount is
	// per vertex: a plain threshold drew straight-edged patches, which read as blocky wet ground on coasts).
	float rocky = smoothstep(0.30, 0.52, slope + (n2 - 0.5) * 0.10 + mountain * 0.06 + exposure * 0.32
		+ rock_relief * 0.16 + (vnoise(p * 0.27 + 4.1) - 0.5) * 0.12);
	c = mix(c, rock, rocky);
	// Noor: scree on the steeper slopes of mountains, below the bare rock.
	if (noor) c = mix(c, gtex(G_SCREE, p) * biome_d.rgb / max(dot(ground_mean[G_SCREE], vec3(0.333)), 0.02),
		smoothstep(0.2, 0.5, mountain) * smoothstep(0.18, 0.28, slope + (n2 - 0.5) * 0.12) * (1.0 - smoothstep(0.34, 0.46, slope)) * 0.8 * g_near);
	// Wet biomes (swamps): darker, muddier ground.
	c = mix(c, c * vec3(0.72, 0.7, 0.62), biome_d.a * smoothstep(0.3, 0.7, n2 + 0.2));
	vec3 sand = mix(vec3(0.800, 0.725, 0.520), vec3(0.700, 0.640, 0.470), n2);             // valley cbb984
	if (noor) sand *= mix(vec3(1.0), gdetail(G_SAND, p), 0.9 * g_detail);
	c = mix(c, sand, smoothstep(0.3, 0.65, wet + (n2 - 0.5) * 0.25) * (1.0 - smoothstep(0.25, 0.5, slope)));
	vec3 bed = mix(vec3(0.360, 0.340, 0.260), vec3(0.450, 0.420, 0.330), n2);              // gravel and silt under water
	// Noor: sea floor below sea level, river and lake beds above it.
	if (noor) bed = mix(bed, mix(gtex(G_RIVER_BED, p), gtex(G_SEA_BED, p), smoothstep(-0.2, -1.5, h)), 0.9 * g_near);
	// River and lake banks (not sea beaches): from the waterline to ~0.5 m above it (wide where the river has a low
	// beach), in patches of dark mud,
	// pale sand or gravel, and plain damp grass, varying along the shore every few tens of metres.
	// Wide, noisy threshold: COLOR.a is per vertex (1 m apart near the player), and a sharp step would show the
	// triangle grid as square patches on flat floodplains.
	float damp_noise = (n3 - 0.5) * 0.14 + (vnoise(p * 0.47 + 3.7) - 0.5) * 0.16;
	float damp = smoothstep(0.2, 0.5, COLOR.a + damp_noise) * (1.0 - submerged) * (1.0 - wet) * (1.0 - smoothstep(0.45, 0.7, slope));
	float patch_kind = vnoise(p * 0.045 + vec2(17.0, 3.0));
	vec3 mud = vec3(0.30, 0.25, 0.18) * (0.9 + 0.2 * n3);
	vec3 shore_sand = mix(vec3(0.66, 0.60, 0.45), vec3(0.55, 0.52, 0.44), n2);
	vec3 gravel = mix(vec3(0.52, 0.50, 0.46), vec3(0.62, 0.60, 0.55), hash(floor(p * 5.0))) * (0.9 + 0.2 * n3);
	vec3 moss = c * vec3(0.55, 0.62, 0.45);
	if (noor) {
		// Mud is wet mud near the waterline and in the rain (Noor's wet variant), dry mud higher up the bank.
		float soaked = max(rain_wetness.x, smoothstep(0.4, 0.55, COLOR.a + damp_noise));
		mud = mix(mud, mix(gtex(G_MUD, p), gtex(G_WET_MUD, p), soaked), g_near);
		gravel = mix(gravel, gtex(G_GRAVEL, p), g_near);
		shore_sand *= mix(vec3(1.0), gdetail(G_SAND, p), 0.8 * g_detail);
		moss = mix(moss, gtex(G_MOSS, p) * 0.85, g_near);
	}
	vec3 shore = mix(c * vec3(0.62, 0.6, 0.55) + vec3(0.03, 0.025, 0.015), mud, smoothstep(0.26, 0.36, patch_kind));
	shore = mix(shore, gravel, smoothstep(0.46, 0.56, patch_kind));
	shore = mix(shore, shore_sand, smoothstep(0.62, 0.72, patch_kind));
	// Wetlands: pool margins are dark mud and wet moss rather than sand and gravel.
	float wetland = smoothstep(0.45, 0.75, biome_d.a);
	shore = mix(shore, mix(mud * 0.8, moss, smoothstep(0.4, 0.7, patch_kind)), wetland);
	c = mix(c, shore, damp);
	// Ground the player dug or raised: bare, slightly lumpy soil.
	vec3 soil = mix(vec3(0.36, 0.28, 0.19), vec3(0.45, 0.36, 0.25), n3);
	if (noor) soil *= mix(vec3(1.0), gdetail(G_DIRT, p), 0.9 * g_detail);
	c = mix(c, soil, smoothstep(0.05, 0.35, UV2.y + (n2 - 0.5) * 0.08));
	// Wetland pool beds (swamps, marshes, fens, bayous): dark peat and silt with sunken leaves and twigs.
	if (wetland > 0.0) {
		vec3 peat = mix(vec3(0.15, 0.115, 0.075), vec3(0.24, 0.20, 0.13), n2) * (0.85 + 0.3 * n3);
		vec2 leaf_cell = floor(p * 3.0);
		float leaf = step(0.82, hash(leaf_cell)) * step(0.35, hash(floor(p * 9.0)));
		vec3 leaf_colour = mix(vec3(0.42, 0.26, 0.11), vec3(0.30, 0.30, 0.13), hash(leaf_cell + 7.0));
		peat = mix(peat, leaf_colour, leaf * 0.8);
		// Twigs: thin dark strokes along a per-cell direction.
		vec2 tc = floor(p * 0.8);
		float ang = hash(tc) * 6.2832;
		vec2 q = fract(p * 0.8) - 0.5;
		float along = dot(q, vec2(cos(ang), sin(ang))), across = dot(q, vec2(-sin(ang), cos(ang)));
		float twig = step(0.6, hash(tc + 3.0)) * step(abs(across), 0.018) * step(abs(along), 0.35);
		peat = mix(peat, vec3(0.09, 0.07, 0.05), twig);
		if (noor) peat = mix(peat, gtex(G_PEAT, p), g_near);
		bed = mix(bed, peat, wetland);
	}
	c = mix(c, bed, submerged);
	// Snow from the biome and the cold (temperature falls with height), patchy at its edge.
	// Snow does not hold on steep rock: faces break through (patchy edge).
	float snow = smoothstep(0.25, 0.75, biome_a.a + (n1 - 0.5) * 0.35) * (1.0 - smoothstep(0.42, 0.62, slope + (n2 - 0.5) * 0.12));
	vec3 snow_c = vec3(0.93, 0.94, 0.96);
	if (noor) snow_c *= mix(vec3(1.0), gdetail(G_SNOW, p), 0.7 * g_detail);
	c = mix(c, snow_c, snow);
	c *= 0.92 + 0.16 * n3;
	// Close-up detail (fades out by ~45 m): pixel-stepped speckle and small clumps at 6 cm texels, in the spirit
	// of the valley game's nearest-filtered pixel textures. Stronger on grass, finer and greyer on rock.
	float near = (1.0 - smoothstep(15.0, 45.0, dist)) * (noor ? 0.3 : 1.0); // Noor's textures bring their own detail
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
	vec3 map_colour = vec3(floor(map_packed / 65536.0), mod(floor(map_packed / 256.0), 256.0), mod(map_packed, 256.0)) / 255.0;
	c = mix(c, map_colour, biome_debug * 0.85);
	if (show_grid) {
		vec2 g = abs(fract(p / 64.0) - 0.5);
		if (max(g.x, g.y) > 0.495) c = vec3(0.1, 0.8, 0.8);
	}
	// Rain: the whole world gets darker and glossier, and small puddles shine in flat spots (purely visual; real
	// standing water is the local simulation's).
	float wetness = rain_wetness.x * (1.0 - snow);
	float dry_under = 0.0;
	if (rain_now.z > 0.5 && rain_now.y > 0.5) {
		// Under a roof or an overhang the ground stays dry (Noor's notes §5), with a soft, ragged edge ~0.5 m wide where the
		// rain blows in: four taps of the 1 m map half a metre apart.
		vec3 wpos = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		if (length(VERTEX) < 30.0) {
			vec2 j = vec2(vnoise(p * 2.3) - 0.5, vnoise(p * 2.3 + 5.1) - 0.5) * 0.5;
			vec2 q = wpos.xz + j;
			dry_under = 0.25 * (covered_at(q + vec2(-0.25, -0.25), wpos.y) + covered_at(q + vec2(0.25, -0.25), wpos.y)
				+ covered_at(q + vec2(-0.25, 0.25), wpos.y) + covered_at(q + vec2(0.25, 0.25), wpos.y));
			wetness *= 1.0 - 0.95 * dry_under;
		}
	}
	float flat_ground = 1.0 - smoothstep(0.03, 0.1, slope);
	float puddle_noise = vnoise(p * 0.21) * 0.65 + vnoise(p * 0.93 + 7.1) * 0.35;
	float puddle = smoothstep(0.64, 0.7, puddle_noise) * flat_ground * rain_wetness.y * (1.0 - submerged) * (1.0 - snow) * (1.0 - 0.6 * forest) * (1.0 - dry_under);
	float wet_rough = 0.45, wet_rough_k = wetness * 0.7;
	if (rain_now.y > 0.5) {
		// Wet look by material (D212, Noor's Weather 2.0 notes §2): each ground holds water its own way, and as the
		// wetness falls they dry in turn - rock first (it sheds water), then grass, sand, and earth last, in patches.
		float w = wetness * (1.0 - 0.3 * smoothstep(0.3, 0.8, forest)); // (the canopy keeps some rain off)
		float patches = vnoise(p * 0.35 + 11.0) * 0.6 + vnoise(p * 1.7 + 3.0) * 0.4 - 0.5;
		float m_rock = rocky, m_sand = wet * (1.0 - rocky), m_dirt = max(bare, damp) * (1.0 - rocky) * (1.0 - m_sand);
		float m_grass = max(1.0 - m_rock - m_sand - m_dirt, 0.0);
		float a_rock = smoothstep(0.35, 0.65, w + patches * 0.3) * m_rock;
		float a_grass = smoothstep(0.2, 0.5, w + patches * 0.35) * m_grass;
		float a_sand = smoothstep(0.15, 0.4, w + patches * 0.3) * m_sand;
		float a_dirt = smoothstep(0.03, 0.3, w + patches * 0.35) * m_dirt;
		float lum = dot(c, vec3(0.3, 0.59, 0.11));
		vec3 richer = max(mix(vec3(lum), c, 1.15), vec3(0.0));
		c = c * (1.0 - a_rock - a_grass - a_sand - a_dirt) + c * 0.6 * a_rock + richer * 0.8 * a_grass + richer * 0.66 * a_sand + c * 0.56 * a_dirt;
		// gloss by material: rock and wet earth shine, sand a little, grass hardly (no plastic look)
		float sum = a_rock + a_grass + a_sand + a_dirt;
		wet_rough = (0.28 * a_rock + 0.8 * a_grass + 0.6 * a_sand + 0.48 * a_dirt) / max(sum, 1e-3);
		wet_rough_k = min(sum, 1.0);
	} else {
		c *= mix(1.0, 0.7, wetness);
	}
	// Ground relief (round 25): the textures' light and dark read as small bumps up close (derivative bump mapping on
	// a smooth copy of the textures), each ground its own roughness, crevices a little darker.
	float base_rough = 0.95;
	if (noor && ground_relief > 0.0) {
		float soft = 1.0 - smoothstep(25.0, 70.0, dist);
		float on_grass = (1.0 - rocky) * (1.0 - snow) * (1.0 - submerged) * (1.0 - smoothstep(0.15, 0.75, forest) * 0.85);
		float on_bare = bare * on_grass;
		base_rough = mix(0.95, 0.86, on_grass - on_bare) - 0.12 * rocky;
		{
			float hgt = 0.0;
			hgt += on_grass * (1.0 - bare) * gheight(G_GRASS, p) * 0.035;
			hgt += on_bare * gheight(G_DIRT, p) * 0.05;
			hgt += rocky * (1.0 - snow) * rock_relief * 0.08;
			hgt += smoothstep(0.15, 0.75, forest) * 0.85 * (1.0 - rocky) * gheight(G_FOREST, p) * 0.04;
			hgt *= ground_relief * soft;
			vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
			float dhx = dFdx(hgt), dhy = dFdy(hgt);
			vec3 r1 = cross(dpdy, NORMAL), r2 = cross(NORMAL, dpdx);
			float det = dot(dpdx, r1);
			vec3 grad = sign(det) * (dhx * r1 + dhy * r2);
			vec3 bumped = abs(det) * NORMAL - grad;
			// (guard: a zero-area pixel footprint gives a zero vector, and one NaN normal spreads over the screen in glow)
			if (abs(det) > 1e-12 && dot(bumped, bumped) > 1e-24) NORMAL = normalize(bumped);
			c *= 1.0 + clamp(hgt * 4.0, -0.15, 0.08); // crevices darker
		}
		base_rough = mix(0.95, base_rough, ground_relief);
	}
	ALBEDO = mix(pow(c, vec3(2.2)), vec3(0.025, 0.03, 0.035), puddle * 0.85);
	ROUGHNESS = mix(mix(mix(base_rough, 0.55, snow), wet_rough, wet_rough_k), 0.04, puddle);
	ROUGHNESS = mix(ROUGHNESS, 0.55, damp * wetland * 0.7); // glistening mud at wetland pool margins
	SPECULAR = mix(0.5, 0.9, puddle);
#ifdef AIR_SHADOW
	air_sun = 1.0;
	if (air_frame.w > 0.0) {
		vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec2 m = (wp.xz - air_frame.xy) / air_frame.z + 0.5;
		if (m.x > 0.0 && m.y > 0.0 && m.x < 1.0 && m.y < 1.0) {
			vec2 gs = textureLod(air_map, m, 0.0).rg; // ground, sun-shadow height (the same map: no slope bias)
			float shaded = smoothstep(4.0, 30.0, gs.g - gs.r);
			// near the camera the real shadow map is sharper: the map takes over from ~350 m
			// (not all of it: the 39 m map is coarse, and Noor asked for darker, not black)
			air_sun = 1.0 - air_frame.w * shaded * smoothstep(350.0, 700.0, length(VERTEX));
		}
	}
#endif
}

#ifdef AIR_SHADOW
// Godot's own Burley diffuse and GGX specular (as render_mode diffuse_burley), with the sun dimmed by air_sun.
void light() {
	float att = ATTENUATION * (LIGHT_IS_DIRECTIONAL ? air_sun : 1.0);
	vec3 h = normalize(VIEW + LIGHT);
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0), ndv = clamp(dot(NORMAL, VIEW), 1e-4, 1.0);
	float ldh = clamp(dot(LIGHT, h), 0.0, 1.0), ndh = clamp(dot(NORMAL, h), 0.0, 1.0);
	float fd90 = 2.0 * ldh * ldh * ROUGHNESS - 0.5;
	float fdv = 1.0 + fd90 * pow(1.0 - ndv, 5.0), fdl = 1.0 + fd90 * pow(1.0 - ndl, 5.0);
	DIFFUSE_LIGHT += LIGHT_COLOR * att * fdv * fdl * ndl / PI;
	float a = max(ROUGHNESS * ROUGHNESS, 0.002), a2 = a * a;
	float dd = ndh * ndh * (a2 - 1.0) + 1.0;
	float d = a2 / (PI * dd * dd);
	float k = a * 0.5;
	float vis = 0.25 / ((ndl * (1.0 - k) + k) * (ndv * (1.0 - k) + k) + 1e-4);
	float f0 = 0.08 * SPECULAR_AMOUNT;
	float f = f0 + (1.0 - f0) * pow(1.0 - ldh, 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * att * ndl * d * vis * f;
}
#endif
