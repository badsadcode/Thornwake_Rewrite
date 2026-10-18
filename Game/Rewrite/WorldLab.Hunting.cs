using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Hunting as this PC plays it (D120; the rules are the host's GameRules): the animals the host reports, drawn and
/// animated; a spear (8), the bow (9) or the sword (0) in hand — the left mouse thrusts or cuts at the animal in front,
/// or holds the bow drawn (longer: farther and harder) and looses on release; arrows fly with gravity (they drop with
/// distance) and this PC tells the host what its own arrows struck. A fallen animal is a carcass: E with a cutting tool
/// butchers it. Wolves and angry stags hurt (the host says so; health lives here).
/// </summary>
public partial class WorldLab
{
    private readonly Dictionary<int, AnimalView> _animalViews = new();
    private readonly Dictionary<int, double> _animalHealth = new();
    private bool _huntLmbWas, _strikeHooked;
    private float _draw, _shoulder;
    /// <summary>Aiming with the right mouse (bow): 0 … 1, eased in and out (Tom, build 63; D124).</summary>
    private float _aimFocus;
    private Label? _crosshair;

    private sealed class Flight
    {
        public required Node3D Node;
        public double X, Y, Z;          // world
        public Vector3 Velocity;
        public bool Mine, Stuck;
        public float Draw;
        public double Age;
        public required List<(double X, double Y, double Z, double T)> Trail;
        public double X0, Y0, Z0;       // where it was loosed (world): the flight is the exact arc from there
        public Vector3 V0;
    }

    /// <summary>Fading trails behind arrows (Tom, build 66: "a small trail so I can see the arc"; D131): the points each
    /// arrow passed, kept for a moment after it lands, drawn as a thin ribbon facing the camera.</summary>
    private readonly List<List<(double X, double Y, double Z, double T)>> _trails = new();
    private MeshInstance3D? _trailMesh;
    private const double TrailLife = 1.4;
    /// <summary>Where this PC's last arrow came down (tests).</summary>
    private (double X, double Y, double Z)? _lastLanding;

    private readonly List<Flight> _arrows = new();
    private static double Now => Time.GetTicksMsec() / 1000.0;

    private void InitHunting(CanvasLayer layer)
    {
        _crosshair = new Label { Text = "+", AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f, OffsetLeft = -8, OffsetTop = -14,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.AddThemeFontSizeOverride("font_size", 22);
        _crosshair.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        layer.AddChild(_crosshair);
        _prompt = new Label { AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = 46,
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        _prompt.AddThemeFontSizeOverride("font_size", 17);
        _prompt.AddThemeColorOverride("font_color", new Color(1, .95f, .82f));
        _prompt.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        _prompt.AddThemeConstantOverride("shadow_offset_x", 1); _prompt.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(_prompt);
    }

    private Label? _prompt;

    /// <summary>Per frame: what E would do here, under the screen's centre (Tom: arrows and the like are easy to miss).</summary>
    private void UpdatePrompt()
    {
        if (_prompt == null) return;
        _prompt.Text = _gathering != null || _menu.Visible || _pack.Visible || _map.Visible || _pieceMenu != null || _buildPicking || _aimFocus > 0 || _draw > 0 ? "" : InteractHint() is { } h ? "E   " + h : "";
    }

    private string? InteractHint()
    {
        if (_aimedAny != 0 && _seenObjects.Get(_aimedAny) is { } aimed && _catalog.Piece(aimed.Kind) is { } piece)
        {
            if (aimed.Blueprint) return $"Bring materials: {piece.Name.ToLowerInvariant()} (needs {NeedsHere(piece, aimed)})";
            if (piece.Has("door")) return aimed.State == 1 ? "Shut the door" : "Open the door";
        }
        var near = _seenObjects.Near(PlayerX, PlayerZ, GameRules.UseReach + 1.5).Where(o => o.Contents.Count > 0 || o.Kind == "campfire")
            .OrderBy(o => (o.X - PlayerX) * (o.X - PlayerX) + (o.Z - PlayerZ) * (o.Z - PlayerZ)).FirstOrDefault();
        if (near != null)
            return near.Kind switch
            {
                "carcass" => $"Butcher the {(_catalog.Animals.TryGetValue(near.Ref, out var a) ? a.Name.ToLowerInvariant() : "animal")}" + (_myInv.HasTag("cutting") ? "" : " (needs a knife, an axe or a sword)"),
                "campfire" => near.Blueprint ? "Bring materials: campfire" : "Put wood on the fire",
                "pack" => "Pick up the pack",
                _ => "Pick up " + string.Join(", ", near.Contents.Select(c => $"{(c.Count > 1 ? c.Count + " " : "")}{_catalog.Item(c.Item).Name.ToLowerInvariant()}")),
            };
        var spot = _streamer.GatherablesNear(PlayerX, PlayerZ, GameRules.UseReach - .5).FirstOrDefault(g => Math.Abs(g.Y - _player.Position.Y) < 1.6); // (on your level)
        if (spot.Layer != 0 || spot.X != 0 || spot.Z != 0) return spot.Layer == ObjectRemovals.Stones ? "Pick up the stone" : "Pick berries";
        return null;
    }

    private void HuntingWorldReady()
    {
        foreach (var v in _animalViews.Values) v.QueueFree();
        _animalViews.Clear();
        foreach (var f in _arrows) f.Node.QueueFree();
        _arrows.Clear();
        _trails.Clear();
        _animalHealth.Clear();
        TrackingWorldReady();
    }

    /// <summary>8 / 9 / 0: the spear, the bow, the sword in hand or put away (the axe and rod go away).</summary>
    private void ToggleWeapon(string item)
    {
        if (_me.Weapon == item) { _me.Weapon = null; _status = $"{_catalog.Item(item).Name} put away."; return; }
        if (_myInv.Count(item) == 0) { _status = $"You have no {_catalog.Item(item).Name.ToLowerInvariant()} (make one: I)."; return; }
        if (_axeHeld) ToggleAxe();
        if (_me.RodHeld) ToggleRod();
        if (_building) SetBuilding(false);
        _me.Weapon = item;
        _tools.Select(0);
        _draw = 0;
        _status = item == "bow"
            ? $"Bow in hand ({_myInv.CountTag("arrow")} arrows): hold the left mouse button to draw, release to shoot. Arrows drop with distance."
            : $"{_catalog.Item(item).Name} in hand: the left mouse button strikes what is in front of you.";
    }

    /// <summary>Per frame: the held weapon's swing or the bow's draw, the arrows in flight, the animals drawn.</summary>
    private void UpdateHunting(double delta, bool captured)
    {
        if (!_strikeHooked) { _me.AttackHit += OnStrike; _strikeHooked = true; }
        bool lmb = captured && Input.IsMouseButtonPressed(MouseButton.Left);
        bool pressed = lmb && !_huntLmbWas, released = !lmb && _huntLmbWas;
        _huntLmbWas = lmb;
        string? weapon = _me.Weapon;
        if (weapon != null && _myInv.Count(weapon) == 0) { _me.Weapon = null; weapon = null; } // (lost it: dropped on death, …)
        if (_crosshair != null) _crosshair.Visible = (weapon == "bow" || _building) && captured;
        var lookYaw = _yaw + Mathf.Pi; // (the way the camera looks, as a facing)
        // The right mouse with the bow: aim — the camera eases in close over the shoulder, the view narrows, the mouse slows.
        bool focus = weapon == "bow" && (captured && Input.IsMouseButtonPressed(MouseButton.Right) || _testAim);
        _aimFocus = Mathf.MoveToward(_aimFocus, focus ? 1 : 0, (float)delta / .45f);
        if (focus) _me.Facing = lookYaw;
        switch (weapon)
        {
            case "spear" or "stone_sword":
                if (lmb && !_me.Attacking) { _me.Strike(); _me.Facing = lookYaw; }
                break;
            case "bow":
                if (pressed && !_myInv.HasTag("arrow")) _status = "No arrows (make some: I).";
                if (lmb && _myInv.CountTag("arrow") > _arrows.Count(f => f.Mine && !f.Stuck))
                {
                    _draw = Mathf.Min(1, _draw + (float)delta / .9f);
                    _me.Facing = lookYaw;
                    ShowAction("Drawing", _draw);
                }
                else if (released && _draw > .15f) { Loose(_draw); _draw = 0; }
                else if (!lmb) _draw = 0;
                _me.Draw = _draw;
                break;
        }
        UpdateArrows(delta);
        DrawTrails();
        UpdateTracking();
        double now = Now;
        foreach (var v in _animalViews.Values) v.Step(now, (float)delta, _originX, _originZ);
    }

    /// <summary>The spear or the sword lands: the animal in front within the weapon's reach is struck (the host decides).</summary>
    private void OnStrike()
    {
        if (_me.Weapon is not { } item) return;
        double reach = _catalog.Item(item).Prop("reach", 2);
        if (AnimalInFront(reach) is { } target) Act(new ActionRequest(ActionKind.Attack, target.Id, 0, item, 0, 0, 0, 0));
    }

    /// <summary>The nearest living animal within <paramref name="reach"/> (plus its body) in front of Alden.</summary>
    private AnimalView? AnimalInFront(double reach)
    {
        var facing = new Vector2(Mathf.Sin(_me.Facing), Mathf.Cos(_me.Facing));
        AnimalView? best = null;
        double bestD = double.MaxValue;
        foreach (var v in _animalViews.Values)
        {
            if (v.State == AnimalState.Dead) continue;
            var to = new Vector2((float)(v.X - PlayerX), (float)(v.Z - PlayerZ));
            double d = to.Length() - v.Def.Radius - v.Def.Length * .3;
            if (d > reach || Math.Abs(v.Y - _player.Position.Y) > 2.5) continue;
            if (to.Length() > .5f && to.Normalized().Dot(facing) < .45f) continue;
            if (d < bestD) { bestD = d; best = v; }
        }
        return best;
    }

    /// <summary>Loose an arrow toward the screen's centre, drawn <paramref name="draw"/>: its speed from the bow and the draw.</summary>
    private void Loose(float draw)
    {
        if (_myInv.Stacks.Select(s => _catalog.Item(s.Item)).FirstOrDefault(d => d.Has("bow")) is not { } bow) return;
        var forward = new Vector3(Mathf.Sin(_me.Facing), 0, Mathf.Cos(_me.Facing));
        var start = _player.Position + Vector3.Up * 1.45f + forward * .45f;
        var from = _camera.GlobalPosition;
        var look = -_camera.GlobalTransform.Basis.Z;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + look * 300, 1));
        var target = hit.Count > 0 ? hit["position"].AsVector3() : from + look * 300;
        // An animal under the crosshair is the target, not the ground behind it (it has no physics shape to stop the ray).
        bool aimed = hit.Count > 0;
        foreach (var v in _animalViews.Values)
            if (v.Hit(from, target, out var on) && on.DistanceTo(from) < target.DistanceTo(from)) { target = on; aimed = true; }
        float speed = (float)bow.Prop("speed", 50) * (.45f + .55f * draw);
        // Aimed allowing for the drop (setting): the arrow arcs onto the point under the crosshair (D131).
        var dir = aimed && GameSettings.BowDrop.Index == 0 ? Aimed(start, target, speed) : (target - start).Normalized();
        Shoot(start, dir * speed, true, draw);
        GameClient?.SendShot(start.X + _originX, start.Y, start.Z + _originZ, dir.X * speed, dir.Y * speed, dir.Z * speed);
    }

    /// <summary>The launch direction that carries an arrow at <paramref name="speed"/> from start onto target under gravity
    /// (the lower of the two arcs); beyond its reach, the farthest-reaching 45°.</summary>
    private static Vector3 Aimed(Vector3 start, Vector3 target, float speed)
    {
        var d = target - start;
        var flat = new Vector2(d.X, d.Z);
        float x = flat.Length(), y = d.Y, v2 = speed * speed, g = 9.8f;
        if (x < .5f) return d.Normalized();
        float root = v2 * v2 - g * (g * x * x + 2 * y * v2);
        float angle = root < 0 ? Mathf.Pi / 4 : Mathf.Atan((v2 - Mathf.Sqrt(root)) / (g * x));
        var h = flat / x;
        return new Vector3(h.X * Mathf.Cos(angle), Mathf.Sin(angle), h.Y * Mathf.Cos(angle));
    }

    private void Shoot(Vector3 start, Vector3 velocity, bool mine, float draw)
    {
        var node = new Node3D { Name = "Arrow" };
        node.AddChild(GameplayModels.Weapon("arrow"));
        AddChild(node);
        var trail = new List<(double, double, double, double)> { (start.X + _originX, start.Y, start.Z + _originZ, Now) };
        _trails.Add(trail);
        _arrows.Add(new Flight { Node = node, X = start.X + _originX, Y = start.Y, Z = start.Z + _originZ, Velocity = velocity, Mine = mine, Draw = draw, Trail = trail,
            X0 = start.X + _originX, Y0 = start.Y, Z0 = start.Z + _originZ, V0 = velocity });
    }

    /// <summary>Arrows in flight: gravity and a little drag; one of this PC's that strikes an animal or the ground is
    /// reported to the host (the others' only fly); stuck ones stay a moment.</summary>
    private void UpdateArrows(double delta)
    {
        float dt = (float)delta;
        for (int i = _arrows.Count - 1; i >= 0; i--)
        {
            var f = _arrows[i];
            f.Age += delta;
            if (f.Stuck)
            {
                if (f.Age > 2) { f.Node.QueueFree(); _arrows.RemoveAt(i); }
                else f.Node.Position = new Vector3((float)(f.X - _originX), (float)f.Y, (float)(f.Z - _originZ));
                continue;
            }
            // The exact arc from where it was loosed (D131): the same on every PC and at any frame rate, and what the aim
            // allowed for — no drag.
            var a = new Vector3((float)(f.X - _originX), (float)f.Y, (float)(f.Z - _originZ));
            double t = f.Age;
            f.Velocity = f.V0 + Vector3.Down * 9.8f * (float)t;
            var b = new Vector3((float)(f.X0 + f.V0.X * t - _originX), (float)(f.Y0 + f.V0.Y * t - 4.9 * t * t), (float)(f.Z0 + f.V0.Z * t - _originZ));
            bool done = false;
            if (f.Mine)
                foreach (var v in _animalViews.Values)
                    if (v.Hit(a, b, out var at))
                    {
                        f.Trail.Add((at.X + _originX, at.Y, at.Z + _originZ, Now));
                        Act(new ActionRequest(ActionKind.Shoot, v.Id, 0, "", at.X + _originX, at.Y, at.Z + _originZ, f.Draw));
                        f.Node.QueueFree(); _arrows.RemoveAt(i); done = true;
                        break;
                    }
            if (done) continue;
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(a, b, 1));
            if (hit.Count > 0)
            {
                var p = hit["position"].AsVector3();
                f.X = p.X + _originX; f.Y = p.Y; f.Z = p.Z + _originZ;
                f.Trail.Add((f.X, f.Y, f.Z, Now));
                f.Stuck = true; f.Age = 0;
                if (f.Mine) _lastLanding = (f.X, f.Y, f.Z);
                f.Node.Position = p - f.Velocity.Normalized() * .25f;
                if (f.Mine) Act(new ActionRequest(ActionKind.Shoot, 0, 0, "", f.X, f.Y, f.Z, f.Draw));
                continue;
            }
            f.X = b.X + _originX; f.Y = b.Y; f.Z = b.Z + _originZ;
            f.Node.Position = b;
            f.Trail.Add((f.X, f.Y, f.Z, Now));
            if (f.Velocity.LengthSquared() > 1e-4f) f.Node.Basis = Basis.LookingAt(-f.Velocity.Normalized(), Mathf.Abs(f.Velocity.Normalized().Y) > .99f ? Vector3.Forward : Vector3.Up);
            if (f.Age > 8)
            {
                if (f.Mine) Act(new ActionRequest(ActionKind.Shoot, 0, 0, "", f.X, f.Y, f.Z, f.Draw));
                f.Node.QueueFree(); _arrows.RemoveAt(i);
            }
        }
    }

    /// <summary>Every trail as a ribbon a few centimetres wide, turned to the camera, fading over its points' age.</summary>
    private void DrawTrails()
    {
        double now = Now;
        foreach (var t in _trails) t.RemoveAll(p => now - p.T > TrailLife);
        _trails.RemoveAll(t => t.Count == 0 && !_arrows.Any(f => f.Trail == t));
        if (_trailMesh == null)
        {
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _trailMesh = new MeshInstance3D { Name = "ArrowTrails", Mesh = new ImmediateMesh(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_trailMesh);
        }
        var mesh = (ImmediateMesh)_trailMesh.Mesh;
        mesh.ClearSurfaces();
        if (!GameSettings.ArrowTrails.On || !_trails.Any(t => t.Count > 1)) return;
        var eye = _camera.GlobalPosition;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach (var t in _trails)
            for (int i = 1; i < t.Count; i++)
            {
                var a = new Vector3((float)(t[i - 1].X - _originX), (float)t[i - 1].Y, (float)(t[i - 1].Z - _originZ));
                var b = new Vector3((float)(t[i].X - _originX), (float)t[i].Y, (float)(t[i].Z - _originZ));
                var along = b - a;
                if (along.LengthSquared() < 1e-6f) continue;
                var side = along.Cross(eye - a).Normalized() * .025f;
                float fa = (float)(1 - (now - t[i - 1].T) / TrailLife), fb = (float)(1 - (now - t[i].T) / TrailLife);
                var ca = new Color(1, .96f, .85f, .55f * fa * fa); var cb = new Color(1, .96f, .85f, .55f * fb * fb);
                void V(Color c, Vector3 p) { mesh.SurfaceSetColor(c); mesh.SurfaceAddVertex(p); }
                V(ca, a - side); V(ca, a + side); V(cb, b + side);
                V(ca, a - side); V(cb, b + side); V(cb, b - side);
            }
        mesh.SurfaceEnd();
    }

    // ---- what the host sends
    void IClientGame.ApplyAnimals(BinaryReader r)
    {
        if (_sampler == null || _generation != null) return;
        var seen = new HashSet<int>();
        double now = Now;
        foreach (var a in Wildlife.Read(r, _catalog.Animals))
        {
            seen.Add(a.Id);
            if (!_animalViews.TryGetValue(a.Id, out var view))
            {
                view = new AnimalView(a);
                AddChild(view);
                _animalViews[a.Id] = view;
            }
            // (its health fell since the last report: it was hit — blood, D132)
            if (_animalHealth.TryGetValue(a.Id, out double before) && a.Health < before - .5) Blood(view);
            _animalHealth[a.Id] = a.Health;
            view.Push(a, now);
        }
        foreach (var id in _animalViews.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _animalViews[id].QueueFree();
            _animalViews.Remove(id);
            _animalHealth.Remove(id); _trackLast.Remove(id);
        }
    }

    void IClientGame.Hurt(double damage, double fromX, double fromZ, string who)
    {
        _camShake = Math.Max(_camShake, .7f);
        _status = $"The {who.ToLowerInvariant()} attacks you!";
        if (NeedsOn) _needs.Health = Math.Max(0, _needs.Health - damage);
    }

    void IClientGame.ArrowShot(int player, double x, double y, double z, float vx, float vy, float vz)
    {
        if (_sampler == null || _generation != null) return;
        Shoot(new Vector3((float)(x - _originX), (float)y, (float)(z - _originZ)), new Vector3(vx, vy, vz), false, 0);
    }

    /// <summary>A carcass (the animal in its death pose), or an arrow that fell (stuck in the ground, tilted).</summary>
    private Node3D CarcassView(PlacedObject o) =>
        _catalog.Animals.TryGetValue(o.Ref, out var def) ? AnimalView.Carcass(def) : new Node3D();

    private static Node3D StuckArrow()
    {
        var root = new Node3D { Name = "StuckArrow" };
        var arrow = GameplayModels.Weapon("arrow");
        arrow.Rotation = new Vector3(.9f, 0, 0); // (tip down into the ground)
        arrow.Position = new Vector3(0, .22f, -.1f);
        root.AddChild(arrow);
        return root;
    }

    /// <summary>The HUD's animal line (Tom could not find any): how many are near and where the nearest is.</summary>
    private string AnimalsHud()
    {
        if (GameSettings.Animals.Index == 0 && _animalsOverride == null) return "Animals: off (Settings → Simulation → Animals)";
        var alive = _animalViews.Values.Where(v => v.State != AnimalState.Dead).ToList();
        if (alive.Count == 0) return "Animals: none near (they live in woods, meadows, wetlands and the cold north)";
        var near = alive.OrderBy(v => (v.X - PlayerX) * (v.X - PlayerX) + (v.Z - PlayerZ) * (v.Z - PlayerZ)).First();
        double dx = near.X - PlayerX, dz = near.Z - PlayerZ;
        string[] compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        // (north is −Z, east +X, as the map and the wind)
        string dir = compass[((int)Math.Round(Math.Atan2(dx, -dz) / (Math.PI / 4)) % 8 + 8) % 8];
        return $"Animals: {alive.Count} within 230 m · nearest {near.Def.Name.ToLowerInvariant()} {Math.Sqrt(dx * dx + dz * dz):0} m {dir}";
    }

    private string HuntingHud() => _me.Weapon switch
    {
        "bow" => $"Holding: bow ({_myInv.CountTag("arrow")} arrows) — hold RMB to aim, hold LMB to draw, release to shoot · 9 puts it away",
        "spear" => "Holding: hunting spear — LMB thrusts at what is in front of you · 8 puts it away",
        "stone_sword" => "Holding: stone-edged sword — LMB cuts at what is in front of you · 0 puts it away",
        _ => "",
    };
}
