using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Twikit;

/// <summary>ツイートに付いたコミュニティノートの要約。</summary>
/// <param name="Id">ノートの ID。</param>
/// <param name="Text">ノートの本文。</param>
public sealed record BirdwatchNote(string Id, string? Text);

/// <summary>
/// ツイート。
/// </summary>
public class Tweet : IEquatable<Tweet>
{
    private readonly Client _client;

    /// <summary>X から受け取った生の JSON（tweet result）。</summary>
    public JsonObject Data { get; private set; }

    /// <summary><c>legacy</c> ブロック。</summary>
    public JsonObject Legacy { get; private set; }

    /// <summary>ツイートの投稿者。</summary>
    public User? User { get; set; }

    /// <summary>ツイートへの返信。</summary>
    public Result<Tweet>? Replies { get; set; }

    /// <summary>このツイートが返信している先のツイート（会話の上流）。</summary>
    public List<Tweet>? ReplyTo { get; set; }

    /// <summary>関連ツイート。</summary>
    public List<Tweet>? RelatedTweets { get; set; }

    /// <summary>投稿者自身によるスレッド（古い順、このツイート自身を含む）。</summary>
    public List<Tweet>? Thread { get; set; }

    /// <summary>
    /// このツイートが含まれていた会話モジュール内の全ツイート ID（X がまとめたもの）。
    /// 会話モジュール経由でなければ null。
    /// </summary>
    public List<string>? ConversationIds { get; set; }

    /// <summary>コミュニティタイムラインから取得した場合の所属コミュニティ。</summary>
    public Community? Community { get; set; }

    public Tweet(Client client, JsonObject data, User? user = null)
    {
        _client = client;
        Data = data;
        Legacy = data.Obj("legacy") ?? new JsonObject();
        User = user;
    }

    /// <summary>ツイートの一意な ID。</summary>
    public string Id => Data.Str("rest_id") ?? "";

    /// <summary>作成日時（X の表記）。</summary>
    public string? CreatedAt => Legacy.Str("created_at");

    /// <summary>本文。リツイートの場合は <c>RT @user: ...</c> の形式（140 文字で切り詰められる）。</summary>
    public string Text => Legacy.Str("full_text") ?? "";

    /// <summary>言語。</summary>
    public string? Lang => Legacy.Str("lang");

    /// <summary>x.com 上の正規 URL。</summary>
    public string Url
    {
        get
        {
            var screenName = User is not null && User.ScreenName.Length > 0 ? User.ScreenName : "i";
            return $"https://x.com/{screenName}/status/{Id}";
        }
    }

    /// <summary>
    /// 添付された長文記事（Article）。無ければ null。
    /// 記事を持つツイートの <see cref="Text"/> には t.co のリンクしか無いため、タイトルと導入文はここから読みます。
    /// X は本文を送ってこないので、得られるのはタイトル・プレビュー・カバー画像だけです。
    /// </summary>
    public Article? Article
    {
        get
        {
            var result = Data.Sub("article").Sub("article_results").Get("result");
            return result is JsonObject o ? new Article(o) : null;
        }
    }

    /// <summary>投稿元クライアントを表す生のアンカー（例: <c>&lt;a href="..."&gt;Twitter for iPhone&lt;/a&gt;</c>）。</summary>
    public string? Source => Data.Str("source") ?? Legacy.Str("source");

    /// <summary><see cref="Source"/> から取り出したクライアント名。</summary>
    public string? SourceName
    {
        get
        {
            var source = Source;
            if (string.IsNullOrEmpty(source)) return null;
            var match = Regex.Match(source, @">(.*?)</a>");
            return match.Success ? match.Groups[1].Value : source;
        }
    }

