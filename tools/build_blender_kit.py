"""Build a native Blender art kit from Thornwake's measured GLB references.
Run with Blender --background --factory-startup --python tools/build_blender_kit.py -- <project root>.
"""
import bpy, json, math, sys, csv
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(sys.argv[sys.argv.index('--') + 1])
KIT = ROOT / 'ArtKit'
ENTRIES = json.loads((KIT / 'asset_manifest.json').read_text(encoding='utf-8-sig'))
REPORT = []


def mesh_objects():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


def points_of(objects):
    return [o.matrix_world @ v.co for o in objects for v in o.data.vertices]


def bounds_of(objects):
    points = points_of(objects)
    return Vector([min(p[i] for p in points) for i in range(3)]), Vector([max(p[i] for p in points) for i in range(3)])


def merged_reference(objects, name, collection):
    vertices, faces, material_indices, smoothing, materials = [], [], [], [], []
    for obj in objects:
        offset = len(vertices)
        vertices.extend(tuple(obj.matrix_world @ v.co) for v in obj.data.vertices)
        slot_map = []
        for slot in obj.data.materials:
            if slot not in materials: materials.append(slot)
            slot_map.append(materials.index(slot))
        for poly in obj.data.polygons:
            faces.append(tuple(offset + i for i in poly.vertices))
            material_indices.append(slot_map[poly.material_index] if slot_map else 0)
            smoothing.append(poly.use_smooth)
    mesh = bpy.data.meshes.new(name + '_Mesh'); mesh.from_pydata(vertices, [], faces); mesh.update()
    # Roof surfaces use vertex colours; keep these when merging library/reference meshes.
    attribute_names = {a.name for obj in objects for a in obj.data.color_attributes}
    for attribute_name in attribute_names:
        attribute = mesh.color_attributes.new(name=attribute_name, type='FLOAT_COLOR', domain='CORNER')
        colors=[]
        for obj in objects:
            source=obj.data.color_attributes.get(attribute_name)
            for polygon in obj.data.polygons:
                for loop_index in polygon.loop_indices:
                    source_index=loop_index if source and source.domain=='CORNER' else obj.data.loops[loop_index].vertex_index
                    colors.extend(source.data[source_index].color if source else (1,1,1,1))
        attribute.data.foreach_set('color',colors)
    for material in materials:
        if material: mesh.materials.append(material)
    for poly, material, smooth in zip(mesh.polygons, material_indices, smoothing):
        poly.material_index = material; poly.use_smooth = smooth
    obj = bpy.data.objects.new(name, mesh); collection.objects.link(obj)
    return obj


def box_guide(collection, name, low, high):
    vertices = [(x,y,z) for x in (low[0],high[0]) for y in (low[1],high[1]) for z in (low[2],high[2])]
    edges = [(i,j) for i in range(8) for j in range(i+1,8) if bin(i ^ j).count('1') == 1]
    mesh = bpy.data.meshes.new(name); mesh.from_pydata(vertices,edges,[])
    obj = bpy.data.objects.new(name,mesh); collection.objects.link(obj)
    obj.display_type='WIRE'; obj.show_in_front=True; obj.hide_select=True; obj.hide_render=True
    obj.color=(0.2,0.7,1.0,1.0)
    return obj


def label(collection, name, text, location, size=.16):
    curve=bpy.data.curves.new(name,'FONT'); curve.body=text; curve.size=size
    obj=bpy.data.objects.new(name,curve); collection.objects.link(obj); obj.location=location
    obj.hide_select=True
    return obj


def configure_view(center, size):
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                space=area.spaces.active; space.clip_end=2000
                space.shading.type='SOLID'; space.shading.color_type='MATERIAL'
                space.overlay.show_floor=True
                space.region_3d.view_distance=max(2.5,size*1.75)
                space.region_3d.view_location=center
                space.region_3d.view_rotation=Quaternion((0.88,0.31,-0.16,-0.31)).normalized()
                space.region_3d.view_perspective='ORTHO'


