// Test only (--cloud-fog-off with --cloud-split-test=4): CloudsNear without Godot's fog — neither the volumetric fog
// nor the emulated sky fog veils these clouds, while the sky behind them keeps its veil. Not used in play.
shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, fog_disabled;
#define CLOUDS_NEAR_FOG_OFF
#include "res://Game/Rewrite/CloudsNear.gdshaderinc"
