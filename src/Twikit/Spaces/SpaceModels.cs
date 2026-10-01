using System.Text.Json.Nodes;

namespace Twikit.Spaces;

/// <summary>Spaces 関連の定数（Web バンドル由来）。</summary>
public static class SpaceConstants
{
    public const string ProxseeHost = "https://proxsee-cf.pscp.tv";
    public const string ChatmanHost = "https://guest-cf.pscp.tv";
    public const string PeriscopeVendorId = "m5-proxsee-login-a2011357b73e";

    /// <summary>新しいスペースの既定メタデータ（Web クライアントの createBroadcast ペイロード）。</summary>
    public static JsonObject DefaultSpaceMetadata() => new()
    {
        ["app_component"] = "audio-room",
        ["content_type"] = "visual_audio",
        ["conversation_controls"] = 0, // 0=request/approval needed to speak, 2=everyone (measured)
        ["description"] = "",
        ["height"] = 1080,
        ["is_360"] = false,
        ["is_space_available_for_clipping"] = false,
        ["is_space_available_for_replay"] = false,
        ["is_webrtc"] = true,
        ["languages"] = new JsonArray(),
        ["narrow_cast_space_type"] = 0, // 0=all, 1=employees, 2=subscribers
        ["region"] = "us-west-1",
        ["replaykit_app_bundle"] = "",
        ["replaykit_app_name"] = "",
        ["requires_psp_version"] = new JsonArray(),
        ["scheduled_start_time"] = 0,
        ["source"] = "",
        ["ticket_group_id"] = "",
        ["tickets_total"] = 0,
        ["topics"] = new JsonArray(),
        ["width"] = 1920,
    };

    /// <summary>publishBroadcast が期待する既定ペイロード。</summary>
    public static JsonObject DefaultPublishPayload() => new()
    {
        ["accept_guests"] = true,
        ["bit_rate"] = 0,
        ["camera_rotation"] = 0,
        ["has_location"] = false,
        ["invitees_twitter"] = new JsonArray(),
        ["locale"] = "en-us",
        ["lock"] = new JsonArray(),
        ["lock_private_channels"] = new JsonArray(),
        ["topics"] = new JsonArray(),
        ["lat"] = 0,
        ["lng"] = 0,
        ["friend_chat"] = false,
        ["private_chat"] = false,
        ["enable_sparkles"] = false,
        ["hidden"] = false,
    };
}

/// <summary>スペースの状態。</summary>
public static class SpaceState
{
    public const string Running = "Running";
    public const string Scheduled = "Scheduled";
    public const string Ended = "Ended";
    public const string TimedOut = "TimedOut";
}

/// <summary>スペース内の役割。</summary>
public static class SpaceRole
{
    public const string Host = "host";
    public const string Cohost = "cohost";
    public const string Speaker = "speaker";
    public const string Listener = "listener";
}

/// <summary>
/// 1 つの X スペース（AudioSpaceById の <c>audioSpace</c> ペイロードのラッパー）。
/// </summary>
public sealed class Space
{
    public JsonObject Data { get; }
    public JsonObject Metadata { get; }
    public JsonObject Participants { get; }
    public JsonObject Host { get; }
    public JsonObject Chat { get; }
    public JsonObject Tweet { get; }
    public JsonObject Broadcast { get; }
    public JsonArray Sharings { get; }

    public Space(JsonObject data)
    {
        Data = data;
        Metadata = data.Sub("metadata");
        Participants = data.Sub("participants");
        Host = data.Sub("host");
        Chat = data.Sub("chat");
        Tweet = data.Sub("tweet");
        Broadcast = data.Sub("broadcast");
        Sharings = data.Sub("sharings").ArrOrEmpty("items");
    }

    /// <summary>このスペースに共有されたツイートの sharing_id（delete_sharing 用）。</summary>
    public List<string> SharingIds => Sharings.Objects().Select(s => s.Str("sharing_id")).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();

    public string? Id => Metadata.Str("rest_id");
    public string? State => Metadata.Str("state");
    public string? Title => Metadata.Str("title");
    public string? MediaKey => Metadata.Str("media_key");
    public bool IsLive => State == SpaceState.Running;
    public bool IsScheduled => State == SpaceState.Scheduled;
    public bool IsAvailableForReplay => Metadata.BoolOr("is_space_available_for_replay", false);
    public long? CreatedAt => Metadata.Long("created_at");
    public long? StartedAt => Metadata.Long("started_at");
    public long? ScheduledStart => Metadata.Long("scheduled_start");

