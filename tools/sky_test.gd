# Sky shader test harness: renders only the rewrite sky (no world) to a PNG in a few seconds.
# xvfb-run godot --path . -s tools/sky_test.gd -- quality=1 out=/tmp/sky.png [debug=0] [pitch=0.4] [hour_elev=50]
extends SceneTree

var _frames := 0
var _out := "/tmp/sky.png"

func _initialize() -> void:
	var args := {}
	for a in OS.get_cmdline_user_args():
		var kv := a.split("=")
		if kv.size() == 2: args[kv[0]] = kv[1]
	_out = args.get("out", _out)
	var mat := ShaderMaterial.new()
	mat.shader = load("res://Game/Rewrite/Sky.gdshader")
	var noise := FastNoiseLite.new()
	noise.frequency = 0.02
	var tex := NoiseTexture2D.new()
	tex.width = 256; tex.height = 256; tex.seamless = true; tex.noise = noise; tex.generate_mipmaps = true
	mat.set_shader_parameter("cloud_tex", tex)
	mat.set_shader_parameter("quality", int(args.get("quality", "1")))
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = Sky.new()
	env.sky.sky_material = mat
	var we := WorldEnvironment.new()
	we.environment = env
	root.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-float(args.get("hour_elev", "50")), 0, 0)
	root.add_child(sun)
	var cam := Camera3D.new()
	cam.rotation = Vector3(float(args.get("pitch", "0.4")), 2.5, 0)
	root.add_child(cam)
	RenderingServer.global_shader_parameter_set("cloud_state", Vector4(100, 200, 0.45, 0))
	var L: PackedStringArray = str(args.get("layers", "300,400,500,600")).split(",")
	RenderingServer.global_shader_parameter_set("cloud_layers", Vector4(float(L[0]), float(L[1]), float(L[2]), float(L[3])))
	RenderingServer.global_shader_parameter_set("sky_mood", Vector4(1, 1, 1, 0))

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 30:
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png(_out)
		quit()
	return false
