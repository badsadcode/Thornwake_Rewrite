using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Molten lava around the nearest active volcano (terrain revision 31, D250) as a texture: 16 km square, 1,024 pixels
/// (about 16 m each), from <see cref="Volcanoes.LavaAt"/> on worker threads when the player comes within 20 km of an
/// active volcano's foot. r = molten (the lava lake, a fresh flow's channel), g = crust (a fresh flow's crusted sides),
/// b, a = which way it moves (× 0.5 + 0.5; its length the speed, D252).
/// The terrain shader draws it glowing (TerrainBody.gdshaderinc: globals <c>lava_map</c>, <c>lava_frame</c>). Glowing
/// ground lights nothing round it in this renderer, so orange lights hang over the lava (D251): a strong one over the
/// lava lake (distance^-0.35), and 15 m over the fresh flows every 150 m (distance^-0.5, the nearest casting
/// shadows), the 16 nearest the camera lit — the crater walls, the rocks beside a flow take the glow; only the lake's lights the haze.
/// Setting <c>fx.lava</c>: off leaves the lava fields as dark rock.
/// </summary>
public sealed class LavaMap
{
    public const int Pixels = 1024;
    public const double Size = 16000;
    private const int Lights = 16;
    private const double LightSpacing = 150;
    private const int ShadowLights = 1;

    private readonly ImageTexture _texture;
    private readonly OmniLight3D[] _lights = new OmniLight3D[Lights];
    private Task<(byte[] Data, (double X, double Y, double Z, double Heat, bool Lake)[] Lights)>? _job;
    private double _jobX = double.NaN, _jobZ, _mapX = double.NaN, _mapZ;
    private (double X, double Y, double Z, double Heat, bool Lake)[] _points = Array.Empty<(double, double, double, double, bool)>();
    private TerrainSampler? _for;

    private readonly MeshInstance3D _shimmer;

