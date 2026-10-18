using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;

namespace Thornwake.Rewrite;

/// <summary>
/// An animal as this PC draws it (D120): its model (Quaternius, scaled to the kind's height), the animation of what it is
/// doing — grazing, walking, galloping, attacking, dying — and its place from the host's reports (ten a second), drawn
/// 100 ms in the past and interpolated between them so it glides.
/// </summary>
public sealed partial class AnimalView : Node3D
{
    private const double Delay = .12;
    private readonly List<(double Time, Animal State)> _states = new();
    private readonly Node3D _model;
    private readonly AnimationPlayer? _anim;
    private string _playing = "";

    public AnimalDef Def { get; }
    public int Id { get; }
    /// <summary>Where it is drawn now (world, not the floating origin) and which way it faces.</summary>
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Z { get; private set; }
    public float Yaw { get; private set; }
    public AnimalState State { get; private set; }
    /// <summary>Seconds since the host last mentioned it.</summary>
    public double Unseen { get; set; }

    public AnimalView(Animal a)
    {
        Def = a.Def; Id = a.Id;
        Name = $"Animal{a.Id}";
        _model = Model(a.Def, out _anim);
        AddChild(_model);
        X = a.X; Y = a.Y; Z = a.Z; Yaw = (float)a.Yaw; State = a.State;
    }