    /// <summary>スピーカーのユーザー ID。</summary>
    public List<string> SpeakerIds => Participants.ArrOrEmpty("speakers").Objects()
        .Select(s => s.Sub("user_results").Sub("result").Str("rest_id"))
        .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!).ToList();

    /// <summary>ホストのユーザー ID。</summary>
    public string? HostUserId => Host.Sub("user_results").Sub("result").Str("rest_id") ?? Host.Str("rest_id");

    public override string ToString() => $"Space(id={Id}, state={State}, title={Title})";
}

/// <summary><c>live_video_stream/status/{media_key}</c> の解析結果。</summary>
public sealed class SpaceStream
{
    public JsonObject Raw { get; }
    public string? SessionId { get; init; }
    public string? SourceLocation { get; init; }
    public string? NoRedirectPlaybackUrl { get; init; }
    public string? ChatToken { get; init; }
    public string? StreamType { get; init; }

    public SpaceStream(JsonObject raw)
    {
        Raw = raw;
    }

    /// <summary>再生に最適な HLS URL（m3u8）。</summary>
    public string? HlsUrl => NoRedirectPlaybackUrl ?? SourceLocation;

    public static SpaceStream FromResponse(JsonObject data)
    {
        var source = data.Sub("source");
        return new SpaceStream(data)
        {
            SessionId = data.Str("session_id"),
            SourceLocation = source.Str("location"),
            NoRedirectPlaybackUrl = source.Str("noRedirectPlaybackUrl"),
            ChatToken = data.Str("chatToken"),
            StreamType = source.Str("stream_type"),
        };
    }
}

/// <summary>解析済みの chatapi v1 メッセージ。</summary>
public sealed class ChatMessage
{
    public JsonObject Raw { get; init; } = new();
    public string Type { get; init; } = "message";
    public JsonObject? Sender { get; init; }
    public string? Body { get; init; }
    public long? Timestamp { get; init; }
    public string? SessionUuid { get; init; }
    public string? Kind { get; init; }

    public static ChatMessage FromPayload(JsonObject raw)
    {
        var payloadNode = raw.Get("payload");
        JsonObject payload;
        if (payloadNode.AsStr() is { } payloadText)
            payload = JsonExtensions.TryParse(payloadText) as JsonObject ?? new JsonObject();
        else
            payload = payloadNode as JsonObject ?? new JsonObject();

        var body = payload.Get("body");
        string? text = null;
        string? kind = null;
        JsonObject? parsedBody = null;
        if (body is JsonValue && body.AsStr() is { } bodyText)
        {
            parsedBody = JsonExtensions.TryParse(bodyText) as JsonObject;
            if (parsedBody is not null)
            {
                // Normal chat messages also encode their body as a JSON
                // object. Only objects without a textual payload are
                // control/system events.
                text = parsedBody.Str("body") ?? parsedBody.Str("text") ?? parsedBody.Str("message");
                kind = text is not null ? (parsedBody.Str("type") ?? "text") : (parsedBody.Str("type") ?? "event");
            }
            else
            {
                text = bodyText;
                kind = "text";
            }
        }
        else if (body is JsonObject bodyObj)
        {
            text = bodyObj.Str("text") ?? bodyObj.Str("body") ?? bodyObj.Str("message");
            kind = bodyObj.Str("kind") ?? bodyObj.Str("type") ?? "text";
        }
        return new ChatMessage
        {
            Raw = raw,
            Type = raw.Str("type") ?? payload.Str("type") ?? "message",
            Sender = payload.Obj("sender") ?? raw.Obj("sender"),
            Body = text,
            Timestamp = raw.Long("timestamp") ?? payload.Long("timestamp"),
            SessionUuid = payload.Str("session_uuid") ?? parsedBody?.Str("session_uuid"),
            Kind = kind,
        };
    }
}

