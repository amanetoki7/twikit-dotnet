using System.Globalization;
using System.Text;

namespace Twikit.Transaction;

/// <summary>アニメーションキー計算で使う数値ユーティリティ（Python 版の interpolate / rotation / utils）。</summary>
public static class TransactionMath
{
    public static double[] Interpolate(IReadOnlyList<double> from, IReadOnlyList<double> to, double f)
    {
        if (from.Count != to.Count)
            throw new ArgumentException($"Mismatched interpolation arguments [{string.Join(", ", from)}]: [{string.Join(", ", to)}]");
        var output = new double[from.Count];
        for (var i = 0; i < from.Count; i++)
            output[i] = InterpolateNum(from[i], to[i], f);
        return output;
    }

    public static double InterpolateNum(double from, double to, double f) => from * (1 - f) + to * f;

    /// <summary>回転角（度）を 2x2 回転行列 <c>[cos, -sin, sin, cos]</c> にします。</summary>
    public static double[] ConvertRotationToMatrix(double rotation)
    {
        var rad = rotation * Math.PI / 180.0;
        return new[] { Math.Cos(rad), -Math.Sin(rad), Math.Sin(rad), Math.Cos(rad) };
    }

    /// <summary>
    /// 浮動小数を 16 進表記にします（Python 版 <c>float_to_hex</c> をそのまま移植。
    /// 元の実装の癖もアニメーションキーの一致に必要なので保っています）。
    /// </summary>
    public static string FloatToHex(double x)
    {
        var result = new List<string>();
        long quotient = (long)x;
        double fraction = x - quotient;

        while (quotient > 0)
        {
            quotient = (long)(x / 16);
            long remainder = (long)(x - ((double)quotient * 16));

            if (remainder > 9)
                result.Insert(0, ((char)(remainder + 55)).ToString());
            else
                result.Insert(0, remainder.ToString(CultureInfo.InvariantCulture));

            x = (double)quotient;
        }

        if (fraction == 0)
            return string.Concat(result);

        result.Add(".");

        while (fraction > 0)
        {
            fraction *= 16;
            long integer = (long)fraction;
            fraction -= (double)integer;

            if (integer > 9)
                result.Add(((char)(integer + 55)).ToString());
            else
                result.Add(integer.ToString(CultureInfo.InvariantCulture));
        }

        return string.Concat(result);
    }

    /// <summary>奇数なら -1.0、偶数なら 0.0。</summary>
    public static double IsOdd(long num) => num % 2 != 0 ? -1.0 : 0.0;

    public static string Base64Encode(byte[] bytes) => Convert.ToBase64String(bytes);

    public static string Base64Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
}
