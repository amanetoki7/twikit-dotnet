using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>
/// 最小限の Janus ゲートウェイクライアント（janus.plugin.videoroom）。
/// Web クライアントと同じく、HTTP ロングポールでイベントを受け、transaction でレスポンスを突き合わせ、
/// 毎リクエスト <c>Authorization: &lt;vidManToken&gt;</c> を付けます。
/// </summary>
public sealed class JanusClient
{
    public string JanusUrl { get; }
    public string VidManToken { get; }
    private readonly SpacesHttp _http;
    public string PeriscopeUserId { get; }
    public string RoomId { get; }
    public long? SessionId { get; set; }
    public long? HandlerId { get; private set; }
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private Task? _pollTask;
    private CancellationTokenSource? _pollCts;

    /// <summary>JSEP（SDP）を受け取ったときに呼ばれます。</summary>
    public Action<JsonObject, JsonObject>? OnJsep { get; set; }
    /// <summary>videoroom に joined したときの publisher id。</summary>
    public Action<long>? OnPublisherId { get; set; }
    /// <summary>publishers 一覧を受け取ったとき。</summary>
    public Action<JsonArray>? OnPublishers { get; set; }
    /// <summary>attached / updated のストリーム一覧。</summary>
    public Action<JsonArray>? OnAttached { get; set; }
    /// <summary>すべての生イベント。</summary>
    public Action<JsonObject>? OnRawEvent { get; set; }

    public JanusClient(string janusUrl, string vidManToken, SpacesHttp http, string periscopeUserId = "", string roomId = "")
    {
        JanusUrl = janusUrl.TrimEnd('/');
        VidManToken = vidManToken;
        _http = http;
        PeriscopeUserId = periscopeUserId;
        RoomId = roomId;
    }

    private Dictionary<string, string> Headers() => new()
    {
        ["Authorization"] = VidManToken,
        ["Content-Type"] = "application/json",
    };

    private async Task<JsonObject> DispatchAsync(JsonObject payload, string suffix = "")
    {
        var transaction = payload.Str("transaction");
        if (transaction is null)
        {
            transaction = SpaceUtils.RandomTransaction();
            payload["transaction"] = transaction;
        }
        var url = suffix.Length > 0 ? $"{JanusUrl}/{suffix}" : JanusUrl;
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[transaction] = tcs;
        try
        {
            var resp = await _http.PostAsync(url, payload, Headers()).ConfigureAwait(false);
            if (resp.Str("janus") == "error")
            {
                var error = resp.Sub("error");
                throw new SpaceException($"Janus error [{error.Get("code").AsStr()}]: {error.Str("reason")}");
            }
            // The long-poll loop may already have resolved this transaction.
            tcs.TrySetResult(resp);
            return resp;
        }
        catch
        {
            tcs.TrySetCanceled();
            throw;
        }
        finally
        {
            _pending.TryRemove(transaction, out _);
        }
    }

    private async Task<JsonObject> MessageAsync(JsonObject payload, JsonObject? jsep = null)
    {
        if (SessionId is null || HandlerId is null) throw new SpaceException("Janus session/handle not created");
        var body = new JsonObject { ["room"] = RoomId, ["periscope_user_id"] = PeriscopeUserId };
        foreach (var kv in payload) body[kv.Key] = kv.Value.Clone();
        var message = new JsonObject { ["janus"] = "message", ["body"] = body };
        if (jsep is not null) message["jsep"] = jsep;
        var resp = await DispatchAsync(message, $"{SessionId}/{HandlerId}").ConfigureAwait(false);
        var plugin = resp.Sub("plugindata").Sub("data");
        if (plugin.Get("error").IsTruthy())
            throw new SpaceException($"Janus videoroom error [{plugin.Get("error_code").AsStr()}]: {plugin.Str("error")}");
        // Depending on gateway timing, the JSEP can be returned directly by
        // this HTTP request instead of arriving through the long poll.
        if (resp.Obj("jsep") is { } responseJsep) OnJsep?.Invoke(responseJsep, resp);
        return resp;
    }

