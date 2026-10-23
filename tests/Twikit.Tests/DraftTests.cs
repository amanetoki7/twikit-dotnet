using System.Net;
using Twikit.Api;
using Xunit;

namespace Twikit.Tests;

/// <summary>下書き API。応答は 2026-10-23 の HAR（x.com_draft.har）の形。</summary>
public class DraftTests
{
    private const string Draft1 = """{"rest_id":"2113419306533089280","tweet_create_request":{"auto_populate_reply_metadata":false,"exclude_reply_user_ids":[],"media_ids":[],"status":"draft test","thread_tweets":[],"type":"PostTweetRequest"},"user_results":{"result":{"__typename":"User","rest_id":"1784152598318346240"}}}""";
    private const string Draft2 = """{"rest_id":"2112363137416728576","tweet_create_request":{"attachment_url":"https://x.com/amanetoki7/status/2112238353773162809","auto_populate_reply_metadata":false,"exclude_reply_user_ids":[],"media_ids":["1"],"status":"quote","thread_tweets":[],"type":"PostTweetRequest"},"user_results":{"result":{"__typename":"User","rest_id":"1784152598318346240"}}}""";
    // Older drafts lack exclude_reply_user_ids / thread_tweets entirely.
    private const string Draft3 = """{"rest_id":"2111781191259156480","tweet_create_request":{"auto_populate_reply_metadata":false,"media_ids":[],"status":"old","type":"PostTweetRequest"},"user_results":{"result":{"__typename":"User","rest_id":"1784152598318346240"}}}""";

