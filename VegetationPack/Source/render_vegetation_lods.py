import bpy,sys,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--')+1]);PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack'
bpy.ops.wm.open_mainfile(filepath=str(PACK/'Blender'/'Thornwake_Woodland.blend'))
base=bpy.data.scenes['00_Woodland_Contact_Sheet'];scene=bpy.data.scenes.new('QA_LOD');bpy.context.window.scene=scene
scene.world=base.world;scene.render.engine='BLENDER_EEVEE';scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.render.film_transparent=True;scene.render.image_settings.color_mode='RGBA'
for obj in base.objects:
    if obj.type=='LIGHT':scene.collection.objects.link(obj)
cd=bpy.data.cameras.new('LOD_Camera');cam=bpy.data.objects.new('LOD_Camera',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO';cd.ortho_scale=18
cam.location=(16,-26,22);cam.rotation_euler=(Vector((0,0,6))-cam.location).to_track_quat('-Z','Y').to_euler()
instance=bpy.data.objects.new('LOD_Tree',None);instance.instance_type='COLLECTION';scene.collection.objects.link(instance)
scene.render.resolution_x=420;scene.render.resolution_y=550;scene.render.resolution_percentage=100
for name in ['Pine_A','Birch_Green_A','Oak_A','Willow_A']:
    for lod in range(3):
        instance.instance_collection=bpy.data.collections['TREE_'+name+'_LOD'+str(lod)]
        scene.render.filepath=str(PACK/'Previews'/('LOD_'+name+'_'+str(lod)+'.png'));bpy.ops.render.render(write_still=True)
print('LOD_RENDERS_PASS',flush=True)
