using System.Numerics;
using Thornwake.Core;

internal static class CaveWaterTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("CAVE WATER: " + label); checks++; }
        var field = new CaveSpikeField(new());
        for (float y = -8.9f; y < -3.01f; y += .1f)
            Check(field.WaterAt(new(9, y, -9)) == null, "whole sealed lower cavity stays dry");
        var snapshot = field.Snapshot();
        var pool = field.Reservoirs[1];
        var before = ReservoirMesher.Build(field, pool);
        Check(before.Vertices.Count > 100, "connected cave basin has a water surface");
        foreach (float spacing in new[] { 1f, .5f }) foreach (var r in field.Reservoirs)
        {
            var mesh = ReservoirMesher.Build(field, r, spacing: spacing);
            var terrain = new CaveTerrainSampler(field, spacing);
            Check(mesh.Vertices.Count == mesh.Depths.Count && mesh.Vertices.Count == mesh.ShoreDistances.Count, "parallel attributes");
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var p = mesh.Vertices[i];
                Check(p.Y == r.Level && float.IsFinite(p.X) && float.IsFinite(p.Z), "finite local water level");
                Check(terrain.Density(p) <= .00001f, "clipped boundary stays at the basin shoreline");
                if (i % 3 == 0)
                {
                    var center=(mesh.Vertices[i]+mesh.Vertices[i+1]+mesh.Vertices[i+2])/3;
                    Check(terrain.Density(center)<=.00002f,"whole water triangle stays inside rendered void");
                }
                Check(mesh.Depths[i] >= 0 && mesh.Depths[i] <= r.Level - r.Bottom + .001f, "depth bounded by local reservoir");
            }
        }
        float Area(ReservoirMeshData mesh)
        {
            float area = 0;
            for (int i = 0; i < mesh.Vertices.Count; i += 3)
                area += Vector3.Cross(mesh.Vertices[i + 1] - mesh.Vertices[i], mesh.Vertices[i + 2] - mesh.Vertices[i]).Length() * .5f;
            return area;
        }
        var cut = new CaveCut(4, -1.4f, -8, 1.5f);
        Check(field.TryCut(cut), "shore excavation accepted");
        Check(Area(ReservoirMesher.Build(field, pool)) > Area(before) + .5f, "excavation updates actual clipped shoreline");
        Check(snapshot.State.Cuts.Count == 0 && snapshot.Density(cut.Center) > 0 && field.Density(cut.Center) < 0, "worker snapshot independent of live mutations");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        foreach (bool volume in new[] { false, true })
        {
            try { VolumeMesher.Build(field, new(0, 0), .5f, volume, cancellation.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { checks++; }
        }
        return checks;
    }
}



