using System.Globalization;
using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

public partial class Client
{
    /// <summary>
    /// スクリーンネームを指定してユーザーを取得します。
    /// </summary>
    /// <exception cref="UserNotFoundException">ユーザーが存在しない。</exception>
    /// <exception cref="UserUnavailableException">ユーザーが利用できない（凍結など）。</exception>
    public async Task<User> GetUserByScreenNameAsync(string screenName)
    {
        var response = await Gql.UserByScreenNameAsync(screenName).ConfigureAwait(false);
        var data = response.Json.Get("data");
        if (!data.Has("user")) throw new UserNotFoundException("The user does not exist.");
        var userData = data.Get("user").Sub("result");
        if (userData.Count == 0) throw new UserNotFoundException("The user does not exist.");
        if (userData.Str("__typename") == "UserUnavailable")
            throw new UserUnavailableException(userData.Str("message"));
        return new User(this, userData);
    }

    /// <summary>
    /// ID を指定してユーザーを取得します。
    /// </summary>
    /// <exception cref="TwitterException">ユーザー ID が不正。</exception>
    /// <exception cref="UserUnavailableException">ユーザーが利用できない（凍結など）。</exception>
    public async Task<User> GetUserByIdAsync(string userId)
    {
        var response = await Gql.UserByRestIdAsync(userId).ConfigureAwait(false);
        var user = response.Json.Get("data").Get("user");
        if (!user.Has("result")) throw new TwitterException($"Invalid user id: {userId}");
        var userData = user.Get("result") as JsonObject ?? new JsonObject();
        if (userData.Str("__typename") == "UserUnavailable")
            throw new UserUnavailableException(userData.Str("message"));
        return new User(this, userData);
    }

    /// <summary>
    /// ログイン中のアカウントのプロフィールを更新します。渡した項目だけが変わり、null のままの項目は
    /// 現在の値を保ちます。空文字を渡すとその項目を消せます。
    /// </summary>
    /// <param name="name">表示名（50 文字まで）。</param>
    /// <param name="description">自己紹介。</param>
    /// <param name="location">場所。</param>
    /// <param name="url">プロフィールに表示する Web サイト。</param>
    public async Task<User> UpdateProfileAsync(string? name = null, string? description = null, string? location = null, string? url = null)
    {
        var fields = new Dictionary<string, string>();
        if (name is not null) fields["name"] = name;
        if (description is not null) fields["description"] = description;
        if (location is not null) fields["location"] = location;
        if (url is not null) fields["url"] = url;
        if (fields.Count == 0) throw new ArgumentException("Nothing to update.");
        if (name is not null && name.Length > 50) throw new ArgumentException("`name` must be at most 50 characters.", nameof(name));

        var response = await V11.UpdateProfileAsync(fields).ConfigureAwait(false);
        return new User(this, Utils.BuildUserData(response.Object ?? new JsonObject()));
    }

    /// <summary>
    /// ユーザーの「このアカウントについて」パネル（<c>account_based_in</c>、<c>source</c>、<c>username_changes</c> など）を取得します。
    /// 表示する情報が無いアカウントでは空のオブジェクトです。
    /// </summary>
    /// <exception cref="UserNotFoundException">ユーザーが存在しない。</exception>
    public async Task<JsonObject> GetAboutAccountAsync(string screenName)
    {
        var response = await Gql.AboutAccountAsync(screenName).ConfigureAwait(false);
        var data = response.Json.Sub("data");
        // X answers an unresolvable handle with an entirely empty `data` and
        // no error, while a real account always comes back under
        // user_result_by_screen_name - even when the panel itself is empty.
        if (!data.ContainsKey("user_result_by_screen_name")) throw new UserNotFoundException("The user does not exist.");
        return data.Sub("user_result_by_screen_name").Sub("result").Sub("about_profile");
    }

