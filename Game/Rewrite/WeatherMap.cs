using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The weather around the player as a texture (round 27): 128 km square, 2 km per pixel, rebuilt from the
/// <see cref="WeatherField"/> on a worker thread every couple of seconds (or after moving 4 km). r = cloud cover,
/// g = rain, b = thunderstorm (convective) strength, a = 1. The sky draws thick, dark cloud and storm towers where it
/// is, and the rain-curtain pass grey veils of rain under it, so bad weather shows where it really is. Shaders read
/// the global <c>weather_map</c> and <c>weather_frame</c> (camera position in the map's uv, 1/size in m, on/off).
/// </summary>
public sealed class WeatherMap
{
    public const int Pixels = 64;
    public const double Size = 128000;

    private readonly ImageTexture _texture;
    private Task<byte[]>? _job;
    private double _jobX, _jobZ, _mapX = double.NaN, _mapZ, _timer;

    public WeatherMap()
    {
        var image = Image.CreateEmpty(Pixels, Pixels, false, Image.Format.Rgba8);
        image.Fill(new Color(0, 0, 0, 1));
        _texture = ImageTexture.CreateFromImage(image);
        RenderingServer.GlobalShaderParameterSet("weather_map", _texture);
    }

    /// <summary>Per frame (simulated weather). <paramref name="camX"/>, <paramref name="camZ"/>: the camera in the world.</summary>
    public void Update(WeatherField field, double gameHours, double camX, double camZ, double delta, bool now)
    {
        _timer -= delta;
        if (_job is { IsCompleted: true } done)
        {
            var image = Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rgba8, done.Result);
            _texture.Update(image);
            _mapX = _jobX; _mapZ = _jobZ;
            _job = null;
        }
        bool moved = double.IsNaN(_mapX) || Math.Abs(camX - _mapX) + Math.Abs(camZ - _mapZ) > 4000;
        if (_job == null && (_timer <= 0 || moved))
        {
            _timer = 2;
            // Snapped to the pixel grid, so the map does not swim as the camera moves.
            double step = Size / Pixels;
            double cx = Math.Round(camX / step) * step, cz = Math.Round(camZ / step) * step;
            _jobX = cx; _jobZ = cz;
            _job = Task.Run(() => Build(field, gameHours, cx, cz));
            if (now) _job.Wait();
            if (now) { Update(field, gameHours, camX, camZ, 0, false); return; }
        }
        if (double.IsNaN(_mapX)) { RenderingServer.GlobalShaderParameterSet("weather_frame", Vector4.Zero); return; }
        // uv of the camera in the map, metres → uv scale, on.
        float u = (float)((camX - (_mapX - Size / 2)) / Size), v = (float)((camZ - (_mapZ - Size / 2)) / Size);
        RenderingServer.GlobalShaderParameterSet("weather_frame", new Vector4(u, v, (float)(1 / Size), 1));
    }

    public static void Disable() => RenderingServer.GlobalShaderParameterSet("weather_frame", Vector4.Zero);

    private static byte[] Build(WeatherField field, double gameHours, double cx, double cz)
    {
        var data = new byte[Pixels * Pixels * 4];
        var scratch = new List<WeatherField.Cell>();
        double step = Size / Pixels;
        for (int j = 0; j < Pixels; j++)
            for (int i = 0; i < Pixels; i++)
            {
                double x = cx - Size / 2 + (i + .5) * step, z = cz - Size / 2 + (j + .5) * step;
                var w = field.At(x, z, gameHours, true, scratch);
                int k = (j * Pixels + i) * 4;
                data[k] = (byte)Math.Clamp(w.Cloud * 255, 0, 255);
                data[k + 1] = (byte)Math.Clamp(w.Precip * 255, 0, 255);
                data[k + 2] = (byte)Math.Clamp(w.Convective * 255, 0, 255);
                data[k + 3] = 255;
            }
        return data;
    }
}
