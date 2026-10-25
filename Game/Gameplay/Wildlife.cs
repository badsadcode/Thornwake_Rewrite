using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>
/// An animal kind (Content/animals.json; D120): its model and size, how it moves and senses, its temper (shy animals
/// flee, proud ones may turn on a hunter who hurt them, predators hunt), where it lives (biome tags) and what butchering
/// it gives.
/// </summary>
public sealed record AnimalDef(string Id, string Name, string Model, double Height, double Length, double Radius, double Health,
    double Walk, double Run, double Sight, double Hearing, string Temper, int GroupMin, int GroupMax,
    IReadOnlySet<string> Biomes, IReadOnlySet<string> Avoid, double Weight, double Damage, double AttackRange, double AttackSeconds,
    IReadOnlyList<Ingredient> Yield, double ButcherSeconds)
{
    public bool Shy => Temper == "shy";
    public bool Proud => Temper == "proud";
    public bool Predator => Temper == "predator";
}

/// <summary>What an animal is doing (the client plays the matching animation).</summary>
public enum AnimalState : byte { Idle, Graze, Walk, Alert, Flee, Chase, Attack, Hurt, Dead }

/// <summary>One animal in the world (the host's true one, or a client's copy of its last report).</summary>
public sealed class Animal
{
    public int Id { get; init; }
    public required AnimalDef Def { get; init; }
    public double X, Y, Z, Yaw, Speed, Health;
    public AnimalState State;
    /// <summary>Stands still whatever happens (tests, a debug spawn).</summary>
    public bool Tame;
    // (host only)
    internal double Timer, HomeX, HomeZ, GoalX, GoalZ, BiteTimer, DeadTimer, Courage;
    internal int Group, Target, KilledBy;
}

/// <summary>A bite or kick that hurt a player (the host tells their PC: health lives there).</summary>
public readonly record struct Hurt(int Player, double Damage, double FromX, double FromZ, string Who);

/// <summary>Where a player is and how loud they are, as the animals sense them.</summary>
public readonly record struct Hunter(int Id, double X, double Y, double Z, double Speed, bool Flying, bool Sneaking = false);

/// <summary>
/// The animals around the players (GAMEPLAY_DESIGN §2 *Creatures*, §9; D120), simulated by the host only (§14.1: the
/// host does everything first). Groups appear out of sight (90–170 m from everyone) in biomes that suit them and go when
/// nobody is near (they are not saved: the land keeps its animals, not each animal). Ten steps a second: shy animals graze
/// and wander, notice a hunter by sight and sound (a sprinting one from much farther) and flee — the whole group at once;
/// a proud stag may turn on a hunter who hurt it; wolves hunt in packs (at night, or when you come close), bite, and run
/// when badly hurt. Engine-free and tested headless.
/// </summary>
public sealed class Wildlife
{
    public const double StepSeconds = .1, SpawnMin = 70, SpawnMax = 150, KeepRadius = 280, AreaRadius = 200;
    private readonly IReadOnlyList<AnimalDef> _defs;
    private readonly IWorldQuery _world;
    private readonly Dictionary<int, Animal> _all = new();
    private readonly Random _rng;
    private double _acc, _spawnTimer;
    private int _next = 1, _nextGroup = 1;

    /// <summary>Building pieces in the way (x, y, z, radius); the rules give it (D135).</summary>
    public Func<double, double, double, double, bool>? PieceBlocked { get; set; }

    /// <summary>How many animals (sandbox: 0 none … 1 normal … 2 many).</summary>
    public double Density { get; set; } = 1;
    /// <summary>
    /// How hard it rains where the animals are (0 … 1; the host's weather, D217): in heavy rain the grazers seek the
    /// trees and stand under them, the predators mostly lie low (Noor's Weather 2.0 notes §9). 0 = as before.
    /// </summary>
    public double Storm { get; set; }
    /// <summary>Animals per player's surroundings at density 1.</summary>
    public const int PerArea = 12;

