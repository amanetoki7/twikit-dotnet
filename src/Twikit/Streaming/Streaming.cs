using System.Text.Json.Nodes;

namespace Twikit.Streaming;

/// <summary>
/// ストリーミングセッション。<c>await foreach</c> で <c>(Topic, Payload)</c> を受け取ります。
/// </summary>
/// <example>
/// <code>
/// var topics = new HashSet&lt;string&gt; { Topic.DmUpdate($"{myId}-{userId}") };
/// var session = await client.GetStreamingSessionAsync(topics);
/// await foreach (var (topic, payload) in session)
/// {
///     if (payload.DmUpdate is { } dm) Console.WriteLine($"{dm.ConversationId}: {dm.UserId} sent a message");
/// }
/// </code>
/// </example>
public sealed class StreamingSession : IAsyncEnumerable<(string? Topic, Payload Payload)>
{
    private readonly Client _client;
    private IAsyncEnumerator<(string? Topic, Payload Payload)> _stream;

    /// <summary>セッションの ID。</summary>
    public string Id { get; private set; }

    /// <summary>ストリーム中のトピック。</summary>
    public HashSet<string> Topics { get; }

    /// <summary>切断時に自動で再接続するか。</summary>
    public bool AutoReconnect { get; set; }

    public StreamingSession(Client client, string sessionId, IAsyncEnumerator<(string? Topic, Payload Payload)> stream,
        IEnumerable<string> topics, bool autoReconnect)
    {
        _client = client;
        Id = sessionId;
        _stream = stream;
        Topics = new HashSet<string>(topics);
        AutoReconnect = autoReconnect;
    }

    /// <summary>セッションを再接続します。最初に受け取った config イベントを返します。</summary>
    public async Task<(string? Topic, Payload Payload)> ReconnectAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        var stream = _client.StreamAsync(Topics).GetAsyncEnumerator();
        if (!await stream.MoveNextAsync().ConfigureAwait(false))
            throw new TwitterException("The streaming connection closed before sending a config event.");
        var configEvent = stream.Current;
        Id = configEvent.Payload.Config?.SessionId ?? Id;
        _stream = stream;
        return configEvent;
    }

    /// <summary>
    /// トピックの購読を更新します。
    /// </summary>
    /// <param name="subscribe">購読を開始するトピック。</param>
    /// <param name="unsubscribe">購読を解除するトピック。</param>
    public Task<Payload> UpdateSubscriptionsAsync(ISet<string>? subscribe = null, ISet<string>? unsubscribe = null)
        => _client.UpdateSubscriptionsAsync(this, subscribe, unsubscribe);

    public async IAsyncEnumerator<(string? Topic, Payload Payload)> GetAsyncEnumerator(
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            while (await _stream.MoveNextAsync().ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return _stream.Current;
            }
            if (!AutoReconnect) break;
            yield return await ReconnectAsync().ConfigureAwait(false);
        }
    }

    public override string ToString() => $"<StreamingSession id=\"{Id}\">";
}

/// <summary>数種類のイベントをまとめたペイロード。</summary>
public sealed class Payload
{
    /// <summary>設定イベント。</summary>
    public ConfigEvent? Config { get; init; }
    /// <summary>購読イベント。</summary>
    public SubscriptionsEvent? Subscriptions { get; init; }
    /// <summary>ツイートのエンゲージメントイベント。</summary>
    public TweetEngagementEvent? TweetEngagement { get; init; }
    /// <summary>DM の更新イベント。</summary>
    public DmUpdateEvent? DmUpdate { get; init; }
    /// <summary>DM の入力中イベント。</summary>
    public DmTypingEvent? DmTyping { get; init; }

    /// <summary>ストリームの 1 行（<c>payload</c> オブジェクト）から作ります。</summary>
    public static Payload FromData(JsonNode? data)
    {
        var payload = new Payload
        {
            Config = data.Get("config") is JsonObject config ? ConfigEvent.FromData(config) : null,
            Subscriptions = data.Get("subscriptions") is JsonObject subs ? SubscriptionsEvent.FromData(subs) : null,
            TweetEngagement = data.Get("tweet_engagement") is JsonObject te ? TweetEngagementEvent.FromData(te) : null,
            DmUpdate = data.Get("dm_update") is JsonObject du ? DmUpdateEvent.FromData(du) : null,
            DmTyping = data.Get("dm_typing") is JsonObject dt ? DmTypingEvent.FromData(dt) : null,
        };
        return payload;
    }

