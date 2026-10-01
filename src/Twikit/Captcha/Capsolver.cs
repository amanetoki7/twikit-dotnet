using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Twikit.Captcha;

/// <summary>
/// Capsolver（https://capsolver.com）を使ってアカウントのロックを自動解除します。
/// </summary>
/// <example>
/// <code>
/// var solver = new Capsolver(apiKey: "your_api_key", maxAttempts: 10);
/// var client = new Client(captchaSolver: solver);
/// </code>
/// </example>
public sealed class Capsolver : CaptchaSolver
{
    private static readonly HttpClient Http = new();

    /// <summary>Capsolver の API キー。</summary>
    public string ApiKey { get; }

    /// <summary>結果を問い合わせる間隔（秒）。</summary>
    public double GetResultInterval { get; }

    /// <summary>blob データをタスクに含めるか。</summary>
    public bool UseBlobData { get; }

    /// <param name="apiKey">Capsolver の API キー。</param>
    /// <param name="maxAttempts">captcha を解く最大試行回数。</param>
    /// <param name="getResultInterval">結果を問い合わせる間隔（秒）。</param>
    /// <param name="useBlobData">blob データをタスクに含めるか。</param>
    public Capsolver(string apiKey, int maxAttempts = 3, double getResultInterval = 1.0, bool useBlobData = false)
    {
        ApiKey = apiKey;
        MaxAttempts = maxAttempts;
        GetResultInterval = getResultInterval;
        UseBlobData = useBlobData;
    }

    public async Task<JsonObject> CreateTaskAsync(JsonObject taskData)
    {
        var data = new JsonObject { ["clientKey"] = ApiKey, ["task"] = taskData };
        using var response = await Http.PostAsJsonAsync("https://api.capsolver.com/createTask", data).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonExtensions.TryParse(text) as JsonObject ?? new JsonObject();
    }

    public async Task<JsonObject> GetTaskResultAsync(string taskId)
    {
        var data = new JsonObject { ["clientKey"] = ApiKey, ["taskId"] = taskId };
        using var response = await Http.PostAsJsonAsync("https://api.capsolver.com/getTaskResult", data).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonExtensions.TryParse(text) as JsonObject ?? new JsonObject();
    }

    public override async Task<JsonObject> SolveFunCaptchaAsync(string? blob)
    {
        var client = Client ?? throw new InvalidOperationException("The captcha solver is not attached to a client.");
        var captchaType = client.Proxy is null ? "FunCaptchaTaskProxyLess" : "FunCaptchaTask";
        var taskData = new JsonObject
        {
            ["type"] = captchaType,
            ["websiteURL"] = "https://iframe.arkoselabs.com",
            ["websitePublicKey"] = CaptchaSiteKey,
            ["funcaptchaApiJSSubdomain"] = "https://client-api.arkoselabs.com",
            ["proxy"] = client.Proxy,
        };
        if (UseBlobData)
        {
            taskData["data"] = "{\"blob\":\"" + blob + "\"}";
            taskData["userAgent"] = client.UserAgent;
        }
        var task = await CreateTaskAsync(taskData).ConfigureAwait(false);
        var taskId = task.Str("taskId") ?? throw new TwitterException("Capsolver returned no taskId: " + task.ToJsonString());
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(GetResultInterval)).ConfigureAwait(false);
            var result = await GetTaskResultAsync(taskId).ConfigureAwait(false);
            var status = result.Str("status");
            if (status is "ready" or "failed") return result;
        }
    }
}
