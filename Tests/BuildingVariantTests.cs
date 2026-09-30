using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class BuildingVariantTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception("BUILDING VARIANTS: "+message);checks++;}
        void Reject(Action action,string message){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("BUILDING VARIANTS: "+message);}
        bool Near(float a,float b)=>MathF.Abs(a-b)<.003f;
        WorldState Clone(WorldState s)=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(s))!;
        var roofTool=catalog.BuildingTools["roof"];var chosen=BuildingVariants.Default(roofTool);
        Check(chosen.Slope==45&&chosen.VariantId=="slope","roof defaults to the supported 45-degree slope");
        Check(catalog.BuildingTools["floor"].Variants.Count>=2&&catalog.BuildingTools["wall"].Variants.Count>=3,"variant model groups floors and walls as well as roofs");
        Check(roofTool.LegacyKinds.Contains("roof")&&!roofTool.Variants.Any(v=>v.Id=="connected"),"legacy flat alias does not duplicate a visible variant");
        var floorChoice=BuildingVariants.Default(catalog.BuildingTools["floor"]);
        Check(BuildingVariants.Resolve(catalog,floorChoice,0).Id=="foundation"&&BuildingVariants.Resolve(catalog,floorChoice,1).Id=="upper_floor","same floor selection resolves its upper-level counterpart");
        var definition=BuildingVariants.Resolve(catalog,chosen);definition.Costs["wood"]=999;
        Check(BuildingVariants.Resolve(catalog,chosen).Costs["wood"]==2,"effective definitions do not mutate shared content");
        var visualVariant=BuildingVariants.Variant(catalog,chosen);string previousMesh=visualVariant.Mesh;
        try{visualVariant.Mesh="missing_visual_asset";BuildingVariants.ValidateCatalog(catalog);Check(BuildingVariants.Resolve(catalog,chosen).Id=="roof_module","missing art override retains known physical geometry");}
        finally{visualVariant.Mesh=previousMesh;}
        string previousGeometry=visualVariant.Geometry;
        try{visualVariant.Geometry="unknown_physics";Reject(()=>BuildingVariants.ValidateCatalog(catalog),"unknown physical geometry is rejected");}
        finally{visualVariant.Geometry=previousGeometry;}

        foreach(float pitch in new[]{30f,45f})
        {
            float rise=3*MathF.Tan(pitch*MathF.PI/180);
            Check(Near(RoofGeometry.Rise("roof_slope",pitch),rise),"slope rise follows selected pitch");
            Check(Near(RoofGeometry.SurfaceHeight("roof_slope",pitch,0,-1.5f),.17f)&&Near(RoofGeometry.SurfaceHeight("roof_slope",pitch,0,1.5f),rise+.17f),"slope endpoints include shared skin baseline");
            Check(Near(RoofGeometry.SurfaceHeight("roof_ridge",pitch,0,0),rise/2+.17f)&&Near(RoofGeometry.SurfaceHeight("roof_ridge",pitch,0,1.5f),.17f),"ridge peak and eave match pitch");
        }
        var state=new WorldState{Seed=7319};foreach(var item in catalog.Items.Keys)state.Player.Inventory[item]=300;
        foreach(var resource in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(resource.Id);
        var sim=new Simulation(state,catalog);
        void Stand(float x,float z){state.Player.X=x+4;state.Player.Z=z;state.Player.Y=null;}
        StructureState Legacy(string kind,float x,float z,float rotation=0)
        {Stand(x,z);var result=sim.Build(kind,x,z,rotation);Check(result.Success,$"legacy {kind}: {result.Message}");return state.Structures[^1];}
        StructureState Selected(BuildSelection selection,float x,float z,float rotation=0,BuildSnapMode mode=BuildSnapMode.Object)
        {Stand(x,z);var result=sim.Build(BuildingVariants.Resolve(catalog,selection).Id,x,z,rotation,0,mode,selection);Check(result.Success,$"selected {selection.VariantId}: {result.Message}");return state.Structures[^1];}
        BuildSelection Roof(string variant,float slope=45,string material="timber",bool mirrored=false)=>new(){DefinitionId="roof",VariantId=variant,Slope=slope,MaterialId=material,Mirrored=mirrored};
        // Begin at the highest tile so all connected foundations meet this naturally shallow patch of soil.
        Legacy("foundation",-12,26);
        foreach(float z in new[]{26f,23f,20f})foreach(float x in new[]{-12f,-15f,-18f})if(x!=-12||z!=26)Legacy("foundation",x,z);
        var support=Legacy("wall",-16.5f,20,MathF.PI/2);
        var choice=Roof("slope",45,"slate");int plankBefore=sim.Count("plank"),woodBefore=sim.Count("wood"),stoneBefore=sim.Count("stone");
        var first=Selected(choice,-15,20);
        Check(sim.Count("plank")==plankBefore-3&&sim.Count("wood")==woodBefore-2&&sim.Count("stone")==stoneBefore-2,"variant and material costs charged together exactly once");
        Check(first.PaidCosts["stone"]==2&&first.Selection!.MaterialId=="slate","saved piece records finish and actual paid costs");
        choice.Slope=30;choice.MaterialId="timber";
        Check(first.Selection!.Slope==45&&first.Selection.MaterialId=="slate","changing current brush cannot mutate placed piece");
        var ridge=Selected(Roof("ridge"),-15,23);
        Check(Near(ridge.Elevation!.Value,first.Elevation!.Value+3)&&ridge.RoofConnectionId==first.Id,"ridge snap carries high-edge elevation");
        var opposite=Selected(Roof("slope"),-15,26,MathF.PI);
        Check(Near(opposite.Elevation!.Value,first.Elevation.Value)&&Near(opposite.Rotation,MathF.PI)&&opposite.RoofConnectionId==ridge.Id,"opposite slope keeps chosen rotation and returns to matching eave height");
        Check(RoofGeometry.Connected(catalog,ridge,first)&&RoofGeometry.Connected(catalog,opposite,ridge),"saved graph edges match complete semantic edge profiles");
        Check(!Construction.CanRemove(state,first,out _)&&!Construction.CanRemove(state,ridge,out _),"roof parents remain protected until connected children are removed");
        Check(!Construction.CanRemove(state,support,out _),"wall support still protects configurable roofs");
        var eave=Selected(Roof("eave"),-15,20);var left=Selected(Roof("gable_edge"),-15,20);var right=Selected(Roof("gable_edge",45,"timber",true),-15,20);
        Check(eave.RoofConnectionId==first.Id&&left.RoofConnectionId==first.Id&&right.RoofConnectionId==first.Id,"trim overlays bind to their compatible slope parent");
        Check(Near(eave.Elevation!.Value,first.Elevation.Value)&&Near(left.Elevation!.Value,first.Elevation.Value),"trim retains exact parent placement");
        var mirroredEdge=RoofGeometry.Edges("roof_gable_edge",45,true).Single();var normalEdge=RoofGeometry.Edges("roof_gable_edge",45,false).Single();
        Check(Near(mirroredEdge.Start.X,1.5f)&&Near(normalEdge.Start.X,-1.5f),"mirroring selects the opposite gable side");
        Stand(-15,20);string inventory=JsonSerializer.Serialize(state.Player.Inventory);
        Check(!sim.Build("roof_module",-15,20,0,0,BuildSnapMode.Object,Roof("eave")).Success&&inventory==JsonSerializer.Serialize(state.Player.Inventory),"duplicate overlay is rejected before charging");
        Check(!sim.Build("roof_module",-15,20,0,0,BuildSnapMode.Object,Roof("gable_edge",30)).Success,"trim cannot use the wrong parent pitch");
        Check(!sim.Build("roof_module",-15,20,MathF.PI/2,0,BuildSnapMode.Object,Roof("eave")).Success,"trim does not silently rotate to its parent");
        Stand(-12,20);
        Check(!sim.PreviewBuild("roof_module",-12,20,0,0,BuildSnapMode.Object,Roof("slope",30)).Valid,"object snapping refuses mismatched pitches");
        var freePose=sim.PreviewBuild("roof_module",-12.17f,20.21f,MathF.PI,0,BuildSnapMode.Free,Roof("slope",30));
        Check(Near(freePose.X,-12.17f)&&Near(freePose.Z,20.21f)&&Near(freePose.Rotation,MathF.PI),"free preview preserves chosen position and rotation even when physically rejected");
        foreach(var bad in new[]{Roof("missing"),Roof("slope",15),Roof("slope",45,"missing"),Roof("slope",45,"timber",true)})
        {inventory=JsonSerializer.Serialize(state.Player.Inventory);Check(!sim.Build("roof_module",-12,20,0,0,BuildSnapMode.Object,bad).Success&&inventory==JsonSerializer.Serialize(state.Player.Inventory),"unsupported selection cannot consume materials");}
        Stand(-18,20);int savedWood=state.Player.Inventory["wood"];state.Player.Inventory["wood"]=0;
        Check(!sim.Build("roof_module",-18,20,0,0,BuildSnapMode.Free,Roof("slope")).Success&&state.Player.Inventory["wood"]==0,"unaffordable variant is atomic");state.Player.Inventory["wood"]=savedWood;
        new Simulation(state,catalog);Check(true,"completed roof graph validates for reloading");

        // A ridge carried upward by its neighboring slope can cut an upper deck even though their bases differ.
        var crossed=Clone(state);crossed.Structures.RemoveAll(s=>s.Kind=="roof_module"&&s.Id!=first.Id);var crossingSim=new Simulation(crossed,catalog);
        crossed.Player.X=-11;crossed.Player.Z=23;crossed.Player.Y=null;
        Check(crossingSim.Build("upper_floor",-15,23,0,1).Success,"intermediate upper floor built below future raised ridge");
        Check(crossingSim.Build("wall",-16.5f,23,MathF.PI/2,1).Success,"upper room has a supporting wall");
        Check(crossingSim.Build("upper_floor",-15,23,0,2).Success,"second upper floor exists before roof placement");
        var piercing=crossingSim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge"));
        Check(!piercing.Valid&&piercing.Message.Contains("pitched roof profile"),"raised roof cannot pierce an upper deck at a different base height");
        crossed.Structures.RemoveAll(s=>s.Kind=="upper_floor"&&s.Level==2);
        Check(crossingSim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge")).Success,"roof is allowed with clearance above the lower room");
        var inverse=crossingSim.PreviewBuild("upper_floor",-15,23,0,2);
        Check(!inverse.Valid&&inverse.Message.Contains("pitched roof profile"),"upper deck cannot be placed through an existing raised roof");

        var touching=Clone(state);touching.Structures.RemoveAll(s=>s.Kind=="roof_module"&&s.Id!=first.Id);
        // A slab touching the eave from underneath is valid; only positive penetration is blocked.
        var parentFloor=touching.Structures.First(s=>s.Id==first.SupportId);
        touching.Structures.Add(new StructureState{Id="touching-slab",Kind="upper_floor",X=-15,Z=23,Level=2,Elevation=first.Elevation!.Value+3+.17f-.08f-Construction.Deck,Modular=true,SupportId=parentFloor.Id});
        touching.Player.X=-11;touching.Player.Z=23;touching.Player.Y=null;
        Check(new Simulation(touching,catalog).PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge")).Valid,"mere underside contact does not count as roof penetration");

        var lowPitch=Clone(state);lowPitch.Structures.RemoveAll(s=>s.Kind=="roof_module"&&s.Id!=first.Id);lowPitch.Structures.First(s=>s.Id==first.Id).Selection!.Slope=30;
        lowPitch.Player.X=-11;lowPitch.Player.Z=23;lowPitch.Player.Y=null;var lowSim=new Simulation(lowPitch,catalog);
        var thirty=lowSim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge",30));
        Check(thirty.Valid&&Near(thirty.Elevation,first.Elevation.Value+3*MathF.Tan(MathF.PI/6)),"30-degree roof connection carries its distinct rise");
        Check(lowSim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge",30)).Success,"30-degree ridge can be committed");
        lowPitch.Player.Z=26;
        var lowOpposite=lowSim.PreviewBuild("roof_module",-15,26,MathF.PI,0,BuildSnapMode.Object,Roof("slope",30));
        Check(lowOpposite.Valid&&Near(lowOpposite.Elevation,first.Elevation.Value),"30-degree opposing slope closes to the original eave height");
        var wallCrossing=Clone(crossed);wallCrossing.Structures.RemoveAll(s=>s.Kind=="roof_module"&&s.Id!=first.Id);wallCrossing.Structures.First(s=>s.Id==first.Id).Selection!.Slope=30;
        var acrossWall=new Simulation(wallCrossing,catalog).PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Roof("ridge",30));
        Check(!acrossWall.Valid&&acrossWall.Message.Contains("pitched roof profile"),"sloped roof cannot pass through an upper room wall");

        string directory=Path.Combine(Path.GetTempPath(),"thornwake-variants-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"new.json");SaveStore.Save(path,state);var loaded=SaveStore.Load(path);new Simulation(loaded,catalog);
            Check(JsonSerializer.Serialize(state)==JsonSerializer.Serialize(loaded),"schema5 roundtrip preserves selections, costs, poses and connection graph");
            var cyclic=Clone(state);cyclic.Structures.First(s=>s.Id==first.Id).RoofConnectionId=ridge.Id;
            Reject(()=>SaveStore.Save(path,cyclic),"cyclic roof dependency rejected before save replacement");
            var missing=Clone(state);missing.Structures.First(s=>s.Id==ridge.Id).RoofConnectionId="missing";
            Reject(()=>SaveStore.Save(path,missing),"missing roof parent rejected before save replacement");
            var invalidPitch=Clone(state);invalidPitch.Structures.First(s=>s.Id==ridge.Id).Selection!.Slope=30;
            Reject(()=>new Simulation(invalidPitch,catalog),"mismatched saved pitch connection rejected");
            var wrongShape=Clone(state);wrongShape.Structures.First(s=>s.Id==first.Id).Selection!.VariantId="missing";
            Reject(()=>new Simulation(wrongShape,catalog),"unknown saved variant rejected");
            var nonFinite=Clone(state);nonFinite.Structures.First(s=>s.Id==first.Id).Selection!.Slope=float.NaN;
            Reject(()=>SaveStore.Save(path,nonFinite),"nonfinite saved pitch rejected");
            var floating=Clone(state);floating.Structures.First(s=>s.Id==ridge.Id).RoofConnectionId="";
            Reject(()=>new Simulation(floating,catalog),"raised roof cannot lose height dependency");
            Check(SaveStore.Load(path).Structures.Count==state.Structures.Count,"failed saves leave prior world intact");

            var legacy=new WorldState{SaveVersion=4};legacy.Structures.Add(new StructureState{Id="old-slope",Kind="roof_slope",X=-20,Z=22,Rotation=.7853982f,Elevation=3.27f,Modular=true});
            var json=JsonNode.Parse(JsonSerializer.Serialize(legacy))!.AsObject();foreach(var s in json["Structures"]!.AsArray()){s!.AsObject().Remove("Selection");s.AsObject().Remove("RoofConnectionId");}
            string legacyPath=Path.Combine(directory,"legacy.json"),legacyBytes=json.ToJsonString();File.WriteAllText(legacyPath,legacyBytes);
            var migrated=SaveStore.Load(legacyPath);new Simulation(migrated,catalog);var old=migrated.Structures.Single();
            Check(migrated.SaveVersion==10&&old.Selection==null&&old.RoofConnectionId==""&&old.Kind=="roof_slope"&&Near(old.Rotation,.7853982f)&&Near(old.Elevation!.Value,3.27f),"schema4 migration preserves legacy shape and pose without converting geometry");
            SaveStore.Save(legacyPath,migrated);SaveStore.Save(legacyPath,migrated);
            Check(File.ReadAllText(legacyPath+".v4")==legacyBytes,"original v4 backup survives later autosaves");
        }
        finally{foreach(var file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        return checks;
    }
}
