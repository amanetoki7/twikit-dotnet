using System.Text.Json.Nodes;

namespace Twikit.Api;

/// <summary>multipart/form-data の 1 パート。</summary>
/// <param name="Name">フォームのフィールド名。</param>
/// <param name="FileName">ファイル名。</param>
/// <param name="Content">内容。</param>
/// <param name="ContentType">Content-Type（省略時は application/octet-stream）。</param>
public sealed record MultipartFile(string Name, string FileName, byte[] Content, string? ContentType = null);

/// <summary>
/// 1 回のリクエストに付けるオプション（Python 版 httpx の <c>params=</c> / <c>json=</c> / <c>data=</c> / <c>files=</c> に相当）。
/// </summary>
public sealed class RequestOptions
{
    /// <summary>追加ヘッダー。</summary>
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>クエリ文字列。</summary>
    public Dictionary<string, string>? Params { get; set; }
    /// <summary>JSON ボディ。</summary>
    public JsonNode? Json { get; set; }
    /// <summary>application/x-www-form-urlencoded ボディ。</summary>
    public Dictionary<string, string>? Form { get; set; }
    /// <summary>multipart/form-data のファイル。</summary>
    public List<MultipartFile>? Files { get; set; }
    /// <summary>リダイレクトを追うか（httpx 同様、既定では追いません）。</summary>
    public bool FollowRedirects { get; set; }
    /// <summary>このリクエストだけのタイムアウト。</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>code 326 を受けたとき captcha ソルバーで自動的にロック解除を試みるか。</summary>
    public bool AutoUnlock { get; set; } = true;
    /// <summary>4xx/5xx を例外にするか。</summary>
    public bool RaiseException { get; set; } = true;
    /// <summary>429 のとき user_state を確認して凍結と区別するか（再帰防止のため内部で false にします）。</summary>
    public bool CheckUserState { get; set; } = true;
}

/// <summary>
/// API の応答。本文は JSON として解析済み（JSON でなければ <see cref="Json"/> は null）で、
/// 生のテキストと <see cref="HttpResponseMessage"/> も参照できます。
/// </summary>
public sealed class ApiResponse
{
    public HttpResponseMessage Http { get; }
    /// <summary>本文のテキスト。</summary>
    public string Text { get; }
    /// <summary>本文を JSON として解析したもの。JSON でなければ null。</summary>
    public JsonNode? Json { get; }
    /// <summary><see cref="Json"/> をオブジェクトとして。</summary>
    public JsonObject? Object => Json as JsonObject;
    /// <summary><see cref="Json"/> を配列として。</summary>
    public JsonArray? Array => Json as JsonArray;
    public int StatusCode => (int)Http.StatusCode;

    public ApiResponse(HttpResponseMessage http, string text)
    {
        Http = http;
        Text = text;
        Json = JsonExtensions.TryParse(text);
    }
}

/// <summary>
/// GraphQL / v1.1 の各エンドポイント層が必要とする、最小限のリクエスト機能。
/// <see cref="Client"/> と <see cref="Twikit.Guest.GuestClient"/> の両方が実装します。
/// </summary>
public interface IRequestClient
{
    /// <summary>API リクエストの基本ヘッダー（呼び出しごとに新しい辞書）。</summary>
    Dictionary<string, string> BaseHeaders { get; }

    Task<ApiResponse> RequestAsync(HttpMethod method, string url, RequestOptions? options = null);

    Task<ApiResponse> GetAsync(string url, RequestOptions? options = null);

    Task<ApiResponse> PostAsync(string url, RequestOptions? options = null);

    /// <summary>Cookie 中の CSRF トークン（ct0）。無ければ null。</summary>
    string? GetCsrfToken();

    /// <summary>
    /// トランザクション ID やエラー処理を挟まない生のリクエスト（Python 版の <c>_send</c>）。
    /// メディアのダウンロードなど、X の API 以外のホストに使います。
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, RequestOptions? options = null);
}

/// <summary>
/// X-Client-Transaction-Id ハンドシェイクが使う生の HTTP セッション。
/// </summary>
public interface IRawHttpSession
{
    Task<HttpResponseMessage> RequestAsync(HttpMethod method, string url, Dictionary<string, string>? headers = null, Dictionary<string, string>? form = null);
}
