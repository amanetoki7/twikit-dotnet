using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Twikit.Api;
using Twikit.Streaming;

namespace Twikit;

public partial class Client
{
    /// <summary>コミュニティを検索します。</summary>
    public async Task<Result<Community>> SearchCommunityAsync(string query, string? cursor = null)
    {
        var response = await Gql.SearchCommunityAsync(query, cursor).ConfigureAwait(false);

        var items = response.Json.FirstDict("items_results") as JsonArray ?? new JsonArray();
        var communities = new List<Community>();
        foreach (var item in items.Objects())
        {
            if (item.Get("result") is not JsonObject result) continue;
            try
            {
                communities.Add(new Community(this, result));
            }
            catch (NotFoundException)
            {
                // skip unresolved entries
            }
        }
        var nextCursor = response.Json.FirstDict("next_cursor").AsStr();
        Func<Task<Result<Community>>>? fetchNext = nextCursor is not null ? () => SearchCommunityAsync(query, nextCursor) : null;
        return new Result<Community>(communities, fetchNext, nextCursor);
    }

    /// <summary>ID を指定してコミュニティを取得します。</summary>
    /// <exception cref="NotFoundException">コミュニティが存在しない。</exception>
    public async Task<Community> GetCommunityAsync(string communityId)
    {
        var response = await Gql.CommunityQueryAsync(communityId).ConfigureAwait(false);
        if (response.Json.FirstDict("result") is not JsonObject communityData) throw new NotFoundException("The community does not exist.");
        return new Community(this, communityData);
    }

