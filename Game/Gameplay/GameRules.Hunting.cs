using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>
/// Hunting (D120): the animals (<see cref="Gameplay.Wildlife"/>), weapons swung at them or arrows shot, carcasses and
/// butchering. A player's PC says what it hit (it draws the swing and the arrow's flight); the host checks the weapon,
/// the reach or range and how often, and has the last word on damage, deaths and what goes into the pack.
/// </summary>
public sealed partial class GameRules
{
    /// <summary>Farthest an arrow is believed to hit (m).</summary>
    public const double ShootRange = 90;
    private Wildlife? _wildlife;
    private readonly List<Hurt> _hurts = new();
    private readonly Dictionary<int, double> _lastAttack = new(), _lastShot = new();
    private readonly Dictionary<int, int> _arrowsIn = new();
    private readonly Random _huntRng = new(7);

    /// <summary>The animals around the players (the host's true ones).</summary>
    public Wildlife Wildlife => _wildlife ??= new Wildlife(Catalog.Animals.Values.ToList(), _world) { PieceBlocked = PieceBlocks };

    /// <summary>A finished building piece fills a point (an animal's body there: radius, at its back's height) — walls,
    /// stairs, low decks; a deck high on stilts can be walked under.</summary>
    private bool PieceBlocks(double x, double y, double z, double radius) =>
        Objects.Near(x, z, 6).Any(o => !o.Blueprint && Catalog.Piece(o.Kind) is { } p
            && Building.Inside(p, new Pose(o.X, o.Y, o.Z, o.Yaw), new V3(x, y + .5, z), radius));
    /// <summary>Real seconds the rules have run (cooldowns: game time runs faster than real time).</summary>
    public double Seconds { get; private set; }

    /// <summary>Bites and kicks since the last call (the host tells each hurt player).</summary>
    public List<Hurt> TakeHurts() { var l = _hurts.ToList(); _hurts.Clear(); return l; }

    /// <summary>Per frame on the host: the animals think and move around <paramref name="hunters"/>; the fallen become carcasses.</summary>
    public void StepWildlife(double dt, IReadOnlyList<Hunter> hunters)
    {
        Seconds += dt;
        Wildlife.Step(dt, hunters);
        _hurts.AddRange(Wildlife.Bites); Wildlife.Bites.Clear();
        foreach (var a in Wildlife.Fallen) Carcass(a);
        Wildlife.Fallen.Clear();
    }

    private void Carcass(Animal a)
    {
        var c = Objects.Add("carcass", a.X, a.Y, a.Z, (float)a.Yaw, 0);
        c.Ref = a.Def.Id;
        c.Size = (float)a.Def.Height;
        foreach (var y in a.Def.Yield) c.Contents.Add((y.Item!, y.Count));
        if (_arrowsIn.Remove(a.Id, out int arrows) && arrows > 0) c.Contents.Add(("arrow", arrows));
    }

    /// <summary>
    /// A swing of the held weapon (or the axe) at an animal: the item in the pack, its reach (with a little slack for the
    /// network's delay) and its pace; the damage is the weapon's (props <c>damage</c>, <c>reach</c>, <c>cooldown</c>).
    /// </summary>
    public ActionResult Attack(int player, int animal, string item, double px, double pz)
    {
        var inv = Inventory(player);
        if (!Catalog.Items.TryGetValue(item, out var w) || inv.Count(item) == 0 || !(w.Has("weapon") || w.Has("chopping")))
            return ActionResult.Fail("You have nothing to strike with.");
        double cooldown = w.Prop("cooldown", 1);
        if (_lastAttack.TryGetValue(player, out double last) && Seconds - last < cooldown * .7) return ActionResult.Fail("");
        if (Wildlife.Get(animal) is not { State: not AnimalState.Dead } a) return ActionResult.Fail("");
        if (Dist(px, pz, a.X, a.Z) > w.Prop("reach", 2) + a.Def.Radius + 1.2) return ActionResult.Fail("Out of reach.");
        _lastAttack[player] = Seconds;
        bool killed = Wildlife.Damage(animal, w.Prop("damage", 10), player, px, pz);
        return new(true, killed ? $"The {a.Def.Name.ToLowerInvariant()} falls." : $"You hit the {a.Def.Name.ToLowerInvariant()}.");
    }

