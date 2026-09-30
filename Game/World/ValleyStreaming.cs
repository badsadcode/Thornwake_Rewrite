using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Core;

namespace Thornwake.World;

public partial class Valley
{
    /// <summary>Development harness entry: empty presentation over the existing terrain authority.</summary>
    public void InitializeTerrainPreview(WorldState state)
    {
        if (_state != null) throw new InvalidOperationException("Terrain source is already initialized.");
        LandscapeGenerator.Validate(state);
        _state = state;
        _terrain = new Node3D { Name = "TerrainRegions" };
        AddChild(_terrain);
        // Valley constructs this container eagerly; attach it so scene teardown owns its lifetime.
        AddChild(Buildings);
    }

    /// <summary>
    /// Opt-in finite legacy adapter. Normal gameplay still calls RebuildTerrain.
    /// Removing presentation must never remove persistent terrain edits or resource state.
    /// </summary>
    public RegionStreamPlan StreamTerrain(IReadOnlyList<WorldRegion> observers,
        int loadRadius = 2, int retainRadius = 3, int buildBudget = 2)
    {
        if (_state == null) throw new InvalidOperationException("Initialize the terrain source first.");
        var residents = _terrainRegions.Keys.Select(r => new WorldRegion(r.X, r.Z)).ToArray();
        var plan = RegionStreaming.Plan(residents, observers, loadRadius, retainRadius, buildBudget,
            r => r.X >= FirstTerrainRegion && r.X <= LastTerrainRegion &&
                 r.Z >= FirstTerrainRegion && r.Z <= LastTerrainRegion);
        foreach (var region in plan.Unload)
        {
            var key = new TerrainRegion((int)region.X, (int)region.Z);
            if (_terrainRegions.Remove(key, out var mesh)) { _terrain.RemoveChild(mesh); mesh.QueueFree(); }
            if (_grassRegions.Remove(key, out var grass)) { _terrain.RemoveChild(grass); grass.QueueFree(); }
        }
        LastTerrainRegionsRebuilt = 0;
        foreach (var region in plan.Load)
        {
            var key = new TerrainRegion((int)region.X, (int)region.Z);
            RebuildRegion(key);
            if (_decorated) RebuildGrassRegion(key);
            LastTerrainRegionsRebuilt++;
        }
        return plan;
    }
}
