using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

public partial class Client
{
    /// <summary>
    /// 投稿に提案されたコミュニティノートをすべて取得します（<c>BirdwatchFetchNotes</c>）。
    /// </summary>
    /// <remarks>
    /// 「誤解を招く」側と「誤解を招かない」側の両方、表示前（<c>NeedsMoreRatings</c>）のものも含みます。
    /// 各ノートは <see cref="CommunityNote.RatingStatus"/> を持ち、<see cref="CommunityNote.RateAsync"/> で評価できます。
    /// </remarks>
    /// <param name="tweetId">投稿の ID。</param>
    /// <returns>ノートの一覧。誰もノートを書いていなければ空。</returns>
    /// <exception cref="NotFoundException">投稿が存在しないか見られない。</exception>
    public async Task<List<CommunityNote>> GetCommunityNotesAsync(string tweetId)
    {
        var response = await Gql.BirdwatchFetchNotesAsync(tweetId).ConfigureAwait(false);
        var result = response.Json.Get("data").Get("tweet_result_by_rest_id").Obj("result");
        if (result is null)
        {
            var errors = JsonExtensions.FatalErrors(response.Json, "tweet_result_by_rest_id");
            if (errors is not null)
            {
                ErrorCodes.RaiseExceptionsFromResponse(errors);
                throw new TwitterException(errors.ErrorMessage(errors[0].ToJsonString()));
            }
            throw new NotFoundException($"No tweet with id '{tweetId}'.");
        }
        var notes = new List<CommunityNote>();
        foreach (var key in new[] { "misleading_birdwatch_notes", "not_misleading_birdwatch_notes" })
            foreach (var note in result.Sub(key).ArrOrEmpty("notes").Objects())
                if (note.ContainsKey("data_v1"))
                    notes.Add(new CommunityNote(this, note, tweetId));
        return notes;
    }

    private async Task<(string Alias, JsonObject Authenticated)> CommunityNotesAliasAsync()
    {
        var response = await Gql.BirdwatchAuthenticatedUserProfileAsync().ConfigureAwait(false);
        var authenticated = response.Json.Get("data").Obj("authenticated_user_birdwatch_profile");
        var alias = authenticated.Str("alias");
        if (authenticated is null || string.IsNullOrEmpty(alias))
            throw new TwitterException("This account has no Community Notes contributor alias (not enrolled, or the alias has not been chosen yet).");
        return (alias, authenticated);
    }

    /// <summary>
    /// コミュニティノートの協力者プロフィール（<c>x.com/i/communitynotes/u/&lt;alias&gt;</c> の評価・執筆の統計）を取得します。
    /// </summary>
    /// <remarks>
    /// <paramref name="alias"/> を省略すると自分のプロフィールで、参加状態と執筆解放までに必要な評価の効果も入ります。
    /// <see cref="CommunityNotesProfile.RatingImpact"/> と <see cref="CommunityNotesProfile.WritingImpact"/> は Web クライアントと同じ式で計算します。
    /// </remarks>
    /// <param name="alias">協力者エイリアス（<c>considerate-vine-conure</c> など）。null なら自分。</param>
    /// <exception cref="TwitterException">自分のアカウントに協力者エイリアスが無い。</exception>
    /// <exception cref="NotFoundException">そのエイリアスのプロフィールが無い。</exception>
    public async Task<CommunityNotesProfile> GetCommunityNotesProfileAsync(string? alias = null)
    {
        JsonObject? authenticated = null;
        if (alias is null)
            (alias, authenticated) = await CommunityNotesAliasAsync().ConfigureAwait(false);
        var response = await Gql.BirdwatchProfileAsync(alias).ConfigureAwait(false);
        var data = response.Json.Get("data").Obj("birdwatch_profile_by_alias");
        if (data is null) throw new NotFoundException($"No Community Notes profile with alias '{alias}'.");
        return new CommunityNotesProfile(this, data, authenticated);
    }

