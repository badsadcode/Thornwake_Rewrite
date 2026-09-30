using Thornwake.Core;

internal static class DrainagePreviewTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("DRAINAGE_PREVIEW: " + reason); checks++; }
        var profiles = MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Content", "world_profiles.json"));
        foreach (string id in new[] { "vast-world", "archipelago" })
        {
            var manifest = MacroWorldManifest.Create(7319, profiles.Single(p => p.Id == id)) with { WorldId = "0123456789abcdef0123456789abcdef" };
            var world = new MacroWorldGenerator(manifest);
            var recipe = MacroDrainageRecipe.Create(manifest);
            var drainage = MacroDrainage.Build(recipe);
            var original = MacroWorldMap.Build(world, 0, 0, manifest.Profile.WorldSize, 128);
            foreach (var layer in new[] { MacroMapLayer.DrainageRoutes, MacroMapLayer.DrainageCatchments, MacroMapLayer.DrainageBasins })
            {
                var map = MacroDrainageMap.Build(world, drainage, 0, 0, manifest.Profile.WorldSize, 128, layer);
                var restored = MacroDrainageMap.Build(new MacroWorldGenerator(manifest), MacroDrainage.Build(recipe), 0, 0, manifest.Profile.WorldSize, 128, layer);
                Check(map.Pixels.SequenceEqual(restored.Pixels), "diagnostic pixels reconstruct deterministically");
                Check(!map.Pixels.SequenceEqual(original.Pixels), "layer contains drainage information");
                Check(map.MinimumHeight == original.MinimumHeight && map.MaximumHeight == original.MaximumHeight && map.LandFraction == original.LandFraction,
                    "diagnostic layers do not change terrain");
                var zoom = MacroDrainageMap.Build(world, drainage, 0, 0, 128, 64, layer);
                Check(zoom.Pixels.Length == 64 * 64 * 4, "extreme zoom keeps bounded raster size");
            }
            var unchanged = MacroWorldMap.Build(world, 0, 0, manifest.Profile.WorldSize, 128);
            Check(original.Pixels.SequenceEqual(unchanged.Pixels), "elevation preview unchanged after diagnostics");
            try { MacroDrainageMap.Build(world, drainage, 0, 0, 128, 64, MacroMapLayer.DrainageRoutes, new CancellationToken(true)); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { checks++; }
        }
        Console.WriteLine($"DRAINAGE_PREVIEW_PASS: deterministic layers, unchanged terrain, bounded zoom and cancellation ({checks} checks)");
        return checks;
    }
}
