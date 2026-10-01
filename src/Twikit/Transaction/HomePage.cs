using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Twikit.Api;

namespace Twikit.Transaction;

/// <summary>
/// ハンドシェイクが読む x.com のホームページ（解析済み DOM と生の HTML）。
/// </summary>
public sealed class HomePage
{
    /// <summary>解析済みの DOM。</summary>
    public IDocument Document { get; }

    /// <summary>生の HTML テキスト。</summary>
    public string Text { get; }

    public HomePage(IDocument document, string text)
    {
        Document = document;
        Text = text;
    }

    private static readonly HtmlParser Parser = new();

    public static HomePage Parse(string html) => new(Parser.ParseDocument(html), html);

    private static readonly Regex MigrationRedirectionRegex = new(
        @"(http(?:s)?://(?:www\.)?(twitter|x){1}\.com(/x)?/migrate([/?])?tok=[a-zA-Z0-9%\-_]+)+",
        RegexOptions.Compiled);

    /// <summary>
    /// x.com を取得し、twitter.com → x.com の移行リダイレクト（meta refresh / 移行フォーム）を
    /// 追ってホームページを返します（Python 版の <c>handle_x_migration</c>）。
    /// </summary>
    public static async Task<HomePage> HandleXMigrationAsync(IRawHttpSession session, Dictionary<string, string> headers)
    {
        var response = await session.RequestAsync(HttpMethod.Get, "https://x.com", headers).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var page = Parse(html);

        var migrationMeta = page.Document.QuerySelector("meta[http-equiv='refresh']");
        var match = MigrationRedirectionRegex.Match(migrationMeta?.OuterHtml ?? "");
        if (!match.Success) match = MigrationRedirectionRegex.Match(html);
        if (match.Success)
        {
            response = await session.RequestAsync(HttpMethod.Get, match.Value, headers).ConfigureAwait(false);
            html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            page = Parse(html);
        }

        var migrationForm = page.Document.QuerySelector("form[name='f']")
                            ?? page.Document.QuerySelector("form[action='https://x.com/x/migrate']");
        if (migrationForm is not null)
        {
            var url = (migrationForm.GetAttribute("action") ?? "https://x.com/x/migrate") + "/?mx=2";
            var method = migrationForm.GetAttribute("method") ?? "POST";
            var payload = new Dictionary<string, string>();
            foreach (var input in migrationForm.QuerySelectorAll("input"))
            {
                var name = input.GetAttribute("name");
                if (name is null) continue;
                payload[name] = input.GetAttribute("value") ?? "";
            }
            response = await session.RequestAsync(new HttpMethod(method.ToUpperInvariant()), url, headers, payload).ConfigureAwait(false);
            html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            page = Parse(html);
        }
        return page;
    }
}
