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
uniform float sun_glare = 1.0; // disc and halo strength (setting)
uniform int sun_style = 0;     // 0 classic (build 11), 1 natural (colour, size and glow change with the sun's height)
// xy: world position of the camera plus the wind drift, wrapped to the cloud period (m); z: coverage 0..1;
// w: storm 0..1 (rain: grey, darker sky and clouds).
global uniform vec4 cloud_state;
// xy: the same for the high cirrus layer, zw: for the low layer (each drifts at its own speed and heading).
global uniform vec4 cloud_layers;
// Biome mood: rgb multiplies the sky colours, a = extra haze toward the horizon colour (0 = realistic sky).
global uniform vec4 sky_mood;
// Night: moon direction (xyz) and phase (w: 0 new … 0.5 full); star field rotation; x = days, y = planets on,
// z = star brightness, w = moon illuminated fraction.
global uniform vec4 night_sky;
global uniform mat3 star_rotation;
global uniform vec4 sky_extra;
// The real sun (xyz) and its elevation in degrees (w), also below the horizon (the light itself stops at −4°).
global uniform vec4 sun_true;

// Twilight (round 16): how much of each twilight effect shows for the sun's true elevation e (degrees).
float golden_w(float e) { return smoothstep(9.0, 1.0, e) * smoothstep(-7.0, -1.0, e); }  // sunset glow at the horizon
float belt_w(float e) { return smoothstep(4.0, 0.0, e) * smoothstep(-7.0, -2.0, e); }     // Belt of Venus
float blue_w(float e) { return smoothstep(-11.0, -4.0, e) * smoothstep(2.0, -2.0, e); }  // blue hour
// Clouds lit from below by a sun under (or at) the horizon: orange, then pink.
vec3 underlight(vec3 dir) {
	float e = sun_true.w;
	float w = smoothstep(-7.0, -1.0, e) * smoothstep(10.0, 2.0, e);
	if (w <= 0.0) return vec3(0.0);
	vec2 sd = normalize(sun_true.xz + vec2(1e-4)), dd = normalize(dir.xz + vec2(1e-4));
	float toward = dot(sd, dd) * 0.5 + 0.5;
	vec3 c = mix(vec3(1.0, 0.55, 0.25), vec3(0.95, 0.42, 0.52), smoothstep(1.0, -4.0, e));
	return c * w * (0.25 + 0.75 * toward * toward);
}

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

