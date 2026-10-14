using System;
using Godot;
using Thornwake.World;

namespace Thornwake.Rewrite;

/// <summary>
/// A person in the world as others see them (multiplayer spike, GAMEPLAY_DESIGN.md §14.0): the body, its walk and the
/// torch. The same for the local player (driven by WorldLab's movement) and for remote players (driven by the network),
/// so there can be several. It holds no input, physics or camera — those stay with whoever moves it.
/// </summary>
public sealed partial class Character : Node3D
{
    /// <summary>Tunic colours by player: the host (or a single player) wears Alden's green; others the rest, by id.</summary>
    private static readonly Color[] Tunics = { new("526b61"), new("7a4a3a"), new("3f5a7a"), new("6b6a3a"), new("5a3f6b"), new("3a6b5a"), new("7a6a4a") };

    /// <summary>The same colour for a player on every PC (multiplayer: chosen from the id the host gave them).</summary>
    public static Color TunicFor(long id) => id <= 1 ? Tunics[0] : Tunics[1 + (int)((id & 0x7fffffff) % (Tunics.Length - 1))];

    /// <summary>The figure (Visuals.Person): faces +Z.</summary>
    public Node3D Body { get; private set; }
    private Color _tunic;
    public Torch Torch { get; }
    private float _walkPhase;
    private bool _bodyHidden;

    public Character(Color tunic, Wind wind)
    {
        Name = "Character";
        _tunic = tunic;
        Body = Visuals.Person(tunic);
        AddChild(Body);
        Torch = new Torch();
        Torch.Follow(wind);
        Body.GetNode<Node3D>("RightArm").AddChild(Torch);
        TorchInHand();
        Torch.Lit = false;
    }

    /// <summary>Dress in another colour (a joining player learns their id, and with it their colour, from the host).</summary>
    public void SetTunic(Color tunic)
    {
        if (tunic == _tunic) return;
        _tunic = tunic;
        bool axe = AxeHeld, rod = RodHeld;
        AxeHeld = false; RodHeld = false; // (they hang on the old body)
        float facing = Facing;
        bool hidden = _bodyHidden;
        var hold = hidden ? Torch.GetParent<Node3D>() : null;
        if (hidden) SetFirstPerson(null);
        Torch.Reparent(this, false);
        Body.QueueFree();
        Body = Visuals.Person(tunic);
        AddChild(Body);
        Torch.Reparent(Body.GetNode<Node3D>("RightArm"), false);
        TorchInHand();
        Facing = facing;
        if (hold != null) SetFirstPerson(hold);
        AxeHeld = axe;
        RodHeld = rod;
    }

    /// <summary>Facing (radians about +Y, 0 = +Z) — the direction the body looks.</summary>
    public float Facing
    {
        get => Body.Rotation.Y;
        set => Body.Rotation = new Vector3(0, value, 0);
    }

    /// <summary>
    /// One frame of the walk: turn toward the direction of travel (unless <paramref name="holdFacing"/>: first person,
    /// where the body faces the view) and swing the limbs with the horizontal speed; with the torch the right arm is
    /// held up and forward, swinging only a little.
    /// </summary>
    public void Animate(float dt, Vector3 velocity, bool flying, bool holdFacing = false)
    {
        var flat = new Vector3(velocity.X, 0, velocity.Z);
        if (flat.LengthSquared() > .05f)
        {
            float target = Mathf.Atan2(flat.X, flat.Z);
            if (!holdFacing) Facing = Mathf.LerpAngle(Facing, target, 1 - Mathf.Exp(-12 * dt));
            _walkPhase += flat.Length() * dt * 1.6f;
        }
        else _walkPhase = Mathf.Lerp(_walkPhase, Mathf.Round(_walkPhase / Mathf.Pi) * Mathf.Pi, 1 - Mathf.Exp(-10 * dt));
        float swing = Mathf.Sin(_walkPhase) * (flying ? 0 : .55f);
        // (The model names its limbs from the viewer's side: "RightArm" is Alden's left. The torch is in his left hand,
        // the axe in his right — the same in first person.)
        Limb("LeftLeg", swing); Limb("RightLeg", -swing);
        Limb(AxeHand, _rod != null && Fishing ? -.7f : -swing * .8f);
        Limb(TorchHand, Torch.Lit ? -.6f + swing * .12f : swing * .8f);
        // Chopping (placeholder, D110): the axe arm raised back over the shoulder, then brought down, ~1.4 strokes a second.
        if (Chopping)
        {
            float before = _chopPhase % 1;
            _chopPhase += dt * 1.4f;
            float k = _chopPhase % 1;
            if (before < .7f && k >= .7f) ChopHit?.Invoke(); // the axe lands
            float angle = k < .6f ? Mathf.Lerp(-.3f, -2.6f, Mathf.SmoothStep(0, 1, k / .6f)) : Mathf.Lerp(-2.6f, -.3f, Mathf.SmoothStep(0, 1, (k - .6f) / .15f));
            Limb(AxeHand, angle);
        }
        else _chopPhase = 0;
    }

