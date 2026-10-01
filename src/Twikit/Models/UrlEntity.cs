using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>ツイート本文やプロフィールに含まれる URL エンティティ。</summary>
public sealed class UrlEntity
{
    /// <summary>生の JSON。</summary>
    public JsonObject Raw { get; }

    public UrlEntity(JsonObject raw)
    {
        Raw = raw;
    }

    /// <summary>t.co の短縮 URL。</summary>
    public string? Url => Raw.Str("url");
    /// <summary>展開後の URL。</summary>
    public string? ExpandedUrl => Raw.Str("expanded_url");
    /// <summary>表示用 URL。</summary>
    public string? DisplayUrl => Raw.Str("display_url");
    /// <summary>本文中の位置。</summary>
    public (int Start, int End)? Indices
    {
        get
        {
            var a = Raw.Arr("indices");
            return a is { Count: >= 2 } ? (a[0].AsInt() ?? 0, a[1].AsInt() ?? 0) : null;
        }
    }

    public override string ToString() => ExpandedUrl ?? Url ?? "";

    internal static List<UrlEntity> ListFrom(JsonNode? array)
        => (array as JsonArray).Objects().Select(o => new UrlEntity(o)).ToList();
}
