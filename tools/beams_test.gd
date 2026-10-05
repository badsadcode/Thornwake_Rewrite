# Light-beam test harness: sky + a flat ground + the LightBeams pass, no world (a few seconds per image).
# xvfb-run godot --path . -s tools/beams_test.gd -- out=/tmp/b.png [elev=12] [cover=0.6] [intensity=1] [pitch=0.15] [turn=0]
extends SceneTree

var _frames := 0
var _out := "/tmp/beams.png"

func _initialize() -> void:
	var args := {}
	for a in OS.get_cmdline_user_args():
		var kv := a.split("=")
		if kv.size() == 2: args[kv[0]] = kv[1]
	_out = args.get("out", _out)
	var noise := FastNoiseLite.new()
	noise.frequency = 0.02
	noise.fractal_octaves = 4
	var tex := NoiseTexture2D.new()
	tex.width = 256; tex.height = 256; tex.seamless = true; tex.noise = noise; tex.generate_mipmaps = true
	var sky := ShaderMaterial.new()
	sky.shader = load("res://Game/Rewrite/Sky.gdshader")
	sky.set_shader_parameter("cloud_tex", tex)
	sky.set_shader_parameter("quality", 0)
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = Sky.new()
	env.sky.sky_material = sky
	env.fog_enabled = true
	env.fog_density = 0.00009
	var we := WorldEnvironment.new()
	we.environment = env
	root.add_child(we)
	var sun := DirectionalLight3D.new()
	var elev := float(args.get("elev", "12"))
	sun.rotation_degrees = Vector3(-elev, 0, 0) # shines toward -z: the sun is toward +z
	root.add_child(sun)
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(60000, 60000)
	ground.mesh = plane
	var gm := StandardMaterial3D.new()
	gm.albedo_color = Color(0.25, 0.32, 0.2)
	ground.material_override = gm
	root.add_child(ground)
	var cam := Camera3D.new()
	cam.far = 40000
	root.add_child(cam)
	cam.position = Vector3(0, 60, 0)
	# Looking toward the sun (+z) by default; turn rotates away from it.
	cam.rotation = Vector3(float(args.get("pitch", "0.15")), PI + float(args.get("turn", "0")), 0)
	var beams := ShaderMaterial.new()
	beams.shader = load("res://Game/Rewrite/LightBeams.gdshader")
	beams.render_priority = 99
	beams.set_shader_parameter("cloud_tex", tex)
	var to_sun := sun.basis.z.normalized()
	beams.set_shader_parameter("light_direction", to_sun)
	beams.set_shader_parameter("light_color", Color(1.0, 0.85, 0.65))
	beams.set_shader_parameter("intensity", float(args.get("intensity", "1")))
	var quad := MeshInstance3D.new()
	var qm := QuadMesh.new()
	qm.size = Vector2(2, 2)
	quad.mesh = qm
	quad.material_override = beams
	quad.extra_cull_margin = 16384
	quad.position = Vector3(0, 0, -1)
	if args.get("beams", "1") == "1": cam.add_child(quad)
	RenderingServer.global_shader_parameter_set("cloud_state", Vector4(100, 200, float(args.get("cover", "0.6")), 0))
	RenderingServer.global_shader_parameter_set("cloud_layers", Vector4(300, 400, 500, 600))
	RenderingServer.global_shader_parameter_set("sky_mood", Vector4(1, 1, 1, 0))
	RenderingServer.global_shader_parameter_set("night_sky", Vector4(0, -1, 0, 0.5))
	RenderingServer.global_shader_parameter_set("star_rotation", Basis())
	RenderingServer.global_shader_parameter_set("sky_extra", Vector4(10, 0, 0, 0))

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 30:
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png(_out)
		quit()
	return false
