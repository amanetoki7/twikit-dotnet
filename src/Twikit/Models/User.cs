using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Twikit;

/// <summary>
/// ユーザー。
/// </summary>
/// <remarks>
/// <para>
/// どのフィールドが埋まるかは、そのユーザーがどこから来たかで変わります。X はプロフィールと
/// タイムラインを別のドキュメントから返すためです。
/// </para>
/// <para>
/// <see cref="Client.GetUserByScreenNameAsync"/> と <see cref="Client.GetUserByIdAsync"/> は
/// X の <c>legacy</c> ブロックにしか無い 9 項目（<see cref="ListedCount"/>、<see cref="FastFollowersCount"/>、
/// <see cref="NormalFollowersCount"/>、<see cref="DefaultProfile"/>、<see cref="DefaultProfileImage"/>、
/// <see cref="HasCustomTimelines"/>、<see cref="WantRetweets"/>、<see cref="IsTranslator"/>、
/// <see cref="WithheldInCountries"/>）を含めてすべて埋めます。
/// </para>
/// <para>
/// ツイート・タイムライン・検索経由のユーザーは <c>legacy</c> を捨てて型付きオブジェクトに
/// 移行したドキュメントから来るため、その 9 項目は既定値（0、false、空リスト）のままで、
/// 既定値は計測値ではありません。そのドキュメントだけが <see cref="ParodyCommentaryFanLabel"/>
/// を運ぶので、直接のプロフィール取得ではラベルは得られません。
/// </para>
/// </remarks>
public class User : IEquatable<User>
{
    private readonly Client _client;

    /// <summary>X から受け取った生の JSON。</summary>
    public JsonObject Data { get; private set; } = new();

    /// <summary>ユーザーの一意な ID。</summary>
    public string Id { get; private set; } = "";
    /// <summary>アカウント作成日時（X の表記）。</summary>
    public string CreatedAt { get; private set; } = "";
    /// <summary>表示名。</summary>
    public string Name { get; private set; } = "";
    /// <summary>スクリーンネーム（@ 無し）。</summary>
    public string ScreenName { get; private set; } = "";
    /// <summary>プロフィール画像の URL（HTTPS）。</summary>
    public string ProfileImageUrl { get; private set; } = "";
    /// <summary>プロフィールのバナー画像の URL。</summary>
    public string? ProfileBannerUrl { get; private set; }
    /// <summary>ユーザーの URL。</summary>
    public string? Url { get; private set; }
    /// <summary>場所。</summary>
    public string Location { get; private set; } = "";
    /// <summary>自己紹介。</summary>
    public string Description { get; private set; } = "";
    /// <summary>自己紹介に含まれる URL。</summary>
    public List<UrlEntity> DescriptionUrls { get; private set; } = new();
    /// <summary>プロフィールに設定された URL。</summary>
    public List<UrlEntity> Urls { get; private set; } = new();
    /// <summary>固定ツイートの ID。</summary>
    public List<string> PinnedTweetIds { get; private set; } = new();
    /// <summary>青いチェックマークで認証済みか。</summary>
    public bool IsBlueVerified { get; private set; }
    /// <summary>旧来の認証バッジを持つか。</summary>
    public bool Verified { get; private set; }
    /// <summary>
    /// 組織アカウントなら 'Business' または 'Government'、それ以外は null。組織は
    /// <see cref="Verified"/> が false で、表示されるバッジはこのフィールドと <see cref="IsBlueVerified"/> に由来します。
    /// </summary>
    public string? VerifiedType { get; private set; }
    /// <summary>X が自己申告を求めるアカウントの 'Parody'、'Commentary'、'Fan'。それ以外は null。</summary>
    public string? ParodyCommentaryFanLabel { get; private set; }
    /// <summary>プロフィールに表示される affiliate ラベル。bot なら 'Automated'、無ければ null。</summary>
    public string? AutomatedLabel { get; private set; }
    /// <summary>X が自動化アカウントとして表示しているか。</summary>
    public bool IsAutomated { get; private set; }
    /// <summary>この bot を運用しているアカウントのスクリーンネーム（X が明示する場合）。</summary>
    public string? AutomatedBy { get; private set; }
    /// <summary>ログイン中のアカウントがこのユーザーの通知をオンにしているか。</summary>
    public bool NotificationsEnabled { get; private set; }
    /// <summary>センシティブな内容を含む可能性があるか。</summary>
    public bool PossiblySensitive { get; private set; }
    /// <summary>DM を受け取れるか。</summary>
    public bool CanDm { get; private set; }
    /// <summary>メディアにタグ付けできるか。</summary>
    public bool CanMediaTag { get; private set; }
    /// <summary>リツイートを希望しているか。</summary>
    public bool WantRetweets { get; private set; }
    /// <summary>既定のプロフィールか。</summary>
    public bool DefaultProfile { get; private set; }
    /// <summary>既定のプロフィール画像か。</summary>
    public bool DefaultProfileImage { get; private set; }
    /// <summary>カスタムタイムラインを持つか。</summary>
    public bool HasCustomTimelines { get; private set; }
    /// <summary>フォロワー数。</summary>
    public int FollowersCount { get; private set; }
    public int FastFollowersCount { get; private set; }
    public int NormalFollowersCount { get; private set; }
    /// <summary>フォロー数。</summary>
    public int FollowingCount { get; private set; }
    /// <summary>ログイン中のアカウントがこのユーザーをフォローしているか。</summary>
    public bool Following { get; private set; }
    /// <summary>このユーザーがログイン中のアカウントをフォローしているか。</summary>
    public bool FollowedBy { get; private set; }
    /// <summary>ログイン中のアカウントがこのユーザーをブロックしているか。</summary>
    public bool Blocking { get; private set; }
    /// <summary>このユーザーがログイン中のアカウントをブロックしているか。</summary>
    public bool BlockedBy { get; private set; }
    /// <summary>ログイン中のアカウントがこのユーザーをミュートしているか。</summary>
    public bool Muting { get; private set; }
    public bool LiveFollowing { get; private set; }
    /// <summary>いいねの数。</summary>
    public int FavouritesCount { get; private set; }
    /// <summary>リストに追加されている数。</summary>
    public int ListedCount { get; private set; }
    /// <summary>メディアの数。</summary>
    public int MediaCount { get; private set; }
    /// <summary>ツイート数。</summary>
    public int StatusesCount { get; private set; }
    /// <summary>翻訳者か。</summary>
    public bool IsTranslator { get; private set; }
    /// <summary>翻訳者の種類。</summary>
    public string TranslatorType { get; private set; } = "";
    /// <summary>プロフィールの警告表示の種類。</summary>
    public string ProfileInterstitialType { get; private set; } = "";
    /// <summary>内容が非表示になっている国。</summary>
    public List<string> WithheldInCountries { get; private set; } = new();
    /// <summary>鍵アカウントか。</summary>
    public bool Protected { get; private set; }