    public Wildlife(IReadOnlyList<AnimalDef> defs, IWorldQuery world, int seed = 1) { _defs = defs; _world = world; _rng = new Random(seed); }

    public IReadOnlyCollection<Animal> All => _all.Values;
    public Animal? Get(int id) => _all.GetValueOrDefault(id);
    public List<Hurt> Bites { get; } = new();
    /// <summary>Animals whose death animation has played: the rules turn each into a carcass.</summary>
    public List<Animal> Fallen { get; } = new();

    public void Clear() { _all.Clear(); Bites.Clear(); Fallen.Clear(); }

    /// <summary>An animal put down by hand (tests, a debug spawn).</summary>
    public Animal Spawn(AnimalDef def, double x, double z, int group = 0)
    {
        var a = new Animal { Id = _next++, Def = def, X = x, Z = z, Y = _world.Ground(x, z), Yaw = _rng.NextDouble() * Math.Tau, Health = def.Health,
            HomeX = x, HomeZ = z, Group = group == 0 ? _nextGroup++ : group, Timer = 1 + _rng.NextDouble() * 4, Courage = _rng.NextDouble() };
        a.State = AnimalState.Graze;
        _all[a.Id] = a;
        return a;
    }

    /// <summary>Advance by <paramref name="dt"/> seconds (in steps of 0.1 s): spawn and remove around the hunters, think, move.</summary>
    public void Step(double dt, IReadOnlyList<Hunter> hunters)
    {
        _acc += dt;
        while (_acc >= StepSeconds)
        {
            _acc -= StepSeconds;
            if ((_spawnTimer -= StepSeconds) <= 0) { _spawnTimer = 2; Populate(hunters); }
            foreach (var a in _all.Values.ToList()) Think(a, hunters);
        }
    }

    /// <summary>A hit (weapon or arrow) from a player at (fromX, fromZ): true if it killed the animal.</summary>
    public bool Damage(int id, double amount, int player, double fromX, double fromZ)
    {
        if (!_all.TryGetValue(id, out var a) || a.State == AnimalState.Dead) return false;
        a.Health -= amount;
        if (a.Health <= 0)
        {
            a.Health = 0; a.State = AnimalState.Dead; a.Speed = 0; a.DeadTimer = 2.2; a.KilledBy = player;
            return true;
        }
        // Hurt: the shy run (the whole group), the proud may turn on the hunter, predators go for them.
        bool fight = a.Def.Predator && a.Health > a.Def.Health * .3 || a.Def.Proud && a.Courage > .4 && Dist(a.X, a.Z, fromX, fromZ) < 12;
        if (fight) { a.State = AnimalState.Chase; a.Target = player; a.Timer = 8; }
        else Scare(a, fromX, fromZ, 8);
        foreach (var o in _all.Values.Where(o => o.Group == a.Group && o != a && o.State != AnimalState.Dead && Dist(o.X, o.Z, a.X, a.Z) < 40))
            if (o.Def.Predator && fight) { o.State = AnimalState.Chase; o.Target = player; o.Timer = 8; }
            else if (!o.Def.Predator) Scare(o, fromX, fromZ, 7);
        return false;
    }

