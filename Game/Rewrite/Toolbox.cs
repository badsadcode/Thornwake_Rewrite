using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>A hand tool (or the dev terraform tool): what its two actions do, how strong, how far, how hard.</summary>
public sealed record TerrainTool(string Name, string Key, BrushMode Primary, BrushMode Secondary, BrushShape Shape, double Radius,
    double Strength, double Reach, double Interval, double RockFactor, bool UsesSoil, string Help);

/// <summary>
/// Terrain tools in the player's hands: select with 1–5, left mouse = primary action, right mouse = secondary.
/// A projected ring previews where and how wide the tool works (red when out of reach). Hand tools have limited
/// reach, strength per stroke and rate, are weak on rock (except the pickaxe), and move real volume: digging fills
/// the soil bag, filling empties it. The dev tool has adjustable shape, mode, radius and strength and no limits.
/// </summary>
public partial class Toolbox : Node3D
{
    public static readonly TerrainTool[] Tools =
    {
        new("Hands", "1", BrushMode.Dig, BrushMode.Raise, BrushShape.Round, 0, 0, 0, 1, 0, false, "no tool"),
        new("Shovel", "2", BrushMode.Dig, BrushMode.Raise, BrushShape.Round, 1.1, .22, 5, .45, .12, true,
            "dig soil (LMB), fill from the soil bag (RMB); weak on rock"),
        new("Pickaxe", "3", BrushMode.Dig, BrushMode.Dig, BrushShape.Directional, .9, .3, 4, .55, .9, true,
            "break ground in the direction you aim, also rock (LMB/RMB)"),
        new("Hoe", "4", BrushMode.Flatten, BrushMode.Smooth, BrushShape.RoundHard, 1.3, .08, 5, .3, .1, false,
            "level ground to the aimed height (LMB), smooth bumps (RMB)"),
        new("Terraform (dev)", "5", BrushMode.Dig, BrushMode.Raise, BrushShape.Round, 4, .6, 300, .08, 1, false,
            "LMB/RMB apply · T mode · Y shape · [ ] radius · - = strength"),
    };
    public const double SoilCapacity = 1.5; // m³ in the bag

    public int Selected { get; private set; }
    public TerrainTool Tool => Tools[Selected];
    public double Soil { get; private set; }
    public BrushMode DevMode { get; private set; } = BrushMode.Dig;
    public BrushShape DevShape { get; private set; } = BrushShape.Round;
    public double DevRadius { get; private set; } = 4;
    public double DevStrength { get; private set; } = .6;
    public string Status { get; private set; } = "";
    /// <summary>Multiplayer client: digs go to the host (which changes the ground for everyone) instead of being applied here.</summary>
    public Action<Thornwake.Rewrite.Net.DigRequest>? Remote { get; set; }
    private bool _remoteSoil;

    /// <summary>The host applied this player's dig: the soil bag follows the volume moved.</summary>
    public void RemoteDone(double volume, double hardness)
    {
        if (_remoteSoil) Soil = Math.Clamp(Soil - volume, 0, SoilCapacity);
        if (hardness > .6 && Tool.RockFactor < .5 && Math.Abs(volume) < .01 && Selected != Tools.Length - 1) Status = $"Too hard for the {Tool.Name.ToLowerInvariant()}: use the pickaxe.";
    }

    private Decal _preview = null!;
    private ImageTexture _round = null!, _square = null!;
    private double _cooldown;

    public override void _Ready()
    {
        _round = Ring(false); _square = Ring(true);
        _preview = new Decal { Size = new Vector3(2, 6, 2), TextureAlbedo = _round, Visible = false, UpperFade = .1f, LowerFade = .1f, CullMask = 1 };
        AddChild(_preview);
    }

    public bool Select(int index)
    {
        if (index < 0 || index >= Tools.Length) return false;
        Selected = index;
        Status = $"{Tool.Name}: {Tool.Help}";
        return true;
    }

    /// <summary>Dev tool keys; returns true if the key was used.</summary>
    public bool DevKey(Key key)
    {
        if (Selected != Tools.Length - 1) return false;
        switch (key)
        {
            case Key.T: DevMode = (BrushMode)(((int)DevMode + 1) % 4); break;
            case Key.Y: DevShape = (BrushShape)(((int)DevShape + 1) % 4); break;
            case Key.Bracketleft: DevRadius = Math.Max(.5, DevRadius / 1.25); break;
            case Key.Bracketright: DevRadius = Math.Min(40, DevRadius * 1.25); break;
            case Key.Minus: DevStrength = Math.Max(.02, DevStrength / 1.4); break;
            case Key.Equal: DevStrength = Math.Min(10, DevStrength * 1.4); break;
            default: return false;
        }
        return true;
    }

