using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>コミュニティノートの評価レベル（<c>helpfulness_level</c>）。</summary>
public static class CommunityNoteHelpfulness
{
    public const string Helpful = "Helpful";
    public const string SomewhatHelpful = "SomewhatHelpful";
    public const string NotHelpful = "NotHelpful";
    public static readonly IReadOnlyList<string> All = new[] { Helpful, SomewhatHelpful, NotHelpful };
}

/// <summary>「役に立った」理由のタグ（<c>helpful_tags</c>）。x.com のバンドル（2026-10-23）の列挙と同じ。</summary>
public static class CommunityNoteHelpfulTags
{
    public const string Informative = "Informative";
    public const string Clear = "Clear";
    public const string Empathetic = "Empathetic";
    public const string GoodSources = "GoodSources";
    public const string UniqueContext = "UniqueContext";
    public const string AddressesClaim = "AddressesClaim";
    public const string ImportantContext = "ImportantContext";
    public const string UnbiasedLanguage = "UnbiasedLanguage";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = new[]
    {
        Informative, Clear, Empathetic, GoodSources, UniqueContext, AddressesClaim, ImportantContext, UnbiasedLanguage, Other,
    };
}

/// <summary>「役に立たなかった」理由のタグ（<c>not_helpful_tags</c>）。x.com のバンドル（2026-10-23）の列挙と同じ。</summary>
public static class CommunityNoteNotHelpfulTags
{
    public const string NoSources = "NoSources";
    public const string Incorrect = "Incorrect";
    public const string Outdated = "Outdated";
    public const string Biased = "Biased";
    public const string MissingKeyPoints = "MissingKeyPoints";
    public const string Unclear = "Unclear";
    public const string Rude = "Rude";
    public const string OffTopic = "OffTopic";
    public const string TwitterViolationAny = "TwitterViolationAny";
    public const string IrrelevantSources = "IrrelevantSources";
    public const string OpinionSpeculation = "OpinionSpeculation";
    public const string NoteNotNeeded = "NoteNotNeeded";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = new[]
    {
        NoSources, Incorrect, Outdated, Biased, MissingKeyPoints, Unclear, Rude, OffTopic, TwitterViolationAny,
        IrrelevantSources, OpinionSpeculation, NoteNotNeeded, Other,
    };
}

/// <summary>評価フォームが表示されていた場所（<c>rating_source</c>）。</summary>
public static class CommunityNoteRatingSource
{
    public const string NyhNotification = "BirdwatchNyhNotification";
    public const string HomeRatedHelpful = "BirdwatchHomeRatedHelpful";
    public const string HomeNew = "BirdwatchHomeNew";
    public const string HomeNeedsYourHelp = "BirdwatchHomeNeedsYourHelp";
    public const string HomeNoteRequest = "BirdwatchHomeNoteRequest";
    public const string OverflowMenu = "BirdwatchOverflowMenu";
    public const string ForYouTimeline = "BirdwatchForYouTimeline";
    public const string FollowingTimeline = "BirdwatchFollowingTimeline";
    public const string PostDetail = "BirdwatchPostDetail";
    public const string Unknown = "BirdwatchUnknown";
    public const string CorrectionNotification = "BirdwatchCorrectionNotification";
}

/// <summary><c>x.com/i/communitynotes</c> の 3 タブ。</summary>
public static class CommunityNotesTimelineTab
{
    /// <summary>「評価が必要」: 評価してほしいノートが付いた投稿（約 15 件の固定ピック）。</summary>
    public const string NeedsYourHelp = "needs_your_help";
    /// <summary>「新しいノート」。</summary>
    public const string New = "new";
    /// <summary>「「役に立った」と評価済み」。</summary>
    public const string RatedHelpful = "rated_helpful";
    public static readonly IReadOnlyList<string> All = new[] { NeedsYourHelp, New, RatedHelpful };
}

/// <summary>
/// コミュニティノートの協力者プロフィール（<c>BirdwatchFetchBirdwatchProfile</c>）。
/// 自分のプロフィールでは <c>BirdwatchFetchAuthenticatedUserProfile</c> の参加状態も入ります。
/// </summary>
public sealed class CommunityNotesProfile : IEquatable<CommunityNotesProfile>
{
    private readonly Client _client;

