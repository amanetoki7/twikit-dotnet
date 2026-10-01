using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

public partial class Client
{
    /// <summary>
    /// リストを作成します。
    /// </summary>
    /// <param name="name">リスト名。</param>
    /// <param name="description">説明。</param>
    /// <param name="isPrivate">非公開にするか。</param>
    public async Task<TwitterList> CreateListAsync(string name, string description = "", bool isPrivate = false)
    {
        var response = await Gql.CreateListAsync(name, description, isPrivate).ConfigureAwait(false);
        if (response.Json.FirstDict("list") is not JsonObject listInfo) throw new NotFoundException("The list does not exist.");
        return new TwitterList(this, listInfo);
    }

    /// <summary>リストを削除します。</summary>
    public async Task<HttpResponseMessage> DeleteListAsync(string listId)
        => (await Gql.DeleteListAsync(listId).ConfigureAwait(false)).Http;

    /// <summary>リストのバナー画像を変更します。</summary>
    public async Task<HttpResponseMessage> EditListBannerAsync(string listId, string mediaId)
        => (await Gql.EditListBannerAsync(listId, mediaId).ConfigureAwait(false)).Http;

    /// <summary>リストのバナー画像を削除します。</summary>
    public async Task<HttpResponseMessage> DeleteListBannerAsync(string listId)
        => (await Gql.DeleteListBannerAsync(listId).ConfigureAwait(false)).Http;

    /// <summary>
    /// リストの情報を編集します。
    /// </summary>
    public async Task<TwitterList> EditListAsync(string listId, string? name = null, string? description = null, bool? isPrivate = null)
    {
        var response = await Gql.UpdateListAsync(listId, name, description, isPrivate).ConfigureAwait(false);
        if (response.Json.FirstDict("list") is not JsonObject listInfo) throw new NotFoundException("The list does not exist.");
        return new TwitterList(this, listInfo);
    }

    /// <summary>リストにユーザーを追加します。</summary>
    public async Task<TwitterList> AddListMemberAsync(string listId, string userId)
    {
        var response = await Gql.ListAddMemberAsync(listId, userId).ConfigureAwait(false);
        var list = response.Json.Get("data").Get("list") as JsonObject ?? throw new NotFoundException("The list does not exist.");
        return new TwitterList(this, list);
    }

    /// <summary>リストからユーザーを削除します。</summary>
    public async Task<TwitterList> RemoveListMemberAsync(string listId, string userId)
    {
        var response = await Gql.ListRemoveMemberAsync(listId, userId).ConfigureAwait(false);
        var errors = JsonExtensions.FatalErrors(response.Json, "list");
        if (errors is not null) throw new TwitterException(errors.ErrorMessage("Failed to remove the list member."));
        var list = response.Json.Get("data").Get("list") as JsonObject ?? throw new NotFoundException("The list does not exist.");
        return new TwitterList(this, list);
    }

    /// <summary>
    /// ログイン中のユーザーのリストを取得します。
    /// </summary>
    public async Task<Result<TwitterList>> GetListsAsync(int count = 100, string? cursor = null)
    {
        var response = await Gql.ListManagementPaceTimelineAsync(count, cursor).ConfigureAwait(false);

        // X can answer with a viewer shell and an error instead of the timeline
        // (code 214, DecodeException); `data` is truthy there.
        var errors = JsonExtensions.FatalErrors(response.Json, "entries");
        if (errors is not null) throw new TwitterException(errors.ErrorMessage("Failed to retrieve the lists."));

        if (response.Json.FirstDict("entries") is not JsonArray entries || entries.Count == 0)
            return Result<TwitterList>.Empty();

        // The cursor is read before anything can bail out: a page that yields
        // no lists is not the end of the collection.
        var nextCursor = entries[^1].Sub("content").Str("value");

        var lists = new List<TwitterList>();
        var items = entries.FindDict("items");
        var cells = items.Count >= 2 ? items[1] as JsonArray : null;
        foreach (var item in cells.Objects())
        {
            var listData = item.Sub("item").Sub("itemContent").Obj("list");
            if (listData is null) continue;
            try
            {
                lists.Add(new TwitterList(this, listData));
            }
            catch (NotFoundException)
            {
                // A cell can carry a `list` that X did not resolve; skip it.
            }
        }

        return Page(lists, count, nextCursor, () => GetListsAsync(count, nextCursor));
    }

