using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Thornwake.WorldGen;

/// <summary>Climate knobs of a world (creation screen; D42). Defaults give a temperate world with colder north and
/// warmer south, spanning more latitude the bigger the world.</summary>
public sealed record ClimateSettings
{
    /// <summary>Latitude (degrees) at the world's centre: 0 equator, 45 temperate, 70 arctic.</summary>
    public double CentreLatitude { get; init; } = 45;
    /// <summary>Degrees of latitude from the south edge to the north edge; ≤ 0 = auto from the world size.</summary>
    public double LatitudeSpan { get; init; } = -1;
    /// <summary>Added to every temperature (°C).</summary>
    public double TemperatureOffset { get; init; }
    /// <summary>Multiplier on all rain and snow.</summary>
    public double Moisture { get; init; } = 1;
    /// <summary>Biomes on; off = the classic single temperate look of build 9 and earlier (Terrain version option).</summary>
    public bool Biomes { get; init; } = true;

    public double Span(double worldSize) => LatitudeSpan > 0 ? LatitudeSpan : Math.Clamp(6 + 64 * Math.Sqrt(worldSize / 4096000), 6, 70);
}

/// <summary>
/// Annual mean temperature and precipitation over the world (D42), on a raster half the macro resolution:
/// temperature from latitude (north = −z) and elevation (−6.5 °C/km); precipitation from prevailing winds by latitude
/// band (trade winds from the east below 30°, westerlies to 60°, polar easterlies beyond) carrying sea moisture
/// inland: air picks moisture up over the sea, rains it out over land (more where it is forced up slopes: wet
/// windward mountains, dry rain shadows), and cold air holds less. Deterministic and engine-free.
/// </summary>
public sealed class Climate
{
    public const int TemperatureChannel = 0, PrecipitationChannel = 1;
    public MacroRaster Raster { get; }
    public ClimateSettings Settings { get; }
    private readonly double _north, _south, _world, _originZ, _originX;

    public Climate(WorldPlan plan, ClimateSettings? settings = null)
    {
        Settings = settings ?? new ClimateSettings();
        var macro = plan.Macro;
        var p = plan.Profile;
        _world = p.WorldSize; _originZ = plan.Manifest.OriginZ; _originX = plan.Manifest.OriginX;
        double span = Settings.Span(_world);
        _north = Settings.CentreLatitude + span / 2; _south = Settings.CentreLatitude - span / 2;
        int n = Math.Max(128, macro.Size / 2);
        double cell = _world / n;
        Raster = new MacroRaster(n, macro.MinX, macro.MinZ, cell, 2);
        var temperature = Raster.Channel(TemperatureChannel);
        var precipitation = Raster.Channel(PrecipitationChannel);
        var height = new double[n * n];
        var land = new bool[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                double x = Raster.CellX(i), z = Raster.CellZ(j);
                macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double l, out double h, out _);
                int k = j * n + i;
                land[k] = l > 0;
                height[k] = Math.Max(0, h);
                temperature[k] = (float)(SeaLevelTemperature(Latitude(z)) - .0065 * height[k] + Settings.TemperatureOffset);
            }

        // Moisture carried by the wind along each row (east–west winds; the sweep direction follows the latitude band).
        double perKm = cell / 1000;
        Parallel.For(0, n, j =>
        {
            double lat = Latitude(Raster.CellZ(j));
            bool fromWest = Math.Abs(lat) >= 30 && Math.Abs(lat) < 60;
            double air = 1;
            for (int s = 0; s < n; s++)
            {
                int i = fromWest ? s : n - 1 - s, k = j * n + i;
                int prev = fromWest ? Math.Max(0, i - 1) : Math.Min(n - 1, i + 1);
                if (!land[k])
                {
                    air += (1 - air) * Math.Min(1, .03 * perKm); // the sea refills the air over ~30 km
                    precipitation[k] = (float)(900 * air);
                    continue;
                }
                double rise = Math.Max(0, height[k] - height[j * n + prev]) / cell; // slope the wind climbs
                double capacity = DetMath.Clamp((temperature[k] + 18) / 38, .2, 1);  // cold air holds less
                double rain = air * Math.Min(.5, (.0028 + rise * 1.4) * perKm);
                air -= rain;
                precipitation[k] = (float)((250 + 2400 * air * capacity + rain / perKm * 900) * Settings.Moisture);
            }
        });
        // Winds are not exact rows: a soft blur, a few cells wide.
        Blur(precipitation, n, Math.Max(1, (int)Math.Round(15000 / cell)));
    }

    /// <summary>Latitude (degrees) at world z (north edge at the most negative z).</summary>
    public double Latitude(double z)
    {
        double t = Math.Clamp((z - _originZ) / _world + .5, 0, 1); // 0 north edge … 1 south edge
        return _north + (_south - _north) * t;
    }

    /// <summary>Annual mean sea-level temperature (°C) at a latitude: ~28 °C at the equator, ~12 at 45°, ~0 at 60°.</summary>
    public static double SeaLevelTemperature(double latitude) => 28 - .0078 * latitude * latitude;

    /// <summary>Temperature (°C) and precipitation (mm/year) at a world point and ground height (m above sea).</summary>
    public (double Temperature, double Precipitation) At(double x, double z, double elevation)
    {
        // Temperature straight from latitude and the actual ground height (walking-scale relief included);
        // precipitation from the raster.
        double p = Raster.Sample(PrecipitationChannel, x, z);
        return (SeaLevelTemperature(Latitude(z)) + Settings.TemperatureOffset - .0065 * Math.Max(0, elevation), p);
    }

    /// <summary>Snow cover 0..1 from the ground set and the cold; without biomes the classic fixed snow line (~2.7 km).</summary>
    public double Snow(double groundSnow, double temperature, double elevation) => Settings.Biomes
        ? Math.Max(groundSnow, DetMath.Smooth((-4 - temperature) / 5))
        : DetMath.Smooth((elevation - 2450) / 400);

    private static void Blur(float[] a, int n, int r)
    {
        var tmp = new float[a.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            Parallel.For(0, n, j =>
            {
                double sum = 0; int count = 0;
                for (int i = -r; i <= r; i++) if (i >= 0 && i < n) { sum += a[j * n + i]; count++; }
                for (int i = 0; i < n; i++)
                {
                    tmp[j * n + i] = (float)(sum / count);
                    int add = i + r + 1, rem = i - r;
                    if (add < n) { sum += a[j * n + add]; count++; }
                    if (rem >= 0) { sum -= a[j * n + rem]; count--; }
                }
            });
            Parallel.For(0, n, i =>
            {
                double sum = 0; int count = 0;
                for (int j = -r; j <= r; j++) if (j >= 0 && j < n) { sum += tmp[j * n + i]; count++; }
                for (int j = 0; j < n; j++)
                {
                    a[j * n + i] = (float)(sum / count);
                    int add = j + r + 1, rem = j - r;
                    if (add < n) { sum += tmp[add * n + i]; count++; }
                    if (rem >= 0) { sum -= tmp[rem * n + i]; count--; }
                }
            });
        }
    }
}