    /// <summary>The kind's model scaled to its height (its feet at the origin, facing +Z), and its animation player.</summary>
    public static Node3D Model(AnimalDef def, out AnimationPlayer? anim)
    {
        var holder = new Node3D { Name = "Model" };
        var model = GameplayModels.Instance(def.Model);
        anim = null;
        if (model == null)
        {
            // (a missing model: a brown capsule of the right size)
            holder.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = (float)def.Radius, Height = (float)def.Length }, Rotation = new Vector3(Mathf.Pi / 2, 0, 0),
                Position = new Vector3(0, (float)def.Height * .5f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("6b4a2e") } });
            return holder;
        }
        holder.AddChild(model);
        // The height of the model's meshes in its bind pose sets the scale.
        var box = new Aabb();
        bool first = true;
        foreach (var child in model.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mi && mi.Mesh != null)
            {
                var t = Transform3D.Identity;
                for (Node? n = mi; n != null && n != model; n = n.GetParent()) if (n is Node3D n3) t = n3.Transform * t;
                var b = t * mi.Mesh.GetAabb();
                box = first ? b : box.Merge(b);
                first = false;
            }
        float scale = box.Size.Y > 1e-3f ? (float)def.Height / box.Size.Y : 1;
        model.Scale = Vector3.One * scale;
        model.Position = new Vector3(0, -box.Position.Y * scale, 0);
        anim = model.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (anim != null)
            foreach (var name in anim.GetAnimationList())
                if (anim.GetAnimation(name) is { } a)
                    a.LoopMode = Loops(name) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        return holder;
    }

    private static bool Loops(string name) => !(name.Contains("Death") || name.Contains("Attack") || name.Contains("HitReact") || name.Contains("Jump"));

    /// <summary>The animation for a state, by the names the pack uses (the first that exists).</summary>
    private static readonly Dictionary<AnimalState, string[]> Clips = new()
    {
        [AnimalState.Idle] = new[] { "Idle", "Idle_2" },
        [AnimalState.Graze] = new[] { "Eating", "Idle_Headlow", "Idle_2_HeadLow" },
        [AnimalState.Walk] = new[] { "Walk" },
        [AnimalState.Alert] = new[] { "Idle_2", "Idle" },
        [AnimalState.Flee] = new[] { "Gallop" },
        [AnimalState.Chase] = new[] { "Gallop" },
        [AnimalState.Attack] = new[] { "Attack", "Attack_Headbutt", "Attack_Kick" },
        [AnimalState.Hurt] = new[] { "Idle_HitReact1", "Idle_HitReact2" },
        [AnimalState.Dead] = new[] { "Death" },
    };

    private static string? Find(AnimationPlayer anim, AnimalState state)
    {
        var all = anim.GetAnimationList();
        foreach (var want in Clips[state])
            foreach (var name in all)
                if (name == want || name.EndsWith("/" + want) || name.EndsWith("|" + want)) return name;
        return null;
    }

    /// <summary>A report from the host, received at <paramref name="now"/> (seconds).</summary>
    public void Push(Animal a, double now)
    {
        _states.Add((now, a));
        if (_states.Count > 12) _states.RemoveAt(0);
        Unseen = 0;
    }

    /// <summary>Per frame: place (100 ms in the past, interpolated) and animate.</summary>
    public void Step(double now, float dt, double originX, double originZ)
    {
        if (_states.Count > 0)
        {
            double t = now - Delay;
            int after = _states.FindIndex(e => e.Time >= t);
            Animal s;
            if (after > 0)
            {
                var (t0, a) = _states[after - 1];
                var (t1, b) = _states[after];
                double k = t1 > t0 ? Math.Clamp((t - t0) / (t1 - t0), 0, 1) : 1;
                X = a.X + (b.X - a.X) * k; Y = a.Y + (b.Y - a.Y) * k; Z = a.Z + (b.Z - a.Z) * k;
                Yaw = Mathf.LerpAngle((float)a.Yaw, (float)b.Yaw, (float)k);
                s = b;
            }
            else
            {
                s = after == 0 ? _states[0].State : _states[^1].State;
                // (late reports: carry on along its heading for a moment)
                double ahead = after < 0 ? Math.Min(t - _states[^1].Time, .25) : 0;
                X = s.X + Math.Sin(s.Yaw) * s.Speed * ahead; Y = s.Y; Z = s.Z + Math.Cos(s.Yaw) * s.Speed * ahead;
                Yaw = (float)s.Yaw;
            }
            State = s.State;
            Play(s);
        }
        Position = new Vector3((float)(X - originX), (float)Y, (float)(Z - originZ));
        Rotation = new Vector3(0, Yaw, 0);
    }

    private void Play(Animal s)
    {
        if (_anim == null) return;
        string? clip = Find(_anim, s.State) ?? Find(_anim, AnimalState.Idle);
        if (clip == null) return;
        if (clip != _playing)
        {
            _anim.Play(clip, .25);
            _playing = clip;
        }
        // Legs keep pace with the ground: the walk and the gallop sped up or slowed to the speed.
        _anim.SpeedScale = s.State switch
        {
            AnimalState.Walk => (float)Math.Clamp(s.Speed / Math.Max(.1, Def.Walk), .5, 1.6),
            AnimalState.Flee or AnimalState.Chase => (float)Math.Clamp(s.Speed / Math.Max(.1, Def.Run) * 1.1, .6, 1.4),
            _ => 1,
        };
    }

    /// <summary>A glow laid over the model (the scan, D134), or null for none.</summary>
    public void SetGlow(Material? glow)
    {
        foreach (var child in _model.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mi) mi.MaterialOverlay = glow;
    }

    /// <summary>A fallen animal (a carcass): the death animation's last frame.</summary>
    public static Node3D Carcass(AnimalDef def)
    {
        var model = Model(def, out var anim);
        if (anim != null && Find(anim, AnimalState.Dead) is { } death)
            anim.Ready += () =>
            {
                // (once in the scene: the player only poses a skeleton it can reach)
                anim.Play(death);
                anim.Seek(anim.GetAnimation(death).Length, true);
                anim.Pause();
            };
        return model;
    }

    /// <summary>Does the segment a → b (world, floating origin) pass through the body (a capsule along its heading)?</summary>
    public bool Hit(Vector3 a, Vector3 b, out Vector3 at)
    {
        var centre = Position + Vector3.Up * (float)(Def.Height * .5);
        var axis = new Vector3(Mathf.Sin(Yaw), 0, Mathf.Cos(Yaw)) * (float)(Def.Length * .5);
        var p0 = centre - axis; var p1 = centre + axis;
        float r = (float)Def.Radius + .12f;
        // The closest points between the segment and the body's axis.
        var pts = Geometry3D.GetClosestPointsBetweenSegments(a, b, p0, p1);
        at = pts[0];
        float best = pts[0].DistanceTo(pts[1]);
        return best < r && State != AnimalState.Dead;
    }
}
