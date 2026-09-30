"""Render actual saved models for visual review, never concept images."""
import bpy,sys,json,math
from pathlib import Path
from mathutils import Vector
ROOT=Path(sys.argv[sys.argv.index('--')+1]);PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack'
bpy.ops.wm.open_mainfile(filepath=str(PACK/'Blender'/'Thornwake_Woodland.blend'))
manifest=json.loads((PACK/'manifest.json').read_text());base=bpy.data.scenes['00_Woodland_Contact_Sheet']
scene=bpy.data.scenes.new('QA_Render');bpy.context.window.scene=scene
scene.world=base.world
scene.render.engine='BLENDER_EEVEE';scene.cycles.samples=64;scene.cycles.use_denoising=False
scene.view_settings.view_transform='Standard';scene.view_settings.look='None';scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
for obj in base.objects:
    if obj.type=='LIGHT':scene.collection.objects.link(obj)
cd=bpy.data.cameras.new('QA_Camera');cam=bpy.data.objects.new('QA_Camera',cd);scene.collection.objects.link(cam);scene.camera=cam;cd.type='ORTHO'
instance=bpy.data.objects.new('QA_Tree',None);scene.collection.objects.link(instance);instance.instance_type='COLLECTION'
mode=sys.argv[-1]
records=[r for r in manifest if r['lod']==0]
if mode=='draft':records=[r for r in records if r['id'] in ['TREE_Pine_A','TREE_Birch_Green_A','TREE_Oak_A','TREE_Willow_A']]
for r in records:
    instance.instance_collection=bpy.data.collections[r['id']+'_LOD0']
    cam.location=(19,-28,23);target=Vector((0,0,7));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();cd.ortho_scale=max(18.5,(r["width_m"]+1.0)*620/420)
    # Fit camera-space bounds, including asymmetric crowns; world X width alone can crop willow branches.
    rotation=cam.rotation_euler.to_matrix();inverse=rotation.transposed()
    points=[inverse@(o.matrix_world@v.co) for o in instance.instance_collection.all_objects if o.type=='MESH' for v in o.data.vertices]
    low=[min(p[i] for p in points) for i in range(2)];high=[max(p[i] for p in points) for i in range(2)]
    current=inverse@cam.location;cam.location+=rotation@Vector(((low[0]+high[0])/2-current.x,(low[1]+high[1])/2-current.y,0))
    cd.ortho_scale=max(17.5,(high[1]-low[1])*1.15,(high[0]-low[0])*620/420*1.15)
    scene.render.resolution_x=420;scene.render.resolution_y=620;scene.render.resolution_percentage=100
    scene.render.filepath=str(PACK/'Previews'/(r['id']+'.png'));bpy.ops.render.render(write_still=True);print('RENDERED',r['id'],flush=True)
if mode!='draft':
    # Close tree / player-height bark views and raised gameplay view.
    for name,loc,target,ortho in [
      ('Birch_Close',(5,-8,4),(0,0,3.4),6),('Pine_Close',(6,-9,4),(0,0,4.8),7),
      ('Oak_Close',(7,-10,5),(0,0,5),8),('Willow_Close',(8,-11,6),(0,0,5),10),
      ('Oak_Gameplay',(16,-23,24),(0,0,4),21)]:
        tree={'Birch':'TREE_Birch_Green_A','Pine':'TREE_Pine_A','Oak':'TREE_Oak_A','Willow':'TREE_Willow_A'}[name.split('_')[0]]
        instance.instance_collection=bpy.data.collections[tree+'_LOD0'];cam.location=loc;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cd.ortho_scale=ortho
        scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.filepath=str(PACK/'Previews'/(name+'.png'));bpy.ops.render.render(write_still=True)
    bpy.context.window.scene=base;base.render.resolution_x=2200;base.render.resolution_y=1450;base.cycles.samples=20;base.cycles.use_denoising=False;base.render.filepath=str(PACK/'Previews'/'Woodland_Preview_Scene.png');bpy.ops.render.render(write_still=True)
print('RENDER_REVIEW_COMPLETE',flush=True)
