using System.Globalization;
using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

/// <summary>
/// メディアオブジェクトの基底クラス。
/// </summary>
public class Media
{
    protected readonly IRequestClient ClientRef;

    /// <summary>X から受け取った生の JSON。</summary>
    public JsonObject Data { get; }

    public Media(IRequestClient client, JsonObject data)
    {
        ClientRef = client;
        Data = data;
    }

    /// <summary>メディア ID。</summary>
    public string? Id => Data.Str("id_str");
    /// <summary>表示用 URL。</summary>
    public string? DisplayUrl => Data.Str("display_url");
    /// <summary>展開済みの表示 URL。</summary>
    public string? ExpandedUrl => Data.Str("expanded_url");
    /// <summary>メディアの URL。</summary>
    public string? MediaUrl => Data.Str("media_url_https");

    /// <summary>元の（フル解像度の）メディア URL。</summary>
    public string? SourceUrl
    {
        get
        {
            var url = Data.Str("media_url_https");
            if (string.IsNullOrEmpty(url)) return url;
            var sep = url.Contains('?') ? "&" : "?";
            return $"{url}{sep}name=orig";
        }
    }

    /// <summary>元のツイートの ID。</summary>
    public string? SourceStatusId => Data.Str("source_status_id_str");
    /// <summary>元のツイートを投稿したユーザーの ID。</summary>
    public string? SourceUserId => Data.Str("source_user_id_str");
    /// <summary>メディアの種類（photo / video / animated_gif）。</summary>
    public string? Type => Data.Str("type");
    /// <summary>t.co の URL。</summary>
    public string? Url => Data.Str("url");
    /// <summary>各サイズの情報。</summary>
    public JsonObject? Sizes => Data.Obj("sizes");
    public JsonObject? OriginalInfo => Data.Obj("original_info");
    /// <summary>幅。</summary>
    public int? Width => OriginalInfo.Int("width");
    /// <summary>高さ。</summary>
    public int? Height => OriginalInfo.Int("height");
    public JsonArray? FocusRects => OriginalInfo.Arr("focus_rects");

