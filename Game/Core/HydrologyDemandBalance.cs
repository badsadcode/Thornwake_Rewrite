using System.Collections.Generic;

namespace Thornwake.Core;

internal sealed partial class AdaptiveBasinTopology
{
    private readonly SortedSet<HydrologyCell> _balance=new();
    private bool _selectingSeed;
    private void ScheduleReachedRoot(Cell root)
    {
        // Initial default seed selection examines a whole patch; do not balance its unrelated dry
        // interiors. Later access schedules only refined roots actually reached by the searches.
        if(_selectingSeed || root.FaceWorkScheduled || root.Children==null)return;
        root.FaceWorkScheduled=true;
        void Collect(Cell c){if(c.Children==null)_balance.Add(c.Key);else foreach(var child in c.Children)Collect(child);}
        Collect(root);
    }
    // Only reached refined roots and force-split leaves are scheduled. No new root is admitted.
    // Each visit is charged to NodeVisits; resulting samples/cells/splits use the normal frozen caps.
    private bool BalanceKnownFaces()
    {
        while(_balance.Count>0)
        {
            _cancel.ThrowIfCancellationRequested();var key=_balance.Min;_balance.Remove(key);
            var cell=At(key.X,key.Z);if(cell.Key!=key)continue; // Retired by an earlier correction.
            if(!Visit(cell))return false;_work?.SharedVisit();
            try
            {
                foreach(var neighbour in KnownNeighbours(cell))
                {
                    string? failure=ReconcileFace(cell,neighbour);
                    if(failure!=null)
                    {
                        Unknown(cell,Owner(cell.Key),failure,-100000);
                        // Resolution uncertainty cannot be repaired by balancing. The next normal
                        // search observes it and keeps the appropriate resolution stop, not closure.
                        if(failure=="resolution-limit")continue;
                        return false;
                    }
                }
            }
            catch(TopologyChanged){_balance.Add(key);}
        }
        return true;
    }
    private IEnumerable<Cell> KnownNeighbours(Cell c)
    {
        var k=c.Key;
        foreach(var direction in Directions)
        {
            bool horizontal=direction.X!=0;
            long fixedCoordinate=horizontal?(direction.X<0?k.X-1:k.X+k.Size):(direction.Z<0?k.Z-1:k.Z+k.Size);
            long start=horizontal?k.Z:k.X,end=start+k.Size;
            if(fixedCoordinate<0 || fixedCoordinate>=(long)(horizontal?_d.Width:_d.Depth)*_patchUnits)continue;
            for(long along=start;along<end;)
            {
                _cancel.ThrowIfCancellationRequested();long x=horizontal?fixedCoordinate:along,z=horizontal?along:fixedCoordinate;
                int patch=(int)(z/_patchUnits)*_d.Width+(int)(x/_patchUnits);
                int root=(int)(z%_patchUnits/_unit)*_d.Subdivisions+(int)(x%_patchUnits/_unit);
                if(!_patches.TryGetValue(patch,out var data) || data.Roots[root]==null)
                {along=System.Math.Min(end,(along/_unit+1)*_unit);continue;}
                var next=At(x,z);yield return next;
                along=System.Math.Min(end,horizontal?next.Key.Z+next.Key.Size:next.Key.X+next.Key.Size);
            }
        }
    }
}
