using System.Numerics;
using System.Text.Json;
using Thornwake.Core;

internal static class TerrainDataTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception("TERRAIN DATA: "+message);checks++;}
        float Legacy(WorldState state,float x,float z)
        {
            float h=WorldGenerator.Height(x,z,state.Seed);
            foreach(var e in state.TerrainEdits){float d=Construction.Distance(x,z,e.X,e.Z)/e.Radius;if(d<1)h+=e.Delta*(1-d*d)*(1-d*d);}
            return h;
        }
        foreach(int seed in new[]{-921,0,7319,73421})
        {
            var world=new WorldState{Seed=seed};
            world.TerrainEdits.AddRange(new[]{new TerrainEdit{X=-16,Z=16,Radius=3,Delta=.8f},new TerrainEdit{X=-15,Z=17,Radius=2,Delta=-.3f}});
            string before=JsonSerializer.Serialize(world);
            for(float x=-50;x<=50;x+=2.3f)for(float z=-50;z<=50;z+=3.1f)
            {
                Check(TerrainData.Height(world,x,z)==Legacy(world,x,z),"exact legacy height preservation");
                Check(Construction.Ground(world,x,z)==TerrainData.Height(world,x,z),"construction uses shared authority");
            }
            var left=TerrainData.SampleRegion(world,new(-2,1),1);
            var right=TerrainData.SampleRegion(world,new(-1,1),1);
            for(int z=0;z<=16;z++)Check(left[16,z]==right[0,z],"shared border samples match after editing");
            var fine=TerrainData.SampleRegion(world,new(-1,1),.5f);
            for(int x=0;x<=16;x++)for(int z=0;z<=16;z++)Check(right[x,z]==fine[x*2,z*2],"display sampling does not change terrain authority");
            var sample=TerrainData.Query(world,-16,16);
            Check(Math.Abs(sample.Normal.Length()-1)<1e-6&&sample.Normal.Y>0,"finite normalized upward normal");
            Check(sample.SlopeDegrees is >=0 and <=90&&sample.Moisture is >=0 and <=1&&sample.Fertility is >=0 and <=1,"bounded classification queries");
            Check(sample.Delta==sample.Height-sample.BaseHeight,"base and modification remain separate");
            Check(JsonSerializer.Serialize(world)==before,"queries never mutate saved world");
            string path=Path.Combine(Path.GetTempPath(),"thornwake-terrain-"+Guid.NewGuid()+".json");
            try {SaveStore.Save(path,world);var loaded=SaveStore.Load(path);Check(TerrainData.Height(loaded,-15.37f,16.72f)==TerrainData.Height(world,-15.37f,16.72f),"exact modified height after save/reload");}
            finally {if(File.Exists(path))File.Delete(path);}
        }
        var dirty=TerrainData.Affected(16,16,3).ToHashSet();
        Check(dirty.SetEquals(new[]{new TerrainRegion(0,0),new(0,1),new(1,0),new(1,1)}),"corner edit invalidates both sides of both borders");
        Check(TerrainData.Affected(8,8,2).Count()==1,"interior edit stays in one region");
        Check(TerrainData.Affected(15.5f,8,.25f).Count()==2,"normal halo updates neighbour");
        Check(TerrainRegion.At(-.1f,-16.1f)==new TerrainRegion(-1,-2),"negative coordinates use floor, not truncation");
        bool rejected=false;try{TerrainData.SampleRegion(new WorldState(),new(0,0),.7f);}catch(ArgumentOutOfRangeException){rejected=true;}
        Check(rejected,"invalid sample interval rejected");
        return checks;
    }
}