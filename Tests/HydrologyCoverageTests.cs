using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyCoverageTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    internal sealed record Row(string World,string Size,int Patch,int CoarseCells,int Algorithm,double Milliseconds,
        TopologyStop Component,TopologyStop Spill,int ComponentCells,double? SpillElevation,TopologyMetrics Metrics,
        HydrologyWorkProfile Work,string[] FrontierReasons,string Fingerprint);
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static int Run(bool baselineOnly=false,bool verify=true,bool afterOnly=false)
    {
        int checks=0;void Check(bool value,string why){if(!value)throw new Exception("HYDROLOGY_COVERAGE: "+why);checks++;}
        var rows=new List<Row>();
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        foreach(string name in new[]{"mountainous/42","mountainous/42/far","vast-world/7319/far"})
        {
            var world=worlds.Cases.Single(c=>c.Name==name).World;var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(world));
            var basins=regional.Drainage.Basins;
            var small=basins.Where(b=>b.Cells.Count<=4).OrderByDescending(b=>b.CapacityEstimate).First();
            var singles=basins.Where(b=>b.Cells.Count==1 && b.CapacityEstimate>0).OrderBy(b=>b.CapacityEstimate).ToArray();
            var tiny=singles[singles.Length/2];
            var medium=basins.Where(b=>b.Cells.Count>4).OrderBy(b=>Math.Abs(b.Cells.Count-16)).ThenByDescending(b=>b.CapacityEstimate).First();
            var large=basins.OrderByDescending(b=>b.Cells.Count).ThenByDescending(b=>b.CapacityEstimate).First();
            foreach(var selection in new[]{(Size:"tiny",Basin:tiny),(Size:"small",Basin:small),(Size:"medium",Basin:medium),(Size:"large",Basin:large)})
            foreach(int algorithm in baselineOnly?new[]{2}:afterOnly?new[]{3}:new[]{2,3})
            {
                int patch=selection.Basin.Cells[0];
                var recipe=new HydrologyTopologyRecipe(1,algorithm,new(1,1,2,regional.Recipe),3,2,64,.25,new(32,2000000,800000,2000000,16384,180000));
                var baseline=HydrologyReconciler.Create(regional);var domain=baseline.CreateDomain(patch,CancellationToken.None);
                var clock=Stopwatch.StartNew();var profiled=AdaptiveBasinTopology.Profile(domain,recipe,patch,null);clock.Stop();var result=profiled.Result;
                Check(result.Metrics.TerrainSamples<=recipe.Limits.Samples && result.Metrics.AllocatedCells<=recipe.Limits.Cells && result.Metrics.NodeVisits<=recipe.Limits.NodeVisits,"work limits");
                Check(profiled.Work.WetGraphVisits+profiled.Work.SpillGraphVisits==result.Metrics.NodeVisits,"graph attribution accounts for every visit");
                Check(profiled.Work.SamplesInComponentRoots<=result.Metrics.TerrainSamples && profiled.Work.SharedFaceSamples<=result.Metrics.TerrainSamples,"sample attribution bounded");
                rows.Add(new(name,selection.Size,patch,selection.Basin.Cells.Count,algorithm,clock.Elapsed.TotalMilliseconds,result.ComponentStop,result.SpillStop,
                    result.ComponentCells.Count,result.SpillProposal?.RequiredElevation,result.Metrics,profiled.Work,result.Frontier.Select(f=>f.Reason).Distinct().Order().ToArray(),Hash(result)));
                Console.WriteLine($"COVERAGE {name}/{selection.Size} coarse={selection.Basin.Cells.Count} patch={patch} A{algorithm} {result.ComponentStop}/{result.SpillStop}: {result.Metrics.TerrainSamples} samples, {clock.Elapsed.TotalMilliseconds:F0}ms; outside visited roots={profiled.Work.SamplesOutsideVisitedRoots}, sampled dry={profiled.Work.SamplesInSampledDryRoots}, shared={profiled.Work.SharedFaceSamples}");
            }
        }
        Directory.CreateDirectory("artifacts/hydrology-work");
        string suffix=baselineOnly?"baseline":afterOnly?"after":"comparison";
        File.WriteAllText($"artifacts/hydrology-work/{suffix}.json",JsonSerializer.Serialize(rows,Json));
        // Timings are observations, not frozen output; pin solver results independently of profiling/timing.
        var matrix=rows.Select(r=>new{r.World,r.Size,r.Patch,r.Algorithm,r.Fingerprint}).ToArray();
        File.WriteAllText($"artifacts/hydrology-work/{suffix}-candidate.json",JsonSerializer.Serialize(matrix,Json));
        if(!baselineOnly && !afterOnly && verify)
        {
            using var expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-work-coverage-v1.json")));
            Check(Hash(expected.RootElement)==Hash(matrix),"frozen coverage results");
        }
        Console.WriteLine($"HYDROLOGY_COVERAGE_PASS: {checks} checks; {rows.Count} queries");return checks;
    }
}
