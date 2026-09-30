extends Node3D
var entries: Array = []
var trees: Array[Node3D] = []
var camera: Camera3D
var caption: Label
var level := 0
var yaw := 0.0
var pitch := 0.5
var distance := 84.0
var focus := Vector3(32,5,-17)
var capture := ""
var frame := 0
var report: Array[Dictionary] = []

func _ready():
    var close := false
    for arg in OS.get_cmdline_user_args():
        if arg.begins_with("--capture="):capture=arg.substr(10)
        if arg == "--view=close":close=true
    entries=JSON.parse_string(FileAccess.get_file_as_string("res://manifest.json"))
    var environment=WorldEnvironment.new();var settings=Environment.new()
    settings.background_mode=Environment.BG_COLOR;settings.background_color=Color("51574d")
    settings.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR;settings.ambient_light_color=Color("c6cbd1");settings.ambient_light_energy=0.45
    settings.tonemap_mode=Environment.TONE_MAPPER_LINEAR;environment.environment=settings;add_child(environment)
    var sun=DirectionalLight3D.new();sun.rotation_degrees=Vector3(-48,-32,0);sun.light_energy=0.9;sun.shadow_enabled=true;add_child(sun)
    var ground=MeshInstance3D.new();var mesh=PlaneMesh.new();mesh.size=Vector2(150,110);ground.mesh=mesh;ground.position=Vector3(32,-.03,-17)
    var material=StandardMaterial3D.new();material.albedo_color=Color(0.18,0.205,0.17);material.roughness=1;ground.material_override=material;add_child(ground)
    camera=Camera3D.new();camera.fov=42;camera.far=240;add_child(camera)
    var layer=CanvasLayer.new();add_child(layer);caption=Label.new();caption.position=Vector2(24,20);caption.add_theme_font_size_override("font_size",20);caption.add_theme_color_override("font_shadow_color",Color.BLACK);caption.add_theme_constant_override("shadow_offset_y",2);layer.add_child(caption)
    for entry in entries:
        var node=load_tree(entry,true)
        if node==null:get_tree().quit(2);return
        node.free()
    var output=FileAccess.open("res://Reports/godot_validation.json",FileAccess.WRITE);output.store_string(JSON.stringify(report,"  "));output.close()
    show_level(0)
    if close:
        # Two close-range representatives beside a 1.8m scale marker.
        for index in range(trees.size()):trees[index].visible=index in [5,10]
        trees[5].position=Vector3(-4,0,0);trees[10].position=Vector3(5,0,0)
        focus=Vector3(0,4,0);distance=28;pitch=.40;yaw=.12
        var marker=MeshInstance3D.new();var body=CapsuleMesh.new();body.height=1.8;body.radius=.24;marker.mesh=body;marker.position=Vector3(0,.9,3);add_child(marker)
    update_camera()
    print("GODOT_WOODLAND_PASS: 51 GLBs; measured bounds, embedded pixel maps, cutout alpha, nearest filtering")

func load_tree(entry: Dictionary,verify: bool) -> Node3D:
    var document=GLTFDocument.new();var state=GLTFState.new()
    if document.append_from_file("res://"+str(entry.file),state)!=OK:
        push_error("Cannot import "+str(entry.file));return null
    var root=document.generate_scene(state) as Node3D
    var stats={"valid":true,"minimum":Vector3(INF,INF,INF),"maximum":Vector3(-INF,-INF,-INF),"materials":0,"masked":0,"imported_filters":[]}
    inspect(root,Transform3D.IDENTITY,stats)
    if not stats.valid:root.free();return null
    var low=Vector3(entry.bounds_min[0],entry.bounds_min[2],-entry.bounds_max[1])
    var high=Vector3(entry.bounds_max[0],entry.bounds_max[2],-entry.bounds_min[1])
    var error=max(low.distance_to(stats.minimum),high.distance_to(stats.maximum))
    if error>0.005:push_error("Bounds changed during GLB import: "+str(entry.id));root.free();return null
    if verify:report.append({"id":entry.id,"lod":entry.lod,"bounds_error_m":error,"textured_materials":stats.materials,"masked_materials":stats.masked,"imported_texture_filters":stats.imported_filters,"preview_filter":"NEAREST"})
    return root

func inspect(node: Node,pose: Transform3D,stats: Dictionary):
    if node is Node3D:pose=pose*node.transform
    if node is MeshInstance3D:
        for v in node.mesh.get_faces():
            var point=pose*v;stats.minimum=stats.minimum.min(point);stats.maximum=stats.maximum.max(point)
        for i in range(node.mesh.get_surface_count()):
            var mat=node.mesh.surface_get_material(i) as StandardMaterial3D
            if mat==null or mat.albedo_texture==null:push_error("Missing textured material");stats.valid=false;continue
            var size=mat.albedo_texture.get_size()
            if int(size.x) not in [32,64] or size.y!=size.x:push_error("Unexpected texture dimensions: "+str(size));stats.valid=false
            stats.imported_filters.append(mat.texture_filter)
            # Use nearest sampling explicitly in the preview: game/project defaults must not blur assets.
            mat.texture_filter=BaseMaterial3D.TEXTURE_FILTER_NEAREST
            if mat.transparency==BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR:stats.masked+=1
            elif mat.transparency!=BaseMaterial3D.TRANSPARENCY_DISABLED:push_error("Unexpected soft transparency");stats.valid=false
            stats.materials+=1
    for child in node.get_children():inspect(child,pose,stats)

func show_level(value: int):
    level=value
    for tree in trees:remove_child(tree);tree.queue_free()
    trees.clear();var index=0
    for entry in entries:
        if int(entry.lod)!=value:continue
        var tree=load_tree(entry,false);add_child(tree);tree.position=Vector3((index%6)*13,0,-floori(index/6.0)*17);trees.append(tree);index+=1
    caption.text="THORNWAKE  /  WOODLAND ASSET REVIEW\nLOD %d   ·   1 / 2 / 3 select detail   ·   Right-drag orbit   ·   Wheel zoom"%level

func update_camera():
    camera.position=focus+Vector3(sin(yaw)*cos(pitch),sin(pitch),cos(yaw)*cos(pitch))*distance
    camera.look_at(focus)
func _input(event):
    if event is InputEventKey and event.pressed and not event.echo:
        if event.keycode in [KEY_1,KEY_2,KEY_3]:show_level(event.keycode-KEY_1)
        if event.keycode==KEY_ESCAPE:get_tree().quit()
    if event is InputEventMouseMotion and Input.is_mouse_button_pressed(MOUSE_BUTTON_RIGHT):
        yaw-=event.relative.x*.005;pitch=clamp(pitch+event.relative.y*.005,.12,1.3);update_camera()
    if event is InputEventMouseButton and event.pressed:
        if event.button_index==MOUSE_BUTTON_WHEEL_UP:distance=max(14,distance-4)
        if event.button_index==MOUSE_BUTTON_WHEEL_DOWN:distance=min(140,distance+4)
        update_camera()
func _process(_delta):
    frame+=1
    if capture!="" and frame==12:
        get_viewport().get_texture().get_image().save_png(capture);print("CAPTURED "+capture);get_tree().quit(0)
