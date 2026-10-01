using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>
/// スペースのチャット。履歴は HTTP で取得でき、ライブの受信・送信は WebSocket（.NET 標準の
/// <see cref="ClientWebSocket"/>）を使います。
/// </summary>
public sealed class SpaceChat
{
    private readonly ProxseeApi _proxsee;
    public string ChatToken { get; }
    public string? Endpoint { get; private set; }
    public string? RoomId { get; private set; }
    public string? AccessToken { get; private set; }
    /// <summary>読み取り専用か（リプレイ、または参加者でない）。</summary>
    public bool ReadOnly { get; private set; }
    private ClientWebSocket? _ws;
    private Exception? _wsError;

    /// <summary>ライブの WebSocket が開いているか。</summary>
    public bool IsLive => _ws is not null && _ws.State == WebSocketState.Open;

    public SpaceChat(ProxseeApi proxsee, string chatToken, string? endpoint = null, string? roomId = null, string? accessToken = null)
    {
        _proxsee = proxsee;
        ChatToken = chatToken;
        Endpoint = endpoint;
        RoomId = roomId;
        AccessToken = accessToken;
    }

    /// <summary>
    /// proxsee でチャットアクセスを解決し、スペースがライブなら WebSocket を開きます。
    /// リプレイは読み取り専用で、履歴は取れますが WebSocket は提供されません。
    /// </summary>
    public async Task<SpaceChat> ConnectAsync()
    {
        // The web client uses accessChat (NOT accessChatPublic) - public
        // access comes back read_only and cannot send messages.
        var data = await _proxsee.AccessChatAsync(ChatToken).ConfigureAwait(false);
        Endpoint = data.Str("endpoint") ?? Endpoint;
        RoomId = data.Str("room_id") ?? RoomId;
        AccessToken = data.Str("access_token") ?? AccessToken;
        ReadOnly = data.BoolOr("read_only", false);
        if (!string.IsNullOrEmpty(Endpoint) && !string.IsNullOrEmpty(AccessToken))
        {
            try
            {
                var wsUrl = Endpoint.Replace("https://", "wss://").Replace("http://", "ws://") + "/chatapi/v1/chatnow";
                var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).ConfigureAwait(false);
                _ws = ws;
                // The chatman protocol sends two control frames on open: an
                // auth frame (kind 3) and a join frame (kind 2/Control wrapping a Join body).
                await SendFrameAsync(new JsonObject
                {
                    ["payload"] = new JsonObject { ["access_token"] = AccessToken }.ToJsonString(),
                    ["kind"] = 3,
                }).ConfigureAwait(false);
                await SendFrameAsync(new JsonObject
                {
                    ["payload"] = new JsonObject
                    {
                        ["body"] = new JsonObject { ["room"] = RoomId }.ToJsonString(),
                        ["kind"] = 1,
                    }.ToJsonString(),
                    ["kind"] = 2,
                }).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                // Replays and temporarily unavailable live-chat gateways can
                // still use HTTP history. Preserve the actual failure.
                _wsError = exc;
                if (_ws is not null)
                {
                    try { _ws.Dispose(); } catch (Exception) { }
                    _ws = null;
                }
            }
        }
        return this;
    }

    private async Task SendFrameAsync(JsonObject frame)
    {
        var ws = _ws ?? throw new SpaceException("chat WebSocket is not connected");
        var bytes = Encoding.UTF8.GetBytes(frame.ToJsonString());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
    }

    private void RequireLiveWebSocket(string action)
    {
        if (_ws is not null) return;
        if (_wsError is not null)
            throw new SpaceException($"{action} is unavailable because the live-chat WebSocket connection failed: {_wsError.Message}", _wsError);
        throw new SpaceException($"{action} is unavailable because this Space did not offer a live-chat WebSocket (replays are history-only)");
    }

    /// <summary>チャット履歴を取得します。</summary>
    public async Task<List<ChatMessage>> HistoryAsync(string? cursor = null, int limit = 1000)
    {
        if (string.IsNullOrEmpty(Endpoint) || string.IsNullOrEmpty(AccessToken))
            throw new SpaceException("chat not connected; call ConnectAsync() first");
        var data = await _proxsee.GetChatHistoryAsync(Endpoint, AccessToken, cursor, limit).ConfigureAwait(false);
        return data.ArrOrEmpty("messages").Objects().Select(ChatMessage.FromPayload).ToList();
    }

    /// <summary>チャットメッセージを到着順に列挙します（WebSocket が必要）。</summary>
    public async IAsyncEnumerable<ChatMessage> ListenAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RequireLiveWebSocket("live chat");
        var ws = _ws!;
        var buffer = new byte[64 * 1024];
        while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) yield break;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var data = JsonExtensions.TryParse(Encoding.UTF8.GetString(message.ToArray())) as JsonObject;
            if (data is null) continue;
            // kind 1 = Chat (with payload+signature), 2 = Control, 3 = Auth.
            // Only chat frames carry user messages.
            var kind = data.Int("kind");
            if (kind is 1 or 2) yield return ChatMessage.FromPayload(data);
        }
    }

    /// <summary>チャットメッセージを送ります（WebSocket が必要。読み取り専用なら <see cref="SpaceException"/>）。</summary>
    public async Task SendAsync(string text)
    {
        if (ReadOnly) throw new SpaceException("chat is read-only for this access level (replay or non-participant)");
        RequireLiveWebSocket("sending chat");
        // Shape used by the web client chatman (Periscope chat protocol).
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ntp = 1e9 * ((nowMs / 1000.0) + 2208988800);
        var sender = new JsonObject
        {
            ["user_id"] = _proxsee.PeriscopeUserId ?? "",
            ["twitter_id"] = _proxsee.TwitterId ?? "",
            ["username"] = _proxsee.TwitterScreenName ?? "",
            ["display_name"] = _proxsee.DisplayName ?? "",
            ["participant_index"] = 0,
        };
        var body = new JsonObject
        {
            ["body"] = text,
            ["displayName"] = _proxsee.DisplayName ?? "",
            ["ntpForBroadcasterFrame"] = ntp,
            ["ntpForLiveFrame"] = ntp,
            ["participant_index"] = 0,
            ["programDateTime"] = DateTime.UtcNow.ToString("o"),
            ["remoteID"] = _proxsee.PeriscopeUserId ?? "",
            ["timestamp"] = nowMs,
            ["type"] = 1, // Chat (X8 message type enum - numeric, not string)
            ["username"] = _proxsee.TwitterScreenName ?? "",
            ["uuid"] = Guid.NewGuid().ToString(),
            ["v"] = 2,
        };
        var frame = new JsonObject
        {
            ["payload"] = new JsonObject
            {
                ["kind"] = 1,
                ["room"] = RoomId,
                ["lang"] = "en",
                ["body"] = body.ToJsonString(),
                ["sender"] = sender,
                ["timestamp"] = nowMs,
            }.ToJsonString(),
            ["kind"] = 1,
        };
        await SendFrameAsync(frame).ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        if (_ws is null) return;
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) { }
        _ws.Dispose();
        _ws = null;
    }
}