/// <summary>Spaces API の失敗。</summary>
public class SpaceException : Exception
{
    public SpaceException(string? message = null, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Periscope のログインフローの失敗。</summary>
public sealed class ProxseeAuthException : SpaceException
{
    public ProxseeAuthException(string? message = null, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Spaces で使う小さなユーティリティ。</summary>
public static class SpaceUtils
{
    private static readonly Random Random = new();

    /// <summary>Web クライアントが使う X-Periscope-User-Agent / X-Idempotence ヘッダー。</summary>
    public static Dictionary<string, string> IdempotenceHeader()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var idempotence = new string(Enumerable.Range(0, 64).Select(_ => chars[Random.Next(chars.Length)]).ToArray());
        return new Dictionary<string, string>
        {
            ["X-Periscope-User-Agent"] = "Twitter/m5",
            ["X-Attempt"] = "1",
            ["X-Idempotence"] = idempotence,
            ["Content-Type"] = "application/json",
        };
    }

    /// <summary>Web クライアントが UTC の日から作る ntp メタデータ（そのまま再現）。</summary>
    public static JsonObject NtpMetadata()
    {
        var day = DateTime.UtcNow.Day;
        var value = 1_000_000_000L * day;
        return new JsonObject { ["ntpForBroadcasterFrame"] = value, ["ntpForLiveFrame"] = value };
    }

    public static string RandomTransaction()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, 12).Select(_ => chars[Random.Next(chars.Length)]).ToArray());
    }

    /// <summary>13 文字の ID、または x.com/i/spaces/&lt;id&gt; の URL 全体を受け付けます。</summary>
    public static string ExtractSpaceId(string value)
    {
        value = value.Trim().TrimEnd('/');
        var slash = value.LastIndexOf('/');
        return slash >= 0 ? value.Substring(slash + 1) : value;
    }

    /// <summary>API/Janus のストリームレコードをサブスクライバー join の形式に変換します。</summary>
    public static List<JsonObject> NormalizeAudioStreams(IEnumerable<JsonObject> streams)
    {
        var normalized = new List<JsonObject>();
        foreach (var stream in streams)
        {
            var feed = stream.Get("feed") ?? stream.Get("feed_id") ?? stream.Get("id");
            if (feed is null) continue;
            normalized.Add(new JsonObject
            {
                ["feed"] = feed.Clone(),
                ["mid"] = stream.Get("mid").AsStr() ?? stream.Get("feed_mid").AsStr() ?? "0",
            });
        }
        return normalized;
    }

    /// <summary>2 つの JsonObject をマージした新しいオブジェクト（後勝ち）。</summary>
    public static JsonObject Merge(JsonObject a, JsonObject? b)
    {
        var o = (JsonObject)a.DeepClone();
        if (b is null) return o;
        foreach (var kv in b) o[kv.Key] = kv.Value.Clone();
        return o;
    }
}

/// <summary>proxsee / chatman / janus 呼び出しで共有する HTTP クライアント。</summary>
public sealed class SpacesHttp : IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public SpacesHttp(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<JsonObject> PostAsync(string url, JsonObject? json = null, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? parameters = null, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, WithParams(url, parameters));
        request.Content = new StringContent((json ?? new JsonObject()).ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        ApplyHeaders(request, headers);
        return await SendAsync(request, url, timeout).ConfigureAwait(false);
    }

    public async Task<JsonObject> GetAsync(string url, Dictionary<string, string>? headers = null,
        Dictionary<string, string>? parameters = null, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, WithParams(url, parameters));
        ApplyHeaders(request, headers);
        return await SendAsync(request, url, timeout).ConfigureAwait(false);
    }

    private static string WithParams(string url, Dictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return url;
        var query = string.Join("&", parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        return url + (url.Contains('?') ? "&" : "?") + query;
    }

    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (key, value) in headers)
        {
            if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
            {
                if (request.Content is not null)
                    request.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(value);
                continue;
            }
            if (!request.Headers.TryAddWithoutValidation(key, value)) request.Content?.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private async Task<JsonObject> SendAsync(HttpRequestMessage request, string url, TimeSpan? timeout)
    {
        using var cts = new CancellationTokenSource(timeout ?? _timeout);
        using var response = await _client.SendAsync(request, cts.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        return Check(response, text, url);
    }

    private static JsonObject Check(HttpResponseMessage response, string text, string url)
    {
        if ((int)response.StatusCode >= 400)
            throw new SpaceException($"{url} returned HTTP {(int)response.StatusCode}: {Truncate(text)}");
        var node = JsonExtensions.TryParse(text);
        if (node is null && text.Trim().Length > 0)
            throw new SpaceException($"{url} returned non-JSON body: {Truncate(text)}");
        return node as JsonObject ?? new JsonObject();
    }

    private static string Truncate(string text) => text.Length > 300 ? text.Substring(0, 300) : text;

    public void Dispose() => _client.Dispose();
}
