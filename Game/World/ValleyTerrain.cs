using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.World;

public enum TerrainDebugView { None, Regions, Delta, Slope, Material, Moisture }

public partial class Valley
{
    // Finite presentation extent; gameplay bounds remain owned by the current valley rules.
    private const int FirstTerrainRegion=-4,LastTerrainRegion=3;
    private const float TerrainMeshSpacing=1;
    private readonly Dictionary<TerrainRegion,MeshInstance3D> _terrainRegions=new();
    private readonly Dictionary<TerrainRegion,MultiMeshInstance3D> _grassRegions=new();
    private readonly Dictionary<TerrainRegion,List<(float X,float Z,float Scale,float Yaw)>> _grassCandidates=new();
    private StandardMaterial3D? _terrainMaterial;
    private Godot.Mesh? _grassMesh;
    public IReadOnlyDictionary<TerrainRegion,MeshInstance3D> TerrainRegions=>_terrainRegions;
    public int LastTerrainRegionsRebuilt {get;private set;}
    public TerrainDebugView TerrainDebug {get;private set;}

    public void RebuildTerrain()
    {
        if(_terrain==null){_terrain=new Node3D{Name="TerrainRegions"};AddChild(_terrain);}
        LastTerrainRegionsRebuilt=0;
        for(int x=FirstTerrainRegion;x<=LastTerrainRegion;x++)
            for(int z=FirstTerrainRegion;z<=LastTerrainRegion;z++){RebuildRegion(new(x,z));LastTerrainRegionsRebuilt++;}
        if(_decorated){AddGrass();Reground(-64,-64,64,64);}
    }
    public void RefreshTerrain(float x,float z,float radius)=>RefreshTerrain(new TerrainEdit{X=x,Z=z,Radius=radius});
    public void RefreshTerrain(TerrainEdit edit)
    {
        LastTerrainRegionsRebuilt=0;
        foreach(var region in TerrainData.Affected(edit.X,edit.Z,edit.Radius))
        {
            if(!_terrainRegions.ContainsKey(region))continue;
            RebuildRegion(region);RebuildGrassRegion(region);LastTerrainRegionsRebuilt++;
        }
        Reground(edit.X-edit.Radius,edit.Z-edit.Radius,edit.X+edit.Radius,edit.Z+edit.Radius);
    }
    private void Reground(float minX,float minZ,float maxX,float maxZ)
    {
        void Update(Node3D node)
        {
            var p=node.Position;
            if(p.X>=minX&&p.X<=maxX&&p.Z>=minZ&&p.Z<=maxZ)node.Position=At(p.X,p.Z);
        }
        foreach(var node in Resources.Values)Update(node);
        foreach(var node in _stumps.Values)Update(node);
        foreach(var node in Planted.Values)Update(node);
    }
    private void RebuildRegion(TerrainRegion region)
    {
        if(_terrainRegions.Remove(region,out var old)){_terrain.RemoveChild(old);old.QueueFree();}
        _terrainMaterial??=new StandardMaterial3D{VertexColorUseAsAlbedo=true,VertexColorIsSrgb=true,Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled};
        var heights=TerrainData.SampleRegion(_state,region,TerrainMeshSpacing);
        int count=heights.GetLength(0)-1;
        using var surface=new SurfaceTool();surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        // The same global-coordinate normal stencil is used on both sides of every region edge.
        var normals=new Vector3[count+1,count+1];var colors=new Color[count+1,count+1];
        for(int x=0;x<=count;x++)for(int z=0;z<=count;z++)
        {
            float wx=region.MinX+x*TerrainMeshSpacing,wz=region.MinZ+z*TerrainMeshSpacing;
            var n=TerrainData.Normal(_state,wx,wz);normals[x,z]=new Vector3(n.X,n.Y,n.Z);
            colors[x,z]=TerrainColor(region,wx,wz);
        }
        int[] ix={0,1,0,1,1,0},iz={0,0,1,0,1,1};
        for(int x=0;x<count;x++)for(int z=0;z<count;z++)for(int i=0;i<6;i++)
        {
            int xx=x+ix[i],zz=z+iz[i];
            surface.SetNormal(normals[xx,zz]);surface.SetColor(colors[xx,zz]);
            surface.AddVertex(new Vector3(region.MinX+xx*TerrainMeshSpacing,heights[xx,zz],region.MinZ+zz*TerrainMeshSpacing));
        }
        var mesh=new MeshInstance3D{Name=$"Region_{region.X}_{region.Z}",Mesh=surface.Commit(),MaterialOverride=_terrainMaterial};
        _terrain.AddChild(mesh);_terrainRegions[region]=mesh;
    }
    private Color TerrainColor(TerrainRegion region,float x,float z)
    {
        if(TerrainDebug==TerrainDebugView.Regions)return ((region.X+region.Z)&1)==0?new Color("548d9a"):new Color("c2a35e");
        if(TerrainDebug!=TerrainDebugView.None)
        {
            var sample=TerrainData.Query(_state,x,z);
            return TerrainDebug switch
            {
                TerrainDebugView.Delta=>new Color("797c70").Lerp(sample.Delta>=0?new Color("dfaa47"):new Color("486fd0"),Mathf.Clamp(Mathf.Abs(sample.Delta)/2,0,1)),
                TerrainDebugView.Slope=>new Color("476c3d").Lerp(new Color("d75142"),sample.SlopeDegrees/60),
                TerrainDebugView.Material=>sample.Surface switch {TerrainSurface.Sand=>new Color("cbb984"),TerrainSurface.Gravel=>new Color("939087"),TerrainSurface.Rock=>new Color("59606c"),_=>new Color("806147")},
                _=>new Color("b39562").Lerp(new Color("3b74b5"),sample.Moisture)
            };
        }
        if(_state.GeneratorVersion==2&&!TerrainData.Worked(_state,x,z))
        {
            var sample=TerrainData.Query(_state,x,z);
            if(Mathf.Abs(x-27)<5)return new Color("827e61");
            var ground=new Color("566b3f").Lerp(new Color("7b8059"),Mathf.Clamp(sample.Height/9,0,1));
            if(sample.SlopeDegrees>30)ground=ground.Lerp(new Color("77766a"),Mathf.Clamp((sample.SlopeDegrees-30)/20,0,1));
            return ground;
        }
        float hue=Mathf.Sin(x*.34f+_state.Seed)*Mathf.Cos(z*.27f)*.045f;
        if(TerrainData.Worked(_state,x,z))return new Color("65543b");
        return Mathf.Abs(x-27)<5?new Color("827e61"):new Color(.30f+hue,.365f+hue,.235f+hue);
    }
    public string CycleTerrainDebug()
    {
        TerrainDebug=(TerrainDebugView)(((int)TerrainDebug+1)%Enum.GetValues<TerrainDebugView>().Length);
        LastTerrainRegionsRebuilt=0;
        foreach(var region in _terrainRegions.Keys.ToArray()){RebuildRegion(region);LastTerrainRegionsRebuilt++;}
        foreach(var grass in _grassRegions.Values)grass.Visible=TerrainDebug==TerrainDebugView.None;
        return TerrainDebug==TerrainDebugView.None?"Terrain view: normal":$"Terrain inspection: {TerrainDebug} · F9 next view";
    }
    private void PrepareGrassCandidates()
    {
        if(_grassCandidates.Count>0)return;
        var random=new Random(_state.Seed^5971);
        for(int i=0;i<8500;i++)
        {
            float x=(float)random.NextDouble()*106-53,z=(float)random.NextDouble()*106-53;
            float scale=.6f+(float)random.NextDouble()*.9f,yaw=(float)random.NextDouble()*Mathf.Tau;
            if(Mathf.Abs(x)<2.1f||Mathf.Abs(z)<2.3f||Mathf.Abs(x-27)<5.5f||x>15&&x<25&&z>5&&z<16)continue;
            var region=TerrainRegion.At(x,z);
            if(!_grassCandidates.TryGetValue(region,out var candidates))_grassCandidates[region]=candidates=new();
            candidates.Add((x,z,scale,yaw));
        }
        _grassMesh=Visuals.GrassMesh();
    }
    private void AddGrass()
    {
        PrepareGrassCandidates();
        foreach(var region in _terrainRegions.Keys)RebuildGrassRegion(region);
    }
    private void RebuildGrassRegion(TerrainRegion region)
    {
        PrepareGrassCandidates();
        if(_grassRegions.Remove(region,out var old)){_terrain.RemoveChild(old);old.QueueFree();}
        if(!_grassCandidates.TryGetValue(region,out var candidates))return;
        var transforms=new List<Transform3D>();
        foreach(var grass in candidates)
        {
            float x=grass.X,z=grass.Z;
            if(Obstacles.Any(o=>new Vector2(x-o.Position.X,z-o.Position.Z).Length()<o.Radius+1))continue;
            if(TerrainData.Worked(_state,x,z))continue;
            if(_state.Structures.Any(s=>Construction.IsFloor(s)&&s.Level==0&&Construction.OnFloor(s,x,z)))continue;
            transforms.Add(new Transform3D(new Basis(Vector3.Up,grass.Yaw).Scaled(Vector3.One*grass.Scale),At(x,z)));
        }
        var multi=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=_grassMesh,InstanceCount=transforms.Count};
        for(int i=0;i<transforms.Count;i++)multi.SetInstanceTransform(i,transforms[i]);
        var node=new MultiMeshInstance3D{Name=$"Grass_{region.X}_{region.Z}",Multimesh=multi,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,Visible=TerrainDebug==TerrainDebugView.None};
        _terrain.AddChild(node);_grassRegions[region]=node;
    }
}