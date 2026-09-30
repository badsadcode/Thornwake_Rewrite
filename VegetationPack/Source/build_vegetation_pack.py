"""Thornwake woodland authoring: deterministic, low-poly, actual textured Blender meshes.
Run Blender 5.1 --background --factory-startup --python tools/build_vegetation_pack.py -- <root>.
"""
import bpy, math, random, json, sys
from pathlib import Path
from mathutils import Vector
from math import sin,cos,pi
ROOT=Path(sys.argv[sys.argv.index('--')+1]);PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack';TEX=PACK/'Textures'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
SPECS=[
 ('Pine_A','pine',101,12.4,2.85,.28),('Pine_B','pine',247,14.7,2.45,.31),('Pine_C','pine',393,10.6,3.00,.30),
 ('Spruce_A','spruce',521,13.2,3.10,.32),('Spruce_B','spruce',699,11.4,3.25,.34),
 ('Birch_Green_A','birch',731,10.5,2.60,.20),('Birch_Green_B','birch',882,12.3,2.25,.19),('Birch_Green_C','birch',946,9.0,2.3,.23),
 ('Birch_Autumn_A','birch_autumn',1121,10.8,2.65,.21),('Birch_Autumn_B','birch_autumn',1293,9.7,2.4,.24),
 ('Oak_A','oak',1331,10.8,4.05,.52),('Oak_B','oak',1462,12.0,4.55,.60),('Oak_C','oak',1588,8.7,3.65,.55),
 ('Maple_Autumn_A','maple',1679,10.4,3.65,.37),('Sparse_A','sparse',1793,8.4,3.15,.29),
 ('Poplar_A','poplar',1864,14.0,1.60,.27),('Willow_A','willow',1951,10.0,4.25,.57)]
MATERIALS={};MANIFEST=[]

def material(species,kind,variant="A"):
    foliage={'pine':'Pine','spruce':'Spruce','birch':'Birch_Green','birch_autumn':'Birch_Autumn','oak':'Oak','maple':'Maple_Autumn','sparse':'Sparse','poplar':'Birch_Green','willow':'Willow'}[species]
    bark='Birch' if species.startswith('birch') else 'Pine' if species in ['pine','spruce'] else 'Willow' if species in ['willow','poplar'] else 'Oak'
    part=bark if kind=='Bark' else foliage
    name=f'MAT_Tree_{part}_{kind}'+('' if variant=='A' else '_'+variant)
    if name in MATERIALS:return MATERIALS[name]
    mat=bpy.data.materials.new(name);mat.use_nodes=True;mat.diffuse_color=(.24,.31,.16,1) if kind!='Bark' else (.27,.22,.16,1)
    nodes=mat.node_tree.nodes;nodes.clear();out=nodes.new('ShaderNodeOutputMaterial');shader=nodes.new('ShaderNodeBsdfPrincipled');tex=nodes.new('ShaderNodeTexImage')
    resolution=32 if kind=='Card' else 64
    tex.image=bpy.data.images.load(str(TEX/f'T_Tree_{part}_{kind}_{resolution}.png'),check_existing=True);tex.interpolation='Closest';tex.extension='REPEAT'
    tex.image.pack();tex.image.filepath=f'//../Textures/T_Tree_{part}_{kind}_{resolution}.png'
    shader.inputs['Roughness'].default_value=.93;shader.inputs['Metallic'].default_value=0;shader.inputs['Specular IOR Level'].default_value=.1
    mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color']);mat.node_tree.links.new(shader.outputs['BSDF'],out.inputs['Surface'])
    if variant!='A':
        tint=nodes.new('ShaderNodeMix');tint.data_type='RGBA';tint.blend_type='MULTIPLY';tint.inputs[0].default_value=1
        next(s for s in tint.inputs if s.name=='B' and s.type=='RGBA').default_value=(.91,.97,.92,1) if variant=='B' else (.98,.94,.87,1)
        mat.node_tree.links.new(tex.outputs['Color'],next(s for s in tint.inputs if s.name=='A' and s.type=='RGBA'));mat.node_tree.links.new(next(s for s in tint.outputs if s.type=='RGBA'),shader.inputs['Base Color']);tint.location=(-140,100)
    if kind=='Card':
        clip=nodes.new('ShaderNodeMath');clip.operation='ROUND';clip.location=(-140,-160)
        mat.node_tree.links.new(tex.outputs['Alpha'],clip.inputs[0]);mat.node_tree.links.new(clip.outputs[0],shader.inputs['Alpha']);mat.surface_render_method='DITHERED';mat.alpha_threshold=.5;mat.use_transparency_overlap=False
        # Binary source alpha is exported as glTF MASK; verify/patch exporter alphaMode below.
        mat['alpha_mode']='MASK';mat.use_backface_culling=False
    else:mat.use_backface_culling=True
    tex.location=(-320,40);shader.location=(0,40);out.location=(310,40)
    MATERIALS[name]=mat;return mat