    /// <summary><c>birdwatch_profile_by_alias</c> の生 JSON。</summary>
    public JsonObject Raw { get; private set; } = new();
    /// <summary>自分のプロフィールのときの <c>authenticated_user_birdwatch_profile</c>。他人のときは null。</summary>
    public JsonObject? Authenticated { get; private set; }

    /// <summary>協力者エイリアス（<c>adjective-noun-bird</c>）。プロフィールはアカウントではなくこれで引きます。</summary>
    public string Alias { get; private set; } = "";
    /// <summary>ログイン中のアカウント自身のプロフィールか。true のときだけ参加状態の各プロパティが入ります。</summary>
    public bool IsMe => Authenticated is not null;
    public bool IsTopWriter { get; private set; }
    /// <summary>公開 API を使う AI ノート執筆者なら true。</summary>
    public bool IsApiContributor { get; private set; }
    public bool HasNotes { get; private set; }
    public int DeletedNotesCount { get; private set; }
    /// <summary>執筆の統計: <c>awaiting_more_ratings</c>、<c>currently_rated_helpful</c>、<c>currently_rated_not_helpful</c>、<c>last_updated_at</c>。0 の項目は X が省きます。</summary>
    public JsonObject NotesCount { get; private set; } = new();
    /// <summary>評価の統計: <c>awaiting_more_ratings</c>、<c>rated_after_decision</c>、<c>successful</c> / <c>unsuccessful</c>（各 <c>helpful_count</c>、<c>not_helpful_count</c>、<c>total</c>）、<c>last_updated_at</c>。</summary>
    public JsonObject RatingsCount { get; private set; } = new();

    /// <summary>ノートを書けるか（自分のプロフィールのみ）。</summary>
    public bool? CanWriteNotes { get; private set; }
    /// <summary><c>NewUser</c>、<c>EarnedIn</c>、<c>EarnedOutNoAcknowledge</c> など（自分のプロフィールのみ）。</summary>
    public string? EnrollmentState { get; private set; }
    /// <summary>執筆を解放するまでに必要な評価の効果（自分のプロフィールのみ）。</summary>
    public int? SuccessfulRatingNeededToEarnIn { get; private set; }
    /// <summary>参加状態が最後に変わった時刻（Unix ミリ秒、自分のプロフィールのみ）。</summary>
    public long? EnrollmentChangedAt { get; private set; }
    public string? SurveyUrl { get; private set; }
    /// <summary>「評価が必要」通知の頻度設定（自分のプロフィールのみ）。</summary>
    public string? NeedsYourHelpFrequency { get; private set; }

    // -- 評価の効果 ---------------------------------------------------------

    /// <summary>ノートが最終的に得た状態と一致した評価の数。</summary>
    public int SuccessfulRatings => RatingsCount.Sub("successful").IntOr("total", 0);
    /// <summary>ノートが最終的に得た状態と逆だった評価の数。</summary>
    public int UnsuccessfulRatings => RatingsCount.Sub("unsuccessful").IntOr("total", 0);
    /// <summary>まだ状態が決まっていないノートへの評価の数。</summary>
    public int RatingsAwaitingMoreRatings => RatingsCount.IntOr("awaiting_more_ratings", 0);
    /// <summary>状態が決まったあとに付けた評価の数（効果なし）。</summary>
    public int RatingsAfterDecision => RatingsCount.IntOr("rated_after_decision", 0);

    /// <summary>
    /// プロフィールページに出る「評価の効果」。Web クライアントと同じ式（2026-10-23 のバンドル）:
    /// <c>successful.total − unsuccessful.helpful_count − 2 × unsuccessful.not_helpful_count</c>。
    /// 新規の協力者は <see cref="SuccessfulRatingNeededToEarnIn"/>（現在 5）に達すると執筆が解放されます。
    /// </summary>
    public int RatingImpact
    {
        get
        {
            var unsuccessful = RatingsCount.Sub("unsuccessful");
            return SuccessfulRatings - unsuccessful.IntOr("helpful_count", 0) - 2 * unsuccessful.IntOr("not_helpful_count", 0);
        }
    }

