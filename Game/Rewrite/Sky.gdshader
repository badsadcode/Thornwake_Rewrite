// Sky with drifting clouds for the rewrite world. The main cloud field is a tileable noise texture laid on a plane
// ~1.6 km above the ground; the same texture (offset along the sun direction) casts the cloud shadows on the land
// (Atmosphere.cs), so shadows match the clouds above. Cloud positions/coverage come from global uniforms: updating
// them every frame does not dirty this material (which would re-bake the sky's radiance map each frame).
//
// Cloud quality (setting "Clouds"):
//   0 one layer   — the main layer only (build 10 look);
//   1 layered     — + high, thin, streaky cirrus drifting slowly on another heading and low, ragged, fast clouds
//                   under the main deck: different speeds and heights give wind and depth;
//   2 volumetric  — experimental: the main deck ray-marched as a 1.3–2.4 km thick volume lit by the sun (self
//                   shadowing, silver lining, darker bases), at half resolution; high and low layers as in 1.
// Biome moods: a global tint (rgb) and haze (a) set by Atmosphere.cs from the biome around the player (option).
shader_type sky;
render_mode use_debanding, use_half_res_pass;

uniform vec3 zenith_color : source_color = vec3(0.29, 0.45, 0.62);
uniform vec3 horizon_color : source_color = vec3(0.68, 0.75, 0.78);
uniform vec3 ground_color : source_color = vec3(0.24, 0.28, 0.25);
uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform float cloud_period = 6000.0;
uniform float cloud_height = 1600.0;
uniform int quality = 1;
// xy: world position of the camera plus the wind drift, wrapped to the cloud period (m); z: coverage 0..1;
// w: storm 0..1 (rain: grey, darker sky and clouds).
global uniform vec4 cloud_state;
// xy: the same for the high cirrus layer, zw: for the low layer (each drifts at its own speed and heading).
global uniform vec4 cloud_layers;
// Biome mood: rgb multiplies the sky colours, a = extra haze toward the horizon colour (0 = realistic sky).
global uniform vec4 sky_mood;

float cloud_density(vec2 xz, float coverage) {
	vec2 uv = xz / cloud_period;
	float base = texture(cloud_tex, uv).r;
	float detail = texture(cloud_tex, uv * 4.0 + vec2(0.31, 0.17)).g;
	float n = base * 0.8 + detail * 0.2;
	float edge = 1.0 - coverage;
	return smoothstep(edge - 0.05, edge + 0.2, n) * step(0.001, coverage);
}

// High cirrus: thin streaks stretched along a slanted axis. All texture scales are integers after the 3-4-5 rotation,
// so the pattern repeats exactly every 25 cloud periods (150 km) and Atmosphere.cs can wrap its drift there.
float cirrus_density(vec2 xz, float coverage) {
	vec2 uv = xz / (cloud_period * 2.5);
	uv = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * uv;
	float n = texture(cloud_tex, uv * vec2(1.0, 4.5)).g * 0.7 + texture(cloud_tex, uv * vec2(3.0, 9.0) + 0.37).r * 0.3;
	float edge = 1.0 - (0.1 + coverage * 0.45);
	return smoothstep(edge, edge + 0.3, n) * 0.35;
}

// Low ragged clouds: smaller, broken pieces.
float low_density(vec2 xz, float coverage, float storm) {
	vec2 uv = xz / (cloud_period * 0.45) + vec2(0.53, 0.29);
	float n = texture(cloud_tex, uv).r * 0.65 + texture(cloud_tex, uv * 3.0).g * 0.35;
	float edge = 1.0 - (coverage * 0.55 + storm * 0.35);
	return smoothstep(edge, edge + 0.16, n) * 0.85 * step(0.05, coverage);
}

vec3 sky_colour(vec3 dir, vec3 sun_dir, vec3 sun_col, float day, float mu) {
	float up = dir.y;
	vec3 zenith = zenith_color * sky_mood.rgb, horizon = horizon_color * mix(vec3(1.0), sky_mood.rgb, 0.7);
	vec3 col = mix(horizon, zenith, smoothstep(0.0, 0.45 + sky_mood.a * 0.4, up));
	float low = 1.0 - smoothstep(0.05, 0.45, sun_dir.y);
	col = mix(col, col * vec3(1.25, 0.95, 0.75), low * smoothstep(0.35, 0.0, up) * (0.5 + 0.5 * max(mu, 0.0)));
	col *= day;
	col += sun_col * pow(max(mu, 0.0), 10.0) * 0.18 + sun_col * pow(max(mu, 0.0), 300.0) * 0.6;
	col = mix(col, ground_color * day, smoothstep(0.0, -0.06, up));
	return col;
}

