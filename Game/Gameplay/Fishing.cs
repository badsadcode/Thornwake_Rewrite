using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>A kind of fish (Content/gameplay_items.json "fish"): where it lives, how long it fights, how hard it pulls.</summary>
public sealed record FishDef(string Id, string Name, IReadOnlySet<string> Waters, double Stamina, double Pull, double MinKg, double MaxKg, bool Dusk);

/// <summary>
/// Fishing (GAMEPLAY_DESIGN §5b, Enshrouded style; D114), the parts that are not drawing: which fish bites in which
/// water at which hour, how long a bite takes, and the reel-in — a tug-of-war (<see cref="FishFight"/>).
/// </summary>
public static class Fishing
{
    /// <summary>Seconds until a bite: 4–14 s, quicker at dawn and dusk and in light rain, slower at night.</summary>
    public static double BiteDelay(double hour, double rain, Random rng)
    {
        double k = Twilight(hour) > .5 ? .6 : hour < 5 || hour > 22 ? 1.3 : 1;
        if (rain is > .05 and < .6) k *= .8;
        return (4 + 10 * rng.NextDouble()) * k;
    }

    /// <summary>0 … 1: how much it is dawn or dusk (around 6:00 and 19:00).</summary>
    public static double Twilight(double hour) =>
        Math.Max(Math.Max(0, 1 - Math.Abs(hour - 6) / 1.5), Math.Max(0, 1 - Math.Abs(hour - 19) / 1.5));

    /// <summary>The fish that bites in this water at this hour (dawn and dusk favour the ones that hunt then), or null.</summary>
    public static FishDef? Pick(IEnumerable<FishDef> fish, string water, double hour, Random rng)
    {
        var here = fish.Where(f => f.Waters.Contains(water)).ToList();
        if (here.Count == 0) return null;
        double tw = Twilight(hour);
        var weights = here.Select(f => f.Dusk ? .4 + 2.5 * tw : 1.0).ToList();
        double r = rng.NextDouble() * weights.Sum();
        for (int i = 0; i < here.Count; i++) if ((r -= weights[i]) <= 0) return here[i];
        return here[^1];
    }

    public static string WaterName(Thornwake.WorldGen.WaterKind kind) => kind switch
    {
        Thornwake.WorldGen.WaterKind.Ocean => "ocean", Thornwake.WorldGen.WaterKind.Lake => "lake",
        Thornwake.WorldGen.WaterKind.River => "river", Thornwake.WorldGen.WaterKind.Pond => "pond", _ => "",
    };
}

/// <summary>
/// The reel-in (Enshrouded's tug-of-war): the fish pulls left or right for a while, then rests a moment, then pulls
/// again. Holding the other way (A against a pull to the right, D against one to the left) tires it — the rod's
/// strength speeds that; holding the wrong way or nothing while it pulls wears the line — the rod's endurance slows
/// that; holding on while it rests wears the line a little (switch, or let go, the moment it stops). The fish is landed
/// when its stamina is gone, lost when the line's is.
/// </summary>
public sealed class FishFight
{
    public FishDef Fish { get; }
    public double FishStamina { get; private set; } = 1;
    public double Line { get; private set; } = 1;
    /// <summary>−1 pulling left, +1 pulling right, 0 resting.</summary>
    public int Pull { get; private set; }
    /// <summary>0 … 1: how hard the line is strained now (the line drawn redder; Tom). High while the fish pulls and is
    /// not answered, moderate while it is, low while it rests.</summary>
    public double Tension { get; private set; } = .2;
    /// <summary>−1 … 1: where the fish has run to, left or right of the line (the bobber is dragged there).</summary>
    public double Side { get; private set; }
    public bool Landed => FishStamina <= 0;
    public bool Lost => Line <= 0 && !Landed;
    public bool Over => Landed || Lost;
    private readonly double _strength, _endurance;
    private readonly Random _rng;
    private double _timer = .6, _sinceSwitch;
    /// <summary>Seconds after the fish switches in which the old answer does not wear the line yet.</summary>
    public const double ReactionWindow = .2;

    public FishFight(FishDef fish, double rodStrength, double rodEndurance, Random rng)
    {
        Fish = fish; _strength = Math.Max(.1, rodStrength); _endurance = Math.Max(.1, rodEndurance); _rng = rng;
    }

    /// <param name="input">−1 holding A (pulling left), +1 holding D (right), 0 nothing.</param>
    public void Step(double dt, int input)
    {
        if (Over || dt <= 0) return;
        _timer -= dt;
        if (_timer <= 0)
        {
            if (Pull != 0) { Pull = 0; _timer = .4 + .5 * _rng.NextDouble(); }
            else { Pull = _rng.NextDouble() < .5 ? -1 : 1; _timer = 1 + 1.5 * _rng.NextDouble(); }
            _sinceSwitch = 0;
        }
        _sinceSwitch += dt;
        bool reacting = _sinceSwitch < ReactionWindow; // (a fair moment to see the switch and answer it)
        // Where it runs: fast unanswered, slowly held; drifting back while it rests.
        Side = Math.Clamp(Side + (Pull != 0 ? Pull * dt * (input == -Pull ? .12 : .45) : -Side * dt * .4), -1, 1);
        double target = Pull == 0 ? (input != 0 ? .35 : .12) : input == -Pull ? .45 : reacting ? .6 : 1;
        Tension += (target - Tension) * Math.Min(1, dt * 6);
        if (Pull != 0)
        {
            if (input == -Pull) FishStamina -= dt * _strength / Fish.Stamina;
            else if (!reacting) Line -= dt * Fish.Pull * .2 / _endurance;
        }
        else
        {
            if (input != 0 && !reacting) Line -= dt * .1 / _endurance;
            FishStamina = Math.Min(1, FishStamina + dt * .015);
        }
        FishStamina = Math.Max(0, FishStamina); Line = Math.Max(0, Line);
    }
}
