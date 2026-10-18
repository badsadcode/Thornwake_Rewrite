using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Felling a tree in three acts (GAMEPLAY_DESIGN.md §5a, agreed with Tom; D112), as this PC draws it — the rules
/// (hits, the notch's side, the fall's direction, the trunk, the logs) are the host's GameRules:
/// 1. the axe's hits cut a notch on the side the first ones came from (it grows with every hit), the tree shakes, and
///    near the end it creaks, leans and drops leaves;
/// 2. it tips over its base toward the notch (scripted: a trunk pivoting under gravity — a whole tree tumbling as a rigid
///    body looks stiff), and on impact the crown breaks up (leaves, twigs, dust, a shake of the camera nearby) and the
///    tree becomes a bare trunk; a tree coming down on you hurts;
/// 3. the bare trunk is a physical log: the PC of its owner (who felled it, or who last pushed it) simulates it and
///    reports where it is; it rolls down slopes, can be pushed, hurts when it rolls into you fast, and the axe cuts it
///    into logs (each cut shows on it).
/// </summary>
public partial class WorldLab
{
    private sealed class FallingTree
    {
        public required Node3D Pivot;   // at the tree's base (placed with the floating origin)
        public required Node3D Model;
        public long Cx, Cz;
        public double X, Y, Z, Height, Scale;
        public float Shake, Angle, Speed, MaxAngle, Lean, LeanTarget;
        public Vector3 Axis, NotchDir;
        public int Bounces, Hits, Total;
        public string Species = "";
        public bool Falling, Landed, Hurt, Creaked;
        public MeshInstance3D? Notch, BackCut;
    }

    private sealed class Trunk
    {
        public required RigidBody3D Body;
        public required Node3D Visual;
        public long Id;
        public float Length, Radius, Shake;
        public bool Waiting, Owned;
        public double SendTimer, HurtTimer;
        public Vector3 LastSentPos, PrevPos;
        public Quaternion LastSentRot;
        public int Cuts = -1;
    }

    private readonly Dictionary<(long, long), FallingTree> _fallingTrees = new();
    private readonly Dictionary<long, Trunk> _trunks = new();
    private readonly Dictionary<long, Node3D> _waitingViews = new();   // piles shown when their tree's crown comes down
    private Vegetation.LatticeTree? _chopTarget;
    private long _chopTrunk = -1;
    private Vector3 _chopPoint;
    private bool _chopHooked;
    private float _camShake;
    private double _feltOriginX = double.NaN, _feltOriginZ, _claimTimer, _flickerFell;
    private int MyId => GameClient?.Id ?? 1;
    private static readonly StandardMaterial3D CutWood = new() { AlbedoColor = new Color("c9a46a"), Roughness = .9f };

    private void FellingWorldReady()
    {
        foreach (var f in _fallingTrees.Values) f.Pivot.QueueFree();
        _fallingTrees.Clear();
        foreach (var t in _trunks.Values) t.Body.QueueFree();
        _trunks.Clear();
        _waitingViews.Clear();
    }

