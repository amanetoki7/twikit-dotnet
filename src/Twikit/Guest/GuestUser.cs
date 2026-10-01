using System.Text.Json.Nodes;

namespace Twikit.Guest;

/// <summary>
/// ゲストクライアントで取得したユーザー。
/// </summary>
public sealed class GuestUser : IEquatable<GuestUser>
{
    private readonly GuestClient _client;

    public JsonObject Data { get; }
    public string Id { get; }
    public string CreatedAt { get; }
    public string Name { get; }
    public string ScreenName { get; }
    public string ProfileImageUrl { get; }
    public string? ProfileBannerUrl { get; }
    public string? Url { get; }
    public string Location { get; }
    public string Description { get; }
    public List<UrlEntity> DescriptionUrls { get; }
    public List<UrlEntity> Urls { get; }
    public List<string> PinnedTweetIds { get; }
    public bool IsBlueVerified { get; }
    public bool Verified { get; }
    public string? ParodyCommentaryFanLabel { get; }
    public bool PossiblySensitive { get; }
    public bool DefaultProfile { get; }
    public bool DefaultProfileImage { get; }
    public bool HasCustomTimelines { get; }
    public int FollowersCount { get; }
    public int FastFollowersCount { get; }
    public int NormalFollowersCount { get; }
    public int FollowingCount { get; }
    public int FavouritesCount { get; }
    public int ListedCount { get; }
    public int MediaCount { get; }
    public int StatusesCount { get; }
    public bool IsTranslator { get; }
    public string TranslatorType { get; }
    public List<string> WithheldInCountries { get; }
    public bool Protected { get; }

    public GuestUser(GuestClient client, JsonObject data)
    {
        _client = client;
        Data = data;
        var legacy = data.Sub("legacy");
        var core = data.Sub("core");
        var avatar = data.Sub("avatar");
        var location = data.Sub("location");
        var verification = data.Sub("verification");
        var privacy = data.Sub("privacy");
        var profileBio = data.Sub("profile_bio");

        Id = data.Str("rest_id") ?? "";
        CreatedAt = User.FirstNonEmpty(core.Str("created_at"), legacy.Str("created_at")) ?? "";
        Name = User.FirstNonEmpty(core.Str("name"), legacy.Str("name")) ?? "";
        ScreenName = User.FirstNonEmpty(core.Str("screen_name"), legacy.Str("screen_name")) ?? "";
        ProfileImageUrl = User.FirstNonEmpty(avatar.Str("image_url"), legacy.Str("profile_image_url_https")) ?? "";
        ProfileBannerUrl = data.Sub("banner").Str("image_url") ?? legacy.Str("profile_banner_url");
        Url = data.Sub("website").Str("url") ?? legacy.Str("url");
        Location = User.FirstNonEmpty(location.Str("location"), legacy.Str("location")) ?? "";
        Description = User.FirstNonEmpty(profileBio.Str("description"), legacy.Str("description")) ?? "";
        var descriptionUrls = profileBio.Sub("entities").Sub("description").Arr("urls");
        DescriptionUrls = UrlEntity.ListFrom(descriptionUrls is { Count: > 0 } ? descriptionUrls : legacy.Sub("entities").Sub("description").Arr("urls"));
        var urls = profileBio.Sub("entities").Sub("url").Arr("urls");
        Urls = UrlEntity.ListFrom(urls is { Count: > 0 } ? urls : legacy.Sub("entities").Sub("url").Arr("urls"));
        PinnedTweetIds = (data.Sub("pinned_items").Arr("tweet_ids_str") ?? legacy.Arr("pinned_tweet_ids_str")).Strings();
        IsBlueVerified = data.BoolOr("is_blue_verified", false);
        Verified = verification.Bool("verified") ?? legacy.BoolOr("verified", false);
        var label = data.Str("parody_commentary_fan_label");
        ParodyCommentaryFanLabel = label is null or "None" ? null : label;
        PossiblySensitive = legacy.BoolOr("possibly_sensitive", false);
        DefaultProfile = legacy.BoolOr("default_profile", false);
        DefaultProfileImage = legacy.BoolOr("default_profile_image", false);
        HasCustomTimelines = legacy.BoolOr("has_custom_timelines", false);
        var relationshipCounts = data.Sub("relationship_counts");
        var tweetCounts = data.Sub("tweet_counts");
        var actionCounts = data.Sub("action_counts");
        FollowersCount = relationshipCounts.Int("followers") ?? legacy.IntOr("followers_count", 0);
        FastFollowersCount = legacy.IntOr("fast_followers_count", 0);
        NormalFollowersCount = legacy.IntOr("normal_followers_count", 0);
        FollowingCount = relationshipCounts.Int("following") ?? legacy.IntOr("friends_count", 0);
        FavouritesCount = actionCounts.Int("favorites_count") ?? legacy.IntOr("favourites_count", 0);
        ListedCount = legacy.IntOr("listed_count", 0);
        MediaCount = tweetCounts.Int("media_tweets") ?? legacy.IntOr("media_count", 0);
        StatusesCount = tweetCounts.Int("tweets") ?? legacy.IntOr("statuses_count", 0);
        IsTranslator = legacy.BoolOr("is_translator", false);
        TranslatorType = legacy.Str("translator_type") ?? "";
        WithheldInCountries = legacy.Arr("withheld_in_countries").Strings();
        Protected = privacy.Bool("protected") ?? legacy.BoolOr("protected", false);
    }

    public DateTimeOffset CreatedAtDatetime => Utils.TimestampToDateTime(CreatedAt);

    /// <summary>ユーザーのツイートを取得します。</summary>
    public Task<List<GuestTweet>> GetTweetsAsync(string tweetType = "Tweets", int count = 40) => _client.GetUserTweetsAsync(Id, tweetType, count);

    /// <summary>ハイライトツイートを取得します。</summary>
    public Task<Result<GuestTweet>> GetHighlightsTweetsAsync(int count = 20, string? cursor = null) => _client.GetUserHighlightsTweetsAsync(Id, count, cursor);

    public override string ToString() => $"<User id=\"{Id}\">";
    public bool Equals(GuestUser? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as GuestUser);
    public override int GetHashCode() => Id.GetHashCode();
}
