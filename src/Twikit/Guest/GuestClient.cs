using System.Net;
using System.Text.Json.Nodes;
using Twikit.Api;
using Twikit.Transaction;

namespace Twikit.Guest;

/// <summary>
/// ゲスト（未ログイン）として Twitter API と対話するクライアント。
/// </summary>
/// <remarks>
/// <b>2026 年現在、X はゲストアクセスを閉じています。</b> 未ログインのリクエストには webpack マニフェストを
/// 含まないページの殻しか返ってこないため、<c>x-client-transaction-id</c> のハンドシェイクが完了できず、
/// <see cref="ActivateAsync"/> は <see cref="InvalidSessionException"/> を送出します。これはライブラリ側では直せません。
/// ログイン済みの Cookie を使う <see cref="Client"/> を利用してください。
/// </remarks>
public sealed class GuestClient : IRequestClient, IRawHttpSession, IDisposable
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    /// <summary>実際に通信する <see cref="HttpClient"/>。</summary>
    public HttpClient Http { get; }
    /// <summary>Cookie ジャー。</summary>
    public CookieContainer Cookies { get; }
    /// <summary>API リクエストで使う言語コード。</summary>
    public string Language { get; set; }
    /// <summary>プロキシの URL。</summary>
    public string? Proxy { get; }
    public GqlClient Gql { get; }
    public V11Client V11 { get; }
    public ClientTransaction ClientTransaction { get; }
    public string UserAgent { get; }

    private readonly string _token = Constants.Token;
    private string? _guestToken;

    /// <param name="language">API リクエストで使う言語コード。</param>
    /// <param name="proxy">プロキシの URL。</param>
    /// <param name="handler">独自の <see cref="HttpMessageHandler"/>。</param>
    public GuestClient(string language = "en-US", string? proxy = null, HttpMessageHandler? handler = null)
    {
        Language = language;
        Proxy = proxy;
        Cookies = new CookieContainer(capacity: 2000, perDomainCapacity: 200, maxCookieSize: 8192);
        handler ??= new HttpClientHandler();
        switch (handler)
        {
            case HttpClientHandler h:
                h.UseCookies = false;
                h.AllowAutoRedirect = false;
                if (proxy is not null) { h.Proxy = new WebProxy(new Uri(proxy)); h.UseProxy = true; }
                break;
            case SocketsHttpHandler s:
                s.UseCookies = false;
                s.AllowAutoRedirect = false;
                if (proxy is not null) { s.Proxy = new WebProxy(new Uri(proxy)); s.UseProxy = true; }
                break;
        }
        Http = new HttpClient(handler, disposeHandler: true);
        UserAgent = DefaultUserAgent;
        Gql = new GqlClient(this);
        V11 = new V11Client(this);
        ClientTransaction = new ClientTransaction();
    }

    /// <summary>生のリクエスト（Python 版の <c>_send</c>）。メディアのダウンロードなどが使います。</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RequestOptions? options = null)
    {
        options ??= new RequestOptions();
        var uri = url;
        if (options.Params is { Count: > 0 })
        {
            var query = string.Join("&", options.Params.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
            uri += (url.Contains('?') ? "&" : "?") + query;
        }
        var redirects = 0;
        var target = new Uri(uri);
        while (true)
        {
            using var request = new HttpRequestMessage(method, target);
            HttpContent? content = null;
            if (options.Json is not null) content = new StringContent(options.Json.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            else if (options.Form is not null) content = new FormUrlEncodedContent(options.Form);
            if (options.Headers is not null)
            {
                foreach (var (key, value) in options.Headers)
                {
                    if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (content is not null) content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(value);
                        continue;
                    }
                    if (!request.Headers.TryAddWithoutValidation(key, value)) content?.Headers.TryAddWithoutValidation(key, value);
                }
            }
            request.Content = content;
            var cookieHeader = Cookies.GetCookieHeader(target);
            if (cookieHeader.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                foreach (var value in values)
                {
                    try { Cookies.SetCookies(target, value); }
                    catch (CookieException) { }
                }
            }
            var status = (int)response.StatusCode;
            if (options.FollowRedirects && status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location && redirects < 20)
            {
                redirects++;
                target = location.IsAbsoluteUri ? location : new Uri(target, location);
                response.Dispose();
                if (status == 303 || ((status is 301 or 302) && method == HttpMethod.Post))
                {
                    method = HttpMethod.Get;
                    options = new RequestOptions { Headers = options.Headers, FollowRedirects = true };
                }
                continue;
            }
            return response;
        }
    }

    Task<HttpResponseMessage> IRawHttpSession.RequestAsync(HttpMethod method, string url, Dictionary<string, string>? headers, Dictionary<string, string>? form)
        => SendAsync(method, url, new RequestOptions { Headers = headers, Form = form });

    public async Task<ApiResponse> RequestAsync(HttpMethod method, string url, RequestOptions? options = null)
    {
        options ??= new RequestOptions();
        var headers = options.Headers is null ? new Dictionary<string, string>() : new Dictionary<string, string>(options.Headers);

        if (!ClientTransaction.IsInited())
        {
            var cookiesBackup = new Dictionary<string, string>();
            foreach (Cookie c in Cookies.GetAllCookies()) if (!c.Expired) cookiesBackup[c.Name] = c.Value;
            var ctHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = $"{Language},{Language.Split('-')[0]};q=0.9",
                ["Cache-Control"] = "no-cache",
                ["Referer"] = $"https://{Constants.Domain}",
                ["User-Agent"] = UserAgent,
            };
            await ClientTransaction.InitAsync(this, ctHeaders).ConfigureAwait(false);
            // A domain-less cookie would be sent to *every* host, so pin the
            // guest token and friends to X.
            foreach (Cookie c in Cookies.GetAllCookies()) c.Expired = true;
            foreach (var (key, value) in cookiesBackup)
                foreach (var domain in Constants.CookieDomains)
                    Cookies.Add(new Cookie(key, value, "/", domain));
        }

        headers["X-Client-Transaction-Id"] = ClientTransaction.GenerateTransactionId(method.Method, new Uri(url).AbsolutePath);

        var response = await SendAsync(method, url, new RequestOptions
        {
            Headers = headers, Params = options.Params, Json = options.Json, Form = options.Form,
            Files = options.Files, FollowRedirects = options.FollowRedirects, Timeout = options.Timeout,
        }).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var apiResponse = new ApiResponse(response, text);

        var statusCode = (int)response.StatusCode;
        if (statusCode >= 400 && options.RaiseException)
        {
            var message = $"status: {statusCode}, message: \"{text}\"";
            var responseHeaders = TwitterException.HeadersOf(response);
            throw statusCode switch
            {
                400 => new BadRequestException(message, responseHeaders),
                401 => new UnauthorizedException(message, responseHeaders),
                403 => new ForbiddenException(message, responseHeaders),
                404 => new NotFoundException(message, responseHeaders),
                408 => new RequestTimeoutException(message, responseHeaders),
                429 => new TooManyRequestsException(message, responseHeaders),
                >= 500 and < 600 => new ServerErrorException(message, responseHeaders),
                _ => new TwitterException(message, responseHeaders),
            };
        }
        return apiResponse;
    }

    public Task<ApiResponse> GetAsync(string url, RequestOptions? options = null) => RequestAsync(HttpMethod.Get, url, options);

    public Task<ApiResponse> PostAsync(string url, RequestOptions? options = null) => RequestAsync(HttpMethod.Post, url, options);

    public string? GetCsrfToken()
    {
        foreach (Cookie c in Cookies.GetAllCookies()) if (c.Name == "ct0" && !c.Expired) return c.Value;
        return null;
    }

    /// <summary>API リクエストの基本ヘッダー。</summary>
    public Dictionary<string, string> BaseHeaders
    {
        get
        {
            var headers = new Dictionary<string, string>
            {
                ["authorization"] = $"Bearer {_token}",
                ["content-type"] = "application/json",
                ["X-Twitter-Active-User"] = "yes",
                ["Referer"] = $"https://{Constants.Domain}",
                ["User-Agent"] = UserAgent,
            };
            if (Language is not null)
            {
                headers["Accept-Language"] = Language;
                headers["X-Twitter-Client-Language"] = Language;
            }
            if (_guestToken is not null) headers["X-Guest-Token"] = _guestToken;
            return headers;
        }
    }

    /// <summary>ゲストトークンを取得してクライアントを有効化します。</summary>
    public async Task<string> ActivateAsync()
    {
        var response = await V11.GuestActivateAsync().ConfigureAwait(false);
        _guestToken = response.Object.Str("guest_token") ?? throw new TwitterException("guest/activate returned no guest_token.");
        return _guestToken;
    }

    /// <summary>スクリーンネームを指定してユーザーを取得します。</summary>
    public async Task<GuestUser> GetUserByScreenNameAsync(string screenName)
    {
        var response = await Gql.UserByScreenNameAsync(screenName).ConfigureAwait(false);
        var result = response.Json.Get("data").Get("user").Get("result") as JsonObject
                     ?? throw new UserNotFoundException("The user does not exist.");
        return new GuestUser(this, result);
    }

    /// <summary>ID を指定してユーザーを取得します。</summary>
    public async Task<GuestUser> GetUserByIdAsync(string userId)
    {
        var response = await Gql.UserByRestIdAsync(userId).ConfigureAwait(false);
        var result = response.Json.Get("data").Get("user").Get("result") as JsonObject
                     ?? throw new UserNotFoundException("The user does not exist.");
        return new GuestUser(this, result);
    }

    /// <summary>
    /// ユーザーのツイートを取得します。
    /// </summary>
    /// <param name="userId">ユーザー ID。</param>
    /// <param name="tweetType">'Tweets' のみ。</param>
    /// <param name="count">取得する件数。</param>
    public async Task<List<GuestTweet>> GetUserTweetsAsync(string userId, string tweetType = "Tweets", int count = 40)
    {
        tweetType = Client.Capitalize(tweetType);
        if (tweetType != "Tweets") throw new ArgumentException($"Invalid tweetType '{tweetType}'; only Tweets is supported.", nameof(tweetType));
        var response = await Gql.UserTweetsAsync(userId, count, null).ConfigureAwait(false);
        var instructionsFound = response.Json.FindDict("instructions", findOne: true);
        if (instructionsFound.Count == 0 || instructionsFound[0] is not JsonArray instructions) return new List<GuestTweet>();
        var items = JsonExtensions.FindEntryByType(instructions, "TimelineAddEntries").ArrOrEmpty("entries");
        var results = new List<GuestTweet>();
        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (!entryId.StartsWith("tweet", StringComparison.Ordinal) && !entryId.StartsWith("profile-conversation", StringComparison.Ordinal)
                && !entryId.StartsWith("profile-grid", StringComparison.Ordinal))
                continue;
            var tweet = GuestTweet.FromData(this, item);
            if (tweet is not null) results.Add(tweet);
        }
        return results;
    }

    /// <summary>ID を指定してツイートを取得します。</summary>
    public async Task<GuestTweet?> GetTweetByIdAsync(string tweetId)
    {
        var response = await Gql.TweetResultByRestIdAsync(tweetId).ConfigureAwait(false);
        return GuestTweet.FromData(this, response.Json);
    }

    /// <summary>ユーザーのハイライトツイートを取得します。</summary>
    public async Task<Result<GuestTweet>> GetUserHighlightsTweetsAsync(string userId, int count = 20, string? cursor = null)
    {
        var response = await Gql.UserHighlightsTweetsAsync(userId, count, cursor).ConfigureAwait(false);
        var instructions = response.Json.Get("data").Get("user").Get("result").Get("timeline").Get("timeline").Arr("instructions");
        var instruction = JsonExtensions.FindEntryByType(instructions, "TimelineAddEntries");
        if (instruction is null) return Result<GuestTweet>.Empty();
        var entries = instruction.ArrOrEmpty("entries");
        string? previousCursor = null;
        string? nextCursor = null;
        var results = new List<GuestTweet>();
        foreach (var entry in entries.Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("tweet", StringComparison.Ordinal))
            {
                var tweet = GuestTweet.FromData(this, entry);
                if (tweet is not null) results.Add(tweet);
            }
            else if (entryId.StartsWith("cursor-top", StringComparison.Ordinal)) previousCursor = entry.Sub("content").Str("value");
            else if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal)) nextCursor = entry.Sub("content").Str("value");
        }
        return new Result<GuestTweet>(results,
            nextCursor is not null ? () => GetUserHighlightsTweetsAsync(userId, count, nextCursor) : null, nextCursor,
            previousCursor is not null ? () => GetUserHighlightsTweetsAsync(userId, count, previousCursor) : null, previousCursor);
    }

    public void Dispose()
    {
        Http.Dispose();
    }
}