    /// <summary>
    /// 協力者エイリアスで書かれたノートを新しい順に取得します（<c>BirdwatchFetchContributorNotesSlice</c>）。
    /// <paramref name="alias"/> を省略すると自分のノートです。
    /// </summary>
    /// <remarks>
    /// まだノートを書いていないアカウントで記録したため、空の形（<c>items: []</c>）しか確認していません。
    /// ノートとして読めない項目はキーを添えた警告（<see cref="TwikitWarnings"/>）を出して読み飛ばします。
    /// <c>slice_info</c> によるページ送りは配線済みですが未検証です。
    /// </remarks>
    /// <param name="alias">協力者エイリアス。null なら自分。</param>
    /// <param name="count">件数。</param>
    /// <param name="includeDrafts">true で自分のプロフィールの「提案」ビュー（未公開の下書きを含む）を要求します。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<CommunityNote>> GetContributorCommunityNotesAsync(string? alias = null, int count = 10, bool includeDrafts = false, string? cursor = null)
    {
        if (alias is null)
            (alias, _) = await CommunityNotesAliasAsync().ConfigureAwait(false);
        var response = await Gql.BirdwatchContributorNotesSliceAsync(alias, count, includeDrafts, cursor).ConfigureAwait(false);
        var profile = response.Json.Get("data").Sub("birdwatch_profile_by_alias");
        var (primary, secondary) = includeDrafts
            ? ("suggestion_overviews_slice", "notes_slice")
            : ("notes_slice", "suggestion_overviews_slice");
        var slice = profile.Obj(primary) ?? profile.Sub(secondary);
        var notes = new List<CommunityNote>();
        foreach (var item in slice.ArrOrEmpty("items"))
        {
            var note = item.Obj("note") ?? item as JsonObject;
            if (note is not null && note.ContainsKey("data_v1"))
                notes.Add(new CommunityNote(this, note));
            else
                TwikitWarnings.Warn($"Skipped a Community Notes slice item of unknown shape: {item?.ToJsonString() ?? "null"}. Please report it with the raw response.");
        }
        var nextCursor = slice.Sub("slice_info").Str("next_cursor");
        var resolvedAlias = alias;
        return Page(notes, count, nextCursor,
            () => GetContributorCommunityNotesAsync(resolvedAlias, count, includeDrafts, nextCursor));
    }

