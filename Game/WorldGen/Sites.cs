namespace Thornwake.WorldGen;

/// <summary>A settlement site chosen once in the plan (village, town, castle, harbour…): a level pad with soft edges at
/// <see cref="Level"/>. Docs/WORLDGEN_PLAN.md step 3; none are chosen yet.</summary>
public sealed record WorldSite(string Kind, double X, double Z, double Radius, double Level);

/// <summary>A road routed once in the plan between sites (points as x, z pairs): a gentle corridor cut and filled into
/// the ground; rivers keep their channel (a ford or a bridge spot). Docs/WORLDGEN_PLAN.md step 3; none are routed yet.</summary>
public sealed record WorldRoad(double[] Points, double Width);
