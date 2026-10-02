using System.Text.Json;
using Thornwake.Core;

try
{
if (args.Contains("--topology-analytic")) { HydrologyTopologyTests.Run(verify:false,includeWorlds:false); return; }
if (args.Contains("--adaptive-topology")) { HydrologyTopologyTests.Run(); return; }
if (args.Contains("--topology-spike")) { HydrologyTopologyTests.Run(verify:false); return; }
if (args.Contains("--reconciliation-spike")) { HydrologyReconciliationTests.Run(verify:false); return; }
if (args.Contains("--record-reconciliation")) { HydrologyReconciliationTests.Run(record:true); return; }
if (args.Contains("--reconciliation")) { HydrologyReconciliationTests.Run(); return; }
if (args.Contains("--record-hydrology-inspection")) { HydrologyInspectionTests.Run(record:true); return; }
if (args.Contains("--hydrology-inspection")) { HydrologyInspectionTests.Run(); return; }
if (args.Contains("--record-a4-acceptance")) { AcceptedWorldTests.Record(); return; }
if (args.Contains("--accepted-a4")) { AcceptedWorldTests.Run(); return; }
if (args.Contains("--mountain-v4")) { MacroMountainsV4Tests.Run(); return; }
if (args.Contains("--cave-benchmark")) { CaveSpikeTests.Benchmark(Path.Combine("artifacts", "cave-mesher-benchmark.json")); return; }
if (args.Contains("--drainage-spike")) { DrainageTests.Run(Path.Combine("artifacts", "drainage-spike.json")); return; }
if (args.Contains("--drainage-preview")) { DrainagePreviewTests.Run(); return; }
if (args.Contains("--regional-drainage")) { RegionalDrainageTests.Run(Path.Combine("artifacts", "regional-drainage.json")); return; }
var catalog = ContentCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Content"));
int passed = 0;
passed += CameraTests.Run();
passed += ConstructionTests.Run(catalog);
passed += AdvancedConstructionTests.Run(catalog);
passed += SnappingTests.Run(catalog);
passed += BuildingVariantTests.Run(catalog);
passed += BuildingAppearanceTests.Run(catalog);
passed += CraftingTests.Run(catalog);
passed += CraftActivityTests.Run(catalog);
passed += CraftingQueueTests.Run(catalog);
passed += HotbarTests.Run(catalog);
passed += RoofJunctionTests.Run(catalog);
passed += VegetationSandboxTests.Run(catalog);
passed += WoodlandTests.Run(catalog);
passed += TerrainDataTests.Run();
passed += EarthworkTests.Run(catalog);
passed += LandscapeTests.Run(catalog);
passed += WorldStreamingTests.Run();
passed += MacroWorldTests.Run();
passed += MacroMountainsV4Tests.Run();
passed += AcceptedWorldTests.Run();
passed += HydrologyInspectionTests.Run();
passed += HydrologyReconciliationTests.Run();
passed += HydrologyTopologyTests.Run();
passed += MacroChunkTests.Run();
passed += DrainageTests.Run();
passed += DrainagePreviewTests.Run();
passed += RegionalDrainageTests.Run();
passed += CaveSpikeTests.Run();
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAILED: " + label);
    passed++;
}
void Reject(Action action, string label)
{
    try { action(); }
    catch (InvalidDataException) { passed++; return; }
    throw new Exception("FAILED: " + label);
}

Check(catalog.Items.ContainsKey("hide") && catalog.Recipes.ContainsKey("flint_blade"), "content resolves");
var missingContent=new WorldState();
missingContent.Structures.Add(new StructureState{Id="unknown-1",Kind="missing_mod_building"});
Reject(()=>new Simulation(missingContent,catalog),"unavailable save content rejected before rendering");
for (int seed = -50; seed < 50; seed++)
{
    var first = WorldGenerator.Generate(seed);
    var second = WorldGenerator.Generate(seed);
    Check(first.SequenceEqual(second), $"determinism seed {seed}");
    Check(first.Count == 170, $"resource count seed {seed}");
    Check(first.Select(x => x.Id).Distinct().Count() == first.Count, "unique resource IDs");
    foreach (var resource in first)
    {
        Check(Math.Abs(resource.X) <= 55 && Math.Abs(resource.Z) <= 55, "world bounds");
        Check(!(Math.Abs(resource.X) < 17 && Math.Abs(resource.Z) < 16), "village clearance");
        Check(Math.Abs(resource.X - 27) >= 5 && Math.Abs(resource.X) >= 3 && Math.Abs(resource.Z) >= 3, "river and path clearance");
        Check(!(resource.X >= 16 && resource.X <= 24 && resource.Z >= 5 && resource.Z <= 15), "village farm clearance");
        Check(MathF.Pow(resource.X + 29, 2) + MathF.Pow(resource.Z + 23, 2) >= 36, "bandit camp clearance");
        Check(float.IsFinite(WorldGenerator.Height(resource.X, resource.Z, seed)), "finite terrain");
    }
    for (int i = 0; i < first.Count; i++)
        for (int j = i + 1; j < first.Count; j++)
            Check(MathF.Pow(first[i].X - first[j].X, 2) + MathF.Pow(first[i].Z - first[j].Z, 2) >= 12.25f, "resource spacing");
}
Check(!WorldGenerator.Generate(1).SequenceEqual(WorldGenerator.Generate(2)), "distinct seeds");