class MeshBuilder:
    def __init__(self,world_uv=False):self.verts=[];self.faces=[];self.uvs=[];self.world_uv=world_uv
    def face(self,coords,uvs):
        if self.world_uv:
            normal=(Vector(coords[1])-Vector(coords[0])).cross(Vector(coords[2])-Vector(coords[0]))
            axis=max(range(3),key=lambda i:abs(normal[i]));axes=[i for i in range(3) if i!=axis]
            uvs=[(round(p[axes[0]]/3.5*64)/64,round(p[axes[1]]/3.5*64)/64) for p in coords]
            area=abs(sum(uvs[i][0]*uvs[(i+1)%len(uvs)][1]-uvs[(i+1)%len(uvs)][0]*uvs[i][1] for i in range(len(uvs))))
            if area<1e-10:uvs=[(p[axes[0]]/3.5,p[axes[1]]/3.5) for p in coords]
        start=len(self.verts);self.verts.extend([tuple(v) for v in coords]);self.faces.append(tuple(range(start,start+len(coords))));self.uvs.append(uvs)
    def object(self,name,collection,mat):
        mesh=bpy.data.meshes.new(name+'_Mesh');mesh.from_pydata(self.verts,[],self.faces);mesh.update()
        uv=mesh.uv_layers.new(name='UVMap')
        for poly,coords in zip(mesh.polygons,self.uvs):
            for index,point in zip(poly.loop_indices,coords):uv.data[index].uv=point
            poly.use_smooth=False
        obj=bpy.data.objects.new(name,mesh);collection.objects.link(obj);mesh.materials.append(mat)
        return obj

def uvgrid(x):return round(x*64)/64