    /// <summary><see cref="Source"/> から取り出したクライアントの URL。</summary>
    public string? SourceUrl
    {
        get
        {
            var source = Source;
            if (string.IsNullOrEmpty(source)) return null;
            var match = Regex.Match(source, @"href=['""](.*?)['""]");
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    /// <summary>返信先のツイート ID。</summary>
    public string? InReplyTo => Legacy.Str("in_reply_to_status_id_str");
    /// <summary>引用ツイートか。</summary>
    public bool IsQuoteStatus => Legacy.BoolOr("is_quote_status", false);
    /// <summary>センシティブな内容を含む可能性があるか。</summary>
    public bool PossiblySensitive => Legacy.BoolOr("possibly_sensitive", false);
    public bool PossiblySensitiveEditable => Legacy.BoolOr("possibly_sensitive_editable", false);
    /// <summary>引用された数。</summary>
    public int QuoteCount => Legacy.IntOr("quote_count", 0);
    /// <summary>返信の数。</summary>
    public int ReplyCount => Legacy.IntOr("reply_count", 0);
    /// <summary>いいねの数。</summary>
    public int FavoriteCount => Legacy.IntOr("favorite_count", 0);
    /// <summary>いいね済みか。</summary>
    public bool Favorited => Legacy.BoolOr("favorited", false);
    /// <summary>リツイートの数。</summary>
    public int RetweetCount => Legacy.IntOr("retweet_count", 0);
    /// <summary>ブックマークの数。</summary>
    public int BookmarkCount => Legacy.IntOr("bookmark_count", 0);
    /// <summary>ブックマーク済みか。</summary>
    public bool Bookmarked => Legacy.BoolOr("bookmarked", false);

    private JsonObject EditControl => Data.Sub("edit_control");

    /// <summary>編集履歴のツイート ID。</summary>
    public List<string> EditTweetIds => EditControl.Arr("edit_tweet_ids").Strings();
    /// <summary>編集可能な期限（ミリ秒）。</summary>
    public long? EditableUntilMsecs => EditControl.Long("editable_until_msecs");
    /// <summary>翻訳可能か。</summary>
    public bool IsTranslatable => Data.BoolOr("is_translatable", false);
    /// <summary>編集可能か。</summary>
    public bool IsEditEligible => EditControl.BoolOr("is_edit_eligible", false);
    /// <summary>残りの編集回数。</summary>
    public int? EditsRemaining => EditControl.Int("edits_remaining");

    /// <summary>
    /// 表示回数。無ければ null。
    /// </summary>
    /// <remarks>
    /// X が数値を送るかどうかは呼び出し方ではなくツイート側の性質です。
    /// <see cref="ViewCountState"/> が 'EnabledWithCount' なら数値があり、'Enabled' なら X が伏せています。
    /// </remarks>
    public long? ViewCount => Data.Sub("views").Long("count");

    /// <summary>表示回数の状態。</summary>
    public string? ViewCountState => Data.Sub("views").Str("state");

    /// <summary>コミュニティノートが付いているか。</summary>
    public bool HasCommunityNotes => Data.BoolOr("has_birdwatch_notes", false);

    /// <summary>引用元のツイート。</summary>
    public Tweet? Quote
    {
        get
        {
            var quoted = Data.Get("quoted_status_result");
            return quoted.IsTruthy() ? FromData(_client, quoted) : null;
        }
    }

    /// <summary>引用元ツイートの ID（引用ツイートの場合）。</summary>
    public string? QuotedStatusId => Legacy.Str("quoted_status_id_str");

    /// <summary>リツイート元のツイート。</summary>
    public Tweet? RetweetedTweet
    {
        get
        {
            var retweeted = Legacy.Get("retweeted_status_result");
            return retweeted.IsTruthy() ? FromData(_client, retweeted) : null;
        }
    }

    private JsonObject? NoteTweetResults
    {
        get
        {
            var note = Data.Obj("note_tweet");
            return note is not null && note.ContainsKey("note_tweet_results") ? note.Obj("note_tweet_results") : null;
        }
    }

    /// <summary>
    /// 全文。長文ツイートは note_tweet から、リツイートは元ツイートの本文を返します
    /// （リツイート自身の本文は 140 文字で切り詰められているため）。<c>RT @user: ...</c> 形式が欲しければ <see cref="Text"/>。
    /// </summary>
    public string FullText
    {
        get
        {
            var noteTweetResults = NoteTweetResults;
            if (noteTweetResults is not null)
            {
                var text = noteTweetResults.Sub("result").Str("text");
                if (text is not null) return text;
            }
            var retweeted = RetweetedTweet;
            if (retweeted is not null) return retweeted.FullText;
            return Text;
        }
    }

    /// <summary>本文に含まれるハッシュタグ。</summary>
    public List<string> Hashtags
    {
        get
        {
            JsonArray? hashtags;
            var noteTweetResults = NoteTweetResults;
            if (noteTweetResults is not null)
                hashtags = noteTweetResults.Sub("result").Sub("entity_set").Arr("hashtags");
            else
                hashtags = Legacy.Sub("entities").Arr("hashtags");
            return hashtags.Objects().Select(h => h.Str("text") ?? "").ToList();
        }
    }

    /// <summary>本文に含まれる URL（常にリスト）。</summary>
    public List<UrlEntity> Urls
    {
        get
        {
            var noteTweetResults = NoteTweetResults;
            if (noteTweetResults is not null)
                return UrlEntity.ListFrom(noteTweetResults.Sub("result").Sub("entity_set").Arr("urls"));
            return UrlEntity.ListFrom(Legacy.Sub("entities").Arr("urls"));
        }
    }

    /// <summary>コミュニティノートの要約（無ければ null）。</summary>
    public BirdwatchNote? CommunityNote
    {
        get
        {
            var pivot = Data.Obj("birdwatch_pivot");
            if (pivot is null || !pivot.ContainsKey("note")) return null;
            return new BirdwatchNote(pivot.Sub("note").Str("rest_id") ?? "", pivot.Sub("subtitle").Str("text"));
        }
    }

    private Dictionary<string, JsonNode?>? BindingValues
    {
        get
        {
            var card = Data.Obj("card");
            if (card is null || !card.ContainsKey("legacy")) return null;
            var legacy = card.Sub("legacy");
            if (!legacy.ContainsKey("binding_values")) return null;
            return ParseBindingValues(legacy.Get("binding_values"));
        }
    }

    internal static Dictionary<string, JsonNode?>? ParseBindingValues(JsonNode? bindingValues)
    {
        if (bindingValues is JsonArray list)
        {
            var dict = new Dictionary<string, JsonNode?>();
            foreach (var item in list.Objects())
            {
                var key = item.Str("key");
                if (key is not null) dict[key] = item.Get("value");
            }
            return dict;
        }
        if (bindingValues is JsonObject obj)
            return obj.ToDictionary(kv => kv.Key, kv => kv.Value);
        return null;
    }

    /// <summary>カードを持つか。</summary>
    public bool HasCard => Data.ContainsKey("card");

    /// <summary>カードに表示される Web ページのタイトル。</summary>
    public string? ThumbnailTitle
    {
        get
        {
            var binding = BindingValues;
            if (binding is null || !binding.TryGetValue("title", out var title)) return null;
            return title.Str("string_value");
        }
    }

    /// <summary>カードに表示される画像の URL。</summary>
    public string? ThumbnailUrl
    {
        get
        {
            var binding = BindingValues;
            if (binding is null || !binding.TryGetValue("thumbnail_image_original", out var thumb)) return null;
            return thumb.Sub("image_value").Str("url");
        }
    }

    /// <summary><see cref="CreatedAt"/> を <see cref="DateTimeOffset"/> にしたもの。</summary>
    public DateTimeOffset CreatedAtDatetime => Utils.TimestampToDateTime(CreatedAt ?? throw new InvalidOperationException("The tweet has no created_at."));

    /// <summary>投票（無ければ null）。</summary>
    public Poll? Poll
    {
        get
        {
            var card = Data.Obj("card");
            var name = card?.Sub("legacy").Str("name");
            return card is not null && name is not null && name.StartsWith("poll", StringComparison.Ordinal)
                ? new Poll(_client, card, this)
                : null;
        }
    }

    /// <summary>関連付けられた場所（無ければ null）。</summary>
    public Place? Place
    {
        get
        {
            var placeData = Legacy.Obj("place");
            return placeData is not null && placeData.Count > 0 ? new Place(_client, placeData) : null;
        }
    }

    /// <summary>添付メディア（<see cref="Photo"/>、<see cref="Video"/>、<see cref="AnimatedGif"/>）。</summary>
    public List<Media> Media
    {
        get
        {
            // `entities.media` carries only the FIRST attachment and no
            // video_info; the complete set lives in `extended_entities`.
            var mediaData = Legacy.Sub("extended_entities").Arr("media");
            if (mediaData is null || mediaData.Count == 0)
                mediaData = Legacy.Sub("entities").Arr("media");
            var list = new List<Media>();
            foreach (var entry in mediaData.Objects())
            {
                var media = Twikit.Media.FromData(_client, entry);
                if (media is not null) list.Add(media);
            }
            return list;
        }
    }

    /// <summary>ツイートを削除します。</summary>
    public Task<HttpResponseMessage> DeleteAsync() => _client.DeleteTweetAsync(Id);

    /// <summary>いいねします。</summary>
    public Task<HttpResponseMessage> FavoriteAsync() => _client.FavoriteTweetAsync(Id);

    /// <summary>いいねを解除します。</summary>
    public Task<HttpResponseMessage> UnfavoriteAsync() => _client.UnfavoriteTweetAsync(Id);

    /// <summary>リツイートします。</summary>
    public Task<HttpResponseMessage> RetweetAsync() => _client.RetweetAsync(Id);

    /// <summary>リツイートを取り消します。</summary>
    public Task<HttpResponseMessage> DeleteRetweetAsync() => _client.DeleteRetweetAsync(Id);

    /// <summary>ブックマークに追加します。</summary>
    public Task<HttpResponseMessage> BookmarkAsync() => _client.BookmarkTweetAsync(Id);

    /// <summary>ブックマークから削除します。</summary>
    public Task<HttpResponseMessage> DeleteBookmarkAsync() => _client.DeleteBookmarkAsync(Id);

    /// <summary>
    /// このツイートに返信します。
    /// </summary>
    /// <param name="text">返信の本文。</param>
    /// <param name="mediaIds">添付するメディア ID の一覧。</param>
    /// <param name="conversationControl">返信できる相手（'followers'、'verified'、'mentioned'）。</param>
    /// <param name="attachmentUrl">引用するツイートの URL。</param>
    /// <param name="communityId">投稿先コミュニティの ID。</param>
    /// <param name="shareWithFollowers">コミュニティ投稿をフォロワーにも共有するか。</param>
    /// <param name="isNoteTweet">280 文字を超える長文ツイートにするか（Premium）。</param>
    /// <param name="richtextOptions">装飾オプション（Premium）。</param>
    /// <param name="editTweetId">編集するツイートの ID（Premium）。</param>
    public Task<Tweet> ReplyAsync(
        string text = "",
        IList<string>? mediaIds = null,
        string? conversationControl = null,
        string? attachmentUrl = null,
        string? communityId = null,
        bool shareWithFollowers = false,
        bool isNoteTweet = false,
        JsonArray? richtextOptions = null,
        string? editTweetId = null)
        => _client.CreateTweetAsync(text, mediaIds, null, Id, conversationControl, attachmentUrl, communityId,
            shareWithFollowers, isNoteTweet, richtextOptions, editTweetId);

    /// <summary>リツイートしたユーザーを取得します。</summary>
    public Task<Result<User>> GetRetweetersAsync(int count = 40, string? cursor = null)
        => _client.GetRetweetersAsync(Id, count, cursor);

    /// <summary>いいねしたユーザーを取得します。</summary>
    public Task<Result<User>> GetFavoritersAsync(int count = 40, string? cursor = null)
        => _client.GetFavoritersAsync(Id, count, cursor);

    /// <summary>類似ツイートを取得します（Premium のみ）。</summary>
    public Task<List<Tweet>> GetSimilarTweetsAsync() => _client.GetSimilarTweetsAsync(Id);

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetTweetByIdAsync(Id).ConfigureAwait(false);
        Data = fresh.Data;
        Legacy = fresh.Legacy;
        User = fresh.User;
        Replies = fresh.Replies;
        ReplyTo = fresh.ReplyTo;
        RelatedTweets = fresh.RelatedTweets;
        Thread = fresh.Thread;
        ConversationIds = fresh.ConversationIds;
        Community = fresh.Community;
    }

    public override string ToString() => $"<Tweet id=\"{Id}\">";
    public bool Equals(Tweet? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Tweet);
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// タイムラインのエントリなどから <see cref="Tweet"/> を作ります（Python 版の <c>tweet_from_data</c>）。
    /// 墓石（削除済み）や投稿者情報の無いエントリは null。
    /// </summary>
    public static Tweet? FromData(Client client, JsonNode? data)
    {
        var found = data.FindDict("result", findOne: true);
        if (found.Count == 0 || found[0] is not JsonObject tweetData) return null;

        if (tweetData.Str("__typename") == "TweetTombstone") return null;
        if (tweetData.Get("tweet") is JsonObject inner) tweetData = inner;
        var core = tweetData.Get("core");
        if (core is not JsonObject) return null;
        // A conversation can carry a promoted module whose result is the
        // advertiser's own User object: it has a `core` holding created_at, name
        // and screen_name, but no `user_results` at all.
        var userResults = core.Get("user_results");
        if (userResults is not JsonObject || !userResults.Has("result")) return null;
        if (!tweetData.ContainsKey("legacy")) return null;

        var userData = userResults.Get("result") as JsonObject ?? new JsonObject();
        return new Tweet(client, tweetData, new User(client, userData));
    }
}

/// <summary>予約投稿。</summary>
public sealed class ScheduledTweet
{
    private readonly Client _client;

