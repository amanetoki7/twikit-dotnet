using System.Net;
using Xunit;

namespace Twikit.Tests;

/// <summary>ピン留め API（twifork 47c8eff から移植）。v1.1 の form POST で、パラメータ名は `tweet_id` ではなく `id`。</summary>
public class PinTests
{
    private const string TweetJson = """{"id_str":"2103725182292820029","created_at":"Thu Oct 09 00:00:00 +0000 2026","full_text":"pinned","user":{"id_str":"1784152598318346240","screen_name":"amanetoki7"}}""";

    [Fact]
    public async Task PinAndUnpinPostFormWithIdParameter()
    {
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/1.1/account/pin_tweet.json", StringComparison.Ordinal) ||
                path.EndsWith("/1.1/account/unpin_tweet.json", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("api.x.com", request.RequestUri.Host);
                Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal("id=2103725182292820029", body);
                return FakeHandler.Json(TweetJson);
            }
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        var pinned = await client.PinTweetAsync("2103725182292820029");
        Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
        var unpinned = await client.UnpinTweetAsync("2103725182292820029");
        Assert.Equal(HttpStatusCode.OK, unpinned.StatusCode);

        var urls = handler.Requests.Select(r => r.Request.RequestUri!.AbsolutePath).Where(p => p.Contains("pin_tweet")).ToList();
        Assert.Equal(new[] { "/1.1/account/pin_tweet.json", "/1.1/account/unpin_tweet.json" }, urls);
    }

    [Fact]
    public async Task PinningSomeoneElsesTweetThrowsNotFound()
    {
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/1.1/account/pin_tweet.json", StringComparison.Ordinal))
                return FakeHandler.Json("""{"errors":[{"code":144,"message":"No status found with that ID."}]}""", HttpStatusCode.NotFound);
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        await Assert.ThrowsAsync<NotFoundException>(() => client.PinTweetAsync("1"));
    }
}
