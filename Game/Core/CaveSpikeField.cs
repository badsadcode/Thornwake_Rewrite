using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Thornwake.Core;

// Explicitly a versioned architecture fixture, not the production cave/deposit generator.
public sealed class CaveSpikeState
{
    public int Version { get; set; } = 2;
    public int Seed { get; set; } = 7319;
    public List<CaveCut> Cuts { get; set; } = new();
    public LocalWaterSave? Water { get; set; }
    public void Validate()
    {
        Water?.Validate();
        if (Water != null && Water.Cells.Any(c=>c.X < -14 || c.X >= 14 || c.Z < -26 || c.Z >= 26 || c.Bed < -10 || c.Roof > 16))
            throw new InvalidDataException("Water state lies outside the cave laboratory domain.");
        if (Version is not (1 or 2) || Cuts == null || Cuts.Count > 128 || Cuts.Any(c => c == null || !c.Valid))
            throw new InvalidDataException("Unsupported or damaged cave laboratory state.");
    }
}
public sealed record CaveCut(float X, float Y, float Z, float Radius, float? StartX = null, float? StartY = null, float? StartZ = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(Radius) &&
        Math.Abs(X) + Radius <= 14 && Z - Radius >= -26 && Z + Radius <= 25 && Y - Radius >= -10 && Y + Radius <= 14 &&
        Radius >= .5f && Radius <= 2.5f &&
        (StartX == null && StartY == null && StartZ == null || StartX.HasValue && StartY.HasValue && StartZ.HasValue &&
         new CaveCut(StartX.Value,StartY.Value,StartZ.Value,Radius).Valid && Vector3.Distance(Start,Center)<=5);
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Center => new(X, Y, Z);
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Start => StartX.HasValue ? new(StartX.Value,StartY!.Value,StartZ!.Value) : Center;
    public float Distance(Vector3 point) => StartX.HasValue ? CaveSpikeField.SegmentDistance(point,Start,Center)-Radius : Vector3.Distance(point,Center)-Radius;
}
public sealed record LocalReservoir(string Id, float X, float Z, float Radius, float Level, float Bottom);

public static class CaveSpikeStore
{
    private const int MaximumBytes = 4194304;
    public static void Save(string path, CaveSpikeState state)
    {
        state.Validate();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(state, new JsonSerializerOptions { WriteIndented = true });
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Cave laboratory state is too large.");
        string absolute = Path.GetFullPath(path), temporary = absolute + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(absolute)) File.Replace(temporary, absolute, absolute + ".bak");
            else File.Move(temporary, absolute);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static CaveSpikeState Load(string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Cave laboratory state is too large.");
        try
        {
            string text = File.ReadAllText(path);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Version", out _) ||
                !document.RootElement.TryGetProperty("Seed", out _) || !document.RootElement.TryGetProperty("Cuts", out _))
                throw new InvalidDataException("Missing cave laboratory header.");
            var state = JsonSerializer.Deserialize<CaveSpikeState>(text) ?? throw new InvalidDataException("Empty cave laboratory state.");
            state.Validate(); return state;
        }
        catch (JsonException error) { throw new InvalidDataException("Damaged cave laboratory state.", error); }
    }
}