def tube(builder,points,radii,sides=6):
    points=[Vector(p) for p in points];rings=[];lengths=[0.0]
    for i,p in enumerate(points):
        tangent=(points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
        if i==0 and abs(p.z)<.00001:tangent=Vector((0,0,1))
        helper=Vector((0,0,1)) if abs(tangent.z)<.93 else Vector((1,0,0))
        a=tangent.cross(helper).normalized();b=tangent.cross(a).normalized()
        rings.append([p+(a*cos(j*2*pi/sides)+b*sin(j*2*pi/sides))*radii[i] for j in range(sides)])
        if i:lengths.append(lengths[-1]+(p-points[i-1]).length)
    for i in range(len(points)-1):
        for j in range(sides):
            u0=uvgrid(j/sides*2*pi*max(radii[i],.025)/1.6);u1=uvgrid((j+1)/sides*2*pi*max(radii[i],.025)/1.6)
            if u1<=u0:u1=u0+1/64
            v0=uvgrid(lengths[i]/2.0);v1=max(v0+1/64,uvgrid(lengths[i+1]/2.0))
            builder.face([rings[i][j],rings[i][(j+1)%sides],rings[i+1][(j+1)%sides],rings[i+1][j]],[(u0,v0),(u1,v0),(u1,v1),(u0,v1)])
    for ring,reverse in [(rings[0],True),(rings[-1],False)]:
        order=list(reversed(ring)) if reverse else ring
        builder.face(order,[(.5+.4*cos(j*2*pi/sides),.5+.4*sin(j*2*pi/sides)) for j in range(sides)])

def card(builder,center,u,v):
    center,u,v=Vector(center),Vector(u),Vector(v)
    builder.face([center-u-v,center+u-v,center+u+v,center-u+v],[(0,0),(1,0),(1,1),(0,1)])

def blob(builder,cards,center,scale,rng,lod,sparse=False):
    c=Vector(center);sx,sy,sz=scale
    # Small interior core plus volumetric leaf sprays: avoid solid, round "bowl" crowns at close range.
    core=1.25 if lod==2 else .44
    segments=7 if lod==0 else 6 if lod==1 else 5
    rings=[];angles=[2*pi*j/segments for j in range(segments)]
    for k,(z,r) in enumerate([(-.65,.50),(-.06,.95),(.52,.70)]):
        rings.append([c+Vector((cos(a)*sx*r*rng.uniform(.88,1.12),sin(a)*sy*r*rng.uniform(.88,1.12),sz*(z+rng.uniform(-.12,.12))))*core for a in angles])
    top=c+Vector((sx*.12,sy*-.1,sz*.93))*core;bottom=c+Vector((0,0,-sz*.94))*core
    for j in range(segments):
        n=(j+1)%segments
        builder.face([bottom,rings[0][n],rings[0][j]],[(.5,0),(1,.5),(0,.5)])
        for k in range(2):builder.face([rings[k][j],rings[k][n],rings[k+1][n],rings[k+1][j]],[(0,k*.5),(1,k*.5),(1,(k+1)*.5),(0,(k+1)*.5)])
        builder.face([rings[2][j],rings[2][n],top],[(0,0),(1,0),(.5,1)])
    if lod<2:
        count=18 if lod==0 else 10
        phase=rng.uniform(0,2*pi)
        for j in range(count):
            z=1-2*(j+.5)/count;rho=math.sqrt(max(0,1-z*z));a=j*2.39996+phase
            normal=Vector((cos(a)*rho,sin(a)*rho,z))
            tangent=normal.cross(Vector((0,0,1))).normalized();up=normal.cross(tangent).normalized()
            pos=c+Vector((normal.x*sx,normal.y*sy,normal.z*sz))*.48
            u=tangent*min(sx,sy)*rng.uniform(.67,.85)
            v=up*sz*rng.uniform(.76,.98)
            card(cards,pos,u,v)
        # Crossed interior sprays carry leaf detail all the way to branch tips, without individual leaves.
        card(cards,c,(sx*.78,0,0),(0,0,sz*.83))
        card(cards,c,(0,sy*.78,0),(0,0,sz*.83))
        if lod==0:card(cards,c+Vector((0,0,sz*.2)),(sx*.8,0,0),(0,sy*.8,0))
def conifer_frond(foliage,cards,start,end,width,rng,lod):
    start,end=Vector(start),Vector(end);d=end-start;side=Vector((-d.y,d.x,0)).normalized()*width
    ridge=start+d*.42+Vector((0,0,width*.20))
    left=start+d*.48+side;right=start+d*.54-side
    # Branch-shaped folded fronds rather than full cone skirts.
    for verts,uvs in [([start,left,ridge],[(.5,0),(0,.48),(.5,.5)]),([left,end,ridge],[(0,.48),(.5,1),(.5,.5)]),([start,ridge,right],[(.5,0),(.5,.5),(1,.52)]),([ridge,end,right],[(.5,.5),(.5,1),(1,.52)])]:
        if (verts[1]-verts[0]).cross(verts[2]-verts[0]).z<0:verts.reverse();uvs.reverse()
        foliage.face(verts,uvs)
    if lod<2:
        card(cards,start+d*.82,side*.75,d*.31+Vector((0,0,.07)))
        if lod==0:
            card(cards,start+d*.43+side*.35,side*.64,d*.31+Vector((0,0,.07)))
            card(cards,start+d*.53-side*.35,side*.64,d*.29-Vector((0,0,.07)))

def make_tree(spec,lod):
    short,species,seed,h,radius,base=spec;name='TREE_'+short; rng=random.Random(seed) # structural randomness stays identical across LODs
    coll=bpy.data.collections.new(name+('_LOD'+str(lod))); scene=bpy.data.scenes.new(name+'_LOD'+str(lod));scene.collection.children.link(coll)
    scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1;scene.unit_settings.length_unit='METERS'
    root=bpy.data.objects.new(name if lod==0 else name+'_LOD'+str(lod),None);coll.objects.link(root);root.empty_display_type='PLAIN_AXES';root.empty_display_size=.3
    root['species']=species;root['seed']=seed;root['lod']=lod;root['metres']=True
    bark=MeshBuilder();foliage=MeshBuilder(world_uv=True);cards=MeshBuilder();supports=[]
    bend=rng.uniform(.10,.38) if species not in ['sparse','willow'] else .9
    heading=rng.uniform(0,2*pi)
    def trunk_at(f):return Vector((sin(f*(8 if species=='sparse' else 3.2))*bend*cos(heading)+f*f*bend*.25,sin(f*4)*bend*sin(heading),f*h))
    trunk_top=.98 if species in ['pine','spruce'] else .88 if species=='poplar' else .82 if species.startswith('birch') else .72
    ring_count=8 if lod==0 else 6 if lod==1 else 4
    pts=[trunk_at(trunk_top*i/(ring_count-1)) for i in range(ring_count)]
    radii=[base*(1-trunk_top*i/(ring_count-1))**.9*(1.65 if i==0 else 1) for i in range(ring_count)]
    tube(bark,pts,radii,7 if lod==0 else 6 if lod==1 else 5)
    if lod<2:
        for j in range(5):
            a=j*2*pi/5+.3;st=trunk_at(.025);tip=Vector((cos(a)*base*2.9,sin(a)*base*2.9,.07))
            tube(bark,[st,st.lerp(tip,.55)+Vector((0,0,.05)),tip],[base*.38,base*.21,.03],5)
    if species in ['pine','spruce']:
        tiers=9 if species=='spruce' else 7 if short!='Pine_B' else 8
        bottom=.17 if species=='spruce' else .27 if short!='Pine_B' else .33
        for tier in range(tiers):
            f=bottom+(1-bottom-.06)*(tier/(tiers-1))+rng.uniform(-.018,.018)
            r=radius*(1-(f-bottom)/(1-bottom))**.79*rng.uniform(.9,1.12)
            count=6 if species=='spruce' else 5;offset=rng.uniform(0,2*pi)
            for j in range(count):
                a=offset+j*2*pi/count+rng.uniform(-.14,.14);length=r*rng.uniform(.8,1.12)
                start=trunk_at(f);end=start+Vector((cos(a)*length,sin(a)*length,-.3-length*.11))
                mid=start.lerp(end,.52)+Vector((0,0,.05));br=base*(1-f)*.58
                if lod<2:tube(bark,[start,mid,end],[max(.025,br),max(.015,br*.5),.008],5 if lod==0 else 4)
                width=length*(.38 if species=='spruce' else .44)
                conifer_frond(foliage,cards,start+Vector((0,0,.60)),end,width,rng,lod)
                if lod==0:
                    side=Vector((-sin(a),cos(a),0))
                    for sign in [-1,1]:
                        fork=mid+side*length*.32*sign+Vector((cos(a)*length*.24,sin(a)*length*.24,-.17))
                        tube(bark,[mid,fork],[max(.016,br*.4),.006],4)
                        conifer_frond(foliage,cards,mid+Vector((0,0,.12)),fork,width*.65,rng,1)
        # Uneven little terminal spear keeps the upper crown thin and attached to the leader.
        tube(foliage,[trunk_at(.90),trunk_at(.97),trunk_at(1.01)],[.42,.21,.012],5)
    else:
        number={'birch':10,'birch_autumn':10,'oak':10,'maple':10,'sparse':7,'poplar':14,'willow':8}[species]
        for j in range(number):
            f=.30+j/(number-1)*(.48 if species!='poplar' else .57)+rng.uniform(-.025,.025)
            if species in ['oak','maple','willow']:f=.23+j/(number-1)*.34+rng.uniform(-.04,.04)
            a=j*2.39996+rng.uniform(-.32,.32)+heading
            r=radius*rng.uniform(.66,1.05)*(1-.48*max(0,(f-.60)/.3))
            if species in ['oak','maple','willow']:r*=1-.52*j/(number-1)
            if species=='poplar':r*=.66
            rise=h*(.19 if species.startswith('birch') or species=='poplar' else .24)*rng.uniform(.78,1.22)
            if species=='sparse':rise*=.8
            start=trunk_at(min(f,trunk_top));elbow=start+Vector((cos(a)*r*.46,sin(a)*r*.46,rise*.38))
            end=start+Vector((cos(a+.12)*r,sin(a+.12)*r,rise))
            br=base*(.42 if species in ['oak','willow'] else .32)*(1-f*.6)
            tube(bark,[start,elbow,end],[br,br*.64,.02],6 if lod==0 else 5 if lod==1 else 4)
            groups=3 if species in ['oak','maple','willow'] else 2 if species not in ['poplar','sparse'] else 1
            for k in range(groups):
                ang=a+(k-(groups-1)/2)*.85+rng.uniform(-.20,.20)
                origin=elbow.lerp(end,.52 if k%2 else .78)
                leaf=end+Vector((cos(ang)*r*.25,sin(ang)*r*.25,rng.uniform(.12,.6)))
                if species=='poplar':leaf=end+Vector((0,0,.45))
                sz=(radius*(.40 if species in ['oak','maple'] else .31))*rng.uniform(.8,1.08)
                scale=(sz,sz*rng.uniform(.80,1.05),sz*(.78 if species=='oak' else 1.10))
                if species=='poplar':scale=(.65,.64,1.55)
                if species=='sparse':scale=(sz*.82,sz*.75,sz*.61)
                if species=='willow':scale=(1.20,1.10,.72)
                include=lod==0 or lod==1 or k==0
                if include:
                    tube(bark,[origin,leaf],[br*.43,.01],5 if lod==0 else 4)
                    blob(foliage,cards,leaf,scale,random.Random(seed+3000+j*37+k*7),lod,species=='sparse');supports.append(tuple(leaf))
                    if species=='willow':
                        strands=7 if lod==0 else 4 if lod==1 else 3
                        strand_rng=random.Random(seed+j*71+k*11)
                        for n in range(strands):
                            theta=n*2*pi/strands+strand_rng.uniform(-.2,.2);off=Vector((cos(theta)*.85,sin(theta)*.85,0))
                            length=strand_rng.uniform(2.1,3.9);top=leaf+off
                            bottom=top+Vector((cos(theta)*.28,sin(theta)*.28,-length))
                            bottom.z=max(1.0,bottom.z)
                            u=Vector((-sin(theta),cos(theta),0))*.50
                            card(cards,(top+bottom)/2,u,(top-bottom)/2)
                            if lod==0 and n%3==0:tube(bark,[leaf,top,top.lerp(bottom,.8)],[.015,.010,.004],4)
        tip=trunk_at(trunk_top)+Vector((0,0,.6 if species!='poplar' else .5))
        blob(foliage,cards,tip,(radius*.36,radius*.33,h*.09),rng,lod)
    objects=[]
    for builder,kind in [(bark,'Bark'),(foliage,'Foliage'),(cards,'Card')]:
        if builder.faces:
            obj=builder.object(root.name+'__'+kind,coll,material(species,kind,short[-1]));obj.parent=root;objects.append(obj)
    # Every asset object stays at identity and vertices are already in metre-space.
    root['variation_controls']='Edit seed, height, crown radius, trunk radius in build_vegetation_pack.py SPECS; meshes remain directly editable.'
    points=[v.co for o in objects for v in o.data.vertices]
    lo=[min(p[i] for p in points) for i in range(3)];hi=[max(p[i] for p in points) for i in range(3)]
    triangles=sum(len(p.vertices)-2 for o in objects for p in o.data.polygons)
    return scene,coll,root,objects,{'id':name,'species':species,'seed':seed,'lod':lod,'triangles':triangles,'objects':len(objects),'bounds_min':lo,'bounds_max':hi,'height_m':hi[2]-lo[2],'width_m':hi[0]-lo[0],'texture_size':64,'foliage_supports':len(supports)}

primary=[]
for spec in SPECS:
    for lod in range(3):
        scene,coll,root,objects,record=make_tree(spec,lod);bpy.context.window.scene=scene
        for o in scene.objects:o.select_set(True)
        bpy.context.view_layer.objects.active=root
        path=PACK/'Exports'/(record['id']+('' if lod==0 else '_LOD'+str(lod))+'.glb')
        bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,export_yup=True,export_apply=True,export_extras=True,export_materials='EXPORT')
        record['file']='Exports/'+path.name;MANIFEST.append(record)
        if lod==0:primary.append((spec,coll,record))
        print('TREE_EXPORTED',record['id'],lod,record['triangles'],flush=True)

