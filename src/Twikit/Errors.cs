using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// twikit が送出する例外の基底クラス。
/// </summary>
/// <remarks>
/// <para>
/// X は HTTP ステータスに加えて本文中に数値の <c>code</c> を返し、実際に何が
/// 起きたかを伝えるのはそちらです。実際に観測したコードの一覧:
/// </para>
/// <list type="table">
/// <listheader><term>code</term><description>意味</description></listheader>
/// <item><term>32</term><description>認証情報が不正または期限切れ。Cookie が死んでいるときの典型。</description></item>
/// <item><term>34</term><description>エンドポイントが存在しない。v1.1 のいくつかがこれを返すようになった。</description></item>
/// <item><term>37</term><description>このリソースへの権限が無い。文言に "suspend" が無い限り凍結ではない。</description></item>
/// <item><term>63</term><description>アカウントが凍結されている。</description></item>
/// <item><term>64</term><description>認証中のアカウントが凍結されている。</description></item>
/// <item><term>88</term><description>レート制限超過。<see cref="Client.RateLimitReset"/> を参照。</description></item>
/// <item><term>89</term><description>トークンが不正または期限切れ。</description></item>
/// <item><term>139</term><description>すでにいいね済み。</description></item>
/// <item><term>144</term><description>その ID のツイートは存在しない。</description></item>
/// <item><term>179</term><description>このツイートを見る権限が無い（鍵アカウント）。</description></item>
/// <item><term>187</term><description>重複ツイート。同じ本文を直近に投稿している。</description></item>
/// <item><term>226</term><description>自動化されたリクエストと判定された。アカウント単位のスパムスコア。</description></item>
/// <item><term>279</term><description>DM の会話が存在しない。</description></item>
/// <item><term>326</term><description>アカウントが一時的にロックされている。<see cref="AccountLockedException"/> を参照。</description></item>
/// <item><term>349</term><description>このユーザーにはメッセージを送れない（フォロー外からの DM を受け付けていない）。</description></item>
/// <item><term>398</term><description>人間であることを確認できない（ログイン時の Arkose/FunCaptcha）。</description></item>
/// <item><term>399</term><description>castle トークンが必要なログインチャレンジ。</description></item>
/// </list>
/// <para>
/// 同じ呼び出しで出たり出なかったりする 404 は、ほぼ例外なくエンドポイントではなく
/// <c>x-client-transaction-id</c> のハンドシェイクが原因です。X はこのヘッダーを
/// 選択的に検証するため、古いキーで作った ID は通る操作と弾かれる操作があります。
/// キーの元になる <c>ondemand.s</c> バンドルは数日ごとに入れ替わるので、長時間動かす
/// プロセスは散発的な 404 に劣化していきます。<see cref="Client.RefreshTransaction"/>
/// でハンドシェイクをやり直すと解消します。
/// </para>
/// </remarks>
public class TwitterException : Exception
{
    /// <summary>例外の元になったレスポンスのヘッダー（無い場合は null）。</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; }

    public TwitterException() : this(null, null, null) { }

    public TwitterException(string? message) : this(message, null, null) { }

    public TwitterException(string? message, Exception? innerException) : this(message, null, innerException) { }

    public TwitterException(string? message, IReadOnlyDictionary<string, string>? headers, Exception? innerException = null)
        : base(message, innerException)
    {
        Headers = headers is null ? null : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlyDictionary<string, string>? HeadersOf(HttpResponseMessage? response)
    {
        if (response is null) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers) dict[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers) dict[h.Key] = string.Join(", ", h.Value);
        return dict;
    }
}

/// <summary>400 Bad Request。</summary>
public class BadRequestException : TwitterException
{
    public BadRequestException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>401 Unauthorized。</summary>
public class UnauthorizedException : TwitterException
{
    public UnauthorizedException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>403 Forbidden。</summary>
public class ForbiddenException : TwitterException
{
    public ForbiddenException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>404 Not Found。存在しないリスト・コミュニティ・ノートなどにも使われます。</summary>
public class NotFoundException : TwitterException
{
    public NotFoundException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>408 Request Timeout。</summary>
public class RequestTimeoutException : TwitterException
{
    public RequestTimeoutException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>429 Too Many Requests。</summary>
public class TooManyRequestsException : TwitterException
{
    /// <summary>レート制限枠がリセットされる Unix 時刻（ヘッダーに無ければ null）。</summary>
    public long? RateLimitReset { get; }