    // ---- populating: keep some animals around every player, out of sight; remove those nobody is near
    private void Populate(IReadOnlyList<Hunter> hunters)
    {
        foreach (var a in _all.Values.Where(a => a.State != AnimalState.Dead && !hunters.Any(h => Dist(h.X, h.Z, a.X, a.Z) < KeepRadius)).ToList())
            _all.Remove(a.Id);
        if (Density <= 0 || _defs.Count == 0) return;
        foreach (var h in hunters)
        {
            int near = _all.Values.Count(a => Dist(h.X, h.Z, a.X, a.Z) < AreaRadius);
            if (near >= PerArea * Density) continue;
            double angle = _rng.NextDouble() * Math.Tau, d = SpawnMin + _rng.NextDouble() * (SpawnMax - SpawnMin);
            double x = h.X + Math.Cos(angle) * d, z = h.Z + Math.Sin(angle) * d;
            if (hunters.Any(o => Dist(o.X, o.Z, x, z) < SpawnMin * .8) || !Walkable(x, z)) continue;
            var tags = _world.BiomeTags(x, z);
            var fits = _defs.Where(def => def.Biomes.Overlaps(tags) && !def.Avoid.Overlaps(tags)).ToList();
            if (fits.Count == 0) continue;
            double total = fits.Sum(f => f.Weight), pick = _rng.NextDouble() * total;
            var def = fits.First(f => (pick -= f.Weight) <= 0 || f == fits[^1]);
            int count = def.GroupMin + _rng.Next(def.GroupMax - def.GroupMin + 1), group = _nextGroup++;
            for (int i = 0; i < count; i++)
            {
                double gx = x + (_rng.NextDouble() - .5) * 12, gz = z + (_rng.NextDouble() - .5) * 12;
                if (Walkable(gx, gz)) { var a = Spawn(def, gx, gz, group); a.HomeX = x; a.HomeZ = z; }
            }
        }
    }

    // ---- one animal's tenth of a second
    private void Think(Animal a, IReadOnlyList<Hunter> hunters)
    {
        const double dt = StepSeconds;
        var def = a.Def;
        if (a.State == AnimalState.Dead)
        {
            if ((a.DeadTimer -= dt) <= 0) { _all.Remove(a.Id); Fallen.Add(a); }
            return;
        }
        a.Timer -= dt;
        a.BiteTimer -= dt;
        if (a.Tame) { a.Speed = 0; if (a.State is not (AnimalState.Graze or AnimalState.Idle)) a.State = AnimalState.Graze; return; }
        double hour = ((_world.Hours % 24) + 24) % 24;
        bool night = hour < 5.5 || hour > 20.5;
        // The nearest hunter it notices: within hearing (a sprinting one is heard from twice as far, a still one from
        // half), or within sight (less at night). Flying players are not hunters.
        Hunter? threat = null;
        double threatDist = double.MaxValue;
        foreach (var h in hunters)
        {
            if (h.Flying) continue;
            double d = Dist(h.X, h.Z, a.X, a.Z);
            double hear = def.Hearing * (h.Speed > 7 ? 2 : h.Speed > 1.5 ? 1 : .5), see = def.Sight * (night ? .55 : 1);
            if (h.Sneaking) { hear *= .35; see *= .6; } // (crouched, slow: D132)
            if ((d < hear || d < see) && d < threatDist) { threat = h; threatDist = d; }
        }
        switch (a.State)
        {
            case AnimalState.Idle or AnimalState.Graze or AnimalState.Walk or AnimalState.Alert:
                if (threat is { } t)
                {
                    if (def.Predator && (night || threatDist < def.Sight * .5 || a.State == AnimalState.Alert && a.Timer <= 0))
                    { a.State = AnimalState.Chase; a.Target = t.Id; a.Timer = 12; WakePack(a, t.Id); break; }
                    if (!def.Predator && (threatDist < def.Hearing * .6 || a.State == AnimalState.Alert && a.Timer <= 0))
                    { Scare(a, t.X, t.Z, 6 + _rng.NextDouble() * 4); AlarmGroup(a, t.X, t.Z); break; }
                    if (a.State != AnimalState.Alert) { a.State = AnimalState.Alert; a.Timer = def.Predator ? 2.5 : .9 + _rng.NextDouble() * .8; a.Speed = 0; }
                    a.Yaw = TurnToward(a.Yaw, Math.Atan2(t.X - a.X, t.Z - a.Z), 4 * dt); // (head up, looking at you)
                    break;
                }
                if (a.State == AnimalState.Alert) { a.State = AnimalState.Idle; a.Timer = 2; }
                if (a.Timer <= 0) NextCalm(a);
                if (a.State == AnimalState.Walk)
                {
                    Move(a, Math.Atan2(a.GoalX - a.X, a.GoalZ - a.Z), def.Walk, dt);
                    if (Dist(a.X, a.Z, a.GoalX, a.GoalZ) < 1) NextCalm(a);
                }
                else a.Speed = 0;
                break;
            case AnimalState.Flee:
            {
                double away = Math.Atan2(a.X - a.GoalX, a.Z - a.GoalZ); // (GoalX/Z: what it runs from)
                Move(a, away, def.Run, dt);
                if (a.Timer <= 0 && (threat == null || threatDist > def.Sight)) { a.State = AnimalState.Walk; a.HomeX = a.X; a.HomeZ = a.Z; NextCalm(a); }
                else if (a.Timer <= 0) a.Timer = 2;
                break;
            }
            case AnimalState.Chase or AnimalState.Attack:
            {
                var prey = hunters.FirstOrDefault(h => h.Id == a.Target);
                if (prey.Id == 0 || prey.Flying || Dist(prey.X, prey.Z, a.X, a.Z) > 60 || a.Timer <= 0 && !night && def.Predator && Dist(prey.X, prey.Z, a.X, a.Z) > 25
                    || a.Def.Proud && a.Timer <= 0)
                { a.State = AnimalState.Walk; a.HomeX = a.X; a.HomeZ = a.Z; NextCalm(a); break; }
                if (def.Predator && a.Health < def.Health * .3) { Scare(a, prey.X, prey.Z, 10); break; }
                double d = Dist(prey.X, prey.Z, a.X, a.Z), toward = Math.Atan2(prey.X - a.X, prey.Z - a.Z);
                // (and on its level: a hunter on a platform above cannot be bitten through the floor — Tom, build 68; it waits below)
                if (d <= def.AttackRange + .4 && Math.Abs(prey.Y - a.Y) < 1.6)
                {
                    a.State = AnimalState.Attack; a.Speed = 0;
                    a.Yaw = TurnToward(a.Yaw, toward, 6 * dt);
                    if (a.BiteTimer <= 0)
                    {
                        a.BiteTimer = def.AttackSeconds;
                        Bites.Add(new Hurt(prey.Id, def.Damage, a.X, a.Z, def.Name));
                        a.Timer = Math.Max(a.Timer, 6);
                    }
                }
                else
                {
                    a.State = AnimalState.Chase;
                    Move(a, toward, def.Run * (d < 8 ? .8 : 1), dt);
                }
                break;
            }
            case AnimalState.Hurt:
                if (a.Timer <= 0) { a.State = AnimalState.Idle; a.Timer = 1; }
                break;
        }
    }

