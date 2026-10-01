using System.Text.Json.Nodes;

namespace Twikit.Guest;

/// <summary>
/// ゲストクライアントで取得したツイート。
/// </summary>
public sealed class GuestTweet : IEquatable<GuestTweet>
{
    private readonly GuestClient _client;
    private readonly JsonArray _media;

    public JsonObject Data { get; }
    public GuestUser? User { get; }
    public List<GuestTweet>? ReplyTo { get; set; }
    public List<GuestTweet>? RelatedTweets { get; set; }
    public List<GuestTweet>? Thread { get; set; }

    public string Id { get; }
    public string CreatedAt { get; }
    public string Text { get; }
    public string? Lang { get; }
    public bool IsQuoteStatus { get; }
    public string? InReplyTo { get; }
    public bool PossiblySensitive { get; }
    public bool PossiblySensitiveEditable { get; }
    public int QuoteCount { get; }
    public int ReplyCount { get; }
    public int FavoriteCount { get; }
    public bool Favorited { get; }
    public int RetweetCount { get; }
    public JsonObject? PlaceData { get; }
    public int BookmarkCount { get; }
    public bool Bookmarked { get; }
    public List<string> EditTweetIds { get; }
    public long? EditableUntilMsecs { get; }
    public bool IsTranslatable { get; }
    public bool IsEditEligible { get; }
    public int? EditsRemaining { get; }
    public string? ViewCount { get; }
    public string? ViewCountState { get; }
    public bool HasCommunityNotes { get; }
    public GuestTweet? Quote { get; }
    public GuestTweet? RetweetedTweet { get; }
    public string FullText { get; }
    public List<UrlEntity> Urls { get; }
    public List<string> Hashtags { get; }
    public BirdwatchNote? CommunityNote { get; }
    public JsonObject? PollData { get; }
    public string? ThumbnailUrl { get; }
    public string? ThumbnailTitle { get; }
    public bool HasCard { get; }

