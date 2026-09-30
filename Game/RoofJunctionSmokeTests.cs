using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyRoofJunctions()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception("Roof junction runtime: "+message);}
        var previous=_state;
        var selectionBefore=_buildSelection?.Copy();string kindBefore=_buildKind;
        float rotationBefore=_rotation;int heightBefore=_buildHeightOffset;
        var anchorBefore=_heightAnchor;var snapBefore=_snapMode;bool reclaimBefore=_dismantling;
        bool inventoryBefore=_hud.IsInventoryOpen,craftBefore=_hud.IsWorkshopOpen,buildBefore=_hud.IsBuildingOpen;
        string stationBefore=_hud.ActiveCraftStation,screenBefore=_hud.IsPlayerScreenOpen?_hud.ActivePlayerScreen:"";
        _hud.HidePanel();_buildHeightOffset=0;_heightAnchor=null;_snapMode=BuildSnapMode.Object;_dismantling=false;

        BuildSelection Choice(string variant,float pitch,bool mirror=false,string material="timber")=>new()
        {DefinitionId=variant is "gable_fill" or "gable_ridge_fill"?"wall":"roof",VariantId=variant,Slope=pitch,MaterialId=material,Mirrored=mirror};
        void Stand(float x,float z)
        {_state.Player.X=x;_state.Player.Z=z;_state.Player.Y=_valley.WalkHeight(x,z);_player.Position=new(x,_state.Player.Y.Value,z);}
        WorldState Fixture()
        {
            var world=new WorldState{Seed=7319,Time=75,CalendarMinutes=720,EnemyDefeated=true};
            foreach(string item in _catalog.Items.Keys)world.Player.Inventory[item]=500;
            foreach(var resource in WorldGenerator.Generate(world.Seed))
            {world.RemovedResources.Add(resource.Id);if(resource.Kind=="tree")world.RemovedStumps.Add(resource.Id);}
            var simulation=new Simulation(world,_catalog);
            void Piece(string kind,float x,float z,float angle=0)
            {
                world.Player.X=x+4;world.Player.Z=z;world.Player.Y=null;
                var result=simulation.Build(kind,x,z,angle);Check(result.Success,"fixture "+kind+": "+result.Message);
            }
            // The highest floor starts the frame so every adjoining piece shares a safe deck elevation.
            Piece("foundation",-12,26);
            foreach(float z in new[]{26f,23f,20f})foreach(float x in new[]{-12f,-15f,-18f})if(x!=-12||z!=26)Piece("foundation",x,z);
            Piece("wall",-16.5f,20,Mathf.Pi/2);
            return world;
        }
        StructureState Place(BuildSelection selection,float x,float z,float angle=0)
        {
            Stand(x+4,z);var result=_sim.Build("roof_module",x,z,angle,0,BuildSnapMode.Object,selection);
            Check(result.Success,$"{selection.VariantId} {selection.Slope} degrees: {result.Message}");return _state.Structures[^1];
        }
        IEnumerable<Vector3> Vertices(Node3D root)
        {
            var vertices=new List<Vector3>();
            void Read(Node node,Transform3D transform)
            {
                foreach(var child in node.GetChildren())
                {
                    var childTransform=child is Node3D spatial?transform*spatial.Transform:transform;
                    if(child is MeshInstance3D mesh&&mesh.Mesh!=null)
                    {
                        for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                        foreach(var vertex in mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array())vertices.Add(childTransform*vertex);
                    }
                    Read(child,childTransform);
                }
            }
            Read(root,Transform3D.Identity);return vertices;
        }
        void InspectCreaseMesh(StructureState piece)
        {
            var mesh=_valley.StructureNodes[piece.Id].GetChildren().OfType<MeshInstance3D>().FirstOrDefault(m=>m.Mesh is ArrayMesh);
            Check(mesh?.Mesh!=null,"folded roof has a batched surface mesh");
            var arrays=mesh!.Mesh!.SurfaceGetArrays(0);
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            string geometry=BuildingVariants.Variant(_catalog,piece.Selection!).Geometry;int creaseVertices=0;
            var topNormals=new HashSet<(int X,int Y,int Z)>();
            for(int i=0;i<vertices.Length;i++)
            {
                var point=vertices[i];float height=RoofGeometry.SurfaceHeight(geometry,piece.Selection!.Slope,point.X,point.Z);
                float offset=point.Y-height;
                Check(Mathf.Abs(offset)<.001f||Mathf.Abs(offset+.10f)<.001f||Mathf.Abs(offset-.012f)<.001f||Mathf.Abs(offset-.014f)<.001f,"rendered corner vertex stays on a sampled roof layer");
                if(Mathf.Abs(point.X-point.Z)<.001f&&offset>=-.001f&&offset<.02f)creaseVertices++;
                if(offset>=-.001f&&offset<.02f)
                {
                    Check(normals[i].Y>.05f,"roof surface normal faces upward");
                    topNormals.Add(((int)Mathf.Round(normals[i].X*1000),(int)Mathf.Round(normals[i].Y*1000),(int)Mathf.Round(normals[i].Z*1000)));
                }
            }
            Check(creaseVertices>10&&topNormals.Count>=2,"shingle surfaces explicitly split at the sharp diagonal crease");
            // No single triangle may bridge the fold: all its vertices lie on one half-plane.
            for(int i=0;i+2<vertices.Length;i+=3)
            {
                float a=vertices[i].X-vertices[i].Z,b=vertices[i+1].X-vertices[i+1].Z,c=vertices[i+2].X-vertices[i+2].Z;
                Check(!(Math.Min(a,Math.Min(b,c))<-.001f&&Math.Max(a,Math.Max(b,c))>.001f),"triangles never span both folded roof planes");
            }
        }
        void InspectGableNormals(StructureState piece)
        {
            var mesh=_valley.StructureNodes[piece.Id].GetChildren().OfType<MeshInstance3D>().First(m=>m.Mesh is ArrayMesh);
            var arrays=mesh.Mesh!.SurfaceGetArrays(0);
            var vertices=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            float centerX=piece.Selection!.Mirrored?1.5f:-1.5f;
            for(int i=0;i+2<vertices.Length;i+=3)
            {
                var a=vertices[i];var b=vertices[i+1];var c=vertices[i+2];
                if(Mathf.Abs(a.X-b.X)<.0001f&&Mathf.Abs(a.X-c.X)<.0001f)
                    Check(normals[i].X*Mathf.Sign(a.X-centerX)>.9f,"gable plaster and timber face normals point outward");
                else if(Mathf.Abs(a.Z-b.Z)<.0001f&&Mathf.Abs(a.Z-c.Z)<.0001f)
                    Check(normals[i].Z*Mathf.Sign(a.Z)>.9f,"gable end face normals point outward");
                else if(Mathf.Abs(a.Y-b.Y)<.0001f&&Mathf.Abs(a.Y-c.Y)<.0001f)
                    Check(normals[i].Y<-.9f,"gable bottom face normal points downward");
                else Check(normals[i].Y>.1f,"gable sloping top normal points upward");
            }
        }
        WorldState Junction(string variant,float pitch,string material)
        {
            CreateWorld(Fixture());bool hip=variant=="hip";
            var first=Place(Choice("slope",pitch,material:material),hip?-12:-18,23);
            var corner=Place(Choice(variant,pitch,material:material),-15,23);
            var last=Place(Choice("slope",pitch,material:material),-15,hip?26:20,Mathf.Pi/2);
            var fill=Place(Choice("gable_fill",pitch,mirror:hip),first.X,first.Z);
            _valley.RebuildStructures();
            Check(corner.RoofConnectionId==first.Id&&last.RoofConnectionId==corner.Id,"L-shaped junction retains its support chain");
            Check(RoofGeometry.Connected(_catalog,corner,first)&&RoofGeometry.Connected(_catalog,last,corner),"both perpendicular roof edges share their full height profile");
            InspectCreaseMesh(corner);
            InspectGableNormals(fill);
            Check(Vertices(_valley.StructureNodes[fill.Id]).Any(),"wall-tool gable fill produces a visible mesh");
            _valley.UpdateShelterVisibility(new Vector3(first.X,_valley.WalkHeight(first.X,first.Z),first.Z));
            Check(!_valley.StructureNodes[first.Id].Visible&&!_valley.StructureNodes[fill.Id].Visible,"gable follows its parent's interior cutaway");
            _valley.UpdateShelterVisibility(new Vector3(-24,0,32));
            Check(_valley.StructureNodes[first.Id].Visible&&_valley.StructureNodes[fill.Id].Visible,"roof and gable return together outside");
            return _state;
        }

        var hipDemo=Junction("hip",22.5f,"timber");
        var valleyDemo=Junction("valley",60,"slate");

        CreateWorld(Fixture());
        var slope=Place(Choice("slope",45),-15,20);
        var ridge=Place(Choice("ridge",45),-15,23);
        Place(Choice("slope",45),-15,26,Mathf.Pi);
        Place(Choice("gable_fill",45),-15,20);
        Place(Choice("gable_fill",45,true),-15,20);
        var left=Place(Choice("gable_ridge_fill",45),-15,23);
        var right=Place(Choice("gable_ridge_fill",45,true),-15,23);
        Place(Choice("gable_fill",45),-15,26,Mathf.Pi);
        Place(Choice("gable_fill",45,true),-15,26,Mathf.Pi);
        _valley.RebuildStructures();
        foreach(var fill in new[]{left,right})
        {
            InspectGableNormals(fill);
            float bottom=RoofGeometry.GableBottom(_state,fill),baseY=Construction.Base(_state,fill);
            Check(bottom< -2.9f,"raised ridge fill extends below its roof base");
            var edge=Construction.World(fill,fill.Selection!.Mirrored?1.5f:-1.5f,0);
            float gapY=baseY+bottom+.5f;
            Check(!_valley.CanWalk(new Vector3(edge.X,gapY,edge.Z)),"runtime movement blocks the former rectangular gap below a raised ridge");
            Check(_valley.CanWalk(new Vector3(fill.X,gapY,fill.Z)),"gable collision leaves the room interior clear");
            Stand(fill.X,fill.Z);float side=fill.Selection.Mirrored?1:-1;
            var picked=RaycastStructure(new Vector3(edge.X+side*2.25f,gapY,edge.Z),new Vector3(-side,0,0));
            Check(picked?.Id==fill.Id,"dismantling ray selects the lowered gable panel from its exposed side");
            float renderedBottom=Vertices(_valley.StructureNodes[fill.Id]).Min(p=>p.Y);
            Check(Mathf.Abs(renderedBottom-bottom)<.035f,"placed gable mesh reaches the collision bottom");
            EnsureGhost(fill.Kind,fill.Selection,bottom);
            Check(Mathf.Abs(Vertices(_ghost).Min(p=>p.Y)-bottom)<.035f,"placement ghost reaches the same lowered bottom");
        }
        Stand(-15,23);Check(!_sim.Dismantle(ridge.Id).Success,"roof cannot be removed before its gable fills and connected sections");
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        foreach(string id in new[]{left.Id,right.Id})
        {
            var fill=_state.Structures.Single(p=>p.Id==id);float bottom=RoofGeometry.GableBottom(_state,fill);
            Check(bottom< -2.9f&&Mathf.Abs(Vertices(_valley.StructureNodes[id]).Min(v=>v.Y)-bottom)<.035f,"saved raised gable reconstructs the full lowered wall");
        }
        var gableDemo=_state;

        WorldState chosen=_view=="junctions"?hipDemo:_view=="valley"?valleyDemo:_view=="gables"?gableDemo:previous;
        if(!ReferenceEquals(_state,chosen))CreateWorld(chosen);
        _buildSelection=selectionBefore;_buildKind=kindBefore;_rotation=rotationBefore;_buildHeightOffset=heightBefore;_heightAnchor=anchorBefore;_snapMode=snapBefore;_dismantling=reclaimBefore;
        if(_view is "junctions" or "valley" or "gables")
        {
            SelectSlot(0);_buildSelection=null;Stand(-22,29);
            _cameraRig.Orbit.Reset();_cameraRig.Orbit.Look(170,-32,1,false);_cameraRig.Orbit.Zoom(-2);
            for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
            _valley.UpdateShelterVisibility(_player.Position);
        }
        else
        {
            if(_slot==1)UpdateGhost();
            if(inventoryBefore)_hud.ShowInventory(_state,_catalog);
            else if(craftBefore)_hud.ShowCrafting(_state,_catalog,stationBefore);
            else if(buildBefore)_hud.ShowBuilding(_state,_catalog);
            else if(screenBefore!="")ShowPlayerScreen(screenBefore);
        }
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        GD.Print("ROOF_JUNCTION_TEST_PASS: rendered hip/valley crease triangles and normals, shallow timber and steep slate L joins, gable cutaways, raised ridge wall collision/picking/ghost bottom, protected parent and save reconstruction");
    }
}
