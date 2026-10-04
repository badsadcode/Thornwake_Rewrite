using System;

namespace Thornwake.WorldGen;

/// <summary>
/// Cross-platform deterministic replacements for the libm functions world generation needs.
/// <para>
/// <c>Math.Sin/Cos/Atan2/Exp/Pow</c> call the platform C library, whose last-bit results differ between
/// Windows and Linux (measured: the original A4 fingerprints fail on Linux). These versions use only IEEE-754
/// exact operations (+ − × ÷, floor, bit manipulation) with the fdlibm polynomial kernels, so every machine
/// produces identical bits. Accuracy is within a few ulp of libm, far below anything visible.
/// </para>
/// </summary>
public static class DetMath
{
    public const double Pi = 3.14159265358979311600e+00;
    public const double TwoPi = 6.28318530717958623200e+00;
    public const double HalfPi = 1.57079632679489655800e+00;

    // Cody–Waite split of pi/2 (fdlibm __ieee754_rem_pio2, medium range).
    private const double Pio2Hi = 1.57079632673412561417e+00, Pio2Lo = 6.07710050650619224932e-11;
    private const double InvPio2 = 6.36619772367581382433e-01;

    /// <summary>Sine. Exact argument reduction for |x| &lt; ~1e6, deterministic (if slightly less accurate) beyond.</summary>
    public static double Sin(double x)
    {
        double n = Math.Floor(x * InvPio2 + 0.5);
        double r = (x - n * Pio2Hi) - n * Pio2Lo;
        return ((long)n & 3) switch
        {
            0 => KernelSin(r),
            1 => KernelCos(r),
            2 => -KernelSin(r),
            _ => -KernelCos(r),
        };
    }

    public static double Cos(double x)
    {
        double n = Math.Floor(x * InvPio2 + 0.5);
        double r = (x - n * Pio2Hi) - n * Pio2Lo;
        return ((long)n & 3) switch
        {
            0 => KernelCos(r),
            1 => -KernelSin(r),
            2 => -KernelCos(r),
            _ => KernelSin(r),
        };
    }

    private static double KernelSin(double x)
    {
        const double S1 = -1.66666666666666324348e-01, S2 = 8.33333333332248946124e-03, S3 = -1.98412698298579493134e-04;
        const double S4 = 2.75573137070700676789e-06, S5 = -2.50507602534068634195e-08, S6 = 1.58969099521155010221e-10;
        double z = x * x;
        return x + x * z * (S1 + z * (S2 + z * (S3 + z * (S4 + z * (S5 + z * S6)))));
    }

    private static double KernelCos(double x)
    {
        const double C1 = 4.16666666666666019037e-02, C2 = -1.38888888888741095749e-03, C3 = 2.48015872894767294178e-05;
        const double C4 = -2.75573143513906633035e-07, C5 = 2.08757232129817482790e-09, C6 = -1.13596475577881948265e-11;
        double z = x * x;
        double r = z * z * (C1 + z * (C2 + z * (C3 + z * (C4 + z * (C5 + z * C6)))));
        double hz = 0.5 * z, w = 1.0 - hz;
        return w + (((1.0 - w) - hz) + r);
    }

    private static readonly double[] AtanHi = { 4.63647609000806093515e-01, 7.85398163397448278999e-01, 9.82793723247329054082e-01, 1.57079632679489655800e+00 };
    private static readonly double[] AtanLo = { 2.26987774529616870924e-17, 3.06161699786838301793e-17, 1.39033110312309984516e-17, 6.12323399573676603587e-17 };

    /// <summary>Arctangent (fdlibm s_atan.c).</summary>
    public static double Atan(double x)
    {
        if (double.IsNaN(x)) return x;
        double sign = x < 0 ? -1 : 1, ax = Math.Abs(x);
        if (ax > 1e17) return sign * HalfPi;
        int id;
        if (ax < 0.4375) id = -1;
        else if (ax < 0.6875) { id = 0; ax = (2.0 * ax - 1.0) / (2.0 + ax); }
        else if (ax < 1.1875) { id = 1; ax = (ax - 1.0) / (ax + 1.0); }
        else if (ax < 2.4375) { id = 2; ax = (ax - 1.5) / (1.0 + 1.5 * ax); }
        else { id = 3; ax = -1.0 / ax; }
        double z = ax * ax, w = z * z;
        double s1 = z * (3.33333333333329318027e-01 + w * (1.42857142725034663711e-01 + w * (9.09088713343650656196e-02 +
            w * (6.66107313738753120669e-02 + w * (4.97687799461593236017e-02 + w * 1.62858201153657823623e-02)))));
        double s2 = w * (-1.99999999998764832476e-01 + w * (-1.11111104054623557880e-01 + w * (-7.69187620504482999495e-02 +
            w * (-5.83357013379057348645e-02 + w * -3.65315727442169155270e-02))));
        if (id < 0) return sign * (ax - ax * (s1 + s2));
        return sign * (AtanHi[id] - ((ax * (s1 + s2) - AtanLo[id]) - ax));
    }

    /// <summary>Two-argument arctangent with the usual quadrant conventions.</summary>
    public static double Atan2(double y, double x)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return double.NaN;
        if (x == 0) return y > 0 ? HalfPi : y < 0 ? -HalfPi : 0;
        double a = Atan(Math.Abs(y / x));
        if (x > 0) return y < 0 ? -a : a;
        return y < 0 ? a - Pi : Pi - a;
    }

    /// <summary>e^x (fdlibm e_exp.c).</summary>
    public static double Exp(double x)
    {
        if (double.IsNaN(x)) return x;
        if (x > 709.78) return double.PositiveInfinity;
        if (x < -745.13) return 0;
        const double Ln2Hi = 6.93147180369123816490e-01, Ln2Lo = 1.90821492927058770002e-10, InvLn2 = 1.44269504088896338700e+00;
        const double P1 = 1.66666666666666019037e-01, P2 = -2.77777777770155933842e-03, P3 = 6.61375632143793436117e-05;
        const double P4 = -1.65339022054652515390e-06, P5 = 4.13813679705723846039e-08;
        double k = Math.Floor(x * InvLn2 + 0.5);
        double hi = x - k * Ln2Hi, lo = k * Ln2Lo, r = hi - lo;
        double t = r * r;
        double c = r - t * (P1 + t * (P2 + t * (P3 + t * (P4 + t * P5))));
        double y = 1.0 - ((lo - (r * c) / (2.0 - c)) - hi);
        return ScaleByPowerOfTwo(y, (int)k);
    }

    /// <summary>y · 2^k, exact (two steps so subnormal/large exponents stay representable).</summary>
    private static double ScaleByPowerOfTwo(double y, int k)
    {
        if (k > 1000) { y *= BitConverter.Int64BitsToDouble((long)(1000 + 1023) << 52); k -= 1000; }
        if (k < -1000) { y *= BitConverter.Int64BitsToDouble((long)(-1000 + 1023) << 52); k += 1000; }
        return y * BitConverter.Int64BitsToDouble((long)(k + 1023) << 52);
    }

    /// <summary>Quintic smoothstep on [0,1] (clamped), C2 continuous.</summary>
    public static double Smooth(double t)
    {
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
