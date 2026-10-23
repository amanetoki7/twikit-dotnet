namespace Twikit.Api;

/// <summary>Jetfuel（server-driven UI、<c>/i/jfapi/...</c>）のエンドポイント URL。</summary>
public static class JetfuelEndpoint
{
    private static readonly string D = Constants.Domain;

    public static readonly string Base = $"https://{D}/i/jfapi";
    /// <summary>クリエイタースタジオのトップ（各プログラムの対象 / 対象外の一覧）。</summary>
    public static readonly string CreatorsStudio = $"{Base}/creators/studio";
    /// <summary>オリジナルコンテンツ報酬プログラムの参加条件と達成状況。</summary>
    public static readonly string CreatorsOriginalContentRewards = $"{Base}/creators/original_content_rewards";
    /// <summary>クリエイターサブスクリプションの参加条件と達成状況。</summary>
    public static readonly string CreatorsSubscriptionsEligibility = $"{Base}/creators/subscriptionsv2/eligibility";
}

/// <summary>
/// Jetfuel エンドポイント層。生の <see cref="ApiResponse"/> を返します。本文はバイナリ文書（Content-Type は text/plain）で
/// JSON ではないため、<see cref="ApiResponse.Json"/> は null で、<see cref="ApiResponse.Bytes"/> を読みます
/// （<c>Twikit.Internal.JetfuelDocument</c> でデコードします）。
/// </summary>
/// <remarks>
/// 公式 Web（2026-10）は <c>x-jf-v: JP-5</c> と <c>x-jf-client-theme</c> を付けて GET します。
/// レート制限は 15 分 5000 回（<c>x-rate-limit-limit</c>）。
/// </remarks>
public sealed class JetfuelClient
{
    /// <summary>公式 Web が送るプロトコル版（<c>x-jf-v</c>）。</summary>
    public const string ProtocolVersion = "JP-5";

    private readonly IRequestClient _base;

    /// <summary><c>x-jf-client-theme</c> の値（"light" / "dark"）。色のスタイルにしか影響しません。</summary>
    public string Theme { get; set; } = "light";

    public JetfuelClient(IRequestClient baseClient)
    {
        _base = baseClient;
    }

    /// <summary>任意の Jetfuel ページを取得します。</summary>
    /// <param name="url"><see cref="JetfuelEndpoint"/> の URL、または <c>/creators/...</c> のようなパス。</param>
    public Task<ApiResponse> GetAsync(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = JetfuelEndpoint.Base + (url.StartsWith('/') ? url : "/" + url);

        var headers = _base.BaseHeaders;
        headers["x-jf-v"] = ProtocolVersion;
        headers["x-jf-client-theme"] = Theme;
        // The page that embeds the Jetfuel frame lives under /i/jf/, not /i/jfapi/.
        headers["Referer"] = url.Replace("/i/jfapi/", "/i/jf/");
        var timezone = IanaTimeZone();
        if (timezone is not null) headers["timezone"] = timezone;
        return _base.GetAsync(url, new RequestOptions { Headers = headers });
    }

    public Task<ApiResponse> CreatorsStudioAsync() => GetAsync(JetfuelEndpoint.CreatorsStudio);

    public Task<ApiResponse> CreatorsOriginalContentRewardsAsync() => GetAsync(JetfuelEndpoint.CreatorsOriginalContentRewards);

    public Task<ApiResponse> CreatorsSubscriptionsEligibilityAsync() => GetAsync(JetfuelEndpoint.CreatorsSubscriptionsEligibility);

    /// <summary>IANA 形式のローカルタイムゾーン（"Asia/Tokyo" など）。変換できなければ null。</summary>
    internal static string? IanaTimeZone()
    {
        try
        {
            var local = TimeZoneInfo.Local;
            if (local.HasIanaId) return local.Id;
            return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : null;
        }
        catch
        {
            return null;
        }
    }
}
