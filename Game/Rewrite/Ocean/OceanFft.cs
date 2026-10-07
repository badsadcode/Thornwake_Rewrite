using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// The "experimental 3" sea (round 23, Tom: the Blender ocean modifier's choppy, crisp waves; "aiming for Sea of
/// Thieves"): a Tessendorf FFT ocean on the GPU. Three cascades (480, 59 and 7.9 m tiles, 256² each) of one JONSWAP wind
/// sea plus a swell, from the same sea state as experimental 2 (SpectrumWaves.Hs, PeakWavelength, SwellHs), choppy
/// (horizontal displacement pinches the crests), with a lingering foam field born where the surface folds. Each frame:
/// spectrum at time t → inverse FFT (rows, columns) → displacement and slope textures (Ocean/*.glsl) sampled by
/// WaterExperimental3.gdshader. Deterministic: the random amplitudes come from the world seed, the time is the shared
/// ocean clock (looping every <see cref="Period"/> s exactly), so <see cref="Height"/> gives swimming and boats the same
/// surface on the CPU (the long waves; the short ones are only centimetres).
/// </summary>
public sealed class OceanFft : IDisposable
{
    public const int N = 256, Log2N = 8;
    /// <summary>The sea repeats every Period seconds (frequencies rounded to multiples of 2π/Period, Tessendorf).</summary>
    public const double Period = 600;
    public static readonly float[] Sizes = { 480f, 59f, 7.9f };
    // Each cascade carries the wavelengths between its tile's long end and the next one's (no wave counted twice).
    private static readonly double[] BandShortest = { 12, 1.6, .25 };
    private const double G = 9.81;

    private readonly RenderingDevice _rd;
    private Rid _h0, _spec, _disp, _deriv;
    private Rid _specShader, _fftShader, _assembleShader, _specPipe, _fftPipe, _assemblePipe;
    private Rid _specSet, _fftSet, _assembleSet;
    private readonly int _seed;
    private double _state = -1, _angle;
    private float[]? _pendingH0;
    private double _lastTime = double.NaN;
    private readonly List<Mode> _modes = new(); // CPU copy of cascade 0's long waves (Height)

    private readonly record struct Mode(double Kx, double Kz, double Omega, double Re, double Im, double ReM, double ImM);

    public Texture2DArrayRD Displacement { get; } = new();
    public Texture2DArrayRD Derivatives { get; } = new();
    /// <summary>Horizontal displacement scale (choppiness) per cascade, by sea state.</summary>
    public float Choppiness { get; private set; } = 1;

    private OceanFft(RenderingDevice rd, int seed)
    {
        _rd = rd;
        _seed = seed;
    }

