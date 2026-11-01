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
/// active volcano's foot. r = molten (the lava lake, a fresh flow's channel), g = crust (a fresh flow's crusted sides).
/// The terrain shader draws it glowing (TerrainBody.gdshaderinc: globals <c>lava_map</c>, <c>lava_frame</c>). Glowing
/// ground lights nothing round it in this renderer, so orange lights hang over the lava (D251): a strong one over the
/// lava lake, and along the fresh flows every 300 m, the 12 nearest the camera lit, falling off gently (distance^-0.35 to
/// ^-0.45) — the crater walls, the flanks and the haze over the crater take the glow. Setting <c>fx.lava</c>: off leaves the lava fields as dark rock.
/// </summary>
public sealed class LavaMap
{
    public const int Pixels = 1024;
    public const double Size = 16000;
    private const int Lights = 12;
    private const double LightSpacing = 300;

    private readonly ImageTexture _texture;
    private readonly OmniLight3D[] _lights = new OmniLight3D[Lights];
    private Task<(byte[] Data, (double X, double Y, double Z, double Heat, bool Lake)[] Lights)>? _job;
    private double _jobX = double.NaN, _jobZ, _mapX = double.NaN, _mapZ;
    private (double X, double Y, double Z, double Heat, bool Lake)[] _points = Array.Empty<(double, double, double, double, bool)>();
    private TerrainSampler? _for;

    public LavaMap(Node3D parent)
    {
        var image = Image.CreateEmpty(Pixels, Pixels, false, Image.Format.Rg8);
        image.Fill(new Color(0, 0, 0, 1));
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
            _texture.Update(Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rg8, done.Result.Data));
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
            light.Position = new Vector3((float)(p.X - originX), (float)p.Y + (p.Lake ? 80 : 25), (float)(p.Z - originZ));
            // (Godot's omni light falls off as distance^-attenuation: at 1.2 a light 300 m off gave the crater walls 2 % of it)
            light.OmniAttenuation = p.Lake ? .35f : .45f;
            light.LightEnergy = p.Lake ? 3.5f : (float)(4 * p.Heat);
            light.OmniRange = p.Lake ? 1400 : 500;
            light.LightVolumetricFogEnergy = p.Lake ? 1.2f : .8f; // (3 turned the whole sky over the crater red)
            light.Visible = true;
        }
    }

    private void Off()
    {
        Disable();
        foreach (var light in _lights) light.Visible = false;
    }

    public static void Disable() => RenderingServer.GlobalShaderParameterSet("lava_frame", Vector4.Zero);

    private static (byte[], (double, double, double, double, bool)[]) Build(TerrainSampler terrain, Volcanoes volcanoes, double cx, double cz)
    {
        var data = new byte[Pixels * Pixels * 2];
        double step = Size / Pixels;
        Parallel.For(0, Pixels, j =>
        {
            for (int i = 0; i < Pixels; i++)
            {
                var (molten, crust) = volcanoes.LavaAt(cx - Size / 2 + (i + .5) * step, cz - Size / 2 + (j + .5) * step);
                int k = (j * Pixels + i) * 2;
                data[k] = (byte)Math.Clamp(molten * 255, 0, 255);
                data[k + 1] = (byte)Math.Clamp(crust * 255, 0, 255);
            }
        });
        var lights = new List<(double, double, double, double, bool)>();
        foreach (var (x, z, heat, lake) in volcanoes.LavaLights(cx, cz, LightSpacing))
            lights.Add((x, terrain.Height(x, z), z, heat, lake));
        return (data, lights.ToArray());
    }
}
