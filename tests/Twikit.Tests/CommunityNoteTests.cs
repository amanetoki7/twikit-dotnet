using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace Twikit.Tests;

/// <summary>コミュニティノート API。応答は 2026-10-23 の HAR（x.com_comyt-note2.har / x.com_comyt-note3tabs-timeline.har）の記録。</summary>
public class CommunityNoteTests
{
    private static readonly JsonObject Cases = (JsonObject)JsonNode.Parse(File.ReadAllText(Fixtures.Path("birdwatch_cases.json")))!;

    private static string Case(string operation) => Cases[operation]!.ToJsonString();

    /// <summary>GraphQL 操作名で記録済み応答を返すハンドラー。呼び出しを <paramref name="calls"/> に記録します。</summary>
    private static FakeHandler Handler(List<(string Op, string Query, string? Body)> calls)
        => Fixtures.HandshakeHandler((request, body) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (!path.Contains("/graphql/", StringComparison.Ordinal)) return FakeHandler.Json("{}", HttpStatusCode.NotFound);
            var op = path[(path.LastIndexOf('/') + 1)..];
            calls.Add((op, Uri.UnescapeDataString(request.RequestUri.Query), body));
            return Cases.ContainsKey(op) ? FakeHandler.Json(Case(op)) : FakeHandler.Json("{}", HttpStatusCode.NotFound);
        });

    [Fact]
    public async Task GetCommunityNotesReturnsBothSides()
    {
        var calls = new List<(string Op, string Query, string? Body)>();
        using var client = Fixtures.NewClient(Handler(calls));

        var notes = await client.GetCommunityNotesAsync("2112509081256874183");

        Assert.Equal("BirdwatchFetchNotes", calls[^1].Op);
        Assert.Contains("\"tweet_id\":\"2112509081256874183\"", calls[^1].Query);
        Assert.Contains("responsive_web_birdwatch_live_note_enabled", calls[^1].Query);
        Assert.Equal(new[] { "MisinformedOrPotentiallyMisleading", "NotMisleading", "NotMisleading" }, notes.Select(n => n.Classification));
        Assert.Equal("2112590853265043494", notes[0].Id);
        Assert.Equal("2112509081256874183", notes[0].TweetId);
        Assert.Equal("likable-savanna-mockingbird", notes[0].AuthorAlias);
        Assert.Equal(new[] { "Other" }, notes[0].MisleadingTags);
        Assert.True(notes[0].TrustworthySources);
        Assert.Equal("NeedsMoreRatings", notes[0].RatingStatus);
        Assert.Equal("ja", notes[0].Language);
        Assert.Equal("CoreModel (v1.1)", notes[0].DecidedBy);
        Assert.Equal(new[] { "ClearlySatire", "PersonalOpinion" }, notes[1].NotMisleadingTags);
        Assert.Equal("CurrentlyRatedNotHelpful", notes[2].RatingStatus);
        Assert.Equal(new[] { "OpinionSpeculation", "MissingKeyPoints" }, notes[2].NotHelpfulTags);
    }

    [Fact]
    public async Task ProfileMergesEnrollmentAndComputesImpact()
    {
        var calls = new List<(string Op, string Query, string? Body)>();
        using var client = Fixtures.NewClient(Handler(calls));

        var me = await client.GetCommunityNotesProfileAsync();

        Assert.Equal(new[] { "BirdwatchFetchAuthenticatedUserProfile", "BirdwatchFetchBirdwatchProfile" }, calls.Select(c => c.Op));
        Assert.Contains("\"alias\":\"considerate-vine-conure\"", calls[1].Query);
        Assert.Equal("considerate-vine-conure", me.Alias);
        Assert.True(me.IsMe);
        Assert.True(me.CanWriteNotes);
        Assert.Equal("NewUser", me.EnrollmentState);
        Assert.Equal(5, me.SuccessfulRatingNeededToEarnIn);
        Assert.Equal("All", me.NeedsYourHelpFrequency);
        Assert.Equal(0, me.RatingImpact);
        Assert.Equal(0, me.WritingImpact);
        Assert.Equal(5, me.RatingsAwaitingMoreRatings);
        Assert.False(me.HasNotes);

        // The formulas, checked against a contributor with real numbers (from a note author's birdwatch_profile):
        // successful.total 188, unsuccessful.helpful 10, unsuccessful.not_helpful 3 -> 188 - 10 - 2*3 = 172.
        var notes = await client.GetCommunityNotesAsync("2112509081256874183");
        var author = new CommunityNotesProfile(client, notes[0].BirdwatchProfile!);
        Assert.False(author.IsMe);
        Assert.Null(author.CanWriteNotes);
        Assert.Equal(172, author.RatingImpact);
        Assert.Equal(188, author.SuccessfulRatings);
        Assert.Equal(13, author.UnsuccessfulRatings);
        Assert.Equal(111, author.RatingsAfterDecision);
        Assert.Equal(7, author.WritingImpact);
        Assert.Equal(24, author.NotesAwaitingMoreRatings);

        // By alias: a single request, no enrollment data.
        calls.Clear();
        var byAlias = await client.GetCommunityNotesProfileAsync("considerate-vine-conure");
        Assert.Equal(new[] { "BirdwatchFetchBirdwatchProfile" }, calls.Select(c => c.Op));
        Assert.False(byAlias.IsMe);
        Assert.Null(byAlias.EnrollmentState);
    }

    [Fact]
    public async Task ContributorNotesResolveOwnAliasAndPassIncludeDrafts()
    {
        var calls = new List<(string Op, string Query, string? Body)>();
        using var client = Fixtures.NewClient(Handler(calls));
        var warnings = new List<string>();
        TwikitWarnings.Handler = warnings.Add;
        try
        {
            var mine = await client.GetContributorCommunityNotesAsync();
            Assert.Equal(new[] { "BirdwatchFetchAuthenticatedUserProfile", "BirdwatchFetchContributorNotesSlice" }, calls.Select(c => c.Op));
            Assert.Contains("\"count\":10", calls[1].Query);
            Assert.Contains("\"alias\":\"considerate-vine-conure\"", calls[1].Query);
            Assert.DoesNotContain("includeDrafts", calls[1].Query);
            Assert.Empty(mine);
            Assert.Null(mine.NextCursor);

            calls.Clear();
            await client.GetContributorCommunityNotesAsync("someone-else-finch", count: 5, includeDrafts: true);
            Assert.Equal(new[] { "BirdwatchFetchContributorNotesSlice" }, calls.Select(c => c.Op));
            Assert.Contains("\"count\":5", calls[0].Query);
            Assert.Contains("\"alias\":\"someone-else-finch\"", calls[0].Query);
            Assert.Contains("\"includeDrafts\":true", calls[0].Query);
            Assert.Empty(warnings);
        }
        finally
        {
            TwikitWarnings.Handler = null;
        }
    }

    [Fact]
    public async Task TimelineTabsUseGlobalTimelineThenGenericTimelineById()
    {
        var calls = new List<(string Op, string Query, string? Body)>();
        using var client = Fixtures.NewClient(Handler(calls));

        var needsHelp = await client.GetCommunityNotesTimelineAsync();
        Assert.Equal(new[] { "BirdwatchFetchGlobalTimeline" }, calls.Select(c => c.Op));
        Assert.Equal(3, needsHelp.Count);
        Assert.Equal("2113462818926469240", needsHelp[0].Id);
        Assert.Equal("2113515887009395006", needsHelp[0].CommunityNote!.Id);
        Assert.StartsWith("pirateは語源上", needsHelp[0].CommunityNote!.Text);

        calls.Clear();
        var rated = await client.GetCommunityNotesTimelineAsync(CommunityNotesTimelineTab.RatedHelpful, count: 30);
        Assert.Equal(new[] { "BirdwatchFetchGlobalTimeline", "GenericTimelineById" }, calls.Select(c => c.Op));
        Assert.Contains("\"timelineId\":\"VGltZWxpbmU6CwA6AAAAEHRvcFR3ZWV0VGFiVjFfamEA\"", calls[1].Query);
        Assert.Contains("\"count\":30", calls[1].Query);
        Assert.Contains("\"withQuickPromoteEligibilityTweetFields\":true", calls[1].Query);
        // 4 tweet entries in the fixture: 2 readable, 1 TweetUnavailable, 1 with empty tweet_results.
        Assert.Equal(2, rated.Count);
        Assert.Equal("2113301037411152040", rated[0].CommunityNote!.Id);
        Assert.Null(rated.NextCursor);

        calls.Clear();
        await client.GetCommunityNotesTimelineAsync(CommunityNotesTimelineTab.New);
        Assert.Contains("\"timelineId\":\"VGltZWxpbmU6CwA6AAAAEzE3ODQxNTI1OTgzMTgzNDYyNDAA\"", calls[1].Query);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.GetCommunityNotesTimelineAsync("nope"));
        Assert.Contains("needs_your_help", ex.Message);
    }

    [Fact]
    public async Task RateSendsDataV2AndValidatesVocabulary()
    {
        var calls = new List<(string Op, string Query, string? Body)>();
        using var client = Fixtures.NewClient(Handler(calls));

        var rating = await client.RateCommunityNoteAsync("2112716986052755651", "2112686221973754164",
            CommunityNoteHelpfulness.Helpful, helpfulTags: new[] { CommunityNoteHelpfulTags.AddressesClaim });

        Assert.Equal("BirdwatchCreateRating", calls[^1].Op);
        var body = calls[^1].Body!;
        Assert.Contains("\"queryId\":\"gbshFt1Vmddrlio4vHWhhQ\"", body);
        Assert.Contains("\"note_id\":\"2112716986052755651\"", body);
        Assert.Contains("\"tweet_id\":\"2112686221973754164\"", body);
        Assert.Contains("\"data_v2\":{\"helpfulness_level\":\"Helpful\",\"helpful_tags\":[\"AddressesClaim\"]}", body);
        Assert.DoesNotContain("not_helpful_tags", body);
        Assert.Contains("\"rating_source\":\"BirdwatchPostDetail\"", body);
        Assert.Contains("\"source_platform\":\"BirdwatchWeb\"", body);
        Assert.Contains("\"for_live_note\":false", body);
        Assert.Equal("Helpful", rating.HelpfulnessLevel);
        Assert.Equal(new[] { "AddressesClaim" }, rating.HelpfulTags);
        Assert.Empty(rating.NotHelpfulTags);

        // Through the note object: ids come from the note.
        var notes = await client.GetCommunityNotesAsync("2112509081256874183");
        await notes[1].RateAsync(CommunityNoteHelpfulness.NotHelpful,
            notHelpfulTags: new[] { CommunityNoteNotHelpfulTags.NoSources, CommunityNoteNotHelpfulTags.OpinionSpeculation });
        body = calls[^1].Body!;
        Assert.Contains("\"note_id\":\"2112813998781710592\"", body);
        Assert.Contains("\"tweet_id\":\"2112509081256874183\"", body);
        Assert.Contains("\"data_v2\":{\"helpfulness_level\":\"NotHelpful\",\"not_helpful_tags\":[\"NoSources\",\"OpinionSpeculation\"]}", body);

        var before = calls.Count;
        await Assert.ThrowsAsync<ArgumentException>(() => client.RateCommunityNoteAsync("1", "2", "helpful"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RateCommunityNoteAsync("1", "2", "Helpful", helpfulTags: new[] { "Nice" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RateCommunityNoteAsync("1", "2", "NotHelpful", notHelpfulTags: new[] { "Wrong" }));
        Assert.Equal(before, calls.Count);
    }
}
