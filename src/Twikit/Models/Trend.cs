using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// トレンド。
/// </summary>
public sealed class Trend
{
    /// <summary>トレンドの名前。</summary>
    public string Name { get; }
    /// <summary>ツイート数の説明（X の表記そのまま。例: "12.3K posts"）。</summary>
    public string? TweetsCount { get; }
    /// <summary>ドメインのコンテキスト。</summary>
    public string? DomainContext { get; }
    /// <summary>まとめられた関連トレンドの名前。</summary>
    public List<string> GroupedTrends { get; }

    public Trend(Client client, JsonObject data)
    {
        _ = client;
        var metadata = data.Sub("trend_metadata");
        Name = data.Str("name") ?? "";
        TweetsCount = metadata.Str("meta_description");
        DomainContext = metadata.Str("domain_context");
        GroupedTrends = data.ArrOrEmpty("grouped_trends").Objects().Select(t => t.Str("name") ?? "").ToList();
    }

    public override string ToString() => $"<Trend name=\"{Name}\">";
}

/// <summary>地域のトレンド一覧。</summary>
public sealed class PlaceTrends
{
    public List<PlaceTrend> Trends { get; }
    public string? AsOf { get; }
    public string? CreatedAt { get; }
    public JsonArray? Locations { get; }

    public PlaceTrends(List<PlaceTrend> trends, string? asOf, string? createdAt, JsonArray? locations)
    {
        Trends = trends;
        AsOf = asOf;
        CreatedAt = createdAt;
        Locations = locations;
    }
}

/// <summary>地域のトレンド。</summary>
public sealed class PlaceTrend
{
    /// <summary>トレンドの名前。</summary>
    public string Name { get; }
    /// <summary>トレンドの URL。</summary>
    public string? Url { get; }
    public JsonNode? PromotedContent { get; }
    /// <summary>対応する検索クエリ。</summary>
    public string? Query { get; }
    /// <summary>ツイート数。</summary>
    public long? TweetVolume { get; }

    public PlaceTrend(Client client, JsonObject data)
    {
        _ = client;
        Name = data.Str("name") ?? "";
        Url = data.Str("url");
        PromotedContent = data.Get("promoted_content");
        Query = data.Str("query");
        TweetVolume = data.Long("tweet_volume");
    }

    public override string ToString() => $"<PlaceTrend name=\"{Name}\">";
}

/// <summary>トレンドを取得できる地域。</summary>
public sealed class Location : IEquatable<Location>
{
    private readonly Client _client;

    public long Woeid { get; }
    public string? Country { get; }
    public string? CountryCode { get; }
    public string Name { get; }
    public long? ParentId { get; }
    public JsonObject? PlaceType { get; }
    public string? Url { get; }

    public Location(Client client, JsonObject data)
    {
        _client = client;
        Woeid = data.Long("woeid") ?? 0;
        Country = data.Str("country");
        CountryCode = data.Str("countryCode");
        Name = data.Str("name") ?? "";
        ParentId = data.Long("parentid");
        PlaceType = data.Obj("placeType");
        Url = data.Str("url");
    }

    /// <summary>この地域のトレンドを取得します。</summary>
    public Task<PlaceTrends> GetTrendsAsync() => _client.GetPlaceTrendsAsync(Woeid);

    public override string ToString() => $"<Location name=\"{Name}\" woeid={Woeid}>";
    public bool Equals(Location? other) => other is not null && Woeid == other.Woeid;
    public override bool Equals(object? obj) => Equals(obj as Location);
    public override int GetHashCode() => Woeid.GetHashCode();
}
