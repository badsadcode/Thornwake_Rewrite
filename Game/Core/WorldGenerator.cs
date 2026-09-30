using System;
using System.Collections.Generic;

namespace Thornwake.Core;

public static class WorldGenerator
{
    public const float WorldLimit = 55;

    public static List<ResourceSpawn> Generate(WorldState state)=>state.GeneratorVersion switch{1=>Generate(state.Seed),2=>LandscapeGenerator.Resources(state),_=>throw new System.IO.InvalidDataException("Unsupported landscape generator.")};

    public static List<ResourceSpawn> Generate(int seed)
    {
        // A specified integer PRNG keeps a saved seed stable across runtime versions.
        uint state = unchecked((uint)seed) ^ 0x9E3779B9u;
        if (state == 0) state = 1;
        float Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFFu) / 16777216f;
        }
        var resources = new List<ResourceSpawn>();
        for (int attempt = 0; attempt < 5000 && resources.Count < 170; attempt++)
        {
            float x = -52 + Next() * 104, z = -52 + Next() * 104;
            if (Math.Abs(x) < 17 && Math.Abs(z) < 16) continue;
            if (Math.Abs(x - 27) < 5 || Math.Abs(x) < 3 || Math.Abs(z) < 3) continue;
            // These occupied lots belong to the village presentation and the road encounter.
            if (x >= 16 && x <= 24 && z >= 5 && z <= 15) continue;
            if ((x + 29) * (x + 29) + (z + 23) * (z + 23) < 36) continue;
            bool crowded = false;
            foreach (var resource in resources)
            {
                float dx = resource.X - x, dz = resource.Z - z;
                if (dx * dx + dz * dz < 12.25f) { crowded = true; break; }
            }
            if (crowded) continue;
            int index = resources.Count;
            string kind = index % 7 == 0 ? "stone" : index % 5 == 0 ? "berries" : "tree";
            resources.Add(new ResourceSpawn($"r-{seed}-{index}", kind, x, z, .75f + Next() * .65f));
        }
        return resources;
    }

    public static float Height(float x, float z, int seed)
    {
        float blend = Math.Clamp((Math.Max(Math.Abs(x) / 17, Math.Abs(z) / 16) - 1) * .65f, 0, 1);
        blend = blend * blend * (3 - 2 * blend);
        float phase = (seed % 10000) * .001f;
        float hills = .95f * MathF.Sin(x * .075f + phase) * MathF.Cos(z * .068f - phase)
            + .3f * MathF.Sin(x * .17f + z * .11f + phase);
        float river = MathF.Exp(-MathF.Pow((x - 27) / 3.2f, 2)) * 1.6f;
        return hills * blend - river;
    }
}