    /// <summary>
    /// プロフィールにピン留めされたスポットライトモジュール（プロアカウントがタイムラインの上に表示するパネル）を取得します。
    /// </summary>
    /// <exception cref="UserNotFoundException">ユーザーが存在しない。</exception>
    public async Task<List<JsonNode?>> GetUserSpotlightsAsync(string screenName)
    {
        var response = await Gql.ProfileSpotlightsAsync(screenName).ConfigureAwait(false);
        var data = response.Json.Sub("data");
        if (!data.ContainsKey("user_result_by_screen_name")) throw new UserNotFoundException("The user does not exist.");
        var result = data.Sub("user_result_by_screen_name").Sub("result");
        var modules = result.Sub("profilemodules").Get("v1");
        return modules is JsonArray a ? a.ToList() : new List<JsonNode?>();
    }

    private async Task<User> UserFromV11Async(Task<ApiResponse> request)
    {
        var response = await request.ConfigureAwait(false);
        return new User(this, Utils.BuildUserData(response.Object ?? new JsonObject()));
    }

    /// <summary>ユーザーをフォローします。</summary>
    public Task<User> FollowUserAsync(string userId) => UserFromV11Async(V11.CreateFriendshipsAsync(userId));

    /// <summary>ユーザーのフォローを解除します。</summary>
    public Task<User> UnfollowUserAsync(string userId) => UserFromV11Async(V11.DestroyFriendshipsAsync(userId));

    /// <summary>ユーザーをブロックします。</summary>
    public Task<User> BlockUserAsync(string userId) => UserFromV11Async(V11.CreateBlocksAsync(userId));

    /// <summary>ユーザーのブロックを解除します。</summary>
    public Task<User> UnblockUserAsync(string userId) => UserFromV11Async(V11.DestroyBlocksAsync(userId));

    /// <summary>ユーザーをミュートします。</summary>
    public Task<User> MuteUserAsync(string userId) => UserFromV11Async(V11.CreateMutesAsync(userId));

    /// <summary>ユーザーのミュートを解除します。</summary>
    public Task<User> UnmuteUserAsync(string userId) => UserFromV11Async(V11.DestroyMutesAsync(userId));

