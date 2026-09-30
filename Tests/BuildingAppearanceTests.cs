using System.Text.Json;
using Thornwake.Core;
internal static class BuildingAppearanceTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("APPEARANCES: "+why);checks++;}
        void Reject(Action action,string why){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("APPEARANCES: "+why);}
        string Json(object o)=>JsonSerializer.Serialize(o);
        var state=new WorldState{Seed=7319};foreach(var r in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(r.Id);
        foreach(var id in catalog.Items.Keys)state.Player.Inventory[id]=100;
        state.Player.EquippedItem="hammer";state.Player.X=-16;state.Player.Z=22;
        var sim=new Simulation(state,catalog);
        Check(sim.Build("foundation",-15,23).Success,"foundation");
        Check(sim.Build("wall",-15,21.5f).Success,"legacy wall");var wall=state.Structures[^1];
        var selected=BuildingVariants.Default(catalog.BuildingTools["roof"]);
        Check(sim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,selected).Success,"roof");var roof=state.Structures[^1];
        var inventory=Json(state.Player.Inventory);var paid=Json(roof.PaidCosts);var refund=Json(sim.ReclaimYield(roof));
        var originalSelection=roof.Selection;var original=Json(roof);var preview=sim.PreviewBuild("roof_module",-15,26,0,0,BuildSnapMode.Object,selected);
        foreach(var finish in catalog.BuildingTools["roof"].Appearances)
        {
            Check(sim.Refinish(roof.Id,finish.Id).Success,"apply "+finish.Id);
            Check(Json(state.Player.Inventory)==inventory&&Json(roof.PaidCosts)==paid&&Json(sim.ReclaimYield(roof))==refund,"cosmetic changes cannot grant or consume materials");
            Check(roof.Selection!.MaterialId=="timber"&&roof.Selection.Slope==45&&roof.SupportId!="","physical variant retained");
            var next=sim.PreviewBuild("roof_module",-15,26,0,0,BuildSnapMode.Object,selected);
            Check(next.X==preview.X&&next.Z==preview.Z&&next.Elevation==preview.Elevation&&next.Valid==preview.Valid&&next.SupportId==preview.SupportId,"snapping unchanged");
        }
        Check(originalSelection!.AppearanceId=="","refinish copies selection rather than mutating brushes");
        Check(!sim.Refinish(roof.Id,"stone").Success,"cross-family finish rejected");
        Check(!sim.Refinish(roof.Id,null!).Success,"null finish rejected");
        Check(!sim.Refinish("missing","clay").Success,"missing piece rejected");
        state.Player.EquippedItem="basic_axe";Check(!sim.Refinish(roof.Id,"clay").Success,"hammer required");state.Player.EquippedItem="hammer";
        state.Player.X=0;Check(!sim.Refinish(roof.Id,"clay").Success,"reach required");state.Player.X=-16;
        var wallBefore=Json(wall);var solid=Construction.SolidAt(state,wall,catalog.Buildings[wall.Kind],wall.X,wall.Z,Construction.Base(state,wall)+1,catalog:catalog);
        Check(sim.Refinish(wall.Id,"stone").Success,"old unconfigured walls support finishes");
        Check(Construction.SolidAt(state,wall,catalog.Buildings[wall.Kind],wall.X,wall.Z,Construction.Base(state,wall)+1,catalog:catalog)==solid,"wall collision unchanged");
        Check(sim.Refinish(roof.Id,"").Success&&Json(roof)==original,"Original exactly restores roof metadata");
        foreach(var tool in catalog.BuildingTools.Values)
        foreach(var variant in tool.Variants)
        foreach(var finish in tool.Appearances)
        {
            var choice=BuildingVariants.Default(tool);choice.VariantId=variant.Id;choice.Slope=variant.Slopes[0];
            var cost=Json(BuildingVariants.Resolve(catalog,choice).Costs);choice.AppearanceId=finish.Id;
            Check(Json(BuildingVariants.Resolve(catalog,choice).Costs)==cost,"all appearances have identical costs");
        }
        var mapping=catalog.BuildingTools["wall"].Appearances.First(a=>a.Id=="stone").Models[0];var oldPath=mapping.Scene;
        try{mapping.Scene="res://../outside.glb";Reject(()=>BuildingVariants.ValidateCatalog(catalog),"path traversal rejected");}
        finally{mapping.Scene=oldPath;}
        var appearance=catalog.BuildingTools["wall"].Appearances[2];appearance.Models.Add(mapping);
        try{Reject(()=>BuildingVariants.ValidateCatalog(catalog),"ambiguous mapping rejected");}finally{appearance.Models.RemoveAt(appearance.Models.Count-1);}
        var path=Path.Combine(Path.GetTempPath(),"thornwake-finishes-"+Guid.NewGuid()+".json");
        try
        {
            SaveStore.Save(path,state);var loaded=SaveStore.Load(path);_=new Simulation(loaded,catalog);
            Check(loaded.Structures.Single(s=>s.Id==wall.Id).Selection!.AppearanceId=="stone","finish survives save/reload");
            state.SaveVersion=7;File.WriteAllText(path,Json(state));var migrated=SaveStore.Load(path);
            Check(migrated.SaveVersion==10&&migrated.Structures.All(s=>s.Selection==null||s.Selection.AppearanceId==""),"v7 migration preserves original appearance");
            SaveStore.Save(path,migrated);Check(File.Exists(path+".v7"),"v7 save permanently backed up");
        }
        finally {foreach(var suffix in new[]{"",".bak",".v7"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
        return checks;
    }
}