    [Fact]
    public async Task ListCreateAndDeleteDrafts()
    {
        var drafts = new List<string> { Draft2, Draft3 };
        string? deleteBody = null;
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            if (path.EndsWith("/L9RqKWmAWxK6vGtR3Qdsxw/FetchDraftTweets", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("\"ascending\":false", query);
                return FakeHandler.Json("""{"data":{"viewer":{"draft_list":{"response_data":[""" + string.Join(",", drafts) + "]}}}}");
            }
            if (path.EndsWith("/cH9HZWz_EW9gnswvA4ZRiQ/CreateDraftTweet", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("\"queryId\":\"cH9HZWz_EW9gnswvA4ZRiQ\"", body);
                Assert.Contains("\"post_tweet_request\":{", body);
                Assert.Contains("\"status\":\"draft test\"", body);
                Assert.Contains("\"media_ids\":[]", body);
                Assert.Contains("\"thread_tweets\":[]", body);
                Assert.Contains("\"exclude_reply_user_ids\":[]", body);
                Assert.Contains("\"auto_populate_reply_metadata\":false", body);
                Assert.DoesNotContain("attachment_url", body);
                drafts.Insert(0, Draft1);
                return FakeHandler.Json("""{"data":{"tweet":{"rest_id":"2113419306533089280"}}}""");
            }
            if (path.EndsWith("/bkh9G3FGgTldS9iTKWWYYw/DeleteDraftTweet", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                deleteBody = body;
                drafts.RemoveAt(0);
                return FakeHandler.Json("""{"data":{"drafttweet_delete":"Done"}}""");
            }
            if (path.EndsWith("/h0rzaIYc9-v3zTQvx1J8OA/DeleteDraftTweets", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                deleteBody = body;
                drafts.Clear();
                return FakeHandler.Json("""{"data":{"draft_tweets_batch_delete":"Done"}}""");
            }
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        var list = await client.GetDraftTweetsAsync();
        Assert.Equal(new[] { "2112363137416728576", "2111781191259156480" }, list.Select(d => d.Id));
        Assert.Equal("quote", list[0].Text);
        Assert.Equal("https://x.com/amanetoki7/status/2112238353773162809", list[0].AttachmentUrl);
        Assert.Equal(new[] { "1" }, list[0].MediaIds);
        Assert.Equal("1784152598318346240", list[0].UserId);
        Assert.Equal("PostTweetRequest", list[0].Type);
        Assert.Null(list[1].AttachmentUrl);
        Assert.Empty(list[1].ExcludeReplyUserIds);
        Assert.Empty(list[1].ThreadTweets);
        Assert.Equal("<DraftTweet id=\"2111781191259156480\">", list[1].ToString());

        var id = await client.CreateDraftTweetAsync("draft test");
        Assert.Equal("2113419306533089280", id);
        list = await client.GetDraftTweetsAsync();
        Assert.Equal(3, list.Count);
        Assert.Equal("draft test", list[0].Text);

        var response = await list[0].DeleteAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"draft_tweet_id\":\"2113419306533089280\"", deleteBody);
        Assert.Contains("\"queryId\":\"bkh9G3FGgTldS9iTKWWYYw\"", deleteBody);
        Assert.Equal(2, (await client.GetDraftTweetsAsync()).Count);

        await client.DeleteDraftTweetsAsync(new[] { "2112363137416728576", "2111781191259156480" });
        Assert.Contains("\"draft_tweet_ids\":[\"2112363137416728576\",\"2111781191259156480\"]", deleteBody);
        Assert.Contains("\"queryId\":\"h0rzaIYc9-v3zTQvx1J8OA\"", deleteBody);
        Assert.Empty(await client.GetDraftTweetsAsync());
    }

    // A reply draft as FetchDraftTweets returns it (2026-10-23): in_reply_to_status_id + auto_populate_reply_metadata.
    private const string ReplyDraft = """{"replied_tweet_results":{"result":{"__typename":"Tweet","core":{"user_results":{"result":{"__typename":"User","core":{"screen_name":"someone"},"rest_id":"1297890090069319681"}}},"legacy":{"entities":{"user_mentions":[]}}}},"rest_id":"2113435966082060288","tweet_create_request":{"auto_populate_reply_metadata":true,"exclude_reply_user_ids":[],"in_reply_to_status_id":"2113419614709592331","media_ids":["55"],"status":"reply draft","thread_tweets":[],"type":"PostTweetRequest"},"user_results":{"result":{"__typename":"User","rest_id":"1784152598318346240"}}}""";
    private const string ThreadDraft = """{"rest_id":"3","tweet_create_request":{"auto_populate_reply_metadata":false,"media_ids":[],"status":"first","thread_tweets":[{"status":"second"}],"type":"PostTweetRequest"},"user_results":{"result":{"__typename":"User","rest_id":"1784152598318346240"}}}""";
    private const string CreatedTweet = """{"data":{"create_tweet":{"tweet_results":{"result":{"rest_id":"9001","core":{"user_results":{"result":{"rest_id":"1784152598318346240","core":{"screen_name":"me"}}}},"legacy":{"full_text":"reply draft","created_at":"Thu Oct 23 00:00:00 +0000 2026","in_reply_to_status_id_str":"2113419614709592331"}}}}}}""";

    [Fact]
    public async Task PostDraftSendsCreateTweetThenDeletesTheDraft()
    {
        // The web client has no "post draft" document: it sends CreateTweet from the stored request, then DeleteDraftTweet.
        var calls = new List<string>();
        string? createBody = null;
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/FetchDraftTweets", StringComparison.Ordinal))
            {
                calls.Add("fetch");
                return FakeHandler.Json("""{"data":{"viewer":{"draft_list":{"response_data":[""" + ReplyDraft + "," + ThreadDraft + "]}}}}");
            }
            if (path.EndsWith("/CreateTweet", StringComparison.Ordinal))
            {
                calls.Add("create");
                createBody = body;
                return FakeHandler.Json(CreatedTweet);
            }
            if (path.EndsWith("/bkh9G3FGgTldS9iTKWWYYw/DeleteDraftTweet", StringComparison.Ordinal))
            {
                calls.Add("delete:" + body);
                return FakeHandler.Json("""{"data":{"drafttweet_delete":"Done"}}""");
            }
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        var drafts = await client.GetDraftTweetsAsync();
        Assert.Equal("2113419614709592331", drafts[0].InReplyToTweetId);
        Assert.True(drafts[0].AutoPopulateReplyMetadata);
        Assert.Null(drafts[1].InReplyToTweetId);

        var tweet = await drafts[0].PostAsync();
        Assert.Equal("9001", tweet.Id);
        Assert.Equal(new[] { "fetch", "create" }, calls.Take(2));
        Assert.StartsWith("delete:", calls[2]);
        Assert.Contains("\"draft_tweet_id\":\"2113435966082060288\"", calls[2]);
        Assert.Contains("\"tweet_text\":\"reply draft\"", createBody);
        Assert.Contains("\"in_reply_to_tweet_id\":\"2113419614709592331\"", createBody);
        Assert.Contains("\"media_id\":\"55\"", createBody);

        // By id: looks the draft up first.
        calls.Clear();
        tweet = await client.PostDraftTweetAsync("2113435966082060288");
        Assert.Equal("9001", tweet.Id);
        Assert.Equal("fetch", calls[0]);
        Assert.Equal("create", calls[1]);
        await Assert.ThrowsAsync<NotFoundException>(() => client.PostDraftTweetAsync("nope"));

        // A thread draft is refused before anything is sent.
        calls.Clear();
        await Assert.ThrowsAsync<TwitterException>(() => drafts[1].PostAsync());
        Assert.Empty(calls);
    }

    [Fact]
    public async Task PostDraftWarnsWhenTheDraftCannotBeDeleted()
    {
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/CreateTweet", StringComparison.Ordinal)) return FakeHandler.Json(CreatedTweet);
            return FakeHandler.Json("""{"data":{"drafttweet_delete":"Failed"}}""");
        });
        using var client = Fixtures.NewClient(handler);
        var warnings = new List<string>();
        var previous = TwikitWarnings.Handler;
        TwikitWarnings.Handler = warnings.Add;
        try
        {
            var tweet = await client.PostDraftTweetAsync(new DraftTweet(client, (System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(ReplyDraft)!));
            Assert.Equal("9001", tweet.Id);
            Assert.Single(warnings);
            Assert.Contains("could not delete the draft", warnings[0]);
        }
        finally
        {
            TwikitWarnings.Handler = previous;
        }
    }

    [Fact]
    public async Task CreateDraftWithQuoteAndMedia()
    {
        string? createBody = null;
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            createBody = body;
            return FakeHandler.Json("""{"data":{"tweet":{"rest_id":"7"}}}""");
        });
        using var client = Fixtures.NewClient(handler);
        Assert.Equal("7", await client.CreateDraftTweetAsync("q", new[] { "11", "12" }, "https://x.com/a/status/1"));
        Assert.Contains("\"media_ids\":[\"11\",\"12\"]", createBody);
        Assert.Contains("\"attachment_url\":\"https://x.com/a/status/1\"", createBody);
    }

    [Fact]
    public async Task DraftErrorsAreRaised()
    {
        var reply = """{"data":{},"errors":[{"code":214,"message":"Draft is too long"}]}""";
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json(reply));
        using var client = Fixtures.NewClient(handler);
        var e = await Assert.ThrowsAsync<CouldNotTweetException>(() => client.CreateDraftTweetAsync("x"));
        Assert.Equal("Draft is too long", e.Message);

        reply = """{"data":{"drafttweet_delete":"Failed"}}""";
        await Assert.ThrowsAsync<TwitterException>(() => client.DeleteDraftTweetAsync("1"));
        reply = """{"data":{"draft_tweets_batch_delete":"Failed"}}""";
        await Assert.ThrowsAsync<TwitterException>(() => client.DeleteDraftTweetsAsync(new[] { "1" }));
    }
}
