// EXPERIMENTAL 3A water (round 24, Tom and Noor's brief docs/design-inbox/THORNWAKE_EXPERIMENTAL3A_PROMPT.md):
// experimental 3's waves unchanged, with foam that is born on breaking crests and then drifts away with the wind,
// breaking up into streaks and fading (no longer riding back and forth with the orbiting water), glassier and deeper
// water, more sparkle and a livelier far sea. Everything else is experimental 2/3's (WaterSea.gdshaderinc, EXP3A).
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_never;
#define FFT_OCEAN
#define EXP3A
#include "res://Game/Rewrite/WaterSea.gdshaderinc"