    /// <summary>Null where compute shaders are unavailable (the Compatibility renderer): the caller falls back to experimental 2.</summary>
    public static OceanFft? Create(int seed)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null) return null;
        var ocean = new OceanFft(rd, seed);
        try { ocean.Init(); }
        catch (Exception e) { GD.PushWarning($"FFT ocean unavailable: {e.Message}"); ocean.Dispose(); return null; }
        return ocean;
    }

    private void Init()
    {
        (_specShader, _specPipe) = Pipeline("ocean_spectrum");
        (_fftShader, _fftPipe) = Pipeline("ocean_fft");
        (_assembleShader, _assemblePipe) = Pipeline("ocean_assemble");
        _h0 = Texture(RenderingDevice.DataFormat.R32G32B32A32Sfloat, 3, 16);
        _spec = Texture(RenderingDevice.DataFormat.R32G32B32A32Sfloat, 6, 16);
        _disp = Texture(RenderingDevice.DataFormat.R16G16B16A16Sfloat, 3, 8);
        _deriv = Texture(RenderingDevice.DataFormat.R16G16B16A16Sfloat, 3, 8);
        _specSet = Set(_specShader, _h0, _spec);
        _fftSet = Set(_fftShader, _spec);
        _assembleSet = Set(_assembleShader, _spec, _disp, _deriv);
        Displacement.TextureRdRid = _disp;
        Derivatives.TextureRdRid = _deriv;
    }

    private (Rid Shader, Rid Pipe) Pipeline(string name)
    {
        var file = GD.Load<RDShaderFile>($"res://Game/Rewrite/Ocean/{name}.glsl");
        var spirv = file.GetSpirV();
        if (!string.IsNullOrEmpty(spirv.CompileErrorCompute)) throw new InvalidOperationException($"{name}: {spirv.CompileErrorCompute}");
        var shader = _rd.ShaderCreateFromSpirV(spirv, name);
        if (!shader.IsValid) throw new InvalidOperationException($"{name}: shader not created");
        return (shader, _rd.ComputePipelineCreate(shader));
    }

    private Rid Texture(RenderingDevice.DataFormat format, int layers, int bytesPerTexel)
    {
        var fmt = new RDTextureFormat
        {
            Format = format, Width = N, Height = N, ArrayLayers = (uint)layers,
            TextureType = RenderingDevice.TextureType.Type2DArray,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit
                | RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
        };
        var data = new Godot.Collections.Array<byte[]>();
        for (int i = 0; i < layers; i++) data.Add(new byte[N * N * bytesPerTexel]); // zero (no foam yet)
        var rid = _rd.TextureCreate(fmt, new RDTextureView(), data);
        if (!rid.IsValid) throw new InvalidOperationException($"texture {format} not created");
        return rid;
    }

    private Rid Set(Rid shader, params Rid[] images)
    {
        var uniforms = new Godot.Collections.Array<RDUniform>();
        for (int i = 0; i < images.Length; i++)
        {
            var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = i };
            u.AddId(images[i]);
            uniforms.Add(u);
        }
        return _rd.UniformSetCreate(uniforms, shader, 0);
    }

    /// <summary>
    /// The sea state (0..1) and wind heading (rad, the direction the waves run): rebuilds the initial spectrum. The
    /// random numbers stay the same, so a change re-weights the waves without a jump.
    /// </summary>
    public void Configure(double seaState, double windAngle)
    {
        if (Math.Abs(seaState - _state) < 1e-4 && Math.Abs(windAngle - _angle) < 1e-4) return;
        _state = Math.Clamp(seaState, 0, 1); _angle = windAngle;
        Choppiness = (float)(.8 + .4 * _state);
        _pendingH0 = BuildH0();
    }

    /// <summary>Advances the sea to the ocean clock (call once per frame).</summary>
    public void Update(double oceanTime)
    {
        double t = Loop(oceanTime);
        float dt = double.IsNaN(_lastTime) ? 0 : (float)Math.Clamp(oceanTime - _lastTime, 0, .25);
        _lastTime = oceanTime;
        var h0 = _pendingH0;
        _pendingH0 = null;
        float chop = Choppiness;
        RenderingServer.CallOnRenderThread(Callable.From(() => Run(h0, (float)t, dt, chop)));
    }

    private static double Loop(double t) => t - Math.Floor(t / Period) * Period;

    private void Run(float[]? h0, float t, float dt, float chop)
    {
        if (h0 != null)
        {
            var bytes = MemoryMarshal.AsBytes(h0.AsSpan());
            int layer = N * N * 16;
            for (int c = 0; c < 3; c++) _rd.TextureUpdate(_h0, (uint)c, bytes.Slice(c * layer, layer));
        }
        long list = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(list, _specPipe);
        _rd.ComputeListBindUniformSet(list, _specSet, 0);
        Push(list, Sizes[0], Sizes[1], Sizes[2], t, N, (float)Period, 0, 0);
        _rd.ComputeListDispatch(list, N / 16, N / 16, 3);
        _rd.ComputeListAddBarrier(list);
        _rd.ComputeListBindComputePipeline(list, _fftPipe);
        _rd.ComputeListBindUniformSet(list, _fftSet, 0);
        for (uint vertical = 0; vertical < 2; vertical++)
        {
            var p = new uint[] { vertical, Log2N, 0, 0 };
            _rd.ComputeListSetPushConstant(list, MemoryMarshal.AsBytes(p.AsSpan()), 16);
            _rd.ComputeListDispatch(list, 1, N, 6);
            _rd.ComputeListAddBarrier(list);
        }
        _rd.ComputeListBindComputePipeline(list, _assemblePipe);
        _rd.ComputeListBindUniformSet(list, _assembleSet, 0);
        // Foam: born where the Jacobian drops below .55 (the surface pinching toward a fold), fading over ~4 s.
        Push(list, chop, chop, chop * .9f, dt, .55f, 1.6f, .3f, N);
        _rd.ComputeListDispatch(list, N / 16, N / 16, 3);
        _rd.ComputeListEnd();
    }

    private void Push(long list, params float[] values)
    {
        _rd.ComputeListSetPushConstant(list, MemoryMarshal.AsBytes(values.AsSpan()), (uint)(values.Length * 4));
    }

    /// <summary>The float origin modulo each tile (double precision): the shader's texture offsets (world-anchored waves).</summary>
    public static (Vector3 X, Vector3 Z) Offsets(double originX, double originZ)
    {
        static float M(double v, float l) => (float)(v - Math.Floor(v / l) * l);
        return (new Vector3(M(originX, Sizes[0]), M(originX, Sizes[1]), M(originX, Sizes[2])),
                new Vector3(M(originZ, Sizes[0]), M(originZ, Sizes[1]), M(originZ, Sizes[2])));
    }

    // ---- the initial spectrum (CPU, deterministic)

    private float[] BuildH0()
    {
        double s = _state;
        double hs = .15 + 14.1 * s * s, swellHs = .5 + 2.0 * s; // SpectrumWaves.Configure
        double peak = SpectrumWaves.PeakWavelength(s);
        // Directional spread cos^2s(Δθ/2): wide and confused in a storm, more orderly in a breeze; the swell narrow,
        // ~40° off the wind.
        double spreadWind = 10 - 6 * s, spreadSwell = 30;
        double swellAngle = _angle + .7;
        var wind = new double[3 * N * N];
        var swell = new double[3 * N * N];
        double sumWind = 0, sumSwell = 0;
        for (int c = 0; c < 3; c++)
        {
            double dk = 2 * Math.PI / Sizes[c];
            double kMin = c == 0 ? 0 : 2 * Math.PI / BandShortest[c - 1], kMax = 2 * Math.PI / BandShortest[c];
            for (int y = 1; y < N; y++)
            for (int x = 1; x < N; x++)
            {
                double kx = (x - N / 2) * dk, kz = (y - N / 2) * dk, k = Math.Sqrt(kx * kx + kz * kz);
                if (k <= kMin || k > kMax || k < 1e-9) continue;
                double theta = Math.Atan2(kz, kx);
                int i = (c * N + y) * N + x;
                wind[i] = Energy(k, theta, peak, _angle, spreadWind) * dk * dk;
                swell[i] = Energy(k, theta, 220, swellAngle, spreadSwell) * dk * dk;
                sumWind += wind[i]; sumSwell += swell[i];
            }
        }
        // Height variance Σ 2|h0|² (h0 and its mirror) = (Hs/4)² for each part.
        double scaleWind = sumWind > 0 ? hs * hs / 16 / (2 * sumWind) : 0;
        double scaleSwell = sumSwell > 0 ? swellHs * swellHs / 16 / (2 * sumSwell) : 0;
        var amp = new double[3 * N * N * 2];
        for (int c = 0; c < 3; c++)
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int i = (c * N + y) * N + x;
            double e = wind[i] * scaleWind + swell[i] * scaleSwell;
            if (e <= 0) continue;
            var (g1, g2) = Gauss(c, x, y);
            double a = Math.Sqrt(e / 2);
            amp[2 * i] = g1 * a; amp[2 * i + 1] = g2 * a;
        }
        var h0 = new float[3 * N * N * 4];
        _modes.Clear();
        double omegaBase = 2 * Math.PI / Period;
        for (int c = 0; c < 3; c++)
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int i = (c * N + y) * N + x;
            int mx = (N - x) % N, my = (N - y) % N, m = (c * N + my) * N + mx; // -k
            double re = amp[2 * i], im = amp[2 * i + 1], reM = amp[2 * m], imM = -amp[2 * m + 1]; // conj(h0(-k))
            h0[4 * i] = (float)re; h0[4 * i + 1] = (float)im; h0[4 * i + 2] = (float)reM; h0[4 * i + 3] = (float)imM;
            // The CPU copy: cascade 0's waves longer than ~20 m (all the height a swimmer or a boat feels).
            if (c == 0 && Math.Abs(x - N / 2) <= 24 && Math.Abs(y - N / 2) <= 24 && (re * re + im * im + reM * reM + imM * imM) > 1e-8)
            {
                double dk = 2 * Math.PI / Sizes[0], kx = (x - N / 2) * dk, kz = (y - N / 2) * dk;
                double omega = Math.Floor(Math.Sqrt(G * Math.Sqrt(kx * kx + kz * kz)) / omegaBase) * omegaBase;
                _modes.Add(new Mode(kx, kz, omega, re, im, reM, imM));
            }
        }
        return h0;
    }

    /// <summary>JONSWAP (γ 3.3) in wavenumber space with a cos^2s(Δθ/2) spread (unnormalised).</summary>
    private static double Energy(double k, double theta, double peakWavelength, double heading, double spread)
    {
        double omega = Math.Sqrt(G * k), omegaPeak = Math.Sqrt(G * 2 * Math.PI / peakWavelength);
        double sigma = omega <= omegaPeak ? .07 : .09;
        double r = (omega - omegaPeak) / (sigma * omegaPeak);
        double jonswap = Math.Pow(omega, -5) * Math.Exp(-1.25 * Math.Pow(omegaPeak / omega, 4)) * Math.Pow(3.3, Math.Exp(-.5 * r * r));
        double sk = jonswap * G / (2 * omega) / k; // S(ω) dω/dk, per k dk dθ
        double d = Math.Pow(Math.Abs(Math.Cos(.5 * Mathf.AngleDifference((float)heading, (float)theta))), 2 * spread);
        return sk * d * Math.Exp(-k * k * 1e-4); // (ripples under a few cm dropped)
    }

    private (double, double) Gauss(int c, int x, int y)
    {
        double u1 = Math.Max(Hash(c, x, y, 1), 1e-12), u2 = Hash(c, x, y, 2);
        double r = Math.Sqrt(-2 * Math.Log(u1));
        return (r * Math.Cos(2 * Math.PI * u2), r * Math.Sin(2 * Math.PI * u2));
    }

    private double Hash(int c, int x, int y, int salt)
    {
        ulong h = (ulong)(uint)_seed * 0x9E3779B97F4A7C15UL ^ (ulong)(c * 73856093 ^ x * 19349663 ^ y * 83492791 ^ salt * 2654435761);
        h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// The sea surface height (m, before the shader's depth and exposure weights) at a world position: cascade 0's long
    /// waves, with the choppy displacement undone (a few fixed-point steps: which water lands here).
    /// </summary>
    public double Height(double worldX, double worldZ, double oceanTime)
    {
        double t = Loop(oceanTime), chop = Choppiness, x = worldX, z = worldZ, y = 0;
        for (int iter = 0; iter < 3; iter++)
        {
            double dx = 0, dz = 0; y = 0;
            foreach (var m in _modes)
            {
                // h = h0 e^{i(k·x - ωt)} + conj(h0(-k)) e^{i(k·x + ωt)} (the shader's spectrum, transformed)
                double a = m.Kx * x + m.Kz * z, p1 = a - m.Omega * t, p2 = a + m.Omega * t;
                double c1 = Math.Cos(p1), s1 = Math.Sin(p1), c2 = Math.Cos(p2), s2 = Math.Sin(p2);
                double re = m.Re * c1 - m.Im * s1 + m.ReM * c2 - m.ImM * s2;
                double im = m.Re * s1 + m.Im * c1 + m.ReM * s2 + m.ImM * c2;
                y += re;
                // D = +i k/|k| h → real part −(k/|k|) Im(h)
                double kl = Math.Sqrt(m.Kx * m.Kx + m.Kz * m.Kz);
                dx -= m.Kx / kl * im; dz -= m.Kz / kl * im;
            }
            x = worldX - chop * dx; z = worldZ - chop * dz;
        }
        return y;
    }

    public void Dispose()
    {
        _modes.Clear();
        var rd = _rd;
        var rids = new[] { _specSet, _fftSet, _assembleSet, _specPipe, _fftPipe, _assemblePipe, _specShader, _fftShader, _assembleShader, _h0, _spec, _disp, _deriv };
        Displacement.TextureRdRid = new Rid();
        Derivatives.TextureRdRid = new Rid();
        RenderingServer.CallOnRenderThread(Callable.From(() =>
        {
            foreach (var r in rids) if (r.IsValid) rd.FreeRid(r);
        }));
    }
}
