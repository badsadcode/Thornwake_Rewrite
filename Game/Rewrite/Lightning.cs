using System;
using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Lightning and thunder (round 27, Tom: "lightning storms and thunder with nice sound propagation"). Strikes come
/// from real thunderstorm cells (simulated weather; in the classic model from a storm over the player): a branching
/// bolt to the ground (or a flash inside the cloud), a return stroke and re-strikes flickering for half a second, the
/// land lit from the strike and the clouds glowing toward it (global <c>lightning</c>). The thunder is built from
/// the bolt itself: every segment's sound sets off at once but reaches the listener at its own distance / 343 m/s,
/// so a long bolt rolls for seconds; the air takes the highs away with distance (a crack close by, a low rumble far
/// off), the land returns late echoes, and it is heard only after the light, from the bolt's direction.
/// Also the rain and wind sound (continuous, synthesized): drizzle hiss to a downpour roar, wind rising and falling
/// with the gusts.
/// </summary>
public partial class Lightning : Node3D
{
    private const int Rate = 22050;
    private const double SpeedOfSound = 343;

    /// <summary>World ground height (m) at a world x, z (for strike points).</summary>
    public Func<double, double, double>? GroundAt;

    private readonly Random _random = new(9091);
    private readonly List<Strike> _strikes = new();
    private readonly List<(double At, AudioStreamWav Sound, Vector3 Dir, float Db)> _pendingThunder = new();
    private DirectionalLight3D _flash = null!;
    private MeshInstance3D _boltMesh = null!;
    private ImmediateMesh _immediate = null!;
    private double _time;
    private readonly Dictionary<(long, long), double> _nextFor = new();

    private sealed class Strike
    {
        public double Start, WorldX, WorldZ, Distance;
        public List<(Vector3 A, Vector3 B, float Width)> Segments = new();
        public bool Ground;
        public int Restrikes;
        public float Brightness;
    }

