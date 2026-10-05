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
varying vec3 world_position;
void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
    vec4 pixel = texture(pixel_map, UV) * tint;
    ALBEDO = pixel.rgb;
    ROUGHNESS = 0.93;
    ALPHA = pixel.a;
    ALPHA_SCISSOR_THRESHOLD = 0.5;
    if (!FRONT_FACING) { NORMAL = -NORMAL; }
    BACKLIGHT = pixel.rgb * vec3(0.9, 1.08, 0.55) * leaf_translucency;
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