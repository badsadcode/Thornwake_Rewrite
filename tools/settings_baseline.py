import re, os, sys
src=open('Game/Rewrite/GameSettings.cs',encoding='utf-8').read()
defaults={}
for m in re.finditer(r'Add\(new\("([a-z_]+\.[a-z_]+)",\s*"[^"]*",\s*"[^"]*",\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+),\s*([-0-9.]+)', src):
    defaults[m.group(1)]=float(m.group(5))
# Recommended baseline (D201): Natural look values + defaults for the rest of the look; drafts off except these.
look_default_keys=['fx.exposure','fx.sun_style','fx.sun_glare','fx.storm_darkness','fx.forest_darkness','fx.leaf_translucency','fx.dof',
 'fx.height_haze','fx.grading','fx.grading_strength','sky.biome_moods','sky.lens_style','sky.cloud_caps','sky.cloud_body','sky.cloud_sea',
 'sky.cloud_terrain_shadow','sky.cloud_soft','sky.cloud_shading','sky.cloud_fine','sky.cloud_cost','sky.cloud_turbulence','fx.rays_clouds',
 'fx.ray_lanes','sky.cloud_smooth','sky.cloud_shadows','sky.cloud_look','sky.cloud_blend']
values={k:defaults[k] for k in look_default_keys}
natural={"fx.haze": 1, "fx.glow": 0.6, "fx.sun_shafts": 0.6, "fx.god_ray_contrast": 0, "fx.light_beams": 1,
 "fx.forest_rays": 3, "fx.ray_scatter": 0.5, "fx.forest_sun_glare": 1, "fx.air_rays": 2, "fx.ray_weather": 1,
 "fx.atmo_contrast": 1, "fx.ray_strength": 1, "fx.ground_fog": 2, "fx.wind_waves": 1, "fx.saturation": 1}
values.update(natural)
values.update({'sky.cloud_speed':1, 'sky.cloud_base':1, 'sky.cloud_one_pass':1, 'sky.cloud_layers':1, 'sky.cloud_shape':1,
 'fx.moon_rays':1, 'fx.ambient_life':1, 'sky.cloud_quality':3})
for k in values: assert k in defaults, k
p=os.environ['APPDATA']+'/Thornwake/rewrite_settings.cfg'
lines=open(p,'rb').read().decode('utf-8').split('\n')
old={}
for i,l in enumerate(lines):
    m=re.match(r'^([a-z_]+\.[a-z_]+)=(.*)$', l.strip())
    if m: old[m.group(1)]=(i,m.group(2))
changes=[]
for k,v in values.items():
    nv=repr(float(v))
    if k in old:
        i,ov=old[k]
        if abs(float(ov)-float(v))>1e-9: changes.append((k,ov,nv)); lines[i]=f'{k}={nv}'
    else:
        if abs(defaults[k]-v)>1e-9:
            # append into the [settings] section end (file is a single section)
            lines.insert(len(lines)-1 if lines[-1]=='' else len(lines), f'{k}={nv}'); changes.append((k,'(default)',nv))
open(p,'wb').write('\n'.join(lines).encode('utf-8'))
for c in changes: print(*c)