    private void NextCalm(Animal a)
    {
        double r = _rng.NextDouble();
        if (Storm > .55)
        {
            // Heavy rain (D217): under a tree (a trunk within ~3 m) a grazer stands still a while, heads down; out in the
            // open it walks to the nearest trees it finds (up to ~35 m) and huddles there. Predators lie low.
            if (a.Def.Predator) { if (r < .75) { a.State = AnimalState.Idle; a.Timer = 8 + _rng.NextDouble() * 12; return; } }
            else if (_world.Obstacle(a.X, a.Z, 3)) { a.State = r < .7 ? AnimalState.Idle : AnimalState.Graze; a.Timer = 10 + _rng.NextDouble() * 15; return; }
            else if (r < .85 && ShelterNear(a) is var (gx, gz) && !double.IsNaN(gx))
            {
                a.State = AnimalState.Walk; a.Timer = 25; a.GoalX = gx; a.GoalZ = gz; a.HomeX = gx; a.HomeZ = gz;
                return;
            }
        }
        if (r < .45) { a.State = AnimalState.Graze; a.Timer = 4 + _rng.NextDouble() * 8; }
        else if (r < .7) { a.State = AnimalState.Idle; a.Timer = 2 + _rng.NextDouble() * 4; }
        else
        {
            a.State = AnimalState.Walk; a.Timer = 12;
            double ang = _rng.NextDouble() * Math.Tau, d = 4 + _rng.NextDouble() * 14;
            a.GoalX = a.HomeX + Math.Cos(ang) * d; a.GoalZ = a.HomeZ + Math.Sin(ang) * d;
        }
    }

