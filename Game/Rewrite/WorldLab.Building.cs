using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;

namespace Thornwake.Rewrite;

/// <summary>
/// Building as this PC plays it (D121; the rules are the host's GameRules). B opens it: a bar of categories (number
/// keys) with the current one's shapes — V cycles them (Soulmask style: a wall, V, a window wall, a doorway…), X its
/// materials (roofs: logs or thatch). A ghost shows where the piece goes: onto the pieces nearby (snapped: a wall along a
/// foundation's edge, a roof's eave on a wall's top), or freely on the ground. R and the wheel turn it (Shift: finer),
/// Page Up / Page Down raise it, Ctrl holds the snapping off; aiming at the middle of a piece of the same category swaps
/// it for the chosen shape. The left mouse places a blueprint; E brings materials from the pack; the middle mouse or
/// Backspace takes a piece down (everything back). E opens doors.
/// </summary>
public partial class WorldLab
{
    /// <summary>Collision layer of blueprints: the aiming ray finds them, nobody bumps into them.</summary>
    private const uint BlueprintLayer = 16;
    private bool _building;
    private int _buildCategory;
    private readonly Dictionary<string, string> _buildShape = new(), _buildMaterial = new();
    private double _buildTurn, _buildRaise;
    private Node3D? _ghost;
    private string _ghostPiece = "";
    private Pose? _ghostPose;
    private bool _ghostOk, _buildLmbWas, _buildMmbWas, _ghostFree;
    private long _ghostReplace;
    private string _ghostWhy = "";
    private readonly Dictionary<long, (Node3D Model, StaticBody3D? Body, Node3D? Leaf)> _pieceParts = new();
    private long _aimedPiece, _aimedAny;

    private void SetBuilding(bool on)
    {
        if (on && _catalog.Pieces.Count == 0) { _status = "No building pieces in this version."; return; }
        _building = on;
        if (on)
        {
            if (_axeHeld) ToggleAxe();
            if (_me.RodHeld) ToggleRod();
            _me.Weapon = null;
            _tools.Select(0);
            _status = "Building: choose with the number keys and V; the left mouse places a blueprint, E brings the materials.";
        }
        else { _ghost?.QueueFree(); _ghost = null; _ghostPiece = ""; _ghostPose = null; _status = "Building closed."; ClosePieceMenu(); _buildPicking = false; UnflashBlocker(); }
        ShowBuildMenu(on);
    }

    // ---- the chosen piece: category → shape (V) and material (X)
    private string Category => _catalog.Categories[Math.Clamp(_buildCategory, 0, _catalog.Categories.Count - 1)];
    private List<PieceDef> InCategory => _catalog.Pieces.Values.Where(p => p.Category == Category).ToList();
    private List<string> Materials => InCategory.Select(p => p.Material).Distinct().ToList();
    private string Material => _buildMaterial.TryGetValue(Category, out var m) && Materials.Contains(m) ? m : Materials[0];
    private List<string> Shapes => InCategory.Where(p => p.Material == Material && _catalog.Family(p)[0] == p).Select(p => p.Shape).Distinct().ToList();
    private string Shape => _buildShape.TryGetValue(Category, out var s) && Shapes.Contains(s) ? s : Shapes[0];
    private PieceDef Piece => InCategory.First(p => p.Material == Material && p.Shape == Shape);
    /// <summary>The piece the ghost is now (the chosen one, or another size of it an edge asked for).</summary>
    private PieceDef _ghostDef = null!;

    private void CycleShape(int step)
    {
        var shapes = Shapes;
        int i = (shapes.IndexOf(Shape) + step + shapes.Count) % shapes.Count;
        _buildShape[Category] = shapes[i];
    }

    private void CycleMaterial()
    {
        var mats = Materials;
        if (mats.Count < 2) { _status = $"{Category}: only {Material.ToLowerInvariant()}."; return; }
        string shape = Shape;
        _buildMaterial[Category] = mats[(mats.IndexOf(Material) + 1) % mats.Count];
        if (Shapes.Contains(shape)) _buildShape[Category] = shape; // (the same shape in the other material)
    }

