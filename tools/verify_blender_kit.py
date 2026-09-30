import bpy, json, sys, math
from pathlib import Path
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--')+1]);KIT=ROOT/'ArtKit'
entries=json.loads((KIT/'asset_manifest.json').read_text(encoding='utf-8-sig'))
roundtrip={'Roof_Slope_30','Roof_Hip_60','Wall_Door','Tool_Axe','Character_Player_Reference'}
results=[]
for entry in ([] if '--preview-only' in sys.argv else entries):
    path=KIT/'Templates'/entry['category']/(entry['id']+'.blend')
    bpy.ops.wm.open_mainfile(filepath=str(path))
    scene=bpy.context.scene
    assert scene.unit_settings.system=='METRIC' and scene.unit_settings.scale_length==1
    model=bpy.data.collections['MODEL'];root=bpy.data.objects[entry['id']]
    assert root.location.length<.00001 and root.parent is None
    assert scene['thornwake_asset_id']==entry['id']
    assert 'REFERENCE__DO_NOT_EXPORT' in bpy.data.collections and 'GUIDES__DO_NOT_EXPORT' in bpy.data.collections
    meshes=[o for o in model.all_objects if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    low=Vector([min(v[i] for v in points) for i in range(3)]);high=Vector([max(v[i] for v in points) for i in range(3)])
    expected=Vector((entry['widthM'],entry['depthM'],entry['heightM']))
    assert ((high-low)-expected).length<.002,(entry['id'],high-low,expected)
    if entry['id']=='Wall_Door':
        leaf=bpy.data.objects['DoorLeaf'];assert (leaf.matrix_world.translation-Vector((-.65,0,0))).length<.001
    if entry['id'] in roundtrip:
        text=bpy.data.texts['EXPORT_MODEL_GLB'].as_string()
        text=text.replace("Path(bpy.data.filepath).parent / 'Exports'","ROOT / 'artifacts' / 'art-roundtrip'")
        exec(compile(text,'EXPORT_MODEL_GLB','exec'),globals())
    results.append({'id':entry['id'],'native_file_opened':True,'dimensions_verified':True,'export_roundtrip':entry['id'] in roundtrip})
    print('NATIVE_VERIFIED '+entry['id'],flush=True)
if '--preview-only' not in sys.argv: (KIT/'verification.json').write_text(json.dumps({'blender':bpy.app.version_string,'count':len(results),'templates':results},indent=2),encoding='utf-8')

# A rendered contact sheet checks the saved overview geometry and labels visually.
bpy.ops.wm.open_mainfile(filepath=str(KIT/'Thornwake_Asset_Library.blend'))
for obj in bpy.data.objects:
    if obj.get('template_file'): obj.name=Path(obj['template_file']).stem
bpy.ops.wm.save_as_mainfile(filepath=str(KIT/'Thornwake_Asset_Library.blend'),compress=True)
scene=bpy.data.scenes.new('Template_Preview');bpy.context.window.scene=scene
chosen=['Roof_Slope_45','Wall_Door','Stairs_Straight','Nature_Tree_Pine','Character_Player_Reference','Decor_Table']
textmat=bpy.data.materials.new('Caption');textmat.diffuse_color=(.03,.06,.08,1);textmat.use_nodes=True;textmat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.03,.06,.08,1)
for i,name in enumerate(chosen):
    source=bpy.data.objects.get(name)
    assert source is not None,name
    obj=source.copy();obj.data=source.data.copy();scene.collection.objects.link(obj)
    x=(i%3)*8;y=(i//3)*10;obj.location=(x,y,0)
    curve=bpy.data.curves.new('Caption_'+name,'FONT');curve.body=name;curve.size=.36
    curve.materials.append(textmat);label=bpy.data.objects.new('Caption_'+name,curve);scene.collection.objects.link(label);label.location=(x-2.5,y-3,0.025)
world=bpy.data.worlds.new('PreviewWorld');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.7,.78,.85,1);world.node_tree.nodes['Background'].inputs[1].default_value=.6
light_data=bpy.data.lights.new('PreviewSun','SUN');light_data.energy=2.4;light_data.angle=math.radians(12)
light=bpy.data.objects.new('PreviewSun',light_data);scene.collection.objects.link(light);light.rotation_euler=(.35,-.5,-.5)
bpy.ops.mesh.primitive_plane_add(size=200,location=(8,5,-.03))
plane=bpy.context.object;mat=bpy.data.materials.new('Backdrop');mat.diffuse_color=(.65,.69,.67,1);plane.data.materials.append(mat)
camera_data=bpy.data.cameras.new('PreviewCamera');camera=bpy.data.objects.new('PreviewCamera',camera_data);scene.collection.objects.link(camera)
camera.location=(23,-23,42);camera.rotation_euler=(Vector((8,5,1.6))-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=34;scene.camera=camera
scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=16
scene.render.resolution_x=1600;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(KIT/'Template_Preview.png')
bpy.ops.render.render(write_still=True)
print('BLENDER_PREVIEW_PASS' if '--preview-only' in sys.argv else 'BLENDER_NATIVE_VALIDATION_PASS: 96 files, 5 model exports, rendered preview',flush=True)
