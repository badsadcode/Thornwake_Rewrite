using System;
using System.Linq;

namespace Thornwake.Core;

public sealed record StationSite(string Id,string Kind,float X,float Z);
public static class Workstations
{
    public static readonly StationSite[] Village={new("village:carpenter","carpenter",6,1),new("village:forge","forge",6,-4),new("village:kiln","kiln",14,2),new("village:cooking","cooking",-7,4),new("village:oven","oven",14,-3)};
    public static string Kind(string kind)=>kind=="workbench"?"carpenter":kind=="campfire"?"cooking":kind;
    public static bool IsStation(string kind)=>new[]{"carpenter","forge","kiln","cooking","oven"}.Contains(Kind(kind));
    public static StationSite? Find(WorldState state,string id)=>Village.FirstOrDefault(s=>s.Id==id)??state.Structures.Where(s=>s.Id==id&&IsStation(s.Kind)).Select(s=>new StationSite(s.Id,Kind(s.Kind),s.X,s.Z)).FirstOrDefault();
    public static bool InReach(WorldState state,StationSite site)
    {
        var piece=state.Structures.Find(s=>s.Id==site.Id);
        float height=piece==null?Construction.Ground(state,site.X,site.Z):Construction.Base(state,piece);
        return Construction.Distance(state.Player.X,state.Player.Z,site.X,site.Z)<=3&&Math.Abs((state.Player.Y??Construction.WalkHeight(state,state.Player.X,state.Player.Z))-height)<1.5f;
    }
}