var state = new WorldState();
var sim = new Simulation(state, catalog);
var inventoryBefore = JsonSerializer.Serialize(state.Player.Inventory);
Check(!sim.Craft("flint_blade").Success, "craft insufficient materials");
Check(inventoryBefore == JsonSerializer.Serialize(state.Player.Inventory), "failed crafting atomicity");
Check(sim.Craft("stone_axe").Success && sim.Count("axe") == 0 && sim.Count("wood") == 3 && sim.Count("stone") == 2, "craft reserves costs without instant output");
sim.TickCraft(catalog.Recipes["stone_axe"].Duration);Check(sim.Count("axe")==1,"craft completes after duration");
Check(!sim.Craft("unknown").Success, "unknown recipe rejected");
inventoryBefore = JsonSerializer.Serialize(state.Player.Inventory);
state.Player.X=20;state.Player.Z=18;
foreach(var r in WorldGenerator.Generate(state.Seed).Where(r=>Construction.Distance(r.X,r.Z,20,20)<5))state.RemovedResources.Add(r.Id);
Check(!sim.Build("foundation", 20, 20).Success, "building insufficient materials");
Check(inventoryBefore == JsonSerializer.Serialize(state.Player.Inventory) && state.Structures.Count == 0, "failed building atomicity");
Check(sim.Build("fence", 20, 20).Success, "build succeeds");
state.Player.Inventory["wood"] = 20;
Check(!sim.Build("fence", 21, 20).Success, "building collision rejected");
Check(!sim.Build("wall", 55, 20).Success && !sim.Build("wall", float.NaN, 0).Success, "building boundaries");
Check(!sim.Build("wall", 27, 20).Success, "river placement rejected");

state.Player.X=0;state.Player.Z=8;
var resourceSpawn = WorldGenerator.Generate(state.Seed).First(r=>!state.RemovedResources.Contains(r.Id));
Check(sim.Gather(resourceSpawn.Id, resourceSpawn.Kind).Success, "gather generated resource");
Check(!sim.Gather(resourceSpawn.Id, resourceSpawn.Kind).Success, "double gathering rejected");
Check(!sim.Gather("imaginary", "tree").Success, "fabricated resource rejected");
Check(!sim.Plant(-20, 20).Success, "plant requires soil");
Check(sim.ModifyTerrain(-20, 20, -.2f).Success && sim.Plant(-20, 20).Success, "soil and planting");
Check(!sim.Plant(-20, 20).Success, "crop collision");
var cropId = state.Crops[0].Id;
Check(!sim.Harvest(cropId).Success, "immature crop rejected");
sim.Tick(60);
Check(sim.Harvest(cropId).Success && state.Crops.Count == 0, "crop harvest maturity");
Check(!sim.Harvest(cropId).Success, "double harvest rejected");
Check(sim.Plant(-20, 20).Success, "replanting");