    /// <summary>Keys while building; true when used.</summary>
    private bool BuildingKey(InputEventKey key)
    {
        if (!_building) return false;
        switch (key.Keycode)
        {
            case Key.Escape when _pieceMenu != null: ClosePieceMenu(true); return true;
            case Key.Escape or Key.Tab when _buildPicking: SetPicking(false); return true;
            case Key.Tab: SetPicking(true); return true;
            case Key.Escape or Key.B: SetBuilding(false); return true;
            case >= Key.Key1 and <= Key.Key9 when (int)(key.Keycode - Key.Key1) < _catalog.Categories.Count:
                _buildCategory = (int)(key.Keycode - Key.Key1); return true;
            case Key.V: CycleShape(key.ShiftPressed ? -1 : 1); return true;
            case Key.P: GameSettings.ShowSnapPoints.Value = GameSettings.ShowSnapPoints.On ? 0 : 1; _status = GameSettings.ShowSnapPoints.On ? "Snap points shown (P hides them)." : "Snap points hidden (P shows them)."; return true;
            case Key.X: CycleMaterial(); return true;
            case Key.R: _buildTurn += (key.ShiftPressed ? -1 : 1) * (Piece.Has("diagonal") ? Math.PI / 4 : Math.PI / 2); return true; // (diagonal pieces: 45°)
            case Key.Pageup: _buildRaise += key.ShiftPressed ? 1 : .25; return true;
            case Key.Pagedown: _buildRaise -= key.ShiftPressed ? 1 : .25; return true;
            case Key.Home: _buildRaise = 0; _buildTurn = 0; return true;
            case Key.Backspace or Key.Delete: Dismantle(); return true;
            case Key.Key0: return true; // (no tenth category: not the sword while building)
        }
        return false;
    }

    /// <summary>The wheel turns the ghost while building (Shift: in 5° steps); true when used.</summary>
    private bool BuildingWheel(InputEventMouseButton button)
    {
        if (!_building || !button.Pressed) return false;
        if (Input.IsKeyPressed(Key.Alt) && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _buildRaise += button.ButtonIndex == MouseButton.WheelUp ? .1 : -.1; // (Alt + wheel: up and down)
            return true;
        }
        double step = (Input.IsKeyPressed(Key.Shift) ? 5 : 15) * Math.PI / 180;
        if (button.ButtonIndex == MouseButton.WheelUp) { _buildTurn += step; return true; }
        if (button.ButtonIndex == MouseButton.WheelDown) { _buildTurn -= step; return true; }
        // The right mouse on a placed piece: its menu (change, turn, take down); elsewhere it closes building.
        if (button.ButtonIndex == MouseButton.Right)
        {
            if (BuildAim((float)GameRules.BuildReach) is { Piece: not 0 } a) OpenPieceMenu(a.Piece);
            else SetBuilding(false);
            return true;
        }
        return false;
    }

    /// <summary>The camera's aim: what the ray from the screen's centre hits (terrain, pieces, blueprints) within reach.</summary>
    private (Vector3 Point, long Piece, Vector3 Normal)? BuildAim(float reachFromPlayer)
    {
        var from = _camera.GlobalPosition;
        var look = -_camera.GlobalTransform.Basis.Z;
        float far = from.DistanceTo(_player.Position) + reachFromPlayer + 2;
        var query = PhysicsRayQueryParameters3D.Create(from, from + look * far, 1 | BlueprintLayer);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return null;
        var p = hit["position"].AsVector3();
        if (p.DistanceTo(_player.Position) > reachFromPlayer + 1) return null;
        long piece = hit["collider"].AsGodotObject() is Node n && n.HasMeta("object") ? n.GetMeta("object").AsInt64() : 0;
        return (p, piece, hit["normal"].AsVector3());
    }

