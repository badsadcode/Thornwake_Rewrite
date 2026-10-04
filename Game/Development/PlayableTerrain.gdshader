shader_type spatial;
render_mode diffuse_burley;
uniform bool show_grid = false;
uniform bool show_rings=false;
uniform int view_mode = 0;
void fragment() {
    float slope = 1.0-UV2.y;
    vec3 grass = vec3(0.075,0.105,0.042);
    vec3 rock = vec3(0.16,0.15,0.13);
    vec3 c = mix(grass,rock,smoothstep(0.06,0.3,slope));
    c = mix(vec3(0.24,0.21,0.14),c,smoothstep(0.0,12.0,UV2.x));
    c = mix(c,vec3(0.22,0.23,0.225),smoothstep(1800.0,3200.0,UV2.x));
    float detail = fract(sin(dot(floor(UV*512.0),vec2(12.9898,78.233)))*43758.5453)*0.006;
    c += detail;
    if(view_mode==1) c=mix(vec3(0.1,0.45,0.2),vec3(0.9,0.15,0.04),clamp(slope*3.0,0.0,1.0));
    if(view_mode==2) c=mix(vec3(0.1,0.25,0.5),vec3(0.9,0.8,0.6),clamp(UV2.x/3500.0,0.0,1.0));
    if(show_rings) c=mix(c,UV.x<1.5?vec3(0.05,0.4,0.12):vec3(0.22,0.1,0.45),0.55);
    float tile_x=UV.x>1.5?UV.x-2.0:UV.x;
    if(show_grid && min(min(tile_x,UV.y),min(1.0-tile_x,1.0-UV.y))<0.003) c=vec3(0.1,0.9,0.9);
    ALBEDO=c;ROUGHNESS=0.93;
}
