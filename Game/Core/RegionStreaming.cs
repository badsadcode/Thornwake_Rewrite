using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Core;

public sealed record RegionStreamPlan(IReadOnlyList<WorldRegion> Load, IReadOnlyList<WorldRegion> Unload, int DesiredCount);

/// <summary>
/// Pure bounded residency planning. Call again after observer movement; no stale pending queue survives.
/// The caller owns resident objects and installs a region only after its build succeeds.
/// </summary>
public static class RegionStreaming
{
    public const int MaximumObservers = 8;
    public const int MaximumRadius = 24;
    public const int MaximumResidentRegions = MaximumObservers * (2 * MaximumRadius + 1) * (2 * MaximumRadius + 1);

    public static RegionStreamPlan Plan(IReadOnlyCollection<WorldRegion> resident,
        IReadOnlyList<WorldRegion> observers, int loadRadius, int retainRadius, int loadBudget,
        Func<WorldRegion, bool>? available = null)
    {
        if (resident == null || observers == null) throw new ArgumentNullException();
        if (observers.Count > MaximumObservers || resident.Count > MaximumResidentRegions ||
            loadRadius < 0 || loadRadius > MaximumRadius || retainRadius < loadRadius ||
            retainRadius > MaximumRadius || loadBudget < 0 || loadBudget > 256)
            throw new ArgumentOutOfRangeException(nameof(loadRadius));
        foreach (var region in resident) region.Validate();
        foreach (var observer in observers) observer.Validate();
        var loaded = new HashSet<WorldRegion>(resident);
        var desired = new HashSet<WorldRegion>();
        foreach (var observer in observers)
            for (int x = -loadRadius; x <= loadRadius; x++)
                for (int z = -loadRadius; z <= loadRadius; z++)
                {
                    var region = new WorldRegion(observer.X + x, observer.Z + z);
                    if (region.X < -WorldRegion.MaximumIndex || region.X > WorldRegion.MaximumIndex ||
                        region.Z < -WorldRegion.MaximumIndex || region.Z > WorldRegion.MaximumIndex) continue;
                    if (available == null || available(region)) desired.Add(region);
                }
        long Distance(WorldRegion region)
        {
            long distance = long.MaxValue;
            foreach (var observer in observers)
                distance = Math.Min(distance, Math.Max(Math.Abs(region.X - observer.X), Math.Abs(region.Z - observer.Z)));
            return distance;
        }
        var unload = loaded.Where(r => Distance(r) > retainRadius || available != null && !available(r))
            .OrderBy(r => r.X).ThenBy(r => r.Z).ToArray();
        var load = desired.Where(r => !loaded.Contains(r)).OrderBy(Distance)
            .ThenBy(r => r.X).ThenBy(r => r.Z).Take(loadBudget).ToArray();
        return new(load, unload, desired.Count);
    }
}