    /// <summary>Per frame: the ghost where the piece would go, its colour, the bar's text; placing and taking down.</summary>
    private void UpdateBuilding(double delta, bool captured)
    {
        bool lmb = captured && Input.IsMouseButtonPressed(MouseButton.Left), mmb = captured && Input.IsMouseButtonPressed(MouseButton.Middle);
        bool closedNow = captured && (_pieceMenu != null || _buildPicking); // (clicked back into the world: that click places nothing)
        if (closedNow) { ClosePieceMenu(); _buildPicking = false; if (_pickTitle != null) _pickTitle.Visible = false; }
        bool place = lmb && !_buildLmbWas && !closedNow || _buildTestPlace, take = mmb && !_buildMmbWas;
        _buildTestPlace = false;
        _buildLmbWas = lmb; _buildMmbWas = mmb;
        // (not building: what you aim at — a blueprint's needs — still shows, and E acts on it)
        var aim = BuildAim((float)GameRules.BuildReach);
        _aimedAny = aim?.Piece ?? 0;
        SetAimed(aim?.Piece ?? 0);
        if (!_building)
        {
            UpdateSnapMarkers(null);
            if (_aimedPiece != 0 && _seenObjects.Get(_aimedPiece) is { Blueprint: true } bp && _catalog.Piece(bp.Kind) is { } bdef && _gathering == null)
                _status = $"{bdef.Name} (blueprint): needs {NeedsHere(bdef, bp)} — E brings them.";
            return;
        }
        var def = Piece;
        var placing = def;
        _ghostPose = null; _ghostReplace = 0; _ghostWhy = ""; _ghostFree = false;
        if (aim is { } a)
        {
            var point = new V3(a.Point.X + _originX, a.Point.Y, a.Point.Z + _originZ);
            double desired = def.Category == "Camp" ? _yaw + _buildTurn : _yaw + Math.PI + _buildTurn; // (camp pieces open toward you)
            var nearby = GameRules.PiecesNear(_catalog, _seenObjects, point.X, point.Z, 12).ToList();
            bool snap = !Input.IsKeyPressed(Key.Ctrl);
            // Aiming at the middle of a piece of this category: swap it for this shape (or turn it).
            if (snap && a.Piece != 0 && nearby.FirstOrDefault(p => p.Id == a.Piece) is { Def: not null } target && target.Def.Category == def.Category
                && NearCentre(target.Def, target.Pose, point))
            {
                double flip = Math.Abs(Math.IEEERemainder(target.Pose.Yaw + Math.PI - desired, Math.Tau)) < Math.Abs(Math.IEEERemainder(target.Pose.Yaw - desired, Math.Tau)) ? Math.PI : 0;
                _ghostPose = target.Pose with { Yaw = target.Pose.Yaw + flip };
                _ghostReplace = target.Id;
                // (in the size of the piece it replaces: a diagonal wall becomes a diagonal doorway)
                placing = _catalog.Family(def).FirstOrDefault(m => Math.Abs(m.Length - target.Def.Length) < .05) ?? def;
                if (target.Def.Id == def.Id && flip == 0) _ghostWhy = "it is that already (R turns it)";
            }
            else if (snap && Building.SnapPiece(def, _catalog.Family(def), point, desired, nearby, new V3(a.Normal.X, a.Normal.Y, a.Normal.Z), _catalog.Smaller(def)) is { } s)
            { _ghostPose = s.Pose; placing = s.Piece; }
            else
            {
                // Freely: a foundation stands level above the highest ground under it, raised or lowered by the player.
                var free = Building.Free(def, point, desired, _buildRaise, (x, z) => _sampler!.Height(x, z));
                _ghostFree = true;
                _ghostPose = free;
                if (Building.BlockedBy(def, free, nearby) is { } blocker)
                {
                    _ghostWhy = $"in the way: {blocker.Def.Name.ToLowerInvariant()} ({blocker.Id}, shown red)";
                    FlashBlocker(blocker.Id);
                }
                else if (!Building.Supported(def, free, (x, z) => _sampler!.Height(x, z), nearby))
                    _ghostWhy = def.Ground ? "it needs firm ground (Page Up / Down) or something to rest on" : "it needs something to rest on";
            }
            if (_ghostPose is { } gp && Math.Sqrt((gp.X - PlayerX) * (gp.X - PlayerX) + (gp.Z - PlayerZ) * (gp.Z - PlayerZ)) > GameRules.BuildReach) _ghostWhy = "too far away";
        }
        else _ghostWhy = "aim at the ground or a piece nearby";
        _ghostOk = _ghostPose != null && _ghostWhy.Length == 0;
        _ghostDef = placing;
        if (_ghostPiece != placing.Id) MakeGhost(placing);
        if (!_ghostWhy.StartsWith("in the way")) UnflashBlocker();
        if (_ghost != null)
        {
            _ghost.Visible = _ghostPose != null;
            if (_ghostPose is { } g)
            {
                _ghost.Position = new Vector3((float)(g.X - _originX), (float)g.Y, (float)(g.Z - _originZ));
                _ghost.Rotation = new Vector3(0, (float)g.Yaw, 0);
                GameplayModels.Wear(_ghost, GameplayModels.Ghost(_ghostOk));
            }
        }
        if (place)
        {
            if (_ghostOk && _ghostPose is { } p) Act(new ActionRequest(ActionKind.Place, _ghostReplace, 0, placing.Id, p.X, p.Y, p.Z, (float)p.Yaw));
            else _status = $"Cannot build there: {_ghostWhy}.";
        }
        if (take) Dismantle();
        UpdateBuildMenu(placing);
        UpdateSnapMarkers(placing);
    }