    /// <summary>予約投稿の ID。</summary>
    public string Id { get; }
    /// <summary>投稿予定時刻（Unix 秒）。</summary>
    public long ExecuteAt { get; }
    /// <summary>状態。</summary>
    public string? State { get; }
    /// <summary>種類。</summary>
    public string? Type { get; }
    /// <summary>本文。</summary>
    public string? Text { get; }
    /// <summary>添付メディアの情報。</summary>
    public List<JsonObject> Media { get; }

    public ScheduledTweet(Client client, JsonObject data)
    {
        _client = client;
        Id = data.Str("rest_id") ?? "";
        var scheduling = data.Sub("scheduling_info");
        ExecuteAt = scheduling.Long("execute_at") ?? 0;
        State = scheduling.Str("state");
        var request = data.Sub("tweet_create_request");
        Type = request.Str("type");
        Text = request.Str("status");
        Media = data.ArrOrEmpty("media_entities").Objects()
            .Select(i => i.Obj("media_info") ?? new JsonObject()).ToList();
    }

    /// <summary>予約投稿を削除します。</summary>
    public Task<HttpResponseMessage> DeleteAsync() => _client.DeleteScheduledTweetAsync(Id);

    public override string ToString() => $"<ScheduledTweet id=\"{Id}\">";
}

/// <summary>削除済みなどで表示できないツイートの墓石。</summary>
public sealed class TweetTombstone : IEquatable<TweetTombstone>
{
    public string Id { get; }
    public string? Text { get; }

