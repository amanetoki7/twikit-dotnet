using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// リスト（Python 版の <c>twikit.List</c>。<c>System.Collections.Generic.List&lt;T&gt;</c> との混同を避けるため
/// C# では <see cref="TwitterList"/> という名前です）。
/// </summary>
public sealed class TwitterList : IEquatable<TwitterList>
{
    private readonly Client _client;

    /// <summary>リストの一意な ID。</summary>
    public string Id { get; private set; } = "";
    /// <summary>作成時刻（エポックミリ秒）。</summary>
    public long? CreatedAt { get; private set; }
    /// <summary>既定のバナー情報。</summary>
    public JsonObject? DefaultBanner { get; private set; }
    /// <summary>バナー情報。カスタムバナーが無ければ既定のバナー。</summary>
    public JsonObject? Banner { get; private set; }
    /// <summary>説明。</summary>
    public string? Description { get; private set; }
    /// <summary>ログイン中のユーザーがこのリストをフォローしているか。</summary>
    public bool Following { get; private set; }
    /// <summary>ログイン中のユーザーがこのリストのメンバーか。</summary>
    public bool IsMember { get; private set; }
    /// <summary>メンバー数。</summary>
    public int MemberCount { get; private set; }
    /// <summary>'Private' または 'Public'。</summary>
    public string? Mode { get; private set; }
    /// <summary>ミュートしているか。</summary>
    public bool Muting { get; private set; }
    /// <summary>リスト名。</summary>
    public string? Name { get; private set; }
    /// <summary>ピン留めしているか。</summary>
    public bool Pinning { get; private set; }
    /// <summary>購読者数。</summary>
    public int SubscriberCount { get; private set; }

    /// <exception cref="NotFoundException">X が解決できなかったリスト（ID だけの殻）。</exception>
    public TwitterList(Client client, JsonObject data)
    {
        _client = client;
        Load(data);
    }

    private void Load(JsonObject data)
    {
        // A deleted or unknown id gets a shell back that still carries the id
        // it was asked about and nothing else. `name` is the marker: every
        // list X actually resolves has one.
        if (string.IsNullOrEmpty(data.Str("id_str")) || data.Get("name") is null)
            throw new NotFoundException("The list does not exist.");

        Id = data.Str("id_str")!;
        CreatedAt = data.Long("created_at");
        DefaultBanner = data.Sub("default_banner_media").Obj("media_info");
        Banner = data.ContainsKey("custom_banner_media")
            ? data.Sub("custom_banner_media").Obj("media_info")
            : DefaultBanner;
        Description = data.Str("description");
        Following = data.BoolOr("following", false);
        IsMember = data.BoolOr("is_member", false);
        MemberCount = data.IntOr("member_count", 0);
        Mode = data.Str("mode");
        Muting = data.BoolOr("muting", false);
        Name = data.Str("name");
        Pinning = data.BoolOr("pinning", false);
        SubscriberCount = data.IntOr("subscriber_count", 0);
    }

    /// <summary><see cref="CreatedAt"/> を <see cref="DateTimeOffset"/> にしたもの（無ければ null）。</summary>
    public DateTimeOffset? CreatedAtDatetime
    {
        get
        {
            if (CreatedAt is null) return null;
            // X has shipped this as seconds and as milliseconds at different
            // times; anything past ~5138 AD in seconds is really milliseconds.
            var value = CreatedAt.Value;
            return value > 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
    }

    /// <summary>バナー画像を変更します。</summary>
    public Task<HttpResponseMessage> EditBannerAsync(string mediaId) => _client.EditListBannerAsync(Id, mediaId);

    /// <summary>バナー画像を削除します。</summary>
    public Task<HttpResponseMessage> DeleteBannerAsync() => _client.DeleteListBannerAsync(Id);

    /// <summary>リストの情報を編集します。</summary>
    public Task<TwitterList> EditAsync(string? name = null, string? description = null, bool? isPrivate = null)
        => _client.EditListAsync(Id, name, description, isPrivate);

    /// <summary>メンバーを追加します。</summary>
    public Task<TwitterList> AddMemberAsync(string userId) => _client.AddListMemberAsync(Id, userId);

    /// <summary>メンバーを削除します。</summary>
    public Task<TwitterList> RemoveMemberAsync(string userId) => _client.RemoveListMemberAsync(Id, userId);

    /// <summary>リストのツイートを取得します。</summary>
    public Task<Result<Tweet>> GetTweetsAsync(int count = 20, string? cursor = null) => _client.GetListTweetsAsync(Id, count, cursor);

    /// <summary>メンバーを取得します。</summary>
    public Task<Result<User>> GetMembersAsync(int count = 20, string? cursor = null) => _client.GetListMembersAsync(Id, count, cursor);

    /// <summary>購読者を取得します。</summary>
    public Task<Result<User>> GetSubscribersAsync(int count = 20, string? cursor = null) => _client.GetListSubscribersAsync(Id, count, cursor);

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetListAsync(Id).ConfigureAwait(false);
        Id = fresh.Id; CreatedAt = fresh.CreatedAt; DefaultBanner = fresh.DefaultBanner; Banner = fresh.Banner;
        Description = fresh.Description; Following = fresh.Following; IsMember = fresh.IsMember; MemberCount = fresh.MemberCount;
        Mode = fresh.Mode; Muting = fresh.Muting; Name = fresh.Name; Pinning = fresh.Pinning; SubscriberCount = fresh.SubscriberCount;
    }

    public override string ToString() => $"<List id=\"{Id}\">";
    public bool Equals(TwitterList? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as TwitterList);
    public override int GetHashCode() => Id.GetHashCode();
}
