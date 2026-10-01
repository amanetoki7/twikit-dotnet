namespace Twikit.Transaction;

/// <summary>3 次ベジェ曲線（アニメーションのイージング）。</summary>
public sealed class Cubic
{
    private readonly double[] _curves;

    public Cubic(IReadOnlyList<double> curves)
    {
        _curves = curves.ToArray();
    }

    public double GetValue(double time)
    {
        double startGradient = 0.0, endGradient = 0.0, start = 0.0, mid = 0.0;
        double end = 1.0;

        if (time <= 0.0)
        {
            if (_curves[0] > 0.0)
                startGradient = _curves[1] / _curves[0];
            else if (_curves[1] == 0.0 && _curves[2] > 0.0)
                startGradient = _curves[3] / _curves[2];
            return startGradient * time;
        }

        if (time >= 1.0)
        {
            if (_curves[2] < 1.0)
                endGradient = (_curves[3] - 1.0) / (_curves[2] - 1.0);
            else if (_curves[2] == 1.0 && _curves[0] < 1.0)
                endGradient = (_curves[1] - 1.0) / (_curves[0] - 1.0);
            return 1.0 + endGradient * (time - 1.0);
        }

        while (start < end)
        {
            mid = (start + end) / 2;
            var xEst = Calculate(_curves[0], _curves[2], mid);
            if (Math.Abs(time - xEst) < 0.00001)
                return Calculate(_curves[1], _curves[3], mid);
            if (xEst < time)
                start = mid;
            else
                end = mid;
        }
        return Calculate(_curves[1], _curves[3], mid);
    }

    public static double Calculate(double a, double b, double m)
        => 3.0 * a * (1 - m) * (1 - m) * m + 3.0 * b * (1 - m) * m * m + m * m * m;
}
