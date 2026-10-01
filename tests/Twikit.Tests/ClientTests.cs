using System.Net;
using System.Text.Json.Nodes;
using Twikit.Api;
using Xunit;

namespace Twikit.Tests;

public class ClientTests
{
    private const string SettingsJson = """{"screen_name":"me","user_id":123}""";

    private static string UserResult(string id, string screenName) =>
        """{"data":{"user":{"result":{"__typename":"User","rest_id":"__ID__","core":{"name":"N","screen_name":"__SN__"},"legacy":{}}}}}"""
            .Replace("__ID__", id).Replace("__SN__", screenName);

    [Fact]
    public async Task RequestPerformsHandshakeAndSendsHeaders()
    {
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("settings.json")) return FakeHandler.Json(SettingsJson, headers: ("x-rate-limit-remaining", "180"));
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);

        var response = await client.V11.SettingsAsync();
        Assert.Equal("me", response.Object.Str("screen_name"));
        Assert.Equal(180, client.RateLimitRemaining);

        // x.com home page, ondemand.s, then the API call
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("https://x.com/", handler.Requests[0].Request.RequestUri!.ToString());
        Assert.Contains("auth_token=auth123", handler.Header(0, "Cookie"));
        var api = handler.Requests[2].Request;
        Assert.Equal("https://api.x.com/1.1/account/settings.json", api.RequestUri!.ToString());
        Assert.False(string.IsNullOrEmpty(handler.Header(2, "X-Client-Transaction-Id")));
        Assert.Equal("csrf456", handler.Header(2, "X-Csrf-Token"));
        Assert.Equal("Bearer " + Constants.Token, handler.Header(2, "authorization"));
        Assert.Equal("ja", handler.Header(2, "X-Twitter-Client-Language"));
        Assert.Contains("ct0=csrf456", handler.Header(2, "Cookie"));
        Assert.Contains("auth_token=auth123", handler.Header(2, "Cookie"));
        Assert.True(client.ClientTransaction.IsInited());
    }

    [Fact]
    public void SetCookiesGeneratesCt0AndPinsDomains()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        using var client = new Client(handler: handler);
        client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "a" });
        var cookies = client.GetCookies();
        Assert.Equal("a", cookies["auth_token"]);
        Assert.Equal(32, cookies["ct0"].Length);
        Assert.Equal(cookies["ct0"], client.GetCsrfToken());
        var domains = client.Cookies.GetAllCookies().Cast<Cookie>().Where(c => c.Name == "auth_token").Select(c => c.Domain).OrderBy(d => d).ToList();
        Assert.Equal(new[] { ".twitter.com", ".x.com" }, domains);
        // not sent to unrelated hosts
        Assert.Equal("", client.Cookies.GetCookieHeader(new Uri("https://abs.twimg.com/x.js")));
        Assert.Contains("auth_token=a", client.Cookies.GetCookieHeader(new Uri("https://api.x.com/1.1/x")));
        Assert.Contains("auth_token=a", client.Cookies.GetCookieHeader(new Uri("https://twitter.com/i/js_inst")));

        // rotating cookies replaces every copy
        client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "b", ["ct0"] = "c" });
        Assert.Equal("b", client.GetCookies()["auth_token"]);
        Assert.Equal("c", client.GetCookies()["ct0"]);
        Assert.Single(client.Cookies.GetAllCookies().Cast<Cookie>().Where(c => c.Name == "auth_token").Select(c => c.Value).Distinct());
    }

    [Fact]
    public void SetCookiesAcceptsBrowserExportList()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        using var client = new Client(handler: handler);
        client.SetCookies(JsonNode.Parse("""[{"name":"auth_token","value":"x","domain":".x.com"},{"name":"ct0","value":"y"},{"junk":1}]""")!);
        Assert.Equal("x", client.GetCookies()["auth_token"]);
        Assert.Equal("y", client.GetCookies()["ct0"]);
    }

    [Fact]
    public void SaveAndLoadCookiesRoundTrip()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json("{}"));
        using var client = new Client(handler: handler);
        client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "a", ["ct0"] = "b" });
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            client.SaveCookies(path);
            using var other = new Client(handler: new FakeHandler((_, _) => FakeHandler.Json("{}")));
            other.LoadCookies(path);
            Assert.Equal(client.GetCookies(), other.GetCookies());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task HostScopedCt0FromXIsDroppedButOurCopyStays()
    {
        var handler = Fixtures.HandshakeHandler((request, _) =>
            FakeHandler.Json(SettingsJson, HttpStatusCode.OK, ("Set-Cookie", "ct0=hostcopy; Path=/; Secure"), ("Set-Cookie", "guest_id=v1%3A1; Domain=.x.com; Path=/")));
        using var client = Fixtures.NewClient(handler);
        await client.V11.SettingsAsync();
        var ct0 = client.Cookies.GetAllCookies().Cast<Cookie>().Where(c => c.Name == "ct0" && !c.Expired).ToList();
        Assert.Equal(2, ct0.Count);
        Assert.All(ct0, c => Assert.Equal("csrf456", c.Value));
        Assert.Contains("guest_id", client.GetCookies().Keys);
        Assert.Equal("csrf456", client.GetCsrfToken());
    }

    [Fact]
    public async Task StatusCodesMapToExceptions()
    {
        var status = HttpStatusCode.Unauthorized;
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json("""{"errors":[{"code":32,"message":"Could not authenticate you"}]}""", status,
            ("x-rate-limit-reset", "1700000000")));
        using var client = Fixtures.NewClient(handler);

        await Assert.ThrowsAsync<UnauthorizedException>(() => client.V11.SettingsAsync());
        status = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<ForbiddenException>(() => client.V11.SettingsAsync());
        status = HttpStatusCode.NotFound;
        await Assert.ThrowsAsync<NotFoundException>(() => client.V11.SettingsAsync());
        status = HttpStatusCode.BadRequest;
        var bad = await Assert.ThrowsAsync<BadRequestException>(() => client.V11.SettingsAsync());
        Assert.Contains("status: 400", bad.Message);
        status = HttpStatusCode.InternalServerError;
        await Assert.ThrowsAsync<ServerErrorException>(() => client.V11.SettingsAsync());
        status = HttpStatusCode.RequestTimeout;
        await Assert.ThrowsAsync<RequestTimeoutException>(() => client.V11.SettingsAsync());
        status = HttpStatusCode.TooManyRequests;
        var tooMany = await Assert.ThrowsAsync<TooManyRequestsException>(() => client.V11.SettingsAsync());
        Assert.Equal(1700000000L, tooMany.RateLimitReset);
        // the 429 branch asked user_state once, with the nested call not re-entering the check
        Assert.Contains(handler.Requests, r => r.Request.RequestUri!.AbsolutePath.Contains("user_state"));
        status = HttpStatusCode.Unauthorized;
        Assert.False(await client.IsLoggedInAsync());
    }

    [Fact]
    public async Task ErrorCodesAreInspected()
    {
        var body = """{"errors":[{"code":326,"message":"locked"}]}""";
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json(body, HttpStatusCode.Forbidden));
        using var client = Fixtures.NewClient(handler);
        var locked = await Assert.ThrowsAsync<AccountLockedException>(() => client.V11.SettingsAsync());
        Assert.Contains("/account/access", locked.Message);

        body = """{"errors":[{"code":64,"message":"Your account is suspended and is not permitted to access this feature"}]}""";
        await Assert.ThrowsAsync<AccountSuspendedException>(() => client.V11.SettingsAsync());

        // code 37 without "suspend" is a plain authorization refusal -> falls through to the status code
        body = """{"errors":[{"code":37,"message":"User is not authorized to use bookmark collections"}]}""";
        await Assert.ThrowsAsync<ForbiddenException>(() => client.V11.SettingsAsync());
    }

    [Fact]
    public async Task RaiseExceptionFalseReturnsTheResponse()
    {
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json("""{"nope":1}""", HttpStatusCode.NotFound));
        using var client = Fixtures.NewClient(handler);
        var response = await client.GetAsync("https://api.x.com/1.1/x.json", new RequestOptions { RaiseException = false });
        Assert.Equal(404, response.StatusCode);
        Assert.Equal(1, response.Object.Int("nope"));
    }

    [Fact]
    public async Task InvalidCookiesSurfaceAsInvalidSession()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Html("<html><body>Log in</body></html>"));
        using var client = Fixtures.NewClient(handler);
        await Assert.ThrowsAsync<InvalidSessionException>(() => client.V11.SettingsAsync());
        Assert.False(await client.IsLoggedInAsync());
    }

    [Fact]
    public async Task UserIdIsMemoisedAndResetBySetCookies()
    {
        var settingsCalls = 0;
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("settings.json")) { settingsCalls++; return FakeHandler.Json(SettingsJson); }
            if (path.Contains("UserByScreenName")) return FakeHandler.Json(UserResult("123", "me"));
            return FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });
        using var client = Fixtures.NewClient(handler);
        Assert.Equal("123", await client.UserIdAsync());
        Assert.Equal("123", await client.UserIdAsync());
        Assert.Equal(1, settingsCalls);
        client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "other", ["ct0"] = "x" });
        Assert.False(client.ClientTransaction.IsInited());
        Assert.Equal("123", await client.UserIdAsync());
        Assert.Equal(2, settingsCalls);
    }

    [Fact]
    public async Task GetUserByScreenNameDistinguishesMissingAndUnavailable()
    {
        var body = UserResult("1", "alice");
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            Assert.Contains("UserByScreenName", request.RequestUri!.AbsolutePath);
            Assert.Contains("variables=", request.RequestUri.Query);
            Assert.Contains("fieldToggles=", request.RequestUri.Query);
            return FakeHandler.Json(body);
        });
        using var client = Fixtures.NewClient(handler);
        var user = await client.GetUserByScreenNameAsync("alice");
        Assert.Equal("alice", user.ScreenName);

        body = """{"data":{}}""";
        await Assert.ThrowsAsync<UserNotFoundException>(() => client.GetUserByScreenNameAsync("nobody"));
        body = """{"data":{"user":{}}}""";
        await Assert.ThrowsAsync<UserNotFoundException>(() => client.GetUserByScreenNameAsync("nobody"));
        body = """{"data":{"user":{"result":{"__typename":"UserUnavailable","message":"suspended"}}}}""";
        var e = await Assert.ThrowsAsync<UserUnavailableException>(() => client.GetUserByScreenNameAsync("gone"));
        Assert.Equal("suspended", e.Message);
    }

    private static string TweetEntry(string id, string user = "u") =>
        """{"entryId":"tweet-__ID__","content":{"itemContent":{"tweet_results":{"result":{"rest_id":"__ID__","core":{"user_results":{"result":{"rest_id":"__U__","core":{"screen_name":"__U__"}}}},"legacy":{"full_text":"t__ID__","created_at":"Wed Oct 10 20:19:24 +0000 2018"}}}}}}"""
            .Replace("__ID__", id).Replace("__U__", user);

    [Fact]
    public async Task SearchTweetPaginatesAndBuffersOverflow()
    {
        var pages = 0;
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            pages++;
            var query = Uri.UnescapeDataString(request.RequestUri!.Query);
            Assert.Contains("\"product\":\"Latest\"", query);
            if (pages == 1)
            {
                Assert.DoesNotContain("\"cursor\"", query);
                return FakeHandler.Json("""{"data":{"search_by_raw_query":{"search_timeline":{"timeline":{"instructions":[{"type":"TimelineAddEntries","entries":[__E1__,__E2__,__E3__,{"entryId":"cursor-top-1","content":{"value":"TOP"}},{"entryId":"cursor-bottom-1","content":{"value":"BOTTOM"}}]}]}}}}}"""
                    .Replace("__E1__", TweetEntry("1")).Replace("__E2__", TweetEntry("2")).Replace("__E3__", TweetEntry("3")));
            }
            Assert.Contains("\"cursor\":\"BOTTOM\"", query);
            return FakeHandler.Json("""{"data":{"search_by_raw_query":{"search_timeline":{"timeline":{"instructions":[{"type":"TimelineAddEntries","entries":[__E4__,{"entryId":"cursor-bottom-2","content":{"value":"BOTTOM2"}}]}]}}}}}"""
                .Replace("__E4__", TweetEntry("4")));
        });
        using var client = Fixtures.NewClient(handler);

        var page = await client.SearchTweetAsync("python", "latest", 2);
        Assert.Equal(new[] { "1", "2" }, page.Select(t => t.Id));
        Assert.Equal("BOTTOM", page.NextCursor);
        Assert.Equal("TOP", page.PreviousCursor);
        Assert.Equal("u", page[0].User!.ScreenName);

        var buffered = await page.NextAsync();
        Assert.Equal(new[] { "3" }, buffered.Select(t => t.Id));
        Assert.Equal(1, pages);

        var fetched = await buffered.NextAsync();
        Assert.Equal(new[] { "4" }, fetched.Select(t => t.Id));
        Assert.Equal(2, pages);
        Assert.Equal("BOTTOM2", fetched.NextCursor);
    }

    [Fact]
    public async Task SearchTweetReturnsEmptyWithoutInstructions()
    {
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json("""{"data":{"search_by_raw_query":{}}}"""));
        using var client = Fixtures.NewClient(handler);
        var page = await client.SearchTweetAsync("nothing", "Top");
        Assert.Empty(page);
        Assert.Null(page.NextCursor);
        Assert.Empty(await page.NextAsync());
    }

    [Fact]
    public async Task GetUserTweetsPutsPinnedFirstAndRaisesForUnavailable()
    {
        var body = """{"data":{"user":{"result":{"timeline":{"timeline":{"instructions":[{"type":"TimelinePinEntry","entry":__E9__},{"type":"TimelineAddEntries","entries":[__E1__,{"entryId":"profile-conversation-1","content":{"metadata":{"conversationMetadata":{"allTweetIds":["2","3"]}},"items":[{"entryId":"profile-conversation-1-tweet-2","item":{"itemContent":{"tweet_results":{"result":{"rest_id":"2","core":{"user_results":{"result":{"rest_id":"u"}}},"legacy":{"full_text":"a"}}}}}},{"entryId":"profile-conversation-1-tweet-3","item":{"itemContent":{"tweet_results":{"result":{"rest_id":"3","core":{"user_results":{"result":{"rest_id":"u"}}},"legacy":{"full_text":"b"}}}}}}]}},{"entryId":"cursor-bottom-1","content":{"value":"NEXT"}}]}]}}}}}}"""
            .Replace("__E9__", TweetEntry("9")).Replace("__E1__", TweetEntry("1"));
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json(body));
        using var client = Fixtures.NewClient(handler);

        var page = await client.GetUserTweetsAsync("u", "tweets", 10);
        Assert.Equal(new[] { "9", "1", "2" }, page.Select(t => t.Id));
        Assert.Equal("NEXT", page.NextCursor);
        var conversation = page[2];
        Assert.Equal(new[] { "2", "3" }, conversation.ConversationIds);
        Assert.Equal(new[] { "3" }, conversation.Replies!.Select(t => t.Id));
        Assert.Equal(new[] { "2", "3" }, conversation.Thread!.Select(t => t.Id));

        body = """{"data":{"user":{"result":{"__typename":"UserUnavailable","message":"protected"}}}}""";
        await Assert.ThrowsAsync<UserUnavailableException>(() => client.GetUserTweetsAsync("u", "Tweets"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserTweetsAsync("u", "Bzdura"));

        body = """{"data":{"user":{"result":{"timeline":{"timeline":{"instructions":[{"type":"TimelineClearCache"}]}}}}}}""";
        var empty = await client.GetUserTweetsAsync("u", "Tweets");
        Assert.Empty(empty);
    }

    [Fact]
    public async Task GetTweetByIdBuildsRepliesAndThread()
    {
        var body = """{"data":{"threaded_conversation_with_injections_v2":{"instructions":[{"type":"TimelineAddEntries","entries":[__E1__,{"entryId":"tweet-2","content":{"itemContent":{"tweet_results":{"result":{"rest_id":"2","core":{"user_results":{"result":{"rest_id":"author"}}},"legacy":{"full_text":"focal"}}}}}},{"entryId":"conversationthread-3","content":{"items":[{"entryId":"conversationthread-3-tweet-3","item":{"itemContent":{"tweetDisplayType":"SelfThread","tweet_results":{"result":{"rest_id":"3","core":{"user_results":{"result":{"rest_id":"author"}}},"legacy":{"full_text":"cont"}}}}}},{"entryId":"conversationthread-3-tweet-4","item":{"itemContent":{"tweet_results":{"result":{"rest_id":"4","core":{"user_results":{"result":{"rest_id":"author"}}},"legacy":{"full_text":"cont2"}}}}}},{"entryId":"conversationthread-3-cursor-showmore-5","item":{"itemContent":{"value":"SHOWMORE"}}}]}},{"entryId":"cursor-bottom-9","content":{"itemContent":{"value":"MORE"}}}]}]}}}"""
            .Replace("__E1__", TweetEntry("1", "author"));
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json(body));
        using var client = Fixtures.NewClient(handler);

        var tweet = await client.GetTweetByIdAsync("2");
        Assert.Equal("2", tweet.Id);
        Assert.Equal(new[] { "1" }, tweet.ReplyTo!.Select(t => t.Id));
        Assert.Equal(new[] { "3" }, tweet.Replies!.Select(t => t.Id));
        Assert.Equal("MORE", tweet.Replies!.NextCursor);
        Assert.Equal(new[] { "2", "3", "4" }, tweet.Thread!.Select(t => t.Id));
        Assert.Equal("SHOWMORE", tweet.Replies[0].Replies!.NextCursor);
        Assert.Equal(new[] { "4" }, tweet.Replies[0].Replies!.Select(t => t.Id));

        body = """{"data":{"threaded_conversation_with_injections_v2":{"instructions":[{"type":"TimelineAddEntries","entries":[]}]}}}""";
        await Assert.ThrowsAsync<TweetNotAvailableException>(() => client.GetTweetByIdAsync("404"));
        body = """{"errors":[{"message":"_Missing: No status found with that ID.","code":144}],"data":{}}""";
        var e = await Assert.ThrowsAsync<TweetNotAvailableException>(() => client.GetTweetByIdAsync("404"));
        Assert.Contains("No status found", e.Message);
    }

    [Fact]
    public async Task CreateTweetSurfacesRefusals()
    {
        var body = """{"errors":[{"message":"Authorization: You've hit the daily limit. Subscribe to Premium for higher limits. (501)","code":501}],"data":{}}""";
        var handler = Fixtures.HandshakeHandler((request, requestBody) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            var json = JsonNode.Parse(requestBody!)!;
            Assert.Equal("wUgPBh9hEKhMMGlg8uDuFw", json.Str("queryId"));
            Assert.Equal("hello", json["variables"].Str("tweet_text"));
            Assert.Equal("m1", json["variables"]!["media"]!["media_entities"]![0].Str("media_id"));
            Assert.Equal("Community", json["variables"]!["conversation_control"].Str("mode"));
            Assert.Equal("77", json["variables"]!["reply"].Str("in_reply_to_tweet_id"));
            return FakeHandler.Json(body);
        });
        using var client = Fixtures.NewClient(handler);

        var refused = await Assert.ThrowsAsync<CouldNotTweetException>(() => client.CreateTweetAsync("hello", new[] { "m1" }, replyTo: "77", conversationControl: "followers"));
        Assert.StartsWith("Authorization: You've hit the daily limit", refused.Message);

        body = """{"errors":[{"message":"Status is a duplicate.","code":187}],"data":{}}""";
        await Assert.ThrowsAsync<DuplicateTweetException>(() => client.CreateTweetAsync("hello", new[] { "m1" }, replyTo: "77", conversationControl: "followers"));

        body = """{"data":{"create_tweet":{"tweet_results":{"result":{"rest_id":"55","core":{"user_results":{"result":{"rest_id":"me"}}},"legacy":{"full_text":"hello"}}}}}}""";
        var tweet = await client.CreateTweetAsync("hello", new[] { "m1" }, replyTo: "77", conversationControl: "followers");
        Assert.Equal("55", tweet.Id);
    }

    [Fact]
    public async Task GetDmInboxSortsByRecencyAndStopsAtEnd()
    {
        var page = 0;
        var handler = Fixtures.HandshakeHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("settings.json")) return FakeHandler.Json(SettingsJson);
            if (path.Contains("UserByScreenName")) return FakeHandler.Json(UserResult("123", "me"));
            page++;
            if (page == 1)
            {
                Assert.EndsWith("inbox_initial_state.json", path);
                return FakeHandler.Json("""{"inbox_initial_state":{"cursor":"IGNORED","conversations":{"a-123":{"conversation_id":"123-a","type":"ONE_TO_ONE","sort_timestamp":"5","participants":[{"user_id":"123"},{"user_id":"a"}]},"g":{"conversation_id":"g","type":"GROUP_DM","sort_timestamp":"9","participants":[]}},"inbox_timelines":{"trusted":{"status":"HAS_MORE","min_entry_id":"77"}}}}""");
            }
            Assert.EndsWith("inbox_timeline/trusted.json", path);
            Assert.Contains("max_id=77", request.RequestUri.Query);
            return FakeHandler.Json("""{"user_events":{"conversations":{"b-123":{"conversation_id":"123-b","type":"ONE_TO_ONE","sort_timestamp":"1","participants":[{"user_id":"b"},{"user_id":"123"}]}},"inbox_timelines":{"trusted":{"status":"AT_END"}}}}""");
        });
        using var client = Fixtures.NewClient(handler);

        var inbox = await client.GetDmInboxAsync();
        Assert.Equal(new[] { "g", "123-a" }, inbox.Select(c => c.Id));
        Assert.Equal("a", inbox[1].PartnerId);
        Assert.Equal("77", inbox.NextCursor);

        var next = await inbox.NextAsync();
        Assert.Single(next);
        Assert.Equal("b", next[0].PartnerId);
        Assert.Null(next.NextCursor);
        Assert.Empty(await next.NextAsync());
    }

    [Fact]
    public async Task FollowRedirectsWhenAsked()
    {
        var handler = new FakeHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/first")
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("/second", UriKind.Relative);
                return response;
            }
            return FakeHandler.Text("done");
        });
        using var client = new Client(handler: handler);
        var direct = await client.SendAsync(HttpMethod.Get, "https://example.com/first");
        Assert.Equal(HttpStatusCode.Found, direct.StatusCode);
        var followed = await client.SendAsync(HttpMethod.Get, "https://example.com/first", new RequestOptions { FollowRedirects = true });
        Assert.Equal("done", await followed.Content.ReadAsStringAsync());
        Assert.Equal("https://example.com/second", handler.Requests[^1].Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task UploadMediaChunksAndFinalizes()
    {
        var commands = new List<string>();
        var handler = Fixtures.HandshakeHandler((request, body) =>
        {
            var query = request.RequestUri!.Query;
            var command = System.Web.HttpUtility.ParseQueryString(query)["command"]!;
            commands.Add(command);
            if (command == "INIT")
            {
                Assert.Contains("media_type=image%2Fpng", query);
                Assert.Contains("total_bytes=", query);
                return FakeHandler.Json("""{"media_id":123,"media_id_string":"123"}""");
            }
            if (command == "APPEND")
            {
                Assert.Contains("multipart/form-data", request.Content!.Headers.ContentType!.ToString());
                Assert.Contains("name=media", body);
                Assert.Contains("filename=blob", body);
                return FakeHandler.Text("", HttpStatusCode.NoContent);
            }
            return FakeHandler.Json("""{"media_id_string":"123"}""");
        });
        using var client = Fixtures.NewClient(handler);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var mediaId = await client.UploadMediaAsync(png, waitForCompletion: true);
        Assert.Equal("123", mediaId);
        Assert.Equal(new[] { "INIT", "APPEND", "FINALIZE" }, commands);
    }

    [Fact]
    public async Task NotificationsMentionsFallBackToTimelineEntries()
    {
        var body = """{"globalObjects":{"users":{"7":{"id_str":"7","screen_name":"bob"}},"tweets":{"1":{"id_str":"1","user_id_str":"7","full_text":"@me hi","created_at":"Wed Oct 10 20:19:24 +0000 2018"}}},"timeline":{"instructions":[{"addEntries":{"entries":[{"entryId":"tweet-1","content":{}},{"entryId":"cursor-bottom-1","content":{"operation":{"cursor":{"value":"NEXT"}}}}]}}]}}""";
        var handler = Fixtures.HandshakeHandler((_, _) => FakeHandler.Json(body));
        using var client = Fixtures.NewClient(handler);
        var notifications = await client.GetNotificationsAsync("mentions");
        Assert.Single(notifications);
        Assert.Equal("1", notifications[0].Tweet!.Id);
        Assert.Equal("bob", notifications[0].FromUser!.ScreenName);
        Assert.Equal("NEXT", notifications.NextCursor);
        Assert.Equal(1288834974657L, notifications[0].TimestampMs);
    }

    [Fact]
    public async Task GetTrendsReadsStoriesModulesAndRetriesBounded()
    {
        var calls = 0;
        var handler = Fixtures.HandshakeHandler((_, _) =>
        {
            calls++;
            return FakeHandler.Json("""{"data":{"timeline":{"timeline":{"instructions":[{"type":"TimelineAddEntries","entries":[{"entryId":"stories-1","content":{"items":[{"item":{"itemContent":{"itemType":"TimelineTrend","name":"A"}}},{"item":{"itemContent":{"itemType":"TimelineTrend","name":"B"}}}]}},{"entryId":"trend-2","content":{"itemContent":{"itemType":"TimelineTrend","name":"C"}}}]}]}}}}""");
        });
        using var client = Fixtures.NewClient(handler);
        var trends = await client.GetTrendsAsync("news", 2);
        Assert.Equal(new[] { "A", "B" }, trends.Select(t => t.Name));

        handler.Route = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("GenericTimelineById")) { calls++; return FakeHandler.Json("""{"data":{"timeline":{"timeline":{"instructions":[]}}}}"""); }
            return FakeHandler.Json("{}");
        };
        calls = 0;
        Assert.Empty(await client.GetTrendsAsync("trending"));
        Assert.Equal(3, calls);
        calls = 0;
        Assert.Empty(await client.GetTrendsAsync("trending", retry: false));
        Assert.Equal(1, calls);
        Assert.Empty(await client.GetTrendsAsync("unknown"));
    }

    [Fact]
    public async Task StreamingSessionParsesEvents()
    {
        var lines = string.Join("\n", new[]
        {
            """{"topic":null,"payload":{"config":{"session_id":"S1","subscription_ttl_millis":1000,"heartbeat_millis":500}}}""",
            "not json",
            """{"topic":"/dm_update/1-2","payload":{"dm_update":{"conversation_id":"1-2","user_id":"2"}}}""",
            """{"topic":"/tweet_engagement/9","payload":{"tweet_engagement":{"like_count":"3","view_count_info":{"count":"7","state":"EnabledWithCount"}}}}""",
        });
        var handler = new FakeHandler((request, _) =>
        {
            Assert.Contains("topics=", request.RequestUri!.Query);
            Assert.Null(request.Content);
            return FakeHandler.Text(lines);
        });
        using var client = Fixtures.NewClient(handler);
        var session = await client.GetStreamingSessionAsync(new[] { Streaming.Topic.DmUpdate("1-2") }, autoReconnect: false);
        Assert.Equal("S1", session.Id);
        var events = new List<(string? Topic, Streaming.Payload Payload)>();
        await foreach (var e in session) events.Add(e);
        Assert.Equal(2, events.Count);
        Assert.Equal("/dm_update/1-2", events[0].Topic);
        Assert.Equal("2", events[0].Payload.DmUpdate!.UserId);
        Assert.Equal("7", events[1].Payload.TweetEngagement!.ViewCount);
        Assert.Equal("3", events[1].Payload.TweetEngagement!.LikeCount);
    }

    [Fact]
    public async Task LoginIsRetired()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Html("<html><body>shell</body></html>"));
        using var client = new Client(handler: handler);
        var e = await Assert.ThrowsAsync<LoginRetiredException>(() => client.LoginAsync("user", "pass"));
        Assert.IsType<InvalidSessionException>(e.InnerException);
    }

    [Fact]
    public async Task GetUserMentionsRejectsIds()
    {
        using var client = new Client(handler: new FakeHandler((_, _) => FakeHandler.Json("{}")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetUserMentionsAsync("1234567890"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetTweetByUrlAsync("https://x.com/home"));
    }
}