/// <summary>One solid/void authority for mesh, physics fixtures, exposure and water queries. Positive = solid.</summary>
public sealed class CaveSpikeField
{
    private readonly WorldState _surface;
    public CaveSpikeState State { get; }
    public LocalReservoir[] Reservoirs { get; }
    public string DepositId { get; }
    private readonly Vector3[] _tunnel = { new(0, 2, 20), new(0, 2, 6), new(0, 1, -7), new(6, 0, -15) };
    private readonly Vector3[] _vein;
    public CaveSpikeField(CaveSpikeState state)
    {
        state.Validate(); State = state;
        _surface = LandscapeGenerator.Create(state.Seed, new GenerationSettings(), new SandboxSettings());
        float lake = Surface(9, -9) - .8f;
        Reservoirs = new[] { new LocalReservoir("upper-pool", 9, -9, 3.4f, lake, lake - 3),
            new LocalReservoir("cave-pool", 1, -9, 14, -1.4f, -4) };
        _vein = new[] { new Vector3(-11, Surface(-11, 10) - .3f, 10), new Vector3(-6, 2, 5), new Vector3(0, 1, -4), new Vector3(9, -1, -9) };
        DepositId = new GenerationIdentity(state.Seed, 1, "cave-spike-v1").FeatureId("deposit", new WorldRegion(0, 0), 0);
    }
    private static float Smooth(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    public float Surface(float x, float z) => TerrainData.Height(_surface, x, z) +
        9 * Smooth((18 - z) / 12) * Smooth((30 - Math.Abs(x)) / 8) * Smooth((z + 30) / 8);
    public static float SegmentDistance(Vector3 point, Vector3 a, Vector3 b)
    {
        var edge = b - a;
        float t = edge.LengthSquared() < 1e-10f ? 0 : Math.Clamp(Vector3.Dot(point - a, edge) / edge.LengthSquared(), 0, 1);
        return Vector3.Distance(point, a + t * edge);
    }
    // Each worker gets a private cut list; generated base data is reconstructed from the same seed.
    public CaveSpikeField Snapshot() => new(new CaveSpikeState { Version = State.Version, Seed = State.Seed, Cuts = State.Cuts.ToList(), Water = State.Water?.Copy() });
    public float Density(Vector3 point) => Density(point, Surface(point.X, point.Z));
    internal float Density(Vector3 point, float surface)
    {
        float solid = surface - point.Y;
        for (int i = 1; i < _tunnel.Length; i++) solid = Math.Min(solid, SegmentDistance(point, _tunnel[i - 1], _tunnel[i]) - 3.2f);
        solid = Math.Min(solid, Vector3.Distance(point, new Vector3(0, 1, -8)) - 4.5f);
        // Sealed lower cavity beneath the elevated lake. Height alone must never fill it.
        solid = Math.Min(solid, Vector3.Distance(point, new Vector3(9, -6, -9)) - 3);
        var pool = Reservoirs[0];
        solid = Math.Min(solid, Vector3.Distance(point, new Vector3(pool.X, pool.Level + 1.3f, pool.Z)) - 3.2f);
        if (State.Version == 1)
            foreach (var cut in State.Cuts) solid = Math.Min(solid, cut.Distance(point));
        else
        {
            // Blend the two closest strokes and their join with the cave. The support expansion is
            // bounded by 0.2 m independent of the number/order of cuts, including duplicate strokes.
            float nearest = float.MaxValue, second = float.MaxValue;
            foreach (var cut in State.Cuts)
            {
                float distance = cut.Distance(point);
                if (distance < nearest) { second = nearest; nearest = distance; }
                else if (distance < second) second = distance;
            }
            solid = SmoothMinimum(solid, SmoothMinimum(nearest, second));
        }
        return solid;
    }
    private static float SmoothMinimum(float a, float b)
    {
        const float width = .4f;
        float h = Math.Max(width-Math.Abs(a-b),0)/width;
        return Math.Min(a,b)-h*h*width*.25f;
    }
    public Vector3 Normal(Vector3 point)
    {
        const float e = .025f;
        var gradient = new Vector3(Density(point + Vector3.UnitX * e) - Density(point - Vector3.UnitX * e),
            Density(point + Vector3.UnitY * e) - Density(point - Vector3.UnitY * e),
            Density(point + Vector3.UnitZ * e) - Density(point - Vector3.UnitZ * e));
        return gradient.LengthSquared() > 1e-12f ? -Vector3.Normalize(gradient) : Vector3.UnitY;
    }
    public bool DepositContains(Vector3 point)
    {
        for (int i = 1; i < _vein.Length; i++) if (SegmentDistance(point, _vein[i - 1], _vein[i]) <= .85f) return true;
        return false;
    }
    public string? WaterAt(Vector3 point)
    {
        if (Density(point) >= 0) return null;
        if (State.Water != null)
        {
            int x=(int)MathF.Floor(point.X), z=(int)MathF.Floor(point.Z);
            return State.Water.Cells.Any(c=>c.X==x && c.Z==z && point.Y>=c.Bed && point.Y<=c.Bed+c.Volume) ? "connected-water" : null;
        }
        foreach (var r in Reservoirs)
        {
            if (point.Y > r.Level || point.Y < r.Bottom ||
                Math.Pow(point.X - r.X, 2) + Math.Pow(point.Z - r.Z, 2) >= r.Radius * r.Radius) continue;
            // This fixture fills downward from its local surface only. A solid roof isolates lower voids.
            // Lateral breach propagation and pressure/flow are separate future hydrology work.
            bool open = true;
            int steps = Math.Max(1, (int)Math.Ceiling((r.Level - point.Y) / .1f));
            for (int i = 1; i <= steps; i++)
                if (Density(new(point.X, point.Y + (r.Level - point.Y) * i / steps, point.Z)) >= 0) { open = false; break; }
            if (open) return r.Id;
        }
        return null;
    }
    public bool TryCut(CaveCut cut)
    {
        if (cut == null || !cut.Valid || State.Cuts.Count >= 128) return false;
        // Surface support fixture stays protected, including the entire edit radius.
        if (SegmentDistance(new Vector3(-8,Surface(-8,-8),-8),cut.Start,cut.Center) < cut.Radius + 2.2f) return false;
        State.Cuts.Add(cut); return true;
    }
    public static bool IsVolumeRegion(TerrainRegion region) => region.X is -1 or 0 && region.Z >= -2 && region.Z <= 1;
}






