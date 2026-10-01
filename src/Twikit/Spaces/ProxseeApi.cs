using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>
/// Periscope（proxsee.pscp.tv）v2 API のクライアント。
/// 認証は 2 段階です: <c>authenticatePeriscope</c>（X の GraphQL）で JWT を得て、
/// <c>loginTwitterToken</c> でそれを proxsee のセッション Cookie に交換します。
/// </summary>
public sealed class ProxseeApi
{
    private readonly Client _client;
    internal SpacesHttp Http { get; }
    private string? _token;
    private JsonObject? _user;
    private string? _userType;

    public ProxseeApi(Client client)
    {
        _client = client;
        Http = new SpacesHttp();
    }

    // -- auth --------------------------------------------------------------

    /// <summary>AuthenticatePeriscope の JWT を proxsee のセッションに交換します。</summary>
    public async Task LoginAsync(bool createUser = false)
    {
        if (_token is not null) return;
        var response = await _client.Gql.AuthenticatePeriscopeAsync().ConfigureAwait(false);
        var data = response.Json.Sub("data");
        // The web client returns the JWT directly under the snake_case field.
        var tokenNode = data.Get("authenticate_periscope");
        var token = tokenNode is JsonObject tokenObj ? tokenObj.Str("token") : tokenNode.AsStr();
        if (string.IsNullOrEmpty(token))
            throw new ProxseeAuthException($"authenticatePeriscope returned no token: {data.ToJsonString()}");
        var payload = new JsonObject
        {
            ["jwt"] = token,
            ["vendor_id"] = SpaceConstants.PeriscopeVendorId,
            ["create_user"] = createUser,
        };
        // Browser-ish headers are required; without them proxsee answers 400.
        // Do NOT send `direct` - its presence breaks the request.
        var resp = await Http.PostAsync($"{SpaceConstants.ProxseeHost}/api/v2/loginTwitterToken", payload, Headers()).ConfigureAwait(false);
        var cookie = resp.Str("cookie");
        if (string.IsNullOrEmpty(cookie))
            throw new ProxseeAuthException($"loginTwitterToken returned no cookie: {resp.ToJsonString()}");
        _token = cookie;
        _userType = resp.Str("type");
        _user = resp.Obj("user") ?? new JsonObject();
    }

    private JsonObject Body(JsonObject payload)
    {
        if (_token is null) throw new ProxseeAuthException("not logged in; call LoginAsync() first");
        var body = (JsonObject)payload.DeepClone();
        body["cookie"] = _token;
        return body;
    }

    /// <summary>proxsee は毎回ブラウザ風のヘッダーを要求します（無いと turnServers などが 401/400）。</summary>
    public static Dictionary<string, string> Headers()
    {
        var headers = SpaceUtils.IdempotenceHeader();
        headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
        headers["Referer"] = "https://x.com/";
        headers["sec-ch-ua"] = "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"";
        headers["sec-ch-ua-platform"] = "\"Windows\"";
        headers["sec-ch-ua-mobile"] = "?0";
        return headers;
    }

    /// <summary>ログイン済みで proxsee の <c>/api/v2/{endpoint}</c> に POST します。</summary>
    public async Task<JsonObject> PostAsync(string endpoint, JsonObject payload)
    {
        await LoginAsync().ConfigureAwait(false);
        // The proxsee API serves every endpoint under the bare `/api/v2/`
        // prefix; the `twitter/`-prefixed variants 404/401.
        return await Http.PostAsync($"{SpaceConstants.ProxseeHost}/api/v2/{endpoint}", Body(payload), Headers()).ConfigureAwait(false);
    }

    public string? PeriscopeUserId => _user.Str("id");
    public string? TwitterScreenName => _user.Str("twitter_screen_name");
    public string? TwitterId => _user.Str("twitter_id");
    public string? DisplayName => _user.Str("display_name");
    public string? UserType => _userType;

    // -- broadcast lifecycle -----------------------------------------------

    /// <summary>ブロードキャストを作成します（broadcast、access_token、room_id、stream_name、credential、webrtc_gw_url などを返します）。</summary>
    public Task<JsonObject> CreateBroadcastAsync(JsonObject metadata) => PostAsync("createBroadcast", metadata);

    public Task<JsonObject> PublishBroadcastAsync(JsonObject? extra = null)
        => PostAsync("publishBroadcast", SpaceUtils.Merge(SpaceConstants.DefaultPublishPayload(), extra));

    public Task<JsonObject> EndBroadcastAsync(string broadcastId)
        => PostAsync("endBroadcast", new JsonObject { ["broadcast_id"] = broadcastId });

    public Task<JsonObject> PrePublishScheduledAudioBroadcastAsync(string broadcastId)
        => PostAsync("prePublishScheduledAudioBroadcast", new JsonObject { ["broadcast_id"] = broadcastId });

    public Task<JsonObject> CancelScheduledSpaceAsync(string broadcastId)
        => PostAsync("cancelScheduledAudioBroadcast", new JsonObject { ["broadcast_id"] = broadcastId });

    public Task<JsonObject> GetScheduledSpacesAsync() => PostAsync("getScheduledAudioBroadcasts", new JsonObject());

    public Task<JsonObject> ReconnectHostAsync(string broadcastId)
        => PostAsync("reconnectHost", new JsonObject { ["broadcast_id"] = broadcastId });

    public Task<JsonObject> AssociateTweetWithBroadcastAsync(string broadcastId, string tweetId, bool tweetExternal = false)
        => PostAsync("associateTweetWithBroadcast", new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["tweet_id"] = tweetId,
            ["tweet_external"] = tweetExternal,
        });

    // -- chat / media ------------------------------------------------------

    /// <summary>チャットトークンを {endpoint, room_id, access_token} に交換します。</summary>
    public Task<JsonObject> AccessChatAsync(string chatToken) => PostAsync("accessChat", new JsonObject { ["chat_token"] = chatToken });

    public Task<JsonObject> AccessChatPublicAsync(string chatToken) => PostAsync("accessChatPublic", new JsonObject { ["chat_token"] = chatToken });

    public Task<JsonObject> GetChatHistoryAsync(string endpoint, string accessToken, string? cursor = null, int limit = 1000,
        JsonNode? since = null, bool quickGet = true)
    {
        var payload = new JsonObject
        {
            ["access_token"] = accessToken,
            ["cursor"] = cursor,
            ["limit"] = limit,
            ["since"] = since,
            ["quick_get"] = quickGet,
        };
        return Http.PostAsync($"{endpoint}/chatapi/v1/history", payload);
    }

    /// <summary>{uris, username, password}（WebRTC 用の TURN サーバー）を返します。</summary>
    public Task<JsonObject> GetTurnServersAsync() => PostAsync("turnServers", new JsonObject());

    public Task<JsonObject> GetTokenForServiceAsync(string service) => PostAsync("authorizeToken", new JsonObject { ["service"] = service });

    public Task<JsonObject> WebrtcBroadcastMetaAsync(JsonObject payload) => PostAsync("webrtcBroadcastMeta", payload);

    public Task<JsonObject> WebrtcPlaybackMetaAsync(JsonObject payload) => PostAsync("webrtcPlaybackMeta", payload);
}