    public override void _Ready()
    {
        _flash = new DirectionalLight3D { ShadowEnabled = false, LightColor = new Color(.78f, .84f, 1f), Visible = false, LightEnergy = 0 };
        AddChild(_flash);
        _immediate = new ImmediateMesh();
        var material = new ShaderMaterial { Shader = new Shader { Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
void fragment() {
	// Bright core, soft glow toward the ribbon's edges (UV.x across); COLOR.a = strike brightness.
	float across = 1.0 - abs(UV.x * 2.0 - 1.0);
	ALBEDO = vec3(0.82, 0.88, 1.0) * (pow(across, 6.0) * 9.0 + across * 1.2) * COLOR.a;
}" } };
        _boltMesh = new MeshInstance3D { Mesh = _immediate, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 50000 };
        AddChild(_boltMesh);
        SetupAmbience();
    }

    /// <summary>
    /// Per frame: <paramref name="cells"/> in world coordinates (or null: classic model, <paramref name="thunderHere"/>
    /// = a storm over the player); <paramref name="camera"/> in the scene; the scene origin in the world.
    /// </summary>
    public void Step(double delta, double gameHours, IReadOnlyList<WeatherField.Cell>? cells, bool thunderHere, bool snowing,
        Vector3 camera, double originX, double originZ, float precipitation, float windSpeed, float gust)
    {
        _time += delta;
        double camX = originX + camera.X, camZ = originZ + camera.Z;
        if (!snowing)
        {
            if (cells != null)
            {
                foreach (var c in cells)
                    if (c.Thunder) CellStrikes(c, gameHours, camX, camZ, camera.Y, originX, originZ);
                if (_fired.Count > 4000) _fired.Clear();
            }
            else if (thunderHere)
                MaybeStrike(camX + 2500, camZ - 1500, 6000, .8, delta, camX, camZ, camera.Y, originX, originZ);
        }
        // Thunder whose sound has now arrived.
        for (int i = _pendingThunder.Count - 1; i >= 0; i--)
        {
            var (at, sound, dir, db) = _pendingThunder[i];
            if (_time < at) continue;
            _pendingThunder.RemoveAt(i);
            var player = new AudioStreamPlayer3D
            {
                Stream = sound, VolumeDb = db + Mathf.LinearToDb(Math.Max(.001f, GameSettings.WeatherVolume.F * GameSettings.ThunderVolume.F)),
                AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled, MaxDistance = 0, DopplerTracking = AudioStreamPlayer3D.DopplerTrackingEnum.Disabled,
                Position = camera + dir * 40,
            };
            AddChild(player);
            player.Finished += player.QueueFree;
            player.Play();
        }
        DrawStrikes(camera, originX, originZ);
        StepAmbience(precipitation, snowing, windSpeed, gust);
    }

    private readonly HashSet<(ulong, long, int)> _fired = new();

    /// <summary>
    /// Strikes of a thunderstorm cell, the same for every player (round 28, multiplayer): game time is cut into slots of
    /// ~3 real seconds (at the shared Day length); in each slot a cell may strike up to twice, more likely the stronger
    /// it is, at a moment, spot, kind and bolt shape all derived from the cell's id and the slot. Each player only
    /// hears it at their own distance / 343 m/s.
    /// </summary>
    private void CellStrikes(WeatherField.Cell c, double gameHours, double camX, double camZ, float camY, double originX, double originZ)
    {
        double slot = 1.2 / Math.Max(10, GameSettings.DayLength.Value); // game hours in ~3 real seconds
        long now = (long)Math.Floor(gameHours / slot);
        for (long s = now - 1; s <= now; s++)
            for (int k = 0; k < 2; k++)
            {
                ulong h = Thornwake.WorldGen.Hashing.Mix(c.Id * 31 + (ulong)s * 0x9E3779B97F4A7C15UL + (ulong)k * 7919);
                if (Thornwake.WorldGen.Hashing.Unit(h) > c.Intensity * c.Intensity * .6) continue;
                double at = (s + Thornwake.WorldGen.Hashing.Unit(Thornwake.WorldGen.Hashing.Mix(h + 1))) * slot;
                if (at > gameHours || at < gameHours - 2 * slot) continue;
                if (!_fired.Add((c.Id, s, k))) continue;
                double u = Math.Max(1e-6, Thornwake.WorldGen.Hashing.Unit(Thornwake.WorldGen.Hashing.Mix(h + 2)));
                double r = c.Radius * Math.Sqrt(-2 * Math.Log(u)) * .55;
                double a = Thornwake.WorldGen.Hashing.Unit(Thornwake.WorldGen.Hashing.Mix(h + 3)) * Math.Tau;
                bool ground = Thornwake.WorldGen.Hashing.Unit(Thornwake.WorldGen.Hashing.Mix(h + 4)) < .45;
                CreateStrike(c.X + Math.Cos(a) * r, c.Z + Math.Sin(a) * r, c.Intensity, ground, false, camX, camZ, camY, originX, originZ,
                    (int)(h >> 33));
            }
    }

    private void MaybeStrike(double cx, double cz, double radius, double intensity, double delta, double camX, double camZ,
        float camY, double originX, double originZ)
    {
        var key = ((long)Math.Round(cx / 1000), (long)Math.Round(cz / 1000));
        // A mature cell: a strike every ~4 s; a weak one every ~40 s.
        double interval = 4 / Math.Max(.1, intensity * intensity);
        if (!_nextFor.TryGetValue(key, out double next)) { _nextFor[key] = _time + _random.NextDouble() * interval; return; }
        if (_time < next) return;
        _nextFor[key] = _time + interval * (.4 + 1.2 * _random.NextDouble());
        // Where: scattered over the core (gaussian-ish), now and then out ahead of it ("bolt from the blue").
        double r = radius * Math.Sqrt(-2 * Math.Log(Math.Max(1e-6, _random.NextDouble()))) * .55;
        double a = _random.NextDouble() * Math.Tau;
        CreateStrike(cx + Math.Cos(a) * r, cz + Math.Sin(a) * r, intensity, _random.NextDouble() < .45, false, camX, camZ, camY, originX, originZ,
            _random.Next());
    }

    /// <summary>A strike in front of the camera (L key, screenshots): 0.3–8 km away, or at <paramref name="distance"/>.</summary>
    public void StrikeNear(Vector3 camera, Vector3 forward, double originX, double originZ, double distance = -1, bool hold = false)
    {
        var f = new Vector2(forward.X, forward.Z);
        f = f.LengthSquared() > 1e-6f ? f.Normalized() : Vector2.Up;
        double d = distance > 0 ? distance : 300 + _random.NextDouble() * 7700;
        double side = (_random.NextDouble() - .5) * d * .5;
        double x = originX + camera.X + f.X * d - f.Y * side, z = originZ + camera.Z + f.Y * d + f.X * side;
        CreateStrike(x, z, 1, true, hold, originX + camera.X, originZ + camera.Z, camera.Y, originX, originZ, _random.Next());
    }

    private bool _hold;

    private Random _rng = new(1);

    private void CreateStrike(double x, double z, double intensity, bool toGround, bool hold, double camX, double camZ, float camY,
        double originX, double originZ, int seed)
    {
        _hold = hold;
        _rng = new Random(seed); // the bolt's shape, height and re-strikes follow from the seed (same for every player)
        double ground = GroundAt?.Invoke(x, z) ?? 0;
        double dx = x - camX, dz = z - camZ, distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance > 60000) return;
        var s = new Strike { Start = _time, WorldX = x, WorldZ = z, Distance = distance, Ground = toGround,
            Restrikes = _rng.Next(0, 4), Brightness = (float)(.6 + .4 * intensity) };
        double baseY = ground + 1300 + 600 * _rng.NextDouble();
        var top = new Vector3((float)(x - originX), (float)baseY, (float)(z - originZ));
        var bottom = new Vector3((float)(x - originX), (float)ground, (float)(z - originZ));
        if (!s.Ground) bottom = top + new Vector3((float)(_rng.NextDouble() - .5) * 4000, -300, (float)(_rng.NextDouble() - .5) * 4000);
        float width = (float)Math.Max(4, distance * .004); // a few pixels wide at any distance (the glow does the rest)
        BuildBolt(s.Segments, top, bottom, width, 0);
        _strikes.Add(s);
        QueueThunder(s, camX, camZ, camY, originX, originZ, seed);
    }

    /// <summary>A jagged bolt by midpoint displacement, with branches that fork off and die out.</summary>
    private void BuildBolt(List<(Vector3, Vector3, float)> into, Vector3 a, Vector3 b, float width, int depth)
    {
        var points = new List<Vector3> { a, b };
        float rough = (a - b).Length() * .18f;
        for (int pass = 0; pass < 7; pass++)
        {
            var next = new List<Vector3>(points.Count * 2);
            for (int i = 0; i < points.Count - 1; i++)
            {
                next.Add(points[i]);
                var m = (points[i] + points[i + 1]) * .5f;
                m += new Vector3((float)(_rng.NextDouble() - .5), (float)(_rng.NextDouble() - .5) * .4f, (float)(_rng.NextDouble() - .5)) * rough;
                next.Add(m);
            }
            next.Add(points[^1]);
            points = next;
            rough *= .52f;
        }
        for (int i = 0; i < points.Count - 1; i++)
        {
            into.Add((points[i], points[i + 1], width));
            if (depth < 2 && i > 4 && i < points.Count - 8 && _rng.NextDouble() < (depth == 0 ? .06 : .03))
            {
                var dir = (points[i + 1] - points[i]).Normalized();
                var side = new Vector3((float)(_rng.NextDouble() - .5), -.6f, (float)(_rng.NextDouble() - .5)).Normalized();
                float length = (a - b).Length() * (float)(.12 + .2 * _rng.NextDouble()) / (depth + 1);
                BuildBolt(into, points[i], points[i] + (dir + side).Normalized() * length, width * .45f, depth + 1);
            }
        }
    }

    /// <summary>The brightness of a strike over time: return stroke, dark gaps, re-strikes, fading channel.</summary>
    private static float StrikeLight(Strike s, double t)
    {
        if (t < 0) return 0;
        float b = t < .06 ? 1 : t < .09 ? .15f : 0;
        for (int k = 0; k < s.Restrikes; k++)
        {
            double at = .09 + k * .07;
            if (t >= at && t < at + .045) b = Math.Max(b, .75f - k * .12f);
        }
        if (t >= .09 + s.Restrikes * .07 && t < .5) b = Math.Max(b, (float)(.25 * (1 - (t - .09 - s.Restrikes * .07) / .4)));
        return b * s.Brightness;
    }

    private void DrawStrikes(Vector3 camera, double originX, double originZ)
    {
        _immediate.ClearSurfaces();
        float flash = 0; Vector3 flashDir = Vector3.Down; float skyGlow = 0; Vector2 skyDir = Vector2.Zero; float skyDist = 0;
        bool any = false;
        for (int i = _strikes.Count - 1; i >= 0; i--)
        {
            var s = _strikes[i];
            double t = _hold ? .03 : _time - s.Start;
            if (t > .6) { _strikes.RemoveAt(i); continue; }
            float light = StrikeLight(s, t);
            if (light <= 0) continue;
            // The land lit from the strike: strong close by, gone by ~15 km (the cloud hides it).
            float reach = (float)Math.Exp(-s.Distance / 5000);
            float f = light * (s.Ground ? 1 : .55f) * (.12f + 1.7f * reach);
            var to = new Vector3((float)(s.WorldX - originX), 0, (float)(s.WorldZ - originZ)) - camera;
            if (f > flash)
            {
                flash = f;
                flashDir = (new Vector3(-to.X, -1500 - (float)s.Distance * .1f, -to.Z)).Normalized();
            }
            float g = light * (s.Ground ? 1 : .8f);
            if (g > skyGlow) { skyGlow = g; skyDir = new Vector2(to.X, to.Z).Normalized(); skyDist = (float)(s.Distance / 1000); }
            if (!s.Ground && s.Distance > 3000) continue; // in-cloud flashes show only as glow
            if (!any) { _immediate.SurfaceBegin(Mesh.PrimitiveType.Triangles); any = true; }
            foreach (var (a, b, w) in s.Segments) Ribbon(a, b, w, light, camera);
        }
        if (any) _immediate.SurfaceEnd();
        _flash.Visible = flash > .01f;
        _flash.LightEnergy = flash;
        if (flash > .01f) _flash.Basis = Basis.LookingAt(flashDir, Mathf.Abs(flashDir.Y) > .99f ? Vector3.Forward : Vector3.Up);
        RenderingServer.GlobalShaderParameterSet("lightning", new Vector4(skyDir.X, skyDir.Y, skyGlow, skyDist));
    }

    private void Ribbon(Vector3 a, Vector3 b, float width, float alpha, Vector3 camera)
    {
        var along = b - a;
        var side = along.Cross((a + b) * .5f - camera).Normalized() * width * .5f;
        var c = new Color(1, 1, 1, alpha);
        void V(Vector3 p, float u) { _immediate.SurfaceSetColor(c); _immediate.SurfaceSetUV(new Vector2(u, 0)); _immediate.SurfaceAddVertex(p); }
        V(a - side, 0); V(a + side, 1); V(b + side, 1);
        V(a - side, 0); V(b + side, 1); V(b - side, 0);
    }

    // ---- thunder

    private void QueueThunder(Strike s, double camX, double camZ, float camY, double originX, double originZ, int seed)
    {
        var cam = new Vector3((float)(camX - originX), camY, (float)(camZ - originZ));
        var (sound, first, db) = Thunder(s.Segments, cam, seed, s.Ground);
        if (sound == null) return;
        var dir = (new Vector3((float)(s.WorldX - camX), 0, (float)(s.WorldZ - camZ))).Normalized();
        _pendingThunder.Add((_time + first, sound, dir, db));
    }

    /// <summary>
    /// Synthesize the thunder of a bolt heard from <paramref name="listener"/>: returns the sound (starting when the
    /// nearest part of the bolt is heard), that delay (s) and its loudness (dB); null when too far to hear.
    /// </summary>
    public static (AudioStreamWav? Sound, double Delay, float Db) Thunder(List<(Vector3 A, Vector3 B, float Width)> segments,
        Vector3 listener, int seed, bool ground)
    {
        var random = new Random(seed);
        double near = double.MaxValue, far = 0;
        var arrivals = new List<(double Distance, double Weight)>(segments.Count);
        foreach (var (a, b, w) in segments)
        {
            double d = (((a + b) * .5f) - listener).Length();
            double len = (a - b).Length();
            near = Math.Min(near, d); far = Math.Max(far, d);
            // Louder for thick (main channel) segments; tortuous channels add random emphasis.
            arrivals.Add((d, len * w * (.5 + random.NextDouble()) / Math.Max(150, d)));
        }
        if (near > 30000) return (null, 0, 0);
        double spread = (far - near) / SpeedOfSound;
        double seconds = Math.Min(16, spread + 2.5 + near / 3000);
        int count = (int)(seconds * Rate);
        // Envelope at 200 Hz: each segment's arrival, smeared a little, then echoes from the land.
        int bins = (int)(seconds * 200) + 1;
        var env = new double[bins];
        foreach (var (d, w) in arrivals)
        {
            int k = (int)((d - near) / SpeedOfSound * 200);
            if (k < bins) env[k] += w;
        }
        // Each arrival rings for ~0.3 s (the channel is not a point); echoes 0.4–3 s later, weaker.
        var ring = new double[bins];
        double decay = Math.Exp(-1.0 / (200 * (.25 + near / 40000)));
        double acc = 0;
        for (int k = 0; k < bins; k++) { acc = acc * decay + env[k]; ring[k] = acc; }
        var total = (double[])ring.Clone();
        for (int e = 0; e < 5; e++)
        {
            int lag = (int)((.4 + random.NextDouble() * 2.6) * 200);
            double g = .18 + .15 * random.NextDouble();
            for (int k = lag; k < bins; k++) total[k] += ring[k - lag] * g;
        }
        // The roll dies away: the first arrivals (the nearest part of the channel) are the loud clap, the rest rumbles off.
        double fadeTime = 1.1 + near / 6000;
        for (int k = 0; k < bins; k++) total[k] *= Math.Exp(-k / 200.0 / fadeTime) * .8 + .2 * Math.Exp(-k / 200.0 / (fadeTime * 3));
        double peak = 0; foreach (var v in total) peak = Math.Max(peak, v);
        if (peak <= 0) return (null, 0, 0);

        // Air takes the highs: ~4 kHz content close by, ~150 Hz at 10 km.
        double cutoff = 70 + 3800 * Math.Exp(-near / 1800);
        double alpha = 1 - Math.Exp(-Math.Tau * cutoff / Rate);
        double alpha2 = 1 - Math.Exp(-Math.Tau * Math.Max(40, cutoff * .35) / Rate);
        double crack = ground ? Math.Clamp((1600 - near) / 1300, 0, 1) : 0;
        var data = new byte[count * 2];
        double lp1 = 0, lp2 = 0, lp3 = 0, brown = 0;
        var samples = new double[count];
        double maxAbs = 1e-9;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / Rate;
            double f = t * 200;
            int k = Math.Min(bins - 2, (int)f);
            double e = (total[k] + (total[k + 1] - total[k]) * (f - k)) / peak;
            double n = random.NextDouble() * 2 - 1;
            lp1 += alpha * (n - lp1); lp2 += alpha * (lp1 - lp2); // two-pole low pass
            lp3 += alpha2 * (lp2 - lp3);
            brown = brown * .995 + n * .05; // the deep body of the rumble
            double s = Math.Pow(Math.Max(0, e), .85) * (lp2 * 1.6 + lp3 * 1.2 + brown * .9);
            // The crack of a close strike: a sharp tearing burst at the start.
            if (crack > 0 && t < .35) s += n * crack * Math.Exp(-t / .045) * 1.6 + lp1 * crack * Math.Exp(-t / .12) * 1.2;
            samples[i] = s;
            maxAbs = Math.Max(maxAbs, Math.Abs(s));
        }
        // Fade in a few ms and out at the end.
        for (int i = 0; i < count; i++)
        {
            double fade = Math.Min(1, i / (Rate * .004)) * Math.Min(1, (count - i) / (Rate * .5));
            short v = (short)Math.Clamp(samples[i] / maxAbs * .9 * fade * 32767, -32767, 32767);
            data[i * 2] = (byte)(v & 255); data[i * 2 + 1] = (byte)((v >> 8) & 255);
        }
        // Loudness: ~1/distance, full at ~700 m.
        float db = Mathf.LinearToDb((float)Math.Clamp(700 / near, .02, 1.4));
        return (new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate }, near / SpeedOfSound, db);
    }

    // ---- rain and wind (continuous, synthesized)
    private AudioStreamGeneratorPlayback? _ambience;
    private double _rainLp, _rainLp2, _rainHi, _windLp, _windLp2, _windBand, _windPhase, _rainLevel, _windLevel, _dropTimer;
    private float _dropAmp;

    private void SetupAmbience()
    {
        var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = .3f }, VolumeDb = -4 };
        AddChild(player);
        player.Play();
        _ambience = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>0 in the open … 1 under cover (Atmosphere.Sheltered, D211): the rain's hiss and ticks drop, a dull patter stays.</summary>
    public float Shelter { get; set; }
    /// <summary>0 … 1 under wet leaves (Atmosphere.LeafDrip, D213): a softer, busier patter in the rain, single drips after it.</summary>
    public float LeafDrip { get; set; }

    private void StepAmbience(float precipitation, bool snowing, float windSpeed, float gust)
    {
        if (_ambience == null) return;
        int frames = _ambience.GetFramesAvailable();
        if (frames <= 0) return;
        float volume = GameSettings.WeatherVolume.F;
        double rainTarget = snowing ? 0 : Math.Pow(Math.Clamp(precipitation, 0, 1), .7) * volume * GameSettings.RainVolume.F;
        double windTarget = Math.Clamp((windSpeed - 2) / 22, 0, 1.2) * (.6 + .5 * gust) * volume * GameSettings.WindVolume.F;
        // Rain tone (round 29, Tom: "rain is good, but could you soften it a bit"): the hiss was raw high-passed white
        // noise; soft tones low-pass it (a gentler patter) and quieten the hiss and the drops.
        double tone = GameSettings.RainTone.F;
        double hissCut = .12 + .88 * tone, hissGain = .35 * (.55 + .45 * tone), dropGain = .25 * (.5 + .5 * tone);
        // (under cover: the open hiss and single drops mostly gone, the deeper roar from outside kept)
        hissCut *= 1 - .7 * Shelter; hissGain *= 1 - .6 * Shelter; dropGain *= 1 - .7 * Shelter;
        var buffer = new Vector2[frames];
        for (int i = 0; i < frames; i++)
        {
            _rainLevel += (rainTarget - _rainLevel) * .00005;
            _windLevel += (windTarget - _windLevel) * .00008;
            double n = _random.NextDouble() * 2 - 1, n2 = _random.NextDouble() * 2 - 1;
            // Rain: hiss (high band) with a deeper roar as it gets heavy, and single drops ticking in light rain.
            _rainLp += .55 * (n - _rainLp);
            double hiss = n - _rainLp; // high pass
            _rainHi += hissCut * (hiss - _rainHi); // softened by the rain tone (1 = unchanged)
            hiss = _rainHi;
            _rainLp2 += .06 * (n2 - _rainLp2);
            double roar = _rainLp2 * 2.2 * Math.Clamp(_rainLevel * 1.5 - .3, 0, 1);
            _dropTimer -= 1.0 / Rate;
            // (under wet leaves: drips keep ticking after the rain, a few a second; in the rain the patter is busier)
            double drip = LeafDrip * volume * GameSettings.RainVolume.F;
            if (_dropTimer <= 0 && (_rainLevel > .02 || drip > .05))
            {
                _dropTimer = _random.NextDouble() * .25 / (1 + _rainLevel * 30 * (1 + LeafDrip) + drip * 6);
                _dropAmp = (float)(.3 + .7 * _random.NextDouble());
            }
            _dropAmp *= .992f;
            double rain = _rainLevel * (hiss * hissGain * (1 - .35 * LeafDrip) + roar) + _dropAmp * n * dropGain * Math.Max(Math.Min(1, _rainLevel * 6), drip * .8);
            // Wind: low rushing band, its pitch and level rising with the speed.
            _windLp += .02 * (n2 - _windLp);
            _windLp2 += .02 * (_windLp - _windLp2);
            _windPhase += 1.0 / Rate;
            double swell = .75 + .25 * Math.Sin(_windPhase * .7) * Math.Sin(_windPhase * .23 + 1.3);
            _windBand += (.03 + .05 * _windLevel) * ((_windLp - _windLp2) * 8 - _windBand);
            double wind = _windLevel * swell * _windBand * 1.4;
            float l = (float)Math.Clamp(rain + wind, -1, 1), r = (float)Math.Clamp(rain * .9 + n2 * _rainLevel * .05 + wind * 1.05, -1, 1);
            buffer[i] = new Vector2(l, r);
        }
        _ambience.PushBuffer(buffer);
    }
}
