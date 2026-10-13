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
    /// <summary>The figure (Visuals.Person): faces +Z.</summary>
    public Node3D Body { get; }
    public Torch Torch { get; }
    private float _walkPhase;
    private bool _bodyHidden;

    public Character(Color tunic, Wind wind)
    {
        Name = "Character";
        Body = Visuals.Person(tunic);
        AddChild(Body);
        Torch = new Torch();
        Torch.Follow(wind);
        Body.GetNode<Node3D>("RightArm").AddChild(Torch);
        TorchInHand();
        Torch.Lit = false;
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
        Limb("LeftLeg", swing); Limb("RightLeg", -swing); Limb("LeftArm", -swing * .8f); Limb("RightArm", Torch.Lit ? -.6f + swing * .12f : swing * .8f);
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
