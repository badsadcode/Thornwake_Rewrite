using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;

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
    private bool _huntLmbWas, _strikeHooked;
    private float _draw, _shoulder;
    private Label? _crosshair;

    private sealed class Flight
    {
        public required Node3D Node;
        public double X, Y, Z;          // world
        public Vector3 Velocity;
        public bool Mine, Stuck;
        public float Draw;
        public double Age;
    }

    private readonly List<Flight> _arrows = new();
    private static double Now => Time.GetTicksMsec() / 1000.0;

    private void InitHunting(CanvasLayer layer)
    {
        _crosshair = new Label { Text = "+", AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f, OffsetLeft = -8, OffsetTop = -14,
            Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _crosshair.AddThemeFontSizeOverride("font_size", 22);
        _crosshair.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        layer.AddChild(_crosshair);
    }

    private void HuntingWorldReady()
    {
        foreach (var v in _animalViews.Values) v.QueueFree();
        _animalViews.Clear();
        foreach (var f in _arrows) f.Node.QueueFree();
        _arrows.Clear();
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
        var dir = (target - start).Normalized();
        float speed = (float)bow.Prop("speed", 50) * (.45f + .55f * draw);
        Shoot(start, dir * speed, true, draw);
        GameClient?.SendShot(start.X + _originX, start.Y, start.Z + _originZ, dir.X * speed, dir.Y * speed, dir.Z * speed);
    }

    private void Shoot(Vector3 start, Vector3 velocity, bool mine, float draw)
    {
        var node = new Node3D { Name = "Arrow" };
        node.AddChild(GameplayModels.Weapon("arrow"));
        AddChild(node);
        _arrows.Add(new Flight { Node = node, X = start.X + _originX, Y = start.Y, Z = start.Z + _originZ, Velocity = velocity, Mine = mine, Draw = draw });
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
            var a = new Vector3((float)(f.X - _originX), (float)f.Y, (float)(f.Z - _originZ));
            f.Velocity += Vector3.Down * 9.8f * dt;
            f.Velocity *= 1 - .015f * dt;
            var b = a + f.Velocity * dt;
            bool done = false;
            if (f.Mine)
                foreach (var v in _animalViews.Values)
                    if (v.Hit(a, b, out var at))
                    {
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
                f.Stuck = true; f.Age = 0;
                f.Node.Position = p - f.Velocity.Normalized() * .25f;
                if (f.Mine) Act(new ActionRequest(ActionKind.Shoot, 0, 0, "", f.X, f.Y, f.Z, f.Draw));
                continue;
            }
            f.X = b.X + _originX; f.Y = b.Y; f.Z = b.Z + _originZ;
            f.Node.Position = b;
            if (f.Velocity.LengthSquared() > 1e-4f) f.Node.Basis = Basis.LookingAt(-f.Velocity.Normalized(), Mathf.Abs(f.Velocity.Normalized().Y) > .99f ? Vector3.Forward : Vector3.Up);
            if (f.Age > 8)
            {
                if (f.Mine) Act(new ActionRequest(ActionKind.Shoot, 0, 0, "", f.X, f.Y, f.Z, f.Draw));
                f.Node.QueueFree(); _arrows.RemoveAt(i);
            }
        }
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
            view.Push(a, now);
        }
        foreach (var id in _animalViews.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _animalViews[id].QueueFree();
            _animalViews.Remove(id);
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

    private string HuntingHud() => _me.Weapon switch
    {
        "bow" => $"Holding: bow ({_myInv.CountTag("arrow")} arrows) — hold LMB to draw, release to shoot · 9 puts it away",
        "spear" => "Holding: hunting spear — LMB thrusts at what is in front of you · 8 puts it away",
        "stone_sword" => "Holding: stone-edged sword — LMB cuts at what is in front of you · 0 puts it away",
        _ => "",
    };
}
