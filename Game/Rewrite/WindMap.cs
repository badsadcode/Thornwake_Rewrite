using System;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// The surface wind around the player as a texture (round 28): 12 km square, 250 m per pixel, from
/// <see cref="WeatherField.WindAt"/> on a worker thread every few seconds (or after moving 1.5 km). r, g = direction
/// (×0.5 + 0.5), b = speed / 32 m/s. Grass, plants and tree crowns read it (WindSway.gdshaderinc), so a sheltered
/// valley sways gently while the ridge above it is in a gale. Globals <c>wind_map</c> and <c>wind_map_frame</c>.
/// </summary>
public sealed class WindMap
{
    public const int Pixels = 48;
    public const double Size = 12000;

    private readonly ImageTexture _texture;
    private Task<byte[]>? _job;
    private double _jobX, _jobZ, _mapX = double.NaN, _mapZ, _timer;

    public WindMap()
    {
        var image = Image.CreateEmpty(Pixels, Pixels, false, Image.Format.Rgba8);
        image.Fill(new Color(.5f, .5f, 0, 1));
        _texture = ImageTexture.CreateFromImage(image);
        RenderingServer.GlobalShaderParameterSet("wind_map", _texture);
    }

    public void Update(WeatherField field, TerrainSampler terrain, double gameHours, double camX, double camZ, double originX,
        double originZ, float playerRatio, double delta, bool now)
    {
        _timer -= delta;
        if (_job is { IsCompleted: true } done)
        {
            _texture.Update(Image.CreateFromData(Pixels, Pixels, false, Image.Format.Rgba8, done.Result));
            _mapX = _jobX; _mapZ = _jobZ;
            _job = null;
        }
        bool moved = double.IsNaN(_mapX) || Math.Abs(camX - _mapX) + Math.Abs(camZ - _mapZ) > 1500;
        if (_job == null && (_timer <= 0 || moved))
        {
            _timer = 3;
            double step = Size / Pixels;
            double cx = Math.Round(camX / step) * step, cz = Math.Round(camZ / step) * step;
            _jobX = cx; _jobZ = cz;
            _job = Background.Run(() => Build(field, terrain, gameHours, cx, cz));
            if (now) { _job.Wait(); Update(field, terrain, gameHours, camX, camZ, originX, originZ, playerRatio, 0, false); return; }
        }
        if (double.IsNaN(_mapX)) { Disable(); return; }
        // The map's corner in scene coordinates; w carries the player's gusts and storm outflow to every plant.
        RenderingServer.GlobalShaderParameterSet("wind_map_frame", new Vector4((float)(_mapX - Size / 2 - originX),
            (float)(_mapZ - Size / 2 - originZ), (float)(1 / Size), Math.Clamp(playerRatio, .5f, 3f)));
    }

    public static void Disable() => RenderingServer.GlobalShaderParameterSet("wind_map_frame", Vector4.Zero);

    private static byte[] Build(WeatherField field, TerrainSampler terrain, double gameHours, double cx, double cz)
    {
        var data = new byte[Pixels * Pixels * 4];
        double step = Size / Pixels;
        for (int j = 0; j < Pixels; j++)
            for (int i = 0; i < Pixels; i++)
            {
                var (dx, dz, speed) = field.WindAt(terrain, cx - Size / 2 + (i + .5) * step, cz - Size / 2 + (j + .5) * step, gameHours);
                int k = (j * Pixels + i) * 4;
                data[k] = (byte)Math.Clamp((dx * .5 + .5) * 255, 0, 255);
                data[k + 1] = (byte)Math.Clamp((dz * .5 + .5) * 255, 0, 255);
                data[k + 2] = (byte)Math.Clamp(speed / 32 * 255, 0, 255);
                data[k + 3] = 255;
            }
        return data;
    }
}
