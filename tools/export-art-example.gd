extends SceneTree
# Recreates the example GLB using the editable scene supplied alongside it.
func _initialize():
    var source = load("res://Assets/Buildings/Wall_Solid_Fieldstone.tscn").instantiate()
    var document = GLTFDocument.new()
    var state = GLTFState.new()
    var result = document.append_from_scene(source, state)
    if result == OK:
        result = document.write_to_filesystem(state, "res://Assets/Buildings/Wall_Solid_Fieldstone.glb")
    source.free()
    print("EXAMPLE_GLB_EXPORT: ", result)
    quit(0 if result == OK else 1)
