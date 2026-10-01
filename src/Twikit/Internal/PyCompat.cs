using System.Numerics;

namespace Twikit.Internal;

/// <summary>
/// Python の数値挙動を再現する補助（アニメーションキーは Python 版と bit 単位で一致させる必要がある）。
/// </summary>
internal static class PyCompat
{
    /// <summary>
    /// Python の <c>round(x, ndigits)</c>。<see cref="Math.Round(double, int)"/> は 10 の冪で
    /// スケーリングしてから丸めるため <c>2.675</c> のような値で結果が食い違う。ここでは
    /// double の厳密な二進値を有理数として扱い、正確な半数偶数丸めを行う。
    /// </summary>
    public static double Round(double x, int ndigits)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++;
        else mantissa |= 1L << 52;
        exponent -= 1075; // |x| = mantissa * 2^exponent
        if (exponent >= 0) return x; // integer valued already
        BigInteger pow10 = BigInteger.Pow(10, ndigits);
        BigInteger numerator = new BigInteger(mantissa) * pow10;
        BigInteger denominator = BigInteger.One << (-exponent);
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int cmp = (remainder * 2).CompareTo(denominator);
        if (cmp > 0 || (cmp == 0 && !quotient.IsEven)) quotient += 1;
        double result = (double)quotient / (double)pow10;
        return negative ? -result : result;
    }

    /// <summary>Python の <c>round(x)</c>（半数偶数丸めで整数へ）。</summary>
    public static long RoundToInt(double x) => (long)Math.Round(x, MidpointRounding.ToEven);

    /// <summary>Python の <c>math.floor</c>。</summary>
    public static long Floor(double x) => (long)Math.Floor(x);
}
