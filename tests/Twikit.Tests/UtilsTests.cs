using System.Text.Json.Nodes;
using Twikit.Internal;
using Xunit;

namespace Twikit.Tests;

public class UtilsTests
{
    [Fact]
    public void BuildQueryAppendsEveryOperator()
    {
        var query = SearchQuery.Build("python", new SearchOptions
        {
            ExactPhrases = new[] { "hello world" },
            OrKeywords = new[] { "a", "b" },
            ExcludeKeywords = new[] { "spam" },
            Hashtags = new[] { "csharp" },
            FromUser = "alice",
            ToUser = "bob",
            Place = "Tokyo",
            MentionedUsers = new[] { "carol" },
            Filters = new[] { SearchFilters.Media },
            ExcludeFilters = new[] { SearchFilters.Retweets },
            Urls = new[] { "example.com" },
            Since = "2024-01-01",
            Until = "2024-02-01",
            Positive = true,
            Negative = true,
            Question = true,
        });
        Assert.Equal(
            "python \"hello world\" a OR b -\"spam\" #csharp from:alice to:bob place:\"Tokyo\" @carol filter:media " +
            "-filter:retweets url:example.com since:2024-01-01 until:2024-02-01 :) :( ?",
            query);
    }

    [Fact]
    public void BuildQueryWithNoOptionsReturnsText()
    {
        Assert.Equal("python", SearchQuery.Build("python", new SearchOptions()));
    }

    [Fact]
    public void FindDictWalksDepthFirstInInsertionOrder()
    {
        var node = JsonNode.Parse("""{"a":{"result":1,"b":[{"result":2},{"c":{"result":3}}]},"result":4}""");
        var all = node.FindDict("result").Select(n => n!.GetValue<int>()).ToList();
        Assert.Equal(new[] { 4, 1, 2, 3 }, all);
        Assert.Equal(4, node.FirstDict("result")!.GetValue<int>());
        Assert.Equal(1, node!["a"].FirstDict("result")!.GetValue<int>());
        Assert.Null(node.FirstDict("missing"));
    }

    [Fact]
    public void FindDictFindOneReturnsNullValueWhenKeyPresent()
    {
        var node = JsonNode.Parse("""{"x":{"entries":null,"y":{"entries":[1]}}}""");
        var found = node.FindDict("entries", findOne: true);
        Assert.Single(found);
        Assert.Null(found[0]);
    }

    [Fact]
    public void CursorHelpersToleratePartialShapes()
    {
        var entries = JsonNode.Parse("""[{"entryId":"tweet-1","content":{}},{"entryId":"cursor-top","content":{"value":"TOP"}},{"entryId":"cursor-bottom","content":{"itemContent":{"value":"BOTTOM"}}}]""")!.AsArray();
        Assert.Equal("BOTTOM", JsonExtensions.LastCursor(entries));
        Assert.Equal("TOP", JsonExtensions.CursorAt(entries, -2));
        Assert.Null(JsonExtensions.CursorAt(entries, -5));
        Assert.Null(JsonExtensions.LastCursor(new JsonArray()));
        Assert.Null(JsonExtensions.CursorAt(null, 0));
    }

    [Fact]
    public void FatalErrorsOnlyReportsErrorsThatSankTheResponse()
    {
        var partial = JsonNode.Parse("""{"data":{"user":{"result":{"rest_id":"1"}}},"errors":[{"message":"field failed","path":["x"]}]}""");
        Assert.Null(JsonExtensions.FatalErrors(partial));
        Assert.Null(JsonExtensions.FatalErrors(partial, "rest_id"));

        var refusal = JsonNode.Parse("""{"data":{"viewer":{"user_results":{"result":{"__typename":"User"}}}},"errors":[{"message":"User is not authorized to use bookmark collections","code":37}]}""");
        Assert.Null(JsonExtensions.FatalErrors(refusal));
        var errors = JsonExtensions.FatalErrors(refusal, "bookmark_collections_slice");
        Assert.NotNull(errors);
        Assert.Equal("User is not authorized to use bookmark collections", errors![0].Str("message"));

        var empty = JsonNode.Parse("""{"data":{},"errors":["weird"]}""");
        var normalized = JsonExtensions.FatalErrors(empty)!;
        Assert.Equal("weird", normalized[0].Str("message"));

        Assert.Null(JsonExtensions.FatalErrors(JsonNode.Parse("""{"data":{},"errors":[]}""")));
    }

