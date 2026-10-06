# Water shader test harness: a sloping beach, a water sheet with the same vertex data as the game (depth, kind, current,
# biome tint, shore data), sky and sun — renders a water style in a few seconds.
# xvfb-run godot --path . -s tools/water_test.gd -- out=/tmp/w.png [style=1] [kind=sea|lake|river] [sea=0.35]
#   [current=0] [fetch=20000] [exposure=1] [slope=0.03] [time=10] [cam=0,8,40] [pitch=-0.25] [yaw=0] [tint=0]
extends SceneTree

var _frames := 0
var _out := "/tmp/water.png"

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
			uvs.append(Vector2(current, 0.0) if kind == "river" else Vector2.ZERO)
			uv2.append(Vector2(packed, 0.65 if tint > 0 else 0.0))
			custom.append_array(PackedFloat32Array([0.0, slope if kind == "sea" else 0.0, exposure, fetch if kind != "river" else 0.0]))
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

func _set_spectrum(mat: ShaderMaterial, s: float, wind_angle: float) -> void:
	var n := 32
	var hs := 0.4 + 6.8 * pow(s, 1.5)
	var lp := 9.0 + 62.0 * pow(s, 1.2)
	var wp := sqrt(9.81 * TAU / lp)
	var lmax := lp * 1.8
	var lmin := maxf(1.2, lp / 34.0)
	var energy := []; var waves := []; var phases := []; var total := 0.0
	for i in n:
		var t := float(i) / float(n - 1)
		var lam := lmax * pow(lmin / lmax, t)
		var k := TAU / lam
		var w := sqrt(9.81 * k)
		var r := wp / w
		var sig := 0.07 if w <= wp else 0.09
		var peak := pow(3.3, exp(-(w - wp) * (w - wp) / (2.0 * sig * sig * wp * wp)))
		var e := pow(w, -5.0) * exp(-1.25 * r * r * r * r) * peak * w
		energy.append(e); total += e
		var spread := (0.3 + 0.55 * s) * (0.6 + 0.9 * t)
		var u := _hash01(i, 1) * 2.0 - 1.0
		var ang := wind_angle + 0.85 if i == 0 else wind_angle + signf(u) * pow(absf(u), 0.8) * spread * 1.6
		waves.append(Vector4(cos(ang), sin(ang), k, 0.0)); phases.append(_hash01(i, 2) * TAU)
	var target := hs / 4.0 * (hs / 4.0)
	var sum_ka := 0.0
	for i in n:
		var wv: Vector4 = waves[i]
		var a := minf(sqrt(2.0 * target * float(energy[i]) / total), 0.3 / wv.z)
		waves[i] = Vector4(wv.x, wv.y, wv.z, a); sum_ka += wv.z * a
	var chop := 0.6 + 0.36 * s
	var qp := PackedVector2Array()
	for i in n: qp.append(Vector2(chop / maxf(sum_ka, 1e-6), float(phases[i])))
	mat.set_shader_parameter("waves2", waves)
	mat.set_shader_parameter("wave2_qp", qp)
	mat.set_shader_parameter("wave2_hs", hs)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames == 40:
		await RenderingServer.frame_post_draw
		root.get_viewport().get_texture().get_image().save_png(_out)
		quit()
	return false