    /// <summary>ID を指定してリストを取得します。</summary>
    public async Task<TwitterList> GetListAsync(string listId)
    {
        var response = await Gql.ListByRestIdAsync(listId).ConfigureAwait(false);
        if (response.Json.FirstDict("list") is not JsonObject listData) throw new ArgumentException($"Invalid list id: {listId}", nameof(listId));
        return new TwitterList(this, listData);
    }

    /// <summary>
    /// リストのツイートを取得します。
    /// </summary>
    public async Task<Result<Tweet>> GetListTweetsAsync(string listId, int count = 20, string? cursor = null)
    {
        var response = await Gql.ListLatestTweetsTimelineAsync(listId, count, cursor).ConfigureAwait(false);

        if (response.Json.FirstDict("entries") is not JsonArray items)
            throw new ArgumentException($"Invalid list id: {listId}", nameof(listId));
        var nextCursor = JsonExtensions.LastCursor(items);

        var results = new List<Tweet>();

        void HandleItem(JsonNode item, List<string>? conversationIds = null)
        {
            var tweet = Tweet.FromData(this, item);
            if (tweet is null) return;
            tweet.ConversationIds = conversationIds;
            results.Add(tweet);
        }

        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (entryId.StartsWith("tweet", StringComparison.Ordinal))
            {
                HandleItem(item);
            }
            else if (entryId.StartsWith("list-conversation", StringComparison.Ordinal))
            {
                var conversationIds = ConversationIds(item.Get("content"));
                foreach (var subItem in item.Sub("content").ArrOrEmpty("items").Objects())
                    HandleItem(subItem, conversationIds);
            }
        }

        return Page(results, count, nextCursor, () => GetListTweetsAsync(listId, count, nextCursor));
    }

    private async Task<Result<User>> GetListUsersAsync(Func<string, int, string?, Task<ApiResponse>> f, string listId, int count, string? cursor)
    {
        var response = await f(listId, count, cursor).ConfigureAwait(false);

        string? nextCursor = null;
        var items = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var results = new List<User>();
        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (entryId.StartsWith("user", StringComparison.Ordinal))
            {
                // An entry X could not resolve (deleted or restricted user) arrives without `result`.
                if (item.FirstDict("result") is not JsonObject userInfo) continue;
                results.Add(new User(this, userInfo));
            }
            else if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal))
            {
                nextCursor = item.Sub("content").Str("value");
                break;
            }
        }

        return Page(results, count, nextCursor, () => GetListUsersAsync(f, listId, count, nextCursor));
    }

    /// <summary>リストのメンバーを取得します。</summary>
    public Task<Result<User>> GetListMembersAsync(string listId, int count = 20, string? cursor = null)
        => GetListUsersAsync(Gql.ListMembersAsync, listId, count, cursor);

    /// <summary>リストの購読者を取得します。</summary>
    public Task<Result<User>> GetListSubscribersAsync(string listId, int count = 20, string? cursor = null)
        => GetListUsersAsync(Gql.ListSubscribersAsync, listId, count, cursor);

    /// <summary>
    /// リストを検索します。
    /// </summary>
    public async Task<Result<TwitterList>> SearchListAsync(string query, int count = 20, string? cursor = null)
    {
        var response = await Gql.SearchTimelineAsync(query, "Lists", count, cursor).ConfigureAwait(false);
        var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();

        JsonArray items;
        if (cursor is null)
            items = entries.Count > 0 ? entries[0].Sub("content").ArrOrEmpty("items") : new JsonArray();
        else
            items = response.Json.FirstDict("moduleItems") as JsonArray ?? new JsonArray();

        var lists = new List<TwitterList>();
        foreach (var item in items.Objects())
        {
            var listData = item.Sub("item").Sub("itemContent").Obj("list");
            if (listData is null || listData.Count == 0) continue;
            try
            {
                lists.Add(new TwitterList(this, listData));
            }
            catch (NotFoundException)
            {
                // skip unresolved cells
            }
        }
        var nextCursor = JsonExtensions.LastCursor(entries);

        return Page(lists, count, nextCursor, () => SearchListAsync(query, count, nextCursor));
    }
}
