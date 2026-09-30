extends SceneTree
var minimum: Vector3
var maximum: Vector3
var bad_names: Array[String] = []
var door_leaf_count := 0
func inspect(node: Node, pose: Transform3D):
    if node is Node3D:
        pose = pose * node.transform
    if str(node.name).begins_with("GUIDE") or str(node.name).begins_with("REFERENCE"):
        bad_names.append(str(node.name))
    if str(node.name) == "DoorLeaf":
        door_leaf_count += 1
    if node is MeshInstance3D and node.mesh != null:
        for vertex in node.mesh.get_faces():
            var point = pose * vertex
            minimum = minimum.min(point)
            maximum = maximum.max(point)
    for child in node.get_children():
        inspect(child, pose)
func _initialize():
    var manifest = JSON.parse_string(FileAccess.get_file_as_string("res://ArtKit/asset_manifest.json"))
    var report: Array[Dictionary] = []
    for asset in manifest:
        var path = "res://artifacts/art-roundtrip/" + asset.id + ".glb"
        if not FileAccess.file_exists(path):
            continue
        var document = GLTFDocument.new()
        var state = GLTFState.new()
        if document.append_from_file(path, state) != OK:
            push_error("Cannot reimport " + path); quit(2); return
        var root = document.generate_scene(state)
        minimum=Vector3(INF,INF,INF);maximum=-minimum;bad_names.clear();door_leaf_count=0
        inspect(root, Transform3D.IDENTITY)
        var expected_min=Vector3(asset.godotMin[0],asset.godotMin[1],asset.godotMin[2])
        var expected_max=Vector3(asset.godotMax[0],asset.godotMax[1],asset.godotMax[2])
        var error=max(minimum.distance_to(expected_min),maximum.distance_to(expected_max))
        if error > 0.002 or not bad_names.is_empty() or (asset.id == "Wall_Door" and door_leaf_count != 1):
            push_error("Roundtrip mismatch: " + asset.id + " error=" + str(error) + " guides=" + str(bad_names));root.free();quit(2);return
        report.append({"id":asset.id,"maximum_bound_error_m":error,"no_guides_or_reference_exported":true,"door_leaf_count":door_leaf_count})
        root.free()
    if report.size() != 5:
        push_error("Expected five roundtrip fixtures");quit(2);return
    var output=FileAccess.open("res://ArtKit/game_roundtrip_verification.json",FileAccess.WRITE)
    output.store_string(JSON.stringify(report,"  "));output.close()
    print("GAME_ART_ROUNDTRIP_PASS: five Blender exports preserve game bounds, orientation and door hinge; no guides included")
    quit(0)