    // ---- act 1: chopping (the axe held: 6; the left mouse held on a tree or a trunk in front of you)
    private void UpdateChopping(double delta, bool swinging)
    {
        if (!_chopHooked) { _me.ChopHit += OnChopHit; _chopHooked = true; }
        _chopTarget = null; _chopTrunk = -1;
        if (!swinging) { _me.Chopping = false; return; }
        var forward = -_camera.GlobalTransform.Basis.Z;
        var flat = new Vector2(forward.X, forward.Z).Normalized();
        var me = new Vector3(_player.Position.X, 0, _player.Position.Z);
        double best = double.MaxValue;
        // A trunk lying in front: the nearest point of its axis within reach.
        foreach (var t in _trunks.Values.Where(t => !t.Waiting))
        {
            var (a, b) = TrunkEnds(t);
            var p = ClosestOnSegment(new Vector3(a.X, 0, a.Z), new Vector3(b.X, 0, b.Z), me);
            float d = p.DistanceTo(me) - t.Radius;
            var to = new Vector2(p.X - me.X, p.Z - me.Z);
            if (d > 1.7f || (to.Length() > .3f && to.Normalized().Dot(flat) < .4f)) continue;
            if (d < best) { best = d; _chopTrunk = t.Id; _chopPoint = ClosestOnSegment(a, b, _player.Position + Vector3.Up * .5f); }
        }
        // A standing tree in front: within reach, inside a cone around where you look (one already falling does not count).
        foreach (var tree in Vegetation.TreesNear(_sampler!, PlayerX, PlayerZ, GameRules.ChopReach, DetailConfig.For(_sampler!)))
        {
            if (_fallingTrees.TryGetValue((tree.Cx, tree.Cz), out var f) && f.Falling) continue;
            var to = new Vector2((float)(tree.X - PlayerX), (float)(tree.Z - PlayerZ));
            float d = to.Length();
            if (d > .3f && to.Normalized().Dot(flat) < .5f) continue; // more than 60° off
            if (d - .6 < best) { best = d - .6; _chopTarget = tree; _chopTrunk = -1; }
        }
        if (_chopTarget == null && _chopTrunk < 0) { _me.Chopping = false; _status = "Nothing to chop within reach in front of you."; return; }
        _me.Chopping = true;
        // The bar over the head: the hits so far (a tree: of those it takes; a trunk: of its cuts).
        if (_chopTarget is { } shown && _fallingTrees.TryGetValue((shown.Cx, shown.Cz), out var ft))
            ShowAction($"Felling a {ft.Species}", ft.Total > 0 ? (double)ft.Hits / ft.Total : 0);
        else if (_chopTrunk >= 0 && _seenObjects.Get(_chopTrunk) is { } lying)
            ShowAction("Cutting up the trunk", (double)lying.Hits / GameRules.TrunkHits(lying.Size));
        if (_chopTarget is { } target)
        {
            _me.Facing = Mathf.Atan2((float)(target.X - PlayerX), (float)(target.Z - PlayerZ));
            StandAlone(target); // (drawn on its own from the first swing: it will shake)
        }
        else _me.Facing = Mathf.Atan2(_chopPoint.X - _player.Position.X, _chopPoint.Z - _player.Position.Z);
    }

    private void OnChopHit()
    {
        if (!_axeHeld) return;
        if (_chopTrunk >= 0 && _trunks.TryGetValue(_chopTrunk, out var trunk))
        {
            Chips(_chopPoint, (_player.Position - _chopPoint).Normalized());
            trunk.Shake = 1;
            Act(new ActionRequest(ActionKind.ChopTrunk, _chopTrunk, 0, "", 0, 0, 0, 0));
            return;
        }
        if (_chopTarget is not { } t) return;
        var at = new Vector3((float)(t.X - _originX), (float)t.Y + .55f, (float)(t.Z - _originZ));
        var toMe = new Vector3(_player.Position.X - at.X, 0, _player.Position.Z - at.Z).Normalized();
        Chips(at + toMe * (float)GameRules.TrunkRadius(t.Scale), toMe);
        Act(new ActionRequest(ActionKind.Chop, t.Cx, t.Cz, "", t.X, t.Y, t.Z, 0));
    }

    /// <summary>The tree of the cell drawn on its own (a proxy), left out of the forest's batches (rebuilt around it).</summary>
    private FallingTree StandAlone(Vegetation.LatticeTree t)
    {
        if (_fallingTrees.TryGetValue((t.Cx, t.Cz), out var known)) return known;
        var pivot = new Node3D { Name = $"Felling{t.Cx}_{t.Cz}" };
        var model = _streamer.Field.Proxy(t.Species, t.Scale, t.Yaw);
        pivot.AddChild(model);
        AddChild(pivot);
        var f = new FallingTree { Pivot = pivot, Model = model, Cx = t.Cx, Cz = t.Cz, X = t.X, Y = t.Y, Z = t.Z, Scale = t.Scale, Height = GameRules.TreeHeight(t.Scale),
            Species = Vegetation.Species[t.Species].Split('_')[0].ToLowerInvariant() };
        _fallingTrees[(t.Cx, t.Cz)] = f;
        _sampler!.Hidden.Remove(ObjectRemovals.Trees, t.Cx, t.Cz, 0);
        _streamer.Invalidate(t.X - 8, t.Z - 8, t.X + 8, t.Z + 8);
        PlaceFalling(f);
        return f;
    }

