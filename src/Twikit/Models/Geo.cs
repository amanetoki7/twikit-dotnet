using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// 場所。
/// </summary>
public sealed class Place : IEquatable<Place>
{
    private readonly Client _client;

    /// <summary>場所の ID。</summary>
    public string Id { get; private set; } = "";
    /// <summary>場所の名前。</summary>
    public string? Name { get; private set; }
    /// <summary>場所の正式名称。</summary>
    public string? FullName { get; private set; }
    /// <summary>国。</summary>
    public string? Country { get; private set; }
    /// <summary>ISO 3166-1 alpha-2 の国コード。</summary>
    public string? CountryCode { get; private set; }
    /// <summary>詳細情報の URL。</summary>
    public string? Url { get; private set; }
    /// <summary>場所の種類。</summary>
    public string? PlaceType { get; private set; }
    public JsonObject? Attributes { get; private set; }
    /// <summary>場所の地理的範囲を表すバウンディングボックス。</summary>
    public JsonObject? BoundingBox { get; private set; }
    /// <summary>緯度・経度で表した中心。</summary>
    public List<double>? Centroid { get; private set; }
    /// <summary>この場所を含む場所の一覧。</summary>
    public List<Place> ContainedWithin { get; private set; } = new();

    public Place(Client client, JsonObject data)
    {
        _client = client;
        Load(data);
    }

    private void Load(JsonObject data)
    {
        Id = data.Str("id") ?? "";
        Name = data.Str("name");
        FullName = data.Str("full_name");
        Country = data.Str("country");
        CountryCode = data.Str("country_code");
        Url = data.Str("url");
        PlaceType = data.Str("place_type");
        Attributes = data.Obj("attributes");
        BoundingBox = data.Obj("bounding_box");
        Centroid = data.Arr("centroid") is { } c ? c.Select(x => x.AsDouble() ?? 0).ToList() : null;
        ContainedWithin = data.ArrOrEmpty("contained_within").Objects().Select(p => new Place(_client, p)).ToList();
    }

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetPlaceAsync(Id).ConfigureAwait(false);
        Id = fresh.Id; Name = fresh.Name; FullName = fresh.FullName; Country = fresh.Country; CountryCode = fresh.CountryCode;
        Url = fresh.Url; PlaceType = fresh.PlaceType; Attributes = fresh.Attributes; BoundingBox = fresh.BoundingBox;
        Centroid = fresh.Centroid; ContainedWithin = fresh.ContainedWithin;
    }

    public override string ToString() => $"<Place id=\"{Id}\" name=\"{Name}\">";
    public bool Equals(Place? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Place);
    public override int GetHashCode() => Id.GetHashCode();

    internal static List<Place> FromResponse(Client client, JsonNode? response)
    {
        if (response.Get("errors") is JsonArray errors && errors.Count > 0 && errors[0] is JsonObject e)
        {
            // No data available for the given coordinate.
            if (e.Int("code") == 6)
                TwikitWarnings.Warn(e.Str("message") ?? "");
            else
                throw new TwitterException(e.Str("message"));
        }
        var places = response.Has("result") ? response.Get("result").ArrOrEmpty("places") : new JsonArray();
        return places.Objects().Select(p => new Place(client, p)).ToList();
    }
}