# Blender evaluation scene uses intentional collection instances, source scenes remain at world origin.
preview=bpy.data.scenes.new('00_Woodland_Contact_Sheet');bpy.context.window.scene=preview
preview.unit_settings.system='METRIC';preview.unit_settings.scale_length=1
for i,(spec,collection,record) in enumerate(primary):
    obj=bpy.data.objects.new('DISPLAY_'+record['id'],None);preview.collection.objects.link(obj);obj.instance_type='COLLECTION';obj.instance_collection=collection
    obj.location=((i%6)*13,(i//6)*17,0)
    curve=bpy.data.curves.new('LABEL_'+spec[0],'FONT');curve.body=spec[0].replace('_',' ');curve.align_x='CENTER';curve.size=.45
    label=bpy.data.objects.new('LABEL_'+spec[0],curve);preview.collection.objects.link(label);label.location=(obj.location.x,-3.8+obj.location.y,.03)
# Simple studio ground and neutral daylight, reusable by the render script.
world=bpy.data.worlds.new('Neutral_Daylight');world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.45,.48,.51,1);world.node_tree.nodes['Background'].inputs[1].default_value=.8;preview.world=world
bpy.ops.mesh.primitive_plane_add(size=220,location=(32,16,-.02));ground=bpy.context.object;ground.name='PREVIEW_Ground'
mat=bpy.data.materials.new('MAT_Preview_Ground');mat.diffuse_color=(.18,.205,.17,1);ground.data.materials.append(mat)
ld=bpy.data.lights.new('PREVIEW_Daylight','SUN');ld.energy=2.6;ld.angle=.12;light=bpy.data.objects.new('PREVIEW_Daylight',ld);preview.collection.objects.link(light);light.rotation_euler=(.35,-.52,-.4)
cd=bpy.data.cameras.new('PREVIEW_Camera');cam=bpy.data.objects.new('PREVIEW_Camera',cd);preview.collection.objects.link(cam);cam.location=(76,-89,70);cam.rotation_euler=(Vector((32,17,5))-cam.location).to_track_quat('-Z','Y').to_euler();cd.type='ORTHO';cd.ortho_scale=95;preview.camera=cam
for index,rotation in enumerate([(.4,.7,2.3),(-.8,-.4,1.4)]):
    filldata=bpy.data.lights.new('PREVIEW_Fill_'+str(index),'SUN');filldata.energy=.75;filldata.use_shadow=False;fill=bpy.data.objects.new(filldata.name,filldata);preview.collection.objects.link(fill);fill.rotation_euler=rotation
preview.render.engine='BLENDER_EEVEE';preview.cycles.samples=64;preview.render.resolution_x=2400;preview.render.resolution_y=1500;preview.render.resolution_percentage=100
preview.view_settings.view_transform='Standard';preview.view_settings.look='Medium High Contrast' if 'Medium High Contrast' in [] else 'None';preview.view_settings.exposure=0;preview.view_settings.gamma=1
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.clip_end=1000;area.spaces.active.shading.type='MATERIAL';area.spaces.active.region_3d.view_distance=70;area.spaces.active.region_3d.view_location=(32,17,5)
text=bpy.data.texts.new('START_HERE');text.write('THORNWAKE WOODLAND PACK\n17 handmade-style low-poly tree variants with generated pixel textures.\n1 unit = 1 metre. Z-up, GLB exports Y-up.\n00_Woodland_Contact_Sheet contains preview instances and neutral lighting only.\nEach TREE_*_LOD0 scene contains the actual asset at origin. LOD1 and LOD2 are separate scenes and GLBs.\nMaterials use packed 64px images and Closest interpolation.\nNo animation or physics colliders are included. Use a simple trunk collider in the game.\nProcedural authoring source and seed table: tools/build_vegetation_pack.py.\nRead the pack README for controls, budgets, import settings and validation.\n')
for unused in list(bpy.data.scenes):
    if len(unused.objects)==0:bpy.data.scenes.remove(unused)
bpy.ops.wm.save_as_mainfile(filepath=str(PACK/'Blender'/'Thornwake_Woodland.blend'),compress=True)
(PACK/'manifest.json').write_text(json.dumps(MANIFEST,indent=2))
print('VEGETATION_BUILD_PASS',len(primary),'trees',len(MANIFEST),'exports',flush=True)