    void IClientGame.TreeHit(in TreeEvent e)
    {
        if (_sampler == null || _generation != null) return;
        if (Vegetation.TreeAt(_sampler, e.Cx, e.Cz, DetailConfig.For(_sampler)) is not { } t) return;
        var f = StandAlone(t);
        f.Shake = 1;
        f.Hits++;
        f.Total = e.Total;
        var dir = new Vector3((float)e.DirX, 0, (float)e.DirZ).Normalized();
        if (!e.Felled)
        {
            // The notch on its side grows; then the back cut; near the end it creaks, leans toward the notch, drops leaves.
            f.NotchDir = dir;
            float progress = (e.Total - e.HitsLeft) / (float)Math.Max(e.Total, 1);
            float before = (e.Total - e.HitsLeft - 1) / (float)Math.Max(e.Total, 1);
            UpdateNotch(f, progress);
            // (Old Berta's lesson, as a hint for whoever is chopping it)
            if (before < .6f && progress >= .6f && _chopTarget is { } mine && mine.Cx == e.Cx && mine.Cz == e.Cz)
                _status = "The notch is cut: it will fall toward it. Finish from behind — don't stand where it lands.";
            if (e.HitsLeft <= 2)
            {
                f.LeanTarget = .025f * (3 - e.HitsLeft);
                if (!f.Creaked) { f.Creaked = true; Leaves(f, false); }
            }
            return;
        }
        if (f.Falling) return;
        // It tips over its base toward Dir, about the horizontal axis across that direction.
        f.Falling = true;
        f.NotchDir = dir;
        f.Axis = Vector3.Up.Cross(dir).Normalized();
        f.Angle = Math.Max(f.Lean, .05f); f.Lean = 0; f.LeanTarget = 0;
        f.Speed = .25f; // (the last cut gives it a nudge: no long, slow start)
        UpdateNotch(f, 1);
        // It stops where the trunk meets the ground (higher ground ahead stops it sooner).
        double rise = _sampler.Height(t.X + dir.X * f.Height * .7, t.Z + dir.Z * f.Height * .7) - t.Y;
        f.MaxAngle = Mathf.Pi / 2 - (float)Math.Atan2(rise, f.Height * .7) - .03f;
    }

    /// <summary>The notch: a pale cut face on its side at knee height, wider and deeper with every hit (placeholder for a
    /// cut in the model); after it, a thin back cut on the other side.</summary>
    private static void UpdateNotch(FallingTree f, float progress)
    {
        float r = (float)GameRules.TrunkRadius(f.Scale);
        float depth = r * .6f * Math.Clamp(progress / .6f, .15f, 1);
        float yaw = Mathf.Atan2(f.NotchDir.X, f.NotchDir.Z);
        if (f.Notch == null) { f.Notch = new MeshInstance3D { MaterialOverride = CutWood }; f.Pivot.AddChild(f.Notch); }
        f.Notch.Mesh = new BoxMesh { Size = new Vector3(r * 1.5f, .1f + depth * .5f, depth) };
        f.Notch.Rotation = new Vector3(0, yaw, 0);
        f.Notch.Position = f.NotchDir * (r - depth / 2 + .015f) + Vector3.Up * .5f;
        if (progress > .6f)
        {
            if (f.BackCut == null) { f.BackCut = new MeshInstance3D { MaterialOverride = CutWood }; f.Pivot.AddChild(f.BackCut); }
            float back = r * .5f * Math.Clamp((progress - .6f) / .4f, .1f, 1);
            f.BackCut.Mesh = new BoxMesh { Size = new Vector3(r * 1.3f, .035f, back) };
            f.BackCut.Rotation = new Vector3(0, yaw, 0);
            f.BackCut.Position = -f.NotchDir * (r - back / 2 + .015f) + Vector3.Up * .62f;
        }
    }

    /// <summary>Per frame: falling trees, trunks, the camera's shake.</summary>
    private void UpdateFelling(double delta)
    {
        float dt = (float)delta;
        _flickerFell += delta;
        // The floating origin moved: physical trunks keep their place in the world.
        if (!double.IsNaN(_feltOriginX) && (_feltOriginX != _originX || _feltOriginZ != _originZ))
        {
            var shift = new Vector3((float)(_feltOriginX - _originX), 0, (float)(_feltOriginZ - _originZ));
            foreach (var t in _trunks.Values) t.Body.GlobalPosition += shift;
        }
        _feltOriginX = _originX; _feltOriginZ = _originZ;

        foreach (var (key, f) in _fallingTrees.ToList())
        {
            f.Shake = Mathf.MoveToward(f.Shake, 0, dt * 2.2f);
            f.Lean = Mathf.MoveToward(f.Lean, f.LeanTarget, dt * .05f);
            if (f.Falling)
            {
                f.Speed += 1.5f * 9.81f / (float)f.Height * Mathf.Sin(Math.Max(f.Angle, .02f)) * dt;
                f.Angle += f.Speed * dt;
                HurtByFall(f);
                if (f.Angle >= f.MaxAngle) { Impact(f); f.Pivot.QueueFree(); _fallingTrees.Remove(key); continue; }
            }
            PlaceFalling(f);
        }
        UpdateTrunks(delta);
        // The camera shakes after a crash nearby or a blow (small, quickly gone).
        _camShake = Mathf.MoveToward(_camShake, 0, dt * 2.5f);
        _camera.HOffset = _camShake > 0 ? .07f * _camShake * Mathf.Sin((float)_flickerFell * 61) : 0;
        _camera.VOffset = _camShake > 0 ? .07f * _camShake * Mathf.Sin((float)_flickerFell * 47 + 1) : 0;
    }

