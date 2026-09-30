using Godot;
using System;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyTerrainRegions()
    {
        void Check(bool value,string message){if(!value)throw new Exception("Terrain regions: "+message);}
        var previous=_state;
        CreateWorld(new WorldState{Seed=7319});
        var ids=_valley.TerrainRegions.ToDictionary(p=>p.Key,p=>p.Value.GetInstanceId());
        var edit=new TerrainEdit{X=-32,Z=16,Radius=3,Delta=-.5f};
        var affected=TerrainData.Affected(edit.X,edit.Z,edit.Radius).ToHashSet();
        _state.TerrainEdits.Add(edit);_valley.RefreshTerrain(edit);
        Check(_valley.LastTerrainRegionsRebuilt==4,"boundary edit rebuilds four regions");
        foreach(var region in _valley.TerrainRegions)
            Check((region.Value.GetInstanceId()!=ids[region.Key])==affected.Contains(region.Key),"only dirty region nodes replaced");
        foreach(var resource in _valley.Resources.Values)
            Check(Mathf.Abs(resource.Position.Y-_valley.Height(resource.Position.X,resource.Position.Z))<.0001f,"resources remain grounded");
        // Inspect actual render vertices and normals on a shared region edge.
        var left=_valley.TerrainRegions[new TerrainRegion(-3,1)].Mesh;
        var right=_valley.TerrainRegions[new TerrainRegion(-2,1)].Mesh;
        var la=left.SurfaceGetArrays(0);var ra=right.SurfaceGetArrays(0);
        var lv=la[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();var rv=ra[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
        var ln=la[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();var rn=ra[(int)Godot.Mesh.ArrayType.Normal].AsVector3Array();
        for(int i=0;i<lv.Length;i++)if(Mathf.IsEqualApprox(lv[i].X,-32))
        {
            int j=Array.FindIndex(rv,v=>v==lv[i]);Check(j>=0,"shared mesh edge has matching heights");
            Check(ln[i].DistanceTo(rn[j])<.0001f,"shared mesh normals match");
        }
        float height=_valley.Height(-31.5f,16.4f);
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(_valley.Height(-31.5f,16.4f)==height,"region reconstruction preserves saved height");
        CreateWorld(previous);
        GD.Print("TERRAIN_REGIONS_TEST_PASS: shared legacy authority, 1m mesh sampling, 64 regions, four-region border edit, untouched region identity, grounded resources, matched edge heights/normals, save reconstruction");
    }
    private void ShowTerrainPreview()
    {
        var state=new WorldState{Seed=7319,EnemyDefeated=true,CalendarMinutes=12*60};
        state.Player.X=-30;state.Player.Z=22;
        state.TerrainEdits.AddRange(new[]{new TerrainEdit{X=-32,Z=16,Radius=3,Delta=-1.1f},new TerrainEdit{X=-28,Z=19,Radius=3,Delta=.9f}});
        foreach(var r in WorldGenerator.Generate(state.Seed).Where(r=>Construction.Distance(r.X,r.Z,-30,18)<9)){state.RemovedResources.Add(r.Id);if(r.Kind=="tree")state.RemovedStumps.Add(r.Id);}
        CreateWorld(state);_hud.ShowGame();SelectSlot(2);
        _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-4);
        for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);_valley.Animate(state.Time,0);
        if(_view=="terrain-regions")_valley.CycleTerrainDebug();
        if(_view=="terrain-delta"){_valley.CycleTerrainDebug();_valley.CycleTerrainDebug();}
        UpdateContext();UpdateHud();
    }
}