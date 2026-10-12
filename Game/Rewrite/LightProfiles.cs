using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Atmosphere 2.0 light model 1: the colours of the light by biome (<c>Content/light_profiles.json</c>). Each profile has
/// its own shade fill and haze curves over the sun elevation, night haze, overcast and storm colours, sun tint, fill
/// share, sun scatter and exposure bias. The profiles around the player are blended by weight, and the weights drift
/// toward the biomes at the player over ~3 s, so crossing into another biome is never a jump.
/// </summary>
public static class LightProfiles
{
    public sealed class Profile
    {
        public required string Id;
        public (float E, Color C)[] Shade = Array.Empty<(float, Color)>(), Fog = Array.Empty<(float, Color)>();
        public Color NightFog, Overcast, Storm, SunTint;
        public float ShadeShare, NightShare, Scatter, Exposure;
    }

    /// <summary>The blended values at one sun elevation.</summary>
    public readonly record struct Mix(Color Shade, Color Fog, Color NightFog, Color Overcast, Color Storm, Color SunTint,
        float ShadeShare, float NightShare, float Scatter, float Exposure);

    private static Profile[]? _profiles;
    private static Dictionary<string, int> _byBiome = new();
    private static float[] _weights = Array.Empty<float>(), _target = Array.Empty<float>();

    public static IReadOnlyList<Profile> All => _profiles ??= Load();

    private static Profile[] Load()
    {
        var root = JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://Content/light_profiles.json"))!.AsObject();
        var nodes = root["profiles"]!.AsArray().Select(n => n!.AsObject()).ToList();
        var basis = nodes[0];
        JsonNode? Get(JsonObject o, string key) => o[key] ?? basis[key];
        static (float, Color)[] Curve(JsonNode n) => n.AsArray().Select(p => (p![0]!.GetValue<float>(), new Color(p[1]!.GetValue<string>()))).ToArray();
        var list = new List<Profile>();
        _byBiome = new Dictionary<string, int>();
        foreach (var o in nodes)
        {
            var p = new Profile
            {
                Id = o["id"]!.GetValue<string>(),
                Shade = Curve(Get(o, "shade")!), Fog = Curve(Get(o, "fog")!),
                NightFog = new Color(Get(o, "night_fog")!.GetValue<string>()), Overcast = new Color(Get(o, "overcast")!.GetValue<string>()),
                Storm = new Color(Get(o, "storm")!.GetValue<string>()), SunTint = new Color(Get(o, "sun_tint")!.GetValue<string>()),
                ShadeShare = Get(o, "shade_share")!.GetValue<float>(), NightShare = Get(o, "night_share")!.GetValue<float>(),
                Scatter = Get(o, "scatter")!.GetValue<float>(), Exposure = Get(o, "exposure")!.GetValue<float>(),
            };
            foreach (var b in o["biomes"]?.AsArray() ?? new JsonArray()) _byBiome[b!.GetValue<string>()] = list.Count;
            list.Add(p);
        }
        _weights = new float[list.Count]; _weights[0] = 1;
        _target = new float[list.Count]; _target[0] = 1;
        return list.ToArray();
    }

    /// <summary>The profile index of a biome (0 = temperate when it is not listed).</summary>
    public static int For(string biomeId) { _ = All; return _byBiome.TryGetValue(biomeId, out int i) ? i : 0; }

    /// <summary>The biomes at the player: profile <paramref name="a"/> with share <paramref name="share"/>, the rest <paramref name="b"/>.</summary>
    public static void SetTarget(int a, int b, float share)
    {
        _ = All;
        Array.Clear(_target);
        _target[a] += share;
        _target[b] += 1 - share;
    }

    /// <summary>Drift the weights toward the target (~3 s), or jump there (teleports, stills).</summary>
    public static void Update(float delta, bool snap)
    {
        _ = All;
        float k = snap ? 1 : 1 - Mathf.Exp(-delta / 3);
        for (int i = 0; i < _weights.Length; i++) _weights[i] = Mathf.Lerp(_weights[i], _target[i], k);
    }

    /// <summary>The profile id with the largest weight (logs).</summary>
    public static string Dominant { get { _ = All; int best = 0; for (int i = 1; i < _weights.Length; i++) if (_weights[i] > _weights[best]) best = i; return _profiles![best].Id; } }

    public static Mix At(float elevation)
    {
        var all = All;
        Color shade = default, fog = default, nightFog = default, overcast = default, storm = default, tint = default;
        float share = 0, night = 0, scatter = 0, exposure = 0, total = 0;
        for (int i = 0; i < all.Count; i++)
        {
            float w = _weights[i];
            if (w < .001f) continue;
            var p = all[i];
            shade += Sample(p.Shade, elevation) * w; fog += Sample(p.Fog, elevation) * w;
            nightFog += p.NightFog * w; overcast += p.Overcast * w; storm += p.Storm * w; tint += p.SunTint * w;
            share += p.ShadeShare * w; night += p.NightShare * w; scatter += p.Scatter * w; exposure += p.Exposure * w;
            total += w;
        }
        float n = 1 / Math.Max(total, 1e-4f);
        return new Mix(shade * n, fog * n, nightFog * n, overcast * n, storm * n, tint * n, share * n, night * n, scatter * n, exposure * n);
    }

    public static Color Sample((float E, Color C)[] t, float x)
    {
        if (x <= t[0].E) return t[0].C;
        for (int i = 1; i < t.Length; i++)
            if (x <= t[i].E) return t[i - 1].C.Lerp(t[i].C, Mathf.SmoothStep(t[i - 1].E, t[i].E, x));
        return t[^1].C;
    }
}