    public override string ToString()
    {
        var fields = new List<string>();
        if (Config is not null) fields.Add($"config={Config}");
        if (Subscriptions is not null) fields.Add($"subscriptions={Subscriptions}");
        if (TweetEngagement is not null) fields.Add($"tweet_engagement={TweetEngagement}");
        if (DmUpdate is not null) fields.Add($"dm_update={DmUpdate}");
        if (DmTyping is not null) fields.Add($"dm_typing={DmTyping}");
        return $"Payload({string.Join(" ", fields)})";
    }
}

/// <summary>設定イベント。</summary>
/// <param name="SessionId">セッション ID。</param>
/// <param name="SubscriptionTtlMillis">購読の有効期間。</param>
/// <param name="HeartbeatMillis">ハートビート間隔（ミリ秒）。</param>
public sealed record ConfigEvent(string SessionId, long SubscriptionTtlMillis, long HeartbeatMillis)
{
    public static ConfigEvent FromData(JsonObject data) => new(
        data.Str("session_id") ?? "",
        data.Long("subscription_ttl_millis") ?? 0,
        data.Long("heartbeat_millis") ?? 0);
}

/// <summary>購読状態イベント。</summary>
/// <param name="Errors">エラーの一覧。</param>
public sealed record SubscriptionsEvent(JsonArray? Errors)
{
    public static SubscriptionsEvent FromData(JsonObject data) => new(data.Arr("errors"));
}

/// <summary>ツイートのエンゲージメント指標イベント。</summary>
public sealed record TweetEngagementEvent(
    string? LikeCount,
    string? RetweetCount,
    string? ViewCount,
    string? ViewCountState,
    long? QuoteCount,
    long? ReplyCount)
{
    public static TweetEngagementEvent FromData(JsonObject data)
    {
        string? viewCount = null, viewCountState = null;
        if (data.Get("view_count_info") is JsonObject viewInfo)
        {
            viewCount = viewInfo.Str("count");
            viewCountState = viewInfo.Str("state");
        }
        return new TweetEngagementEvent(
            data.Str("like_count"),
            data.Str("retweet_count"),
            viewCount,
            viewCountState,
            data.Long("quote_count"),
            data.Long("reply_count"));
    }
}

/// <summary>DM の更新イベント。</summary>
/// <param name="ConversationId">会話の ID。</param>
/// <param name="UserId">DM を送ったユーザーの ID。</param>
public sealed record DmUpdateEvent(string ConversationId, string UserId)
{
    public static DmUpdateEvent FromData(JsonObject data) => new(data.Str("conversation_id") ?? "", data.Str("user_id") ?? "");
}

/// <summary>DM の入力中イベント。</summary>
/// <param name="ConversationId">会話の ID。</param>
/// <param name="UserId">入力中のユーザーの ID。</param>
public sealed record DmTypingEvent(string ConversationId, string UserId)
{
    public static DmTypingEvent FromData(JsonObject data) => new(data.Str("conversation_id") ?? "", data.Str("user_id") ?? "");
}

/// <summary>ストリーミング用のトピック文字列を生成します。</summary>
public static class Topic
{
    /// <summary>ツイートのエンゲージメントイベント。</summary>
    public static string TweetEngagement(string tweetId) => $"/tweet_engagement/{tweetId}";

    /// <summary>DM の更新イベント。conversationId はグループ ID または <c>相手ID-自分ID</c>。</summary>
    public static string DmUpdate(string conversationId) => $"/dm_update/{conversationId}";

    /// <summary>DM の入力中イベント。conversationId はグループ ID または <c>相手ID-自分ID</c>。</summary>
    public static string DmTyping(string conversationId) => $"/dm_typing/{conversationId}";
}
