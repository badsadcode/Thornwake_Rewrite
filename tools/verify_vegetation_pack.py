"""Validate delivered Blender source and GLBs, including material filtering and LOD budgets."""
import bpy,json,sys,math,struct
from pathlib import Path
ROOT=Path(sys.argv[sys.argv.index('--')+1]);PACK=ROOT if (ROOT/'Textures').is_dir() else ROOT/'VegetationPack'
bpy.ops.wm.open_mainfile(filepath=str(PACK/'Blender'/'Thornwake_Woodland.blend'))
manifest=json.loads((PACK/'manifest.json').read_text());reports=[]
for record in manifest:
    name=record['id']+'_LOD'+str(record['lod']);scene=bpy.data.scenes[name];bpy.context.window.scene=scene
    meshes=[o for o in scene.objects if o.type=='MESH'];root=[o for o in scene.objects if o.type=='EMPTY']
    assert len(root)==1 and len(meshes) in (2,3),(name,'object count')
    points=[];zero_area=0;uv_zero=0
    for o in scene.objects:
        assert o.location.length<1e-6 and all(abs(s-1)<1e-6 for s in o.scale) and o.rotation_euler.to_matrix().is_identity,(name,o.name,'transforms')
        assert not o.hide_render and not o.hide_viewport,(name,o.name,'hidden')
    for o in meshes:
        m=o.data;m.calc_loop_triangles();points.extend(v.co for v in m.vertices)
        used={v for p in m.polygons for v in p.vertices};assert len(used)==len(m.vertices),(name,'unused vertices')
        assert len(m.uv_layers)==1,(name,'missing UV map')
        assert all(math.isfinite(n) for v in m.vertices for n in v.co),(name,'nonfinite geometry')
        zero_area+=sum(t.area<1e-9 for t in m.loop_triangles)
        for p in m.polygons:
            uv=[m.uv_layers.active.data[i].uv for i in p.loop_indices]
            area=abs(sum(uv[i].x*uv[(i+1)%len(uv)].y-uv[(i+1)%len(uv)].x*uv[i].y for i in range(len(uv))))*.5
            if area<1e-10:uv_zero+=1
        for mat in m.materials:
            textures=[n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE'];assert len(textures)==1
            assert textures[0].interpolation=='Closest' and textures[0].image.size[0] in [32,64] and textures[0].image.packed_file,(name,mat.name,'texture configuration')
    low=[min(v[i] for v in points) for i in range(3)];high=[max(v[i] for v in points) for i in range(3)]
    assert abs(low[2])<.002,(name,'not grounded',low[2])
    assert zero_area==0,(name,'degenerate geometry',zero_area)
    assert uv_zero==0,(name,'collapsed UV islands',uv_zero)
    data=(PACK/record['file']).read_bytes();assert data[:4]==b'glTF'
    count=struct.unpack_from('<I',data,12)[0];doc=json.loads(data[20:20+count])
    assert len(doc['scenes'])==1 and len(doc['meshes'])==len(meshes) and len(doc['nodes'])==len(meshes)+1,(name,'extra exported scenes or geometry')
    assert len(doc['materials'])==len(meshes) and len(doc['images'])<=len(meshes),(name,'unused material or image data')
    for mat in doc['materials']:
        if mat['name'].endswith(('_B','_C')):assert mat['pbrMetallicRoughness'].get('baseColorFactor') is not None,(name,'lost variation tint')
    assert not doc.get('cameras') and not doc.get('animations'),(name,'unexpected nodes')
    assert all(s.get('magFilter')==9728 and s.get('minFilter') in [9728,9984] for s in doc.get('samplers',[])),(name,'smoothing')
    for mat in doc['materials']:
        assert mat.get('alphaMode','OPAQUE') in ['OPAQUE','MASK'],(name,'soft transparency')
        if '_Card' in mat['name']:assert mat.get('alphaMode')=='MASK' and mat.get('doubleSided'),(name,'cutout configuration')
    assert all('TEXCOORD_0' in p['attributes'] for m in doc['meshes'] for p in m['primitives'])
    reports.append({'asset':name,'triangles':record['triangles'],'grounded':True,'identity_transforms':True,'uv_maps':True,'nearest_filtering':True,'masked_cards':True,'degenerate_triangles':zero_area,'collapsed_uv_faces':uv_zero,'no_hidden_geometry':True,'embedded_images':len(doc.get('images',[]))})
for r in manifest:
    if r['lod']!=0:continue
    levels=[x for x in manifest if x['id']==r['id']]
    assert levels[0]['triangles']>levels[1]['triangles']>levels[2]['triangles']
    for low in levels[1:]:
        assert abs(low['height_m']-r['height_m'])/r['height_m']<.18,(r['id'],'LOD height drift')
        assert abs(low['width_m']-r['width_m'])/r['width_m']<.35,(r['id'],'LOD width drift')
(PACK/'Reports'/'asset_validation.json').write_text(json.dumps({'blender':bpy.app.version_string,'assets':len(reports),'checks':reports},indent=2))
print('VEGETATION_ASSET_VALIDATION_PASS',len(reports),'native scenes and GLBs',flush=True)
