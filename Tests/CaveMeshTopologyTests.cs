using System.Numerics;
using Thornwake.Core;

internal static class CaveMeshTopologyTests
{
    public static int Run()
    {
        int checks = 0;
        for (int scenario = 0; scenario < 6; scenario++)
        {
            var field = new CaveSpikeField(new());
            for (int i = 0; i < 20; i++)
                field.TryCut(new(2.8f + .42f * (i % 3), .3f + .43f * (i % 4), -4.1f - .51f * i, 1.5f));
            var random = new Random(1837 + scenario);
            if (scenario > 0)
                for (int i = 0; i < 60; i++)
                    field.TryCut(new((float)random.NextDouble() * 7 - 1, (float)random.NextDouble() * 8 - 4,
                        (float)random.NextDouble() * 16 - 15, .5f + (float)random.NextDouble() * 1.5f));
            if(scenario>=4)
            {
                field=new(CaveSpikeStore.Load("Tests/Fixtures/cave-player-shore-cuts-v2.json"));
                // The saved file contains six cuts; the screenshot's unsaved seventh is unavailable.
                if(scenario==5 && !field.TryCut(new(0,-1.9f,2.7f,1.5f))) throw new Exception("shore bridge cut rejected");
            }
            var regions=scenario>=4 ? new[]{new TerrainRegion(-1,0),new TerrainRegion(0,0),new TerrainRegion(-1,1),new TerrainRegion(0,1)} : new[]{new TerrainRegion(0,-1)};
            foreach(var region in regions)
            foreach (float spacing in new[] { 1f, .5f })
            {

                var mesh = VolumeMesher.Build(field, region, spacing, true);
                var sharedNormals = new Dictionary<Vector3,Vector3>();
                for(int i=0;i<mesh.Vertices.Count;i++)
                {
                    var point=mesh.Vertices[i]; var normal=mesh.Normals[i];
                    if(!float.IsFinite(normal.X) || Math.Abs(normal.Length()-1)>.0001f ||
                        sharedNormals.TryGetValue(point,out var prior) && Vector3.Distance(prior,normal)>.0001f)
                        throw new Exception("Excavated stress mesh has invalid or split shared-vertex normals");
                    sharedNormals[point]=normal; checks++;
                }
                var edges = new Dictionary<(Vector3, Vector3), (int Count, int Direction)>();
                int Compare(Vector3 a, Vector3 b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
                void Edge(Vector3 a, Vector3 b)
                {
                    int direction = Compare(a, b) < 0 ? 1 : -1;
                    var key = direction > 0 ? (a, b) : (b, a);
                    edges.TryGetValue(key, out var value);
                    edges[key] = (value.Count + 1, value.Direction + direction);
                }
                for (int i = 0; i < mesh.Vertices.Count; i += 3)
                {
                    var a = mesh.Vertices[i]; var b = mesh.Vertices[i + 1]; var c = mesh.Vertices[i + 2];
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
                bool Boundary(Vector3 a, Vector3 b) => a.X == b.X && (a.X == region.MinX || a.X == region.MinX + 16) ||
                    a.Z == b.Z && (a.Z == region.MinZ || a.Z == region.MinZ + 16) || a.Y == b.Y && (a.Y == -12 || a.Y == 16);
                int holes = 0, reversed = 0;
                foreach (var (edge, value) in edges)
                {
                    if (Boundary(edge.Item1, edge.Item2)) continue;
                    if (value.Count != 2) holes++;
                    else if (value.Direction != 0) reversed++;
                    checks++;
                }
                if (holes != 0 || reversed != 0)
                    throw new Exception($"Excavated topology at {spacing} m: {holes} open/nonmanifold edges, {reversed} inconsistently wound shared edges");
            }
        }
        return checks;
    }
}