    public TweetTombstone(string tweetId, JsonObject data)
    {
        Id = tweetId;
        Text = data.Sub("text").Str("text");
    }

    public override string ToString() => $"<TweetTombstone id=\"{Id}\">";
    public bool Equals(TweetTombstone? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as TweetTombstone);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>投票の選択肢。</summary>
/// <param name="Number">選択肢の番号（'1' から）。</param>
/// <param name="Label">ラベル。</param>
/// <param name="Count">票数（文字列）。</param>
public sealed record PollChoice(string Number, string? Label, string Count);

/// <summary>
/// ツイートに付いた投票。
/// </summary>
public sealed class Poll : IEquatable<Poll>
{
    private readonly Client _client;

    /// <summary>投票が付いたツイート。</summary>
    public Tweet? Tweet { get; }
    /// <summary>投票の ID。</summary>
    public string Id { get; }
    /// <summary>投票の名前（poll2choice_text_only など）。</summary>
    public string? Name { get; }
    /// <summary>選択肢の一覧。</summary>
    public List<PollChoice> Choices { get; }
    /// <summary>投票期間（分）。</summary>
    public int? DurationMinutes { get; }
    /// <summary>終了日時（UTC）。</summary>
    public string? EndDatetimeUtc { get; }
    /// <summary>最終更新日時（UTC）。</summary>
    public string? LastUpdatedDatetimeUtc { get; }
    /// <summary>票数が確定しているか。</summary>
    public bool CountsAreFinal { get; }
    /// <summary>自分が選んだ選択肢の番号。</summary>
    public string? SelectedChoice { get; }

