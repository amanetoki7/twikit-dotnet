using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>ICE（STUN/TURN）サーバー。</summary>
/// <param name="Urls">サーバーの URL。</param>
/// <param name="Username">ユーザー名（TURN）。</param>
/// <param name="Credential">資格情報（TURN）。</param>
public sealed record IceServer(IReadOnlyList<string> Urls, string? Username = null, string? Credential = null);

/// <summary>
/// WebRTC の RTCPeerConnection の抽象。
/// </summary>
/// <remarks>
/// .NET 標準には WebRTC の実装が無いため（Python 版は aiortc を使います）、音声経路を使うときは
/// SIPSorcery などでこのインターフェイスを実装し、<see cref="IPeerConnectionFactory"/> として渡してください。
/// メタデータ・チャット・コントロール API は実装無しで動作します。
/// </remarks>
public interface IPeerConnection : IAsyncDisposable
{
    /// <summary>接続状態（"new"、"connecting"、"connected"、"disconnected"、"failed"、"closed"）。</summary>
    string ConnectionState { get; }
    /// <summary>接続状態が変わったとき。</summary>
    event Action? ConnectionStateChanged;
    /// <summary>リモートのトラックを受け取ったとき（実装固有のオブジェクト）。</summary>
    event Action<object>? TrackReceived;
    /// <summary>トランシーバーを追加します。<paramref name="track"/> が null なら受信専用（"recvonly"）。</summary>
    void AddTransceiver(object? track, string direction);
    /// <summary>SDP offer を作ります。</summary>
    Task<string> CreateOfferAsync();
    /// <summary>SDP answer を作ります。</summary>
    Task<string> CreateAnswerAsync();
    Task SetLocalDescriptionAsync(string type, string sdp);
    Task SetRemoteDescriptionAsync(string type, string sdp);
    /// <summary>現在のローカル SDP。</summary>
    string? LocalSdp { get; }
    Task CloseAsync();
}

/// <summary><see cref="IPeerConnection"/> を作るファクトリ。</summary>
public interface IPeerConnectionFactory
{
    IPeerConnection Create(IReadOnlyList<IceServer> iceServers);
}

/// <summary>
/// スペースの高レベル WebRTC セッション（発言または聴取）。
/// <see cref="IPeerConnectionFactory"/> の実装が必要です。
/// </summary>
public sealed class SpaceVoiceSession
{
    private readonly SpacesHttp _http;
    private readonly IPeerConnectionFactory? _factory;
    public string PeriscopeUserId { get; }
    public string RoomId { get; }
    public JanusClient Janus { get; }
    public string StreamName { get; }
    public string SessionUuid { get; }
    public string Display { get; }
    /// <summary>ピア接続（<see cref="ConnectAsync"/> 後）。</summary>
    public IPeerConnection? Pc { get; private set; }
    public long? PublisherId { get; private set; }
    private TaskCompletionSource<long>? _publisherTcs;
    private JsonArray _streams = new();
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _negotiationFailed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _negotiationError;
    private readonly List<Task> _jsepTasks = new();
    private readonly HashSet<(string Type, string Sdp)> _seenJsep = new();
    private JanusClient? _subscriberHandle;
    /// <summary>聴取時に受け取ったリモートの音声トラック（実装固有のオブジェクト）。</summary>
    public object? RemoteAudio { get; private set; }
    /// <summary>リモートの音声トラックを受け取ったときのコールバック。</summary>
    public Action<object>? OnAudioTrack { get; set; }

    public SpaceVoiceSession(
        string janusUrl,
        string vidManToken,
        string streamName = "",
        string sessionUuid = "",
        string display = "",
        string periscopeUserId = "",
        string roomId = "",
        SpacesHttp? http = null,
        IPeerConnectionFactory? peerConnectionFactory = null,
        Action<object>? onAudioTrack = null)
    {
        _http = http ?? new SpacesHttp();
        _factory = peerConnectionFactory;
        PeriscopeUserId = periscopeUserId;
        RoomId = roomId;
        Janus = new JanusClient(janusUrl, vidManToken, _http, periscopeUserId, roomId);
        StreamName = streamName;
        SessionUuid = sessionUuid;
        Display = display;
        OnAudioTrack = onAudioTrack;
    }

    private IPeerConnectionFactory RequireFactory()
        => _factory ?? throw new SpaceException(
            "WebRTC voice requires an IPeerConnectionFactory implementation (for example one built on SIPSorcery). " +
            "Pass it as `peerConnectionFactory`; the metadata, chat and control APIs work without it.");