// A flat cloud layer at height h: the colour over the sky and the opacity.
vec4 layer(vec3 dir, vec2 origin, float h, float d, vec3 sun_dir, vec3 sun_col, float day, float mu, float toward, float bright) {
	float lit = clamp(1.0 - toward * 0.65, 0.0, 1.0);
	vec3 shade = mix(vec3(0.46, 0.5, 0.56), vec3(1.0, 0.99, 0.97), lit);
	vec3 cloud = shade * (0.25 + 0.85 * day) * mix(vec3(1.0), LIGHT0_COLOR, 0.5) * bright;
	cloud += sun_col * pow(max(mu, 0.0), 6.0) * (1.0 - d) * 0.8;
	return vec4(cloud, d);
}

// ---- volumetric main deck (quality 2)
const float VBOTTOM = 1300.0;
const float VTOP = 2400.0;

float vdensity(vec3 p, float coverage) {
	vec2 uv = p.xz / cloud_period;
	float n = texture(cloud_tex, uv).r * 0.8 + texture(cloud_tex, uv * 4.0 + vec2(0.31, 0.17)).g * 0.2;
	float edge = 1.0 - coverage;
	// Same footprint as the flat layer (so gaps and cloud shadows match); thicker clouds tower higher.
	float mass = smoothstep(edge - 0.05, edge + 0.2, n);
	if (mass <= 0.0) return 0.0;
	float h = (p.y - VBOTTOM) / (VTOP - VBOTTOM);
	// Rounded domes: the top rises quickly from the cloud edge (sqrt), flat bases.
	float top = sqrt(mass) * (0.4 + 0.6 * smoothstep(edge, edge + 0.45, n));
	float shape = smoothstep(0.0, 0.06, h) * smoothstep(0.0, 0.2, top - h);
	float detail = texture(cloud_tex, uv * 9.0 + vec2(h * 0.7, h * 0.4)).g;
	return clamp(shape * (0.6 + 0.8 * detail) - 0.25 * (1.0 - mass), 0.0, 1.0);
}