    /// <summary>A spot beside the nearest trunk (or rock) within ~35 m, or NaN: rings of 12 directions, nearest first.</summary>
    private (double X, double Z) ShelterNear(Animal a)
    {
        for (double d = 6; d <= 35; d += 6)
            for (int i = 0; i < 12; i++)
            {
                double ang = i * Math.Tau / 12 + d * .13;
                double x = a.X + Math.Sin(ang) * d, z = a.Z + Math.Cos(ang) * d;
                if (!_world.Obstacle(x, z, 2.2) || !Walkable(x, z)) continue;
                // (stand short of it, on the animal's side)
                return (x - Math.Sin(ang) * 2.2, z - Math.Cos(ang) * 2.2);
            }
        return (double.NaN, double.NaN);
    }

    private void Scare(Animal a, double fromX, double fromZ, double seconds)
    {
        if (a.State == AnimalState.Dead) return;
        a.State = AnimalState.Flee; a.GoalX = fromX; a.GoalZ = fromZ; a.Timer = seconds;
    }

    private void AlarmGroup(Animal a, double fromX, double fromZ)
    {
        foreach (var o in _all.Values.Where(o => o.Group == a.Group && o != a && o.State is not (AnimalState.Dead or AnimalState.Flee) && Dist(o.X, o.Z, a.X, a.Z) < 40))
            Scare(o, fromX, fromZ, 5 + _rng.NextDouble() * 4);
    }

    private void WakePack(Animal a, int prey)
    {
        foreach (var o in _all.Values.Where(o => o.Group == a.Group && o != a && o.Def.Predator && o.State is not (AnimalState.Dead or AnimalState.Chase or AnimalState.Attack) && Dist(o.X, o.Z, a.X, a.Z) < 50))
        { o.State = AnimalState.Chase; o.Target = prey; o.Timer = 12; }
    }

    /// <summary>Turn toward <paramref name="heading"/> and step at <paramref name="speed"/>: around water and too-steep
    /// ground (it tries turning either way), on the ground.</summary>
    private void Move(Animal a, double heading, double speed, double dt)
    {
        double want = heading;
        // (stuck inside something — spawned in a thicket, a wall built around it: obstacles do not count until it is out)
        double r = a.Def.Radius + .15;
        bool free = !InTheWay(a.X, a.Y, a.Z, r);
        for (int i = 0; i < 9; i++)
        {
            double h = heading + (i == 0 ? 0 : (i % 2 == 1 ? 1 : -1) * ((i + 1) / 2) * .45);
            double ax = a.X + Math.Sin(h) * 2.5, az = a.Z + Math.Cos(h) * 2.5;
            double mx = a.X + Math.Sin(h) * 1.1, mz = a.Z + Math.Cos(h) * 1.1;
            // Round water, too-steep ground, tree trunks and building pieces: probed a stride and a body ahead.
            if (Walkable(ax, az) && Math.Abs(_world.Ground(ax, az) - a.Y) < 2.2 && (!free || !InTheWay(mx, a.Y, mz, r) && !InTheWay(ax, a.Y, az, r))) { want = h; break; }
            if (i == 8) { a.Speed = 0; a.Yaw = TurnToward(a.Yaw, heading + Math.PI, 3 * dt); return; }
        }
        a.Yaw = TurnToward(a.Yaw, want, (speed > a.Def.Walk * 1.5 ? 5 : 2.5) * dt);
        // (accelerates and slows over ~half a second; goes the way it faces)
        a.Speed += (speed - a.Speed) * Math.Min(1, 4 * dt);
        double nx = a.X + Math.Sin(a.Yaw) * a.Speed * dt, nz = a.Z + Math.Cos(a.Yaw) * a.Speed * dt;
        if (free && InTheWay(nx, a.Y, nz, r)) { a.Speed *= .3; return; } // (still turning toward the way round: it waits)
        a.X = nx; a.Z = nz;
        a.Y = _world.Ground(a.X, a.Z);
    }

