shader_type spatial;
render_mode cull_disabled, specular_disabled;
uniform sampler2D pixel_map : source_color, filter_nearest, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform vec3 player_world = vec3(0.0);
uniform vec2 player_screen = vec2(-10000.0);
uniform vec2 viewport_pixels = vec2(1280.0, 800.0);
uniform float clear_radius = 52.0;
// Leaf translucency (rewrite world setting; 0 in the valley game, which never sets it).
global uniform float leaf_translucency;
global uniform vec4 sun_glow;
global uniform vec3 sun_dir_world;
// Wind (rewrite world; zero in the valley game, which never sets it): crowns sway, more toward the top.
global uniform vec4 wind;
varying vec3 world_position;
void vertex() {
    vec3 base = MODEL_MATRIX[3].xyz;
    vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    if (wind.y > 0.0) {
        float h = clamp((wp.y - base.y) / 14.0, 0.0, 1.0);
        float phase = base.x * 0.13 + base.z * 0.17;
        float sway = h * h * (0.08 + 0.22 * wind.y) * (0.7 + 0.3 * wind.w);
        vec3 offset = vec3(wind.x, 0.0, wind.z) * sway * (0.65 + 0.35 * sin(TIME * (0.9 + 0.3 * wind.y) + phase));
        offset += vec3(sin(TIME * 2.1 + phase + wp.y), 0.0, cos(TIME * 1.7 + phase)) * 0.03 * h * (0.3 + wind.y);
        VERTEX += (inverse(mat3(MODEL_MATRIX)) * offset);
        wp += offset;
    }
    world_position = wp;
}
void fragment() {
    vec4 pixel = texture(pixel_map, UV) * tint;
    ALBEDO = pixel.rgb;
    ROUGHNESS = 0.93;
    ALPHA = pixel.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    if (!FRONT_FACING) { NORMAL = -NORMAL; }
    BACKLIGHT = pixel.rgb * vec3(0.9, 1.08, 0.55) * leaf_translucency;
    {
        vec3 view_ray = normalize(world_position - CAMERA_POSITION_WORLD);
        float forward = pow(clamp(dot(view_ray, sun_dir_world), 0.0, 1.0), 5.0);
        EMISSION += pixel.rgb * vec3(0.95, 1.1, 0.5) * sun_glow.rgb * forward * leaf_translucency * 0.32 * 1.0;
    }
    vec3 toward_player = player_world - CAMERA_POSITION_WORLD;
    float player_distance = length(toward_player);
    float along_view = dot(world_position - CAMERA_POSITION_WORLD, normalize(toward_player));
    // A small pixel-dithered window only through foliage in front of the character.
    // Leaves behind the player, bark and branches keep their normal appearance.
    float radius = length(SCREEN_UV * viewport_pixels - player_screen) / clear_radius;
    if (along_view > 0.0 && along_view < player_distance - 0.45 && radius < 1.0) {
        float edge = smoothstep(0.72, 1.0, radius);
        float stipple = fract(dot(floor(FRAGCOORD.xy / 2.0), vec2(0.75487766, 0.56984029)));
        if (stipple > edge) { ALPHA = 0.0; }
    }
}