    /// <summary>
    /// コミュニティのツイートを取得します。
    /// </summary>
    /// <param name="communityId">コミュニティの ID。</param>
    /// <param name="tweetType">'Top'、'Latest'、'Media' のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Tweet>> GetCommunityTweetsAsync(string communityId, string tweetType, int count = 40, string? cursor = null)
    {
        var response = tweetType switch
        {
            "Media" => await Gql.CommunityMediaTimelineAsync(communityId, count, cursor).ConfigureAwait(false),
            "Top" => await Gql.CommunityTweetsTimelineAsync(communityId, "Relevance", count, cursor).ConfigureAwait(false),
            "Latest" => await Gql.CommunityTweetsTimelineAsync(communityId, "Recency", count, cursor).ConfigureAwait(false),
            _ => throw new ArgumentException($"Invalid tweetType: {tweetType}", nameof(tweetType)),
        };

        var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        JsonArray items;
        string? nextCursor;
        string? previousCursor;
        if (tweetType == "Media")
        {
            items = cursor is null
                ? (entries.Count > 0 ? entries[0].Sub("content").ArrOrEmpty("items") : new JsonArray())
                : response.Json.FirstDict("moduleItems") as JsonArray ?? new JsonArray();
            nextCursor = JsonExtensions.LastCursor(entries);
            previousCursor = JsonExtensions.CursorAt(entries, -2);
        }
        else
        {
            items = entries;
            nextCursor = JsonExtensions.LastCursor(items);
            previousCursor = JsonExtensions.CursorAt(items, -2);
        }

        var tweets = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (!entryId.StartsWith("tweet", StringComparison.Ordinal) && !entryId.StartsWith("communities-grid", StringComparison.Ordinal)) continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) tweets.Add(tweet);
        }

        return Page(tweets, count, nextCursor,
            () => GetCommunityTweetsAsync(communityId, tweetType, count, nextCursor),
            previousCursor,
            () => GetCommunityTweetsAsync(communityId, tweetType, count, previousCursor));
    }

    /// <summary>コミュニティタイムラインのツイートを取得します。</summary>
    public async Task<Result<Tweet>> GetCommunitiesTimelineAsync(int count = 20, string? cursor = null)
    {
        var response = await Gql.CommunitiesMainPageTimelineAsync(count, cursor).ConfigureAwait(false);
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var tweets = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            if (!item.EntryId().StartsWith("tweet", StringComparison.Ordinal)) continue;
            if (item.FirstDict("result") is not JsonObject tweetData) continue;
            if (tweetData.Get("tweet") is JsonObject inner) tweetData = inner;
            // A promoted entry carries the advertiser's User object, whose
            // `core` has no user_results - and no community either.
            var userData = tweetData.Sub("core").Sub("user_results").Obj("result");
            var communityData = tweetData.Sub("community_results").Obj("result");
            if (userData is null || communityData is null) continue;
            communityData["rest_id"] = communityData.Str("id_str");
            var community = new Community(this, communityData);
            var tweet = new Tweet(this, tweetData, new User(this, userData)) { Community = community };
            tweets.Add(tweet);
        }

        var nextCursor = JsonExtensions.LastCursor(items);
        var previousCursor = JsonExtensions.CursorAt(items, -2);

        return Page(tweets, count, nextCursor,
            () => GetCommunitiesTimelineAsync(count, nextCursor),
            previousCursor,
            () => GetCommunitiesTimelineAsync(count, previousCursor));
    }

    /// <summary>コミュニティに参加します。</summary>
    public async Task<Community> JoinCommunityAsync(string communityId)
    {
        var response = await Gql.JoinCommunityAsync(communityId).ConfigureAwait(false);
        var communityData = response.Json.Get("data").Obj("community_join") ?? throw new NotFoundException("The community does not exist.");
        communityData["rest_id"] = communityData.Str("id_str");
        return new Community(this, communityData);
    }

    /// <summary>コミュニティから退出します。</summary>
    public async Task<Community> LeaveCommunityAsync(string communityId)
    {
        var response = await Gql.LeaveCommunityAsync(communityId).ConfigureAwait(false);
        var communityData = response.Json.Get("data").Obj("community_leave") ?? throw new NotFoundException("The community does not exist.");
        communityData["rest_id"] = communityData.Str("id_str");
        return new Community(this, communityData);
    }

    /// <summary>コミュニティへの参加をリクエストします。</summary>
    /// <param name="communityId">コミュニティの ID。</param>
    /// <param name="answer">参加質問への回答。</param>
    public async Task<Community> RequestToJoinCommunityAsync(string communityId, string? answer = null)
    {
        var response = await Gql.RequestToJoinCommunityAsync(communityId, answer).ConfigureAwait(false);
        if (response.Json.FirstDict("result") is not JsonObject communityData) throw new NotFoundException("The community does not exist.");
        communityData["rest_id"] = communityData.Str("id_str");
        return new Community(this, communityData);
    }

    private async Task<Result<CommunityMember>> GetCommunityUsersAsync(Func<string, int, string?, Task<ApiResponse>> f,
        string communityId, int count, string? cursor)
    {
        var response = await f(communityId, count, cursor).ConfigureAwait(false);

        var items = response.Json.FirstDict("items_results") as JsonArray ?? new JsonArray();
        var users = new List<CommunityMember>();
        foreach (var item in items.Objects())
        {
            if (item.Get("result") is not JsonObject result) continue;
            if (result.Str("__typename") != "User") continue;
            try
            {
                users.Add(new CommunityMember(this, result));
            }
            catch (NotFoundException)
            {
                // skip unresolved members
            }
        }

        var nextCursor = response.Json.FirstDict("next_cursor").AsStr();
        var (page, overflow) = Paging.Limited(users, count);
        Func<Task<Result<CommunityMember>>>? fetchNext = nextCursor is not null
            ? () => GetCommunityUsersAsync(f, communityId, count, nextCursor)
            : null;
        return new Result<CommunityMember>(page, fetchNext, nextCursor, null, null, overflow, count);
    }

    /// <summary>コミュニティのメンバーを取得します。</summary>
    public Task<Result<CommunityMember>> GetCommunityMembersAsync(string communityId, int count = 20, string? cursor = null)
        => GetCommunityUsersAsync(Gql.MembersSliceTimelineQueryAsync, communityId, count, cursor);

    /// <summary>コミュニティのモデレーターを取得します。</summary>
    public Task<Result<CommunityMember>> GetCommunityModeratorsAsync(string communityId, int count = 20, string? cursor = null)
        => GetCommunityUsersAsync(Gql.ModeratorsSliceTimelineQueryAsync, communityId, count, cursor);

    /// <summary>コミュニティ内のツイートを検索します。</summary>
    public async Task<Result<Tweet>> SearchCommunityTweetAsync(string communityId, string query, int count = 20, string? cursor = null)
    {
        var response = await Gql.CommunityTweetSearchModuleQueryAsync(communityId, query, count, cursor).ConfigureAwait(false);

        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var tweets = new List<Tweet>();
        foreach (var item in items.Objects())
        {
            if (!item.EntryId().StartsWith("tweet", StringComparison.Ordinal)) continue;
            var tweet = Tweet.FromData(this, item);
            if (tweet is not null) tweets.Add(tweet);
        }

        var nextCursor = JsonExtensions.LastCursor(items);
        var previousCursor = JsonExtensions.CursorAt(items, -2);

        return Page(tweets, count, nextCursor,
            () => SearchCommunityTweetAsync(communityId, query, count, nextCursor),
            previousCursor,
            () => SearchCommunityTweetAsync(communityId, query, count, previousCursor));
    }

    // ------------------------------------------------------------ streaming

    /// <summary>
    /// live_pipeline のイベントストリームを開きます（<see cref="GetStreamingSessionAsync"/> が使います）。
    /// </summary>
    /// <remarks>
    /// The streaming connection is the one request that does not carry an
    /// X-Client-Transaction-Id: it goes out raw, exactly as in the Python original.
    /// </remarks>
    public async IAsyncEnumerable<(string? Topic, Payload Payload)> StreamAsync(IEnumerable<string> topics,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var url = $"https://api.{Constants.Domain}/live_pipeline/events";
        var parameters = new Dictionary<string, string> { ["topics"] = string.Join(",", topics) };
        var headers = BaseHeaders;
        headers.Remove("content-type");

        using var response = await SendCoreAsync(HttpMethod.Get, url, new RequestOptions
        {
            Params = parameters,
            Headers = headers,
            Timeout = Timeout.InfiniteTimeSpan,
        }, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        RemoveDuplicateCt0Cookie();
        if ((int)response.StatusCode >= 400)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new TwitterException($"status: {(int)response.StatusCode}, message: \"{body}\"", TwitterException.HeadersOf(response));
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            var data = JsonExtensions.TryParse(line);
            if (data is not JsonObject obj) continue;
            var payload = Payload.FromData(obj.Get("payload"));
            yield return (obj.Str("topic"), payload);
        }
    }

    /// <summary>
    /// ストリーミング API のセッションを取得します。
    /// </summary>
    /// <param name="topics">ストリームするトピック（<see cref="Topic"/> で生成）。</param>
    /// <param name="autoReconnect">切断時に自動で再接続するか。</param>
    /// <example>
    /// <code>
    /// var topics = new HashSet&lt;string&gt;
    /// {
    ///     Topic.TweetEngagement("1739617652"),
    ///     Topic.DmUpdate("17544932482-174455537996"),
    /// };
    /// var session = await client.GetStreamingSessionAsync(topics);
    /// await foreach (var (topic, payload) in session)
    /// {
    ///     if (payload.DmUpdate is { } dm) Console.WriteLine($"{dm.ConversationId}: {dm.UserId} sent a message");
    ///     if (payload.TweetEngagement is { } e) Console.WriteLine($"likes: {e.LikeCount}");
    /// }
    /// </code>
    /// </example>
    public async Task<StreamingSession> GetStreamingSessionAsync(IEnumerable<string> topics, bool autoReconnect = true)
    {
        var topicSet = new HashSet<string>(topics);
        var stream = StreamAsync(topicSet).GetAsyncEnumerator();
        if (!await stream.MoveNextAsync().ConfigureAwait(false))
            throw new TwitterException("The streaming connection closed before sending a config event.");
        var sessionId = stream.Current.Payload.Config?.SessionId
                        ?? throw new TwitterException("The first streaming event carried no session id.");
        return new StreamingSession(this, sessionId, stream, topicSet, autoReconnect);
    }

    internal async Task<Payload> UpdateSubscriptionsAsync(StreamingSession session, ISet<string>? subscribe, ISet<string>? unsubscribe)
    {
        subscribe ??= new HashSet<string>();
        unsubscribe ??= new HashSet<string>();

        var response = await V11.LivePipelineUpdateSubscriptionsAsync(
            session.Id, string.Join(",", subscribe), string.Join(",", unsubscribe)).ConfigureAwait(false);
        session.Topics.UnionWith(subscribe);
        session.Topics.ExceptWith(unsubscribe);

        return Payload.FromData(response.Json);
    }
}