    [Fact]
    public void TruthinessFollowsPython()
    {
        Assert.False(JsonNode.Parse("{}").IsTruthy());
        Assert.False(JsonNode.Parse("[]").IsTruthy());
        Assert.False(JsonNode.Parse("0").IsTruthy());
        Assert.False(JsonNode.Parse("\"\"").IsTruthy());
        Assert.False(((JsonNode?)null).IsTruthy());
        Assert.True(JsonNode.Parse("{\"a\":1}").IsTruthy());
        Assert.True(JsonNode.Parse("\"x\"").IsTruthy());
        Assert.True(JsonNode.Parse("1").IsTruthy());
    }

    [Fact]
    public void NumericAccessorsAcceptNumbersAndStrings()
    {
        var node = JsonNode.Parse("""{"a":"123","b":45,"c":true,"d":1.5,"e":"x"}""");
        Assert.Equal(123, node.Int("a"));
        Assert.Equal(45L, node.Long("b"));
        Assert.Equal("45", node.Str("b"));
        Assert.Equal("true", node.Str("c"));
        Assert.True(node.Bool("c"));
        Assert.Equal(1.5, node.Double("d"));
        Assert.Null(node.Int("e"));
        Assert.Null(node.Str("missing"));
    }

    [Fact]
    public async Task ResultHandsOutOverflowBeforeFetchingNext()
    {
        var fetches = 0;
        Func<Task<Result<int>>> fetchNext = () => { fetches++; return Task.FromResult(new Result<int>(new List<int> { 99 })); };
        var (page, rest) = Paging.Limited(new List<int> { 1, 2, 3, 4, 5 }, 2);
        var result = new Result<int>(page, fetchNext, "cursor", overflow: rest, pageSize: 2);
        Assert.Equal(new[] { 1, 2 }, result.ToList());
        Assert.Equal("cursor", result.NextCursor);

        var second = await result.NextAsync();
        Assert.Equal(new[] { 3, 4 }, second.ToList());
        Assert.Equal(0, fetches);

        var third = await second.NextAsync();
        Assert.Equal(new[] { 5 }, third.ToList());
        Assert.Equal(0, fetches);

        var fourth = await third.NextAsync();
        Assert.Equal(new[] { 99 }, fourth.ToList());
        Assert.Equal(1, fetches);

        var fifth = await fourth.NextAsync();
        Assert.Empty(fifth);
        Assert.Empty(await fifth.PreviousAsync());
    }

    [Fact]
    public void LimitedKeepsEverythingWhenCountIsNotPositive()
    {
        var (page, rest) = Paging.Limited(new List<int> { 1, 2, 3 }, 0);
        Assert.Equal(3, page.Count);
        Assert.Empty(rest);
    }