    public Poll(Client client, JsonObject data, Tweet? tweet = null)
    {
        _client = client;
        Tweet = tweet;

        var legacy = data.Sub("legacy");
        var bindingValues = Twikit.Tweet.ParseBindingValues(legacy.Get("binding_values")) ?? new Dictionary<string, JsonNode?>();

        Id = data.Str("rest_id") ?? "";
        Name = legacy.Str("name");

        // Image polls are named poll2choice_image etc., so the text_only
        // pattern found nothing and indexing [0] raised IndexError.
        var match = Regex.Match(Name ?? "", @"poll(\d)choice");
        var choicesNumber = match.Success ? int.Parse(match.Groups[1].Value) : 0;
        Choices = new List<PollChoice>();
        for (var i = 1; i <= choicesNumber; i++)
        {
            bindingValues.TryGetValue($"choice{i}_label", out var label);
            bindingValues.TryGetValue($"choice{i}_count", out var count);
            Choices.Add(new PollChoice(i.ToString(), label.Str("string_value"), count.Str("string_value") ?? "0"));
        }

        // A poll that is still open ships neither per-choice counts nor
        // `last_updated_datetime_utc`, so every read has to tolerate absence.
        string? Binding(string key, string kind = "string_value")
        {
            if (!bindingValues.TryGetValue(key, out var value) || value is not JsonObject o) return null;
            return o.Get(kind).AsStr();
        }

        var duration = Binding("duration_minutes");
        DurationMinutes = duration is not null && int.TryParse(duration, out var d) ? d : null;
        EndDatetimeUtc = Binding("end_datetime_utc");
        LastUpdatedDatetimeUtc = Binding("last_updated_datetime_utc");
        CountsAreFinal = bindingValues.TryGetValue("counts_are_final", out var final) && final is JsonObject fo
                         && (fo.Get("boolean_value").AsBool() ?? false);
        SelectedChoice = Binding("selected_choice");
    }

