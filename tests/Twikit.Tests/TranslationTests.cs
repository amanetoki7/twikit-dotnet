using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Twikit.Tests;

/// <summary>
/// Grok 翻訳（<c>api.x.com/2/grok/translation.json</c>）。応答は改行区切りの JSON で、初回はトークンごと、
/// キャッシュ済みなら全文 1 行（2026-10-26 の HAR とライブ確認から）。
/// </summary>
public class TranslationTests
{
    private const string Streamed =
        "{\"result\":{\"content_type\":\"POST\",\"text\":\"Lu\"}}\n" +
        "{\"result\":{\"content_type\":\"POST\",\"text\":\"ott\"}}\n" +
        "{\"result\":{\"content_type\":\"POST\",\"text\":\"amus\"}}\n" +
        "{\"result\":{\"content_type\":\"POST\",\"text\":\" 😭\"}}\n";

    private const string Cached =
        "{\"result\":{\"content_type\":\"POST\",\"text\":\"私は完全にあなたの虜です。\",\"entities\":{}}}\n";

    [Fact]
    public void StreamedLinesAreConcatenated()
    {
        var t = new TweetTranslation("1", "fi", Streamed);
        Assert.Equal("Luottamus 😭", t.Text);
        Assert.False(t.Cached);
        Assert.Equal("POST", t.ContentType);
        Assert.Empty(t.Entities);
        Assert.Equal("<TweetTranslation tweet_id=\"1\" dst_lang=\"fi\">", t.ToString());
    }

    [Fact]
    public void CachedSingleLineIsWholeText()
    {
        var t = new TweetTranslation("1", "ja", Cached);
        Assert.Equal("私は完全にあなたの虜です。", t.Text);
        Assert.True(t.Cached);
    }

    [Fact]
    public async Task PostsJsonBodyAndDefaultsToClientLanguage()
    {
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath == "/2/grok/translation.json")
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("api.x.com", request.RequestUri.Host);
                var json = JsonNode.Parse(body!)!;
                Assert.Equal("POST", json["content_type"]!.GetValue<string>());
                Assert.Equal("2114550616811094253", json["id"]!.GetValue<string>());
                var lang = json["dst_lang"]!.GetValue<string>();
                return FakeHandler.Text(lang == "ja" ? Cached : Streamed);
            }
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);   // language "ja"

        var byDefault = await client.TranslateTweetAsync("2114550616811094253");
        Assert.Equal("ja", byDefault.DstLang);
        Assert.Equal("私は完全にあなたの虜です。", byDefault.Text);

        var explicitLang = await client.TranslateTweetAsync("2114550616811094253", "fi");
        Assert.Equal("fi", explicitLang.DstLang);
        Assert.Equal("Luottamus 😭", explicitLang.Text);
        Assert.False(explicitLang.Cached);
    }

    [Fact]
    public async Task UnknownTweetIdThrowsServerError()
    {
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/2/grok/translation.json")
                return FakeHandler.Json("""{"errors":[{"message":"Internal error","code":131}]}""", HttpStatusCode.InternalServerError);
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        await Assert.ThrowsAsync<ServerErrorException>(() => client.TranslateTweetAsync("1", "ja"));
    }
}
