using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit.Spaces;

/// <summary>
/// X スペースのすべての操作の入り口（<see cref="Client.Spaces"/>）。
/// </summary>
/// <remarks>
/// <para>
/// メタデータ / 検索 — GraphQL（AudioSpaceById、AudioSpaceSearch、BrowseSpaceTopics、...）。
/// ブロードキャストのライフサイクル — proxsee.pscp.tv（Periscope）v2 API。
/// コントロールプレーン（ミュート、設定、終了、承認 ...）— guest-cf.pscp.tv /api/v1（chatman）。
/// チャット — proxsee accessChat → chatapi v1（HTTP 履歴 + WebSocket）。
/// 音声（発言・聴取）— Janus WebRTC videoroom。ピア接続は <see cref="IPeerConnectionFactory"/> の実装が必要です。
/// </para>
/// <para>API キーは不要で、twikit の他の機能と同じ Cookie 認証のセッションを使います。</para>
/// </remarks>
public sealed class Spaces
{
    private readonly Client _client;
    /// <summary>Periscope（proxsee）API。</summary>
    public ProxseeApi Proxsee { get; }
    /// <summary>chatman コントロールプレーン。</summary>
    public ChatmanApi Chatman { get; }
    private readonly SpacesHttp _http;
    private readonly Dictionary<string, string> _hostSessions = new();

    public Spaces(Client client)
    {
        _client = client;
        Proxsee = new ProxseeApi(client);
        Chatman = new ChatmanApi(client);
        _http = new SpacesHttp();
    }

    // -- metadata / discovery ----------------------------------------------

    /// <summary>13 文字の ID（または URL 全体）でスペースのメタデータを取得します。</summary>
    public async Task<Space> GetSpaceAsync(string spaceId, bool withReplays = true, bool withListeners = true)
    {
        spaceId = SpaceUtils.ExtractSpaceId(spaceId);
        var response = await _client.Gql.AudioSpaceByIdAsync(spaceId, withReplays, withListeners).ConfigureAwait(false);
        var audioSpace = response.Json.Get("data").Obj("audioSpace");
        if (audioSpace is null) throw new SpaceException($"Space not found: {spaceId}");
        return new Space(audioSpace);
    }

    public Task<Space> GetSpaceByUrlAsync(string url, bool withReplays = true, bool withListeners = true)
        => GetSpaceAsync(SpaceUtils.ExtractSpaceId(url), withReplays, withListeners);

    /// <summary>
    /// スペースを検索します。<paramref name="filter"/> は 'Top'、'Live'、'Upcoming'。
    /// X の検索ペイロードには各スペースの rest_id しか無いため、既定では <see cref="GetSpaceAsync"/> で
    /// 並列にメタデータを埋めます。ID だけでよければ <paramref name="hydrate"/> を false に。
    /// </summary>
    public async Task<List<Space>> SearchAsync(string query, string filter = "Live", bool hydrate = true)
    {
        var response = await _client.Gql.AudioSpaceSearchAsync(query, filter).ConfigureAwait(false);
        var audioSpace = response.Json.Get("data").Obj("search_by_raw_query");
        if (audioSpace is null) return new List<Space>();
        var sections = audioSpace.Sub("audio_spaces_grouped_by_section").ArrOrEmpty("sections");
        var spaces = new List<Space>();
        foreach (var section in sections.Objects())
        {
            foreach (var item in section.ArrOrEmpty("items").Objects())
            {
                var spaceData = item.Obj("space");
                if (spaceData is null) continue;
                var metadata = spaceData.Obj("metadata") ?? spaceData;
                spaces.Add(new Space(new JsonObject { ["metadata"] = metadata.DeepClone() }));
            }
        }
        if (!hydrate) return spaces;

        async Task<Space> HydrateOne(Space space)
        {
            if (string.IsNullOrEmpty(space.Id)) return space;
            try
            {
                return await GetSpaceAsync(space.Id).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A live result can end/disappear between the grouped search
                // and AudioSpaceById; retain its id rather than dropping it.
                return space;
            }
        }

        return (await Task.WhenAll(spaces.Select(HydrateOne)).ConfigureAwait(false)).ToList();
    }

