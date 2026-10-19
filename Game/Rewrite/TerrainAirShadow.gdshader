// Rewrite world terrain with far mountain shadows from the air map (D154, setting Light and shadow in the haze).
shader_type spatial;
render_mode diffuse_burley;
#define AIR_SHADOW
#include "res://Game/Rewrite/TerrainBody.gdshaderinc"