    /// <summary>
    /// コミュニティノートのホーム（<c>x.com/i/communitynotes</c>）の 1 タブを取得します:
    /// 評価が必要な投稿、新しいノート、「役に立った」と評価済みのノート。
    /// </summary>
    /// <remarks>
    /// 結果は投稿の一覧です。X がその投稿に対して選んだノートは <see cref="Tweet.CommunityNote"/>（ID と本文）に入り、
    /// 投稿の全ノートを評価できる形で取るには <see cref="Tweet.GetCommunityNotesAsync"/> を使います。
    /// <c>needs_your_help</c> は <c>BirdwatchFetchGlobalTimeline</c>、残り 2 タブはその応答が示すタイムライン ID での
    /// <c>GenericTimelineById</c> です。記録時（2026-10-23）は 3 タブともカーソル無しで終端していたため、<c>NextAsync</c> は通常 null です。
    /// 削除済み・非公開で取得できない投稿（<c>TweetUnavailable</c>）は含まれません。
    /// </remarks>
    /// <param name="tab"><see cref="CommunityNotesTimelineTab"/> のいずれか。</param>
    /// <param name="count"><c>new</c> / <c>rated_helpful</c> のページサイズ。<c>needs_your_help</c> は約 15 件の固定ピックで無視されます。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    /// <exception cref="ArgumentException">X が提供していないタブ名。</exception>
    public async Task<Result<Tweet>> GetCommunityNotesTimelineAsync(string tab = CommunityNotesTimelineTab.NeedsYourHelp, int count = 20, string? cursor = null)
    {
        var response = await Gql.BirdwatchGlobalTimelineAsync().ConfigureAwait(false);
        var body = response.Json.Get("data").Get("viewer").Get("birdwatch_home_page").Sub("body");
        var initial = body.Sub("initialTimeline");
        var tabs = new Dictionary<string, string>();
        foreach (var t in body.ArrOrEmpty("timelines").Objects())
            if (t.Str("id") is { } id && t.Sub("timeline").Str("id") is { } timelineId)
                tabs[id] = timelineId;

        JsonArray? instructions;
        if (tab == initial.Str("id") && cursor is null)
            instructions = initial.FirstDict("instructions") as JsonArray;
        else
        {
            if (!tabs.TryGetValue(tab, out var timelineId))
                throw new ArgumentException($"Unknown Community Notes tab '{tab}'; X offers {string.Join(", ", tabs.Keys)}.", nameof(tab));
            var extra = new JsonObject { ["withQuickPromoteEligibilityTweetFields"] = true };
            if (cursor is not null) extra["cursor"] = cursor;
            var page = await Gql.GenericTimelineByIdAsync(timelineId, count, extra).ConfigureAwait(false);
            instructions = page.Json.FirstDict("instructions") as JsonArray;
        }

        var instruction = JsonExtensions.FindEntryByType(instructions, "TimelineAddEntries");
        if (instruction is null) return Result<Tweet>.Empty();
        string? nextCursor = null;
        string? previousCursor = null;
        var results = new List<Tweet>();
        foreach (var entry in instruction.ArrOrEmpty("entries").Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("tweet", StringComparison.Ordinal))
            {
                var tweet = Tweet.FromData(this, entry);
                if (tweet is not null) results.Add(tweet);
            }
            else if (entryId.StartsWith("cursor-top", StringComparison.Ordinal))
                previousCursor = entry.Sub("content").Str("value");
            else if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal))
                nextCursor = entry.Sub("content").Str("value");
        }
        return Page(results, count, nextCursor,
            () => GetCommunityNotesTimelineAsync(tab, count, nextCursor),
            previousCursor,
            () => GetCommunityNotesTimelineAsync(tab, count, previousCursor));
    }

    /// <summary>
    /// コミュニティノートを評価します（<c>BirdwatchCreateRating</c>）。
    /// <c>x.com/i/birdwatch/t/&lt;tweet_id&gt;</c> の「役に立ちましたか？」フォームと同じリクエストで、同じノートを再評価すると上書きされます。
    /// </summary>
    /// <param name="noteId">ノートの ID。</param>
    /// <param name="tweetId">ノートが付いた投稿の ID（<see cref="CommunityNote.TweetId"/>）。</param>
    /// <param name="helpfulness"><see cref="CommunityNoteHelpfulness"/> のいずれか。</param>
    /// <param name="helpfulTags">役に立った理由（<see cref="CommunityNoteHelpfulTags"/>）。Web のフォームは Helpful と SomewhatHelpful で尋ねます。</param>
    /// <param name="notHelpfulTags">役に立たなかった理由（<see cref="CommunityNoteNotHelpfulTags"/>）。NotHelpful と SomewhatHelpful（TwitterViolationAny を除く）で尋ねます。</param>
    /// <param name="ratingSource">評価フォームが表示されていた場所（<see cref="CommunityNoteRatingSource"/>）。</param>
    /// <param name="forLiveNote"></param>
    /// <returns>X が保存した評価（レベルとタグ）。</returns>
    /// <exception cref="ArgumentException">不明な <paramref name="helpfulness"/> またはタグ。</exception>
    /// <exception cref="TwitterException">X が評価を確認しなかった。</exception>
    public async Task<CommunityNoteRating> RateCommunityNoteAsync(
        string noteId, string tweetId, string helpfulness,
        IEnumerable<string>? helpfulTags = null, IEnumerable<string>? notHelpfulTags = null,
        string ratingSource = CommunityNoteRatingSource.PostDetail, bool forLiveNote = false)
    {
        if (!CommunityNoteHelpfulness.All.Contains(helpfulness))
            throw new ArgumentException($"helpfulness must be one of {string.Join(", ", CommunityNoteHelpfulness.All)}, got '{helpfulness}'.", nameof(helpfulness));
        var helpful = helpfulTags?.ToList() ?? new List<string>();
        var notHelpful = notHelpfulTags?.ToList() ?? new List<string>();
        var unknown = helpful.Except(CommunityNoteHelpfulTags.All).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown helpfulTags: {string.Join(", ", unknown)}. Known: {string.Join(", ", CommunityNoteHelpfulTags.All)}.", nameof(helpfulTags));
        unknown = notHelpful.Except(CommunityNoteNotHelpfulTags.All).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown notHelpfulTags: {string.Join(", ", unknown)}. Known: {string.Join(", ", CommunityNoteNotHelpfulTags.All)}.", nameof(notHelpfulTags));

        var dataV2 = new JsonObject { ["helpfulness_level"] = helpfulness };
        if (helpful.Count > 0) dataV2["helpful_tags"] = helpful.ToJsonArray();
        if (notHelpful.Count > 0) dataV2["not_helpful_tags"] = notHelpful.ToJsonArray();
        var response = await Gql.BirdwatchCreateRatingAsync(noteId, tweetId, dataV2, ratingSource, forLiveNote).ConfigureAwait(false);
        var result = response.Json.Get("data").Obj("birdwatchnote_rate_v3");
        if (result is null)
        {
            var errors = JsonExtensions.FatalErrors(response.Json, "birdwatchnote_rate_v3");
            if (errors is not null)
            {
                ErrorCodes.RaiseExceptionsFromResponse(errors);
                throw new TwitterException(errors.ErrorMessage(errors[0].ToJsonString()));
            }
            throw new TwitterException($"X did not confirm the rating: {response.Json.Dump()}");
        }
        return new CommunityNoteRating(noteId, tweetId, result);
    }
}
