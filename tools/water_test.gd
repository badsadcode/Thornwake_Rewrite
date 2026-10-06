# Water shader test harness: a sloping beach, a water sheet with the same vertex data as the game (depth, kind, current,
# biome tint, shore data), sky and sun — renders a water style in a few seconds.
# xvfb-run godot --path . -s tools/water_test.gd -- out=/tmp/w.png [style=1] [kind=sea|lake|river] [sea=0.35]
#   [current=0] [fetch=20000] [exposure=1] [slope=0.03] [time=10] [cam=0,8,40] [pitch=-0.25] [yaw=0] [tint=0]
#   [origin=x,z] (world position: experimental 2 wave headings depend on it)
extends SceneTree

var _frames := 0
var _out := "/tmp/water.png"
var _origin := Vector2.ZERO

func _initialize() -> void:
	var args := {}
	for a in OS.get_cmdline_user_args():
		var kv := a.split("=")
		if kv.size() == 2: args[kv[0]] = kv[1]
	_out = args.get("out", _out)
	var kind: String = args.get("kind", "sea")
	var sea_state := float(args.get("sea", "0.35"))
	var slope := float(args.get("slope", "0.03"))
	var current := float(args.get("current", "0"))
	var exposure := float(args.get("exposure", "1"))
	var fetch := float(args.get("fetch", "20000"))
	var t := float(args.get("time", "10"))
	var o: PackedStringArray = str(args.get("origin", "0,0")).split(",") # world position of the scene (regional headings)
	_origin = Vector2(float(o[0]), float(o[1]))

	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = Sky.new()
	var sky := ProceduralSkyMaterial.new()
	sky.sky_top_color = Color(0.32, 0.47, 0.62); sky.sky_horizon_color = Color(0.70, 0.76, 0.78)
	env.sky.sky_material = sky
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	var we := WorldEnvironment.new(); we.environment = env; root.add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-float(args.get("elev", "25")), float(args.get("sun_yaw", "160")), 0)
	sun.shadow_enabled = true
	root.add_child(sun)

	# Ground: the bed rises toward +z at the given slope and becomes a beach above y = 0 (z > 0).
	var size := float(args.get("size", "240"))
	var n := int(args.get("grid", "241"))
	var ground := _grid(size, n, func(x: float, z: float) -> float: return _bed(x, z, slope, kind))
	var gm := StandardMaterial3D.new(); gm.albedo_color = Color(0.62, 0.56, 0.42)
	var gi := MeshInstance3D.new(); gi.mesh = ground; gi.material_override = gm; root.add_child(gi)

	# Water sheet with the game's vertex data.
	var st := SurfaceTool.new()
	var verts := PackedVector3Array(); var cols := PackedColorArray(); var uvs := PackedVector2Array(); var uv2 := PackedVector2Array()
	var custom := PackedFloat32Array(); var idx := PackedInt32Array()
	var tint := int(args.get("tint", "0"))
	var tint_rgb := Color(0.36, 0.29, 0.18) if tint == 1 else Color(0.31, 0.62, 0.55)
	var packed := float(int(tint_rgb.r8) * 65536 + int(tint_rgb.g8) * 256 + int(tint_rgb.b8))
	var kind_code := 0.0 if kind == "sea" or kind == "deep" else (0.5 if kind == "lake" else 1.0)
	for j in n:
		for i in n:
			var x := -size * 0.5 + size * i / (n - 1)
			var z := -size * 0.5 + size * j / (n - 1)
			var g: float = _bed(x, z, slope, kind)
			var depth := -g
			verts.append(Vector3(x, 0.0, z))
			var edge: float = 1.0 if depth > 0.0 else clampf(1.0 + depth / 0.6, 0.0, 1.0)
			cols.append(Color(clampf(depth / 20.0, 0.0, 1.0), kind_code, 0.0, edge))
			uvs.append(Vector2(0.0, -current) if kind == "river" else Vector2.ZERO) # downstream toward the camera (channel along z)
			uv2.append(Vector2(packed, 0.65 if tint > 0 else 0.0))
			custom.append_array(PackedFloat32Array([0.0, slope if kind == "sea" else 0.0, exposure, fetch if kind != "river" else float(args.get("width", "0"))]))
	for j in n - 1:
		for i in n - 1:
			var a := j * n + i
			idx.append_array(PackedInt32Array([a, a + 1, a + n, a + 1, a + n + 1, a + n]))
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts; arrays[Mesh.ARRAY_COLOR] = cols; arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_TEX_UV2] = uv2; arrays[Mesh.ARRAY_CUSTOM0] = custom; arrays[Mesh.ARRAY_INDEX] = idx
	var normals := PackedVector3Array(); normals.resize(verts.size()); normals.fill(Vector3.UP)
	arrays[Mesh.ARRAY_NORMAL] = normals
	var wmesh := ArrayMesh.new()
	wmesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], {}, Mesh.ARRAY_CUSTOM_RGBA_FLOAT << Mesh.ARRAY_FORMAT_CUSTOM0_SHIFT)
	var mat := ShaderMaterial.new()
	var style: String = args.get("style", "1")
	var shader_path := "res://Game/Rewrite/Water.gdshader"
	if style == "1": shader_path = "res://Game/Rewrite/WaterExperimental.gdshader"
	elif style == "2": shader_path = "res://Game/Rewrite/WaterExperimental2.gdshader"
	mat.shader = load(shader_path)
	_set_waves(mat, sea_state, 0.64)
	_set_spectrum(mat, sea_state, 0.64)
	if args.has("redfoam"): mat.set_shader_parameter("foam_color", Color(1, 0, 0))
	if args.has("amount"): mat.set_shader_parameter("debug_foam", 1)
	mat.set_shader_parameter("sun_direction", sun.basis.z.normalized())
	mat.set_shader_parameter("sun_color", Color(1, 0.95, 0.85))
	var wi := MeshInstance3D.new(); wi.mesh = wmesh; wi.material_override = mat; wi.extra_cull_margin = 50.0; root.add_child(wi)

	RenderingServer.global_shader_parameter_set("ocean_time", t)
	RenderingServer.global_shader_parameter_set("wind", Vector4(0.8, float(args.get("wind", "0.5")), -0.6, 0.0))
	var cam := Camera3D.new(); cam.far = 4000
	root.add_child(cam)
	var c: PackedStringArray = str(args.get("cam", "0,8,-40")).split(",")
	cam.position = Vector3(float(c[0]), float(c[1]), float(c[2]))
	cam.rotation = Vector3(float(args.get("pitch", "-0.25")), PI + float(args.get("yaw", "0")), 0)