float nhash(vec2 p) { vec3 p3 = fract(vec3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return fract((p3.x + p3.y) * p3.z); }

// Stars (twinkling), a faint Milky Way, planets and the moon with its phase; faded in as the sun sets.
vec3 night_sky_colour(vec3 dir, vec3 sun_dir, float night) {
	if (night <= 0.001 || dir.y < -0.05) return vec3(0.0);
	vec3 sd = star_rotation * dir;
	vec3 col = vec3(0.0);
	float strength = sky_extra.z;
	// Stars on a cube-face grid of the celestial sphere (even density everywhere): one candidate per cell, few lit.
	vec3 a = abs(sd);
	vec2 fuv; float face;
	if (a.x >= a.y && a.x >= a.z) { fuv = sd.yz / a.x; face = sign(sd.x); }
	else if (a.y >= a.z) { fuv = sd.xz / a.y; face = 2.0 + sign(sd.y); }
	else { fuv = sd.xy / a.z; face = 4.0 + sign(sd.z); }
	float theta = atan(sd.z, sd.x), phi = asin(clamp(sd.y, -1.0, 1.0));
	for (int layer = 0; layer < 2; layer++) {
		float scale = layer == 0 ? 90.0 : 230.0;
		vec2 g = fuv * scale;
		vec2 cell = floor(g);
		vec2 key = cell + face * 1013.0 + float(layer) * 91.7;
		float h = nhash(key);
		if (h > (layer == 0 ? 0.955 : 0.92)) {
			vec2 c = cell + 0.25 + 0.5 * vec2(nhash(key + 3.1), nhash(key + 7.3));
			float d = length(g - c);
			float size = layer == 0 ? 0.3 : 0.24;
			float b = smoothstep(size, 0.0, d) * pow(fract(h * 37.0), layer == 0 ? 2.0 : 4.0) * (layer == 0 ? 2.2 : 0.9);
			b *= 0.75 + 0.25 * sin(TIME * (2.0 + 5.0 * nhash(key)) + h * 40.0); // twinkle
			vec3 tint = mix(vec3(0.75, 0.85, 1.0), vec3(1.0, 0.85, 0.7), nhash(key + 11.0));
			col += tint * b;
		}
	}
	// Milky Way: a soft band around a great circle, broken by dust.
	float bd = dot(sd, normalize(vec3(0.2, 0.55, 0.81))), band = exp(-bd * bd * 22.0); // (pow of a negative base is undefined)
	float dust = nhash(floor(vec2(theta, phi) * 60.0)) * 0.4 + 0.6;
	col += vec3(0.55, 0.6, 0.75) * band * dust * 0.06;
	// Planets on the ecliptic (tilted 23.4°), each wandering at its own pace.
	if (sky_extra.y > 0.5) {
		for (int i = 0; i < 4; i++) {
			float fi = float(i);
			float lon = fi * 1.7 + sky_extra.x * (0.035 / (1.0 + fi * 1.3)) + 0.6;
			vec3 p = normalize(vec3(cos(lon), sin(lon) * 0.397, sin(lon) * 0.918));
			vec3 pd = transpose(star_rotation) * p;
			float d = acos(clamp(dot(dir, pd), -1.0, 1.0));
			vec3 tint = i == 0 ? vec3(1.0, 0.98, 0.9) : i == 1 ? vec3(1.0, 0.6, 0.45) : i == 2 ? vec3(1.0, 0.92, 0.75) : vec3(0.85, 0.9, 1.0);
			col += tint * smoothstep(0.0022, 0.0006, d) * (i == 0 ? 3.0 : 1.6);
		}
	}
	col *= strength * smoothstep(-0.02, 0.12, dir.y); // thick air at the horizon hides the faint stars
	// Moon: a lit sphere (phase from the sun's direction), a soft glow around it.
	vec3 md = night_sky.xyz;
	float mc = dot(dir, md);
	float rad = 0.02; // a bit larger than life, as in most games
	float r = sqrt(max(0.0, 2.0 * (1.0 - mc))) / rad;
	if (r < 1.0) {
		vec3 right = normalize(cross(md, vec3(0.0, 1.0, 0.0) + vec3(1e-4)));
		vec3 up = cross(right, md);
		vec2 q = vec2(dot(dir - md, right), dot(dir - md, up)) / rad;
		// The visible hemisphere faces us; it is lit where it faces the (far away) sun: full when opposite the sun.
		vec3 n = normalize(right * q.x + up * q.y - md * sqrt(max(0.0, 1.0 - dot(q, q))));
		float light = clamp(dot(n, sun_dir) * 1.5, 0.0, 1.0);
		float maria = 0.8 + 0.2 * nhash(floor(q * 4.0 + 9.0));
		col = mix(col, vec3(0.92, 0.93, 0.98) * (0.035 + 1.6 * light * maria), smoothstep(1.0, 0.92, r));
	}
	col += vec3(0.5, 0.58, 0.75) * pow(max(mc, 0.0), 400.0) * 0.45 * sky_extra.w * strength;
	return col * night;
}

vec3 sky_colour(vec3 dir, vec3 sun_dir, vec3 sun_col, float day, float mu) {
	float up = dir.y;
	vec3 zenith = zenith_color * sky_mood.rgb, horizon = horizon_color * mix(vec3(1.0), sky_mood.rgb, 0.7);
	vec3 col = mix(horizon, zenith, smoothstep(0.0, 0.45 + sky_mood.a * 0.4, up));
	float low = 1.0 - smoothstep(0.05, 0.45, sun_dir.y);
	col = mix(col, col * vec3(1.25, 0.95, 0.75), low * smoothstep(0.35, 0.0, up) * (0.5 + 0.5 * max(mu, 0.0)));
	col *= day;
	// Twilight colours from the sun's true elevation (round 16, docs/SKY_IDEAS.md): a red-orange band along the horizon
	// toward the sun; opposite it the pink Belt of Venus with Earth's blue-grey shadow rising underneath after sunset;
	// then the deep blue hour.
	{
		float e = sun_true.w;
		vec2 sd = normalize(sun_true.xz + vec2(1e-4)), dd = normalize(dir.xz + vec2(1e-4));
		float az = dot(sd, dd); // 1 toward the sun's azimuth, −1 away
		float h = max(up, 0.0);
		float gw = golden_w(e);
		if (gw > 0.0) {
			float band = exp(-h * h / mix(0.004, 0.02, smoothstep(-4.0, 6.0, e)));
			vec3 glow = mix(vec3(1.0, 0.62, 0.25), vec3(0.95, 0.3, 0.12), smoothstep(3.0, -3.0, e));
			col += glow * band * gw * (0.08 + 0.92 * pow(max(az, 0.0), 2.0)) * 0.65 * sky_mood.rgb;
		}
		float bw = belt_w(e);
		if (bw > 0.0) {
			float shadow_top = clamp(-e * 0.012, 0.0, 0.09);           // Earth's shadow rises as the sun sinks
			float bx = (h - shadow_top - 0.06) / 0.05, belt = exp(-bx * bx);
			float away = pow(max(-az, 0.0), 1.5);
			col += vec3(0.62, 0.36, 0.45) * belt * bw * away * 0.45;
			col = mix(col, vec3(0.16, 0.2, 0.32) * 0.6, (1.0 - smoothstep(shadow_top * 0.6, shadow_top + 0.01, h)) * bw * away * step(0.001, shadow_top) * 0.6);
		}
		float blw = blue_w(e);
		if (blw > 0.0) col += vec3(0.035, 0.07, 0.17) * blw * (0.55 + 0.45 * smoothstep(0.0, 0.5, h)) * sky_mood.rgb;
	}
	if (sun_style == 0) col += (sun_col * pow(max(mu, 0.0), 10.0) * 0.18 + sun_col * pow(max(mu, 0.0), 300.0) * 0.6) * sun_glare;
	else {
		// Natural: a tight white halo high up; near the horizon a wide, warm glow (sunrise, sunset).
		float lowsun = 1.0 - smoothstep(0.0, 0.35, sun_dir.y);
		vec3 warm = mix(vec3(1.0, 0.95, 0.88), vec3(1.0, 0.55, 0.25), lowsun);
		col += sun_col * warm * (pow(max(mu, 0.0), mix(400.0, 60.0, lowsun)) * 0.5 + pow(max(mu, 0.0), mix(18.0, 5.0, lowsun)) * mix(0.1, 0.32, lowsun)) * sun_glare;
	}
	col = mix(col, ground_color * day, smoothstep(0.0, -0.06, up));
	return col;
}

// A flat cloud layer at height h: the colour over the sky and the opacity.
vec4 layer(vec3 dir, vec2 origin, float h, float d, vec3 sun_dir, vec3 sun_col, float day, float mu, float toward, float bright) {
	float lit = clamp(1.0 - toward * 0.65, 0.0, 1.0);
	vec3 shade = mix(vec3(0.46, 0.5, 0.56), vec3(1.0, 0.99, 0.97), lit);
	vec3 cloud = shade * (0.25 + 0.85 * day) * mix(vec3(1.0), LIGHT0_COLOR, 0.5) * bright;
	// Night: clouds go dark, faintly lit by the moon (brighter toward it).
	float night = 1.0 - smoothstep(-0.16, 0.04, sun_dir.y);
	vec3 moonlit = vec3(0.07, 0.08, 0.11) * (0.4 + 0.6 * sky_extra.w) * (1.0 + 2.0 * pow(max(dot(dir, night_sky.xyz), 0.0), 8.0));
	cloud = mix(cloud, moonlit * shade, night);
	// Sunset and twilight: clouds glow from below, toward the sun most (their white daylight shading gives way, so
	// they turn golden and pink rather than cream).
	vec3 ul = underlight(dir);
	cloud *= 1.0 - 0.45 * clamp(dot(ul, vec3(0.5)), 0.0, 0.8);
	cloud += ul * (0.45 + 0.55 * d) * 0.9;
	cloud += sun_col * pow(max(mu, 0.0), 6.0) * (1.0 - d) * 0.8;
	// Silver lining (Tom, round 8): in front of the sun, thick cloud is backlit and darker while its thin edges blaze
	// (light scattered forward through the edge).
	float toward_sun = max(mu, 0.0);
	float rim = smoothstep(0.02, 0.25, d) * (1.0 - smoothstep(0.35, 0.85, d));
	cloud *= 1.0 - 0.35 * pow(toward_sun, 10.0) * smoothstep(0.5, 1.0, d) * (1.0 - night);
	cloud += sun_col * rim * (pow(toward_sun, 60.0) * 2.2 + pow(toward_sun, 14.0) * 0.6) * (1.0 - night);
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
		vec3 lit = sun_light * sun_trans * phase * 7.0 * mix(1.0, powder, 0.5) + ambient_top * mix(0.3, 0.75, h);
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
		float night = 1.0 - smoothstep(-0.16, 0.04, sun_dir.y);
		if (!AT_CUBEMAP_PASS) col += night_sky_colour(dir, sun_dir, night);
		vec3 disc = sun_col * smoothstep(0.99955, 0.9998, mu) * 6.0 * sun_glare;
		if (sun_style == 1) {
			// Softer at noon (less blinding), larger and orange-red as it sinks toward the horizon.
			float lowsun = 1.0 - smoothstep(0.0, 0.4, sun_dir.y);
			float r0 = mix(0.99962, 0.99915, lowsun);
			vec3 tint = mix(vec3(1.0, 0.98, 0.92), vec3(1.0, 0.45, 0.18), lowsun * lowsun);
			disc = sun_col * tint * smoothstep(r0, r0 + 0.0002, mu) * mix(3.2, 0.95, lowsun) * sun_glare;
		}
		disc *= smoothstep(-0.004, 0.004, up); // a sun under the horizon shows no disc
		bool layered = quality >= 1 && !AT_CUBEMAP_PASS;
		if (up > 0.0) {
			// High cirrus (7 km): thin, bright, slow, on its own heading.
			if (layered) {
				float t = 7000.0 / max(up, 0.015);
				float d = cirrus_density(cloud_layers.xy + dir.xz * t, coverage) * smoothstep(0.02, 0.15, up) * (1.0 - storm);
				vec3 c = vec3(1.0, 0.99, 0.97) * (0.3 + 0.8 * day) * mix(vec3(1.0), LIGHT0_COLOR, 0.6) + sun_col * pow(max(mu, 0.0), 8.0) * 0.5
					+ underlight(dir) * 1.1; // high cirrus stays lit longest after sunset
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
			// Low ragged clouds (900 m): greyer, faster, in front of the main deck (layered mode; the volume has its own depth).
			if (layered && quality == 1) {
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
		col = mix(col, horizon_color * mix(vec3(1.0), sky_mood.rgb, 0.8) * day, sky_mood.a * 0.7 * (1.0 - smoothstep(0.0, 0.6, up)));
		// Storm: a low grey overcast.
		vec3 overcast = mix(vec3(0.42, 0.45, 0.48), vec3(0.3, 0.32, 0.35), smoothstep(0.0, 0.5, up)) * (0.35 + 0.65 * day);
		col = mix(col, overcast, storm * 0.85);
		disc *= 1.0 - storm;
		COLOR = col + disc;
	}
}
