using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Atmosphere 2.0 M1 (docs/ATMOSPHERE_2_0_M1_PLAN.md): the light of the frame in one place — sun colour and energy,
/// the sky light's energy — from the sun elevation and the cloud gloom. Setting <c>fx.light_model</c>: 0 = build 47's
/// formulas exactly (the return point), 1 = the new sun curve by phase (candidate values, tuned against Tom and Noor's
/// reference images), a cool fill in the shade (warm sun, cool shade) and a narrow exposure adaptation (forest interiors,
/// golden hour). Bloom comes later (pause point 3).
/// </summary>
public static class AtmosphereLight
{
    /// <param name="ShadeTint">Colour mixed into the sky light (1 − <paramref name="SkyShare"/> of it): the cool shade.</param>
    public readonly record struct State(Color SunColor, float SunEnergy, float AmbientEnergy, Color ShadeTint, float SkyShare);

    public static bool Modern => GameSettings.LightModel.Index == 1;

    /// <param name="elevation">Sun elevation in degrees (the light stops at −4°).</param>
    /// <param name="gloom">0 clear … 1 storm (overcast counts 0.7).</param>
    /// <param name="natural">Look natural (ACES) rather than classic.</param>
    public static State Compute(float elevation, float gloom, bool natural)
    {
        if (!Modern)
        {
            // Build 47: one ramp to 25°, one warm factor to 30°.
            float warm = 1 - Mathf.Clamp(elevation / 30, 0, 1);
            return new State(
                new Color("fff3df").Lerp(new Color("ffc58a"), warm * .8f),
                Mathf.Clamp(elevation / 25, 0.05f, 1.1f) * Mathf.Lerp(1, .3f, gloom),
                natural ? Mathf.Lerp(.42f, .3f, gloom) : Mathf.Lerp(.55f, .38f, gloom), Colors.White, 1);
        }
        // By phase: blue hour (below 0°), golden hour (0–10°: warm, low), morning/afternoon (10–35°: rising),
        // high noon (above 35°: white, strongest). Overcast takes most of the sun and flattens toward the sky light.
        float sun = Curve(elevation, SunEnergyCurve) * Mathf.Lerp(1, .28f, gloom);
        var color = CurveColor(elevation).Lerp(new Color("e8ecf2"), gloom * .6f);
        float sky = Curve(elevation, SkyEnergyCurve) * (natural ? 1 : 1.3f) * Mathf.Lerp(1, .82f, gloom);
        // Warm sun, cool shade: a blue fill in the sky light, deepest in the blue hour, near-neutral under overcast.
        var shade = CurveColor(elevation, ShadeCurve).Lerp(new Color("b4b9c2"), gloom);
        return new State(color, sun, sky, shade, Mathf.Lerp(.7f, .88f, gloom));
    }

    /// <summary>Exposure multiplier the eye adapts toward (1 in build 47's model): a forest interior is lifted up to
    /// +0.5 stop and a dim golden hour or overcast day up to +0.25 stop, so neither needs the Brightness slider, while
    /// night stays dark and noon on open ground stays as it is.</summary>
    /// <param name="canopy">0 open … 1 closed forest around the camera.</param>
    public static float Exposure(in State light, float elevation, float canopy)
    {
        if (!Modern) return 1;
        // How much light reaches open ground, relative to a clear noon (sun on flat ground plus the sky).
        float key = (light.SunEnergy * .75f + light.AmbientEnergy * 1.6f) / (1.3f * .75f + .44f * 1.6f);
        float day = Mathf.Clamp(Mathf.Pow(Mathf.Max(key, .05f), -.25f), 1, 1.19f);
        day = Mathf.Lerp(1, day, Mathf.SmoothStep(-4, 2, elevation));
        float forest = Mathf.Pow(Mathf.Lerp(1, .5f, Mathf.Clamp(canopy, 0, 1)), -.5f);
        return Mathf.Clamp(day * forest, 1, 1.41f);
    }

    // (elevation °, value): sun energy and sky-light energy (natural look).
    private static readonly (float E, float V)[] SunEnergyCurve =
        { (-4, .02f), (0, .16f), (5, .42f), (10, .68f), (20, .92f), (35, 1.12f), (60, 1.28f), (90, 1.32f) };
    private static readonly (float E, float V)[] SkyEnergyCurve =
        { (-6, .16f), (0, .24f), (10, .33f), (35, .40f), (60, .43f), (90, .44f) };
    // (elevation °, colour): deep orange at sunrise, gold, warm white, white at noon.
    private static readonly (float E, Color C)[] SunColorCurve =
    {
        (-4, new Color("ff8e52")), (0, new Color("ffa262")), (5, new Color("ffb978")), (10, new Color("ffcf98")),
        (20, new Color("ffe2bd")), (35, new Color("fff0dc")), (60, new Color("fff6ea")), (90, new Color("fff8ee")),
    };

    private static float Curve(float x, (float E, float V)[] table)
    {
        if (x <= table[0].E) return table[0].V;
        for (int i = 1; i < table.Length; i++)
            if (x <= table[i].E) return Mathf.Lerp(table[i - 1].V, table[i].V, Mathf.SmoothStep(table[i - 1].E, table[i].E, x));
        return table[^1].V;
    }

    // (elevation °, colour of the shade fill): night blue, blue hour, violet-blue at golden hour, sky blue by day.
    private static readonly (float E, Color C)[] ShadeCurve =
    {
        (-8, new Color("50608f")), (-2, new Color("7482c4")), (4, new Color("9ea6dc")), (15, new Color("a9c0ea")),
        (40, new Color("b6cdf2")), (90, new Color("bcd2f4")),
    };

    private static Color CurveColor(float x) => CurveColor(x, SunColorCurve);

    private static Color CurveColor(float x, (float E, Color C)[] t)
    {
        if (x <= t[0].E) return t[0].C;
        for (int i = 1; i < t.Length; i++)
            if (x <= t[i].E) return t[i - 1].C.Lerp(t[i].C, Mathf.SmoothStep(t[i - 1].E, t[i].E, x));
        return t[^1].C;
    }
}
