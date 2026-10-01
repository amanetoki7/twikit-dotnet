using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// Python 版 <c>twikit.utils</c> の雑多なヘルパー。
/// </summary>
public static class Utils
{
    /// <summary>
    /// X の日時表記（<c>Wed Oct 10 20:19:24 +0000 2018</c>）を <see cref="DateTimeOffset"/> にします。
    /// </summary>
    public static DateTimeOffset TimestampToDateTime(string timestamp)
        => DateTimeOffset.ParseExact(timestamp, "ddd MMM dd HH:mm:ss zzz yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// v1.1 形式のツイート JSON を、GraphQL 形式（<c>rest_id</c> と <c>legacy</c> を持つ）に組み替えます。
    /// </summary>
    public static JsonObject BuildTweetData(JsonObject rawData)
    {
        var data = (JsonObject)rawData.DeepClone();
        // v1.1 sends both; `id` is a number that loses precision the
        // moment it is re-serialised, `id_str` is the safe one.
        data["rest_id"] = rawData.Str("id_str") ?? rawData.Str("id");
        data["is_translatable"] = null;
        data["views"] = new JsonObject();
        data["edit_control"] = new JsonObject();
        data["legacy"] = new JsonObject
        {
            ["created_at"] = rawData["created_at"].Clone(),
            ["full_text"] = (rawData["full_text"] ?? rawData["text"]).Clone(),
            ["lang"] = rawData["lang"].Clone(),
            ["is_quote_status"] = rawData["is_quote_status"].Clone(),
            ["in_reply_to_status_id_str"] = rawData["in_reply_to_status_id_str"].Clone(),
            ["retweeted_status_result"] = rawData["retweeted_status_result"].Clone(),
            ["possibly_sensitive"] = rawData["possibly_sensitive"].Clone(),
            ["possibly_sensitive_editable"] = rawData["possibly_sensitive_editable"].Clone(),
            ["quote_count"] = rawData["quote_count"].Clone(),
            ["entities"] = rawData["entities"].Clone(),
            ["reply_count"] = rawData["reply_count"].Clone(),
            ["favorite_count"] = rawData["favorite_count"].Clone(),
            ["favorited"] = rawData["favorited"].Clone(),
            ["retweet_count"] = rawData["retweet_count"].Clone(),
        };
        return data;
    }

    /// <summary>
    /// v1.1 形式のユーザー JSON を、GraphQL 形式（<c>rest_id</c> と <c>legacy</c> を持つ）に組み替えます。
    /// </summary>
    public static JsonObject BuildUserData(JsonObject rawData)
    {
        var data = (JsonObject)rawData.DeepClone();
        data["rest_id"] = rawData.Str("id_str") ?? rawData.Str("id");
        data["is_blue_verified"] = rawData["ext_is_blue_verified"].Clone();
        var legacy = new JsonObject();
        foreach (var key in new[]
                 {
                     "created_at", "name", "screen_name", "profile_image_url_https", "location", "description",
                     "entities", "pinned_tweet_ids_str", "verified", "possibly_sensitive", "can_dm", "can_media_tag",
                     "want_retweets", "default_profile", "default_profile_image", "has_custom_timelines",
                     "followers_count", "fast_followers_count", "normal_followers_count", "friends_count",
                     "favourites_count", "listed_count", "media_count", "statuses_count", "is_translator",
                     "translator_type", "withheld_in_countries", "url", "profile_banner_url",
                 })
        {
            legacy[key] = rawData[key].Clone();
        }
        data["legacy"] = legacy;
        return data;
    }

    /// <summary>GraphQL URL からクエリ ID を取り出します（<c>.../graphql/{id}/{name}</c>）。</summary>
    public static string GetQueryId(string url)
    {
        var parts = url.Split('/');
        return parts.Length >= 2 ? parts[^2] : url;
    }

    /// <summary>Base64 文字列を UTF-8 文字列にデコードします。</summary>
    public static string B64ToStr(string b64) => Encoding.UTF8.GetString(Convert.FromBase64String(b64));

    /// <summary>クエリ文字列用に、オブジェクト・配列の値を JSON 文字列に平坦化します。</summary>
    public static Dictionary<string, string> FlattenParams(IEnumerable<KeyValuePair<string, JsonNode?>> parameters)
    {
        var flat = new Dictionary<string, string>();
        foreach (var (key, value) in parameters)
        {
            flat[key] = value switch
            {
                null => "",
                JsonObject or JsonArray => value.ToJsonString(),
                _ => value.AsStr() ?? value.ToJsonString(),
            };
        }
        return flat;
    }

    /// <summary>ツイート ID（Snowflake）から作成時刻（ミリ秒）を復元します。</summary>
    public static long SnowflakeToTimestampMs(string tweetId)
        => (long.Parse(tweetId, CultureInfo.InvariantCulture) >> 22) + 1288834974657L;
}

/// <summary>
/// ライブラリが発する警告（Python 版の <c>warnings.warn</c>）。既定では
/// <see cref="System.Diagnostics.Trace"/> に書き出します。<see cref="Handler"/> を差し替えて受け取れます。
/// </summary>
public static class TwikitWarnings
{
    /// <summary>警告を受け取るハンドラー。null にすると既定（Trace）に戻ります。</summary>
    public static Action<string>? Handler { get; set; }