    /// <summary>
    /// 投票します。
    /// </summary>
    /// <param name="selectedChoice">選択肢のラベル、またはその番号（'1'、'2'、...）。X は番号しか受け付けないので、ラベルは変換されます。</param>
    public Task<Poll> VoteAsync(string selectedChoice)
    {
        var choice = selectedChoice;
        foreach (var entry in Choices)
        {
            if (entry.Label == selectedChoice)
            {
                choice = entry.Number;
                break;
            }
        }
        var tweetId = Tweet?.Id ?? throw new InvalidOperationException("This poll is not attached to a tweet.");
        return _client.VoteAsync(choice, Id, tweetId, Name ?? "");
    }

    public override string ToString() => $"<Poll id=\"{Id}\">";
    public bool Equals(Poll? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Poll);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// コミュニティノート。
/// </summary>
public sealed class CommunityNote : IEquatable<CommunityNote>
{
    private readonly Client _client;

    /// <summary>ノートの ID。</summary>
    public string Id { get; private set; } = "";
    /// <summary>ノートの本文。</summary>
    public string? Text { get; private set; }
    /// <summary>誤解を招く情報を示すタグ。</summary>
    public List<string> MisleadingTags { get; private set; } = new();
    /// <summary>情報源が信頼できるか。</summary>
    public bool TrustworthySources { get; private set; }
    /// <summary>役に立つ情報を示すタグ。</summary>
    public List<string> HelpfulTags { get; private set; } = new();
    /// <summary>作成時刻。</summary>
    public long? CreatedAt { get; private set; }
    /// <summary>異議申し立てできるか。</summary>
    public bool CanAppeal { get; private set; }
    /// <summary>異議申し立ての状態。</summary>
    public string? AppealStatus { get; private set; }
    /// <summary>メディアに対するノートか。</summary>
    public bool IsMediaNote { get; private set; }
    public string? MediaNoteMatches { get; private set; }
    /// <summary>ノートに関連する Birdwatch プロフィール。</summary>
    public JsonObject? BirdwatchProfile { get; private set; }
    /// <summary>ノートが付いたツイートの ID。</summary>
    public string? TweetId { get; private set; }

