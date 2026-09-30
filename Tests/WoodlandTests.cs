using Thornwake.Core;

internal static class WoodlandTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception("WOODLAND: "+message);checks++;}
        var seen=new HashSet<string>();
        for(int seed=-20;seed<=20;seed++)
        {
            var original=WorldGenerator.Generate(seed);
            foreach(var tree in original.Where(r=>r.Kind=="tree"))
            {
                string asset=Woodland.Natural(tree);seen.Add(asset);
                Check(Woodland.Assets.Contains(asset),"wild tree resolves to a shipped mesh");
                Check(Woodland.TrunkRadius(asset)>0 && Woodland.NaturalScale(tree) is >=.9f and <=1.1f,"sensible collision and presentation scale");
                Check(Woodland.Natural(tree)==Woodland.Natural(tree with {}),"appearance derived from persistent identity");
                Check(Woodland.Planted(tree.Id).StartsWith("Pine_"),"pine seedlings stay pine");
                Check(Woodland.Rotation(tree.Id) is >=0 and <MathF.Tau,"valid orientation");
            }
            Check(original.SequenceEqual(WorldGenerator.Generate(seed)),"presentation leaves resource generation unchanged");
        }
        Check(seen.SetEquals(Woodland.Assets),"all 17 variants occur in natural worlds");
        Check(Woodland.Detail(10,2)==0&&Woodland.Detail(100,0)==2,"teleport selects correct LOD");
        Check(Woodland.Detail(40,0)==0&&Woodland.Detail(40,1)==1,"near threshold hysteresis");
        Check(Woodland.Detail(74,1)==1&&Woodland.Detail(74,2)==2,"far threshold hysteresis");
        Check(Woodland.Detail(50,0)==1&&Woodland.Detail(50,2)==1,"middle range has no missing level");
        var state=new WorldState{Seed=7319};var sim=new Simulation(state,catalog);
        state.Player.EquippedItem="basic_axe";
        foreach(var resource in WorldGenerator.Generate(state.Seed).Where(r=>r.Kind=="tree"))
        {
            state.Player.X=resource.X+1;state.Player.Z=resource.Z;
            int cones=sim.Count("pine_cone"),wood=sim.Count("wood");
            Check(sim.Gather(resource.Id,"tree").Success&&sim.Count("wood")>wood,"every species yields timber");
            Check(sim.Count("pine_cone")-cones==(Woodland.Natural(resource).StartsWith("Pine_")?1:0),"only wild pines supply pine cones");
            Check(!sim.Gather(resource.Id,"tree").Success,"felled tree cannot pay twice");
        }
        return checks;
    }
}