    public static void Warn(string message)
    {
        var handler = Handler;
        if (handler is not null) handler(message);
        else System.Diagnostics.Trace.TraceWarning("twikit: " + message);
    }
}

/// <summary><see cref="SearchOptions.Filters"/> に指定できるフィルター名。</summary>
public static class SearchFilters
{
    public const string Media = "media";
    public const string Retweets = "retweets";
    public const string NativeVideo = "native_video";
    public const string Periscope = "periscope";
    public const string Vine = "vine";
    public const string Images = "images";
    public const string Twimg = "twimg";
    public const string Links = "links";
}

/// <summary>
/// <see cref="SearchQuery.Build"/> に渡す検索オプション。
/// </summary>
public sealed class SearchOptions
{
    /// <summary>検索クエリに含める完全一致フレーズの一覧。</summary>
    public IList<string>? ExactPhrases { get; set; }
    /// <summary>いずれか 1 つを含めばよいキーワードの一覧。</summary>
    public IList<string>? OrKeywords { get; set; }
    /// <summary>除外するキーワードの一覧。</summary>
    public IList<string>? ExcludeKeywords { get; set; }
    /// <summary>検索クエリに含めるハッシュタグの一覧。</summary>
    public IList<string>? Hashtags { get; set; }
    /// <summary>このユーザーのツイートだけを対象にします。</summary>
    public string? FromUser { get; set; }
    /// <summary>このユーザー宛てのツイートだけを対象にします。</summary>
    public string? ToUser { get; set; }
    /// <summary>
    /// 場所で絞り込みます（例: 'Warsaw'）。X は <c>near:</c>、<c>within:</c>、<c>geocode:</c> を
    /// 廃止しており何も返さないため、残っている位置フィルターはこれだけです。
    /// </summary>
    public string? Place { get; set; }
    /// <summary>これらのユーザーに言及しているツイートだけを対象にします。</summary>
    public IList<string>? MentionedUsers { get; set; }
    /// <summary>含めるフィルター（<see cref="SearchFilters"/>）。</summary>
    public IList<string>? Filters { get; set; }
    /// <summary>除外するフィルター（<see cref="SearchFilters"/>）。</summary>
    public IList<string>? ExcludeFilters { get; set; }
    /// <summary>これらの URL を含むツイートだけを対象にします。</summary>
    public IList<string>? Urls { get; set; }
    /// <summary>この日付（YYYY-MM-DD）以降のツイートだけを対象にします。</summary>
    public string? Since { get; set; }
    /// <summary>この日付（YYYY-MM-DD）までのツイートだけを対象にします。</summary>
    public string? Until { get; set; }
    /// <summary>ポジティブな感情のツイートを含めます。</summary>
    public bool? Positive { get; set; }
    /// <summary>ネガティブな感情のツイートを含めます。</summary>
    public bool? Negative { get; set; }
    /// <summary>疑問形のツイートを検索します。</summary>
    public bool? Question { get; set; }
}

/// <summary>検索クエリの組み立て。</summary>
public static class SearchQuery
{
    /// <summary>
    /// 本文と検索オプションから X の検索クエリを組み立てます。
    /// https://developer.twitter.com/en/docs/twitter-api/v1/rules-and-filtering/search-operators
    /// </summary>
    /// <param name="text">検索クエリの元になる本文。</param>
    /// <param name="options">検索オプション。</param>
    /// <returns>組み立てた検索クエリ。</returns>
    public static string Build(string text, SearchOptions options)
    {
        var sb = new StringBuilder(text);

        if (options.ExactPhrases is { Count: > 0 } exact)
            sb.Append(' ').Append(string.Join(" ", exact.Select(i => $"\"{i}\"")));

        if (options.OrKeywords is { Count: > 0 } or)
            sb.Append(' ').Append(string.Join(" OR ", or));

        if (options.ExcludeKeywords is { Count: > 0 } exclude)
            sb.Append(' ').Append(string.Join(" ", exclude.Select(i => $"-\"{i}\"")));

        if (options.Hashtags is { Count: > 0 } hashtags)
            sb.Append(' ').Append(string.Join(" ", hashtags.Select(i => $"#{i}")));

        if (!string.IsNullOrEmpty(options.FromUser))
            sb.Append($" from:{options.FromUser}");

        if (!string.IsNullOrEmpty(options.ToUser))
            sb.Append($" to:{options.ToUser}");

        if (!string.IsNullOrEmpty(options.Place))
            sb.Append($" place:\"{options.Place}\"");

        if (options.MentionedUsers is { Count: > 0 } mentioned)
            sb.Append(' ').Append(string.Join(" ", mentioned.Select(i => $"@{i}")));

        if (options.Filters is { Count: > 0 } filters)
            sb.Append(' ').Append(string.Join(" ", filters.Select(i => $"filter:{i}")));

        if (options.ExcludeFilters is { Count: > 0 } excludeFilters)
            sb.Append(' ').Append(string.Join(" ", excludeFilters.Select(i => $"-filter:{i}")));

        if (options.Urls is { Count: > 0 } urls)
            sb.Append(' ').Append(string.Join(" ", urls.Select(i => $"url:{i}")));

        if (!string.IsNullOrEmpty(options.Since))
            sb.Append($" since:{options.Since}");

        if (!string.IsNullOrEmpty(options.Until))
            sb.Append($" until:{options.Until}");

        if (options.Positive == true) sb.Append(" :)");
        if (options.Negative == true) sb.Append(" :(");
        if (options.Question == true) sb.Append(" ?");

        return sb.ToString();
    }
}