    /// <summary>メディアの内容を取得します。</summary>
    public async Task<byte[]> GetAsync()
    {
        var url = MediaUrl ?? throw new NotFoundException("This media has no URL.");
        var response = await ClientRef.SendAsync(HttpMethod.Get, url, new RequestOptions { FollowRedirects = true }).ConfigureAwait(false);
        if ((int)response.StatusCode >= 400)
        {
            // Writing the error page to disk as a .jpg looked like success.
            throw new NotFoundException($"Media unavailable ({(int)response.StatusCode}) at {url}");
        }
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>メディアをファイルに保存します。</summary>
    public async Task DownloadAsync(string outputPath)
    {
        await File.WriteAllBytesAsync(outputPath, await GetAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    public override string ToString() => $"<{GetType().Name} id={Id}>";

    /// <summary>
    /// メディア JSON から適切なクラスのインスタンスを作ります。未知の種類は警告を出して null。
    /// </summary>
    public static Media? FromData(IRequestClient client, JsonObject data)
    {
        switch (data.Str("type"))
        {
            case "video": return new Video(client, data);
            case "photo": return new Photo(client, data);
            case "animated_gif": return new AnimatedGif(client, data);
            default:
                TwikitWarnings.Warn($"Unknown media type: '{data.Str("type")}'");
                return null;
        }
    }
}

/// <summary>写真。</summary>
public sealed class Photo : Media
{
    public Photo(IRequestClient client, JsonObject data) : base(client, data) { }

    /// <summary>写真の特徴（顔の位置など）。</summary>
    public JsonObject? Features => Data.Obj("features");
}

/// <summary>
/// メディアストリーム（動画の 1 つの画質）。
/// </summary>
public sealed class Stream
{
    private readonly IRequestClient _client;

    public JsonObject Data { get; }

    public Stream(IRequestClient client, JsonObject data)
    {
        _client = client;
        Data = data;
    }

    /// <summary>ストリームの URL。</summary>
    public string? Url => Data.Str("url");
    /// <summary>ビットレート。</summary>
    public long? Bitrate => Data.Long("bitrate");
    /// <summary>MIME タイプ。</summary>
    public string? ContentType => Data.Str("content_type") ?? Data.Str("content-type");

    /// <summary>ストリームの内容を取得します。</summary>
    public async Task<byte[]> GetAsync()
    {
        var url = Url ?? throw new NotFoundException("This stream has no URL.");
        var response = await _client.SendAsync(HttpMethod.Get, url, new RequestOptions { FollowRedirects = true }).ConfigureAwait(false);
        if ((int)response.StatusCode >= 400)
            throw new NotFoundException($"Stream unavailable ({(int)response.StatusCode}) at {url}");
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>ストリームをダウンロードしてファイルに保存します。</summary>
    public async Task DownloadAsync(string outputPath)
    {
        await File.WriteAllBytesAsync(outputPath, await GetAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    public override string ToString() => $"<Stream url=\"{Url}\">";
}

/// <summary>アニメーション GIF。</summary>
public sealed class AnimatedGif : Media
{
    public AnimatedGif(IRequestClient client, JsonObject data) : base(client, data) { }

    public JsonObject? VideoInfo => Data.Obj("video_info");

    /// <summary>アスペクト比。</summary>
    public (int Width, int Height)? AspectRatio
    {
        get
        {
            var a = VideoInfo.Arr("aspect_ratio");
            return a is { Count: >= 2 } ? (a[0].AsInt() ?? 0, a[1].AsInt() ?? 0) : null;
        }
    }

    /// <summary>動画ストリームの一覧。</summary>
    public List<Stream> Streams => VideoInfo.ArrOrEmpty("variants").Objects().Select(v => new Stream(ClientRef, v)).ToList();
}

/// <summary>WebVTT の 1 キュー。</summary>
/// <param name="Start">開始時刻（"00:00:01.000" 形式）。</param>
/// <param name="End">終了時刻。</param>
/// <param name="Text">字幕テキスト。</param>
public sealed record WebVttCaption(string Start, string End, string Text)
{
    public TimeSpan StartTime => WebVtt.ParseTimestamp(Start);
    public TimeSpan EndTime => WebVtt.ParseTimestamp(End);
}

/// <summary>最小限の WebVTT パーサー。</summary>
public static class WebVtt
{
    public static List<WebVttCaption> Parse(string text)
    {
        var captions = new List<WebVttCaption>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var arrow = line.IndexOf("-->", StringComparison.Ordinal);
            if (arrow < 0) continue;
            var start = line.Substring(0, arrow).Trim();
            var end = line.Substring(arrow + 3).Trim().Split(' ')[0];
            var body = new List<string>();
            for (i++; i < lines.Length && lines[i].Trim().Length > 0; i++) body.Add(lines[i]);
            captions.Add(new WebVttCaption(start, end, string.Join("\n", body)));
        }
        return captions;
    }

    public static TimeSpan ParseTimestamp(string value)
    {
        var parts = value.Split(':');
        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        int minutes = parts.Length >= 2 ? int.Parse(parts[^2], CultureInfo.InvariantCulture) : 0;
        int hours = parts.Length >= 3 ? int.Parse(parts[^3], CultureInfo.InvariantCulture) : 0;
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }
}

/// <summary>M3U8 プレイリストの最小限の表現。</summary>
public sealed class M3u8Playlist
{
    /// <summary>#EXT-X-MEDIA のエントリ。</summary>
    public List<M3u8Media> Media { get; } = new();
    /// <summary>#EXTINF に続くセグメント URI。</summary>
    public List<string> Segments { get; } = new();

    public static M3u8Playlist Parse(string text)
    {
        var playlist = new M3u8Playlist();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var expectSegment = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.Ordinal))
            {
                playlist.Media.Add(M3u8Media.Parse(line.Substring("#EXT-X-MEDIA:".Length)));
            }
            else if (line.StartsWith("#EXTINF", StringComparison.Ordinal))
            {
                expectSegment = true;
            }
            else if (!line.StartsWith('#'))
            {
                if (expectSegment) playlist.Segments.Add(line);
                expectSegment = false;
            }
        }
        return playlist;
    }
}

/// <summary>#EXT-X-MEDIA の属性。</summary>
public sealed class M3u8Media
{
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Type => Attributes.TryGetValue("TYPE", out var v) ? v : null;
    public string? Uri => Attributes.TryGetValue("URI", out var v) ? v : null;
    public string? GroupId => Attributes.TryGetValue("GROUP-ID", out var v) ? v : null;
    public string? Language => Attributes.TryGetValue("LANGUAGE", out var v) ? v : null;
    public string? Name => Attributes.TryGetValue("NAME", out var v) ? v : null;

    internal static M3u8Media Parse(string attributes)
    {
        var media = new M3u8Media();
        var i = 0;
        while (i < attributes.Length)
        {
            var eq = attributes.IndexOf('=', i);
            if (eq < 0) break;
            var key = attributes.Substring(i, eq - i).Trim();
            i = eq + 1;
            string value;
            if (i < attributes.Length && attributes[i] == '"')
            {
                var close = attributes.IndexOf('"', i + 1);
                if (close < 0) close = attributes.Length;
                value = attributes.Substring(i + 1, close - i - 1);
                i = close + 1;
            }
            else
            {
                var comma = attributes.IndexOf(',', i);
                if (comma < 0) comma = attributes.Length;
                value = attributes.Substring(i, comma - i);
                i = comma;
            }
            media.Attributes[key] = value;
            if (i < attributes.Length && attributes[i] == ',') i++;
        }
        return media;
    }
}

/// <summary>
/// 動画。
/// </summary>
/// <example>
/// <code>
/// var tweet = await client.GetTweetByIdAsync("00000000000");
/// var video = (Video)tweet.Media[0];
/// await video.Streams[0].DownloadAsync("output.mp4");
/// </code>
/// </example>
public sealed class Video : Media
{
    private M3u8Playlist? _playlist;
    private M3u8Playlist? _subtitlesPlaylist;
    private const string BaseUrl = "https://video.twimg.com";

    public Video(IRequestClient client, JsonObject data) : base(client, data) { }

    public JsonObject? VideoInfo => Data.Obj("video_info");

    /// <summary>アスペクト比。</summary>
    public (int Width, int Height)? AspectRatio
    {
        get
        {
            var a = VideoInfo.Arr("aspect_ratio");
            return a is { Count: >= 2 } ? (a[0].AsInt() ?? 0, a[1].AsInt() ?? 0) : null;
        }
    }

    /// <summary>再生時間（ミリ秒）。</summary>
    public long? DurationMillis => VideoInfo.Long("duration_millis");

    private IEnumerable<JsonObject> Variants => VideoInfo.ArrOrEmpty("variants").Objects();

    /// <summary>動画ストリームの一覧（content_type が video/ のもの）。</summary>
    public List<Stream> Streams => Variants
        .Where(v => (v.Str("content_type") ?? "").StartsWith("video", StringComparison.Ordinal))
        .Select(v => new Stream(ClientRef, v)).ToList();

    private async Task<M3u8Playlist?> GetPlaylistAsync()
    {
        if (_playlist is not null) return _playlist;
        var m3u8Stream = Variants.FirstOrDefault(v => v.Str("content_type") == "application/x-mpegURL");
        if (m3u8Stream is null) return null;
        var url = m3u8Stream.Str("url");
        if (url is null) return null;
        var response = await ClientRef.GetAsync(url).ConfigureAwait(false);
        _playlist = M3u8Playlist.Parse(response.Text);
        return _playlist;
    }

    private async Task<M3u8Playlist?> GetSubtitlesPlaylistAsync()
    {
        if (_subtitlesPlaylist is not null) return _subtitlesPlaylist;
        var playlist = await GetPlaylistAsync().ConfigureAwait(false);
        if (playlist is null) return null;
        var subtitles = playlist.Media.FirstOrDefault(m => m.Type == "SUBTITLES");
        if (subtitles?.Uri is null) return null;
        var response = await ClientRef.GetAsync(BaseUrl + subtitles.Uri).ConfigureAwait(false);
        _subtitlesPlaylist = M3u8Playlist.Parse(response.Text);
        return _subtitlesPlaylist;
    }

    /// <summary>
    /// 動画の字幕を取得します。字幕が無ければ null。
    /// </summary>
    public async Task<List<WebVttCaption>?> GetSubtitlesAsync()
    {
        var subtitlesPlaylist = await GetSubtitlesPlaylistAsync().ConfigureAwait(false);
        if (subtitlesPlaylist is null || subtitlesPlaylist.Segments.Count == 0) return null;
        var response = await ClientRef.GetAsync(BaseUrl + subtitlesPlaylist.Segments[0]).ConfigureAwait(false);
        return WebVtt.Parse(response.Text);
    }
}