    public User(Client client, JsonObject data)
    {
        _client = client;
        Load(data);
    }

    private static readonly Regex AutomatedByRegex = new(@"Automated by @(\w+)", RegexOptions.Compiled);

    private void Load(JsonObject data)
    {
        Data = data;
        var legacy = data.Sub("legacy");
        // X moved several profile fields out of `legacy` into top-level objects
        // (core/avatar/location/verification/...); read those first, fall back to legacy.
        var core = data.Sub("core");
        var avatar = data.Sub("avatar");
        var banner = data.Sub("banner");
        var location = data.Sub("location");
        var verification = data.Sub("verification");
        var privacy = data.Sub("privacy");
        var dmPermissions = data.Sub("dm_permissions");
        var mediaPermissions = data.Sub("media_permissions");
        var profileBio = data.Sub("profile_bio");
        var website = data.Sub("website");
        var pinnedItems = data.Sub("pinned_items");
        var relationshipCounts = data.Sub("relationship_counts");
        var tweetCounts = data.Sub("tweet_counts");
        var actionCounts = data.Sub("action_counts");
        var profileMetadata = data.Sub("profile_metadata");
        var relationship = data.Sub("relationship_perspectives");
        var bioEntities = profileBio.Sub("entities");

        Id = data.Str("rest_id") ?? "";
        CreatedAt = FirstNonEmpty(core.Str("created_at"), legacy.Str("created_at")) ?? "";
        Name = FirstNonEmpty(core.Str("name"), legacy.Str("name")) ?? "";
        ScreenName = FirstNonEmpty(core.Str("screen_name"), legacy.Str("screen_name")) ?? "";
        ProfileImageUrl = FirstNonEmpty(avatar.Str("image_url"), legacy.Str("profile_image_url_https")) ?? "";
        ProfileBannerUrl = FirstNonEmpty(banner.Str("image_url"), legacy.Str("profile_banner_url"));
        Url = FirstNonEmpty(website.Str("url"), legacy.Str("url"));
        Location = FirstNonEmpty(location.Str("location"), legacy.Str("location")) ?? "";
        Description = FirstNonEmpty(profileBio.Str("description"), legacy.Str("description")) ?? "";
        var descriptionUrls = bioEntities.Sub("description").Arr("urls");
        DescriptionUrls = UrlEntity.ListFrom(descriptionUrls is { Count: > 0 }
            ? descriptionUrls
            : legacy.Sub("entities").Sub("description").Arr("urls"));
        var urls = bioEntities.Sub("url").Arr("urls");
        Urls = UrlEntity.ListFrom(urls is { Count: > 0 } ? urls : legacy.Sub("entities").Sub("url").Arr("urls"));
        var pinned = pinnedItems.Arr("tweet_ids_str");
        PinnedTweetIds = (pinned is { Count: > 0 } ? pinned : legacy.Arr("pinned_tweet_ids_str")).Strings();
        IsBlueVerified = data.BoolOr("is_blue_verified", false);
        Verified = verification.Bool("verified") ?? legacy.BoolOr("verified", false);
        // X split verification into a legacy boolean and a type. Organisations
        // come back with verified=false and verified_type='Business'.
        VerifiedType = verification.Has("verified_type") ? verification.Str("verified_type") : legacy.Str("verified_type");
        // X sends the string 'None' rather than null for an account with no label.
        var label = data.Str("parody_commentary_fan_label");
        ParodyCommentaryFanLabel = label is null or "None" ? null : label;
        // X marks bot accounts with an affiliate label whose long description
        // names the operator, e.g. "Automated by @billsnitzer".
        var highlighted = data.Sub("affiliates_highlighted_label");
        var labelData = highlighted.Sub("label");
        AutomatedLabel = labelData.Str("description");
        IsAutomated = AutomatedLabel == "Automated";
        var longDescription = labelData.Sub("longDescription").Str("text") ?? "";
        var match = AutomatedByRegex.Match(longDescription);
        AutomatedBy = match.Success ? match.Groups[1].Value : null;
        NotificationsEnabled = data.Sub("notifications_settings").BoolOr("notifications_enabled", false);
        PossiblySensitive = data.Bool("possibly_sensitive") ?? legacy.BoolOr("possibly_sensitive", false);
        CanDm = dmPermissions.Bool("can_dm") ?? legacy.BoolOr("can_dm", false);
        CanMediaTag = mediaPermissions.Bool("can_media_tag") ?? legacy.BoolOr("can_media_tag", false);
        WantRetweets = legacy.BoolOr("want_retweets", false);
        DefaultProfile = legacy.BoolOr("default_profile", false);
        DefaultProfileImage = legacy.BoolOr("default_profile_image", false);
        HasCustomTimelines = legacy.BoolOr("has_custom_timelines", false);
        FollowersCount = relationshipCounts.Int("followers") ?? legacy.IntOr("followers_count", 0);
        FastFollowersCount = legacy.IntOr("fast_followers_count", 0);
        NormalFollowersCount = legacy.IntOr("normal_followers_count", 0);
        FollowingCount = relationshipCounts.Int("following") ?? legacy.IntOr("friends_count", 0);
        FavouritesCount = actionCounts.Int("favorites_count") ?? legacy.IntOr("favourites_count", 0);
        ListedCount = legacy.IntOr("listed_count", 0);
        MediaCount = tweetCounts.Int("media_tweets") ?? legacy.IntOr("media_count", 0);
        StatusesCount = tweetCounts.Int("tweets") ?? legacy.IntOr("statuses_count", 0);
        IsTranslator = legacy.BoolOr("is_translator", false);
        TranslatorType = data.Sub("profile_translation").Str("translator_type") ?? legacy.Str("translator_type") ?? "";
        ProfileInterstitialType = profileMetadata.Str("profile_interstitial_type") ?? legacy.Str("profile_interstitial_type") ?? "";
        WithheldInCountries = legacy.Arr("withheld_in_countries").Strings();
        Protected = privacy.Bool("protected") ?? legacy.BoolOr("protected", false);
        // X moved the viewer's relationship with this account into
        // relationship_perspectives; the legacy flags are the old home.
        Following = relationship.Bool("following") ?? legacy.BoolOr("following", false);
        FollowedBy = relationship.Bool("followed_by") ?? legacy.BoolOr("followed_by", false);
        Blocking = relationship.Bool("blocking") ?? legacy.BoolOr("blocking", false);
        BlockedBy = relationship.Bool("blocked_by") ?? legacy.BoolOr("blocked_by", false);
        Muting = relationship.Bool("muting") ?? legacy.BoolOr("muting", false);
        LiveFollowing = relationship.Bool("live_following") ?? legacy.BoolOr("live_following", false);
    }

