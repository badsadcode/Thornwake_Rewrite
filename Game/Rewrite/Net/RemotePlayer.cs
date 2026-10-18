using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// Another player as this PC draws them (multiplayer spike): their Character with their name above, moved by the
/// positions the host relays (~20 a second). Drawn 100 ms in the past and interpolated between the two positions around
/// that moment, so they glide instead of stepping; when a position is late, carried on along their velocity for up to
/// a quarter of a second.
/// </summary>
public sealed partial class RemotePlayer : Node3D
{
    private const double Delay = .1, Extrapolate = .25;
    private readonly Character _character;
    private readonly Label3D _label;
    private readonly List<(double Time, PlayerState State)> _states = new();
    private bool _wasAttacking;

    public int Id { get; }
    public Color Tunic { get; }
    /// <summary>The newest position from the host, or null before the first.</summary>
    public PlayerState? Latest => _states.Count > 0 ? _states[^1].State : null;

    public RemotePlayer(int id, string name, Wind wind)
    {
        Name = $"Player{id}";
        Id = id;
        Tunic = Character.TunicFor(id);
        _character = new Character(Tunic, wind);
        AddChild(_character);
        _label = new Label3D
        {
            Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = Vector3.Up * 2.2f, FontSize = 40,
            PixelSize = .006f, OutlineSize = 10, Modulate = new Color(1, 1, 1, .9f), FixedSize = false,
        };
        AddChild(_label);
        Visible = false;
    }

    public string PlayerName { get => _label.Text; set => _label.Text = value; }

    /// <summary>A new position from the host, received at <paramref name="now"/> (seconds).</summary>
    public void Push(in PlayerState state, double now)
    {
        _states.Add((now, state));
        if (_states.Count > 40) _states.RemoveAt(0);
    }

    /// <summary>Per frame: place and animate at <paramref name="now"/> − 100 ms, in the scene's floating origin. Returns
    /// true when the newest position was already older than that (drawn guessed ahead: a position came late).</summary>
    public bool Step(double now, float dt, double originX, double originZ)
    {
        if (_states.Count == 0) return false;
        bool guessed = false;
        double t = now - Delay;
        PlayerState s;
        int after = _states.FindIndex(e => e.Time >= t);
        if (after > 0)
        {
            var (t0, a) = _states[after - 1];
            var (t1, b) = _states[after];
            double k = t1 > t0 ? (t - t0) / (t1 - t0) : 1;
            s = new PlayerState(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Z + (b.Z - a.Z) * k,
                Mathf.LerpAngle(a.Facing, b.Facing, (float)k), b.Vx, b.Vy, b.Vz, b.Flags, b.Held);
        }
        else if (after == 0) s = _states[0].State;
        else
        {
            var (tl, last) = _states[^1];
            guessed = true;
            double ahead = System.Math.Min(t - tl, Extrapolate);
            s = last with { X = last.X + last.Vx * ahead, Y = last.Y + last.Vy * ahead, Z = last.Z + last.Vz * ahead };
        }
        Visible = true;
        Position = new Vector3((float)(s.X - originX), (float)s.Y, (float)(s.Z - originZ));
        _character.Facing = s.Facing;
        _character.Torch.Lit = (s.Flags & PlayerFlags.Torch) != 0;
        _character.AxeHeld = (s.Flags & PlayerFlags.Axe) != 0;
        _character.Chopping = (s.Flags & PlayerFlags.Chopping) != 0;
        _character.RodHeld = (s.Flags & PlayerFlags.Rod) != 0;
        _character.Fishing = (s.Flags & PlayerFlags.Fishing) != 0;
        // The weapon in hand (D120) and its swing or draw.
        _character.Weapon = (s.Held & 0x7f) switch { 1 => "spear", 2 => "stone_sword", 3 => "bow", _ => null };
        _character.Crouching = (s.Held & 0x80) != 0;
        bool attacking = (s.Flags & PlayerFlags.Attacking) != 0;
        if ((s.Held & 0x7f) == 3) _character.Draw = attacking ? 1 : 0;
        else if (attacking && !_wasAttacking) _character.Strike();
        _wasAttacking = attacking;
        _character.Animate(dt, new Vector3(s.Vx, s.Vy, s.Vz), (s.Flags & PlayerFlags.Flying) != 0, holdFacing: true);
        return guessed;
    }
}