    /// <summary>
    /// Janus と WebRTC の接続を確立します。
    /// </summary>
    /// <param name="iceServers">ICE サーバー（空なら直接接続）。</param>
    /// <param name="asPublisher">発言側として接続するか。</param>
    /// <param name="createRoom">Janus ルームを作るか（ホスト用）。</param>
    /// <param name="streams">聴取時に購読するストリーム。</param>
    /// <param name="withDummyPublisher">ルーム作成時に dummy publisher を付けるか。</param>
    /// <param name="audioTrack">送信する音声トラック（実装固有のオブジェクト）。</param>
    public async Task ConnectAsync(IReadOnlyList<IceServer> iceServers, bool asPublisher = true, bool createRoom = false,
        IList<JsonObject>? streams = null, bool withDummyPublisher = false, object? audioTrack = null)
    {
        var factory = RequireFactory();
        _publisherTcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        Janus.OnPublisherId = ResolvePublisher;
        Janus.OnPublishers = publishers => _streams = publishers;
        Janus.OnJsep = OnJsep;

        await Janus.CreateAsync().ConfigureAwait(false);
        await Janus.AttachAsync().ConfigureAwait(false);

        // Hosts must also attach a SECOND videoroom handle on the SAME session
        // and subscribe to their own feed. Without it the backend marks the
        // space TimedOut after ~2 minutes. Only the main handle long-polls the
        // session; the main poller dispatches by `sender` handle id.
        _subscriberHandle = null;
        if (asPublisher)
        {
            var janus2 = new JanusClient(Janus.JanusUrl, Janus.VidManToken, _http, PeriscopeUserId, RoomId)
            {
                SessionId = Janus.SessionId,
            };
            await janus2.AttachAsync().ConfigureAwait(false);
            _subscriberHandle = janus2;

            Janus.OnRawEvent = evt =>
            {
                if (evt.Long("sender") != janus2.HandlerId) return;
                if (evt.Obj("jsep") is { } jsep) janus2.OnJsep?.Invoke(jsep, evt);
                var plugin = evt.Sub("plugindata").Sub("data");
                if (plugin.Str("videoroom") == "attached" && plugin.Arr("streams") is { } s) janus2.OnAttached?.Invoke(s);
            };
        }

        Pc = factory.Create(iceServers);
        Pc.TrackReceived += OnTrack;
        Pc.ConnectionStateChanged += OnConnectionState;
        // Janus delivers videoroom events and SDP exclusively through the long
        // poll. It must be running before join/configure messages.
        Janus.StartPolling();

        if (asPublisher)
        {
            if (createRoom)
            {
                try
                {
                    await Janus.CreateRoomAsync(withDummyPublisher).ConfigureAwait(false);
                }
                catch (SpaceException e)
                {
                    // The room already exists when the host re-joins their own
                    // space or when joining an existing one - that is fine.
                    var msg = (e.Message ?? "").ToLowerInvariant();
                    if (!msg.Contains("already exists") && !msg.Contains("427")) throw;
                }
            }
            await Janus.JoinAsPublisherAsync(Display).ConfigureAwait(false);
            long? publisherId;
            try
            {
                publisherId = await WaitForPublisherIdAsync(15).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                publisherId = null;
            }
            // Subscribe to our own feed on the second handle.
            if (_subscriberHandle is not null && publisherId is not null)
            {
                try
                {
                    await _subscriberHandle.JoinAsSubscriberAsync(new[] { new JsonObject { ["feed"] = publisherId.Value, ["mid"] = "0" } }).ConfigureAwait(false);
                }
                catch (SpaceException)
                {
                    // best effort
                }
            }
            if (audioTrack is not null)
            {
                // The web client adds its audio track with an explicit
                // 'sendonly' direction; 'sendrecv' makes X's SFU classify the
                // session as a listener and tear it down after ~60s.
                Pc.AddTransceiver(audioTrack, "sendonly");
            }
            // Give the track time to be ready before the offer.
            await Task.Delay(200).ConfigureAwait(false);
            var offer = await Pc.CreateOfferAsync().ConfigureAwait(false);
            await Pc.SetLocalDescriptionAsync("offer", offer).ConfigureAwait(false);
            await Janus.SendSdpOfferAsync(offer, StreamName, SessionUuid, Janus.VidManToken).ConfigureAwait(false);
        }
        else
        {
            Pc.AddTransceiver(null, "recvonly");
            var normalized = SpaceUtils.NormalizeAudioStreams(streams ?? new List<JsonObject>());
            if (normalized.Count == 0)
            {
                var participants = await Janus.ListParticipantsAsync().ConfigureAwait(false);
                normalized = participants
                    .Where(p => p.BoolOr("publisher", false) && p.Get("id") is not null)
                    .Select(p => new JsonObject { ["feed"] = p.Get("id").Clone(), ["mid"] = "0" })
                    .ToList();
            }
            if (normalized.Count == 0) throw new SpaceException("no active audio publisher in the Space");
            // Subscriber negotiation is server-driven: join makes Janus send an
            // SDP offer through long polling; ApplyRemoteJsep answers it.
            await Janus.JoinAsSubscriberAsync(normalized).ConfigureAwait(false);
        }

        // ICE completion alone is not enough: DTLS may remain stuck in
        // `connecting`, yielding a track object but never any RTP frames.
        var finished = await Task.WhenAny(_connected.Task, _negotiationFailed.Task, Task.Delay(TimeSpan.FromSeconds(25))).ConfigureAwait(false);
        if (finished == _negotiationFailed.Task && _negotiationError is not null)
        {
            var error = _negotiationError;
            await CloseAsync().ConfigureAwait(false);
            throw new SpaceException($"WebRTC SDP negotiation failed: {error.Message}", error);
        }
        if (finished != _connected.Task)
        {
            var role = asPublisher ? "publisher" : "listener";
            await CloseAsync().ConfigureAwait(false);
            throw new SpaceException($"{role} WebRTC/DTLS negotiation timed out");
        }
    }