    private void PlaceFalling(FallingTree f)
    {
        f.Pivot.Position = new Vector3((float)(f.X - _originX), (float)f.Y, (float)(f.Z - _originZ));
        Basis basis;
        if (f.Falling) basis = new Basis(f.Axis, f.Angle);
        else if (f.Lean > 0 && f.NotchDir != Vector3.Zero) basis = new Basis(Vector3.Up.Cross(f.NotchDir).Normalized(), f.Lean);
        else basis = Basis.Identity;
        // A hit shakes the crown: a quick, dying sway about the trunk's base.
        if (f.Shake > 0)
            basis = basis * new Basis(Vector3.Right, .025f * f.Shake * Mathf.Sin((float)_flickerFell * 38)) * new Basis(Vector3.Forward, .02f * f.Shake * Mathf.Sin((float)_flickerFell * 31 + 1));
        f.Pivot.Basis = basis;
    }

    /// <summary>A tree coming down on you hurts (each PC looks after its own player): the trunk's sweep through you.</summary>
    private void HurtByFall(FallingTree f)
    {
        if (f.Hurt || f.Speed < .5f) return;
        var a = f.Pivot.Position;
        var b = a + f.Pivot.Basis * Vector3.Up * (float)f.Height * .85f;
        var me = _player.Position + Vector3.Up * .9f;
        if (ClosestOnSegment(a, b, me).DistanceTo(me) > (float)GameRules.TrunkRadius(f.Scale) + .5f) return;
        f.Hurt = true;
        Hurt(20 + 25 * f.Scale, "The tree comes down on you!");
    }

    private void Hurt(double amount, string what)
    {
        if (!NeedsOn) { _status = what; _camShake = 1; return; }
        _needs.Health = Math.Max(0, _needs.Health - amount);
        _status = $"{what} (−{amount:0} health)";
        _camShake = 1;
    }

    /// <summary>Act 2's end: the crown breaks up — leaves and twigs burst, dust along the trunk — and the bare trunk and
    /// the branches it leaves are there.</summary>
    private void Impact(FallingTree f)
    {
        var basis = new Basis(f.Axis, f.MaxAngle);
        var crown = f.Pivot.Position + basis * Vector3.Up * (float)f.Height * .75f;
        Leaves(f, true, crown);
        var dust = Burst(f.Pivot.Position + basis * Vector3.Up * (float)f.Height * .35f, new Color(.55f, .48f, .38f, .55f), 40, 1.6f, 3, new Vector3(0, 1, 0), 70, (float)f.Height * .35f);
        dust.Gravity = new Vector3(0, -.6f, 0);
        float near = 1 - Mathf.Clamp(f.Pivot.Position.DistanceTo(_player.Position) / 30, 0, 1);
        _camShake = Math.Max(_camShake, near * .8f);
        // The trunk and the crown's branches nearby appear now (the rules made them when it was felled).
        foreach (var t in _trunks.Values.Where(t => t.Waiting).ToList())
        {
            var (a, b) = TrunkEnds(t);
            if (((a + b) / 2).DistanceTo(f.Pivot.Position) > (float)f.Height) continue;
            // Lying where this tree came down: along its fall, its base at the stump.
            float r = t.Radius;
            var axis = basis * Vector3.Up;
            var centre = f.Pivot.Position + axis * (t.Length / 2) + Vector3.Up * r * 1.05f; // (resting on the ground: physics settles it)
            t.Body.GlobalTransform = new Transform3D(basis, centre);
            Show(t);
            if (t.Owned) { t.Body.Freeze = false; SendTrunk(t, force: true); }
        }
        foreach (var (id, view) in _waitingViews.ToList())
            if (_seenObjects.Get(id) is { } o && Math.Abs(o.X - f.X) < f.Height * 1.2 && Math.Abs(o.Z - f.Z) < f.Height * 1.2)
            { view.Visible = true; _waitingViews.Remove(id); }
    }