EXPORT_HELPER = r'''# Select this whole text and Run Script to export ONLY the MODEL collection.
# Save As a working copy before modifying a supplied template.
import bpy
from pathlib import Path
scene=bpy.context.scene
collection=bpy.data.collections.get('MODEL')
if collection is None: raise RuntimeError('Open an individual Thornwake template first.')
asset_id=scene['thornwake_asset_id']
folder=Path(bpy.data.filepath).parent / 'Exports'
folder.mkdir(parents=True,exist_ok=True)
if bpy.context.object and bpy.context.object.mode!='OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
for obj in collection.all_objects:
    obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active=next(iter(collection.objects))
bpy.ops.export_scene.gltf(filepath=str(folder/(asset_id+'.glb')),export_format='GLB',use_selection=True,export_yup=True)
print('EXPORTED',folder/(asset_id+'.glb'))
'''

for number, entry in enumerate(ENTRIES,1):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1.0; scene.unit_settings.length_unit='METERS'
    bpy.ops.import_scene.gltf(filepath=str(KIT/entry['reference']))
    bpy.context.view_layer.update()
    imported=list(scene.objects); meshes=mesh_objects()
    low,high=bounds_of(meshes)
    expected_low=Vector((entry['godotMin'][0],-entry['godotMax'][2],entry['godotMin'][1]))
    expected_high=Vector((entry['godotMax'][0],-entry['godotMin'][2],entry['godotMax'][1]))
    error=max((low-expected_low).length,(high-expected_high).length)
    if error>.002: raise RuntimeError(f"Scale/axis mismatch {entry['id']}: {low}, {high} vs {expected_low}, {expected_high}; {error}")
    model=bpy.data.collections.new('MODEL'); scene.collection.children.link(model)
    for obj in imported:
        for collection in list(obj.users_collection): collection.objects.unlink(obj)
        model.objects.link(obj)
    for obj in imported:
        if obj.name==entry['id']: obj.name='PrototypeRoot'
    root=bpy.data.objects.new(entry['id'],None); model.objects.link(root); root.empty_display_type='PLAIN_AXES'; root.empty_display_size=.3
    for obj in imported:
        if obj.parent is None:
            world=obj.matrix_world.copy(); obj.parent=root; obj.matrix_world=world
    root['asset_id']=entry['id']; root['game_id']=entry['gameId']; root['origin_contract']=entry['notes']
    root['integration']=entry['integration']; root['pitch_degrees']=entry['pitch']; root['mirror_side']='right' if entry['mirrored'] else 'left/default'
    for idx,obj in enumerate(meshes,1): obj.name=f"{entry['id']}__Part{idx:03}"
    reference=bpy.data.collections.new('REFERENCE__DO_NOT_EXPORT'); scene.collection.children.link(reference)
    snapshot=merged_reference(meshes,'REFERENCE_Prototype',reference)
    snapshot.display_type='WIRE'; snapshot.hide_select=True; snapshot.hide_render=True; snapshot.hide_set(True)
    guides=bpy.data.collections.new('GUIDES__DO_NOT_EXPORT'); scene.collection.children.link(guides)
    box_guide(guides,'GUIDE_VisualBounds',low,high)
    if entry['gridWidthM']>0:
        halfx=entry['gridWidthM']/2; halfy=entry['gridDepthM']/2
        box_guide(guides,'GUIDE_GridFootprint',(-halfx,-halfy,0),(halfx,halfy,0))
    origin=bpy.data.objects.new('GUIDE_Origin_1mAxes',None);guides.objects.link(origin)
    origin.empty_display_type='ARROWS';origin.empty_display_size=1;origin.hide_select=True;origin.hide_render=True
    dimensions=f"{entry['widthM']:.3f} W x {entry['depthM']:.3f} D x {entry['heightM']:.3f} H metres"
    label(guides,'GUIDE_AssetLabel',entry['id']+'\n'+dimensions,(low.x,low.y-.4,low.z),max(.06,min(.16,max(high-low)*.045))).hide_render=True
    readme=bpy.data.texts.new('READ_ME_FIRST')
    readme.write(f"THORNWAKE ASSET TEMPLATE\n{entry['id']}\n{dimensions}\n\n1 Blender unit = 1 metre; Z up.\nMODEL contains editable prototype meshes. Replace or rebuild these meshes under the asset root.\nREFERENCE is a locked original copy: toggle its eye in the Outliner to compare.\nGUIDES show measured visual bounds and nominal build footprint; never export them.\nKeep the MODEL root at world origin. Do not recenter hand tools or gable fills.\n{entry['notes']}\n\nIntegration: {entry['integration']}.\nOnly building appearance mappings are available now. Other families need their future visual adapter.\nCharacters are scale/pose references, not production rigs.\n\nRun the EXPORT_MODEL_GLB text to export MODEL only.\nSave a working copy outside the template kit before editing.\n")
    bpy.data.texts.new('EXPORT_MODEL_GLB').write(EXPORT_HELPER)
    scene['thornwake_asset_id']=entry['id'];scene['thornwake_game_id']=entry['gameId'];scene['thornwake_integration']=entry['integration']
    scene['thornwake_width_m']=entry['widthM'];scene['thornwake_depth_m']=entry['depthM'];scene['thornwake_height_m']=entry['heightM']
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True);bpy.context.view_layer.objects.active=root
    configure_view((low+high)/2,max(high-low))
    destination=KIT/'Templates'/entry['category']/(entry['id']+'.blend');destination.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(destination),compress=True)
    REPORT.append({'id':entry['id'],'blend':str(destination.relative_to(KIT)),'axis_scale_error_m':error,'meshes':len(meshes),'door_pivot':any(o.name=='DoorLeaf' for o in imported)})
    print(f"TEMPLATE {number}/{len(ENTRIES)} {entry['id']}",flush=True)

