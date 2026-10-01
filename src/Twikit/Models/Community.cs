using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>コミュニティの作成者（完全なユーザー情報が無い場合）。</summary>
public sealed record CommunityCreator(string Id, string? ScreenName, bool Verified);

/// <summary>コミュニティのルール。</summary>
public sealed record CommunityRule(string Id, string? Name);

/// <summary>
/// コミュニティのメンバー。
/// </summary>
public sealed class CommunityMember : IEquatable<CommunityMember>
{
    private readonly Client _client;

    public string Id { get; }
    public string? CommunityRole { get; }
    public bool SuperFollowing { get; }
    public bool SuperFollowEligible { get; }
    public bool SuperFollowedBy { get; }
    public bool SmartBlocking { get; }
    public bool IsBlueVerified { get; }
    public string? ScreenName { get; }
    public string? Name { get; }
    public bool FollowRequestSent { get; }
    public bool Protected { get; }
    public bool Following { get; }
    public bool FollowedBy { get; }
    public bool Blocking { get; }
    public string? ProfileImageUrlHttps { get; }
    public bool Verified { get; }

    /// <exception cref="NotFoundException">X が解決できなかったメンバー（rest_id 無し）。</exception>
    public CommunityMember(Client client, JsonObject data)
    {
        _client = client;
        // A member entry X could not resolve arrives without rest_id.
        if (string.IsNullOrEmpty(data.Str("rest_id")))
            throw new NotFoundException("The community member does not exist.");
        Id = data.Str("rest_id")!;

        CommunityRole = data.Str("community_role");
        SuperFollowing = data.BoolOr("super_following", false);
        SuperFollowEligible = data.BoolOr("super_follow_eligible", false);
        SuperFollowedBy = data.BoolOr("super_followed_by", false);
        SmartBlocking = data.BoolOr("smart_blocking", false);
        IsBlueVerified = data.BoolOr("is_blue_verified", false);

        // The current documents drop `legacy` and split the same fields across
        // typed objects, so both shapes have to be read.
        var legacy = data.Sub("legacy");
        var core = data.Sub("core");
        var avatar = data.Sub("avatar");
        var privacy = data.Sub("privacy");
        var verification = data.Sub("verification");
        var relationship = data.Sub("relationship_perspectives");

        ScreenName = User.FirstNonEmpty(core.Str("screen_name"), legacy.Str("screen_name"));
        Name = User.FirstNonEmpty(core.Str("name"), legacy.Str("name"));
        FollowRequestSent = data.Bool("follow_request_sent") ?? legacy.BoolOr("follow_request_sent", false);
        Protected = privacy.Bool("protected") ?? legacy.BoolOr("protected", false);
        Following = relationship.Bool("following") ?? legacy.BoolOr("following", false);
        FollowedBy = relationship.Bool("followed_by") ?? legacy.BoolOr("followed_by", false);
        Blocking = relationship.Bool("blocking") ?? legacy.BoolOr("blocking", false);
        ProfileImageUrlHttps = User.FirstNonEmpty(avatar.Str("image_url"), legacy.Str("profile_image_url_https"));
        Verified = verification.Bool("verified") ?? legacy.BoolOr("verified", false);
    }

    public override string ToString() => $"<CommunityMember id=\"{Id}\">";
    public bool Equals(CommunityMember? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as CommunityMember);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// コミュニティ。
/// </summary>
public sealed class Community : IEquatable<Community>
{
    private readonly Client _client;

    /// <summary>コミュニティの ID。</summary>
    public string Id { get; private set; } = "";
    /// <summary>名前。</summary>
    public string? Name { get; private set; }
    /// <summary>メンバー数。</summary>
    public int MemberCount { get; private set; }
    /// <summary>NSFW か。</summary>
    public bool IsNsfw { get; private set; }
    /// <summary>メンバーのプロフィール画像 URL。</summary>
    public List<string?> MembersFacepileResults { get; private set; } = new();
    /// <summary>バナー情報。</summary>
    public JsonObject? Banner { get; private set; }
    /// <summary>ログイン中のユーザーがメンバーか。</summary>
    public bool IsMember { get; private set; }
    /// <summary>ログイン中のユーザーの役割。</summary>
    public string? Role { get; private set; }
    /// <summary>説明。</summary>
    public string? Description { get; private set; }
    /// <summary>作成者（完全なユーザー情報がある場合）。</summary>
    public User? Creator { get; private set; }
    /// <summary>作成者（ID とスクリーンネームだけの場合）。</summary>
    public CommunityCreator? CreatorInfo { get; private set; }
    /// <summary>管理者。</summary>
    public User? Admin { get; private set; }
    /// <summary>参加ポリシー。</summary>
    public string? JoinPolicy { get; private set; }
    /// <summary>作成時刻。</summary>
    public long? CreatedAt { get; private set; }
    /// <summary>招待ポリシー。</summary>
    public string? InvitesPolicy { get; private set; }
    /// <summary>ピン留めされているか。</summary>
    public bool IsPinned { get; private set; }
    /// <summary>ルール（無ければ null）。</summary>
    public List<CommunityRule>? Rules { get; private set; }