    private void Leaves(FallingTree f, bool burst, Vector3? at = null)
    {
        var pos = at ?? f.Pivot.Position + Vector3.Up * (float)f.Height * .75f;
        var leaves = Burst(pos, new Color(.33f, .45f, .2f), burst ? 90 : 25, burst ? 1.8f : 3.5f, burst ? 4 : .4f, Vector3.Up, burst ? 80 : 180, (float)f.Height * .25f);
        leaves.Gravity = new Vector3(0, burst ? -2.5f : -.7f, 0);
        if (burst) Burst(pos, new Color(.42f, .3f, .18f), 30, 1.2f, 5, Vector3.Up, 70, (float)f.Height * .2f); // twigs
    }

    /// <summary>A one-shot burst of small flat pieces (leaves, twigs, dust).</summary>
    private CpuParticles3D Burst(Vector3 at, Color colour, int amount, float life, float speed, Vector3 dir, float spread, float size)
    {
        var p = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = amount, Lifetime = life, Explosiveness = .9f, LocalCoords = false,
            Mesh = new QuadMesh { Size = new Vector2(.09f, .07f), Material = new StandardMaterial3D { AlbedoColor = colour, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1,
                Transparency = colour.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled } },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = size, Position = at,
            Direction = dir, Spread = spread, InitialVelocityMin = speed * .4f, InitialVelocityMax = speed,
            AngularVelocityMin = -300, AngularVelocityMax = 300, ScaleAmountMin = .7f, ScaleAmountMax = 1.6f,
        };
        AddChild(p);
        GetTree().CreateTimer(life + .5).Timeout += p.QueueFree;
        return p;
    }

    /// <summary>Wood chips flying off where the axe lands.</summary>
    private void Chips(Vector3 at, Vector3 outward)
    {
        var mesh = new BoxMesh { Size = new Vector3(.05f, .015f, .03f), Material = CutWood };
        var chips = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = 14, Lifetime = .9, Explosiveness = .95f, LocalCoords = false,
            Mesh = mesh, Direction = outward + Vector3.Up * .6f, Spread = 35, Position = at,
            InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, -9.8f, 0),
            AngularVelocityMin = -400, AngularVelocityMax = 400,
        };
        AddChild(chips);
        GetTree().CreateTimer(1.5).Timeout += chips.QueueFree;
    }

    // ---- act 3: the bare trunk, a physical log
    private void TrunkChanged(PlacedObject o)
    {
        if (!_trunks.TryGetValue(o.Id, out var t))
        {
            float length = (float)GameRules.TrunkLength(o.Size), r = (float)GameRules.TrunkRadius(o.Size);
            var body = new RigidBody3D
            {
                Name = $"Trunk{o.Id}", Mass = 90 + 70 * o.Size, CollisionLayer = 1, CollisionMask = 1, LinearDamp = .4f, AngularDamp = 1.2f,
                PhysicsMaterialOverride = new PhysicsMaterial { Friction = .9f, Bounce = .05f }, ContinuousCd = true,
                Freeze = true, FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic,
            };
            body.SetMeta("trunk", o.Id);
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Height = length, Radius = r } });
            var visual = new Node3D();
            var bark = _streamer.Field.Bark(o.Variant) ?? new StandardMaterial3D { AlbedoColor = new Color("6b4a2e"), Roughness = .95f };
            visual.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r * .8f, BottomRadius = r, Height = length, RadialSegments = 12 }, MaterialOverride = bark });
            foreach (var (end, radius) in new[] { (-length / 2, r), (length / 2, r * .8f) })
                visual.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius * .97f, BottomRadius = radius * .97f, Height = .02f, RadialSegments = 12 }, MaterialOverride = CutWood, Position = new Vector3(0, end, 0) });
            body.AddChild(visual);
            AddChild(body);
            t = new Trunk { Body = body, Visual = visual, Id = o.Id, Length = length, Radius = r };
            _trunks[o.Id] = t;
            body.GlobalTransform = Pose(o);
            t.PrevPos = body.GlobalPosition;
            // Just felled here: it appears when its tree comes down (a late joiner, or one felled far away: at once).
            t.Waiting = _fallingTrees.Values.Any(f => f.Falling && Math.Abs(f.X - o.X) < f.Height && Math.Abs(f.Z - o.Z) < f.Height);
            if (t.Waiting) Hide(t); else Show(t);
        }
        bool owned = o.Owner == MyId;
        if (owned != t.Owned)
        {
            t.Owned = owned;
            if (!t.Waiting) t.Body.Freeze = !owned;
            if (owned) SendTrunk(t, force: true);
        }
        // Cuts so far: a pale ring at each place the trunk is being cut through into logs.
        int cuts = o.Hits / 2;
        if (cuts != t.Cuts)
        {
            t.Cuts = cuts;
            foreach (var ring in t.Visual.GetChildren().Where(c => c.Name.ToString().StartsWith("Cut"))) ring.QueueFree();
            int logs = GameRules.TrunkLogs(o.Size);
            for (int k = 1; k <= Math.Min(cuts, logs - 1); k++)
            {
                float y = -t.Length / 2 + t.Length * k / logs;
                t.Visual.AddChild(new MeshInstance3D { Name = $"Cut{k}", Mesh = new CylinderMesh { TopRadius = t.Radius * 1.02f, BottomRadius = t.Radius * 1.02f, Height = .06f, RadialSegments = 12 },
                    MaterialOverride = CutWood, Position = new Vector3(0, y, 0) });
            }
        }
    }

    private Transform3D Pose(PlacedObject o) =>
        new(new Basis(new Quaternion(o.Qx, o.Qy, o.Qz, o.Qw).Normalized()), new Vector3((float)(o.X - _originX), (float)o.Y, (float)(o.Z - _originZ)));

    private static void Hide(Trunk t) { t.Waiting = true; t.Body.Visible = false; t.Body.Freeze = true; t.Body.CollisionLayer = 0; }
    private static void Show(Trunk t) { t.Waiting = false; t.Body.Visible = true; t.Body.CollisionLayer = 1; t.Body.Freeze = !t.Owned; }

    void IClientGame.ObjectMoved(long id, double x, double y, double z, float qx, float qy, float qz, float qw)
    {
        if (_seenObjects.Get(id) is not { } o || o.Owner == MyId) return; // (mine: this PC moves it)
        o.X = x; o.Y = y; o.Z = z; o.Qx = qx; o.Qy = qy; o.Qz = qz; o.Qw = qw;
    }

    private void ObjectGone(long id)
    {
        if (!_trunks.Remove(id, out var t)) return;
        // Cut through: chips where it lay, then the logs (the rules' log pile) are there.
        if (t.Body.Visible) Chips(t.Body.GlobalPosition + Vector3.Up * t.Radius, Vector3.Up);
        t.Body.QueueFree();
    }

    private void WaitForImpact(long id, Node3D view, PlacedObject o)
    {
        if (o.Kind != "branch_pile" || !_fallingTrees.Values.Any(f => f.Falling && Math.Abs(f.X - o.X) < f.Height * 1.2 && Math.Abs(f.Z - o.Z) < f.Height * 1.2)) return;
        view.Visible = false;
        _waitingViews[id] = view;
    }

    private void UpdateTrunks(double delta)
    {
        foreach (var t in _trunks.Values)
        {
            if (t.Waiting || _seenObjects.Get(t.Id) is not { } o) continue;
            var pos = t.Body.GlobalPosition;
            float speed = (pos - t.PrevPos).Length() / (float)Math.Max(delta, 1e-4);
            t.PrevPos = pos;
            if (t.Owned)
            {
                // Out of reach of this PC's ground (no collision far away): it waits where it is.
                bool far = pos.DistanceTo(_player.Position) > 150;
                if (far != t.Body.Freeze) t.Body.Freeze = far;
                SendTrunk(t);
            }
            else
            {
                // Someone else's PC moves it: follow where it says (smoothly).
                var target = Pose(o);
                var k = 1 - Mathf.Exp(-12 * (float)delta);
                var q = t.Body.GlobalTransform.Basis.GetRotationQuaternion().Slerp(target.Basis.GetRotationQuaternion(), k);
                t.Body.GlobalTransform = new Transform3D(new Basis(q), pos.Lerp(target.Origin, k));
            }
            // A cut shakes it a little.
            t.Shake = Mathf.MoveToward(t.Shake, 0, (float)delta * 4);
            t.Visual.Position = t.Shake > 0 ? new Vector3(.02f * t.Shake * Mathf.Sin((float)_flickerFell * 50), 0, 0) : Vector3.Zero;
            // Rolling into you fast hurts.
            t.HurtTimer -= delta;
            if (speed > 3 && t.HurtTimer <= 0)
            {
                var (a, b) = TrunkEnds(t);
                var me = _player.Position + Vector3.Up * .9f;
                if (ClosestOnSegment(a, b, me).DistanceTo(me) < t.Radius + .55f) { t.HurtTimer = 1.5; Hurt(speed * 3.5, "A rolling trunk hits you!"); }
            }
        }
    }

    /// <summary>This PC moves the trunk: where it is now goes to the host ~10 a second while it moves.</summary>
    private void SendTrunk(Trunk t, bool force = false)
    {
        t.SendTimer -= GetProcessDeltaTime();
        if (!force && t.SendTimer > 0) return;
        var tr = t.Body.GlobalTransform;
        var q = tr.Basis.GetRotationQuaternion();
        if (!force && tr.Origin.DistanceTo(t.LastSentPos) < .02f && q.AngleTo(t.LastSentRot) < .01f) return;
        t.SendTimer = .1;
        t.LastSentPos = tr.Origin; t.LastSentRot = q;
        double x = tr.Origin.X + _originX, z = tr.Origin.Z + _originZ;
        if (_seenObjects.Get(t.Id) is { } o) { o.X = x; o.Y = tr.Origin.Y; o.Z = z; o.Qx = q.X; o.Qy = q.Y; o.Qz = q.Z; o.Qw = q.W; }
        GameClient?.SendMove(t.Id, x, tr.Origin.Y, z, q.X, q.Y, q.Z, q.W);
    }

    /// <summary>Physics frame, after the player moved: walking into a trunk pushes it (one this PC moves), or takes it
    /// over from the PC that moves it (then the push works).</summary>
    private void PushObjects(double delta)
    {
        _claimTimer -= delta;
        for (int i = 0; i < _player.GetSlideCollisionCount(); i++)
        {
            var c = _player.GetSlideCollision(i);
            if (c.GetCollider() is not RigidBody3D body || !body.HasMeta("trunk")) continue;
            long id = body.GetMeta("trunk").AsInt64();
            if (!_trunks.TryGetValue(id, out var t) || t.Waiting) continue;
            if (t.Owned && !body.Freeze)
            {
                var push = -c.GetNormal() with { Y = 0 };
                body.ApplyImpulse(push.Normalized() * 420 * (float)delta, c.GetPosition() - body.GlobalPosition);
            }
            else if (!t.Owned && _claimTimer <= 0) { _claimTimer = 1; Act(new ActionRequest(ActionKind.Claim, id, 0, "", 0, 0, 0, 0)); }
        }
    }

    private static (Vector3 A, Vector3 B) TrunkEnds(Trunk t)
    {
        var tr = t.Body.GlobalTransform;
        var axis = tr.Basis.Y.Normalized() * (t.Length / 2);
        return (tr.Origin - axis, tr.Origin + axis);
    }

    private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        float k = ab.LengthSquared() < 1e-6f ? 0 : Mathf.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return a + ab * k;
    }

    /// <summary>The branches the crown left (placeholder).</summary>
    private static Node3D BranchPile(PlacedObject o)
    {
        var root = new Node3D { Name = "BranchPile" };
        var bark = new StandardMaterial3D { AlbedoColor = new Color("6b4a2e"), Roughness = .95f };
        var leaf = new StandardMaterial3D { AlbedoColor = new Color("4d6b2f"), Roughness = 1 };
        for (int i = 0; i < 7; i++)
        {
            float a = i * 1.3f;
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .02f, BottomRadius = .04f, Height = 1.4f + i % 3 * .3f, RadialSegments = 5 }, MaterialOverride = bark,
                Position = new Vector3(Mathf.Cos(a) * .4f, .12f + i % 2 * .08f, Mathf.Sin(a) * .4f), Rotation = new Vector3(Mathf.Pi / 2 - .15f, a, 0) });
            root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .25f, Height = .3f, RadialSegments = 6, Rings = 3 }, MaterialOverride = leaf,
                Position = new Vector3(Mathf.Cos(a) * .9f, .18f, Mathf.Sin(a) * .9f) });
        }
        return root;
    }
}