    public CommunityNote(Client client, JsonObject data)
    {
        _client = client;
        Load(data);
    }

    private void Load(JsonObject data)
    {
        Id = data.Str("rest_id") ?? "";
        var dataV1 = data.Sub("data_v1");
        Text = dataV1.Sub("summary").Str("text");
        MisleadingTags = dataV1.Arr("misleading_tags").Strings();
        TrustworthySources = dataV1.BoolOr("trustworthy_sources", false);
        HelpfulTags = data.Arr("helpful_tags").Strings();
        CreatedAt = data.Long("created_at");
        CanAppeal = data.BoolOr("can_appeal", false);
        AppealStatus = data.Str("appeal_status");
        IsMediaNote = data.BoolOr("is_media_note", false);
        MediaNoteMatches = data.Str("media_note_matches");
        BirdwatchProfile = data.Obj("birdwatch_profile");
        TweetId = data.Sub("tweet_results").Sub("result").Str("rest_id");
    }

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetCommunityNoteAsync(Id).ConfigureAwait(false);
        Id = fresh.Id; Text = fresh.Text; MisleadingTags = fresh.MisleadingTags; TrustworthySources = fresh.TrustworthySources;
        HelpfulTags = fresh.HelpfulTags; CreatedAt = fresh.CreatedAt; CanAppeal = fresh.CanAppeal; AppealStatus = fresh.AppealStatus;
        IsMediaNote = fresh.IsMediaNote; MediaNoteMatches = fresh.MediaNoteMatches; BirdwatchProfile = fresh.BirdwatchProfile; TweetId = fresh.TweetId;
    }

    public override string ToString() => $"<CommunityNote id=\"{Id}\">";
    public bool Equals(CommunityNote? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as CommunityNote);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// ツイートに添付された長文記事（Article）。
/// </summary>
public sealed class Article
{
    /// <summary>生の JSON。</summary>
    public JsonObject Data { get; }
    /// <summary>記事の ID。</summary>
    public string? Id { get; }
    /// <summary>タイトル。</summary>
    public string? Title { get; }
    /// <summary>導入文として表示されるプレビューテキスト。</summary>
    public string? PreviewText { get; }
    /// <summary>カバー画像の URL。</summary>
    public string? CoverImageUrl { get; }
    /// <summary>初回公開時刻（Unix 秒）。</summary>
    public long? PublishedAt { get; }
    /// <summary>最終編集時刻（Unix 秒）。</summary>
    public long? ModifiedAt { get; }

    public Article(JsonObject data)
    {
        Data = data;
        Id = data.Str("rest_id");
        Title = data.Str("title");
        PreviewText = data.Str("preview_text");
        CoverImageUrl = data.Sub("cover_media").Sub("media_info").Str("original_img_url");
        PublishedAt = data.Sub("metadata").Long("first_published_at_secs");
        ModifiedAt = data.Sub("lifecycle_state").Long("modified_at_secs");
    }

    public override string ToString() => $"<Article id=\"{Id}\" title=\"{Title}\">";
}