    /// <summary>The hit point is within the middle of a piece (its thin side counts whole): a swap, not a snap beside it.</summary>
    private static bool NearCentre(PieceDef def, Pose pose, V3 point)
    {
        var c = Building.ToWorld(pose, def.BoxCenter);
        double dx = point.X - c.X, dz = point.Z - c.Z, s = Math.Sin(pose.Yaw), co = Math.Cos(pose.Yaw);
        double lx = dx * co - dz * s, lz = dx * s + dz * co, ly = point.Y - c.Y;
        bool Within(double v, double half) => Math.Abs(v) <= (half < .4 ? half + .3 : half * .55);
        return Within(lx, def.BoxHalf.X) && Within(ly, def.BoxHalf.Y) && Within(lz, def.BoxHalf.Z);
    }

    private void MakeGhost(PieceDef def)
    {
        _ghost?.QueueFree();
        _ghost = PieceModel(def) ?? new Node3D();
        _ghost.Name = "Ghost";
        foreach (var light in _ghost.FindChildren("*", "Light3D", true, false)) ((Node3D)light).Visible = false;
        foreach (var child in _ghost.GetChildren()) if (child is Node3D n && n.Name.ToString().StartsWith("Flame")) n.Visible = false;
        GameplayModels.HideCollisionMeshes(_ghost);
        AddChild(_ghost);
        _ghostPiece = def.Id;
    }

    private string NeedsHere(PieceDef def, PlacedObject o)
    {
        var parts = def.Cost.Select(c => (c, have: o.Supplied.Where(s => s.Item == c.Item).Sum(s => s.Count))).Where(x => x.have < x.c.Count)
            .Select(x => $"{x.c.Count - x.have} {_catalog.Item(x.c.Item!).Name.ToLowerInvariant()} ({_myInv.Count(x.c.Item!)} in the pack)");
        return string.Join(", ", parts);
    }

    private void Dismantle()
    {
        if (BuildAim((float)GameRules.BuildReach) is { Piece: not 0 } a) Act(new ActionRequest(ActionKind.Dismantle, a.Piece, 0, "", 0, 0, 0, 0));
        else _status = "Aim at a piece to take it down.";
    }

    /// <summary>The aimed blueprint glows a little.</summary>
    private void SetAimed(long id)
    {
        if (id == _aimedPiece) return;
        if (_aimedPiece != 0 && _pieceParts.TryGetValue(_aimedPiece, out var old) && _seenObjects.Get(_aimedPiece) is { Blueprint: true })
            GameplayModels.Wear(old.Model, GameplayModels.Blueprint(), GameSettings.BlueprintShadows.On);
        _aimedPiece = id;
        if (id != 0 && _pieceParts.TryGetValue(id, out var now) && _seenObjects.Get(id) is { Blueprint: true })
            GameplayModels.Wear(now.Model, GameplayModels.Blueprint(true), GameSettings.BlueprintShadows.On);
    }