    public GuestTweet(GuestClient client, JsonObject data, GuestUser? user = null)
    {
        _client = client;
        Data = data;
        User = user;

        Id = data.Str("rest_id") ?? "";
        var legacy = data.Sub("legacy");
        CreatedAt = legacy.Str("created_at") ?? "";
        Text = legacy.Str("full_text") ?? "";
        Lang = legacy.Str("lang");
        IsQuoteStatus = legacy.BoolOr("is_quote_status", false);
        InReplyTo = legacy.Str("in_reply_to_status_id_str");
        PossiblySensitive = legacy.BoolOr("possibly_sensitive", false);
        PossiblySensitiveEditable = legacy.BoolOr("possibly_sensitive_editable", false);
        QuoteCount = legacy.IntOr("quote_count", 0);
        // entities.media holds only the first attachment and no video_info,
        // and it can be missing entirely.
        _media = (legacy.Obj("extended_entities") ?? legacy.Obj("entities") ?? new JsonObject()).Arr("media") ?? new JsonArray();
        ReplyCount = legacy.IntOr("reply_count", 0);
        FavoriteCount = legacy.IntOr("favorite_count", 0);
        Favorited = legacy.BoolOr("favorited", false);
        RetweetCount = legacy.IntOr("retweet_count", 0);
        PlaceData = legacy.Obj("place");
        BookmarkCount = legacy.IntOr("bookmark_count", 0);
        Bookmarked = legacy.BoolOr("bookmarked", false);
        var editControl = data.Sub("edit_control");
        EditTweetIds = editControl.Arr("edit_tweet_ids").Strings();
        EditableUntilMsecs = editControl.Long("editable_until_msecs");
        IsTranslatable = data.BoolOr("is_translatable", false);
        IsEditEligible = editControl.BoolOr("is_edit_eligible", false);
        EditsRemaining = editControl.Int("edits_remaining");
        ViewCount = data.Sub("views").Str("count");
        ViewCountState = data.Sub("views").Str("state");
        HasCommunityNotes = data.BoolOr("has_birdwatch_notes", false);

        Quote = null;
        if (data.Get("quoted_status_result") is JsonObject quotedResult && quotedResult.Get("result") is JsonObject quoted)
        {
            if (quoted.Get("tweet") is JsonObject inner) quoted = inner;
            if (quoted.Str("__typename") != "TweetTombstone")
            {
                var quotedUser = quoted.Sub("core").Sub("user_results").Obj("result");
                Quote = new GuestTweet(client, quoted, quotedUser is null ? null : new GuestUser(client, quotedUser));
            }
        }

        RetweetedTweet = null;
        if (legacy.Get("retweeted_status_result") is JsonObject retweetedResult && retweetedResult.Get("result") is JsonObject retweeted)
        {
            if (retweeted.Get("tweet") is JsonObject inner) retweeted = inner;
            var retweetedUser = retweeted.Sub("core").Sub("user_results").Obj("result");
            RetweetedTweet = new GuestTweet(client, retweeted, retweetedUser is null ? null : new GuestUser(client, retweetedUser));
        }

        var noteTweetResults = data.FindDict("note_tweet_results", findOne: true);
        FullText = Text;
        JsonArray? hashtags;
        if (noteTweetResults.Count > 0 && noteTweetResults[0] is JsonObject note)
        {
            var text = note.FindDict("text", findOne: true);
            if (text.Count > 0 && text[0].AsStr() is { } t) FullText = t;
            var entitySet = note.Sub("result").Sub("entity_set");
            Urls = UrlEntity.ListFrom(entitySet.Arr("urls"));
            hashtags = entitySet.Arr("hashtags");
        }
        else
        {
            var entities = legacy.Sub("entities");
            Urls = UrlEntity.ListFrom(entities.Arr("urls"));
            hashtags = entities.Arr("hashtags");
        }
        Hashtags = hashtags.Objects().Select(h => h.Str("text") ?? "").ToList();

        CommunityNote = null;
        if (data.Obj("birdwatch_pivot") is { } pivot && pivot.ContainsKey("note"))
            CommunityNote = new BirdwatchNote(pivot.Sub("note").Str("rest_id") ?? "", pivot.Sub("subtitle").Str("text"));

        var card = data.Obj("card");
        var cardName = card?.Sub("legacy").Str("name");
        PollData = card is not null && cardName is not null && cardName.StartsWith("poll", StringComparison.Ordinal) ? card : null;

        HasCard = card is not null;
        if (card is not null && card.Sub("legacy").ContainsKey("binding_values"))
        {
            var binding = Tweet.ParseBindingValues(card.Sub("legacy").Get("binding_values")) ?? new Dictionary<string, JsonNode?>();
            if (binding.TryGetValue("title", out var title)) ThumbnailTitle = title.Str("string_value");
            if (binding.TryGetValue("thumbnail_image_original", out var thumb)) ThumbnailUrl = thumb.Sub("image_value").Str("url");
        }
    }

    public DateTimeOffset CreatedAtDatetime => Utils.TimestampToDateTime(CreatedAt);

    /// <summary>添付メディア。</summary>
    public List<Media> Media
    {
        get
        {
            var list = new List<Media>();
            foreach (var entry in _media.Objects())
            {
                var media = Twikit.Media.FromData(_client, entry);
                if (media is not null) list.Add(media);
            }
            return list;
        }
    }

    public override string ToString() => $"<Tweet id=\"{Id}\">";
    public bool Equals(GuestTweet? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as GuestTweet);
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>タイムラインのエントリなどから <see cref="GuestTweet"/> を作ります。</summary>
    public static GuestTweet? FromData(GuestClient client, JsonNode? data)
    {
        var found = data.FindDict("result", findOne: true);
        if (found.Count == 0 || found[0] is not JsonObject tweetData) return null;
        if (tweetData.Str("__typename") == "TweetTombstone") return null;
        if (tweetData.Get("tweet") is JsonObject inner) tweetData = inner;
        if (tweetData.Get("core") is not JsonObject core) return null;
        var userResults = core.Get("user_results");
        if (userResults is not JsonObject || !userResults.Has("result")) return null;
        if (!tweetData.ContainsKey("legacy")) return null;
        var userData = userResults.Get("result") as JsonObject ?? new JsonObject();
        return new GuestTweet(client, tweetData, new GuestUser(client, userData));
    }
}