    /// <summary>スペースのトピック一覧を取得します。</summary>
    public async Task<List<JsonObject>> TopicsAsync()
    {
        var response = await _client.Gql.BrowseSpaceTopicsAsync().ConfigureAwait(false);
        return response.Json.Get("data").Sub("browse_space_topics").ArrOrEmpty("categories").Objects().ToList();
    }

    // -- stream (HLS listening / replay) -----------------------------------

    /// <summary>media_key からストリーム情報（HLS URL とチャットトークン）を取得します。</summary>
    public async Task<SpaceStream> GetStreamAsync(string mediaKey)
    {
        var response = await _client.V11.LiveVideoStreamStatusAsync(mediaKey).ConfigureAwait(false);
        return SpaceStream.FromResponse(response.Object ?? new JsonObject());
    }

    /// <summary>ライブ / リプレイのスペースの HLS m3u8 URL。</summary>
    public async Task<string?> StreamUrlAsync(Space space)
    {
        if (string.IsNullOrEmpty(space.MediaKey)) return null;
        var stream = await GetStreamAsync(space.MediaKey).ConfigureAwait(false);
        return stream.HlsUrl;
    }

    // -- create / end ------------------------------------------------------

    /// <summary>
    /// スペースを作成します。proxsee の createBroadcast レスポンス（broadcast.id、access_token、room_id、
    /// stream_name、credential、webrtc_gw_url など）を返します。
    /// </summary>
    /// <param name="title">タイトル。</param>
    /// <param name="description">説明。</param>
    /// <param name="topics">トピック。</param>
    /// <param name="conversationControls">発言できる相手（0=リクエスト/承認、1=フォロー中、2=全員）。</param>
    /// <param name="narrowCastSpaceType">0=全員、1=従業員、2=サブスクライバー。</param>
    /// <param name="isSpaceAvailableForReplay">リプレイを残すか。</param>
    /// <param name="isSpaceAvailableForClipping">クリップを許可するか。</param>
    /// <param name="scheduledStartTime">予約開始時刻（エポックミリ秒。10^12 未満は秒として扱い変換します）。</param>
    /// <param name="languages">言語。</param>
    /// <param name="region">リージョン。</param>
    /// <param name="extraMetadata">追加のメタデータ。</param>
    /// <param name="autoPublish">作成後すぐに公開するか。</param>
    /// <param name="publishExtra">publishBroadcast に追加するペイロード。</param>
    public async Task<JsonObject> CreateSpaceAsync(
        string title = "",
        string description = "",
        JsonArray? topics = null,
        int conversationControls = 2,
        int narrowCastSpaceType = 0,
        bool isSpaceAvailableForReplay = false,
        bool isSpaceAvailableForClipping = false,
        long scheduledStartTime = 0,
        JsonArray? languages = null,
        string region = "us-west-1",
        JsonObject? extraMetadata = null,
        bool autoPublish = true,
        JsonObject? publishExtra = null)
    {
        var sst = scheduledStartTime;
        if (sst != 0 && sst < 1_000_000_000_000L) sst *= 1000;
        var metadata = SpaceConstants.DefaultSpaceMetadata();
        metadata["title"] = title;
        metadata["description"] = description;
        metadata["topics"] = topics?.DeepClone() ?? new JsonArray();
        metadata["conversation_controls"] = conversationControls;
        metadata["narrow_cast_space_type"] = narrowCastSpaceType;
        metadata["is_space_available_for_replay"] = isSpaceAvailableForReplay;
        metadata["is_space_available_for_clipping"] = isSpaceAvailableForClipping;
        metadata["scheduled_start_time"] = sst;
        metadata["languages"] = languages?.DeepClone() ?? new JsonArray();
        metadata["region"] = region;
        metadata = SpaceUtils.Merge(metadata, extraMetadata);
        await Proxsee.LoginAsync(createUser: true).ConfigureAwait(false);
        var resp = await Proxsee.CreateBroadcastAsync(metadata).ConfigureAwait(false);
        // Scheduled spaces have no Janus room yet - skip the live-publish dance.
        if (autoPublish && sst == 0)
        {
            var broadcast = resp.Sub("broadcast");
            var broadcastId = broadcast.Str("id") ?? resp.Str("broadcast_id") ?? "";
            // A live publish needs a real Janus room + publisher registration
            // (measured: publishBroadcast 500s without them).
            var janus = new JanusClient(resp.Str("webrtc_gw_url") ?? "", resp.Str("credential") ?? "", _http,
                Proxsee.PeriscopeUserId ?? "", broadcastId);
            await janus.CreateAsync().ConfigureAwait(false);
            await janus.AttachAsync().ConfigureAwait(false);
            await janus.CreateRoomAsync().ConfigureAwait(false);
            await janus.JoinAsPublisherAsync().ConfigureAwait(false);
            long? publisherId = null;
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var evt = await janus.WaitForEventAsync(10).ConfigureAwait(false);
                if (evt is null) break;
                var plugin = evt.Sub("plugindata").Sub("data");
                if (plugin.Str("videoroom") == "joined")
                {
                    publisherId = plugin.Long("id");
                    break;
                }
                if (publisherId is not null) break;
            }
            if (publisherId is null)
            {
                await janus.CloseAsync().ConfigureAwait(false);
                // Clean up the broadcast so we never leave a zombie space.
                try { await Proxsee.EndBroadcastAsync(broadcastId).ConfigureAwait(false); }
                catch (SpaceException) { }
                throw new SpaceException("could not obtain Janus publisher id; space creation aborted");
            }
            var payload = new JsonObject
            {
                ["broadcast_id"] = broadcastId,
                ["status"] = title,
                ["topics"] = topics?.DeepClone() ?? new JsonArray(),
                ["conversation_controls"] = conversationControls,
                ["mentioned_twitter_user_ids"] = new JsonArray(),
                ["janus_publisher_id"] = publisherId.Value,
                ["janus_room_id"] = broadcastId,
                ["webrtc_handle_id"] = janus.HandlerId,
                ["webrtc_session_id"] = janus.SessionId,
            };
            await Proxsee.PublishBroadcastAsync(SpaceUtils.Merge(payload, publishExtra)).ConfigureAwait(false);
            // Register the host as the space admin in chatman. Without this the
            // host shows up muted and listeners hear nothing.
            try
            {
                await Chatman.InitializeAsync(resp.Str("access_token")).ConfigureAwait(false);
                await Chatman.AdminInviteAsync(broadcastId, new[] { broadcast.Get("twitter_id").AsStr() ?? "" }).ConfigureAwait(false);
            }
            catch (SpaceException) { }
            // The room/publisher registration has served its purpose.
            await janus.CloseAsync().ConfigureAwait(false);
        }
        return resp;
    }

    /// <summary>
    /// スペースを終了します。chatman で audiospace を終了し、proxsee でブロードキャストを落とします。
    /// <paramref name="spaceId"/> は proxsee の broadcast id（create_space の戻り値）または X のスペース ID。
    /// </summary>
    public async Task EndSpaceAsync(string spaceId)
    {
        // Chat shutdown is useful, but must never prevent the authoritative
        // proxsee endBroadcast call.
        try
        {
            var space = await GetSpaceAsync(spaceId).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(space.MediaKey))
            {
                var stream = await GetStreamAsync(space.MediaKey).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(stream.ChatToken))
                {
                    await Chatman.InitializeAsync(stream.ChatToken).ConfigureAwait(false);
                    await Chatman.EndAudioSpaceAsync(spaceId).ConfigureAwait(false);
                }
            }
        }
        catch (Exception) { }

        // A dropped connection here otherwise leaves a real Space Running.
        // Retry only transport errors; API errors retain the best-effort behaviour.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await Proxsee.EndBroadcastAsync(spaceId).ConfigureAwait(false);
                break;
            }
            catch (SpaceException)
            {
                break;
            }
            catch (Exception)
            {
                if (attempt == 2) throw;
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt)).ConfigureAwait(false);
            }
        }
    }

    public async Task CancelScheduledSpaceAsync(string broadcastId)
    {
        await Proxsee.LoginAsync().ConfigureAwait(false);
        await Proxsee.CancelScheduledSpaceAsync(broadcastId).ConfigureAwait(false);
    }

    /// <summary>予約済みのスペースを一覧します（各要素は broadcast 情報: id、state='NOT_STARTED'、scheduled_start、media_key、...）。</summary>
    public async Task<List<JsonObject>> GetScheduledSpacesAsync()
    {
        await Proxsee.LoginAsync(createUser: true).ConfigureAwait(false);
        var resp = await Proxsee.GetScheduledSpacesAsync().ConfigureAwait(false);
        return resp.ArrOrEmpty("broadcasts").Objects().Select(b => b.Obj("broadcast") ?? b).ToList();
    }

    // -- join / speak ------------------------------------------------------

    private async Task<string> EnsureChatmanAsync(Space space)
    {
        var spaceId = space.Id;
        if (string.IsNullOrEmpty(spaceId)) throw new SpaceException("Space has no id");
        string? chatToken = null;
        if (!string.IsNullOrEmpty(space.MediaKey))
        {
            try
            {
                var stream = await GetStreamAsync(space.MediaKey).ConfigureAwait(false);
                chatToken = stream.ChatToken;
            }
            catch (Exception)
            {
                chatToken = null;
            }
        }
        chatToken = !string.IsNullOrEmpty(chatToken) ? chatToken : space.Chat.Str("chat_token");
        if (string.IsNullOrEmpty(chatToken)) throw new SpaceException("could not resolve chat token for space");
        // The raw chat token must be exchanged via accessChat for the chatman
        // access token (sending the raw token makes chatman reply `invalid secretvalue`).
        try
        {
            var exchanged = await Proxsee.AccessChatAsync(chatToken).ConfigureAwait(false);
            chatToken = exchanged.Str("access_token") ?? chatToken;
        }
        catch (SpaceException) { }
        await Chatman.InitializeAsync(chatToken).ConfigureAwait(false);
        return spaceId;
    }

    private Task<string> EnsureChatmanAsync(string spaceId) => ResolveThen(spaceId, EnsureChatmanAsync);

    private async Task<string> ResolveThen(string spaceId, Func<Space, Task<string>> f)
        => await f(await GetSpaceAsync(spaceId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// スピーカーまたはリスナーとしてスペースに参加します。chatman audiospace/join の
    /// {session_uuid, can_auto_join, ...} を返します。
    /// </summary>
    public async Task<JsonObject> JoinAsync(Space space, bool asSpeaker = false, bool joinAsAdmin = false, bool shouldAutoJoin = true)
    {
        _ = asSpeaker;
        var spaceId = await EnsureChatmanAsync(space).ConfigureAwait(false);
        return await Chatman.JoinAsSpeakerAsync(spaceId, joinAsAdmin, shouldAutoJoin).ConfigureAwait(false);
    }

    public async Task<JsonObject> JoinAsync(string spaceId, bool asSpeaker = false, bool joinAsAdmin = false, bool shouldAutoJoin = true)
        => await JoinAsync(await GetSpaceAsync(spaceId).ConfigureAwait(false), asSpeaker, joinAsAdmin, shouldAutoJoin).ConfigureAwait(false);

    /// <summary>
    /// スピーカーとして参加し、WebRTC の publish セッションを開きます（<see cref="IPeerConnectionFactory"/> が必要）。
    /// </summary>
    /// <param name="space">スペース。</param>
    /// <param name="audioTrack">送信する音声トラック（実装固有のオブジェクト）。</param>
    /// <param name="peerConnectionFactory">ピア接続のファクトリ。</param>
    /// <param name="iceServers">ICE サーバー。既定は直接接続（X の TURN は約 60 秒で切れるため）。</param>
    /// <param name="sessionUuid">既存の chatman セッションを再利用する場合の UUID（挙手が承認された後など）。</param>
    /// <param name="joinAsAdmin">管理者として参加するか。</param>
    /// <param name="shouldAutoJoin">自動参加するか。</param>
    public async Task<SpaceVoiceSession> SpeakAsync(
        Space space,
        object audioTrack,
        IPeerConnectionFactory peerConnectionFactory,
        IReadOnlyList<IceServer>? iceServers = null,
        string? sessionUuid = null,
        bool joinAsAdmin = false,
        bool shouldAutoJoin = true)
    {
        if (sessionUuid is null)
        {
            var joined = await JoinAsync(space, asSpeaker: true, joinAsAdmin: joinAsAdmin, shouldAutoJoin: shouldAutoJoin).ConfigureAwait(false);
            sessionUuid = joined.Str("session_uuid");
        }
        if (string.IsNullOrEmpty(sessionUuid)) throw new SpaceException($"no session_uuid for SpeakAsync() on {space}");
        var neg = await Chatman.NegotiateStreamAsync(sessionUuid).ConfigureAwait(false);
        var janusJwt = neg.Str("janus_jwt") ?? neg.Str("janusJwt");
        var webrtcGwUrl = neg.Str("webrtc_gw_url") ?? neg.Str("webrtcGwUrl");
        if (string.IsNullOrEmpty(janusJwt) || string.IsNullOrEmpty(webrtcGwUrl))
            throw new SpaceException($"negotiate returned no janus info: {neg.ToJsonString()}");

        // Default to DIRECT connection (no TURN): X's TURN server tears the TLS
        // connection down after ~60s, killing the media path.
        iceServers ??= Array.Empty<IceServer>();

        var session = new SpaceVoiceSession(
            janusUrl: webrtcGwUrl,
            vidManToken: janusJwt,
            streamName: space.Id ?? "",
            sessionUuid: sessionUuid,
            display: Proxsee.PeriscopeUserId ?? "",
            periscopeUserId: Proxsee.PeriscopeUserId ?? "",
            roomId: space.Id ?? "",
            http: _http,
            peerConnectionFactory: peerConnectionFactory);
        await session.ConnectAsync(iceServers, asPublisher: true, createRoom: false, audioTrack: audioTrack).ConfigureAwait(false);
        // Host/speaker bookkeeping that the web client performs right after
        // the SDP offer: unmute the speaker and announce the published stream.
        try { await Chatman.UnmuteSpeakerAsync(sessionUuid, space.Id ?? "").ConfigureAwait(false); }
        catch (SpaceException) { }
        try { await Chatman.PublishStreamAsync(sessionUuid).ConfigureAwait(false); }
        catch (SpaceException) { }
        return session;
    }

    /// <summary>
    /// <see cref="CreateSpaceAsync"/> で作ったスペースのホスト音声セッションを開きます。
    /// </summary>
    /// <param name="created"><see cref="CreateSpaceAsync"/> の戻り値。</param>
    /// <param name="audioTrack">送信する音声トラック。</param>
    /// <param name="peerConnectionFactory">ピア接続のファクトリ。</param>
    /// <param name="iceServers">ICE サーバー。</param>
    /// <param name="publishExtra">publishBroadcast に追加するペイロード。</param>
    public async Task<SpaceVoiceSession> HostAsync(
        JsonObject created,
        object audioTrack,
        IPeerConnectionFactory peerConnectionFactory,
        IReadOnlyList<IceServer>? iceServers = null,
        JsonObject? publishExtra = null)
    {
        var broadcast = created.Sub("broadcast");
        var broadcastId = broadcast.Str("id") ?? created.Str("broadcast_id");
        if (string.IsNullOrEmpty(broadcastId)) throw new SpaceException("CreateSpaceAsync response has no broadcast id");
        iceServers ??= Array.Empty<IceServer>();
        // The current web client reconnects the host before opening media.
        JsonObject reconnected;
        try
        {
            reconnected = await Proxsee.ReconnectHostAsync(broadcastId).ConfigureAwait(false);
        }
        catch (SpaceException)
        {
            reconnected = new JsonObject();
        }
        var hostDetails = SpaceUtils.Merge(created, reconnected);
        var hostBroadcast = reconnected.Obj("broadcast") ?? broadcast;
        var sessionUuid = hostDetails.Str("session_uuid") ?? "";
        if (sessionUuid.Length > 0) _hostSessions[broadcastId] = sessionUuid;
        await Chatman.InitializeAsync(hostDetails.Str("access_token") ?? "").ConfigureAwait(false);

        var session = new SpaceVoiceSession(
            janusUrl: hostDetails.Str("webrtc_gw_url") ?? "",
            vidManToken: hostDetails.Str("credential") ?? "",
            streamName: hostDetails.Str("stream_name") ?? broadcastId,
            sessionUuid: sessionUuid,
            display: Proxsee.PeriscopeUserId ?? "",
            periscopeUserId: Proxsee.PeriscopeUserId ?? "",
            roomId: broadcastId,
            http: _http,
            peerConnectionFactory: peerConnectionFactory);
        await session.ConnectAsync(iceServers, asPublisher: true, createRoom: true, audioTrack: audioTrack).ConfigureAwait(false);
        // The SFU must know the room's active publisher: re-publish with the
        // new session's publisher id.
        if (session.PublisherId is not null)
        {
            try
            {
                var payload = new JsonObject
                {
                    ["broadcast_id"] = broadcastId,
                    ["status"] = hostBroadcast.Str("title") ?? "",
                    ["topics"] = new JsonArray(),
                    ["conversation_controls"] = 0,
                    ["mentioned_twitter_user_ids"] = new JsonArray(),
                    ["janus_publisher_id"] = session.PublisherId.Value,
                    ["janus_room_id"] = broadcastId,
                    ["webrtc_handle_id"] = session.Janus.HandlerId,
                    ["webrtc_session_id"] = session.Janus.SessionId,
                };
                await Proxsee.PublishBroadcastAsync(SpaceUtils.Merge(payload, publishExtra)).ConfigureAwait(false);
            }
            catch (SpaceException) { }
        }
        try { await Chatman.UnmuteSpeakerAsync(sessionUuid, broadcastId).ConfigureAwait(false); }
        catch (SpaceException) { }
        try { await Chatman.PublishStreamAsync(sessionUuid).ConfigureAwait(false); }
        catch (SpaceException) { }
        return session;
    }

    /// <summary>
    /// リスナーとして参加し、WebRTC の受信セッションを開きます（<see cref="IPeerConnectionFactory"/> が必要）。
    /// より簡単に聴くには <see cref="StreamUrlAsync"/> の HLS URL を ffmpeg などで再生してください。
    /// </summary>
    public async Task<SpaceVoiceSession> ListenAsync(
        Space space,
        IPeerConnectionFactory peerConnectionFactory,
        Action<object>? onAudioTrack = null,
        IReadOnlyList<IceServer>? iceServers = null,
        int attempts = 3)
    {
        iceServers ??= Array.Empty<IceServer>();
        var sid = space.Id ?? "";
        attempts = Math.Max(1, attempts);
        Exception? lastError = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            SpaceVoiceSession? session = null;
            try
            {
                var joined = await JoinAsync(space, asSpeaker: false, shouldAutoJoin: true).ConfigureAwait(false);
                var sessionUuid = joined.Str("session_uuid");
                if (string.IsNullOrEmpty(sessionUuid)) throw new SpaceException($"join returned no session_uuid: {joined.ToJsonString()}");
                var neg = await Chatman.NegotiateStreamAsync(sessionUuid).ConfigureAwait(false);
                var janusJwt = neg.Str("janus_jwt") ?? neg.Str("janusJwt") ?? "";
                var webrtcGwUrl = neg.Str("webrtc_gw_url") ?? neg.Str("webrtcGwUrl") ?? "";
                session = new SpaceVoiceSession(
                    janusUrl: webrtcGwUrl,
                    vidManToken: janusJwt,
                    streamName: sid,
                    sessionUuid: sessionUuid,
                    periscopeUserId: Proxsee.PeriscopeUserId ?? "",
                    roomId: sid,
                    http: _http,
                    peerConnectionFactory: peerConnectionFactory);
                await session.ConnectAsync(iceServers, asPublisher: false).ConfigureAwait(false);
                if (onAudioTrack is not null && session.RemoteAudio is not null) onAudioTrack(session.RemoteAudio);
                return session;
            }
            catch (Exception exc)
            {
                lastError = exc;
                if (session is not null) await session.CloseAsync().ConfigureAwait(false);
                if (attempt + 1 < attempts) await Task.Delay(TimeSpan.FromSeconds(1 + attempt)).ConfigureAwait(false);
            }
        }
        throw lastError ?? new SpaceException("could not listen to the Space");
    }

    // -- chat --------------------------------------------------------------

    /// <summary>スペースのチャットセッション（履歴 + 任意の WebSocket）を取得します。</summary>
    public async Task<SpaceChat> ChatAsync(Space space)
    {
        string? chatToken = null;
        if (!string.IsNullOrEmpty(space.MediaKey))
        {
            try
            {
                var stream = await GetStreamAsync(space.MediaKey).ConfigureAwait(false);
                chatToken = stream.ChatToken;
            }
            catch (Exception)
            {
                // ended spaces 404 on live_video_stream/status; fall back to the
                // chat token embedded in the AudioSpaceById payload
                chatToken = null;
            }
        }
        chatToken = !string.IsNullOrEmpty(chatToken) ? chatToken : space.Chat.Str("chat_token");
        if (string.IsNullOrEmpty(chatToken)) throw new SpaceException("could not resolve chat token for space");
        await Proxsee.LoginAsync().ConfigureAwait(false);
        return await new SpaceChat(Proxsee, chatToken).ConnectAsync().ConfigureAwait(false);
    }

    public async Task<SpaceChat> ChatAsync(string spaceId) => await ChatAsync(await GetSpaceAsync(spaceId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// Web クライアントの HTTP ストリーム（/live-chat）によるライブチャット。解析済みの JSON イベント
    /// （userId、controlType、...）を列挙します。WebSocket は不要です。
    /// <paramref name="replay"/> を true にすると終了したスペースのチャットログ（replay=1）を <paramref name="cursor"/> でページ送りしながら返します。
    /// </summary>
    public async IAsyncEnumerable<JsonObject> StreamLiveChatAsync(Space space, string? sessionId = null, string? cursor = null,
        bool replay = false, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var spaceId = space.Id;
        if (string.IsNullOrEmpty(spaceId)) throw new SpaceException("Space has no id");
        var parameters = new Dictionary<string, string> { ["broadcastId"] = spaceId, ["sessionId"] = sessionId ?? "" };
        if (cursor is not null) parameters["cursor"] = cursor;
        if (replay) parameters["replay"] = "1";
        var headers = _client.BaseHeaders;
        // /live-chat demands the same client-transaction header as the rest of
        // the API (401 without it) plus the CSRF token.
        headers["X-Client-Transaction-Id"] = _client.ClientTransaction.GenerateTransactionId("GET", "/live-chat");
        headers["X-Csrf-Token"] = _client.GetCsrfToken() ?? "";
        using var response = await _client.SendCoreAsync(HttpMethod.Get, "https://x.com/live-chat", new RequestOptions
        {
            Params = parameters,
            Headers = headers,
            Timeout = Timeout.InfiniteTimeSpan,
        }, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if ((int)response.StatusCode != 200) throw new SpaceException($"/live-chat returned HTTP {(int)response.StatusCode}");
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) break;
            line = line.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            if (JsonExtensions.TryParse(line) is JsonObject evt && evt.Count > 0) yield return evt;
        }
    }

    // -- moderation shortcuts ----------------------------------------------

    /// <summary>スペース全体をミュートします（ホストのみ）。</summary>
    public async Task<JsonObject> MuteSpaceAsync(string spaceId)
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        return await Chatman.MuteSpaceAsync(spaceId).ConfigureAwait(false);
    }

    /// <summary>スペース全体のミュートを解除します（ホストのみ）。</summary>
    public async Task<JsonObject> UnmuteSpaceAsync(string spaceId)
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        return await Chatman.UnmuteSpaceAsync(spaceId).ConfigureAwait(false);
    }

    /// <summary>発言できる相手を変更します: 0=リクエスト/承認、1=フォロー中、2=全員。</summary>
    public async Task<JsonObject> SetSpaceSettingsAsync(string spaceId, int conversationControls)
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        return await Chatman.SetSpaceSettingsAsync(spaceId, conversationControls).ConfigureAwait(false);
    }

    /// <summary>参加者を共同ホストにします。</summary>
    public async Task<JsonObject> AddAdminAsync(string spaceId, string userId)
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        return await Chatman.AddAdminAsync(spaceId, userId).ConfigureAwait(false);
    }

    /// <summary>共同ホストを通常の参加者に戻します。</summary>
    public async Task<JsonObject> RemoveAdminAsync(string spaceId, string userId, string sessionUuid = "")
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        var hostSession = sessionUuid.Length > 0 ? sessionUuid : _hostSessions.GetValueOrDefault(spaceId, "");
        return await Chatman.RemoveAdminAsync(spaceId, hostSession, userId).ConfigureAwait(false);
    }

    public Task<JsonObject> MuteSpeakerAsync(string spaceId, string sessionUuid) => Chatman.MuteSpeakerAsync(sessionUuid, spaceId);

    public Task<JsonObject> UnmuteSpeakerAsync(string spaceId, string sessionUuid) => Chatman.UnmuteSpeakerAsync(sessionUuid, spaceId);

    /// <summary>発言リクエストを承認します。<paramref name="spaceId"/> を渡すと chatman の初期化も行います。</summary>
    public async Task ApproveAsync(string sessionUuid, string? spaceId = null)
    {
        if (spaceId is not null) await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        await Chatman.ApproveRequestAsync(sessionUuid).ConfigureAwait(false);
    }

    /// <summary>発言リクエストを却下します。</summary>
    public async Task RejectAsync(string sessionUuid, string? spaceId = null)
    {
        if (spaceId is not null) await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        await Chatman.RejectRequestAsync(sessionUuid).ConfigureAwait(false);
    }

    /// <summary>
    /// リスナーとして参加したスペースで発言をリクエストします。ホストが <see cref="ApproveAsync"/> で承認する session_uuid を返します。
    /// </summary>
    public async Task<string> RequestToSpeakAsync(Space space)
    {
        var spaceId = await EnsureChatmanAsync(space).ConfigureAwait(false);
        var resp = await Chatman.SubmitSpeakerRequestAsync(spaceId).ConfigureAwait(false);
        var suuid = resp.Str("session_uuid");
        if (string.IsNullOrEmpty(suuid)) throw new SpaceException($"submit speaker request returned no session_uuid: {resp.ToJsonString()}");
        return suuid;
    }

    public async Task<string> RequestToSpeakAsync(string spaceId)
        => await RequestToSpeakAsync(await GetSpaceAsync(spaceId).ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// call/status をポーリングし、セッションがスピーカーになる（ホストが承認する）まで待ちます。
    /// タイムアウト時は null。
    /// </summary>
    public async Task<JsonObject?> WaitForSpeakerAsync(string spaceId, string sessionUuid, double timeoutSeconds = 120.0, double intervalSeconds = 3.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            JsonObject status;
            try
            {
                status = await GetCallStatusAsync(spaceId).ConfigureAwait(false);
            }
            catch (SpaceException)
            {
                status = new JsonObject();
            }
            foreach (var guest in status.ArrOrEmpty("guest_sessions").Objects())
            {
                if (guest.Str("session_uuid") == sessionUuid && guest.Int("session_state") == 4) return guest;
            }
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds)).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>call/status のスナップショット（guest_sessions、states、...）を取得します。</summary>
    public async Task<JsonObject> GetCallStatusAsync(string spaceId)
    {
        await EnsureChatmanAsync(spaceId).ConfigureAwait(false);
        return await Chatman.GetCallStatusAsync(spaceId).ConfigureAwait(false);
    }

    public Task<JsonObject> RemoveParticipantAsync(string spaceId, IEnumerable<string> userIds) => Chatman.RemoveParticipantAsync(spaceId, userIds);

    public Task<JsonObject> RaiseHandAsync(string spaceId, string sessionUuid) => Chatman.RaiseHandAsync(sessionUuid, spaceId);

    public Task<JsonObject> LowerHandAsync(string spaceId, string sessionUuid) => Chatman.LowerHandAsync(sessionUuid, spaceId);

    /// <summary>保留中の発言リクエストを取り下げます。</summary>
    public Task<JsonObject> CancelSpeakerRequestAsync(string spaceId, string sessionUuid) => Chatman.CancelSpeakerRequestAsync(spaceId, sessionUuid);

    /// <summary>スペースにツイートを共有します。</summary>
    public Task AddSharingAsync(string spaceId, string tweetId) => _client.Gql.AudioSpaceAddSharingAsync(spaceId, tweetId);

    /// <summary>スペースからツイートの共有を削除します。</summary>
    public Task DeleteSharingAsync(string spaceId, string sharingId) => _client.Gql.AudioSpaceDeleteSharingAsync(spaceId, sharingId);

    public Task SubscribeScheduledAsync(string spaceId) => _client.Gql.SubscribeToScheduledSpaceAsync(spaceId);

    public Task UnsubscribeScheduledAsync(string spaceId) => _client.Gql.UnsubscribeFromScheduledSpaceAsync(spaceId);

    /// <summary>ツイートをブロードキャストに関連付けます（proxsee associateTweetWithBroadcast）。</summary>
    public Task<JsonObject> AssociateTweetWithBroadcastAsync(string spaceId, string tweetId, bool tweetExternal = false)
        => Proxsee.AssociateTweetWithBroadcastAsync(spaceId, tweetId, tweetExternal);

    /// <summary>内部の HTTP クライアントを閉じます。</summary>
    public void Close()
    {
        _http.Dispose();
        Proxsee.Http.Dispose();
        Chatman.Http.Dispose();
    }
}