    public async Task<long> CreateAsync()
    {
        var resp = await DispatchAsync(new JsonObject { ["janus"] = "create" }).ConfigureAwait(false);
        SessionId = resp.Sub("data").Long("id") ?? throw new SpaceException("Janus create returned no session id");
        return SessionId.Value;
    }

    public async Task<long> AttachAsync()
    {
        var resp = await DispatchAsync(new JsonObject { ["janus"] = "attach", ["plugin"] = "janus.plugin.videoroom" }, SessionId?.ToString() ?? "").ConfigureAwait(false);
        HandlerId = resp.Sub("data").Long("id") ?? throw new SpaceException("Janus attach returned no handle id");
        return HandlerId.Value;
    }

    public Task CreateRoomAsync(bool withDummyPublisher = false) => MessageAsync(new JsonObject
    {
        ["request"] = "create",
        ["audiocodec"] = "opus",
        ["videocodec"] = "h264",
        ["transport_wide_cc_ext"] = true,
        ["app_component"] = "audio-room",
        ["h264_profile"] = "42e01f",
        ["dummy_publisher"] = withDummyPublisher,
    });

    public Task DestroyRoomAsync() => MessageAsync(new JsonObject { ["request"] = "destroy" });

    public Task JoinAsPublisherAsync(string display = "") => MessageAsync(new JsonObject
    {
        ["request"] = "join",
        ["ptype"] = "publisher",
        ["display"] = display.Length > 0 ? display : PeriscopeUserId,
    });

    public Task JoinAsSubscriberAsync(IEnumerable<JsonObject> streams) => MessageAsync(new JsonObject
    {
        ["request"] = "join",
        ["ptype"] = "subscriber",
        ["streams"] = new JsonArray(streams.Select(s => (JsonNode)s.DeepClone()).ToArray()),
    });

    /// <summary>現在の Janus ルーム参加者を返します。</summary>
    public async Task<List<JsonObject>> ListParticipantsAsync()
    {
        var resp = await MessageAsync(new JsonObject { ["request"] = "listparticipants" }).ConfigureAwait(false);
        return resp.Sub("plugindata").Sub("data").ArrOrEmpty("participants").Objects().ToList();
    }

    public Task SubscribeAsync(IEnumerable<JsonObject> streams) => MessageAsync(new JsonObject
    {
        ["request"] = "subscribe",
        ["streams"] = new JsonArray(streams.Select(s => (JsonNode)s.DeepClone()).ToArray()),
    });

    public Task UpdateStreamsAsync(IEnumerable<JsonObject>? subscribe = null, IEnumerable<JsonObject>? unsubscribe = null) => MessageAsync(new JsonObject
    {
        ["request"] = "update",
        ["subscribe"] = new JsonArray((subscribe ?? Array.Empty<JsonObject>()).Select(s => (JsonNode)s.DeepClone()).ToArray()),
        ["unsubscribe"] = new JsonArray((unsubscribe ?? Array.Empty<JsonObject>()).Select(s => (JsonNode)s.DeepClone()).ToArray()),
    });

    public Task SwitchStreamsAsync(IEnumerable<JsonObject> streams) => MessageAsync(new JsonObject
    {
        ["request"] = "switch",
        ["streams"] = new JsonArray(streams.Select(s => (JsonNode)s.DeepClone()).ToArray()),
    });

    public Task SendSdpOfferAsync(string sdp, string streamName = "", string sessionUuid = "", string vidManToken = "", bool iceRestart = false)
    {
        var payload = new JsonObject
        {
            ["request"] = "configure",
            ["session_uuid"] = sessionUuid,
            ["stream_name"] = streamName,
            ["vidman_token"] = vidManToken,
        };
        // NOTE: do NOT include `descriptions` - the gateway answers 429
        // ("Error processing SDP") when it is present.
        if (iceRestart) payload["restart"] = true;
        return MessageAsync(payload, new JsonObject { ["type"] = "offer", ["sdp"] = sdp });
    }

