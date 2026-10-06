# Sky shader test harness: renders only the rewrite sky (no world) to a PNG in a few seconds.
# xvfb-run godot --path . -s tools/sky_test.gd -- quality=1 out=/tmp/sky.png [debug=0] [pitch=0.4] [yaw=2.5] [hour_elev=50]
# (hour_elev may go below the horizon for twilight: the light stops at -4 degrees as in the game, the sky sees the true sun)
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
	mat.set_shader_parameter("sun_style", int(args.get("sun", "0")))
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = Sky.new()
	env.sky.sky_material = mat
	var we := WorldEnvironment.new()
	we.environment = env
	root.add_child(we)
	var sun := DirectionalLight3D.new()
	var elev := float(args.get("hour_elev", "50"))
	sun.rotation_degrees = Vector3(-maxf(elev, -4.0), 0, 0) # the game's light stops at -4 degrees
	root.add_child(sun)
	# The true sun for twilight (sun_true): same azimuth, real elevation.
	var true_sun := Basis.from_euler(Vector3(deg_to_rad(-elev), 0, 0)).z
	RenderingServer.global_shader_parameter_set("sun_true", Vector4(true_sun.x, true_sun.y, true_sun.z, elev))
	var cam := Camera3D.new()
	cam.rotation = Vector3(float(args.get("pitch", "0.4")), float(args.get("yaw", "2.5")), 0)
	root.add_child(cam)
	RenderingServer.global_shader_parameter_set("cloud_state", Vector4(100, 200, float(args.get("cover", "0.45")), 0))
	var L: PackedStringArray = str(args.get("layers", "300,400,500,600")).split(",")
	RenderingServer.global_shader_parameter_set("cloud_layers", Vector4(float(L[0]), float(L[1]), float(L[2]), float(L[3])))
	RenderingServer.global_shader_parameter_set("sky_mood", Vector4(1, 1, 1, 0))
	RenderingServer.global_shader_parameter_set("rain_wetness", Vector2(float(args.get("wet", "0")), 0))
	RenderingServer.global_shader_parameter_set("aurora", float(args.get("aurora", "0")))
	var md := Vector3(0.3, 0.35, -0.88).normalized()
	cam.look_at_from_position(Vector3.ZERO, md * 10.0 if args.has("look_moon") else Vector3(0, 3, -10), Vector3.UP) if args.has("look_moon") else null
	RenderingServer.global_shader_parameter_set("night_sky", Vector4(md.x, md.y, md.z, 0.5))
	RenderingServer.global_shader_parameter_set("star_rotation", Basis(Vector3.RIGHT, 0.6))
	RenderingServer.global_shader_parameter_set("sky_extra", Vector4(10, 1, 1, 1))
	# Cloud types (round 17): types=cumulus,storm,flash (the storms straight ahead, flash 0..1 in the middle cell),
	# lens=x,y,z,strength relative to the camera, more=mackerel,wind_angle,time,base_above, storm_off=rad
	var fwd := -cam.basis.z
	var T: PackedStringArray = str(args.get("types", "0,0,0")).split(",")
	var az := atan2(fwd.z, fwd.x) + float(args.get("storm_off", "0"))
	RenderingServer.global_shader_parameter_set("cloud_types", Vector4(float(T[0]), float(T[1]), az, float(T[2])))
	var LN: PackedStringArray = str(args.get("lens", "0,0,0,0")).split(",")
	RenderingServer.global_shader_parameter_set("cloud_lens", Vector4(float(LN[0]), float(LN[1]), float(LN[2]), float(LN[3])))
	var M: PackedStringArray = str(args.get("more", "0,0.6,100,1500")).split(",")
	RenderingServer.global_shader_parameter_set("cloud_more", Vector4(float(M[0]), float(M[1]), float(M[2]), float(M[3])))

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 30:
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png(_out)
		quit()
	return false