    internal static string? FirstNonEmpty(string? a, string? b) => !string.IsNullOrEmpty(a) ? a : b;

    /// <summary><see cref="CreatedAt"/> を <see cref="DateTimeOffset"/> にしたもの。</summary>
    public DateTimeOffset CreatedAtDatetime => Utils.TimestampToDateTime(CreatedAt);

    /// <summary>
    /// ユーザーのツイートを取得します。
    /// </summary>
    /// <param name="tweetType">'Tweets'、'Replies'、'Media'、'Likes' のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    public Task<Result<Tweet>> GetTweetsAsync(string tweetType, int count = 40)
        => _client.GetUserTweetsAsync(Id, tweetType, count);

    /// <summary>フォローします。</summary>
    public Task<User> FollowAsync() => _client.FollowUserAsync(Id);

    /// <summary>フォローを解除します。</summary>
    public Task<User> UnfollowAsync() => _client.UnfollowUserAsync(Id);

    /// <summary>ブロックします。</summary>
    public Task<User> BlockAsync() => _client.BlockUserAsync(Id);

    /// <summary>ブロックを解除します。</summary>
    public Task<User> UnblockAsync() => _client.UnblockUserAsync(Id);

    /// <summary>ミュートします。</summary>
    public Task<User> MuteAsync() => _client.MuteUserAsync(Id);

