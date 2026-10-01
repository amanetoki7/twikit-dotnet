using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// ブックマークフォルダー。
/// </summary>
public sealed class BookmarkFolder : IEquatable<BookmarkFolder>
{
    private readonly Client _client;

    /// <summary>フォルダーの ID。</summary>
    public string Id { get; }
    /// <summary>フォルダー名。</summary>
    public string? Name { get; }
    /// <summary>アイコン画像の情報。</summary>
    public JsonObject? Media { get; }

    public BookmarkFolder(Client client, JsonObject data)
    {
        _client = client;
        Id = data.Str("id") ?? "";
        Name = data.Str("name");
        Media = data.Obj("media");
    }

    /// <summary>フォルダー内のツイートを取得します。</summary>
    public Task<Result<Tweet>> GetTweetsAsync(string? cursor = null) => _client.GetBookmarksAsync(20, cursor, Id);

    /// <summary>フォルダー名を変更します。</summary>
    public Task<BookmarkFolder> EditAsync(string name) => _client.EditBookmarkFolderAsync(Id, name);

    /// <summary>フォルダーを削除します。</summary>
    public Task<HttpResponseMessage> DeleteAsync() => _client.DeleteBookmarkFolderAsync(Id);

    /// <summary>フォルダーにツイートを追加します。</summary>
    public Task<HttpResponseMessage> AddAsync(string tweetId) => _client.BookmarkTweetAsync(tweetId, Id);

    public override string ToString() => $"<BookmarkFolder id=\"{Id}\">";
    public bool Equals(BookmarkFolder? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as BookmarkFolder);
    public override int GetHashCode() => Id.GetHashCode();
}