    /// <summary>E on the aimed piece: a blueprint gets materials (a moment's work), a door opens or shuts. False: nothing aimed.</summary>
    private bool InteractPiece()
    {
        if (BuildAim((float)GameRules.BuildReach) is not { Piece: not 0 } a || _seenObjects.Get(a.Piece) is not { } o || _catalog.Piece(o.Kind) is not { } def) return false;
        if (o.Blueprint)
        {
            StartTimed(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0), o.X, o.Z, .9, $"Building: {def.Name.ToLowerInvariant()}");
            return true;
        }
        if (def.Has("door")) { Act(new ActionRequest(ActionKind.Use, o.Id, 0, "", 0, 0, 0, 0)); return true; }
        return false;
    }

    // ---- placed pieces as this PC draws them
    /// <summary>A piece's model: its GLB, or the campfire and lean-to drawn in code.</summary>
    private Node3D? PieceModel(PieceDef def) => def.Id switch
    {
        "campfire" => CampfireModel(),
        "lean_to" => LeanTo(),
        _ => def.Model.Length > 0 ? GameplayModels.Instance(def.Model) : null,
    };

    /// <summary>The campfire drawn in code, with a plume of smoke while it burns (SmokePlume; setting Smoke).</summary>
    private Node3D CampfireModel()
    {
        var fire = Thornwake.World.Visuals.Campfire();
        fire.AddChild(new SmokePlume(SmokePlume.Kind.Campfire, _atmosphere.Wind));
        return fire;
    }

    private Node3D PieceView(PlacedObject o, PieceDef def)
    {
        var root = new Node3D { Name = $"Piece{o.Id}" };
        var model = PieceModel(def) ?? new Node3D();
        GameplayModels.HideCollisionMeshes(model);
        root.AddChild(model);
        var leaf = model.FindChild("Leaf", true, false) as Node3D;
        StaticBody3D? body = null;
        if (def.Model.Length > 0)
        {
            body = new StaticBody3D { Name = "Body", CollisionMask = 0 };
            body.SetMeta("object", o.Id);
            StaticBody3D? leafBody = null;
            foreach (var (shape, at, under) in GameplayModels.Shapes(def.Model, model))
            {
                if (under == "Leaf" && leaf != null)
                {
                    if (leafBody == null) { leafBody = new StaticBody3D { Name = "LeafBody", CollisionMask = 0 }; leafBody.SetMeta("object", o.Id); leaf.AddChild(leafBody); }
                    leafBody.AddChild(new CollisionShape3D { Shape = shape, Transform = at });
                }
                else body.AddChild(new CollisionShape3D { Shape = shape, Transform = at });
            }
            model.AddChild(body);
        }
        else
        {
            // (drawn in code — the campfire, the lean-to: a box to aim at, to take down or swap; nobody bumps into it)
            body = new StaticBody3D { Name = "Body", CollisionMask = 0, CollisionLayer = BlueprintLayer };
            body.SetMeta("object", o.Id);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((float)def.BoxHalf.X, (float)def.BoxHalf.Y, (float)def.BoxHalf.Z) * 2 },
                Position = new Vector3((float)def.BoxCenter.X, (float)def.BoxCenter.Y, (float)def.BoxCenter.Z) });
            model.AddChild(body);
        }
        _pieceParts[o.Id] = (model, body, leaf);
        RefreshPiece(o, def);
        return root;
    }

    /// <summary>A piece changed (finished, a door opened): its look and whether it is solid.</summary>
    private void RefreshPiece(PlacedObject o, PieceDef def)
    {
        if (!_pieceParts.TryGetValue(o.Id, out var parts)) return;
        GameplayModels.Wear(parts.Model, o.Blueprint ? GameplayModels.Blueprint(o.Id == _aimedPiece) : null, GameSettings.BlueprintShadows.On);
        uint layer = o.Blueprint || def.Model.Length == 0 ? BlueprintLayer : 1;
        if (parts.Body != null) parts.Body.CollisionLayer = layer;
        if (parts.Leaf?.GetNodeOrNull<StaticBody3D>("LeafBody") is { } lb) lb.CollisionLayer = layer;
        // (the lean-to and the campfire wear the ghost look while blueprints; their flames show only when lit)
        if (def.Model.Length == 0) foreach (var light in parts.Model.FindChildren("*", "Light3D", true, false)) ((Node3D)light).Visible = !o.Blueprint && o.Lit;
        if (parts.Model.GetNodeOrNull<SmokePlume>("FlameSmoke") is { } smoke) smoke.Burning = !o.Blueprint && o.Lit;
    }

    /// <summary>Per frame: doors swing toward open or shut.</summary>
    private void UpdatePieces(double delta)
    {
        foreach (var (id, parts) in _pieceParts)
            if (parts.Leaf != null && _seenObjects.Get(id) is { } o)
            {
                float target = o.State == 1 ? -Mathf.Pi / 2 : 0;
                var r = parts.Leaf.Rotation;
                parts.Leaf.Rotation = new Vector3(r.X, Mathf.MoveToward(r.Y, target, (float)delta * 3.5f), r.Z);
            }
    }

    private long _flashed;

    /// <summary>The piece that blocks the ghost glows red while it does (Tom: "it said something was there, but nothing was visible").</summary>
    private void FlashBlocker(long id)
    {
        if (_flashed == id) return;
        UnflashBlocker();
        if (!_pieceParts.TryGetValue(id, out var parts)) return;
        _flashed = id;
        GameplayModels.Wear(parts.Model, GameplayModels.Ghost(false));
    }

    private void UnflashBlocker()
    {
        if (_flashed == 0) return;
        long id = _flashed; _flashed = 0;
        if (_seenObjects.Get(id) is { } o && _catalog.Piece(o.Kind) is { } def) RefreshPiece(o, def);
    }

    private void PieceGone(long id) { _pieceParts.Remove(id); if (_aimedPiece == id) _aimedPiece = 0; }

    private bool _shadowHooked;

    private void BuildingWorldReady()
    {
        if (!_shadowHooked)
        {
            _shadowHooked = true;
            GameSettings.BlueprintShadows.Changed += _ =>
            {
                foreach (var id in _pieceParts.Keys.ToList())
                    if (_seenObjects.Get(id) is { } o && _catalog.Piece(o.Kind) is { } d) RefreshPiece(o, d);
            };
        }
        _pieceParts.Clear();
        _aimedPiece = 0;
        if (_building) SetBuilding(false);
    }

    private string BuildingHud() => _building ? "Building — see the bar below · B closes" : "";

    /// <summary>Walking into the edge of a building piece no higher than a step (a foundation's deck, a stair's
    /// tread): up onto it. Only pieces: the terrain's own steep faces stay as they are.</summary>
    private void StepUp(Vector3 move, float speed, float dt)
    {
        if (move.LengthSquared() < .01f || !_player.IsOnFloor()) return;
        bool blocked = false;
        for (int i = 0; i < _player.GetSlideCollisionCount() && !blocked; i++)
        {
            var c = _player.GetSlideCollision(i);
            blocked = c.GetNormal().Y < .3f && c.GetCollider() is Node n && n.HasMeta("object");
        }
        if (!blocked) return;
        const float Step = .55f;
        var motion = move.Normalized() * Mathf.Max(.12f, speed * dt);
        var t = _player.GlobalTransform;
        if (_player.TestMove(t, Vector3.Up * Step)) return;           // (no room above)
        var up = new Transform3D(t.Basis, t.Origin + Vector3.Up * Step);
        if (_player.TestMove(up, motion)) return;                      // (still blocked higher up: a wall, not a step)
        _player.GlobalPosition = up.Origin + motion;
        _player.MoveAndCollide(Vector3.Down * Step);
    }
}
