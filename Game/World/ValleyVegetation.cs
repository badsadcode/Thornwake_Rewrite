using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.World;

public partial class Valley
{
    private sealed class PlantVisual
    {
        public VegetationState State = null!;
        public Node3D Node = null!;
        public string Kind = "";
        public bool IsStump;
        public bool HasFruit;
        public float Scale;
    }

    private readonly Dictionary<string,PlantVisual> _vegetationVisuals=new();
    public IReadOnlyDictionary<string,Node3D> Stumps=>_stumps;

    /// <summary>Synchronizes changed plant models without rebuilding the valley or its ground cover.</summary>
    public void RefreshVegetation()
    {
        var currentIds=_state.Vegetation.Select(p=>p.Id).ToHashSet();
        foreach(string id in _vegetationVisuals.Keys.Where(id=>!currentIds.Contains(id)).ToArray())RetirePlantVisual(id);
        foreach(string id in _stumps.Keys.Where(id=>_state.RemovedStumps.Contains(id)).ToArray())
        {
            RetirePlantNode(_stumps[id]);_stumps.Remove(id);
        }
        foreach(var plant in _state.Vegetation)
        {
            bool fruit=PlantHasFruit(plant);
            bool reuse=_vegetationVisuals.TryGetValue(plant.Id,out var visual)&&visual.Kind==plant.Kind&&visual.IsStump==plant.IsStump&&visual.HasFruit==fruit&&GodotObject.IsInstanceValid(visual.Node)&&!visual.Node.IsQueuedForDeletion();
            if(!reuse)
            {
                RetirePlantVisual(plant.Id);
                // The first harvest turns a generated berry resource into a persistent living bush.
                if(Resources.TryGetValue(plant.Id,out var natural)){RetirePlantNode(natural);Resources.Remove(plant.Id);}
                if(_stumps.TryGetValue(plant.Id,out var oldStump)){RetirePlantNode(oldStump);_stumps.Remove(plant.Id);}
                Node3D node=plant.IsStump?WoodlandAssets.Stump(Woodland.Planted(plant.Id),rotation:Woodland.Rotation(plant.Id)):plant.Kind=="tree"?WoodlandAssets.Tree(Woodland.Planted(plant.Id),rotation:Woodland.Rotation(plant.Id)):BerryBush(1,fruit);
                float scale=PlantScale(plant);node.Scale=Vector3.One*scale;Place(node,plant.X,plant.Z);
                visual=new PlantVisual{State=plant,Node=node,Kind=plant.Kind,IsStump=plant.IsStump,HasFruit=fruit,Scale=scale};
                _vegetationVisuals[plant.Id]=visual;
            }
            visual!.State=plant;
            visual.Node.Position=At(plant.X,plant.Z);
            SetPlantScale(visual);
            Spawns[plant.Id]=new ResourceSpawn(plant.Id,plant.Kind,plant.X,plant.Z,plant.Kind=="tree"?1:.8f);
            if(plant.IsStump){Resources.Remove(plant.Id);_stumps[plant.Id]=visual.Node;}
            else{_stumps.Remove(plant.Id);Resources[plant.Id]=visual.Node;}
        }
        foreach(var stump in _stumps.Values)
        {
            stump.Position=At(stump.Position.X,stump.Position.Z);
            stump.Visible=!_state.Structures.Any(s=>Construction.IsFloor(s)&&s.Level==0&&Construction.OnFloor(s,stump.Position.X,stump.Position.Z));
        }
    }

    private static bool PlantHasFruit(VegetationState plant)=>plant.Kind=="berries"&&!plant.IsStump&&plant.Growth>=1&&plant.Fruit>=1;
    private static float PlantScale(VegetationState plant)
    {
        if(plant.IsStump)return 1;
        float growth=(float)Math.Clamp(plant.Growth,0,1);
        return plant.Kind=="tree"?Mathf.Lerp(.15f,1,growth):Mathf.Lerp(.18f,.8f,growth);
    }
    private static void SetPlantScale(PlantVisual plant)
    {
        float scale=PlantScale(plant.State);
        if(Mathf.Abs(scale-plant.Scale)<.000001f)return;
        plant.Scale=scale;plant.Node.Scale=Vector3.One*scale;
    }
    private void AnimateVegetation()
    {
        bool rebuild=false;
        foreach(var plant in _vegetationVisuals.Values)
        {
            if(plant.State.IsStump!=plant.IsStump||PlantHasFruit(plant.State)!=plant.HasFruit){rebuild=true;continue;}
            SetPlantScale(plant);
        }
        if(rebuild)RefreshVegetation();
    }
    private float TreeCollisionRadius(string id)=>Resources.TryGetValue(id,out var node)&&node is WoodlandTree tree ? .28f+Woodland.TrunkRadius(tree.Asset)*tree.Scale.X : .55f;
    private void RetirePlantVisual(string id)
    {
        if(!_vegetationVisuals.Remove(id,out var visual))return;
        if(Resources.TryGetValue(id,out var resource)&&resource==visual.Node)Resources.Remove(id);
        if(_stumps.TryGetValue(id,out var stump)&&stump==visual.Node)_stumps.Remove(id);
        Spawns.Remove(id);RetirePlantNode(visual.Node);
    }
    private static void RetirePlantNode(Node3D node)
    {
        if(!GodotObject.IsInstanceValid(node)||node.IsQueuedForDeletion())return;
        node.GetParent()?.RemoveChild(node);node.QueueFree();
    }
}