    private void ResolvePublisher(long pid)
    {
        PublisherId = pid;
        _publisherTcs?.TrySetResult(pid);
    }

    public async Task<long> WaitForPublisherIdAsync(double timeoutSeconds = 15.0)
    {
        if (PublisherId is not null) return PublisherId.Value;
        var task = _publisherTcs?.Task ?? throw new SpaceException("ConnectAsync() has not been called");
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds))).ConfigureAwait(false);
        if (finished != task) throw new TimeoutException("Timed out waiting for the Janus publisher id.");
        return await task.ConfigureAwait(false);
    }

    private void OnJsep(JsonObject jsep, JsonObject evt)
    {
        if (Pc is null) return;
        var task = ApplyRemoteJsepAsync(jsep);
        lock (_jsepTasks) _jsepTasks.Add(task);
        _ = task.ContinueWith(t =>
        {
            lock (_jsepTasks) _jsepTasks.Remove(task);
            if (t.IsFaulted && t.Exception is not null && _negotiationError is null)
            {
                _negotiationError = t.Exception.GetBaseException();
                _negotiationFailed.TrySetResult(true);
            }
        }, TaskScheduler.Default);
    }

    private async Task ApplyRemoteJsepAsync(JsonObject jsep)
    {
        var sdp = jsep.Str("sdp");
        if (string.IsNullOrEmpty(sdp) || Pc is null) return;
        var jsepType = jsep.Str("type") ?? "answer";
        lock (_seenJsep)
        {
            if (!_seenJsep.Add((jsepType, sdp))) return;
        }
        await Pc.SetRemoteDescriptionAsync(jsepType, sdp).ConfigureAwait(false);
        if (jsepType == "offer")
        {
            var answer = await Pc.CreateAnswerAsync().ConfigureAwait(false);
            await Pc.SetLocalDescriptionAsync("answer", answer).ConfigureAwait(false);
            await Janus.SendSdpAnswerAsync(Pc.LocalSdp ?? answer).ConfigureAwait(false);
        }
    }

    private void OnTrack(object track)
    {
        RemoteAudio = track;
        OnAudioTrack?.Invoke(track);
    }

    private void OnConnectionState()
    {
        try
        {
            if (Pc?.ConnectionState == "connected") _connected.TrySetResult(true);
        }
        catch (Exception)
        {
            // ignore
        }
    }

    public async Task CloseAsync()
    {
        Task[] tasks;
        lock (_jsepTasks) tasks = _jsepTasks.ToArray();
        if (tasks.Length > 0)
        {
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch (Exception) { }
        }
        if (Pc is not null)
        {
            await Pc.CloseAsync().ConfigureAwait(false);
            Pc = null;
        }
        await Janus.CloseAsync().ConfigureAwait(false);
    }
}
