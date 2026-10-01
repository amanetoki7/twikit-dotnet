using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>
/// chatman コントロールプレーン（guest-cf.pscp.tv の <c>/api/v1</c>）のクライアント。
/// すべてのリクエストは proxsee <c>authorizeToken</c>（service=guest）で得たトークンで認証され、
/// ボディにスペースのチャットトークンを持ちます。
/// </summary>
public sealed class ChatmanApi
{
    private readonly Client _client;
    internal SpacesHttp Http { get; }
    private readonly ProxseeApi _proxsee;
    private string? _guestServiceToken;
    private string? _chatAccessToken;
    private bool _initialized;

    public ChatmanApi(Client client)
    {
        _client = client;
        Http = new SpacesHttp();
        _proxsee = new ProxseeApi(client);
    }

    public async Task InitializeAsync(string? chatAccessToken = null)
    {
        if (_initialized)
        {
            // The guest-service authorization is account-scoped, but the chat
            // access token changes when entering/reconnecting a Space.
            if (chatAccessToken is not null) _chatAccessToken = chatAccessToken;
            return;
        }
        var resp = await _proxsee.GetTokenForServiceAsync("guest").ConfigureAwait(false);
        var token = resp.Str("authorization_token");
        if (string.IsNullOrEmpty(token)) throw new SpaceException($"authorizeToken returned no token: {resp.ToJsonString()}");
        _guestServiceToken = token;
        if (chatAccessToken is not null) _chatAccessToken = chatAccessToken;
        _initialized = true;
    }

    private Task<JsonObject> PostAsync(string endpoint, JsonObject payload)
    {
        if (!_initialized) throw new SpaceException("ChatmanApi not initialized; call InitializeAsync() first");
        var body = (JsonObject)payload.DeepClone();
        body["chat_token"] = _chatAccessToken;
        return Http.PostAsync($"{SpaceConstants.ChatmanHost}/api/v1/{endpoint}", body,
            new Dictionary<string, string> { ["Authorization"] = _guestServiceToken! });
    }

    private static JsonObject WithNtp(JsonObject payload) => SpaceUtils.Merge(SpaceUtils.NtpMetadata(), payload);

    // -- session -----------------------------------------------------------