func _bed(x: float, z: float, slope: float, kind: String) -> float:
	if kind == "river":
		return -1.6 + 1.6 * pow(abs(x) / 40.0, 2.0) # a 80 m wide channel along z
	if kind == "lake":
		return -3.0 + slope * (z - 20.0) * 0.5
	if kind == "deep":
		return -120.0
	return slope * (z - 20.0) + 0.6 * sin(x * 0.05) * 0.3 # beach at z = 20

func _grid(size: float, n: int, height: Callable) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for j in n - 1:
		for i in n - 1:
			for q in [[0, 0], [1, 0], [0, 1], [1, 0], [1, 1], [0, 1]]:
				var x: float = -size * 0.5 + size * (i + int(q[0])) / (n - 1)
				var z: float = -size * 0.5 + size * (j + int(q[1])) / (n - 1)
				st.add_vertex(Vector3(x, float(height.call(x, z)), z))
	st.generate_normals()
	return st.commit()

# OceanWaves.Configure in GDScript (same numbers as the C# class).
func _set_waves(mat: ShaderMaterial, sea_state: float, wind_angle: float) -> void:
	var spread := [0, .35, -.4, .7, -.75, .2, -.25, 1.0, -1.1, .55, .5, -.3]
	var longest := 8.0 + 64.0 * pow(sea_state, 1.3)
	var steep := .008 + .026 * sea_state
	var aligned := 1.2 - .5 * sea_state
	var chop := .45 + .5 * sea_state
	var waves := []; var q := PackedFloat32Array(); var total := 0.0
	for i in 12:
		var lam := 0.0; var amp := 0.0; var a := 0.0
		if i < 10:
			lam = longest * pow(.72, i); amp = lam * steep * (1.0 if i == 0 else .62); a = wind_angle + float(spread[i]) * aligned
		else:
			lam = 90.0 + 110.0 * sea_state + (i - 10) * 37.0; amp = lam * (.0008 + .011 * sea_state) * (1.0 if i == 10 else .6); a = wind_angle + float(spread[i])
		var k := TAU / lam
		waves.append(Vector4(cos(a), sin(a), k, amp)); total += amp
	for i in 12:
		var w: Vector4 = waves[i]
		q.append(chop / (w.z * w.w * 12.0 + 1e-9))
	mat.set_shader_parameter("waves", waves)
	mat.set_shader_parameter("wave_q", q)
	mat.set_shader_parameter("wave_height", total)
	mat.set_shader_parameter("sea_state", sea_state)