    private async Task<Result<User>> GetUserFriendshipAsync(string? userId, int count,
        Func<string?, int, string?, Task<ApiResponse>> f, string? cursor)
    {
        var response = await f(userId, count, cursor).ConfigureAwait(false);

        // A protected (or suspended/deactivated) account answers with a bare
        // UserUnavailable and no timeline.
        var userResult = response.Json.Get("data").Sub("user").Sub("result");
        if (userResult.Str("__typename") == "UserUnavailable")
            throw new UserUnavailableException(userResult.Str("message") ?? "The account is protected, suspended or deactivated.");

        string? nextCursor = null;
        if (response.Json.FirstDict("entries") is not JsonArray items) return Result<User>.Empty();
        var results = new List<User>();
        foreach (var item in items.Objects())
        {
            var entryId = item.EntryId();
            if (entryId.StartsWith("user", StringComparison.Ordinal))
            {
                var userInfo = item.FindDict("result", findOne: true);
                if (userInfo.Count == 0 || userInfo[0] is not JsonObject info)
                {
                    TwikitWarnings.Warn(
                        "Some followers are excluded because \"Quality Filter\" is enabled. " +
                        "To get all followers, turn off it in the Twitter settings.");
                    continue;
                }
                if (info.Str("__typename") == "UserUnavailable") continue;
                results.Add(new User(this, info));
            }
            else if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal))
            {
                nextCursor = item.Sub("content").Str("value");
            }
        }

        // X ignores `count` here the same way it does on timelines.
        return Page(results, count, nextCursor, () => GetUserFriendshipAsync(userId, count, f, nextCursor));
    }

    private static string? V11Cursor(JsonNode? response, string key)
    {
        var value = response.Get(key).AsStr();
        // v1.1 reports the end of the walk as cursor 0.
        return value is null or "0" or "" ? null : value;
    }

    private async Task<Result<User>> GetUserFriendship2Async(string? userId, string? screenName, int count,
        Func<string?, string?, int, string?, Task<ApiResponse>> f, string? cursor)
    {
        var response = await f(userId, screenName, count, cursor).ConfigureAwait(false);
        var users = response.Json.ArrOrEmpty("users");
        var results = users.Objects().Select(u => new User(this, Utils.BuildUserData(u))).ToList();

        var previousCursor = V11Cursor(response.Json, "previous_cursor");
        var nextCursor = V11Cursor(response.Json, "next_cursor");

        return Page(results, count, nextCursor,
            () => GetUserFriendship2Async(userId, screenName, count, f, nextCursor),
            previousCursor,
            () => GetUserFriendship2Async(userId, screenName, count, f, previousCursor));
    }

    /// <summary>ログイン中のユーザーがミュートしているアカウントを取得します。</summary>
    public Task<Result<User>> GetMutedUsersAsync(int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(null, count, (_, c, cur) => Gql.MutedAccountsAsync(c, cur), cursor);

    /// <summary>ログイン中のユーザーがブロックしているアカウントを取得します。</summary>
    public Task<Result<User>> GetBlockedUsersAsync(int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(null, count, (_, c, cur) => Gql.BlockedAccountsAsync(c, cur), cursor);

    /// <summary>
    /// 他のユーザーが所有・購読しているリストを取得します。<see cref="GetListsAsync"/> はログイン中の
    /// アカウントにしか届きませんが、これは誰の公開リストでも読めます。
    /// </summary>
    public async Task<Result<TwitterList>> GetUserListsAsync(string userId, int count = 100, string? cursor = null)
    {
        var response = await Gql.CombinedListsAsync(userId, count, cursor).ConfigureAwait(false);
        if (response.Json.FirstDict("entries") is not JsonArray entries) return Result<TwitterList>.Empty();

        var lists = new List<TwitterList>();
        string? nextCursor = null;
        foreach (var entry in entries.Objects())
        {
            var entryId = entry.EntryId();
            if (entryId.StartsWith("cursor-bottom", StringComparison.Ordinal))
            {
                nextCursor = entry.Sub("content").Str("value");
                continue;
            }
            if (entry.FirstDict("list") is JsonObject listData)
            {
                try
                {
                    lists.Add(new TwitterList(this, listData));
                }
                catch (NotFoundException)
                {
                    // An entry X did not resolve should cost that entry, not the rest of the page.
                }
            }
        }

        return Page(lists, count, nextCursor, () => GetUserListsAsync(userId, count, nextCursor));
    }

    /// <summary>ユーザーのフォロワーを取得します。</summary>
    public Task<Result<User>> GetUserFollowersAsync(string userId, int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.FollowersAsync(id!, c, cur), cursor);

    /// <summary>最新のフォロワーを取得します（最大 200 件）。</summary>
    public Task<Result<User>> GetLatestFollowersAsync(string? userId = null, string? screenName = null, int count = 200, string? cursor = null)
        => GetUserFriendship2Async(userId, screenName, count, V11.FollowersListAsync, cursor);

    /// <summary>
    /// 最新のフォロー中ユーザーを取得します（最大 200 件）。v1.1 の friends/list は廃止されたため、
    /// GraphQL の Following エンドポイントを使います。
    /// </summary>
    public async Task<Result<User>> GetLatestFriendsAsync(string? userId = null, string? screenName = null, int count = 200, string? cursor = null)
    {
        if (userId is null)
        {
            if (screenName is null) throw new ArgumentException("userId or screenName is required");
            userId = (await GetUserByScreenNameAsync(screenName).ConfigureAwait(false)).Id;
        }
        return await GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.FollowingAsync(id!, c, cur), cursor).ConfigureAwait(false);
    }

    /// <summary>ユーザーの認証済みフォロワーを取得します。</summary>
    public Task<Result<User>> GetUserVerifiedFollowersAsync(string userId, int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.BlueVerifiedFollowersAsync(id!, c, cur), cursor);

    /// <summary>知り合いかもしれない共通のフォロワーを取得します。</summary>
    public Task<Result<User>> GetUserFollowersYouKnowAsync(string userId, int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.FollowersYouKnowAsync(id!, c, cur), cursor);

    /// <summary>ユーザーがフォローしているユーザーを取得します。</summary>
    public Task<Result<User>> GetUserFollowingAsync(string userId, int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.FollowingAsync(id!, c, cur), cursor);

    /// <summary>ユーザーがサブスクライブしているユーザーを取得します。</summary>
    public Task<Result<User>> GetUserSubscriptionsAsync(string userId, int count = 20, string? cursor = null)
        => GetUserFriendshipAsync(userId, count, (id, c, cur) => Gql.UserCreatorSubscriptionsAsync(id!, c, cur), cursor);

    private async Task<Result<long>> GetFriendshipIdsAsync(string? userId, string? screenName, int count,
        Func<string?, string?, int, string?, Task<ApiResponse>> f, string? cursor)
    {
        var response = await f(userId, screenName, count, cursor).ConfigureAwait(false);
        var previousCursor = V11Cursor(response.Json, "previous_cursor");
        var nextCursor = V11Cursor(response.Json, "next_cursor");

        var ids = response.Json.ArrOrEmpty("ids").Select(i => i.AsLong() ?? 0).ToList();
        return Page(ids, count, nextCursor,
            () => GetFriendshipIdsAsync(userId, screenName, count, f, nextCursor),
            previousCursor,
            () => GetFriendshipIdsAsync(userId, screenName, count, f, previousCursor));
    }

    /// <summary>ユーザーのフォロワーの ID を取得します。</summary>
    public Task<Result<long>> GetFollowersIdsAsync(string? userId = null, string? screenName = null, int count = 5000, string? cursor = null)
        => GetFriendshipIdsAsync(userId, screenName, count, V11.FollowersIdsAsync, cursor);

    /// <summary>ユーザーがフォローしているユーザーの ID を取得します。</summary>
    public Task<Result<long>> GetFriendsIdsAsync(string? userId = null, string? screenName = null, int count = 5000, string? cursor = null)
        => GetFriendshipIdsAsync(userId, screenName, count, V11.FriendsIdsAsync, cursor);

    /// <summary>
    /// 緯度・経度から周辺の場所を検索します。
    /// </summary>
    /// <param name="lat">緯度。</param>
    /// <param name="lng">経度。</param>
    /// <param name="accuracy">検索する「範囲」のヒント。</param>
    /// <param name="granularity">返す場所の最小粒度: neighborhood、city、admin、country。</param>
    /// <param name="maxResults">返す件数のヒント。</param>
    public async Task<List<Place>> ReverseGeocodeAsync(double lat, double lng, string? accuracy = null, string? granularity = null, int? maxResults = null)
    {
        var response = await V11.ReverseGeocodeAsync(lat, lng, accuracy, granularity, maxResults).ConfigureAwait(false);
        return Place.FromResponse(this, response.Json);
    }

    /// <summary>
    /// ツイートに添付できる場所を検索します。
    /// </summary>
    public async Task<List<Place>> SearchGeoAsync(double? lat = null, double? lng = null, string? query = null, string? ip = null,
        string? granularity = null, int? maxResults = null)
    {
        var response = await V11.SearchGeoAsync(lat, lng, query, ip, granularity, maxResults).ConfigureAwait(false);
        return Place.FromResponse(this, response.Json);
    }

    /// <summary>ID を指定して場所を取得します。</summary>
    public async Task<Place> GetPlaceAsync(string id)
    {
        var response = await V11.GetPlaceAsync(id).ConfigureAwait(false);
        return new Place(this, response.Object ?? new JsonObject());
    }

    /// <summary>
    /// トレンドを取得します。
    /// </summary>
    /// <param name="category">'trending'、'for-you'、'news'、'sports'、'entertainment' のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="retry">トレンドが取れなかったとき、数回だけ再試行するか。</param>
    /// <param name="additionalRequestParams">GraphQL の variables に追加するパラメーター。</param>
    public async Task<List<Trend>> GetTrendsAsync(string category, int count = 20, bool retry = true, JsonObject? additionalRequestParams = null)
    {
        category = category.ToLowerInvariant();
        if (!Constants.TimelineIds.TryGetValue(category, out var timelineId)) return new List<Trend>();

        // A Twitter hiccup can drop the trend entries; give it a couple of
        // tries, then accept that the category has nothing to show. Retrying
        // used to recurse without bound and burn hundreds of requests.
        var attempts = retry ? 3 : 1;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var response = await Gql.GenericTimelineByIdAsync(timelineId, count, additionalRequestParams).ConfigureAwait(false);
            // The News / Sports / Entertainment tabs wrap trends in a `stories-*`
            // module, so filtering on the `trend-` prefix alone found nothing.
            var itemContents = new List<JsonNode?>();
            foreach (var entry in (response.Json.FirstDict("entries") as JsonArray).Objects())
            {
                var entryId = entry.EntryId();
                var content = entry.Sub("content");
                if (entryId.StartsWith("trend", StringComparison.Ordinal))
                    itemContents.Add(content.Get("itemContent"));
                else if (entryId.StartsWith("stories", StringComparison.Ordinal))
                    foreach (var item in content.ArrOrEmpty("items"))
                        itemContents.Add(item.Sub("item").Get("itemContent"));
            }
            var entries = itemContents.OfType<JsonObject>().Where(i => i.Str("itemType") == "TimelineTrend").ToList();
            if (entries.Count == 0) continue;

            // Trends have no cursor, so honouring `count` here is a plain trim.
            var trends = entries.Select(i => new Trend(this, i)).ToList();
            return count > 0 && trends.Count > count ? trends.GetRange(0, count) : trends;
        }
        return new List<Trend>();
    }

    /// <summary>Explore ページに表示されるトレンドを取得します。</summary>
    public async Task<List<Trend>> GetExplorePageAsync()
    {
        var response = await Gql.ExplorePageAsync().ConfigureAwait(false);
        var results = new List<Trend>();
        foreach (var entry in (response.Json.FirstDict("entries") as JsonArray).Objects())
        {
            var itemContent = entry.Sub("content").Obj("itemContent");
            if (itemContent is null || itemContent.Str("itemType") != "TimelineTrend") continue;
            results.Add(new Trend(this, itemContent));
        }
        return results;
    }

    /// <summary>トレンドを取得できる地域の一覧を取得します。</summary>
    public async Task<List<Location>> GetAvailableLocationsAsync()
    {
        var response = await V11.AvailableTrendsAsync().ConfigureAwait(false);
        return response.Array.Objects().Select(d => new Location(this, d)).ToList();
    }

    /// <summary>
    /// 指定した WOEID の上位 50 トレンドを取得します。WOEID は <see cref="GetAvailableLocationsAsync"/> で取得できます。
    /// </summary>
    public async Task<PlaceTrends> GetPlaceTrendsAsync(long woeid)
    {
        var response = await V11.PlaceTrendsAsync(woeid).ConfigureAwait(false);
        if (response.Array is not { Count: > 0 } array || array[0] is not JsonObject trendData)
            throw new NotFoundException("No trends available for that location.");
        var trends = trendData.ArrOrEmpty("trends").Objects().Select(d => new PlaceTrend(this, d)).ToList();
        return new PlaceTrends(trends, trendData.Str("as_of"), trendData.Str("created_at"), trendData.Arr("locations"));
    }
}