    /// <summary>スピーカーとして参加します。{can_auto_join, session_uuid, ...} を返します。</summary>
    public Task<JsonObject> JoinAsSpeakerAsync(string broadcastId, bool joinAsAdmin = false, bool shouldAutoJoin = true)
        => PostAsync("audiospace/join", WithNtp(new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["join_as_admin"] = joinAsAdmin,
            ["should_auto_join"] = shouldAutoJoin,
        }));

    /// <summary>Janus の資格情報 {janus_jwt, webrtc_gw_url, ...} を返します。</summary>
    public Task<JsonObject> NegotiateStreamAsync(string sessionUuid)
        => PostAsync("audiospace/stream/negotiate", new JsonObject { ["session_uuid"] = sessionUuid });

    public Task<JsonObject> PublishStreamAsync(string sessionUuid, JsonObject? extra = null)
        => PostAsync("audiospace/stream/publish", SpaceUtils.Merge(WithNtp(new JsonObject { ["session_uuid"] = sessionUuid }), extra));

    public Task<JsonObject> EndStreamAsync(string sessionUuid)
        => PostAsync("audiospace/stream/end", WithNtp(new JsonObject { ["session_uuid"] = sessionUuid }));

    // -- moderation --------------------------------------------------------

    /// <summary>スペース全体を終了します（ホスト/管理者のみ）。</summary>
    public Task<JsonObject> EndAudioSpaceAsync(string broadcastId)
        => PostAsync("audiospace/admin/endAudiospace", new JsonObject { ["broadcast_id"] = broadcastId });

    public Task<JsonObject> SetSpaceSettingsAsync(string broadcastId, int conversationControls = 0, JsonArray? topics = null,
        JsonArray? mentionedTwitterUserIds = null)
        => PostAsync("audiospace/admin/setAudiospaceSettings", new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["conversation_controls"] = conversationControls,
            ["topics"] = topics ?? new JsonArray(),
            ["mentioned_twitter_user_ids"] = mentionedTwitterUserIds ?? new JsonArray(),
        });

    public Task<JsonObject> MuteSpeakerAsync(string sessionUuid, string broadcastId)
        => PostAsync("audiospace/muteSpeaker", WithNtp(new JsonObject { ["session_uuid"] = sessionUuid, ["broadcast_id"] = broadcastId }));

    public Task<JsonObject> UnmuteSpeakerAsync(string sessionUuid, string broadcastId)
        => PostAsync("audiospace/unmuteSpeaker", WithNtp(new JsonObject { ["session_uuid"] = sessionUuid, ["broadcast_id"] = broadcastId }));

    public Task<JsonObject> MuteSpaceAsync(string broadcastId)
        => PostAsync("audiospace/admin/muteSpace", WithNtp(new JsonObject { ["broadcast_id"] = broadcastId }));

    public Task<JsonObject> UnmuteSpaceAsync(string broadcastId)
        => PostAsync("audiospace/admin/unmuteSpace", WithNtp(new JsonObject { ["broadcast_id"] = broadcastId }));

    public Task<JsonObject> RaiseHandAsync(string sessionUuid, string broadcastId, string emoji = "✋")
        => PostAsync("audiospace/raiseHand", new JsonObject { ["session_uuid"] = sessionUuid, ["broadcast_id"] = broadcastId, ["emoji"] = emoji });

    public Task<JsonObject> LowerHandAsync(string sessionUuid, string broadcastId)
        => PostAsync("audiospace/lowerHand", new JsonObject { ["session_uuid"] = sessionUuid, ["broadcast_id"] = broadcastId });

    public Task<JsonObject> ApproveRequestAsync(string sessionUuid)
        => PostAsync("audiospace/request/approve", WithNtp(new JsonObject { ["session_uuid"] = sessionUuid }));

    public Task<JsonObject> RejectRequestAsync(string sessionUuid)
        => PostAsync("audiospace/request/reject", new JsonObject { ["session_uuid"] = sessionUuid });

    public Task<JsonObject> SubmitSpeakerRequestAsync(string broadcastId)
        => PostAsync("audiospace/request/submit", WithNtp(new JsonObject { ["broadcast_id"] = broadcastId }));

    public Task<JsonObject> CancelSpeakerRequestAsync(string broadcastId, string sessionUuid)
        => PostAsync("audiospace/request/cancel", WithNtp(new JsonObject { ["broadcast_id"] = broadcastId, ["session_uuid"] = sessionUuid }));

    public Task<JsonObject> RemoveParticipantAsync(string broadcastId, IEnumerable<string> twitterUserIds)
        => PostAsync("audiospace/admin/removeParticipant", WithNtp(new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["twitter_user_ids"] = twitterUserIds.ToJsonArray(),
        }));

    public Task<JsonObject> AddAdminAsync(string broadcastId, string twitterUserId)
        => PostAsync("audiospace/admin/addAdmin", WithNtp(new JsonObject { ["broadcast_id"] = broadcastId, ["twitter_user_id"] = twitterUserId }));

    public Task<JsonObject> RemoveAdminAsync(string broadcastId, string sessionUuid, string twitterUserId)
        => PostAsync("audiospace/removeAdmin", WithNtp(new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["session_uuid"] = sessionUuid,
            ["twitter_user_id"] = twitterUserId,
        }));

    /// <summary>
    /// 管理者（ホスト/共同ホスト）を chatman のルームに登録します。Web クライアントは publishBroadcast の直後に
    /// ホスト自身の twitter id で呼びます。これがホストを管理者（かつミュート解除）としてリスナーに見せます。
    /// </summary>
    public Task<JsonObject> AdminInviteAsync(string broadcastId, IEnumerable<string> twitterUserIds, string sessionUuid = "")
        => PostAsync("audiospace/admin/invite", new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["twitter_user_ids"] = twitterUserIds.ToJsonArray(),
            ["session_uuid"] = sessionUuid,
        });

    /// <summary>
    /// Janus の音声ストリームから publisher を追い出します。X は Chatman と Janus の両方の識別子を要求します。
    /// </summary>
    public Task<JsonObject> StreamEjectAsync(string sessionUuid, long webrtcHandleId, long webrtcSessionId, string janusRoomId, long janusParticipantId)
        => PostAsync("audiospace/stream/eject", WithNtp(new JsonObject
        {
            ["session_uuid"] = sessionUuid,
            ["webrtc_handle_id"] = webrtcHandleId,
            ["webrtc_session_id"] = webrtcSessionId,
            ["janus_room_id"] = janusRoomId,
            ["janus_participant_id"] = janusParticipantId,
        }));

    public Task<JsonObject> GetCallStatusAsync(string broadcastId, bool includeNonActiveSessions = true)
        => PostAsync("audiospace/call/status", new JsonObject
        {
            ["broadcast_id"] = broadcastId,
            ["include_non_active_sessions"] = includeNonActiveSessions,
        });
}
