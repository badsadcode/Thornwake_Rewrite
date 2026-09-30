using System.Globalization;
using System.Text.Json;
using Thornwake.Core;

internal static class WorldStreamingTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("WORLD STREAMING: " + label); checks++; }
        void Reject(Action action, string label)
        {
            try { action(); } catch (ArgumentException) { checks++; return; }
            throw new Exception("WORLD STREAMING accepted " + label);
        }
        Check(WorldRegion.At(-.001, -16.001) == new WorldRegion(-1, -2), "negative coordinates use floor");
        Check(WorldRegion.At(16, -16) == new WorldRegion(1, -1), "exact boundaries have one owner");
        foreach (long index in new[] { -999_999_999L, -100_000L, -1L, 0L, 1L, 999_999_999L })
        {
            var region = new WorldRegion(index, -index);
            var origin = region.Origin(123.5);
            var point = new WorldPoint(origin.X + .125, origin.Y + .25, origin.Z + 15.875);
            Check(WorldRegion.At(point.X, point.Z) == region, "far origin retains region ownership");
            Check(point.RelativeTo(origin) == new System.Numerics.Vector3(.125f, .25f, 15.875f), "small offsets survive large coordinates");
            Check(JsonSerializer.Deserialize<WorldPoint>(JsonSerializer.Serialize(point)) == point, "global coordinate JSON roundtrip");
        }
        Reject(() => WorldRegion.At(double.NaN, 0), "NaN position");
        Reject(() => WorldRegion.At(double.PositiveInfinity, 0), "infinite position");
        Reject(() => WorldRegion.At(double.MaxValue, 0), "overflow position");
        Reject(() => new WorldRegion(long.MinValue, 0).Origin(), "invalid integer address");
        Reject(() => new WorldPoint(9000, 0, 0).RelativeTo(default), "unsafe float conversion before rebase");
        var identity = new GenerationIdentity(-7319, 3, "continental-v1");
        var owner = new WorldRegion(-12, 98);
        string id = identity.FeatureId("deposits", owner, 7);
        Check(id == "world:-7319:3:continental-v1:deposits:-12:98:7", "specified full identity");
        ulong seed = identity.FeatureSeed("deposits", owner, 7);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Check(identity.FeatureId("deposits", owner, 7) == id && identity.FeatureSeed("deposits", owner, 7) == seed,
                "identity and generation stream independent of process culture");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check(identity.FeatureId("caves", owner, 7) != id, "layers have separate identities");
        Check((identity with { ProfileRevision = "continental-v2" }).FeatureId("deposits", owner, 7) != id, "profile revisions are explicit");
        Check((identity with { AlgorithmVersion = 4 }).FeatureId("deposits", owner, 7) != id, "algorithm version pins feature identity");
        Check(identity.FeatureId("deposits", owner with { X = 12 }, 7) != id, "signed region keys do not alias");
        Reject(() => identity.FeatureId("invalid:token", owner, 7), "ambiguous identity token");
        Reject(() => identity.FeatureId("deposits", owner, -1), "negative ordinal");
        Reject(() => (identity with { AlgorithmVersion = 0 }).FeatureId("deposits", owner, 0), "unknown algorithm zero");

        var resident = new HashSet<WorldRegion>();
        void Apply(RegionStreamPlan plan)
        {
            foreach (var region in plan.Unload) resident.Remove(region);
            foreach (var region in plan.Load) Check(resident.Add(region), "no duplicate region commit");
        }
        var observers = new[] { new WorldRegion(0, 0) };
        var first = RegionStreaming.Plan(resident, observers, 2, 3, 2);
        Check(first.Load.Count == 2 && first.Load[0] == observers[0] && first.DesiredCount == 25, "nearest-first bounded startup");
        Check(resident.Count == 0, "planning does not mutate residency");
        Apply(first);
        var retry = RegionStreaming.Plan(resident, observers, 2, 3, 2);
        Check(retry.Load.SequenceEqual(RegionStreaming.Plan(resident.Reverse().ToArray(), observers, 2, 3, 2).Load), "load order independent of set enumeration");
        for (int i = 0; i < 20; i++) Apply(RegionStreaming.Plan(resident, observers, 2, 3, 2));
        Check(resident.Count == 25, "fills desired window then stops");
        var nudge = RegionStreaming.Plan(resident, new[] { new WorldRegion(1, 0) }, 2, 3, 2);
        Check(nudge.Unload.Count == 0, "retention ring avoids boundary thrash");
        var far = new[] { new WorldRegion(100_000_000, -100_000_000) };
        var teleport = RegionStreaming.Plan(resident, far, 2, 3, 1);
        Check(teleport.Unload.Count == 25 && teleport.Load.SequenceEqual(far), "teleport drops old demand and prioritizes destination");
        Apply(teleport);
        Check(resident.Count == 1, "teleport leaves no obsolete residency");
        var random = new Random(2519);
        for (int step = 0; step < 400; step++)
        {
            var focus = new WorldRegion(random.Next(-50, 50), random.Next(-50, 50));
            var plan = RegionStreaming.Plan(resident, new[] { focus }, 2, 3, 3);
            Check(plan.Load.Count <= 3, "per-frame build bound"); Apply(plan);
            Check(resident.Count <= 49, "resident memory independent of explored distance");
            Check(resident.All(r => Math.Max(Math.Abs(r.X - focus.X), Math.Abs(r.Z - focus.Z)) <= 3), "no stale residency after observer movement");
        }
        Apply(RegionStreaming.Plan(resident, Array.Empty<WorldRegion>(), 2, 3, 0));
        Check(resident.Count == 0, "no observers releases all visual residency");
        var multi = new[] { new WorldRegion(-1, 0), new WorldRegion(1, 0), new WorldRegion(-1, 0) };
        var union = RegionStreaming.Plan(resident, multi, 1, 2, 256);
        Check(union.DesiredCount == 15 && union.Load.Distinct().Count() == 15, "overlapping observers share region ownership");
        Check(union.Load.SequenceEqual(RegionStreaming.Plan(resident, multi.Reverse().ToArray(), 1, 2, 256).Load), "observer order has no effect");
        Apply(union);
        var keepSecond = RegionStreaming.Plan(resident, new[] { multi[1] }, 1, 1, 256);
        Apply(keepSecond); Check(resident.Count == 9, "departing observer cannot unload another observer's window");
        Check(RegionStreaming.Plan(Array.Empty<WorldRegion>(), new[] { new WorldRegion(-4, -4) }, 2, 3, 256,
            r => r.X >= -4 && r.X <= 3 && r.Z >= -4 && r.Z <= 3).DesiredCount == 9, "legacy adapter clips finite edges");
        var edge = new WorldRegion(WorldRegion.MaximumIndex, WorldRegion.MaximumIndex);
        Check(RegionStreaming.Plan(Array.Empty<WorldRegion>(), new[] { edge }, 1, 1, 256).DesiredCount == 4, "domain edge does not overflow");
        Reject(() => RegionStreaming.Plan(resident, observers, -1, 3, 2), "negative radius");
        Reject(() => RegionStreaming.Plan(resident, observers, 3, 2, 2), "inverted hysteresis");
        Reject(() => RegionStreaming.Plan(resident, observers, 25, 25, 2), "unbounded window");
        Reject(() => RegionStreaming.Plan(resident, observers, 2, 3, -1), "negative budget");
        Reject(() => RegionStreaming.Plan(resident, Enumerable.Repeat(default(WorldRegion), 9).ToArray(), 2, 3, 2), "unbounded observers");
        Reject(() => RegionStreaming.Plan(new[] { new WorldRegion(long.MaxValue, 0) }, observers, 2, 3, 2), "overflowing resident distance");
        return checks;
    }
}