    /// <summary>ミュートを解除します。</summary>
    public Task<User> UnmuteAsync() => _client.UnmuteUserAsync(Id);

    /// <summary>フォロワーを取得します。</summary>
    public Task<Result<User>> GetFollowersAsync(int count = 20) => _client.GetUserFollowersAsync(Id, count);

    /// <summary>認証済みのフォロワーを取得します。</summary>
    public Task<Result<User>> GetVerifiedFollowersAsync(int count = 20) => _client.GetUserVerifiedFollowersAsync(Id, count);

    /// <summary>知り合いかもしれないフォロワーを取得します。</summary>
    public Task<Result<User>> GetFollowersYouKnowAsync(int count = 20) => _client.GetUserFollowersYouKnowAsync(Id, count);

    /// <summary>フォロー中のユーザーを取得します。</summary>
    public Task<Result<User>> GetFollowingAsync(int count = 20) => _client.GetUserFollowingAsync(Id, count);

    /// <summary>サブスクライブしているユーザーを取得します。</summary>
    public Task<Result<User>> GetSubscriptionsAsync(int count = 20) => _client.GetUserSubscriptionsAsync(Id, count);

    /// <summary>最新のフォロワーを取得します（最大 200 件）。</summary>
    public Task<Result<User>> GetLatestFollowersAsync(int count = 200, string? cursor = null)
        => _client.GetLatestFollowersAsync(Id, null, count, cursor);

    /// <summary>最新のフォロー中ユーザーを取得します（最大 200 件）。</summary>
    public Task<Result<User>> GetLatestFriendsAsync(int count = 200, string? cursor = null)
        => _client.GetLatestFriendsAsync(Id, null, count, cursor);

    /// <summary>
    /// このユーザーに DM を送ります。
    /// </summary>
    /// <param name="text">本文。</param>
    /// <param name="mediaId">添付するメディア ID（<see cref="Client.UploadMediaAsync(string, bool, double?, string?, string?, bool)"/> で取得）。</param>
    /// <param name="replyTo">返信先のメッセージ ID。</param>
    public Task<Message> SendDmAsync(string text, string? mediaId = null, string? replyTo = null)
        => _client.SendDmAsync(Id, text, mediaId, replyTo);

    /// <summary>このユーザーとの DM 履歴を取得します。</summary>
    public Task<Result<Message>> GetDmHistoryAsync(string? maxId = null) => _client.GetDmHistoryAsync(Id, maxId);

    /// <summary>ハイライトされたツイートを取得します。</summary>
    public Task<Result<Tweet>> GetHighlightsTweetsAsync(int count = 20, string? cursor = null)
        => _client.GetUserHighlightsTweetsAsync(Id, count, cursor);

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetUserByIdAsync(Id).ConfigureAwait(false);
        Load(fresh.Data);
    }

    public override string ToString() => $"<User id=\"{Id}\">";
    public bool Equals(User? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as User);
    public override int GetHashCode() => Id.GetHashCode();
}