    /// <exception cref="NotFoundException">X が解決できなかったコミュニティ。</exception>
    public Community(Client client, JsonObject data)
    {
        _client = client;
        Load(data);
    }

    private void Load(JsonObject data)
    {
        // X answers an unknown community id with an empty result.
        if (string.IsNullOrEmpty(data.Str("rest_id")))
            throw new NotFoundException("The community does not exist.");
        Id = data.Str("rest_id")!;
        Name = data.Str("name");
        MemberCount = data.IntOr("member_count", 0);
        IsNsfw = data.BoolOr("is_nsfw", false);
        MembersFacepileResults = data.ArrOrEmpty("members_facepile_results").Objects()
            .Select(i => i.Sub("result").Sub("avatar").Str("image_url")
                         ?? i.Sub("result").Sub("legacy").Str("profile_image_url_https"))
            .ToList();
        Banner = data.Sub("default_banner_media").Obj("media_info");
        IsMember = data.BoolOr("is_member", false);
        Role = data.Str("role");
        Description = data.Str("description");

        Creator = null;
        CreatorInfo = null;
        if (data.ContainsKey("creator_results"))
        {
            var creator = data.Sub("creator_results").Sub("result");
            if (creator.ContainsKey("rest_id"))
            {
                Creator = new User(_client, creator);
            }
            else
            {
                var encoded = creator.Str("id") ?? "";
                string decoded;
                try { decoded = Utils.B64ToStr(encoded); }
                catch (FormatException) { decoded = encoded; }
                if (decoded.StartsWith("User:", StringComparison.Ordinal)) decoded = decoded.Substring("User:".Length);
                CreatorInfo = new CommunityCreator(
                    decoded,
                    creator.Sub("core").Str("screen_name") ?? creator.Sub("legacy").Str("screen_name"),
                    creator.Sub("verification").Bool("verified") ?? creator.Sub("legacy").BoolOr("verified", false));
            }
        }

        Admin = data.ContainsKey("admin_results") ? new User(_client, data.Sub("admin_results").Sub("result")) : null;
        JoinPolicy = data.Str("join_policy");
        CreatedAt = data.Long("created_at");
        InvitesPolicy = data.Str("invites_policy");
        IsPinned = data.BoolOr("is_pinned", false);
        Rules = data.ContainsKey("rules")
            ? data.ArrOrEmpty("rules").Objects().Select(r => new CommunityRule(r.Str("rest_id") ?? "", r.Str("name"))).ToList()
            : null;
    }

    /// <summary>コミュニティのツイートを取得します。</summary>
    /// <param name="tweetType">'Top'、'Latest'、'Media' のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public Task<Result<Tweet>> GetTweetsAsync(string tweetType, int count = 40, string? cursor = null)
        => _client.GetCommunityTweetsAsync(Id, tweetType, count, cursor);

    /// <summary>コミュニティに参加します。</summary>
    public Task<Community> JoinAsync() => _client.JoinCommunityAsync(Id);

    /// <summary>コミュニティから退出します。</summary>
    public Task<Community> LeaveAsync() => _client.LeaveCommunityAsync(Id);

    /// <summary>参加をリクエストします。</summary>
    public Task<Community> RequestToJoinAsync(string? answer = null) => _client.RequestToJoinCommunityAsync(Id, answer);

    /// <summary>メンバーを取得します。</summary>
    public Task<Result<CommunityMember>> GetMembersAsync(int count = 20, string? cursor = null)
        => _client.GetCommunityMembersAsync(Id, count, cursor);

    /// <summary>モデレーターを取得します。</summary>
    public Task<Result<CommunityMember>> GetModeratorsAsync(int count = 20, string? cursor = null)
        => _client.GetCommunityModeratorsAsync(Id, count, cursor);

    /// <summary>コミュニティ内のツイートを検索します。</summary>
    public Task<Result<Tweet>> SearchTweetAsync(string query, int count = 20, string? cursor = null)
        => _client.SearchCommunityTweetAsync(Id, query, count, cursor);

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetCommunityAsync(Id).ConfigureAwait(false);
        Id = fresh.Id; Name = fresh.Name; MemberCount = fresh.MemberCount; IsNsfw = fresh.IsNsfw;
        MembersFacepileResults = fresh.MembersFacepileResults; Banner = fresh.Banner; IsMember = fresh.IsMember; Role = fresh.Role;
        Description = fresh.Description; Creator = fresh.Creator; CreatorInfo = fresh.CreatorInfo; Admin = fresh.Admin;
        JoinPolicy = fresh.JoinPolicy; CreatedAt = fresh.CreatedAt; InvitesPolicy = fresh.InvitesPolicy; IsPinned = fresh.IsPinned; Rules = fresh.Rules;
    }

    public override string ToString() => $"<Community id=\"{Id}\">";
    public bool Equals(Community? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Community);
    public override int GetHashCode() => Id.GetHashCode();
}