    /// <summary>Swinging the axe (the arm animation; each stroke's impact raises <see cref="ChopHit"/>).</summary>
    public bool Chopping { get; set; }
    /// <summary>The moment the axe lands, once per stroke (~1.4 a second).</summary>
    public event Action? ChopHit;
    private float _chopPhase;
    private Node3D? _axe;

    /// <summary>An axe in Alden's right hand (the original game's model); the torch stays in his left.</summary>
    private const string TorchHand = "RightArm", AxeHand = "LeftArm"; // (Alden's left and right hands)
    private const float RodLength = 1.8f;
    private MeshInstance3D? _rod;

    /// <summary>A fishing rod in Alden's right hand (placeholder: a long, thin, springy stick), angled forward and up.</summary>
    public bool RodHeld
    {
        get => _rod != null;
        set
        {
            if (value == (_rod != null)) return;
            if (value)
            {
                if (AxeHeld) AxeHeld = false;
                var axis = new Vector3(0, Mathf.Cos(1.15f), Mathf.Sin(1.15f));
                _rod = new MeshInstance3D
                {
                    Name = "Rod", Mesh = new CylinderMesh { TopRadius = .008f, BottomRadius = .018f, Height = RodLength, RadialSegments = 5 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("7a5a36"), Roughness = .8f },
                    Rotation = new Vector3(1.15f, 0, 0), Position = new Vector3(0, -.5f, .02f) + axis * RodLength / 2,
                };
                Body.GetNode<Node3D>(AxeHand).AddChild(_rod);
            }
            else { _rod!.QueueFree(); _rod = null; Fishing = false; }
        }
    }

    /// <summary>The rod held out (fishing: the arm raised).</summary>
    public bool Fishing { get; set; }

    /// <summary>The rod's tip in the world (where the line starts), or null without a rod.</summary>
    public Vector3? RodTip => _rod is { } r && r.IsInsideTree() ? r.GlobalTransform * new Vector3(0, RodLength / 2, 0) : null;

    public bool AxeHeld
    {
        get => _axe != null;
        set
        {
            if (value == (_axe != null)) return;
            var right = Body.GetNode<Node3D>(AxeHand);
            if (value) { if (RodHeld) RodHeld = false; _axe = Thornwake.World.Visuals.Tool(0); right.AddChild(_axe); }
            else { _axe.QueueFree(); _axe = null; Chopping = false; }
        }
    }

    private void Limb(string name, float angle)
    {
        if (Body.GetNodeOrNull<Node3D>(name) is { } limb) limb.Rotation = new Vector3(angle, limb.Rotation.Y, limb.Rotation.Z);
    }

    /// <summary>
    /// First person: the body only casts its shadow, and the torch goes to <paramref name="viewHold"/> (a node under the
    /// camera); null puts everything back (third person, the torch in the right hand).
    /// </summary>
    public void SetFirstPerson(Node3D? viewHold)
    {
        bool hide = viewHold != null;
        if (hide == _bodyHidden) return;
        _bodyHidden = hide;
        foreach (var mesh in Body.FindChildren("*", "MeshInstance3D", true, false))
            if (mesh is MeshInstance3D m && !Torch.IsAncestorOf(m))
                m.CastShadow = hide ? GeometryInstance3D.ShadowCastingSetting.ShadowsOnly : GeometryInstance3D.ShadowCastingSetting.On;
        if (viewHold != null) { Torch.Reparent(viewHold, false); Torch.Position = Vector3.Zero; Torch.Rotation = Vector3.Zero; }
        else { Torch.Reparent(Body.GetNode<Node3D>("RightArm"), false); TorchInHand(); }
    }

    /// <summary>In the right fist, the stick up and a little forward while the arm is raised; tilted out, away from the head.</summary>
    private void TorchInHand() { Torch.Position = new Vector3(0, -.51f, .02f); Torch.Rotation = new Vector3(.85f, 0, -.3f); }
}
