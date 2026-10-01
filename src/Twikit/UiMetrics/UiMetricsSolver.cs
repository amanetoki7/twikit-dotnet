using System.Text.RegularExpressions;
using Jint;

namespace Twikit.UiMetrics;

/// <summary>
/// X の難読化された <c>ui_metrics</c> チャレンジを JavaScript インタープリター（Jint）で解きます
/// （Python 版は js2py）。
/// </summary>
public static class UiMetricsSolver
{
    private static readonly Regex FunctionPattern = new(@"function [a-zA-Z]+\(\) ({.+})", RegexOptions.Compiled);
    private static readonly Regex EqualPattern = new(@"(![a-zA-Z]{5}\|\|[a-zA-Z]{5})==([a-zA-Z]{5})", RegexOptions.Compiled);

    /// <summary>
    /// <c>/i/js_inst?c_name=ui_metrics</c> の応答を評価し、API に送る JSON 文字列を返します。
    /// </summary>
    public static string Solve(string uiMetrics)
    {
        var match = FunctionPattern.Match(uiMetrics);
        if (!match.Success)
            throw new ArgumentException("No function pattern found in ui_metrics input", nameof(uiMetrics));
        var innerFunction = match.Groups[1].Value;
        // Replace '==' with '===' to ensure proper object comparison.
        innerFunction = EqualPattern.Replace(innerFunction, "$1===$2");
        var engine = new Engine(options => options.LimitRecursion(512).TimeoutInterval(TimeSpan.FromSeconds(30)));
        engine.SetValue("document", new MockDocument());
        engine.Execute("function main()" + innerFunction);
        var result = engine.Evaluate("JSON.stringify(main())");
        return result.IsString() ? result.AsString() : result.ToString();
    }
}
