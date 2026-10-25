using System;
using System.Globalization;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Esc → Weather test (D206; Tom: "I need proper tools to test lightnings and rain intensity"): rain here at any strength
/// (drizzle … downpour), the wind's speed and direction, lightning strikes at a chosen distance and a test storm that keeps
/// striking around you. Test overrides only: nothing is saved, and switching a test off gives the weather back. The
/// lightning works with either weather model (the classic one has no Lightning node of its own; the test makes one).
/// </summary>
public partial class WorldLab
{
    private bool _testRain, _testWind, _testStorm;
    private float _testRainAmount = .6f, _testWindSpeed = 10, _testWindFrom = 315, _testStormEvery = 6;
    private double _testStormNext;
    private Label? _weatherTestInfo;
    private readonly Random _testRandom = new(4242);

    private Control BuildWeatherTestTab()
    {
        var box = new VBoxContainer { Name = "Weather test" };
        box.AddChild(new Label
        {
            Text = "Test the weather by hand. Nothing here is saved; switch a test off to give the weather back. The menu closes with Esc " +
                   "so you can watch; the tests keep running.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(900, 0), Modulate = new Color(1, 1, 1, .75f),
        });

        Control Row(string label, Control a, Control? b = null)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(220, 0) });
            a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(a);
            if (b != null) row.AddChild(b);
            return row;
        }
        Label Value(string text) => new() { Text = text, CustomMinimumSize = new Vector2(150, 0) };

        // Rain
        box.AddChild(new Label { Text = "Rain" });
        var rainOn = new CheckButton { Text = "Rain here (test)" };
        var rainValue = Value("");
        var rainSlider = new HSlider { MinValue = 0, MaxValue = 1, Step = .01, Value = _testRainAmount };
        void RainText() => rainValue.Text = $"{_testRainAmount:0.00} {RainName(_testRainAmount)}";
        rainSlider.ValueChanged += v => { _testRainAmount = (float)v; RainText(); };
        rainOn.Toggled += on => { _testRain = on; if (!on) _shotRainHere = -1; };
        RainText();
        box.AddChild(rainOn);
        box.AddChild(Row("Strength", rainSlider, rainValue));
        var presets = new HBoxContainer();
        foreach (var (name, v) in new[] { ("Drizzle", .12f), ("Light", .3f), ("Moderate", .55f), ("Heavy", .8f), ("Downpour", 1f) })
        {
            var b = new Button { Text = name };
            b.Pressed += () => { rainOn.ButtonPressed = true; rainSlider.Value = v; };
            presets.AddChild(b);
        }
        box.AddChild(presets);

        // Wind
        box.AddChild(new Label { Text = "Wind" });
        var windOn = new CheckButton { Text = "Wind (test)" };
        var speedValue = Value("");
        var speedSlider = new HSlider { MinValue = 0, MaxValue = 30, Step = .5, Value = _testWindSpeed };
        speedSlider.ValueChanged += v => { _testWindSpeed = (float)v; speedValue.Text = $"{v:0.0} m/s"; ApplyTestWind(); };
        speedValue.Text = $"{_testWindSpeed:0.0} m/s";
        var fromValue = Value("");
        var fromSlider = new HSlider { MinValue = 0, MaxValue = 360, Step = 5, Value = _testWindFrom };
        void FromText() => fromValue.Text = $"from {_testWindFrom:0}° {Compass(_testWindFrom)}";
        fromSlider.ValueChanged += v => { _testWindFrom = (float)v; FromText(); ApplyTestWind(); };
        FromText();
        windOn.Toggled += on => { _testWind = on; ApplyTestWind(); };
        box.AddChild(windOn);
        box.AddChild(Row("Speed", speedSlider, speedValue));
        box.AddChild(Row("Blowing from", fromSlider, fromValue));

        // Lightning
        box.AddChild(new Label { Text = "Lightning" });
        var strikes = new HBoxContainer();
        strikes.AddChild(new Label { Text = "Strike ahead at", CustomMinimumSize = new Vector2(220, 0) });
        foreach (var d in new[] { 150, 400, 1000, 3000, 8000, 20000 })
        {
            var b = new Button { Text = d >= 1000 ? $"{d / 1000} km" : $"{d} m" };
            b.Pressed += () => TestStrike(d, ahead: true);
            strikes.AddChild(b);
        }
        box.AddChild(strikes);
        var stormOn = new CheckButton { Text = "Test storm: strikes all around you" };
        var everyValue = Value("");
        var everySlider = new HSlider { MinValue = 1, MaxValue = 30, Step = .5, Value = _testStormEvery };
        everySlider.ValueChanged += v => { _testStormEvery = (float)v; everyValue.Text = $"every ~{v:0.#} s"; };
        everyValue.Text = $"every ~{_testStormEvery:0.#} s";
        stormOn.Toggled += on => { _testStorm = on; _testStormNext = 0; };
        box.AddChild(stormOn);
        box.AddChild(Row("How often", everySlider, everyValue));
        box.AddChild(new Label
        {
            Text = "Strikes are 0.3–8 km away in the storm (a few close ones). The thunder comes at the speed of sound: count the seconds × 343 m.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(900, 0), Modulate = new Color(1, 1, 1, .6f),
        });

        _weatherTestInfo = new Label { Modulate = new Color(1, 1, 1, .8f) };
        box.AddChild(_weatherTestInfo);
        return box;
    }

    private static string RainName(float p) => p < .05f ? "(none)" : p < .2f ? "drizzle" : p < .4f ? "light rain" : p < .65f ? "moderate rain" : p < .9f ? "heavy rain" : "downpour";

    private static string Compass(float from)
    {
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return names[(int)Math.Round(from / 45) % 8];
    }

    private void ApplyTestWind()
    {
        Wind.ForceSpeed = _testWind ? _testWindSpeed : _cmdWindSpeed;
        // (from: where it comes from, 0 = north = −Z; the wind blows toward the opposite side)
        float a = Mathf.DegToRad(_testWindFrom);
        Wind.ForceDirection = _testWind ? new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) : null;
    }

    private float? _cmdWindSpeed;
    private bool _testLightning; // (a test strike made or kept the Lightning node: the classic model keeps it)

    /// <summary>After the weather each frame: the rain test (and <c>--rain-here</c>) overrides the rain here — also with the
    /// weather off (Settings → Simulation → Weather), where nothing else would rain.</summary>
    private void WeatherTestApply()
    {
        if (_testRain) _shotRainHere = _testRainAmount;
        if (_shotRainHere >= 0) _atmosphere.Precipitation = _shotRainHere;
    }

    /// <summary>A lightning strike for the tests: ahead of the camera, or in a random direction (the test storm).</summary>
    private void TestStrike(double distance, bool ahead)
    {
        EnsureTestLightning();
        var forward = -_camera.GlobalBasis.Z;
        if (!ahead)
        {
            float a = (float)(_testRandom.NextDouble() * Math.Tau);
            forward = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        }
        _lightning!.StrikeNear(_camera.GlobalPosition, forward, _originX, _originZ, distance);
        _status = string.Create(CultureInfo.InvariantCulture, $"Lightning (test) {distance / 1000:0.0} km away: the thunder in ~{distance / 343:0} s.");
    }

    private void EnsureTestLightning()
    {
        _testLightning = true;
        if (_lightning != null) return;
        _lightning = new Lightning { GroundAt = (x, z) => _sampler?.Height(x, z, 32) ?? 0 };
        AddChild(_lightning);
    }

    /// <summary>Per frame: the test storm, the lightning with the classic weather model, the readout.</summary>
    private void WeatherTestStep(double delta, SkyState sky, Vector3 cam)
    {
        if (_testStorm && _sampler != null)
        {
            _testStormNext -= delta;
            if (_testStormNext <= 0)
            {
                _testStormNext = _testStormEvery * (.4 + 1.2 * _testRandom.NextDouble());
                // mostly 1–8 km, now and then close
                double d = _testRandom.NextDouble() < .15 ? 300 + _testRandom.NextDouble() * 700 : 1000 + _testRandom.NextDouble() * 7000;
                TestStrike(d, ahead: false);
            }
        }
        // The classic model has no Lightning of its own: a test strike made one; step it with no storm cells.
        bool simulated = GameSettings.WeatherModel.Index == 1 && GameSettings.WeatherOn.On;
        // (the rain test brings the rain sound too: it lives in the Lightning node, which the weather off has none of)
        if (_shotRainHere >= 0) EnsureTestLightning();
        if (_lightning != null) _lightning.Shelter = _atmosphere.Sheltered;
        if (!simulated && _lightning != null)
            _lightning.Step(delta, sky.Day * 24 + sky.Hour, null, false, _atmosphere.Snowing, cam, _originX, _originZ,
                _atmosphere.Precipitation, _atmosphere.Wind.Speed, _atmosphere.Wind.Gust);
        if (_weatherTestInfo != null && _menu.Visible)
            _weatherTestInfo.Text = string.Create(CultureInfo.InvariantCulture,
                $"Now: rain {_atmosphere.Precipitation:0.00} ({RainName(_atmosphere.Precipitation)}{(_atmosphere.Snowing ? ", snow" : "")}) · wind {_atmosphere.Wind.Speed:0.0} m/s from {_atmosphere.Wind.Compass()} · " +
                $"weather model {(simulated ? "simulated" : GameSettings.WeatherOn.On ? "classic" : "off")}{(_testRain || _testWind || _testStorm ? " · tests on" : "")}");
    }
}
