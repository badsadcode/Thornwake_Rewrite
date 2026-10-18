using System;
using System.Collections.Generic;
using System.Linq;
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
    private float _walkPhase, _lean;
    private bool _bodyHidden;

    public Character(Color tunic, Wind wind)
    {
        Name = "Character";
        _tunic = tunic;
        Body = Person(tunic);
        AddChild(Body);
        Torch = new Torch();
        Torch.Follow(wind);
        Part("RightArm").AddChild(Torch);
        TorchInHand();
        Torch.Lit = false;
    }

    /// <summary>
    /// Visuals.Person with a waist and a neck (Tom, build 74: crouching should bend the torso, not the legs, the head kept
    /// almost straight): everything from the belt up hangs from a pivot at the hips ("Upper"), the head from one at the
    /// neck ("Neck"). The model itself is unchanged (the original game uses it).
    /// </summary>
    private static Node3D Person(Color tunic)
    {
        var body = Visuals.Person(tunic);
        var upper = new Node3D { Name = "Upper", Position = new Vector3(0, WaistY, 0) };
        var neck = new Node3D { Name = "Neck", Position = new Vector3(0, NeckY - WaistY, 0) };
        upper.AddChild(neck);
        foreach (var part in body.GetChildren().OfType<Node3D>().ToList())
        {
            if (part.Name.ToString().EndsWith("Leg") || part.Position.Y < WaistY + .03f) continue; // (legs and the tunic's hem stay)
            bool head = part.Position.Y >= NeckY - .01f;
            body.RemoveChild(part);
            part.Position -= new Vector3(0, head ? NeckY : WaistY, 0);
            (head ? neck : upper).AddChild(part);
        }
        body.AddChild(upper);
        return body;
    }

    private const float WaistY = .9f, NeckY = 1.43f;

    /// <summary>A limb or another named part of the figure, wherever it hangs.</summary>
    private Node3D? Part(string name) => Body.FindChild(name, true, false) as Node3D;

    /// <summary>Dress in another colour (a joining player learns their id, and with it their colour, from the host).</summary>
    public void SetTunic(Color tunic)
    {
        if (tunic == _tunic) return;
        _tunic = tunic;
        bool axe = AxeHeld, rod = RodHeld;
        string? weapon = Weapon;
        AxeHeld = false; RodHeld = false; Weapon = null; // (they hang on the old body)
        float facing = Facing;
        bool hidden = _bodyHidden;
        var hold = hidden ? Torch.GetParent<Node3D>() : null;
        if (hidden) SetFirstPerson(null);
        Torch.Reparent(this, false);
        Body.QueueFree();
        Body = Person(tunic);
        AddChild(Body);
        Torch.Reparent(Part("RightArm"), false);
        TorchInHand();
        Facing = facing;
        if (hold != null) SetFirstPerson(hold);
        AxeHeld = axe;
        RodHeld = rod;
        Weapon = weapon;
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
        AnimateWeapon(dt, swing);
        // Sneaking (Tom, build 74): bent forward at the waist, the head kept up, a little lower on legs set one before the
        // other (the legs are rigid: angled, both feet still reach the ground). Build 68–74 swung both legs forward.
        float ease = 1 - Mathf.Exp(-10 * dt);
        _lean = Mathf.Lerp(_lean, Crouching ? .55f : 0, ease);
        Body.Position = Body.Position with { Y = Mathf.Lerp(Body.Position.Y, Crouching ? -.1f : 0, ease) };
        if (Part("Upper") is { } upper) upper.Rotation = new Vector3(_lean, 0, 0);
        if (Part("Neck") is { } neck) neck.Rotation = new Vector3(-_lean * .85f, 0, 0);
        if (Crouching) { Limb("LeftLeg", swing * .4f - .45f); Limb("RightLeg", -swing * .4f + .45f); }
    }

    /// <summary>The hand the bow is held in (setting Interface → Bow hand; Tom: the right).</summary>
    private static string BowHand => GameSettings.BowHand.Index == 0 ? AxeHand : TorchHand;

    // ---- weapons (D120): a spear or a sword in the right hand, a bow in the left (the torch goes out)
    private Node3D? _weapon, _nocked;
    private string? _weaponId;
    private float _attackPhase = -1;
    private const float SpearSeconds = .55f, SwordSeconds = .5f;

    /// <summary>The weapon in hand: "spear", "stone_sword", "bow" (item ids), or null.</summary>
    public string? Weapon
    {
        get => _weaponId;
        set
        {
            if (value == _weaponId) return;
            _weapon?.QueueFree(); _weapon = null; _nocked = null;
            _weaponId = value;
            _attackPhase = -1;
            Draw = 0;
            Torch.Visible = value != "bow";
            if (value == null) return;
            if (AxeHeld) AxeHeld = false;
            if (RodHeld) RodHeld = false;
            if (value == "bow") Torch.Lit = false;
            _weapon = GameplayModels.Weapon(value);
            var hold = new Node3D { Name = "Weapon", Position = new Vector3(0, -.49f, 0) };
            hold.AddChild(_weapon);
            Part(value == "bow" ? BowHand : AxeHand).AddChild(hold);
            _weapon = hold;
            if (value == "bow")
            {
                _nocked = GameplayModels.Weapon("arrow");
                _nocked.Position = new Vector3(0, .12f, .3f);
                _nocked.Visible = false;
                hold.AddChild(_nocked);
            }
        }
    }

    /// <summary>Crouched (sneaking): the body low, the knees bent.</summary>
    public bool Crouching { get; set; }

    /// <summary>The bow drawn: 0 (resting) … 1 (full draw); an arrow shows on the string while above 0.</summary>
    public float Draw { get; set; }
    /// <summary>A swing or thrust in progress (others see it: a flag in the player's state).</summary>
    public bool Attacking => _attackPhase >= 0 || Draw > 0;
    /// <summary>The moment a swing or thrust lands (once per attack).</summary>
    public event Action? AttackHit;

    /// <summary>Start a swing (sword) or a thrust (spear); ignored while one is going.</summary>
    public void Strike() { if (_weaponId is "spear" or "stone_sword" && _attackPhase < 0) _attackPhase = 0; }

    private void AnimateWeapon(float dt, float swing)
    {
        if (_weaponId == null || _weapon == null) return;
        if (_weaponId == "bow")
        {
            // The bow arm held out toward the target, the string hand drawn back to the chin.
            float d = Mathf.Clamp(Draw, 0, 1);
            Limb(BowHand, d > 0 ? -1.5f : swing * .8f);
            if (d > 0) Limb(BowHand == AxeHand ? TorchHand : AxeHand, -1.35f - .15f * d);
            if (_nocked != null) { _nocked.Visible = d > 0; _nocked.Position = new Vector3(0, .12f, .3f - .35f * d); }
            return;
        }
        if (_attackPhase < 0) { Limb(AxeHand, -swing * .8f - .25f); _weapon.Position = new Vector3(0, -.49f, 0); return; }
        float before = _attackPhase;
        _attackPhase += dt / (_weaponId == "spear" ? SpearSeconds : SwordSeconds);
        float k = Mathf.Min(_attackPhase, 1);
        if (_weaponId == "spear")
        {
            // Drawn back at shoulder height, then a straight thrust (the spear slides forward in the hand), then back.
            if (before < .45f && k >= .45f) AttackHit?.Invoke();
            float arm = k < .3f ? Mathf.Lerp(-.3f, -1.45f, k / .3f) : -1.45f;
            float reach = k < .3f ? Mathf.Lerp(0, -.35f, k / .3f) : k < .45f ? Mathf.Lerp(-.35f, .55f, (k - .3f) / .15f) : Mathf.Lerp(.55f, 0, (k - .45f) / .55f);
            if (k > .75f) arm = Mathf.Lerp(-1.45f, -.3f, (k - .75f) / .25f);
            Limb(AxeHand, arm);
            _weapon.Position = new Vector3(0, -.49f, reach);
        }
        else
        {
            // Raised over the shoulder, a fast cut down and across.
            if (before < .5f && k >= .5f) AttackHit?.Invoke();
            float angle = k < .35f ? Mathf.Lerp(-.3f, -2.5f, Mathf.SmoothStep(0, 1, k / .35f))
                : k < .55f ? Mathf.Lerp(-2.5f, -.2f, (k - .35f) / .2f) : Mathf.Lerp(-.2f, -.3f, (k - .55f) / .45f);
            Limb(AxeHand, angle);
        }
        if (_attackPhase >= 1) _attackPhase = -1;
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
    private const int RodSegments = 5;
    private Node3D? _rod;
    private readonly List<Node3D> _rodJoints = new();

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
                Weapon = null;
                // A chain of joints (thick at the hand, thin at the tip) so it can bend toward the fish.
                _rod = new Node3D { Name = "Rod", Rotation = new Vector3(1.15f, 0, 0), Position = new Vector3(0, -.5f, .02f) };
                var wood = new StandardMaterial3D { AlbedoColor = new Color("7a5a36"), Roughness = .8f };
                Node3D parent = _rod;
                _rodJoints.Clear();
                float seg = RodLength / RodSegments;
                for (int i = 0; i < RodSegments; i++)
                {
                    var joint = new Node3D { Position = i == 0 ? Vector3.Zero : new Vector3(0, seg, 0) };
                    float r0 = .018f - .01f * i / RodSegments, r1 = .018f - .01f * (i + 1) / RodSegments;
                    joint.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r1, BottomRadius = r0, Height = seg, RadialSegments = 5 }, MaterialOverride = wood, Position = new Vector3(0, seg / 2, 0) });
                    parent.AddChild(joint);
                    _rodJoints.Add(joint);
                    parent = joint;
                }
                Part(AxeHand).AddChild(_rod);
            }
            else { _rod!.QueueFree(); _rod = null; _rodJoints.Clear(); Fishing = false; }
        }
    }

    /// <summary>The rod held out (fishing: the arm raised).</summary>
    public bool Fishing { get; set; }

    /// <summary>The rod's tip in the world (where the line starts), or null without a rod.</summary>
    public Vector3? RodTip => _rodJoints.Count > 0 && _rodJoints[^1].IsInsideTree() ? _rodJoints[^1].GlobalTransform * new Vector3(0, RodLength / RodSegments, 0) : null;

    /// <summary>Bend the rod (fishing): <paramref name="down"/> 0 … 1 toward the water with the line's tension,
    /// <paramref name="side"/> −1 … 1 toward where the fish runs. The tip bends most.</summary>
    public void BendRod(float down, float side)
    {
        for (int i = 0; i < _rodJoints.Count; i++)
        {
            float k = (i + 1f) / _rodJoints.Count; // (more toward the tip)
            _rodJoints[i].Rotation = new Vector3(.22f * down * k, 0, -.16f * side * k);
        }
    }

    public bool AxeHeld
    {
        get => _axe != null;
        set
        {
            if (value == (_axe != null)) return;
            var right = Part(AxeHand);
            if (value) { if (RodHeld) RodHeld = false; Weapon = null; _axe = Thornwake.World.Visuals.Tool(0); right.AddChild(_axe); }
            else { _axe.QueueFree(); _axe = null; Chopping = false; }
        }
    }

    private void Limb(string name, float angle)
    {
        if (Part(name) is { } limb) limb.Rotation = new Vector3(angle, limb.Rotation.Y, limb.Rotation.Z);
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
        else { Torch.Reparent(Part("RightArm"), false); TorchInHand(); }
    }

    /// <summary>In the right fist, the stick up and a little forward while the arm is raised; tilted out, away from the head.</summary>
    private void TorchInHand() { Torch.Position = new Vector3(0, -.51f, .02f); Torch.Rotation = new Vector3(.85f, 0, -.3f); }
}
