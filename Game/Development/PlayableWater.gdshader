shader_type spatial;
render_mode cull_disabled;
uniform bool show_status=false;
varying vec3 p;
// 2 cm presentation bias prevents coplanar flicker on almost-zero-depth ocean shelves.
// CPU surface/stage remains exactly at declared sea level; no simulation is changed.
void vertex(){p=VERTEX;VERTEX.y+=0.02;}
void fragment(){
    float a=sin(p.x*0.19634954+TIME*0.7)*cos(p.z*0.24543693-TIME*0.6);
    NORMAL=normalize(NORMAL+vec3(a*0.09,0.0,cos(p.x*0.24543693+TIME)*0.07));
    ALBEDO=show_status?vec3(0.05,0.55,0.7):vec3(0.055,0.23,0.28)+a*0.016;
    ROUGHNESS=0.24;METALLIC=0.15;SPECULAR=0.6;
}