# One browsable scene per family; thumbnails are arranged copies, individual files are at origin.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
first=True
for category in dict.fromkeys(e['category'] for e in ENTRIES):
    scene=bpy.context.scene if first else bpy.data.scenes.new(category)
    first=False;scene.name=category;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1;scene.unit_settings.length_unit='METERS'
    bpy.context.window.scene=scene
    collection=bpy.data.collections.new(category);scene.collection.children.link(collection)
    items=[e for e in ENTRIES if e['category']==category];columns=min(5,len(items))
    for index,entry in enumerate(items):
        before=set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(KIT/entry['reference']))
        bpy.context.view_layer.update()
        added=[o for o in bpy.data.objects if o not in before];meshes=[o for o in added if o.type=='MESH']
        thumbnail=merged_reference(meshes,entry['id'],collection)
        thumbnail.location=((index%columns)*12,-(index//columns)*14,0)
        thumbnail['template_file']='Templates/'+category+'/'+entry['id']+'.blend'
        thumbnail['not_for_export']='Overview display offset only. Open the individual template at the origin.'
        for obj in added:bpy.data.objects.remove(obj,do_unlink=True)
        thumbnail.name=entry['id']
        caption=label(collection,'LABEL_'+entry['id'],entry['id']+'\n'+f"{entry['widthM']:.2f} x {entry['depthM']:.2f} x {entry['heightM']:.2f} m",(thumbnail.location.x-3,thumbnail.location.y-4,0),.30)
    scene['readme']='Choose an asset family from the Scene menu. Individual editable files live in Templates/<family>. Overview positions are display offsets, not asset origins.'
    scene['template_count']=len(items)
# Default to walls: clear first view with useful everyday modules.
bpy.context.window.scene=bpy.data.scenes['02_Walls']
configure_view(Vector((24,-21,1.5)),40)
bpy.data.texts.new('START_HERE').write('THORNWAKE ART KIT\n96 editable templates at actual game scale.\nChoose a category in the Scene selector at the top right.\nSelect an object and press Numpad . to frame it.\nOpen its matching individual file under Templates/ to work at the correct origin.\nThe library overview is a browse-only layout; do not export these translated thumbnails.\nRead README.md beside this file for workflow and asset integration status.\n')
bpy.ops.wm.save_as_mainfile(filepath=str(KIT/'Thornwake_Asset_Library.blend'),compress=True)
(KIT/'template_validation.json').write_text(json.dumps(REPORT,indent=2),encoding='utf-8')
with (KIT/'asset_dimensions.csv').open('w',newline='',encoding='utf-8-sig') as f:
    fields=['id','category','gameId','widthM','depthM','heightM','gridWidthM','gridDepthM','pitch','mirrored','integration','reference','notes']
    writer=csv.DictWriter(f,fields,extrasaction='ignore');writer.writeheader();writer.writerows(ENTRIES)
print(f"BLENDER_KIT_PASS: {len(REPORT)} templates; dimensions and axes verified",flush=True)
