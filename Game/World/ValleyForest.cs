using Godot;
using Thornwake.Core;

namespace Thornwake.World;

public partial class Valley
{
    private double _forestRefresh;
    private Node3D WildTree(ResourceSpawn spawn)=>WoodlandAssets.Tree(Woodland.Natural(spawn),Woodland.NaturalScale(spawn),Woodland.Rotation(spawn.Id));
    private Node3D WildStump(ResourceSpawn spawn)=>WoodlandAssets.Stump(Woodland.Natural(spawn),Woodland.NaturalScale(spawn),Woodland.Rotation(spawn.Id));
    public void UpdateForest(Vector3 cameraPosition,double delta,bool force=false)
    {
        _forestRefresh+=delta;if(!force&&_forestRefresh<.15)return;_forestRefresh=0;
        foreach(var resource in Resources.Values)
            if(resource is WoodlandTree tree && !tree.IsQueuedForDeletion())tree.UpdateDetail(cameraPosition);
    }
    public string TreeName(string id)=>Resources.TryGetValue(id,out var node)&&node is WoodlandTree tree?Woodland.DisplayName(tree.Asset):"tree";
}