    [Fact]
    public void TotpMatchesRfc6238Vector()
    {
        // RFC 6238 appendix B, SHA-1, secret "12345678901234567890" at T=59 -> 94287082 (8 digits) -> 287082 (6 digits).
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        Assert.Equal("287082", Totp.Now(secret, at: DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal("94287082", Totp.Now(secret, digits: 8, at: DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal("07081804", Totp.Now(secret, digits: 8, at: DateTimeOffset.FromUnixTimeSeconds(1111111109)));
    }

    [Fact]
    public void MimeGuessRecognisesCommonMedia()
    {
        Assert.Equal("image/jpeg", MimeGuess.Guess(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }));
        Assert.Equal("image/png", MimeGuess.Guess(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
        Assert.Equal("image/gif", MimeGuess.Guess("GIF89a"u8.ToArray()));
        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0 };
        Assert.Equal("video/mp4", MimeGuess.Guess(mp4));
        var webp = "RIFF\0\0\0\0WEBPVP8 "u8.ToArray();
        Assert.Equal("image/webp", MimeGuess.Guess(webp));
        Assert.Null(MimeGuess.Guess(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void M3u8ParsesMediaAndSegments()
    {
        var master = "#EXTM3U\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"subs\",NAME=\"English\",LANGUAGE=\"en\",URI=\"/ext_tw_video/1/pu/subs/en.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=1\n/video/1.m3u8\n";
        var playlist = M3u8Playlist.Parse(master);
        Assert.Single(playlist.Media);
        Assert.Equal("SUBTITLES", playlist.Media[0].Type);
        Assert.Equal("/ext_tw_video/1/pu/subs/en.m3u8", playlist.Media[0].Uri);
        Assert.Equal("English", playlist.Media[0].Name);
        Assert.Empty(playlist.Segments);

        var media = "#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:9.9,\n/segment0.vtt\n#EXTINF:9.9,\n/segment1.vtt\n#EXT-X-ENDLIST\n";
        var segments = M3u8Playlist.Parse(media).Segments;
        Assert.Equal(new[] { "/segment0.vtt", "/segment1.vtt" }, segments);
    }

    [Fact]
    public void WebVttParsesCues()
    {
        var vtt = "WEBVTT\n\n1\n00:00:01.000 --> 00:00:02.500 align:start\nHello\nworld\n\n00:01:00.000 --> 00:01:01.000\nBye\n";
        var cues = WebVtt.Parse(vtt);
        Assert.Equal(2, cues.Count);
        Assert.Equal("00:00:01.000", cues[0].Start);
        Assert.Equal("00:00:02.500", cues[0].End);
        Assert.Equal("Hello\nworld", cues[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(1), cues[0].StartTime);
        Assert.Equal(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1), cues[1].EndTime);
    }

    [Fact]
    public void TimestampParsing()
    {
        var dt = Utils.TimestampToDateTime("Wed Oct 10 20:19:24 +0000 2018");
        Assert.Equal(new DateTimeOffset(2018, 10, 10, 20, 19, 24, TimeSpan.Zero), dt);
        Assert.Equal(1288834974657L + (1519480761749016577L >> 22), Utils.SnowflakeToTimestampMs("1519480761749016577"));
    }

    [Fact]
    public void GetQueryIdReadsSecondToLastSegment()
    {
        Assert.Equal("queryid", Utils.GetQueryId("https://x.com/i/api/graphql/queryid/SearchTimeline"));
    }

    [Fact]
    public void FlattenParamsSerialisesObjects()
    {
        var flat = Utils.FlattenParams(new JsonObject { ["variables"] = new JsonObject { ["a"] = 1 }, ["s"] = "x", ["n"] = 2 });
        Assert.Equal("{\"a\":1}", flat["variables"]);
        Assert.Equal("x", flat["s"]);
        Assert.Equal("2", flat["n"]);
    }

    [Fact]
    public void BuildUserDataMovesLegacyFields()
    {
        var raw = (JsonObject)JsonNode.Parse("""{"id":1,"id_str":"1","screen_name":"alice","name":"Alice","followers_count":3,"ext_is_blue_verified":true,"entities":null}""")!;
        var built = Utils.BuildUserData(raw);
        Assert.Equal("1", built.Str("rest_id"));
        Assert.True(built.Bool("is_blue_verified"));
        Assert.Equal("alice", built["legacy"].Str("screen_name"));
        Assert.Equal(3, built["legacy"].Int("followers_count"));
    }
}
