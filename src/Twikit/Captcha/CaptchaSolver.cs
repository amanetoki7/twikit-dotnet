using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Twikit.Api;
using Twikit.UiMetrics;

namespace Twikit.Captcha;

/// <summary>ロック解除ページ（/account/access）を解析した結果。</summary>
public sealed record UnlockHtml(
    string? AuthenticityToken,
    string? AssignmentToken,
    bool NeedsUnlock,
    bool StartButton,
    bool FinishButton,
    bool DeleteButton,
    string? Blob);

/// <summary>
/// captcha ソルバーの基底クラス。<see cref="Client"/> に渡すと、アカウントがロックされたとき
/// （code 326）に自動でロック解除を試みます。
/// </summary>
public abstract class CaptchaSolver
{
    /// <summary>このソルバーを使うクライアント（<see cref="Client"/> のコンストラクターが設定します）。</summary>
    public Client? Client { get; internal set; }

    /// <summary>ロック解除の最大試行回数。</summary>
    public int MaxAttempts { get; set; } = 3;

    public static readonly string CaptchaUrl = $"https://{Constants.Domain}/account/access";
    public const string CaptchaSiteKey = "0152B4EB-D2DC-460A-89A1-629838B529C9";

    private Client RequireClient() => Client ?? throw new InvalidOperationException("The captcha solver is not attached to a client.");

    /// <summary>ロック解除ページを取得して解析します。</summary>
    public async Task<(HttpResponseMessage Response, UnlockHtml Html)> GetUnlockHtmlAsync()
    {
        var client = RequireClient();
        var headers = new Dictionary<string, string>
        {
            ["X-Twitter-Client-Language"] = "en-US",
            ["User-Agent"] = client.UserAgent,
            ["Upgrade-Insecure-Requests"] = "1",
        };
        var response = await client.GetAsync(CaptchaUrl, new RequestOptions { Headers = headers }).ConfigureAwait(false);
        return (response.Http, ParseUnlockHtml(response.Text));
    }

    /// <summary>ui_metrics スクリプトの本体（<c>return {...};</c> の部分）を取得します。</summary>
    public async Task<string> UiMetrixAsync()
    {
        var client = RequireClient();
        var js = await client.GetAsync($"https://{Constants.Domain}/i/js_inst?c_name=ui_metrics").ConfigureAwait(false);
        var match = Regex.Match(js.Text, @"return ({.*?});", RegexOptions.Singleline);
        if (!match.Success) throw new TwitterException("Could not find the ui_metrics function.");
        return match.Groups[1].Value;
    }

    /// <summary>ロック解除フォームを送信します。</summary>
    public async Task<(HttpResponseMessage Response, UnlockHtml Html)> ConfirmUnlockAsync(
        string? authenticityToken,
        string? assignmentToken,
        string? verificationString = null,
        bool uiMetrics = false)
    {
        var client = RequireClient();
        var data = new Dictionary<string, string>
        {
            ["authenticity_token"] = authenticityToken ?? "",
            ["assignment_token"] = assignmentToken ?? "",
            ["lang"] = "en",
            ["flow"] = "",
        };
        var parameters = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(verificationString))
        {
            data["verification_string"] = verificationString;
            data["language_code"] = "en";
            parameters["lang"] = "en";
        }
        if (uiMetrics)
        {
            data["ui_metrics"] = UiMetricsSolver.Solve(await client.UiMetricsAsync().ConfigureAwait(false));
        }
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/x-www-form-urlencoded",
            ["Upgrade-Insecure-Requests"] = "1",
            ["Referer"] = CaptchaUrl,
        };
        var response = await client.PostAsync(CaptchaUrl, new RequestOptions
        {
            Params = parameters,
            Form = data,
            Headers = headers,
        }).ConfigureAwait(false);
        return (response.Http, ParseUnlockHtml(response.Text));
    }

    /// <summary>FunCaptcha を解きます。<c>errorId</c> と <c>solution.token</c> を含む結果を返します。</summary>
    public abstract Task<JsonObject> SolveFunCaptchaAsync(string? blob);

    private static readonly HtmlParser Parser = new();

    /// <summary>ロック解除ページを解析します。</summary>
    public static UnlockHtml ParseUnlockHtml(string html)
    {
        var doc = Parser.ParseDocument(html);

        var authenticityToken = doc.QuerySelector("input[name='authenticity_token']")?.GetAttribute("value");
        var assignmentToken = doc.QuerySelector("input[name='assignment_token']")?.GetAttribute("value");
        var needsUnlock = doc.QuerySelector("input#verification_string") is not null;
        var startButton = doc.QuerySelector("input[value='Start']") is not null;
        var finishButton = doc.QuerySelector("input[value='Continue to X']") is not null;
        var deleteButton = doc.QuerySelector("input[value='Delete']") is not null;

        string? blob = null;
        var iframe = doc.QuerySelector("#arkose_iframe");
        if (iframe is not null)
        {
            var match = Regex.Match(iframe.GetAttribute("src") ?? "", @"data=(.+)");
            if (match.Success) blob = match.Groups[1].Value;
        }

        return new UnlockHtml(authenticityToken, assignmentToken, needsUnlock, startButton, finishButton, deleteButton, blob);
    }
}
