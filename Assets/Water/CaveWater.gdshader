shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque;

// Laboratory water: geometry supplies local bed depth and distance to shore.
// No screen/depth texture dependency, so this also works with the Compatibility renderer.
varying vec2 world_xz;
varying float bed_depth;
varying float shore;

void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
    bed_depth = COLOR.r * 4.0;
    shore = COLOR.g;
    // Keep clipped geometry fixed: displacement opens cracks at shoreline T-junctions.
    // Moving ripples are supplied by the fragment normal below.
}

void fragment() {
    float a = world_xz.x * 3.1 + world_xz.y * 1.8 + TIME * 1.4;
    float b = world_xz.x * -2.2 + world_xz.y * 4.3 - TIME * 1.1;
    vec3 ripple_normal = normalize(vec3(-0.075 * cos(a) + 0.034 * cos(b), 1.0,
                                       -0.046 * cos(a) - 0.065 * cos(b)));
    vec3 n = normalize((VIEW_MATRIX * vec4(ripple_normal, 0.0)).xyz);
    if (!FRONT_FACING) n = -n;
    NORMAL = n;
    float fresnel = pow(1.0 - clamp(dot(n, VIEW), 0.0, 1.0), 4.0);
    float depth = 1.0 - exp(-bed_depth * 0.9);
    vec3 water = mix(vec3(0.12, 0.30, 0.27), vec3(0.025, 0.115, 0.16), depth);
    // A restrained environment tint and moving specular highlights make grazing angles readable underground.
    water = mix(water, vec3(0.30, 0.43, 0.46), fresnel * 0.55);
    float edge = (1.0 - smoothstep(0.015, 0.10, shore)) * (0.55 + 0.15 * sin(a + b));
    ALBEDO = mix(water, vec3(0.39, 0.49, 0.44), edge * 0.3);
    float ripple = sin(a * 2.3 + sin(b)) * sin(b * 1.7 + sin(a));
    ALBEDO += vec3(0.025, 0.04, 0.045) * ripple * shore;
    ROUGHNESS = 0.12 + edge * 0.2;
    SPECULAR = 0.7;
    // Fade the final shallow film into the bed instead of displaying a hard polygon outline.
    ALPHA = (mix(0.30, 0.85, depth) + fresnel * 0.12) * smoothstep(0.0, 0.12, bed_depth);
}




