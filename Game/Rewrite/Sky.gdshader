// Sky with drifting clouds for the rewrite world. The cloud field is a tileable noise texture laid on a plane
// ~1.6 km above the ground; the same texture (offset along the sun direction) casts the cloud shadows on the land
// (Atmosphere.cs), so shadows match the clouds above. Cloud position/coverage come from a global uniform: updating it
// every frame does not dirty this material (which would re-bake the sky's radiance map each frame).
shader_type sky;
render_mode use_debanding;

uniform vec3 zenith_color : source_color = vec3(0.29, 0.45, 0.62);
uniform vec3 horizon_color : source_color = vec3(0.68, 0.75, 0.78);
uniform vec3 ground_color : source_color = vec3(0.24, 0.28, 0.25);
uniform sampler2D cloud_tex : filter_linear_mipmap, repeat_enable;
uniform float cloud_period = 6000.0;
uniform float cloud_height = 1600.0;
// xy: world position of the camera plus the wind drift, wrapped to the cloud period (m); z: coverage 0..1.
global uniform vec4 cloud_state;

float cloud_density(vec2 xz, float coverage) {
	vec2 uv = xz / cloud_period;
	float base = texture(cloud_tex, uv).r;
	float detail = texture(cloud_tex, uv * 3.7 + vec2(0.31, 0.17)).g;
	float n = base * 0.8 + detail * 0.2;
	float edge = 1.0 - coverage;
	return smoothstep(edge - 0.05, edge + 0.2, n) * step(0.001, coverage);
}

void sky() {
	vec3 dir = EYEDIR;
	float up = dir.y;
	vec3 sun_dir = LIGHT0_DIRECTION;
	vec3 sun_col = LIGHT0_COLOR * LIGHT0_ENERGY;
	float day = clamp(sun_dir.y * 4.0 + 0.35, 0.08, 1.0);
	float mu = dot(dir, sun_dir);

	vec3 col = mix(horizon_color, zenith_color, smoothstep(0.0, 0.45, up));
	// Low sun: warmer, brighter horizon toward the sun.
	float low = 1.0 - smoothstep(0.05, 0.45, sun_dir.y);
	col = mix(col, col * vec3(1.25, 0.95, 0.75), low * smoothstep(0.35, 0.0, up) * (0.5 + 0.5 * max(mu, 0.0)));
	col *= day;
	col += sun_col * pow(max(mu, 0.0), 10.0) * 0.18 + sun_col * pow(max(mu, 0.0), 300.0) * 0.6;
	col = mix(col, ground_color * day, smoothstep(0.0, -0.06, up));
	vec3 disc = sun_col * smoothstep(0.99955, 0.9998, mu) * 6.0;

	if (up > 0.0) {
		float coverage = cloud_state.z;
		float t = cloud_height / max(up, 0.015);
		vec2 xz = cloud_state.xy + dir.xz * t;
		float d = cloud_density(xz, coverage);
		if (d > 0.0) {
			// Self-shadowing: cloud mass toward the sun darkens the base; edges facing the sun glow.
			float toward = cloud_density(xz + normalize(sun_dir.xz + vec2(1e-4)) * 260.0, coverage);
			float lit = clamp(1.0 - toward * 0.65, 0.0, 1.0);
			vec3 shade = mix(vec3(0.46, 0.5, 0.56), vec3(1.0, 0.99, 0.97), lit);
			vec3 cloud = shade * (0.25 + 0.85 * day) * mix(vec3(1.0), LIGHT0_COLOR, 0.5);
			cloud += sun_col * pow(max(mu, 0.0), 6.0) * (1.0 - d) * 0.8;
			// Thin out toward the horizon (distance), where the plane would otherwise squash into streaks.
			d *= smoothstep(0.015, 0.12, up);
			col = mix(col, cloud, d);
			disc *= 1.0 - d;
		}
	}
	COLOR = col + disc;
}