    /// <summary>
    /// An arrow shot with the bow, drawn <paramref name="draw"/> (0 … 1), as the shooter's PC flew it: it struck
    /// <paramref name="animal"/> (0: none) or came down at (x, y, z). One arrow from the pack either way; one that misses
    /// may be found again where it fell, one that hits may be in the carcass.
    /// </summary>
    public ActionResult Shoot(int player, int animal, double draw, double x, double y, double z, double px, double pz)
    {
        var inv = Inventory(player);
        var bow = inv.Stacks.Select(s => Catalog.Item(s.Item)).FirstOrDefault(d => d.Has("bow"));
        if (bow == null) return ActionResult.Fail("You need a bow.");
        if (!inv.HasTag("arrow")) return ActionResult.Fail("No arrows left.");
        if (_lastShot.TryGetValue(player, out double last) && Seconds - last < .45) return ActionResult.Fail("");
        _lastShot[player] = Seconds;
        inv.Take(new Ingredient(null, "arrow", 1));
        _inventoryChanged.Add(player);
        if (animal > 0 && Wildlife.Get(animal) is { State: not AnimalState.Dead } a && Dist(px, pz, a.X, a.Z) <= ShootRange)
        {
            double damage = bow.Prop("damage", 30) * (.35 + .65 * Math.Clamp(draw, 0, 1));
            if (_huntRng.NextDouble() < .75) _arrowsIn[animal] = _arrowsIn.GetValueOrDefault(animal) + 1;
            bool killed = Wildlife.Damage(animal, damage, player, px, pz);
            return new(true, killed ? $"The {a.Def.Name.ToLowerInvariant()} falls." : $"A hit! The {a.Def.Name.ToLowerInvariant()} is wounded.");
        }
        if (Dist(px, pz, x, z) <= ShootRange + 20 && double.IsFinite(x + y + z) && _huntRng.NextDouble() < .85)
        {
            var stuck = Objects.Add("arrow", x, y, z, (float)Math.Atan2(x - px, z - pz), 0);
            stuck.Contents.Add(("arrow", 1));
        }
        return new(true, "");
    }

    /// <summary>Butcher a carcass within reach with a cutting tool (knife, axe, sword): what it gives goes into the pack.</summary>
    public ActionResult Butcher(int player, long objectId, double px, double pz)
    {
        var inv = Inventory(player);
        if (!inv.HasTag("cutting")) return ActionResult.Fail("You need something sharp to butcher with (a knife, an axe).");
        if (Objects.Get(objectId) is not { Kind: "carcass" } c) return ActionResult.Fail("Nothing to butcher there.");
        if (Dist(px, pz, c.X, c.Z) > UseReach + 1.5) return ActionResult.Fail("Too far away.");
        foreach (var (item, count) in c.Contents) inv.Add(item, count);
        string what = string.Join(", ", c.Contents.Select(x => $"{x.Count} × {Catalog.Item(x.Item).Name.ToLowerInvariant()}"));
        string name = Catalog.Animals.TryGetValue(c.Ref, out var def) ? def.Name.ToLowerInvariant() : "animal";
        Objects.Remove(c.Id);
        _inventoryChanged.Add(player);
        return new(true, $"You butcher the {name}: {what}.");
    }

    /// <summary>How long butchering takes (s): the animal's time, quicker with a proper knife (tag <c>butchering</c>).</summary>
    public static double ButcherSeconds(Catalog catalog, Inventory inv, string animal) =>
        (catalog.Animals.TryGetValue(animal, out var d) ? d.ButcherSeconds : 5) * (inv.HasTag("butchering") ? .6 : 1);
}