# SpectrumWaves.Configure in GDScript (same numbers as the C# class).
func _hash01(i: int, salt: int) -> float:
	# Not bit-identical to the C# hash (64-bit mixing); only the look matters here.
	var h := hash(Vector2i(i * 7919 + salt * 104729, 12345))
	return float(h % 1000003) / 1000003.0

func _set_spectrum(mat: ShaderMaterial, s: float, _wind_angle: float) -> void:
	# SpectrumWaves (round 11): fixed wavelengths and directions; amplitudes from the sea state.
	var n := 64; var nw := 52
	var hs := 0.15 + 14.1 * s * s
	var swell_hs := 0.5 + 2.0 * s
	var lp := 9.0 + 130.0 * s * s
	var wp := sqrt(9.81 * TAU / lp)
	var waves := []; var phases := PackedFloat32Array(); var energy := []; var total := 0.0
	for i in n:
		var lam := 0.0; var ang := 0.0
		if i < nw:
			var t := float(i) / float(nw - 1)
			lam = 300.0 * pow(1.0 / 300.0, t) * (1.0 + 0.04 * (_hash01(i, 3) - 0.5))
			ang = i * 2.39996323 + 0.3 * (_hash01(i, 1) - 0.5)
		else:
			var j := i - nw
			lam = 150.0 * pow(1.45, j % 3)
			ang = j * PI / 6.0
		var k := TAU / lam
		waves.append(Vector4(cos(ang), sin(ang), k, 0.0)); phases.append(_hash01(i, 2) * TAU)
		var e := 0.0
		if i < nw:
			var w := sqrt(9.81 * k)
			var r := wp / w
			var sig := 0.07 if w <= wp else 0.09
			var peak := pow(3.3, exp(-(w - wp) * (w - wp) / (2.0 * sig * sig * wp * wp)))
			e = pow(w, -5.0) * exp(-1.25 * r * r * r * r) * peak * w
			total += e
		energy.append(e)
	for i in n:
		var wv: Vector4 = waves[i]
		var a := sqrt(2.0 * (hs / 4.0) * (hs / 4.0) * float(energy[i]) / total) if i < nw else sqrt(2.0 * (swell_hs / 4.0) * (swell_hs / 4.0) / float(n - nw))
		waves[i] = Vector4(wv.x, wv.y, wv.z, a)
	mat.set_shader_parameter("waves2", waves)
	mat.set_shader_parameter("wave2_phase", phases)
	mat.set_shader_parameter("wave2_hs", hs)
	mat.set_shader_parameter("wave2_chop", 0.62 + 0.3 * s)
	mat.set_shader_parameter("world_origin", Vector2(float(_origin.x), float(_origin.y)))

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 40:
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png(_out)
		quit()
	return false