    public string Hud()
    {
        if (Selected == 0) return "Tool: hands (1–5 select tools)";
        if (Selected == Tools.Length - 1)
            return $"Tool: terraform (dev) · mode {DevMode} (T) · shape {DevShape} (Y) · radius {DevRadius:0.0} m ([ ]) · strength {DevStrength:0.00} m (- =)";
        return $"Tool: {Tool.Name} · {Tool.Help}" + (Tool.UsesSoil ? $" · soil bag {Soil:0.00}/{SoilCapacity:0.0} m³" : "");
    }

    /// <summary>
    /// Per physics frame: preview at the aimed point; apply on mouse buttons. Returns the touched rectangle when the
    /// ground changed (the caller rebuilds tiles and water there).
    /// </summary>
    public SculptResult? Update(double dt, TerrainSampler sampler, Vector3? hitLocal, Vector3 aimDirection, double originX, double originZ,
        Vector3 playerLocal, bool primary, bool secondary)
    {
        _cooldown -= dt;
        var tool = Tool;
        bool dev = Selected == Tools.Length - 1;
        double radius = dev ? DevRadius : tool.Radius;
        var shape = dev ? DevShape : tool.Shape;
        if (Selected == 0 || hitLocal is not { } hit) { _preview.Visible = false; return null; }
        bool inReach = dev || hit.DistanceTo(playerLocal + Vector3.Up * 1.4f) <= tool.Reach;
        // Preview: a projected ring the size of the brush (square brush: a square), red when out of reach.
        _preview.Visible = true;
        _preview.Position = hit;
        float size = (float)(shape == BrushShape.Directional ? radius * 2 + radius * 2.5 : radius * 2);
        _preview.Size = new Vector3(size, 6, size);
        _preview.TextureAlbedo = shape == BrushShape.Square ? _square : _round;
        var mode = primary ? (dev ? DevMode : tool.Primary) : (dev && DevMode is BrushMode.Dig or BrushMode.Raise ? Opposite(DevMode) : tool.Secondary);
        _preview.Modulate = !inReach ? new Color(1f, .3f, .25f, .8f) : (primary || !secondary ? ModeColor(dev ? DevMode : tool.Primary) : ModeColor(mode));
        if (!(primary || secondary) || _cooldown > 0 || !inReach) return null;
        _cooldown = tool.Interval;

        double strength = dev ? DevStrength : tool.Strength;
        double budget = double.PositiveInfinity;
        if (tool.UsesSoil && !dev)
        {
            if (mode == BrushMode.Raise) budget = Soil;
            if (mode == BrushMode.Dig) budget = SoilCapacity - Soil;
            if (budget <= 1e-4)
            {
                Status = mode == BrushMode.Raise ? "The soil bag is empty: dig somewhere first." : "The soil bag is full: fill a hole or drop soil (RMB).";
                return null;
            }
        }
        var world = (hit.X + originX, (double)hit.Y, hit.Z + originZ);
        if (Remote != null)
        {
            Remote(new Thornwake.Rewrite.Net.DigRequest(mode, shape, radius, strength, dev ? 1 : tool.RockFactor, budget,
                world.Item1, world.Item2, world.Item3, aimDirection.X, aimDirection.Y, aimDirection.Z));
            _remoteSoil = tool.UsesSoil && !dev;
            return null;
        }
        var result = TerrainSculpt.Apply(sampler, mode, shape, radius, strength, world, (aimDirection.X, aimDirection.Y, aimDirection.Z),
            dev ? 1 : tool.RockFactor, budget);
        if (tool.UsesSoil && !dev) Soil = Math.Clamp(Soil - result.Volume, 0, SoilCapacity);
        if (result.Hardness > .6 && !dev && tool.RockFactor < .5 && Math.Abs(result.Volume) < .01) Status = $"Too hard for the {tool.Name.ToLowerInvariant()}: use the pickaxe.";
        return result.Changed ? result : null;
    }

    private static BrushMode Opposite(BrushMode m) => m == BrushMode.Dig ? BrushMode.Raise : BrushMode.Dig;

    private static Color ModeColor(BrushMode m) => m switch
    {
        BrushMode.Dig => new Color(1f, .75f, .35f, .85f),
        BrushMode.Raise => new Color(.45f, .9f, .45f, .85f),
        BrushMode.Flatten => new Color(.5f, .75f, 1f, .85f),
        _ => new Color(.85f, .6f, 1f, .85f),
    };

    /// <summary>White ring (or square outline) with a faint fill, for the projected preview.</summary>
    private static ImageTexture Ring(bool square)
    {
        const int N = 128;
        var image = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / N * 2 - 1;
                float r = square ? Math.Max(Math.Abs(u), Math.Abs(v)) : MathF.Sqrt(u * u + v * v);
                float edge = r <= 1 ? 1 - Mathf.SmoothStep(.0f, .06f, Math.Abs(r - .95f)) : 0;
                float fill = r < .95f ? .12f : 0;
                float cross = r < .12f && (Math.Abs(u) < .015f || Math.Abs(v) < .015f) ? .8f : 0;
                image.SetPixel(x, y, new Color(1, 1, 1, Math.Max(Math.Max(edge, fill), cross)));
            }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