    private bool Walkable(double x, double z) => !_world.IsWater(x, z);

    private bool InTheWay(double x, double y, double z, double radius) =>
        _world.Obstacle(x, z, radius) || PieceBlocked?.Invoke(x, y, z, radius) == true;

    private static double TurnToward(double yaw, double target, double maxStep)
    {
        double d = Math.IEEERemainder(target - yaw, Math.Tau);
        return yaw + Math.Clamp(d, -maxStep, maxStep);
    }

    private static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

    // ---- network: the animals near one player (unreliable, ten a second). Compact (D138, the real multiplayer test: the
    // animals were half of what twelve players were sent): the place they are measured from, then per animal its id, kind
    // (an index into the kinds sorted by id), place in centimetres from there (±327 m), height, heading, state, speed and
    // health — 18 bytes an animal instead of ~40.
    public const double MessageRadius = 320;

    public void Write(BinaryWriter w, double x, double z, double radius)
    {
        radius = Math.Min(radius, MessageRadius);
        var kinds = Kinds(_defs.Select(d => d.Id));
        var near = _all.Values.Where(a => Dist(a.X, a.Z, x, z) < radius).ToList();
        double rx = Math.Round(x), rz = Math.Round(z);
        w.Write(rx); w.Write(rz);
        w.Write((ushort)near.Count);
        foreach (var a in near)
        {
            w.Write(a.Id); w.Write((byte)Math.Max(0, kinds.IndexOf(a.Def.Id)));
            w.Write((short)Math.Clamp(Math.Round((a.X - rx) * 100), short.MinValue, short.MaxValue));
            w.Write((short)Math.Clamp(Math.Round((a.Z - rz) * 100), short.MinValue, short.MaxValue));
            w.Write((float)a.Y);
            w.Write((ushort)Math.Round((a.Yaw / Math.Tau - Math.Floor(a.Yaw / Math.Tau)) * 65535));
            w.Write((byte)a.State);
            w.Write((byte)Math.Clamp(Math.Round(a.Speed * 10), 0, 255));
            w.Write((byte)Math.Clamp(Math.Round(a.Health / a.Def.Health * 255), 0, 255));
        }
    }

    /// <summary>The kinds in the order the message numbers them (both ends load the same catalogue).</summary>
    private static List<string> Kinds(IEnumerable<string> ids) => ids.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>A client's copy: the animals in the message (kinds it does not know are skipped).</summary>
    public static List<Animal> Read(BinaryReader r, IReadOnlyDictionary<string, AnimalDef> defs)
    {
        var kinds = Kinds(defs.Keys);
        var list = new List<Animal>();
        double rx = r.ReadDouble(), rz = r.ReadDouble();
        for (int n = r.ReadUInt16(); n > 0; n--)
        {
            int id = r.ReadInt32(); int kind = r.ReadByte();
            double x = rx + r.ReadInt16() / 100.0, z = rz + r.ReadInt16() / 100.0; float y = r.ReadSingle();
            double yaw = r.ReadUInt16() / 65535.0 * Math.Tau; var state = (AnimalState)r.ReadByte();
            double speed = r.ReadByte() / 10.0; byte health = r.ReadByte();
            if (kind >= kinds.Count || !defs.TryGetValue(kinds[kind], out var def)) continue;
            list.Add(new Animal { Id = id, Def = def, X = x, Y = y, Z = z, Yaw = yaw, State = state, Speed = speed, Health = health / 255.0 * def.Health });
        }
        return list;
    }
}
