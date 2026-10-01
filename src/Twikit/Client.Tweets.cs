using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Twikit.Api;
using Twikit.Internal;

namespace Twikit;

public partial class Client
{
    internal static string Capitalize(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

    private static bool HasCursor(string? cursor) => !string.IsNullOrEmpty(cursor);

    /// <summary>要求件数で切り詰め、余剰を next() 用に残した <see cref="Result{T}"/> を作ります。</summary>
    internal static Result<T> Page<T>(List<T> results, int count, string? nextCursor, Func<Task<Result<T>>>? fetchNext,
        string? previousCursor = null, Func<Task<Result<T>>>? fetchPrevious = null)
    {
        var (page, overflow) = Paging.Limited(results, count);
        return new Result<T>(page,
            HasCursor(nextCursor) ? fetchNext : null, nextCursor,
            HasCursor(previousCursor) ? fetchPrevious : null, previousCursor,
            overflow, count);
    }

    /// <summary>All tweet ids of a conversation module, when X ships them.</summary>
    private static List<string>? ConversationIds(JsonNode? content)
    {
        var ids = content.Sub("metadata").Sub("conversationMetadata").Get("allTweetIds");
        return ids is JsonArray a ? a.Strings() : null;
    }

    /// <summary>Author id of one entry inside a profile-conversation module.</summary>
    private static string? ConversationAuthorId(JsonNode? item)
    {
        var result = item.Sub("item").Sub("itemContent").Sub("tweet_results").Sub("result");
        // TweetWithVisibilityResults nests the real tweet one level down.
        if (result.ContainsKey("tweet")) result = result.Sub("tweet");
        return result.Sub("core").Sub("user_results").Sub("result").Str("rest_id");
    }

    /// <summary>
    /// クエリと種類を指定してツイートを検索します。
    /// </summary>
    /// <param name="query">検索クエリ。</param>
    /// <param name="product">'Top'、'Latest'、'Media' のいずれか。</param>
    /// <param name="count">取得する件数（1～20）。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Tweet>> SearchTweetAsync(string query, string product, int count = 20, string? cursor = null)
    {
        product = Capitalize(product);

        var response = await Gql.SearchTimelineAsync(query, product, count, cursor).ConfigureAwait(false);
        var instructionsFound = response.Json.FindDict("instructions", findOne: true);
        if (instructionsFound.Count == 0 || instructionsFound[0] is not JsonArray instructions)
            return Result<Tweet>.Empty();

        JsonArray items;
        if (product == "Media" && cursor is not null)
        {
            items = instructions.FirstDict("moduleItems") as JsonArray ?? new JsonArray();
        }
        else
        {
            items = instructions.FirstDict("entries") as JsonArray ?? new JsonArray();
            if (product == "Media")
            {
                var first = items.Count > 0 ? items[0].Sub("content") : null;
                items = first is not null && first.ContainsKey("items") ? first.ArrOrEmpty("items") : new JsonArray();
            }
        }

        string? nextCursor = null;
        string? previousCursor = null;
        var results = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal)) nextCursor = item.Sub("content").Str("value");
            if (entryId.StartsWith("cursor-top", StringComparison.Ordinal)) previousCursor = item.Sub("content").Str("value");
            if (!entryId.StartsWith("tweet", StringComparison.Ordinal) && !entryId.StartsWith("search-grid", StringComparison.Ordinal))
                continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }

        if (nextCursor is null)
        {
            if (product == "Media")
            {
                var entries = instructions.FirstDict("entries") as JsonArray;
                nextCursor = JsonExtensions.LastCursor(entries);
                previousCursor = JsonExtensions.CursorAt(entries, -2);
            }
            else
            {
                // An instruction without an `entry` key (TerminateTimeline) or a
                // list shorter than two must not take down the whole search.
                string? EntryCursor(int index) => instructions.At(index).Sub("entry").Sub("content").Str("value");
                nextCursor = EntryCursor(-1);
                previousCursor = EntryCursor(-2);
            }
        }

        var product1 = product;
        return Page(results, count, nextCursor,
            () => SearchTweetAsync(query, product1, count, nextCursor),
            previousCursor,
            () => SearchTweetAsync(query, product1, count, previousCursor));
    }

    /// <summary>
    /// ユーザーを検索します。
    /// </summary>
    /// <param name="query">検索クエリ。</param>
    /// <param name="count">1 リクエストあたりの取得件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<User>> SearchUserAsync(string query, int count = 20, string? cursor = null)
    {
        var response = await Gql.SearchTimelineAsync(query, "People", count, cursor).ConfigureAwait(false);
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var nextCursor = JsonExtensions.LastCursor(items);

        var results = new List<User>();
        foreach (var item in items.Objects())
        {
            if (!item.Sub("content").ContainsKey("itemContent")) continue;
            // An entry X could not resolve (deleted or restricted user) arrives without `result`.
            if (item.FirstDict("result") is not JsonObject userInfo) continue;
            results.Add(new User(this, userInfo));
        }

        return Page(results, count, nextCursor, () => SearchUserAsync(query, count, nextCursor));
    }

    /// <summary>指定したツイートに類似したツイートを取得します（Premium のみ）。</summary>
    public async Task<List<Tweet>> GetSimilarTweetsAsync(string tweetId)
    {
        var response = await Gql.SimilarPostsAsync(tweetId).ConfigureAwait(false);
        var results = new List<Tweet>();
        if (response.Json.FirstDict("entries") is not JsonArray items) return results;
        foreach (var item in items.Objects())
        {
            if (!item.EntryId().StartsWith("tweet", StringComparison.Ordinal)) continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }
        return results;
    }

    /// <summary>ユーザーのハイライトツイートを取得します。</summary>
    public async Task<Result<Tweet>> GetUserHighlightsTweetsAsync(string userId, int count = 20, string? cursor = null)
    {
        var response = await Gql.UserHighlightsTweetsAsync(userId, count, cursor).ConfigureAwait(false);
        var instructions = response.Json.FirstDict("instructions") as JsonArray;
        var instruction = JsonExtensions.FindEntryByType(instructions, "TimelineAddEntries");
        if (instruction is null) return Result<Tweet>.Empty();
        var entries = instruction.ArrOrEmpty("entries");
        string? previousCursor = null;
        string? nextCursor = null;
        var results = new List<Tweet>();

        foreach (var entry in entries.Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("tweet", StringComparison.Ordinal))
            {
                var tweet = Tweet.FromData(this, entry);
                if (tweet is not null) results.Add(tweet);
            }
            else if (entryId.StartsWith("cursor-top", StringComparison.Ordinal))
                previousCursor = entry.Sub("content").Str("value");
            else if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal))
                nextCursor = entry.Sub("content").Str("value");
        }

        return Page(results, count, nextCursor,
            () => GetUserHighlightsTweetsAsync(userId, count, nextCursor),
            previousCursor,
            () => GetUserHighlightsTweetsAsync(userId, count, previousCursor));
    }

    /// <summary>
    /// メディアをアップロードします（ファイルパス）。
    /// </summary>
    /// <param name="path">アップロードするファイルのパス。</param>
    /// <param name="waitForCompletion">処理完了を待つか。</param>
    /// <param name="statusCheckInterval">状態を確認する間隔（秒）。省略時は X が返す check_after_secs。</param>
    /// <param name="mediaType">MIME タイプ。省略時は内容から推定します。</param>
    /// <param name="mediaCategory">メディアカテゴリ（'tweet_gif'、'dm_gif' など）。</param>
    /// <param name="isLongVideo">2:20 を超える動画をアップロードするか（Premium のみ）。</param>
    /// <returns>アップロードしたメディアの ID。</returns>
    public async Task<string> UploadMediaAsync(string path, bool waitForCompletion = false, double? statusCheckInterval = null,
        string? mediaType = null, string? mediaCategory = null, bool isLongVideo = false)
    {
        var binary = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        return await UploadMediaAsync(binary, waitForCompletion, statusCheckInterval, mediaType, mediaCategory, isLongVideo).ConfigureAwait(false);
    }

    /// <summary>
    /// メディアをアップロードします（バイト列）。
    /// </summary>
    /// <param name="binary">メディアの内容。</param>
    /// <param name="waitForCompletion">処理完了を待つか。</param>
    /// <param name="statusCheckInterval">状態を確認する間隔（秒）。省略時は X が返す check_after_secs。</param>
    /// <param name="mediaType">MIME タイプ。省略時は内容から推定します。</param>
    /// <param name="mediaCategory">メディアカテゴリ（'tweet_gif'、'dm_gif' など）。</param>
    /// <param name="isLongVideo">2:20 を超える動画をアップロードするか（Premium のみ）。</param>
    /// <returns>アップロードしたメディアの ID。</returns>
    public async Task<string> UploadMediaAsync(byte[] binary, bool waitForCompletion = false, double? statusCheckInterval = null,
        string? mediaType = null, string? mediaCategory = null, bool isLongVideo = false)
    {
        mediaType ??= MimeGuess.Guess(binary)
                      ?? throw new TwitterException("Could not guess the media type; pass mediaType explicitly.");

        if (waitForCompletion)
        {
            if (mediaType == "image/gif")
            {
                if (mediaCategory is null)
                {
                    throw new TwitterException(
                        "`mediaCategory` must be specified to check the upload status of gif images ('dm_gif' or 'tweet_gif')");
                }
            }
            else if (mediaType.StartsWith("image", StringComparison.Ordinal))
            {
                // Checking the upload status of an image is impossible.
                waitForCompletion = false;
            }
        }

        var totalBytes = binary.Length;

        // ============ INIT =============
        var initResponse = await V11.UploadMediaInitAsync(mediaType, totalBytes, mediaCategory, isLongVideo).ConfigureAwait(false);
        var mediaId = initResponse.Object.Get("media_id_string").AsStr()
                      ?? initResponse.Object.Get("media_id").AsStr()
                      ?? throw new TwitterException("media/upload INIT returned no media_id.");

        // =========== APPEND ============
        const int maxSegmentSize = 8 * 1024 * 1024; // The maximum segment size is 8 MB
        var appendTasks = new List<Task<ApiResponse>>();
        var segmentIndex = 0;
        var bytesSent = 0;
        while (bytesSent < totalBytes)
        {
            var size = Math.Min(maxSegmentSize, totalBytes - bytesSent);
            var chunk = new byte[size];
            Buffer.BlockCopy(binary, bytesSent, chunk, 0, size);
            appendTasks.Add(V11.UploadMediaAppendAsync(isLongVideo, mediaId, segmentIndex, chunk));
            segmentIndex++;
            bytesSent += size;
        }
        await Task.WhenAll(appendTasks).ConfigureAwait(false);

        // ========== FINALIZE ===========
        await V11.UploadMediaFinalizeAsync(isLongVideo, mediaId).ConfigureAwait(false);

        if (waitForCompletion)
        {
            while (true)
            {
                var state = await CheckMediaStatusAsync(mediaId, isLongVideo).ConfigureAwait(false);
                var processingInfo = state.Sub("processing_info");
                if (processingInfo.ContainsKey("error"))
                    throw new InvalidMediaException(processingInfo.Sub("error").Str("message"));
                if (processingInfo.Str("state") == "succeeded") break;
                var delay = statusCheckInterval ?? processingInfo.Double("check_after_secs") ?? 1.0;
                await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
            }
        }

        return mediaId;
    }

    /// <summary>
    /// アップロードしたメディアの状態を確認します。
    /// </summary>
    /// <exception cref="NotFoundException">
    /// STATUS コマンドはチャンクアップロードにしか存在せず、画像は 1 リクエストで完了して
    /// <c>processing_info</c> を持たないため 404 になります。
    /// </exception>
    public async Task<JsonObject> CheckMediaStatusAsync(string mediaId, bool isLongVideo = false)
    {
        var response = await V11.UploadMediaStatusAsync(isLongVideo, mediaId).ConfigureAwait(false);
        return response.Object ?? new JsonObject();
    }

    /// <summary>
    /// アップロードしたメディアにメタデータ（代替テキスト、センシティブ警告）を付けます。
    /// </summary>
    /// <param name="mediaId">メディア ID。</param>
    /// <param name="altText">代替テキスト。</param>
    /// <param name="sensitiveWarning">'adult_content'、'graphic_violence'、'other' の一覧。</param>
    public async Task<HttpResponseMessage> CreateMediaMetadataAsync(string mediaId, string? altText = null, IEnumerable<string>? sensitiveWarning = null)
    {
        var response = await V11.CreateMediaMetadataAsync(mediaId, altText, sensitiveWarning).ConfigureAwait(false);
        return response.Http;
    }

    /// <summary>
    /// 投票を作成し、card URI を返します。
    /// </summary>
    /// <param name="choices">選択肢（最大 4 つ）。</param>
    /// <param name="durationMinutes">投票期間（分）。</param>
    public async Task<string> CreatePollAsync(IList<string> choices, int durationMinutes)
    {
        var response = await V11.CreateCardAsync(choices, durationMinutes).ConfigureAwait(false);
        return response.Object.Str("card_uri") ?? throw new TwitterException("cards/create returned no card_uri.");
    }

    /// <summary>
    /// 投票に票を入れます。
    /// </summary>
    /// <param name="selectedChoice">選択肢の番号（'1' から）。</param>
    /// <param name="cardUri">投票カードの URI。</param>
    /// <param name="tweetId">投票が付いた元ツイートの ID。</param>
    /// <param name="cardName">投票カードの名前。</param>
    public async Task<Poll> VoteAsync(string selectedChoice, string cardUri, string tweetId, string cardName)
    {
        var response = await V11.VoteAsync(selectedChoice, cardUri, tweetId, cardName).ConfigureAwait(false);
        var card = response.Object.CloneObj()?.Obj("card") ?? new JsonObject();
        var cardData = new JsonObject
        {
            ["rest_id"] = card.Str("url"),
            ["legacy"] = card,
        };
        return new Poll(this, cardData, null);
    }

    /// <summary>
    /// ツイートを作成します。
    /// </summary>
    /// <param name="text">本文。</param>
    /// <param name="mediaIds">添付するメディア ID の一覧（<see cref="UploadMediaAsync(string, bool, double?, string?, string?, bool)"/> で取得）。</param>
    /// <param name="pollUri">添付する投票の URI（<see cref="CreatePollAsync"/> で取得）。</param>
    /// <param name="replyTo">返信先ツイートの ID。</param>
    /// <param name="conversationControl">返信できる相手: 'followers'（フォロワーのみ）、'verified'（認証済みのみ）、'mentioned'（メンションした相手のみ）。</param>
    /// <param name="attachmentUrl">引用するツイートの URL。</param>
    /// <param name="communityId">投稿先コミュニティの ID。</param>
    /// <param name="shareWithFollowers">コミュニティ投稿をフォロワーにも共有するか。</param>
    /// <param name="isNoteTweet">280 文字を超える長文ツイートにするか（Premium のみ）。</param>
    /// <param name="richtextOptions">装飾オプション（Premium のみ）。</param>
    /// <param name="editTweetId">編集するツイートの ID（Premium のみ）。</param>
    /// <exception cref="DuplicateTweetException">重複ツイート。</exception>
    /// <exception cref="CouldNotTweetException">X が投稿を拒否した（日次上限など。メッセージは X のものをそのまま含みます）。</exception>
    public async Task<Tweet> CreateTweetAsync(
        string text = "",
        IList<string>? mediaIds = null,
        string? pollUri = null,
        string? replyTo = null,
        string? conversationControl = null,
        string? attachmentUrl = null,
        string? communityId = null,
        bool shareWithFollowers = false,
        bool isNoteTweet = false,
        JsonArray? richtextOptions = null,
        string? editTweetId = null)
    {
        var mediaEntities = new JsonArray();
        foreach (var mediaId in mediaIds ?? Array.Empty<string>())
            mediaEntities.Add(new JsonObject { ["media_id"] = mediaId, ["tagged_users"] = new JsonArray() });

        string? limitMode = null;
        if (conversationControl is not null)
        {
            limitMode = conversationControl.ToLowerInvariant() switch
            {
                "followers" => "Community",
                "verified" => "Verified",
                "mentioned" => "ByInvitation",
                _ => throw new ArgumentException($"Invalid conversationControl: {conversationControl}", nameof(conversationControl)),
            };
        }

        var response = await Gql.CreateTweetAsync(
            isNoteTweet, text, mediaEntities, pollUri, replyTo, attachmentUrl, communityId, shareWithFollowers,
            richtextOptions, editTweetId, limitMode).ConfigureAwait(false);
        var errors = JsonExtensions.FatalErrors(response.Json, "tweet_results");
        if (errors is not null)
        {
            ErrorCodes.RaiseExceptionsFromResponse(errors);
            // Lead with the message, e.g. "You've hit the daily limit. Subscribe
            // to Premium for higher limits. (501)", keep the rest reachable.
            throw new CouldNotTweetException(errors[0].Str("message") ?? errors[0].ToJsonString());
        }
        var result = (isNoteTweet || richtextOptions is not null)
            ? response.Json.Get("data").Get("notetweet_create").Get("tweet_results")
            : response.Json.Get("data").Get("create_tweet").Get("tweet_results");
        return Tweet.FromData(this, result) ?? throw new CouldNotTweetException("X returned no tweet: " + response.Text);
    }

    /// <summary>
    /// 予約投稿を作成します。
    /// </summary>
    /// <param name="scheduledAt">投稿する時刻（Unix 秒）。</param>
    /// <param name="text">本文。</param>
    /// <param name="mediaIds">添付するメディア ID の一覧。</param>
    /// <returns>予約投稿の ID。</returns>
    public async Task<string> CreateScheduledTweetAsync(long scheduledAt, string text = "", IList<string>? mediaIds = null)
    {
        var response = await Gql.CreateScheduledTweetAsync(scheduledAt, text, mediaIds).ConfigureAwait(false);
        var errors = JsonExtensions.FatalErrors(response.Json, "tweet");
        if (errors is not null)
        {
            ErrorCodes.RaiseExceptionsFromResponse(errors);
            throw new CouldNotTweetException(errors[0].Str("message") ?? errors[0].ToJsonString());
        }
        return response.Json.Get("data").Get("tweet").Str("rest_id") ?? throw new CouldNotTweetException("X returned no scheduled tweet id.");
    }

    /// <summary>ツイートを削除します。</summary>
    public async Task<HttpResponseMessage> DeleteTweetAsync(string tweetId)
        => (await Gql.DeleteTweetAsync(tweetId).ConfigureAwait(false)).Http;

    private static string? TrailingCursor(JsonArray entries)
    {
        // X has two shapes for the trailing cursor entry: the legacy
        // `content.itemContent.value` and a newer, flatter `content.value`.
        if (entries.Count == 0) return null;
        var last = entries[^1];
        if (!last.EntryId().StartsWith("cursor", StringComparison.Ordinal)) return null;
        var content = last.Sub("content");
        var itemContent = content.Get("itemContent");
        if (itemContent is JsonObject ic && ic.ContainsKey("value")) return ic.Str("value");
        if (content.ContainsKey("value")) return content.Str("value");
        return null;
    }

    private async Task<Result<Tweet>> GetMoreRepliesAsync(string tweetId, string cursor)
    {
        var response = await Gql.TweetDetailAsync(tweetId, cursor).ConfigureAwait(false);
        var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();

        var results = new List<Tweet>();
        foreach (var entry in entries.Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("cursor", StringComparison.Ordinal) || entryId.StartsWith("label", StringComparison.Ordinal)) continue;
            var tweet = Tweet.FromData(this, entry);
            if (tweet is not null) results.Add(tweet);
        }

        var nextCursor = TrailingCursor(entries);
        Func<Task<Result<Tweet>>>? fetchNext = nextCursor is not null ? () => GetMoreRepliesAsync(tweetId, nextCursor) : null;
        return new Result<Tweet>(results, fetchNext, nextCursor);
    }

    private async Task<Result<Tweet>> ShowMoreRepliesAsync(string tweetId, string cursor)
    {
        var response = await Gql.TweetDetailAsync(tweetId, cursor).ConfigureAwait(false);
        var items = response.Json.FirstDict("moduleItems") as JsonArray ?? new JsonArray();
        var results = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            if (!item.EntryId().Contains("tweet", StringComparison.Ordinal)) continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }
        return new Result<Tweet>(results);
    }

    /// <summary>
    /// ID を指定してツイートを取得します。返信（<see cref="Tweet.Replies"/>）、返信先（<see cref="Tweet.ReplyTo"/>）、
    /// スレッド（<see cref="Tweet.Thread"/>）も埋めます。
    /// </summary>
    /// <exception cref="TweetNotAvailableException">ツイートが存在しない、または見られない。</exception>
    public async Task<Tweet> GetTweetByIdAsync(string tweetId, string? cursor = null)
    {
        var response = await Gql.TweetDetailAsync(tweetId, cursor).ConfigureAwait(false);

        var errors = JsonExtensions.FatalErrors(response.Json, "entries");
        if (errors is not null)
            throw new TweetNotAvailableException(errors.ErrorMessage("The tweet is not available."));

        var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var replyTo = new List<Tweet>();
        var repliesList = new List<Tweet>();
        var relatedTweets = new List<Tweet>();
        Tweet? tweet = null;

        foreach (var entry in entries.Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("cursor", StringComparison.Ordinal)) continue;
            var tweetObject = Tweet.FromData(this, entry);
            if (tweetObject is null) continue;

            if (entryId.StartsWith("tweetdetailrelatedtweets", StringComparison.Ordinal))
            {
                relatedTweets.Add(tweetObject);
                continue;
            }

            if (entryId == $"tweet-{tweetId}")
            {
                tweet = tweetObject;
            }
            else if (tweet is null)
            {
                replyTo.Add(tweetObject);
            }
            else
            {
                var replies = new List<Tweet>();
                string? srCursor = null;
                Func<Task<Result<Tweet>>>? showReplies = null;

                var items = entry.Sub("content").ArrOrEmpty("items");
                foreach (var reply in items.Objects().Skip(1))
                {
                    var replyId = reply.EntryId();
                    if (replyId.Contains("tweetcomposer", StringComparison.Ordinal)) continue;
                    if (replyId.Contains("tweet", StringComparison.Ordinal))
                    {
                        var rpl = Tweet.FromData(this, reply);
                        if (rpl is null) continue;
                        replies.Add(rpl);
                    }
                    if (replyId.Contains("cursor", StringComparison.Ordinal))
                    {
                        srCursor = reply.Sub("item").Sub("itemContent").Str("value");
                        var c = srCursor;
                        if (c is not null) showReplies = () => ShowMoreRepliesAsync(tweetId, c);
                    }
                }
                tweetObject.Replies = new Result<Tweet>(replies, showReplies, srCursor);
                repliesList.Add(tweetObject);

                var displayType = entry.FindDict("tweetDisplayType", findOne: true);
                if (displayType.Count > 0 && displayType[0].AsStr() == "SelfThread")
                {
                    // `Thread` means the same thing on both builders: the author's
                    // chain, oldest first, the tweet itself included.
                    var thread = new List<Tweet> { tweet, tweetObject };
                    thread.AddRange(replies);
                    tweet.Thread = thread;
                }
            }
        }

        if (tweet is null)
        {
            // X answers a nonexistent or hidden id with a timeline that has no
            // tweet entry and no `errors`.
            throw new TweetNotAvailableException($"No tweet with id '{tweetId}' is available.");
        }

        var replyNextCursor = TrailingCursor(entries);
        Func<Task<Result<Tweet>>>? fetchMoreReplies = replyNextCursor is not null
            ? () => GetMoreRepliesAsync(tweetId, replyNextCursor)
            : null;

        tweet.Replies = new Result<Tweet>(repliesList, fetchMoreReplies, replyNextCursor);
        tweet.ReplyTo = replyTo;
        tweet.RelatedTweets = relatedTweets;

        return tweet;
    }

    /// <summary>
    /// ユーザーに言及しているツイートを取得します。<see cref="GetNotificationsAsync"/> と違い、
    /// 検索なのでログイン中でない任意のアカウントについて使えます。
    /// </summary>
    /// <param name="screenName">対象のスクリーンネーム（先頭の @ は有っても無くても可）。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public Task<Result<Tweet>> GetUserMentionsAsync(string screenName, int count = 20, string? cursor = null)
    {
        var handle = screenName.TrimStart('@');
        // This searches for the literal text "@handle", so a user id produces
        // a query that matches nothing and looks like "nobody mentioned them".
        if (handle.Length > 0 && handle.All(char.IsDigit))
        {
            throw new ArgumentException(
                $"`screenName` must be a handle, not a user id: '{handle}'. Resolve it first with GetUserByIdAsync(...).ScreenName.",
                nameof(screenName));
        }
        return SearchTweetAsync($"@{handle}", "Latest", count, cursor);
    }

    /// <summary>
    /// 期間を指定してツイートを検索します（<see cref="SearchTweetAsync"/> と <see cref="SearchQuery.Build"/> の薄いラッパー）。
    /// </summary>
    /// <param name="query">検索テキスト。</param>
    /// <param name="since">開始日（YYYY-MM-DD、この日を含む）。</param>
    /// <param name="until">終了日（YYYY-MM-DD、この日を含まない）。</param>
    /// <param name="product">'Top'、'Latest'、'Media'。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public Task<Result<Tweet>> SearchTweetsByDateAsync(string query, string since, string until, string product = "Latest",
        int count = 20, string? cursor = null)
        => SearchTweetAsync(SearchQuery.Build(query, new SearchOptions { Since = since, Until = until }), product, count, cursor);

    /// <summary>
    /// スレッド内の任意のツイートからスレッド全体を取得します。返信先・ツイート自身・続きを繋ぎ、
    /// 他人の返信を除いた投稿者の連鎖を時系列順に返します。
    /// </summary>
    public async Task<List<Tweet>> GetThreadAsync(string tweetId)
    {
        var tweet = await GetTweetByIdAsync(tweetId).ConfigureAwait(false);
        var authorId = tweet.User?.Id;

        var chain = new List<Tweet>();
        var seen = new HashSet<string>();
        foreach (var part in new[] { tweet.ReplyTo ?? new List<Tweet>(), new List<Tweet> { tweet }, tweet.Thread ?? new List<Tweet>() })
        {
            foreach (var item in part)
            {
                if (seen.Contains(item.Id)) continue;
                var itemAuthor = item.User?.Id;
                if (authorId is not null && itemAuthor != authorId) continue;
                seen.Add(item.Id);
                chain.Add(item);
            }
        }
        return chain;
    }

    /// <summary>
    /// URL を指定してツイートを取得します。クエリ文字列、<c>/photo/1</c> のような末尾パス、
    /// twitter.com / fxtwitter.com 形式のホストも受け付けます。
    /// </summary>
    public Task<Tweet> GetTweetByUrlAsync(string url)
    {
        var match = Regex.Match(url, @"/status(?:es)?/(\d+)");
        if (!match.Success) throw new ArgumentException($"Not a tweet URL: '{url}'", nameof(url));
        return GetTweetByIdAsync(match.Groups[1].Value);
    }

    /// <summary>複数の ID を指定してツイートを取得します。</summary>
    public async Task<List<Tweet>> GetTweetsByIdsAsync(IEnumerable<string> ids)
    {
        var response = await Gql.TweetResultsByRestIdsAsync(ids).ConfigureAwait(false);
        var tweetResults = response.Json.Get("data").ArrOrEmpty("tweetResult");
        var results = new List<Tweet>();
        foreach (var tweetResult in tweetResults)
        {
            var tweet = Tweet.FromData(this, tweetResult);
            if (tweet is not null) results.Add(tweet);
        }
        return results;
    }

    /// <summary>予約投稿の一覧を取得します。</summary>
    public async Task<List<ScheduledTweet>> GetScheduledTweetsAsync()
    {
        var response = await Gql.FetchScheduledTweetsAsync().ConfigureAwait(false);
        var tweets = response.Json.FirstDict("scheduled_tweet_list") as JsonArray ?? new JsonArray();
        return tweets.Objects().Select(t => new ScheduledTweet(this, t)).ToList();
    }

    /// <summary>予約投稿を削除します。</summary>
    public async Task<HttpResponseMessage> DeleteScheduledTweetAsync(string tweetId)
        => (await Gql.DeleteScheduledTweetAsync(tweetId).ConfigureAwait(false)).Http;

    private async Task<Result<User>> GetTweetEngagementsAsync(string tweetId, int count, string? cursor,
        Func<string, int, string?, Task<ApiResponse>> f)
    {
        var response = await f(tweetId, count, cursor).ConfigureAwait(false);
        if (response.Json.FirstDict("entries") is not JsonArray items) return Result<User>.Empty();
        var nextCursor = JsonExtensions.LastCursor(items);
        var previousCursor = JsonExtensions.CursorAt(items, -2);

        var results = new List<User>();
        foreach (var item in items.Objects())
        {
            if (!item.EntryId().StartsWith("user", StringComparison.Ordinal)) continue;
            if (item.FirstDict("result") is not JsonObject userInfo) continue;
            results.Add(new User(this, userInfo));
        }

        return Page(results, count, nextCursor,
            () => GetTweetEngagementsAsync(tweetId, count, nextCursor, f),
            previousCursor,
            () => GetTweetEngagementsAsync(tweetId, count, previousCursor, f));
    }

    /// <summary>ツイートをリツイートしたユーザーを取得します。</summary>
    public Task<Result<User>> GetRetweetersAsync(string tweetId, int count = 40, string? cursor = null)
        => GetTweetEngagementsAsync(tweetId, count, cursor, Gql.RetweetersAsync);

    /// <summary>ツイートをいいねしたユーザーを取得します。</summary>
    public Task<Result<User>> GetFavoritersAsync(string tweetId, int count = 40, string? cursor = null)
        => GetTweetEngagementsAsync(tweetId, count, cursor, Gql.FavoritersAsync);

    /// <summary>ID を指定してコミュニティノートを取得します。</summary>
    /// <exception cref="NotFoundException">ノートが存在しない。</exception>
    /// <exception cref="TwitterException">ノート ID が不正。</exception>
    public async Task<CommunityNote> GetCommunityNoteAsync(string noteId)
    {
        var response = await Gql.BirdWatchOneNoteAsync(noteId).ConfigureAwait(false);
        var noteData = response.Json.Get("data").Get("birdwatch_note_by_rest_id");
        if (noteData is not JsonObject note) throw new NotFoundException($"No community note with id '{noteId}'.");
        if (!note.ContainsKey("data_v1")) throw new TwitterException($"Invalid note id: {noteId}");
        return new CommunityNote(this, note);
    }

    /// <summary>
    /// ユーザーのタイムラインからツイートを取得します。
    /// </summary>
    /// <param name="userId">ユーザー ID（<see cref="GetUserByScreenNameAsync"/> で取得できます）。</param>
    /// <param name="tweetType">'Tweets'、'Replies'、'Media'、'Likes' のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    /// <exception cref="UserUnavailableException">鍵・凍結・削除済みのアカウント。</exception>
    public async Task<Result<Tweet>> GetUserTweetsAsync(string userId, string tweetType, int count = 40, string? cursor = null)
    {
        tweetType = Capitalize(tweetType);
        Func<string, int, string?, Task<ApiResponse>> f = tweetType switch
        {
            "Tweets" => Gql.UserTweetsAsync,
            "Replies" => Gql.UserTweetsAndRepliesAsync,
            "Media" => Gql.UserMediaAsync,
            "Likes" => Gql.UserLikesAsync,
            _ => throw new ArgumentException($"Invalid tweetType '{tweetType}'; expected one of Tweets, Replies, Media, Likes.", nameof(tweetType)),
        };
        var response = await f(userId, count, cursor).ConfigureAwait(false);

        // A protected (or suspended/deactivated) account answers with a bare
        // UserUnavailable result and no timeline at all.
        var userResult = response.Json.Get("data").Sub("user").Sub("result");
        if (userResult.Str("__typename") == "UserUnavailable")
            throw new UserUnavailableException(userResult.Str("message") ?? "The account is protected, suspended or deactivated.");

        var instructionsFound = response.Json.FindDict("instructions", findOne: true);
        if (instructionsFound.Count == 0 || instructionsFound[0] is not JsonArray instructions)
            return Result<Tweet>.Empty();

        // Accounts with no visible tweets return a timeline with no entries or
        // cursor entries; derive cursors only when present, else return empty.
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();

        static string? Cursor(JsonArray entries, string kind)
        {
            foreach (var entry in entries.Objects())
                if (entry.EntryId().StartsWith($"cursor-{kind}", StringComparison.Ordinal))
                    return entry.Sub("content").Str("value");
            return null;
        }

        var nextCursor = Cursor(items, "bottom");
        var previousCursor = Cursor(items, "top");

        if (tweetType == "Media")
        {
            if (cursor is null)
            {
                var module = items.Objects().FirstOrDefault(e => e.Sub("content").ContainsKey("items"));
                items = module is not null ? module.Sub("content").ArrOrEmpty("items") : new JsonArray();
            }
            else
            {
                // TimelineAddToModule is rarely the first instruction, so indexing
                // [0] returned nothing and Media paginated to an empty second page.
                items = instructions.FirstDict("moduleItems") as JsonArray ?? new JsonArray();
            }
        }

        var results = new List<Tweet>();

        // A pinned tweet arrives in its own TimelinePinEntry instruction rather
        // than among the entries. It belongs at the top, and only on the first page.
        var pinnedIds = new HashSet<string>();
        if (tweetType == "Tweets" && cursor is null)
        {
            foreach (var instruction in instructions.Objects())
            {
                if (instruction.Str("type") != "TimelinePinEntry") continue;
                var pinned = instruction.Sub("entry").Sub("content");
                var pinnedTweet = Tweet.FromData(this, pinned.Sub("itemContent"));
                if (pinnedTweet is not null)
                {
                    pinnedIds.Add(pinnedTweet.Id);
                    results.Add(pinnedTweet);
                }
            }
        }

        foreach (var entry in items.Objects())
        {
            var entryId = entry.EntryId();
            if (!entryId.StartsWith("tweet", StringComparison.Ordinal)
                && !entryId.StartsWith("profile-conversation", StringComparison.Ordinal)
                && !entryId.StartsWith("profile-grid", StringComparison.Ordinal))
                continue;

            // `item` gets reassigned to one of the module's children below,
            // so the module metadata has to be read off it first.
            var conversationIds = ConversationIds(entry.Get("content"));
            JsonNode item = entry;
            List<Tweet>? replies;

            if (entryId.StartsWith("profile-conversation", StringComparison.Ordinal))
            {
                var tweets = entry.Sub("content").ArrOrEmpty("items").Objects().ToList();
                if (tweetType == "Replies")
                {
                    // On the Replies tab a conversation reads [what was replied to,
                    // the user's reply]. Emit the user's own entries and keep the
                    // rest as context.
                    var own = tweets.Where(t => ConversationAuthorId(t) == userId).ToList();
                    var context = tweets.Where(t => ConversationAuthorId(t) != userId).ToList();
                    if (own.Count > 0)
                    {
                        replies = new List<Tweet>();
                        foreach (var other in context)
                        {
                            var tweetObject = Tweet.FromData(this, other);
                            if (tweetObject is not null) replies.Add(tweetObject);
                        }
                        // Taking own[-1] threw away every earlier reply the author
                        // made in the same conversation. Emit them all.
                        foreach (var other in own.Take(own.Count - 1))
                        {
                            var tweetObject = Tweet.FromData(this, other);
                            if (tweetObject is null) continue;
                            tweetObject.Replies = new Result<Tweet>(replies);
                            tweetObject.ConversationIds = conversationIds;
                            if (pinnedIds.Contains(tweetObject.Id)) continue;
                            results.Add(tweetObject);
                        }
                        item = own[^1];
                    }
                    else
                    {
                        replies = null;
                        item = tweets.Count > 0 ? tweets[0] : entry;
                    }
                }
                else
                {
                    replies = new List<Tweet>();
                    foreach (var reply in tweets.Skip(1))
                    {
                        var tweetObject = Tweet.FromData(this, reply);
                        if (tweetObject is not null) replies.Add(tweetObject);
                    }
                    item = tweets.Count > 0 ? tweets[0] : entry;
                }
            }
            else
            {
                replies = null;
            }

            var tweet = Tweet.FromData(this, item);
            if (tweet is null) continue;
            tweet.Replies = replies is null ? null : new Result<Tweet>(replies);
            tweet.ConversationIds = conversationIds;
            if (replies is { Count: > 0 } && replies.All(r => r.User is not null && tweet.User is not null && r.User.Id == tweet.User.Id))
            {
                // Only a module where every entry is the same author is a thread.
                var thread = new List<Tweet> { tweet };
                thread.AddRange(replies);
                tweet.Thread = thread;
            }
            if (pinnedIds.Contains(tweet.Id))
            {
                // X usually keeps the pinned tweet out of the entries, but not
                // when it heads a conversation module.
                continue;
            }
            results.Add(tweet);
        }

        var type = tweetType;
        return Page(results, count, nextCursor,
            () => GetUserTweetsAsync(userId, type, count, nextCursor),
            previousCursor,
            () => GetUserTweetsAsync(userId, type, count, previousCursor));
    }

    /// <summary>
    /// ホームタイムライン（For You）を取得します。
    /// </summary>
    /// <param name="count">取得する件数。</param>
    /// <param name="seenTweetIds">既読のツイート ID。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Tweet>> GetTimelineAsync(int count = 20, IList<string>? seenTweetIds = null, string? cursor = null)
    {
        var response = await Gql.HomeTimelineAsync(count, seenTweetIds, cursor).ConfigureAwait(false);
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var nextCursor = JsonExtensions.LastCursor(items);
        var results = new List<Tweet>();

        foreach (var item in items.Objects())
        {
            if (!item.Sub("content").ContainsKey("itemContent")) continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }

        // X ignores `count` on the home timeline too.
        return Page(results, count, nextCursor, () => GetTimelineAsync(count, seenTweetIds, nextCursor));
    }

    /// <summary>
    /// ホームタイムライン（Following）を取得します。
    /// </summary>
    /// <param name="count">取得する件数。</param>
    /// <param name="seenTweetIds">既読のツイート ID。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Tweet>> GetLatestTimelineAsync(int count = 20, IList<string>? seenTweetIds = null, string? cursor = null)
    {
        var response = await Gql.HomeLatestTimelineAsync(count, seenTweetIds, cursor).ConfigureAwait(false);
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var nextCursor = JsonExtensions.LastCursor(items);
        var results = new List<Tweet>();

        void HandleItem(JsonNode item, List<string>? conversationIds = null)
        {
            var tweet = Tweet.FromData(this, item);
            if (tweet is null) return;
            tweet.ConversationIds = conversationIds;
            results.Add(tweet);
        }

        foreach (var item in items.Objects())
        {
            var content = item.Sub("content");
            if (content.ContainsKey("items"))
            {
                // home-conversation entries
                var conversationIds = ConversationIds(content);
                foreach (var subItem in content.ArrOrEmpty("items").Objects())
                {
                    if (!subItem.Sub("item").ContainsKey("itemContent")) continue;
                    HandleItem(subItem, conversationIds);
                }
            }
            else
            {
                if (!content.ContainsKey("itemContent")) continue;
                HandleItem(item);
            }
        }

        return Page(results, count, nextCursor, () => GetLatestTimelineAsync(count, seenTweetIds, nextCursor));
    }

    /// <summary>ツイートをいいねします。</summary>
    public async Task<HttpResponseMessage> FavoriteTweetAsync(string tweetId)
        => (await Gql.FavoriteTweetAsync(tweetId).ConfigureAwait(false)).Http;

    /// <summary>ツイートのいいねを解除します。</summary>
    public async Task<HttpResponseMessage> UnfavoriteTweetAsync(string tweetId)
        => (await Gql.UnfavoriteTweetAsync(tweetId).ConfigureAwait(false)).Http;

    /// <summary>
    /// ツイートをリツイートします。取り消すときは <see cref="DeleteRetweetAsync"/> に<b>元ツイート</b>の ID を渡してください。
    /// </summary>
    public async Task<HttpResponseMessage> RetweetAsync(string tweetId)
        => (await Gql.RetweetAsync(tweetId).ConfigureAwait(false)).Http;

    /// <summary>
    /// リツイートを取り消します。
    /// </summary>
    /// <param name="tweetId">リツイートされた<b>元ツイート</b>の ID（リツイート自身の ID ではありません）。</param>
    /// <remarks>
    /// X は何も取り消されなくても 200 を返し、本文は渡した ID を返すだけです。タイムラインから得た
    /// リツイートの ID を渡すと黙って何も起きません。<see cref="Tweet.RetweetedTweet"/> の ID を使ってください。
    /// </remarks>
    public async Task<HttpResponseMessage> DeleteRetweetAsync(string tweetId)
        => (await Gql.DeleteRetweetAsync(tweetId).ConfigureAwait(false)).Http;

    /// <summary>ツイートをブックマークに追加します。</summary>
    /// <param name="tweetId">ツイートの ID。</param>
    /// <param name="folderId">追加先フォルダーの ID（任意）。</param>
    public async Task<HttpResponseMessage> BookmarkTweetAsync(string tweetId, string? folderId = null)
    {
        var response = folderId is null
            ? await Gql.CreateBookmarkAsync(tweetId).ConfigureAwait(false)
            : await Gql.BookmarkTweetToFolderAsync(tweetId, folderId).ConfigureAwait(false);
        return response.Http;
    }

    /// <summary>ツイートをブックマークから削除します。</summary>
    public async Task<HttpResponseMessage> DeleteBookmarkAsync(string tweetId)
        => (await Gql.DeleteBookmarkAsync(tweetId).ConfigureAwait(false)).Http;

    /// <summary>
    /// ブックマークを取得します。
    /// </summary>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    /// <param name="folderId">フォルダーの ID（任意）。</param>
    public async Task<Result<Tweet>> GetBookmarksAsync(int count = 20, string? cursor = null, string? folderId = null)
    {
        var response = folderId is null
            ? await Gql.BookmarksAsync(count, cursor).ConfigureAwait(false)
            : await Gql.BookmarkFolderTimelineAsync(count, cursor, folderId).ConfigureAwait(false);

        if (response.Json.FirstDict("entries") is not JsonArray items) return Result<Tweet>.Empty();
        var nextCursor = JsonExtensions.LastCursor(items);
        string? previousCursor = null;
        Func<Task<Result<Tweet>>>? fetchPrevious = null;
        if (folderId is null)
        {
            previousCursor = JsonExtensions.CursorAt(items, -2);
            fetchPrevious = () => GetBookmarksAsync(count, previousCursor, folderId);
        }

        var results = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }

        return Page(results, count, nextCursor, () => GetBookmarksAsync(count, nextCursor, folderId), previousCursor, fetchPrevious);
    }

    /// <summary>すべてのブックマークを削除します。</summary>
    public async Task<HttpResponseMessage> DeleteAllBookmarksAsync()
        => (await Gql.DeleteAllBookmarksAsync().ConfigureAwait(false)).Http;

    /// <summary>ブックマークフォルダーの一覧を取得します。</summary>
    public async Task<Result<BookmarkFolder>> GetBookmarkFoldersAsync(string? cursor = null)
    {
        var response = await Gql.BookmarkFoldersSliceAsync(cursor).ConfigureAwait(false);

        var errors = JsonExtensions.FatalErrors(response.Json, "bookmark_collections_slice");
        if (errors is not null)
            throw new TwitterException(errors.ErrorMessage("Failed to retrieve bookmark folders."));

        if (response.Json.FirstDict("bookmark_collections_slice") is not JsonObject slice)
            return Result<BookmarkFolder>.Empty();
        var results = slice.ArrOrEmpty("items").Objects().Select(item => new BookmarkFolder(this, item)).ToList();

        // X omits the bottom cursor on the last page.
        var sliceInfo = slice.Sub("slice_info");
        string? nextCursor = sliceInfo.ContainsKey("next_cursor") ? sliceInfo.Str("next_cursor") : null;
        Func<Task<Result<BookmarkFolder>>>? fetchNext = nextCursor is not null ? () => GetBookmarkFoldersAsync(nextCursor) : null;
        return new Result<BookmarkFolder>(results, fetchNext, nextCursor);
    }

    /// <summary>ブックマークフォルダーの名前を変更します。</summary>
    public async Task<BookmarkFolder> EditBookmarkFolderAsync(string folderId, string name)
    {
        var response = await Gql.EditBookmarkFolderAsync(folderId, name).ConfigureAwait(false);
        var folder = response.Json.Get("data").Get("bookmark_collection_update") as JsonObject
                     ?? throw new TwitterException("X returned no folder: " + response.Text);
        return new BookmarkFolder(this, folder);
    }

    /// <summary>ブックマークフォルダーを削除します。</summary>
    public async Task<HttpResponseMessage> DeleteBookmarkFolderAsync(string folderId)
        => (await Gql.DeleteBookmarkFolderAsync(folderId).ConfigureAwait(false)).Http;

    /// <summary>ブックマークフォルダーを作成します（Premium のみ）。</summary>
    public async Task<BookmarkFolder> CreateBookmarkFolderAsync(string name)
    {
        var response = await Gql.CreateBookmarkFolderAsync(name).ConfigureAwait(false);
        var errors = JsonExtensions.FatalErrors(response.Json, "bookmark_collection_create");
        if (errors is not null)
        {
            // Bookmark collections are Premium-only; X answers code 37,
            // "User is not authorized to use bookmark collections".
            ErrorCodes.RaiseExceptionsFromResponse(errors);
            throw new TwitterException(errors[0].Str("message") ?? errors[0].ToJsonString());
        }
        var folder = response.Json.Get("data").Get("bookmark_collection_create") as JsonObject
                     ?? throw new TwitterException("X returned no folder for the new bookmark collection.");
        return new BookmarkFolder(this, folder);
    }
}
