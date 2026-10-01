using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Twikit.Api;
using Twikit.Captcha;
using Twikit.Internal;
using Twikit.Transaction;
using Twikit.UiMetrics;

namespace Twikit;

/// <summary>
/// Twitter/X API と対話するクライアント。すべてのメソッドは非同期です。
/// </summary>
/// <remarks>
/// <para>
/// <b>パスワードではなく Cookie でログインしてください。</b> X は <see cref="LoginAsync"/> が使う
/// オンボーディングフローを廃止しています。ブラウザでログインしたセッションの <c>auth_token</c> と
/// <c>ct0</c> を <see cref="SetCookies(IDictionary{string, string}, bool)"/> に渡してください。
/// </para>
/// <para>
/// Python 版の <c>impersonate=</c>（curl_cffi によるブラウザ TLS フィンガープリントの偽装）に
/// 相当する仕組みは .NET 標準にはありません。必要なら <c>handler</c> 引数に独自の
/// <see cref="HttpMessageHandler"/> を渡してください（Cookie とリダイレクトはこのクラスが自前で
/// 処理するので、ハンドラー側では無効にしておきます）。
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var client = new Client("ja");
/// client.SetCookies(new Dictionary&lt;string, string&gt; { ["auth_token"] = "...", ["ct0"] = "..." });
/// if (!await client.IsLoggedInAsync()) throw new Exception("Cookie が無効です");
/// var tweets = await client.SearchTweetAsync("python", "Latest");
/// </code>
/// </example>
public partial class Client : IRequestClient, IRawHttpSession, IDisposable
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6_1) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";

    /// <summary>実際に通信する <see cref="HttpClient"/>。</summary>
    public HttpClient Http { get; }

    /// <summary>Cookie ジャー。</summary>
    public CookieContainer Cookies { get; }

    /// <summary>API リクエストで使う言語コード。</summary>
    public string Language { get; set; }

    /// <summary>プロキシの URL（コンストラクターで指定したもの）。</summary>
    public string? Proxy { get; }

    /// <summary>captcha ソルバー。</summary>
    public CaptchaSolver? CaptchaSolver { get; }

    /// <summary>X-Client-Transaction-Id のハンドシェイク状態。</summary>
    public ClientTransaction ClientTransaction { get; }

    /// <summary>User-Agent。</summary>
    public string UserAgent { get; }

    /// <summary>GraphQL エンドポイント層。</summary>
    public GqlClient Gql { get; }

    /// <summary>v1.1 エンドポイント層。</summary>
    public V11Client V11 { get; }

    /// <summary>X スペース（<see cref="Twikit.Spaces.Spaces"/>）。</summary>
    public Spaces.Spaces Spaces { get; }

    private readonly string _token = Constants.Token;
    private string? _userId;
    private string? _actAs;
    // Headers of the most recent response. X reports the remaining budget
    // on every call, but until now it was only reachable from a 429.
    private Dictionary<string, string>? _responseHeaders;
    private readonly bool _ownsHttp;

    /// <param name="language">API リクエストで使う言語コード。</param>
    /// <param name="proxy">プロキシの URL（例: 'http://0.0.0.0:0000'、'socks5://...'）。</param>
    /// <param name="captchaSolver"><see cref="Capsolver"/> など。</param>
    /// <param name="userAgent">User-Agent（省略時は Safari 相当）。</param>
    /// <param name="handler">
    /// 独自の <see cref="HttpMessageHandler"/>。<see cref="HttpClientHandler"/> / <see cref="SocketsHttpHandler"/> なら
    /// Cookie と自動リダイレクトをこちらで無効化します。
    /// </param>
    /// <param name="timeout">HTTP のタイムアウト（既定 100 秒）。</param>
    public Client(
        string language = "en-US",
        string? proxy = null,
        CaptchaSolver? captchaSolver = null,
        string? userAgent = null,
        HttpMessageHandler? handler = null,
        TimeSpan? timeout = null)
    {
        Language = language;
        Proxy = proxy;
        Cookies = new CookieContainer(capacity: 2000, perDomainCapacity: 200, maxCookieSize: 8192);
        handler ??= new HttpClientHandler();
        ConfigureHandler(handler, proxy);
        Http = new HttpClient(handler, disposeHandler: true);
        if (timeout is not null) Http.Timeout = timeout.Value;
        _ownsHttp = true;
        CaptchaSolver = captchaSolver;
        if (captchaSolver is not null) captchaSolver.Client = this;
        ClientTransaction = new ClientTransaction();
        UserAgent = userAgent ?? DefaultUserAgent;
        Gql = new GqlClient(this);
        V11 = new V11Client(this);
        Spaces = new Spaces.Spaces(this);
    }

    private static void ConfigureHandler(HttpMessageHandler handler, string? proxy)
    {
        // Cookies and redirects are handled by this class (see SendCoreAsync), so
        // the handler must not do either on its own.
        switch (handler)
        {
            case HttpClientHandler h:
                h.UseCookies = false;
                h.AllowAutoRedirect = false;
                if (proxy is not null)
                {
                    h.Proxy = BuildProxy(proxy);
                    h.UseProxy = true;
                }
                break;
            case SocketsHttpHandler s:
                s.UseCookies = false;
                s.AllowAutoRedirect = false;
                if (proxy is not null)
                {
                    s.Proxy = BuildProxy(proxy);
                    s.UseProxy = true;
                }
                break;
        }
    }

    private static WebProxy BuildProxy(string proxy)
    {
        var uri = new Uri(proxy);
        var webProxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            webProxy.Credentials = new NetworkCredential(
                Uri.UnescapeDataString(parts[0]),
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "");
        }
        return webProxy;
    }

    // ------------------------------------------------------------------ HTTP

    /// <summary>
    /// トランザクション ID やエラー処理を挟まない生のリクエスト（Python 版の <c>_send</c>）。
    /// Cookie の送受信とリダイレクト（<see cref="RequestOptions.FollowRedirects"/>）はここで処理します。
    /// </summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RequestOptions? options = null)
        => SendCoreAsync(method, url, options, HttpCompletionOption.ResponseContentRead);

    Task<HttpResponseMessage> IRawHttpSession.RequestAsync(HttpMethod method, string url, Dictionary<string, string>? headers, Dictionary<string, string>? form)
        => SendAsync(method, url, new RequestOptions { Headers = headers, Form = form });

    internal async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, RequestOptions? options, HttpCompletionOption completion)
    {
        options ??= new RequestOptions();
        var uri = BuildUri(url, options.Params);
        var redirects = 0;
        while (true)
        {
            using var request = BuildRequest(method, uri, options);
            var cookieHeader = Cookies.GetCookieHeader(uri);
            if (cookieHeader.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            HttpResponseMessage response;
            if (options.Timeout is { } timeout)
            {
                using var cts = new CancellationTokenSource(timeout);
                response = await Http.SendAsync(request, completion, cts.Token).ConfigureAwait(false);
            }
            else
            {
                response = await Http.SendAsync(request, completion).ConfigureAwait(false);
            }
            StoreResponseCookies(uri, response);

            var status = (int)response.StatusCode;
            if (options.FollowRedirects && status is 301 or 302 or 303 or 307 or 308
                && response.Headers.Location is { } location && redirects < 20)
            {
                redirects++;
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                if (status == 303 || ((status is 301 or 302) && method == HttpMethod.Post))
                {
                    method = HttpMethod.Get;
                    options = new RequestOptions
                    {
                        Headers = options.Headers,
                        FollowRedirects = true,
                        Timeout = options.Timeout,
                    };
                }
                continue;
            }
            return response;
        }
    }

    private void StoreResponseCookies(Uri uri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return;
        foreach (var value in values)
        {
            try
            {
                Cookies.SetCookies(uri, value);
            }
            catch (CookieException)
            {
                // A cookie .NET refuses to parse is not worth failing the request over.
            }
        }
    }

    private static Uri BuildUri(string url, Dictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0) return new Uri(url);
        var query = string.Join("&", parameters.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        var separator = url.Contains('?') ? "&" : "?";
        return new Uri(url + separator + query);
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, Uri uri, RequestOptions options)
    {
        var request = new HttpRequestMessage(method, uri);
        HttpContent? content = null;
        if (options.Files is { Count: > 0 } files)
        {
            var multipart = new MultipartFormDataContent();
            foreach (var file in files)
            {
                var part = new ByteArrayContent(file.Content);
                part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");
                multipart.Add(part, file.Name, file.FileName);
            }
            content = multipart;
        }
        else if (options.Json is not null)
        {
            content = new StringContent(options.Json.ToJsonString(), Encoding.UTF8, "application/json");
        }
        else if (options.Form is not null)
        {
            content = new FormUrlEncodedContent(options.Form);
        }

        if (options.Headers is not null)
        {
            foreach (var (key, value) in options.Headers)
            {
                if (key.Equals("content-type", StringComparison.OrdinalIgnoreCase))
                {
                    if (content is not null && content is not MultipartFormDataContent)
                        content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                    continue;
                }
                if (!request.Headers.TryAddWithoutValidation(key, value))
                    content?.Headers.TryAddWithoutValidation(key, value);
            }
        }
        request.Content = content;
        return request;
    }

    /// <summary>
    /// X の API にリクエストを送ります。ハンドシェイク、<c>X-Client-Transaction-Id</c> の付与、
    /// Cookie の整理、エラーコードから例外への変換を行います。
    /// </summary>
    public async Task<ApiResponse> RequestAsync(HttpMethod method, string url, RequestOptions? options = null)
    {
        options ??= new RequestOptions();
        var headers = options.Headers is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(options.Headers);

        if (!ClientTransaction.IsInited())
        {
            var cookiesBackup = GetCookies();
            var ctHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = $"{Language},{Language.Split('-')[0]};q=0.9",
                ["Cache-Control"] = "no-cache",
                ["Referer"] = $"https://{Constants.Domain}",
                ["User-Agent"] = UserAgent,
            };
            await ClientTransaction.InitAsync(this, ctHeaders).ConfigureAwait(false);
            // Internal restore: must not invalidate the handshake we just did.
            RestoreCookiesInternal(cookiesBackup);
        }

        var path = new Uri(url).AbsolutePath;
        headers["X-Client-Transaction-Id"] = ClientTransaction.GenerateTransactionId(method.Method, path);

        var sendOptions = CloneWithHeaders(options, headers);
        var cookiesBackup2 = GetCookies();
        var response = await SendAsync(method, url, sendOptions).ConfigureAwait(false);
        var apiResponse = await CaptureResponseAsync(response).ConfigureAwait(false);

        if (apiResponse.Json.Get("errors") is JsonArray errors && errors.Count > 0)
        {
            var firstError = errors[0] as JsonObject ?? new JsonObject();
            var errorCode = firstError.Int("code");
            var errorMessage = firstError.Str("message");
            // X reuses code 37 for plain authorization refusals - being told
            // you cannot use bookmark collections is not a suspension - so the
            // message has to agree before reporting one.
            if (errorCode is 37 or 64 && (errorMessage ?? "").Contains("suspend", StringComparison.OrdinalIgnoreCase))
                throw new AccountSuspendedException(errorMessage);

            if (errorCode == 326)
            {
                // Account unlocking
                if (CaptchaSolver is null)
                {
                    throw new AccountLockedException(
                        $"Your account is locked. Visit https://{Constants.Domain}/account/access to unlock it.");
                }
                if (options.AutoUnlock)
                {
                    await UnlockAsync().ConfigureAwait(false);
                    RestoreCookiesInternal(cookiesBackup2);
                    // The id has to be minted again: it encodes a timestamp and X
                    // enforces the header selectively.
                    headers["X-Client-Transaction-Id"] = ClientTransaction.GenerateTransactionId(method.Method, path);
                    response = await SendAsync(method, url, CloneWithHeaders(options, headers)).ConfigureAwait(false);
                    apiResponse = await CaptureResponseAsync(response).ConfigureAwait(false);
                }
            }
        }

        var statusCode = (int)response.StatusCode;
        if (statusCode >= 400 && options.RaiseException)
        {
            var message = $"status: {statusCode}, message: \"{apiResponse.Text}\"";
            var responseHeaders = TwitterException.HeadersOf(response);
            switch (statusCode)
            {
                case 400: throw new BadRequestException(message, responseHeaders);
                case 401: throw new UnauthorizedException(message, responseHeaders);
                case 403: throw new ForbiddenException(message, responseHeaders);
                case 404: throw new NotFoundException(message, responseHeaders);
                case 408: throw new RequestTimeoutException(message, responseHeaders);
                case 429:
                    // CheckUserState=false when called recursively from GetUserStateAsync itself.
                    if (options.CheckUserState && await GetUserStateAsync().ConfigureAwait(false) == "suspended")
                        throw new AccountSuspendedException(message, responseHeaders);
                    throw new TooManyRequestsException(message, responseHeaders);
                case >= 500 and < 600: throw new ServerErrorException(message, responseHeaders);
                default: throw new TwitterException(message, responseHeaders);
            }
        }

        return apiResponse;
    }

    private async Task<ApiResponse> CaptureResponseAsync(HttpResponseMessage response)
    {
        _responseHeaders = new Dictionary<string, string>(TwitterException.HeadersOf(response)!, StringComparer.OrdinalIgnoreCase);
        RemoveDuplicateCt0Cookie();
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new ApiResponse(response, text);
    }

    private static RequestOptions CloneWithHeaders(RequestOptions options, Dictionary<string, string> headers) => new()
    {
        Headers = headers,
        Params = options.Params,
        Json = options.Json,
        Form = options.Form,
        Files = options.Files,
        FollowRedirects = options.FollowRedirects,
        Timeout = options.Timeout,
        AutoUnlock = options.AutoUnlock,
        RaiseException = options.RaiseException,
        CheckUserState = options.CheckUserState,
    };

    public Task<ApiResponse> GetAsync(string url, RequestOptions? options = null) => RequestAsync(HttpMethod.Get, url, options);

    public Task<ApiResponse> PostAsync(string url, RequestOptions? options = null) => RequestAsync(HttpMethod.Post, url, options);

    /// <summary>直近のレスポンスに含まれる、現在のウィンドウの残りリクエスト数。</summary>
    public int? RateLimitRemaining
    {
        get
        {
            var value = _responseHeaders?.GetValueOrDefault("x-rate-limit-remaining");
            return value is not null && int.TryParse(value, out var v) ? v : null;
        }
    }

    /// <summary>現在のレート制限ウィンドウがリセットされる Unix 時刻。</summary>
    public long? RateLimitReset
    {
        get
        {
            var value = _responseHeaders?.GetValueOrDefault("x-rate-limit-reset");
            return value is not null && long.TryParse(value, out var v) ? v : null;
        }
    }

    private void RemoveDuplicateCt0Cookie()
    {
        // X answers with a ct0 pinned to the exact host it was talking to, and
        // those host-scoped copies are the duplicates worth losing. The
        // .x.com / .twitter.com ones are the copies this client writes deliberately.
        foreach (Cookie cookie in Cookies.GetAllCookies())
        {
            if (cookie.Name != "ct0" || Constants.CookieDomains.Contains(cookie.Domain)) continue;
            cookie.Expired = true;
        }
    }

    /// <summary>現在のセッションの Cookie から CSRF トークン（ct0）を取り出します。</summary>
    public string? GetCsrfToken() => GetCookies().GetValueOrDefault("ct0");

    /// <summary>Twitter API リクエストの基本ヘッダー（呼び出しごとに新しい辞書）。</summary>
    public Dictionary<string, string> BaseHeaders
    {
        get
        {
            var headers = new Dictionary<string, string>
            {
                ["authorization"] = $"Bearer {_token}",
                ["content-type"] = "application/json",
                ["X-Twitter-Auth-Type"] = "OAuth2Session",
                ["X-Twitter-Active-User"] = "yes",
                ["Referer"] = $"https://{Constants.Domain}/",
                ["User-Agent"] = UserAgent,
            };
            if (Language is not null)
            {
                headers["Accept-Language"] = Language;
                headers["X-Twitter-Client-Language"] = Language;
            }
            var csrfToken = GetCsrfToken();
            if (csrfToken is not null) headers["X-Csrf-Token"] = csrfToken;
            if (_actAs is not null) headers["X-Act-As-User-Id"] = _actAs;
            return headers;
        }
    }

    // --------------------------------------------------------------- cookies

    /// <summary>
    /// Cookie を名前と値の辞書として取得します。<see cref="SetCookies(IDictionary{string, string}, bool)"/>
    /// に渡せばログイン手続きを省略できます。
    /// </summary>
    public Dictionary<string, string> GetCookies()
    {
        var cookies = new Dictionary<string, string>();
        foreach (Cookie cookie in Cookies.GetAllCookies())
        {
            if (cookie.Expired) continue;
            cookies[cookie.Name] = cookie.Value;
        }
        return cookies;
    }

    /// <summary>Cookie を JSON ファイルに保存します。</summary>
    public void SaveCookies(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(GetCookies()), new UTF8Encoding(false));
    }

    private void SetCookie(string name, string value)
    {
        // X answers with Set-Cookie scoped to the exact host, so after a few
        // calls the jar holds auth_token under x.com, api.x.com, abs.twimg.com
        // *and* .x.com. Drop every copy first, so rotating cookies never leaves
        // the previous account's auth_token travelling with the new ct0.
        foreach (Cookie cookie in Cookies.GetAllCookies())
            if (cookie.Name == name) cookie.Expired = true;
        foreach (var domain in Constants.CookieDomains)
        {
            try
            {
                Cookies.Add(new Cookie(name, value, "/", domain));
            }
            catch (CookieException e)
            {
                TwikitWarnings.Warn($"Could not store cookie '{name}': {e.Message}");
            }
        }
    }

    private void ClearCookies()
    {
        foreach (Cookie cookie in Cookies.GetAllCookies()) cookie.Expired = true;
    }

    private void RestoreCookiesInternal(IDictionary<string, string> cookies)
    {
        ClearCookies();
        foreach (var (key, value) in cookies) SetCookie(key, value);
    }

    /// <summary>
    /// Cookie を設定します。<c>auth_token</c> と <c>ct0</c> だけで十分です。
    /// </summary>
    /// <param name="cookies">名前と値のペア。</param>
    /// <param name="clearCookies">既存の Cookie を先に消すか。</param>
    public void SetCookies(IDictionary<string, string> cookies, bool clearCookies = false)
    {
        if (clearCookies) ClearCookies();
        // Updating from a plain dict produces cookies with no domain, and those
        // would be sent to *every* host. Pin them to X.
        foreach (var (key, value) in cookies) SetCookie(key, value);
        // Without a ct0, the first write request answers 403 code 353. A
        // self-generated one clears the check at exactly 32 hex chars.
        if (!cookies.ContainsKey("ct0") && string.IsNullOrEmpty(GetCookies().GetValueOrDefault("ct0")))
            SetCookie("ct0", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        // UserIdAsync() memoises the account, so rotating to another set of
        // cookies must forget it. The transaction keys are tied to the session
        // that fetched them, so they have to go as well.
        _userId = null;
        ClientTransaction.Reset();
    }

    /// <summary>
    /// Cookie を設定します。名前と値のオブジェクト、またはブラウザ拡張 / Playwright が書き出す
    /// 形式（<c>name</c> と <c>value</c> を持つオブジェクトの配列）を受け付けます。
    /// </summary>
    public void SetCookies(JsonNode cookies, bool clearCookies = false)
    {
        var dict = new Dictionary<string, string>();
        if (cookies is JsonArray list)
        {
            foreach (var c in list.Objects())
            {
                var name = c.Str("name");
                var value = c.Str("value");
                if (name is not null && value is not null) dict[name] = value;
            }
        }
        else if (cookies is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                var value = kv.Value.AsStr();
                if (value is not null) dict[kv.Key] = value;
            }
        }
        SetCookies(dict, clearCookies);
    }

    /// <summary>JSON ファイルから Cookie を読み込みます。</summary>
    public void LoadCookies(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path)) ?? throw new JsonException("The cookie file is empty.");
        SetCookies(node);
    }

    /// <summary>
    /// 代理操作するアカウントを設定します（null で解除）。
    /// </summary>
    /// <remarks>
    /// X は内部 API の一部でしか委任を認めません。拒否するエンドポイントは 403 code 90
    /// "Contributor access is not permitted on this endpoint" を返します（<see cref="UserAsync"/> もその 1 つ）。
    /// </remarks>
    public void SetDelegateAccount(string? userId) => _actAs = userId;

    /// <summary>
    /// X-Client-Transaction-Id のハンドシェイクを強制的にやり直します。
    /// 長時間動かすプロセスで散発的な 404 が出始めたときの安価な対処です。
    /// </summary>
    public void RefreshTransaction() => ClientTransaction.Reset();

    // ------------------------------------------------------------- account

    private async Task<string> GetGuestTokenAsync()
    {
        var response = await V11.GuestActivateAsync().ConfigureAwait(false);
        return response.Object.Str("guest_token") ?? throw new TwitterException("guest/activate returned no guest_token.");
    }

    /// <summary>ui_metrics チャレンジのスクリプトを取得します（twitter.com のまま）。</summary>
    public async Task<string> UiMetricsAsync()
    {
        var response = await GetAsync("https://twitter.com/i/js_inst?c_name=ui_metrics").ConfigureAwait(false);
        return response.Text;
    }

    /// <summary>パスワードログインが X に対して動作しない理由。</summary>
    public const string LoginRetiredMessage =
        "X no longer serves the LoginFlow onboarding task this method drives; " +
        "it answers code 366, \"flow name LoginFlow is currently not accessible\". " +
        "Measured against live x.com: /i/flow/login redirects to /i/jf/onboarding/web and the site now posts to " +
        "/i/jfapi/onboarding/web/actions/begin_login, which requires a ~5 KB $castle_token produced by obfuscated " +
        "in-page JavaScript, and offers passkey/WebAuthn as a first-factor path. None of that is reachable from a " +
        "plain HTTP client. Authenticate with cookies instead - see Client.SetCookies and Client.LoadCookies.";

    /// <summary>
    /// アカウントにログインします。
    /// </summary>
    /// <remarks>
    /// <b>これはもう動作しません。</b> X はこのメソッドが使うオンボーディングフローを廃止しており、
    /// code 366 "flow name LoginFlow is currently not accessible" を返します。代わりに
    /// <see cref="SetCookies(IDictionary{string, string}, bool)"/> または <see cref="LoadCookies"/> を使ってください。
    /// </remarks>
    /// <param name="authInfo1">ユーザー名、メールアドレス、電話番号のいずれか。</param>
    /// <param name="password">パスワード。</param>
    /// <param name="authInfo2">2 つ目の認証情報（任意、推奨）。</param>
    /// <param name="totpSecret">2 段階認証の TOTP シークレット。</param>
    /// <param name="cookiesFile">Cookie を保存・読み込みするファイル。存在すればログインを省略します。</param>
    /// <param name="enableUiMetrics">難読化された ui_metrics を評価して送るか。</param>
    /// <param name="codeCallback">確認コードや 2FA コードを求められたときに呼ばれるコールバック（既定は標準入力）。</param>
    public async Task<JsonObject?> LoginAsync(
        string authInfo1,
        string password,
        string? authInfo2 = null,
        string? totpSecret = null,
        string? cookiesFile = null,
        bool enableUiMetrics = true,
        Func<string, Task<string>>? codeCallback = null)
    {
        ClearCookies();

        if (cookiesFile is not null && File.Exists(cookiesFile))
        {
            LoadCookies(cookiesFile);
            return null;
        }

        string guestToken;
        try
        {
            guestToken = await GetGuestTokenAsync().ConfigureAwait(false);
        }
        catch (ClientTransactionException e)
        {
            // The handshake needs a logged-in home page; while logging in there
            // are no cookies yet, so it reports "refresh your cookies", which is
            // nonsense advice in this context.
            throw new LoginRetiredException(LoginRetiredMessage, e);
        }

        var flow = new Flow(this, guestToken);

        var subtaskVersions = new JsonObject
        {
            ["action_list"] = 2, ["alert_dialog"] = 1, ["app_download_cta"] = 1, ["check_logged_in_account"] = 1,
            ["choice_selection"] = 3, ["contacts_live_sync_permission_prompt"] = 0, ["cta"] = 7, ["email_verification"] = 2,
            ["end_flow"] = 1, ["enter_date"] = 1, ["enter_email"] = 2, ["enter_password"] = 5, ["enter_phone"] = 2,
            ["enter_recaptcha"] = 1, ["enter_text"] = 5, ["enter_username"] = 2, ["generic_urt"] = 3, ["in_app_notification"] = 1,
            ["interest_picker"] = 3, ["js_instrumentation"] = 1, ["menu_dialog"] = 1, ["notifications_permission_prompt"] = 2,
            ["open_account"] = 2, ["open_home_timeline"] = 1, ["open_link"] = 1, ["phone_verification"] = 4, ["privacy_options"] = 1,
            ["security_key"] = 3, ["select_avatar"] = 4, ["select_banner"] = 2, ["settings_list"] = 7, ["show_code"] = 1,
            ["sign_up"] = 2, ["sign_up_review"] = 4, ["tweet_selection_urt"] = 1, ["update_users"] = 1, ["upload_media"] = 1,
            ["user_recommendations_list"] = 4, ["user_recommendations_urt"] = 1, ["wait_spinner"] = 3, ["web_modal"] = 1,
        };
        await flow.ExecuteTaskAsync(null, new Dictionary<string, string> { ["flow_name"] = "login" }, new JsonObject
        {
            ["input_flow_data"] = new JsonObject
            {
                ["flow_context"] = new JsonObject
                {
                    ["debug_overrides"] = new JsonObject(),
                    ["start_location"] = new JsonObject { ["location"] = "splash_screen" },
                },
            },
            ["subtask_versions"] = subtaskVersions,
        }).ConfigureAwait(false);
        await flow.SsoInitAsync("apple").ConfigureAwait(false);

        var uiMetricsResponse = enableUiMetrics
            ? UiMetricsSolver.Solve(await UiMetricsAsync().ConfigureAwait(false))
            : "";

        await flow.ExecuteTaskAsync(new JsonObject
        {
            ["subtask_id"] = "LoginJsInstrumentationSubtask",
            ["js_instrumentation"] = new JsonObject { ["response"] = uiMetricsResponse, ["link"] = "next_link" },
        }).ConfigureAwait(false);
        await flow.ExecuteTaskAsync(new JsonObject
        {
            ["subtask_id"] = "LoginEnterUserIdentifierSSO",
            ["settings_list"] = new JsonObject
            {
                ["setting_responses"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["key"] = "user_identifier",
                        ["response_data"] = new JsonObject { ["text_data"] = new JsonObject { ["result"] = authInfo1 } },
                    },
                },
                ["link"] = "next_link",
            },
        }).ConfigureAwait(false);

        if (flow.TaskId == "LoginEnterAlternateIdentifierSubtask")
        {
            await flow.ExecuteTaskAsync(new JsonObject
            {
                ["subtask_id"] = "LoginEnterAlternateIdentifierSubtask",
                ["enter_text"] = new JsonObject { ["text"] = authInfo2, ["link"] = "next_link" },
            }).ConfigureAwait(false);
        }

        if (flow.TaskId == "DenyLoginSubtask") throw new TwitterException(DenyLoginText(flow));

        await flow.ExecuteTaskAsync(new JsonObject
        {
            ["subtask_id"] = "LoginEnterPassword",
            ["enter_password"] = new JsonObject { ["password"] = password, ["link"] = "next_link" },
        }).ConfigureAwait(false);

        if (flow.TaskId == "DenyLoginSubtask") throw new TwitterException(DenyLoginText(flow));

        // X hands out LoginAcid and LoginTwoFactorAuthChallenge in whatever
        // order it likes, and an account can be asked for both. Keep consuming
        // challenges until none is left.
        for (var i = 0; i < 4; i++)
        {
            if (flow.TaskId == "LoginAcid")
            {
                var prompt = flow.Response.FirstDict("secondary_text").Str("text") ?? "";
                var code = await AskLoginCodeAsync(codeCallback, prompt).ConfigureAwait(false);
                await flow.ExecuteTaskAsync(new JsonObject
                {
                    ["subtask_id"] = "LoginAcid",
                    ["enter_text"] = new JsonObject { ["text"] = code, ["link"] = "next_link" },
                }).ConfigureAwait(false);
            }
            else if (flow.TaskId == "LoginTwoFactorAuthChallenge")
            {
                string totpCode;
                if (totpSecret is null)
                {
                    var prompt = flow.Response.FirstDict("secondary_text").Str("text") ?? "";
                    totpCode = await AskLoginCodeAsync(codeCallback, prompt).ConfigureAwait(false);
                }
                else
                {
                    totpCode = Totp.Now(totpSecret);
                }
                await flow.ExecuteTaskAsync(new JsonObject
                {
                    ["subtask_id"] = "LoginTwoFactorAuthChallenge",
                    ["enter_text"] = new JsonObject { ["text"] = totpCode, ["link"] = "next_link" },
                }).ConfigureAwait(false);
            }
            else
            {
                break;
            }

            if (flow.TaskId == "DenyLoginSubtask") throw new TwitterException(DenyLoginText(flow));
        }

        if (flow.TaskId == "AccountDuplicationCheck")
        {
            await flow.ExecuteTaskAsync(new JsonObject
            {
                ["subtask_id"] = "AccountDuplicationCheck",
                ["check_logged_in_account"] = new JsonObject { ["link"] = "AccountDuplicationCheck_false" },
            }).ConfigureAwait(false);
        }

        if (cookiesFile is not null) SaveCookies(cookiesFile);

        if (flow.Response?.Arr("subtasks") is not { Count: > 0 }) return flow.Response;

        var userId = flow.Response.FirstDict("id_str").AsStr();
        if (userId is null)
            throw new TwitterException($"Login did not complete - X returned no account. Last subtask: {flow.TaskId}");
        _userId = userId;
        return flow.Response;
    }

    private static string DenyLoginText(Flow flow)
        => flow.Response.Arr("subtasks").At(0).Sub("cta").Sub("secondary_text").Str("text") ?? "Login denied.";

    private static async Task<string> AskLoginCodeAsync(Func<string, Task<string>>? callback, string prompt)
    {
        if (callback is null)
        {
            Console.WriteLine(prompt);
            Console.Write(">>> ");
            return Console.ReadLine() ?? "";
        }
        return await callback(prompt).ConfigureAwait(false);
    }

    /// <summary>ログアウトします。</summary>
    public async Task<ApiResponse> LogoutAsync() => await V11.AccountLogoutAsync().ConfigureAwait(false);

    /// <summary>
    /// 指定した captcha ソルバーでアカウントのロックを解除します。
    /// </summary>
    public async Task UnlockAsync()
    {
        if (CaptchaSolver is null) throw new InvalidOperationException("Captcha solver is not provided.");

        var (response, html) = await CaptchaSolver.GetUnlockHtmlAsync().ConfigureAwait(false);

        if (html.DeleteButton)
            (response, html) = await CaptchaSolver.ConfirmUnlockAsync(html.AuthenticityToken, html.AssignmentToken, uiMetrics: true).ConfigureAwait(false);

        if (html.StartButton || html.FinishButton)
            (response, html) = await CaptchaSolver.ConfirmUnlockAsync(html.AuthenticityToken, html.AssignmentToken, uiMetrics: true).ConfigureAwait(false);

        var cookiesBackup = GetCookies();
        var maxUnlockAttempts = CaptchaSolver.MaxAttempts;
        var attempt = 0;
        while (attempt < maxUnlockAttempts)
        {
            attempt++;

            if (html.AuthenticityToken is null)
                (response, html) = await CaptchaSolver.GetUnlockHtmlAsync().ConfigureAwait(false);

            var result = await CaptchaSolver.SolveFunCaptchaAsync(html.Blob).ConfigureAwait(false);
            if (result.Int("errorId") == 1) continue;

            RestoreCookiesInternal(cookiesBackup);
            (response, html) = await CaptchaSolver.ConfirmUnlockAsync(
                html.AuthenticityToken, html.AssignmentToken, result.Sub("solution").Str("token")).ConfigureAwait(false);

            if (html.FinishButton)
                (response, html) = await CaptchaSolver.ConfirmUnlockAsync(html.AuthenticityToken, html.AssignmentToken, uiMetrics: true).ConfigureAwait(false);

            // Read the redirect target off the header, which every transport carries.
            var location = response.Headers.Location?.ToString() ?? "";
            string locationPath;
            try { locationPath = new Uri(new Uri($"https://{Constants.Domain}/"), location).AbsolutePath; }
            catch (UriFormatException) { locationPath = location; }
            if (locationPath is "/" or "/home") return;
        }
        throw new TwitterException("Could not unlock the account.");
    }

    /// <summary>認証中のアカウントのユーザー ID を取得します（初回だけ問い合わせ、以後は記憶します）。</summary>
    public async Task<string> UserIdAsync()
    {
        if (_userId is not null) return _userId;
        var response = await V11.SettingsAsync().ConfigureAwait(false);
        var screenName = response.Object.Str("screen_name") ?? throw new TwitterException("account/settings returned no screen_name.");
        _userId = (await GetUserByScreenNameAsync(screenName).ConfigureAwait(false)).Id;
        return _userId;
    }

    /// <summary>認証中のユーザーの詳細情報を取得します。</summary>
    public async Task<User> UserAsync() => await GetUserByIdAsync(await UserIdAsync().ConfigureAwait(false)).ConfigureAwait(false);

    /// <summary>
    /// 現在の Cookie でまだアカウントを認証できるかを確認します。
    /// </summary>
    /// <remarks>
    /// X がセッションをまだ受け付けているかだけを報告します。ロック・凍結されたアカウントも false になります。
    /// </remarks>
    public async Task<bool> IsLoggedInAsync()
    {
        ApiResponse response;
        try
        {
            response = await V11.SettingsAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is UnauthorizedException or ForbiddenException or AccountLockedException
                                      or AccountSuspendedException or ClientTransactionException)
        {
            return false;
        }
        // A 200 that is not JSON (a Cloudflare interstitial, most often) comes
        // back as a bare string.
        if (response.Object is null) return false;
        return !string.IsNullOrEmpty(response.Object.Str("screen_name"));
    }

    internal async Task<string> GetUserStateAsync()
    {
        // RequestAsync() calls this method whenever it receives a 429, to
        // decide between TooManyRequests and AccountSuspended. Pass
        // CheckUserState=false to the nested request so that if this
        // user_state GET also 429s, RequestAsync raises TooManyRequests
        // directly instead of re-entering this branch.
        try
        {
            var response = await V11.UserStateAsync(checkUserState: false).ConfigureAwait(false);
            return response.Object.Str("userState") ?? "normal";
        }
        catch (Exception e) when (e is TooManyRequestsException or HttpRequestException)
        {
            return "normal";
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) Http.Dispose();
        GC.SuppressFinalize(this);
    }
}