    /// <summary>サーバーの SDP offer に answer します（サブスクライバー側、<c>start</c>）。</summary>
    public Task<JsonObject> SendSdpAnswerAsync(string sdp)
        => MessageAsync(new JsonObject { ["request"] = "start" }, new JsonObject { ["type"] = "answer", ["sdp"] = sdp });

    public Task UnpublishAsync() => MessageAsync(new JsonObject { ["request"] = "unpublish" });

    public Task LeaveAsync() => MessageAsync(new JsonObject { ["request"] = "leave" });

    public async Task DetachAsync()
    {
        if (SessionId is null || HandlerId is null) return;
        try
        {
            await DispatchAsync(new JsonObject { ["janus"] = "detach" }, $"{SessionId}/{HandlerId}").ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best effort
        }
    }

    public async Task DestroyAsync()
    {
        if (SessionId is null) return;
        try
        {
            await DispatchAsync(new JsonObject { ["janus"] = "destroy" }, SessionId.ToString()!).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // best effort
        }
        SessionId = null;
    }

    // -- long poll ---------------------------------------------------------

    public void StartPolling()
    {
        if (_pollTask is not null && !_pollTask.IsCompleted) return;
        _pollCts = new CancellationTokenSource();
        _pollTask = LongPollLoopAsync(_pollCts.Token);
    }

    private async Task LongPollLoopAsync(CancellationToken cancellationToken)
    {
        while (SessionId is not null && !cancellationToken.IsCancellationRequested)
        {
            JsonObject evt;
            try
            {
                evt = await _http.GetAsync($"{JanusUrl}/{SessionId}", Headers(),
                    new Dictionary<string, string> { ["maxev"] = "1" }, TimeSpan.FromSeconds(35)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }
            if (evt.Count == 0) continue;
            var transaction = evt.Str("transaction");
            if (transaction is not null && _pending.TryRemove(transaction, out var tcs))
            {
                tcs.TrySetResult(evt);
                // The event may carry the async result of the request (e.g.
                // the SDP answer to a configure). Surface it to the handlers.
                HandleEvent(evt);
                continue;
            }
            HandleEvent(evt);
        }
    }

    private void HandleEvent(JsonObject evt)
    {
        OnRawEvent?.Invoke(evt);
        if (evt.Obj("jsep") is { } jsep) OnJsep?.Invoke(jsep, evt);
        var plugin = evt.Sub("plugindata").Sub("data");
        var videoroom = plugin.Str("videoroom");
        if (videoroom == "joined" && OnPublisherId is not null)
        {
            var pid = plugin.Long("id");
            if (pid is not null) OnPublisherId(pid.Value);
        }
        if (plugin.Arr("publishers") is { Count: > 0 } publishers) OnPublishers?.Invoke(publishers);
        if (videoroom is "attached" or "updated" && OnAttached is not null)
        {
            var streams = plugin.Arr("streams");
            if (streams is { Count: > 0 }) OnAttached(streams);
        }
    }

    /// <summary>1 回ロングポールして（イベントが来るまで待って）返します。</summary>
    public async Task<JsonObject?> WaitForEventAsync(double timeoutSeconds = 15.0)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = Math.Min(35.0, (deadline - DateTime.UtcNow).TotalSeconds + 1);
            var evt = await _http.GetAsync($"{JanusUrl}/{SessionId}", Headers(),
                new Dictionary<string, string> { ["maxev"] = "1" }, TimeSpan.FromSeconds(Math.Max(1, remaining))).ConfigureAwait(false);
            if (evt.Count == 0) continue;
            HandleEvent(evt);
            return evt;
        }
        return null;
    }

    public async Task CloseAsync()
    {
        if (_pollTask is not null)
        {
            _pollCts?.Cancel();
            try { await _pollTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _pollTask = null;
        }
        await DetachAsync().ConfigureAwait(false);
        await DestroyAsync().ConfigureAwait(false);
    }
}