state.Player.Inventory["coins"] = 0;
inventoryBefore = JsonSerializer.Serialize(state.Player.Inventory);
Check(!sim.Trade("wood", true).Success && inventoryBefore == JsonSerializer.Serialize(state.Player.Inventory), "trade insufficient funds atomicity");
Check(!sim.Trade("sword", false).Success && !sim.Trade("coins", false).Success, "trade bounds");
state.Player.Inventory["seeds"] = 0;
Check(!sim.Trade("seeds", false).Success && sim.Count("seeds") == 0, "cannot sell absent seeds");
state.Player.Inventory["coins"] = 3;
Check(sim.Trade("seeds", true).Success && sim.Count("seeds") == 1 && sim.Count("coins") == 0, "buy seed spends exact balance");
Check(!sim.Trade("seeds", true).Success && sim.Count("coins") == 0, "buy seed cannot overdraw");
Check(sim.Trade("wood", false).Success && sim.Count("coins") == 2, "selling earns crowns");
state.Player.Inventory["coins"] = 100;
state.Settlement.Food = 0;
Check(sim.Price("berries", true) == 4 && sim.Price("berries", false) == 3 && sim.Price("wood", true) == 3, "shortage pricing affects only food");
Check(!sim.Trade("berries", true).Success, "empty market stock");
Check(sim.Trade("berries", false).Success && state.Settlement.Food == 1, "selling supplies town");
state.Settlement.Food = 200;
Check(sim.Price("berries", true) == 3 && sim.Price("berries", false) == 2, "supplied food returns to baseline price");
Check(!sim.Trade("berries", false).Success, "full market stock");
state.Player.Health = 30;
Check(sim.Eat().Success && state.Player.Health > 30, "food restores health");
Check(sim.DefeatEnemy().Success && sim.Count("hide") == 1, "enemy reward");
Check(!sim.DefeatEnemy().Success && sim.Count("hide") == 1, "enemy reward cannot repeat");

var economyA = new WorldState { EnemyDefeated = true };
var economyB = new WorldState { EnemyDefeated = true };
var simA = new Simulation(economyA, catalog);
var simB = new Simulation(economyB, catalog);
simA.Tick(180);
for (int i = 0; i < 1800; i++) simB.Tick(.1);
// Frames may fall just short of exact floating point boundaries; cross by a negligible amount.
simB.Tick(0.00001);
Check(JsonSerializer.Serialize(economyA.Settlement) == JsonSerializer.Serialize(economyB.Settlement), "frame-independent economy");
Check(economyA.Settlement.CaravanArrivals == 2, "safe road caravan schedule");
var unsafeState = new WorldState();
new Simulation(unsafeState, catalog).Tick(180);
Check(unsafeState.Settlement.CaravanArrivals == 1, "wolf disrupts caravan schedule");

string saveFolder = Path.Combine(Path.GetTempPath(), "thornwake-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(saveFolder);
try
{
    string save = Path.Combine(saveFolder, "world.json");
    SaveStore.Save(save, state);
    var loaded = SaveStore.Load(save);
    Check(JsonSerializer.Serialize(state) == JsonSerializer.Serialize(loaded), "save preserves all world changes");
    state.Player.X = 1;
    SaveStore.Save(save, state);
    Check(File.Exists(save + ".bak") && SaveStore.Load(save + ".bak").Player.X == 0, "atomic save retains prior backup");
    Check(!File.Exists(save + ".tmp"), "temporary save removed");
    File.WriteAllText(save, "{broken");
    Reject(() => SaveStore.Load(save), "malformed save rejected");
    File.WriteAllText(save, "{}");
    Reject(() => SaveStore.Load(save), "missing save header rejected");
    state.SaveVersion = 42;
    File.WriteAllText(save, JsonSerializer.Serialize(state));
    Reject(() => SaveStore.Load(save), "future save version rejected");
    state.SaveVersion = 10;
    state.GeneratorVersion = 42;
    File.WriteAllText(save, JsonSerializer.Serialize(state));
    Reject(() => SaveStore.Load(save), "future world generator rejected");
    state.GeneratorVersion = 1;
    state.Player.Inventory["wood"] = -1;
    File.WriteAllText(save, JsonSerializer.Serialize(state));
    Reject(() => SaveStore.Load(save), "negative inventory rejected");
    state.Player.Inventory["wood"] = 1;
    state.Crops[0].PlantedAt = state.Time + 100;
    Reject(() => SaveStore.Save(save, state), "invalid crop time rejected on save");
    state.Crops[0].PlantedAt = state.Time;
    state.Player.X = float.NaN;
    Reject(() => SaveStore.Save(save, state), "invalid coordinate rejected on save");
}
finally
{
    // The path is a newly-created isolated test directory with a random name.
    Directory.Delete(saveFolder, recursive: true);
}

Console.WriteLine($"PASS — {passed:N0} assertions: 100 seeds, crafting, building, farming, trade, economy, and versioned atomic saves.");
}
catch(Exception error)
{
    Console.Error.WriteLine("TEST FAILURE: "+error);
    Environment.ExitCode=1;
}