vec4 volumetric(vec3 dir, vec3 sun_dir, vec3 sun_col, float day, float mu, float coverage, float storm) {
	if (dir.y < 0.01 || coverage < 0.001) return vec4(0.0);
	vec2 cam = cloud_state.xy;
	float t0 = VBOTTOM / dir.y, t1 = min(VTOP / dir.y, 45000.0);
	if (t0 > t1) return vec4(0.0);
	const int STEPS = 40;
	float step_len = (t1 - t0) / float(STEPS);
	float jitter = fract(sin(dot(dir.xz * 731.0, vec2(12.9898, 78.233))) * 43758.5453);
	float trans = 1.0;
	vec3 light = vec3(0.0);
	// Henyey-Greenstein mix: strong forward scattering (silver lining toward the sun) plus a soft back lobe.
	float g = 0.6, hg = (1.0 - g * g) / pow(1.0 + g * g - 2.0 * g * mu, 1.5) * 0.08;
	float phase = mix(0.09, hg, 0.65);
	vec3 ambient_top = mix(horizon_color, zenith_color, 0.6) * sky_mood.rgb * (0.3 + 0.6 * day);
	vec3 sun_light = sun_col * mix(vec3(1.0), LIGHT0_COLOR, 0.3) * (1.0 - storm * 0.7);
	for (int i = 0; i < STEPS; i++) {
		float t = t0 + (float(i) + jitter) * step_len;
		vec3 p = vec3(cam.x, 0.0, cam.y) + dir * t;
		float d = vdensity(p, coverage);
		if (d <= 0.001) continue;
		// Light reaching this point: density toward the sun (a few coarse steps).
		float toward = 0.0;
		for (int k = 1; k <= 4; k++) toward += vdensity(p + sun_dir * float(k) * 160.0, coverage);
		float sun_trans = exp(-toward * 160.0 * 0.004);
		float powder = 1.0 - exp(-d * 2.0);
		float h = (p.y - VBOTTOM) / (VTOP - VBOTTOM);
		vec3 lit = sun_light * sun_trans * phase * 4.5 * mix(1.0, powder, 0.5) + ambient_top * mix(0.3, 0.75, h);
		float sigma = d * 0.003;
		float a = exp(-sigma * step_len);
		light += trans * (1.0 - a) * lit;
		trans *= a;
		if (trans < 0.02) break;
	}
	// Fade with distance toward the horizon (the slab would otherwise end in a hard line).
	float fade = smoothstep(0.01, 0.1, dir.y);
	return vec4(light * fade, (1.0 - trans) * fade);
}

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;
	vec3 sun_dir = LIGHT0_DIRECTION;
	vec3 sun_col = LIGHT0_COLOR * LIGHT0_ENERGY;
	float day = clamp(sun_dir.y * 4.0 + 0.35, 0.08, 1.0);
	float mu = dot(dir, sun_dir);
	float coverage = cloud_state.z, storm = cloud_state.w;

	if (AT_HALF_RES_PASS) {
		// Only the volumetric deck is drawn here (premultiplied colour + opacity), composited in the full pass.
		COLOR = vec3(0.0); ALPHA = 0.0;
		if (quality == 2) { vec4 v = volumetric(dir, sun_dir, sun_col, day, mu, coverage, storm); COLOR = v.rgb; ALPHA = v.a; }
	} else {
		vec3 col = sky_colour(dir, sun_dir, sun_col, day, mu);
		vec3 disc = sun_col * smoothstep(0.99955, 0.9998, mu) * 6.0;
		bool layered = quality >= 1 && !AT_CUBEMAP_PASS;
		if (up > 0.0) {
			// High cirrus (7 km): thin, bright, slow, on its own heading.
			if (layered) {
				float t = 7000.0 / max(up, 0.015);
				float d = cirrus_density(cloud_layers.xy + dir.xz * t, coverage) * smoothstep(0.02, 0.15, up) * (1.0 - storm);
				vec3 c = vec3(1.0, 0.99, 0.97) * (0.3 + 0.8 * day) * mix(vec3(1.0), LIGHT0_COLOR, 0.6) + sun_col * pow(max(mu, 0.0), 8.0) * 0.5;
				col = mix(col, c, d);
			}
			// Main deck: volumetric (quarter-res pass) or the flat textured layer.
			if (quality == 2 && !AT_CUBEMAP_PASS) {
				vec4 v = HALF_RES_COLOR;
				col = col * (1.0 - v.a) + v.rgb;
				disc *= 1.0 - v.a;
			} else {
				float t = cloud_height / max(up, 0.015);
				vec2 xz = cloud_state.xy + dir.xz * t;
				float d = cloud_density(xz, coverage);
				if (d > 0.0) {
					float toward = cloud_density(xz + normalize(sun_dir.xz + vec2(1e-4)) * 260.0, coverage);
					vec4 c = layer(dir, xz, cloud_height, d, sun_dir, sun_col, day, mu, toward, 1.0);
					d *= smoothstep(0.015, 0.12, up);
					col = mix(col, c.rgb, d);
					disc *= 1.0 - d;
				}
			}
			// Low ragged clouds (900 m): greyer, faster, in front of the main deck.
			if (layered) {
				float t = 900.0 / max(up, 0.015);
				vec2 xz = cloud_layers.zw + dir.xz * t;
				float d = low_density(xz, coverage, storm) * smoothstep(0.02, 0.1, up);
				if (d > 0.0) {
					float toward = low_density(xz + normalize(sun_dir.xz + vec2(1e-4)) * 150.0, coverage, storm);
					vec4 c = layer(dir, xz, 900.0, d, sun_dir, sun_col, day, mu, toward, 0.86);
					col = mix(col, c.rgb, d);
					disc *= 1.0 - d;
				}
			}
		}
		// Biome haze: a veil of the horizon colour (mist, dust, ash).
		col = mix(col, horizon_color * mix(vec3(1.0), sky_mood.rgb, 0.8) * day, sky_mood.a * 0.45 * (1.0 - smoothstep(0.0, 0.6, up)));
		// Storm: a low grey overcast.
		vec3 overcast = mix(vec3(0.42, 0.45, 0.48), vec3(0.3, 0.32, 0.35), smoothstep(0.0, 0.5, up)) * (0.35 + 0.65 * day);
		col = mix(col, overcast, storm * 0.85);
		disc *= 1.0 - storm;
		COLOR = col + disc;
	}
}
