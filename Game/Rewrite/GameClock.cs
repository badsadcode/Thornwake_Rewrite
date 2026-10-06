using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>Where the sun, moon and stars are (degrees; azimuth from north, clockwise; elevation above the horizon).</summary>
public readonly record struct SkyState(double Hour, int Day, double SunElevation, double SunAzimuth, double MoonElevation,
    double MoonAzimuth, double MoonPhase, Basis Stars, double Latitude, double DaylightHours);

/// <summary>
/// The game's day/night clock (Tom, round 7). One in-game day lasts <see cref="GameSettings.DayLength"/> real minutes
/// (default 120, up to real time). Daylight takes its realistic share of the day — from the latitude where the player
/// is and the season — or a custom share; the clock runs day and night at different speeds to fit them into the real
/// minutes. Sun and moon positions come from simple spherical astronomy (declination from the season, a 29.5-day moon
/// cycle); the star field turns once a day around the celestial pole.
/// </summary>
public sealed class GameClock
{
    /// <summary>Position in the real day cycle (0 = sunrise … day share = sunset … 1 = next sunrise).</summary>
    private double _phase = .2;
    public int Day { get; private set; } = 1;
    public double Hour { get; private set; } = 10.5;

    /// <summary>Season → solar declination (degrees).</summary>
    public static double Declination(int season) => season switch { 0 => 0, 1 => 23.44, 2 => 0, _ => -23.44 };

    /// <summary>Hours of daylight at a latitude and declination (sunrise to sunset, with ~0.8° for refraction).</summary>
    public static double DaylightHours(double latitude, double declination)
    {
        double phi = Mathf.DegToRad(latitude), d = Mathf.DegToRad(declination), h0 = Mathf.DegToRad(-.83);
        double c = (Math.Sin(h0) - Math.Sin(phi) * Math.Sin(d)) / (Math.Cos(phi) * Math.Cos(d));
        if (c <= -1) return 24; // midnight sun
        if (c >= 1) return 0;   // polar night
        return 2 * Mathf.RadToDeg(Math.Acos(c)) / 15;
    }

    /// <summary>Daylight hours and the share of the real cycle given to daylight under the current settings.</summary>
    public static (double Hours, double Share) Daylight(double latitude)
    {
        double hours = Math.Clamp(DaylightHours(latitude, Declination(GameSettings.Season.Index)), .5, 23.5);
        double share = GameSettings.DaylightShare.Value > 0 ? GameSettings.DaylightShare.Value / 100 : hours / 24;
        return (hours, Math.Clamp(share, .05, .95));
    }

    /// <summary>Set the day number (debug dumps, --day; the weather follows day and hour).</summary>
    public void SetDay(int day) => Day = Math.Max(1, day);

    /// <summary>Set the clock to an hour of the day (Esc menu slider, --hour).</summary>
    public void SetHour(double hour, double latitude)
    {
        var (hours, share) = Daylight(latitude);
        double sunrise = 12 - hours / 2;
        double sinceRise = ((hour - sunrise) % 24 + 24) % 24;
        _phase = sinceRise < hours ? sinceRise / hours * share : share + (sinceRise - hours) / (24 - hours) * (1 - share);
        Hour = hour;
    }

    /// <summary>Advance by real seconds (paused when the setting says so) and compute the sky at a latitude.</summary>
    public SkyState Advance(double realSeconds, double latitude, bool run)
    {
        var (hours, share) = Daylight(latitude);
        if (run && !GameSettings.TimePaused.On)
        {
            _phase += realSeconds / (GameSettings.DayLength.Value * 60);
            while (_phase >= 1) { _phase -= 1; Day++; }
        }
        double sunrise = 12 - hours / 2;
        Hour = _phase < share ? sunrise + _phase / share * hours : sunrise + hours + (_phase - share) / (1 - share) * (24 - hours);
        Hour = (Hour % 24 + 24) % 24;
        return Sky(latitude, hours);
    }

    private SkyState Sky(double latitude, double daylight)
    {
        double decl = Declination(GameSettings.Season.Index);
        var (sunEl, sunAz) = Position(latitude, decl, (Hour - 12) * 15);
        // Moon: a 29.5-day cycle; it runs behind the sun by the phase angle (new moon with the sun, full moon opposite).
        double phase = ((Day - 1 + Hour / 24) / 29.53 + .35) % 1; // start a few days before full
        double moonDecl = -decl * Math.Cos(phase * Math.Tau) + 5 * Math.Sin(Day * .41);
        var (moonEl, moonAz) = Position(latitude, moonDecl, (Hour - 12) * 15 - phase * 360);
        // Stars: the celestial sphere turns once a day around the pole (tilted by the latitude); a little each day.
        double turn = Mathf.DegToRad((Hour - 12) * 15 + Day * .9856);
        var stars = new Basis(Vector3.Right, (float)Mathf.DegToRad(90 - latitude)) * new Basis(Vector3.Up, (float)turn);
        return new SkyState(Hour, Day, sunEl, sunAz, moonEl, moonAz, phase, stars.Orthonormalized(), latitude, daylight);
    }

    /// <summary>Elevation and azimuth (from north, clockwise) for a declination and hour angle (degrees).</summary>
    public static (double Elevation, double Azimuth) Position(double latitude, double declination, double hourAngle)
    {
        double phi = Mathf.DegToRad(latitude), d = Mathf.DegToRad(declination), h = Mathf.DegToRad(hourAngle);
        double sinEl = Math.Sin(phi) * Math.Sin(d) + Math.Cos(phi) * Math.Cos(d) * Math.Cos(h);
        double el = Math.Asin(Math.Clamp(sinEl, -1, 1));
        double az = Math.Atan2(-Math.Sin(h) * Math.Cos(d), Math.Cos(phi) * Math.Sin(d) - Math.Sin(phi) * Math.Cos(d) * Math.Cos(h));
        return (Mathf.RadToDeg(el), (Mathf.RadToDeg(az) + 360) % 360);
    }

    /// <summary>Unit vector toward a sky position (north = −Z, east = +X, up = +Y).</summary>
    public static Vector3 Direction(double elevation, double azimuth)
    {
        double el = Mathf.DegToRad(elevation), az = Mathf.DegToRad(azimuth);
        return new Vector3((float)(Math.Cos(el) * Math.Sin(az)), (float)Math.Sin(el), (float)(-Math.Cos(el) * Math.Cos(az)));
    }

    public string Describe() => $"Day {Day}, {(int)Hour:00}:{(int)((Hour - (int)Hour) * 60):00}";
}