    public LavaMap(Node3D parent, Camera3D camera)
    {
        // Heat shimmer (D255): a full-screen pass in front of the camera, drawn first of the transparent passes — it copies the
        // opaque picture, and drawn last it laid that copy over the haze passes (brighter bands over the flow).
        _shimmer = new MeshInstance3D
        {
            Name = "HeatShimmer", Mesh = new QuadMesh { Size = new Vector2(2, 2) },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/HeatShimmer.gdshader"), RenderPriority = -128 },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1),
            GIMode = GeometryInstance3D.GIModeEnum.Disabled, Visible = false,
        };
        camera.AddChild(_shimmer);
        var image = Image.CreateEmpty(Pixels, Pixels, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, .5f, .5f)); // (no lava, standing still)
        _texture = ImageTexture.CreateFromImage(image);
        RenderingServer.GlobalShaderParameterSet("lava_map", _texture);
        for (int i = 0; i < Lights; i++)
        {
            _lights[i] = new OmniLight3D { Name = "LavaLight" + i, LightColor = new Color(1f, .38f, .1f), ShadowEnabled = false, Visible = false };
            parent.AddChild(_lights[i]);
        }
        Disable();
    }

    public void Update(TerrainSampler terrain, double camX, double camZ, double originX, double originZ, bool now)
    {
        if (_job is { IsCompleted: true } done)
        {
            _texture.Update(Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rgba8, done.Result.Data));
            _points = done.Result.Lights;
            _mapX = _jobX; _mapZ = _jobZ;
            _job = null;
        }
        if (_for != terrain) { _for = terrain; _mapX = _jobX = double.NaN; _points = Array.Empty<(double, double, double, double, bool)>(); }
        var volcanoes = terrain.Plan.PlanLandforms.OfType<Volcanoes>().FirstOrDefault();
        if (GameSettings.Lava.Index == 0 || volcanoes == null) { Off(); return; }
        // The nearest active volcano within 20 km of its foot.
        double bestX = double.NaN, bestZ = 0, bestDistance = double.MaxValue;
        foreach (var (x, z, radius) in volcanoes.Active())
        {
            double d = Math.Sqrt((x - camX) * (x - camX) + (z - camZ) * (z - camZ));
            if (d < radius + 20000 && d < bestDistance) { bestDistance = d; bestX = x; bestZ = z; }
        }
        if (double.IsNaN(bestX)) { Off(); return; }
        if (_job == null && (bestX != _mapX || bestZ != _mapZ) && (bestX != _jobX || bestZ != _jobZ || double.IsNaN(_mapX)))
        {
            double vx = bestX, vz = bestZ;
            _jobX = vx; _jobZ = vz;
            _job = Background.Run(() => Build(terrain, volcanoes, vx, vz));
            if (now) { _job.Wait(); Update(terrain, camX, camZ, originX, originZ, false); return; }
        }
        if (double.IsNaN(_mapX)) { Off(); return; }
        RenderingServer.GlobalShaderParameterSet("lava_frame", new Vector4((float)(_mapX - originX), (float)(_mapZ - originZ), (float)Size, 1));
        PlaceLights(camX, camZ, originX, originZ);
        _shimmer.Visible = GameSettings.HeatShimmer.Index == 1 && bestDistance < 12000;
    }

    /// <summary>The lake's light always (strong, far-reaching, glowing in the haze); the flow lights nearest the camera.</summary>
    private void PlaceLights(double camX, double camZ, double originX, double originZ)
    {
        var chosen = _points.OrderBy(p => p.Lake ? -1 : (p.X - camX) * (p.X - camX) + (p.Z - camZ) * (p.Z - camZ)).Take(Lights).ToArray();
        for (int i = 0; i < Lights; i++)
        {
            var light = _lights[i];
            if (i >= chosen.Length) { light.Visible = false; continue; }
            var p = chosen[i];
            // Flow lights close over the lava (10 m), every 150 m, falling off faster (D253, Noor: "nearby lava should
            // illuminate surrounding rocks with a softer orange glow ... not uniformly"): warm on the rocks beside a flow,
            // fading over a few hundred metres; the nearest casts shadows (three cost too much: six scene renders each), so the rocks throw irregular shadows.
            // (Godot's omni light falls off as distance^-attenuation: at 1.2 a light 300 m off gave the crater walls 2 % of it)
            light.Position = new Vector3((float)(p.X - originX), (float)p.Y + (p.Lake ? 80 : 15), (float)(p.Z - originZ));
            light.OmniAttenuation = p.Lake ? .35f : .5f; // (0.8 lit only a few metres round each)
            light.LightEnergy = p.Lake ? 3.5f : (float)(7.5 * p.Heat); // (D257: the terrain's own blurred glow beside a flow is gone; the lights carry it)
            light.OmniRange = p.Lake ? 1400 : 450;
            // (the haze over the crater glows from the lake only, gently: 1.2 turned a cloud the crater sat in into a red wash
            // in build 124's first package, and fog lit by every flow light cost frames)
            light.LightVolumetricFogEnergy = p.Lake ? .3f : 0f;
            light.ShadowEnabled = !p.Lake && i <= ShadowLights;
            light.Visible = true;
        }
    }

    private void Off()
    {
        Disable();
        _shimmer.Visible = false;
        foreach (var light in _lights) light.Visible = false;
    }

    public static void Disable() => RenderingServer.GlobalShaderParameterSet("lava_frame", Vector4.Zero);

    private static (byte[], (double, double, double, double, bool)[]) Build(TerrainSampler terrain, Volcanoes volcanoes, double cx, double cz)
    {
        var data = new byte[Pixels * Pixels * 4];
        double step = Size / Pixels;
        Parallel.For(0, Pixels, j =>
        {
            for (int i = 0; i < Pixels; i++)
            {
                var (molten, crust, dx, dz) = volcanoes.LavaFlow(cx - Size / 2 + (i + .5) * step, cz - Size / 2 + (j + .5) * step);
                int k = (j * Pixels + i) * 4;
                data[k] = (byte)Math.Clamp(molten * 255, 0, 255);
                data[k + 1] = (byte)Math.Clamp(crust * 255, 0, 255);
                data[k + 2] = (byte)Math.Clamp((dx * .5 + .5) * 255, 0, 255);
                data[k + 3] = (byte)Math.Clamp((dz * .5 + .5) * 255, 0, 255);
            }
        });
        var lights = new List<(double, double, double, double, bool)>();
        foreach (var (x, z, heat, lake) in volcanoes.LavaLights(cx, cz, LightSpacing))
            lights.Add((x, terrain.Height(x, z), z, heat, lake));
        return (data, lights.ToArray());
    }
}