    public TooManyRequestsException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers)
    {
        if (headers is not null && headers.TryGetValue("x-rate-limit-reset", out var reset) && long.TryParse(reset, out var value))
            RateLimitReset = value;
    }
}

/// <summary>5xx サーバーエラー。</summary>
public class ServerErrorException : TwitterException
{
    public ServerErrorException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>ツイートを送信できなかった。</summary>
public class CouldNotTweetException : TwitterException
{
    public CouldNotTweetException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>重複ツイート。</summary>
public class DuplicateTweetException : CouldNotTweetException
{
    public DuplicateTweetException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>ツイートが利用できない（削除済み・非公開など）。</summary>
public class TweetNotAvailableException : TwitterException
{
    public TweetNotAvailableException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>ツイートに添付したメディア ID に問題がある。</summary>
public class InvalidMediaException : TwitterException
{
    public InvalidMediaException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>ユーザーが存在しない。</summary>
public class UserNotFoundException : TwitterException
{
    public UserNotFoundException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>ユーザーが利用できない（鍵・凍結・削除済みなど）。</summary>
public class UserUnavailableException : TwitterException
{
    public UserUnavailableException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>アカウントが凍結されている。</summary>
public class AccountSuspendedException : TwitterException
{
    public AccountSuspendedException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>アカウントがロックされている（多くの場合 Arkose チャレンジ）。</summary>
public class AccountLockedException : TwitterException
{
    public AccountLockedException(string? message = null, IReadOnlyDictionary<string, string>? headers = null) : base(message, headers) { }
}

/// <summary>X-Client-Transaction-Id のハンドシェイクを完了できない。</summary>
public class ClientTransactionException : TwitterException
{
    public ClientTransactionException(string? message = null, IReadOnlyDictionary<string, string>? headers = null, Exception? inner = null) : base(message, headers, inner) { }
}

/// <summary>
/// パスワードログインを試みたときに送出されます。
/// </summary>
/// <remarks>
/// X は <see cref="Client.LoginAsync"/> が使うオンボーディングフローを廃止しました。
/// ハンドシェイクは単に最初に失敗する場所に過ぎないため、<see cref="ClientTransactionException"/>
/// とは別の型にしています。ハンドシェイクの再試行のために捕捉している側が
/// 「パスワードログインはもう存在しない」を握りつぶさないようにするためです。
/// </remarks>
public class LoginRetiredException : TwitterException
{
    public LoginRetiredException(string? message = null, Exception? inner = null) : base(message, null, inner) { }
}

/// <summary>
/// x.com が未ログインのページの殻を返した。Cookie が無い・期限切れ・拒否されたことを意味します。
/// </summary>
public class InvalidSessionException : ClientTransactionException
{
    public InvalidSessionException(string? message = null, Exception? inner = null) : base(message, null, inner) { }
}

/// <summary>X のエラーコードと例外の対応。</summary>
public static class ErrorCodes
{
    /// <summary>
    /// GraphQL の <c>errors</c> 配列を走査し、対応する例外があれば送出します
    /// （187 → <see cref="DuplicateTweetException"/>、324 → <see cref="InvalidMediaException"/>）。
    /// </summary>
    public static void RaiseExceptionsFromResponse(IEnumerable<JsonNode?> errors)
    {
        foreach (var error in errors)
        {
            if (error is not JsonObject e) continue;
            var code = e["code"].AsLong();
            if (code is not (187 or 324))
                code = e["extensions"]?["code"].AsLong();
            var message = e["message"].AsStr();
            switch (code)
            {
                case 187: throw new DuplicateTweetException(message);
                case 324: throw new InvalidMediaException(message);
            }
        }
    }
}