    // -- 執筆の効果 ---------------------------------------------------------

    public int NotesRatedHelpful => NotesCount.IntOr("currently_rated_helpful", 0);
    public int NotesRatedNotHelpful => NotesCount.IntOr("currently_rated_not_helpful", 0);
    public int NotesAwaitingMoreRatings => NotesCount.IntOr("awaiting_more_ratings", 0);

    /// <summary>プロフィールページに出る「執筆の効果」: <c>currently_rated_helpful − currently_rated_not_helpful</c>（Web クライアントと同じ式）。</summary>
    public int WritingImpact => NotesRatedHelpful - NotesRatedNotHelpful;

    public CommunityNotesProfile(Client client, JsonObject data, JsonObject? authenticated = null)
    {
        _client = client;
        Load(data, authenticated);
    }

    private void Load(JsonObject data, JsonObject? authenticated)
    {
        Raw = data;
        Authenticated = authenticated;
        Alias = data.Str("alias") ?? authenticated.Str("alias") ?? "";
        IsTopWriter = data.BoolOr("is_top_writer", false);
        IsApiContributor = data.BoolOr("is_api_contributor", false);
        HasNotes = data.BoolOr("has_notes", false);
        DeletedNotesCount = data.IntOr("deleted_notes_count", 0);
        NotesCount = data.Sub("notes_count");
        RatingsCount = data.Sub("ratings_count");

        var enrollment = authenticated.Sub("user_enrollment");
        CanWriteNotes = authenticated.Bool("can_write_notes");
        EnrollmentState = enrollment.Str("enrollment_state");
        SuccessfulRatingNeededToEarnIn = enrollment.Int("successful_rating_needed_to_earn_in");
        EnrollmentChangedAt = enrollment.Long("timestamp_of_last_state_change");
        SurveyUrl = enrollment.Str("survey_url");
        NeedsYourHelpFrequency = authenticated.Sub("notification_settings").Str("needs_your_help_frequency");
    }

    /// <summary>このエイリアスで書かれたノート。</summary>
    /// <seealso cref="Client.GetContributorCommunityNotesAsync"/>
    public Task<Result<CommunityNote>> GetNotesAsync(int count = 10, bool includeDrafts = false, string? cursor = null)
        => _client.GetContributorCommunityNotesAsync(Alias, count, includeDrafts, cursor);

    /// <summary>プロフィールを取り直して全プロパティを更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetCommunityNotesProfileAsync(IsMe ? null : Alias).ConfigureAwait(false);
        Load(fresh.Raw, fresh.Authenticated);
    }

    public override string ToString() => $"<CommunityNotesProfile alias=\"{Alias}\" rating_impact={RatingImpact} writing_impact={WritingImpact}>";
    public bool Equals(CommunityNotesProfile? other) => other is not null && Alias == other.Alias;
    public override bool Equals(object? obj) => Equals(obj as CommunityNotesProfile);
    public override int GetHashCode() => Alias.GetHashCode();
}

/// <summary><c>BirdwatchCreateRating</c> の応答（<c>data.birdwatchnote_rate_v3</c>）: X が保存した評価。</summary>
public sealed class CommunityNoteRating
{
    public string NoteId { get; }
    public string TweetId { get; }
    /// <summary><see cref="CommunityNoteHelpfulness"/> のいずれか。</summary>
    public string? HelpfulnessLevel { get; }
    public List<string> HelpfulTags { get; }
    public List<string> NotHelpfulTags { get; }
    /// <summary>生の JSON。</summary>
    public JsonObject Raw { get; }

    public CommunityNoteRating(string noteId, string tweetId, JsonObject data)
    {
        NoteId = noteId;
        TweetId = tweetId;
        Raw = data;
        var dataV2 = data.Sub("data_v2");
        HelpfulnessLevel = dataV2.Str("helpfulness_level");
        HelpfulTags = dataV2.Arr("helpful_tags").Strings();
        NotHelpfulTags = dataV2.Arr("not_helpful_tags").Strings();
    }

    public override string ToString() => $"<CommunityNoteRating note_id=\"{NoteId}\" helpfulness_level=\"{HelpfulnessLevel}\">";
}
