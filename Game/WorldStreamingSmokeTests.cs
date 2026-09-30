using System;
using System.Linq;
using System.Text.Json;
using Godot;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyTerrainStreaming()
    {
        void Check(bool value, string label) { if (!value) throw new Exception("Terrain streaming: " + label); }
        var previous = _state;
        var state = LandscapeGenerator.Create(7319, new GenerationSettings(), new SandboxSettings());
        state.TerrainEdits.Add(new TerrainEdit { X = -32, Z = 16, Radius = 3, Delta = -.5f });
        state.Earthworks.Steps[TerrainGrid.Key(-32, 16)] = -25;
        CreateWorld(state);
        int resources = _valley.Resources.Count;
        string before = JsonSerializer.Serialize(_state);
        _valley.StreamTerrain(Array.Empty<WorldRegion>());
        Check(_valley.TerrainRegions.Count == 0, "empty observer list releases meshes");
        Check(_valley.Resources.Count == resources && JsonSerializer.Serialize(_state) == before, "unloading does not mutate gameplay state");
        var west = new[] { new WorldRegion(-2, 1) };
        for (int i = 0; i < 5; i++)
        {
            _valley.StreamTerrain(west, 1, 1, 2);
            Check(_valley.LastTerrainRegionsRebuilt <= 2, "bounded mesh commits");
        }
        Check(_valley.TerrainRegions.Count == 9, "nine desired regions loaded");
        var key = new TerrainRegion(-2, 1);
        ulong originalNode = _valley.TerrainRegions[key].GetInstanceId();
        _valley.StreamTerrain(west, 1, 1, 2);
        Check(_valley.LastTerrainRegionsRebuilt == 0 && _valley.TerrainRegions[key].GetInstanceId() == originalNode, "stationary view reuses nodes");
        _valley.StreamTerrain(new[] { new WorldRegion(3, -4) }, 1, 1, 2);
        Check(!_valley.TerrainRegions.ContainsKey(key), "teleport unloads old presentation");
        // Change source while unloaded; the next load must sample current authority, not stale cached mesh data.
        _state.Earthworks.Steps[TerrainGrid.Key(-32, 16)] = -55;
        _state.Earthworks.Revision++;
        _valley.RefreshTerrain(-32, 16, 2);
        for (int i = 0; i < 5; i++) _valley.StreamTerrain(west, 1, 1, 2);
        Check(_valley.TerrainRegions[key].GetInstanceId() != originalNode, "return reconstructs disposed node");
        foreach (var mesh in _valley.TerrainRegions.Values)
        {
            var arrays = mesh.Mesh.SurfaceGetArrays(0);
            foreach (var vertex in arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array())
                Check(Math.Abs(vertex.Y - TerrainData.Height(_state, vertex.X, vertex.Z)) < .00001f, "reloaded vertices include legacy and sparse edits");
        }
        var left = _valley.TerrainRegions[new TerrainRegion(-3, 1)].Mesh.SurfaceGetArrays(0);
        var right = _valley.TerrainRegions[key].Mesh.SurfaceGetArrays(0);
        var lv = left[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
        var rv = right[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
        var ln = left[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
        var rn = right[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
        for (int i = 0; i < lv.Length; i++) if (lv[i].X == -32)
        {
            int j = Array.FindIndex(rv, v => v == lv[i]);
            Check(j >= 0 && ln[i].DistanceTo(rn[j]) < .00001f, "reloaded border heights and normals match");
        }
        float expected = TerrainData.Height(_state, -31.75f, 16.1f);
        SaveStore.Save(SavePath, _state);
        CreateWorld(SaveStore.Load(SavePath));
        Check(TerrainData.Height(_state, -31.75f, 16.1f) == expected, "streamed edits survive existing save roundtrip");
        Check(_valley.TerrainRegions.Count == 64, "default main-game presentation remains unchanged");
        CreateWorld(previous);
        GD.Print("TERRAIN_STREAMING_TEST_PASS: bounded builds, teleport/unload/reload, unchanged authority, unloaded edits, shared edges/normals, saved edits, default 64-region gameplay");
    }
}
