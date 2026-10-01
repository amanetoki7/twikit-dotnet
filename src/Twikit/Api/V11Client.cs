using System.Globalization;
using System.Text.Json.Nodes;

namespace Twikit.Api;

/// <summary>v1.1 / その他の REST エンドポイントの URL。</summary>
public static class V11Endpoint
{
    private static readonly string D = Constants.Domain;

    public static readonly string GuestActivate = $"https://api.{D}/1.1/guest/activate.json";
    public static readonly string OnboardingSsoInit = $"https://api.{D}/1.1/onboarding/sso_init.json";
    public static readonly string AccountLogout = $"https://api.{D}/1.1/account/logout.json";
    public static readonly string OnboardingTask = $"https://api.{D}/1.1/onboarding/task.json";
    public static readonly string Settings = $"https://api.{D}/1.1/account/settings.json";
    public static readonly string UploadMedia = $"https://upload.{D}/i/media/upload.json";
    public static readonly string UploadMedia2 = $"https://upload.{D}/i/media/upload2.json";
    public static readonly string CreateMediaMetadata = $"https://api.{D}/1.1/media/metadata/create.json";
    public static readonly string CreateCard = $"https://caps.{D}/v2/cards/create.json";
    public static readonly string Vote = $"https://caps.{D}/v2/capi/passthrough/1";
    public static readonly string ReverseGeocode = $"https://api.{D}/1.1/geo/reverse_geocode.json";
    public static readonly string SearchGeo = $"https://api.{D}/1.1/geo/search.json";
    public static string GetPlace(string id) => $"https://api.{D}/1.1/geo/id/{id}.json";
    public static readonly string CreateFriendships = $"https://{D}/i/api/1.1/friendships/create.json";
    public static readonly string DestroyFriendships = $"https://{D}/i/api/1.1/friendships/destroy.json";
    public static readonly string CreateBlocks = $"https://{D}/i/api/1.1/blocks/create.json";
    public static readonly string DestroyBlocks = $"https://{D}/i/api/1.1/blocks/destroy.json";
    public static readonly string CreateMutes = $"https://{D}/i/api/1.1/mutes/users/create.json";
    public static readonly string DestroyMutes = $"https://{D}/i/api/1.1/mutes/users/destroy.json";
    public static readonly string Guide = $"https://{D}/i/api/2/guide.json";
    public static readonly string AvailableTrends = $"https://api.{D}/1.1/trends/available.json";
    public static readonly string PlaceTrends = $"https://api.{D}/1.1/trends/place.json";
    public static readonly string FollowersList = $"https://api.{D}/1.1/followers/list.json";
    public static readonly string FriendsList = $"https://api.{D}/1.1/friends/list.json";
    public static readonly string FollowersIds = $"https://api.{D}/1.1/followers/ids.json";
    public static readonly string UpdateProfile = $"https://api.{D}/1.1/account/update_profile.json";
    public static readonly string FriendsIds = $"https://api.{D}/1.1/friends/ids.json";
    public static readonly string DmNew = $"https://{D}/i/api/1.1/dm/new2.json";
    public static readonly string DmInbox = $"https://{D}/i/api/1.1/dm/inbox_initial_state.json";
    public static string DmInboxTimeline(string name) => $"https://{D}/i/api/1.1/dm/inbox_timeline/{name}.json";
    public static string DmConversation(string id) => $"https://{D}/i/api/1.1/dm/conversation/{id}.json";
    public static string ConversationUpdateName(string id) => $"https://{D}/i/api/1.1/dm/conversation/{id}/update_name.json";
    public static string DeleteConversation(string id) => $"https://{D}/i/api/1.1/dm/conversation/{id}/delete.json";
    public static readonly string NotificationsAll = $"https://{D}/i/api/2/notifications/all.json";
    public static readonly string NotificationsVerified = $"https://{D}/i/api/2/notifications/verified.json";
    public static readonly string NotificationsMentions = $"https://{D}/i/api/2/notifications/mentions.json";
    public static readonly string LivePipelineEvents = $"https://api.{D}/live_pipeline/events";
    public static readonly string LivePipelineUpdateSubscriptions = $"https://api.{D}/1.1/live_pipeline/update_subscriptions";
    public static readonly string UserState = $"https://api.{D}/help-center/forms/api/prod/user_state.json";
    /// <summary>スペースのストリーム状態（HLS/WebRTC ソースとチャットトークン）。media_key は AudioSpaceById の <c>{twitter_user_id}_{broadcast_id}</c>。</summary>
    public static string LiveVideoStreamStatus(string mediaKey) => $"https://{D}/i/api/1.1/live_video_stream/status/{mediaKey}";
    /// <summary>ライブチャットの行区切り HTTP ストリーム。</summary>
    public static readonly string LiveChat = $"https://{D}/live-chat";
}

/// <summary>
/// v1.1 エンドポイント層。生の <see cref="ApiResponse"/> を返します。
/// </summary>
public sealed class V11Client
{
    private readonly IRequestClient _base;

    public V11Client(IRequestClient baseClient)
    {
        _base = baseClient;
    }

    private static Dictionary<string, string> FormHeaders(Dictionary<string, string> headers)
    {
        headers["content-type"] = "application/x-www-form-urlencoded";
        return headers;
    }

    public Task<ApiResponse> GuestActivateAsync()
    {
        var headers = _base.BaseHeaders;
        headers.Remove("X-Twitter-Active-User");
        headers.Remove("X-Twitter-Auth-Type");
        return _base.PostAsync(V11Endpoint.GuestActivate, new RequestOptions { Headers = headers, Form = new Dictionary<string, string>() });
    }

    public Task<ApiResponse> AccountLogoutAsync()
        => _base.PostAsync(V11Endpoint.AccountLogout, new RequestOptions { Headers = _base.BaseHeaders });

    public Task<ApiResponse> OnboardingTaskAsync(string guestToken, string? token, JsonArray? subtaskInputs,
        JsonObject? data = null, Dictionary<string, string>? parameters = null)
    {
        data ??= new JsonObject();
        if (token is not null) data["flow_token"] = token;
        if (subtaskInputs is not null) data["subtask_inputs"] = subtaskInputs;

        var headers = new Dictionary<string, string>
        {
            ["x-guest-token"] = guestToken,
            ["Authorization"] = "Bearer " + Constants.Token,
        };
        var csrf = _base.GetCsrfToken();
        if (!string.IsNullOrEmpty(csrf))
        {
            headers["x-csrf-token"] = csrf;
            headers["x-twitter-auth-type"] = "OAuth2Session";
        }
        return _base.PostAsync(V11Endpoint.OnboardingTask, new RequestOptions { Json = data, Headers = headers, Params = parameters });
    }

    public Task<ApiResponse> SsoInitAsync(string provider, string guestToken)
    {
        var headers = _base.BaseHeaders;
        headers["x-guest-token"] = guestToken;
        headers.Remove("X-Twitter-Active-User");
        headers.Remove("X-Twitter-Auth-Type");
        return _base.PostAsync(V11Endpoint.OnboardingSsoInit, new RequestOptions
        {
            Json = new JsonObject { ["provider"] = provider },
            Headers = headers,
        });
    }

    public Task<ApiResponse> SettingsAsync()
        => _base.GetAsync(V11Endpoint.Settings, new RequestOptions { Headers = _base.BaseHeaders });

    public Task<ApiResponse> UploadMediaAsync(HttpMethod method, bool isLongVideo, RequestOptions options)
        => _base.RequestAsync(method, isLongVideo ? V11Endpoint.UploadMedia2 : V11Endpoint.UploadMedia, options);

    public Task<ApiResponse> UploadMediaInitAsync(string mediaType, long totalBytes, string? mediaCategory, bool isLongVideo)
    {
        var parameters = new Dictionary<string, string>
        {
            ["command"] = "INIT",
            ["total_bytes"] = totalBytes.ToString(CultureInfo.InvariantCulture),
            ["media_type"] = mediaType,
        };
        if (mediaCategory is not null) parameters["media_category"] = mediaCategory;
        return UploadMediaAsync(HttpMethod.Post, isLongVideo, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> UploadMediaAppendAsync(bool isLongVideo, string mediaId, int segmentIndex, byte[] chunk)
    {
        var parameters = new Dictionary<string, string>
        {
            ["command"] = "APPEND",
            ["media_id"] = mediaId,
            ["segment_index"] = segmentIndex.ToString(CultureInfo.InvariantCulture),
        };
        var headers = _base.BaseHeaders;
        headers.Remove("content-type");
        var files = new List<MultipartFile> { new("media", "blob", chunk, "application/octet-stream") };
        return UploadMediaAsync(HttpMethod.Post, isLongVideo, new RequestOptions { Params = parameters, Headers = headers, Files = files });
    }

    public Task<ApiResponse> UploadMediaFinalizeAsync(bool isLongVideo, string mediaId)
    {
        var parameters = new Dictionary<string, string> { ["command"] = "FINALIZE", ["media_id"] = mediaId };
        return UploadMediaAsync(HttpMethod.Post, isLongVideo, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> UploadMediaStatusAsync(bool isLongVideo, string mediaId)
    {
        var parameters = new Dictionary<string, string> { ["command"] = "STATUS", ["media_id"] = mediaId };
        return UploadMediaAsync(HttpMethod.Get, isLongVideo, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> CreateMediaMetadataAsync(string mediaId, string? altText, IEnumerable<string>? sensitiveWarning)
    {
        var data = new JsonObject { ["media_id"] = mediaId };
        if (altText is not null) data["alt_text"] = new JsonObject { ["text"] = altText };
        if (sensitiveWarning is not null) data["sensitive_media_warning"] = sensitiveWarning.ToJsonArray();
        return _base.PostAsync(V11Endpoint.CreateMediaMetadata, new RequestOptions { Json = data, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> CreateCardAsync(IList<string> choices, int durationMinutes)
    {
        var cardData = new JsonObject
        {
            ["twitter:card"] = $"poll{choices.Count}choice_text_only",
            ["twitter:api:api:endpoint"] = "1",
            ["twitter:long:duration_minutes"] = durationMinutes,
        };
        for (var i = 0; i < choices.Count; i++)
            cardData[$"twitter:string:choice{i + 1}_label"] = choices[i];

        var data = new Dictionary<string, string> { ["card_data"] = cardData.ToJsonString() };
        return _base.PostAsync(V11Endpoint.CreateCard, new RequestOptions { Form = data, Headers = FormHeaders(_base.BaseHeaders) });
    }

    public Task<ApiResponse> VoteAsync(string selectedChoice, string cardUri, string tweetId, string cardName)
    {
        var data = new Dictionary<string, string>
        {
            ["twitter:string:card_uri"] = cardUri,
            ["twitter:long:original_tweet_id"] = tweetId,
            ["twitter:string:response_card_name"] = cardName,
            ["twitter:string:cards_platform"] = "Web-12",
            ["twitter:string:selected_choice"] = selectedChoice,
        };
        return _base.PostAsync(V11Endpoint.Vote, new RequestOptions { Form = data, Headers = FormHeaders(_base.BaseHeaders) });
    }

    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public Task<ApiResponse> ReverseGeocodeAsync(double lat, double lng, string? accuracy, string? granularity, int? maxResults)
    {
        var parameters = new Dictionary<string, string> { ["lat"] = F(lat), ["long"] = F(lng) };
        if (accuracy is not null) parameters["accuracy"] = accuracy;
        if (granularity is not null) parameters["granularity"] = granularity;
        if (maxResults is not null) parameters["max_results"] = maxResults.Value.ToString(CultureInfo.InvariantCulture);
        return _base.GetAsync(V11Endpoint.ReverseGeocode, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> SearchGeoAsync(double? lat, double? lng, string? query, string? ip, string? granularity, int? maxResults)
    {
        var parameters = new Dictionary<string, string>();
        if (lat is not null) parameters["lat"] = F(lat.Value);
        if (lng is not null) parameters["long"] = F(lng.Value);
        if (query is not null) parameters["query"] = query;
        if (ip is not null) parameters["ip"] = ip;
        if (granularity is not null) parameters["granularity"] = granularity;
        if (maxResults is not null) parameters["max_results"] = maxResults.Value.ToString(CultureInfo.InvariantCulture);
        return _base.GetAsync(V11Endpoint.SearchGeo, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> GetPlaceAsync(string id)
        => _base.GetAsync(V11Endpoint.GetPlace(id), new RequestOptions { Headers = _base.BaseHeaders });

    private static Dictionary<string, string> FriendshipForm(string userId) => new()
    {
        ["include_profile_interstitial_type"] = "1",
        ["include_blocking"] = "1",
        ["include_blocked_by"] = "1",
        ["include_followed_by"] = "1",
        ["include_want_retweets"] = "1",
        ["include_mute_edge"] = "1",
        ["include_can_dm"] = "1",
        ["include_can_media_tag"] = "1",
        ["include_ext_is_blue_verified"] = "1",
        ["include_ext_verified_type"] = "1",
        ["include_ext_profile_image_shape"] = "1",
        ["skip_status"] = "1",
        ["user_id"] = userId,
    };

    public Task<ApiResponse> CreateFriendshipsAsync(string userId)
        => _base.PostAsync(V11Endpoint.CreateFriendships, new RequestOptions { Form = FriendshipForm(userId), Headers = FormHeaders(_base.BaseHeaders) });

    public Task<ApiResponse> DestroyFriendshipsAsync(string userId)
        => _base.PostAsync(V11Endpoint.DestroyFriendships, new RequestOptions { Form = FriendshipForm(userId), Headers = FormHeaders(_base.BaseHeaders) });

    private Task<ApiResponse> UserIdFormPostAsync(string url, string userId)
        => _base.PostAsync(url, new RequestOptions
        {
            Form = new Dictionary<string, string> { ["user_id"] = userId },
            Headers = FormHeaders(_base.BaseHeaders),
        });

    public Task<ApiResponse> CreateBlocksAsync(string userId) => UserIdFormPostAsync(V11Endpoint.CreateBlocks, userId);
    public Task<ApiResponse> DestroyBlocksAsync(string userId) => UserIdFormPostAsync(V11Endpoint.DestroyBlocks, userId);
    public Task<ApiResponse> CreateMutesAsync(string userId) => UserIdFormPostAsync(V11Endpoint.CreateMutes, userId);
    public Task<ApiResponse> DestroyMutesAsync(string userId) => UserIdFormPostAsync(V11Endpoint.DestroyMutes, userId);

    public Task<ApiResponse> GuideAsync(string category, int count, Dictionary<string, string>? additionalRequestParams)
    {
        var parameters = new Dictionary<string, string>
        {
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
            ["include_page_configuration"] = "true",
            ["initial_tab_id"] = category,
        };
        if (additionalRequestParams is not null)
            foreach (var kv in additionalRequestParams) parameters[kv.Key] = kv.Value;
        return _base.GetAsync(V11Endpoint.Guide, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> AvailableTrendsAsync()
        => _base.GetAsync(V11Endpoint.AvailableTrends, new RequestOptions { Headers = _base.BaseHeaders });

    public Task<ApiResponse> PlaceTrendsAsync(long woeid)
        => _base.GetAsync(V11Endpoint.PlaceTrends, new RequestOptions
        {
            Params = new Dictionary<string, string> { ["id"] = woeid.ToString(CultureInfo.InvariantCulture) },
            Headers = _base.BaseHeaders,
        });

    private Task<ApiResponse> FriendshipsAsync(string? userId, string? screenName, int count, string endpoint, string? cursor)
    {
        var parameters = new Dictionary<string, string> { ["count"] = count.ToString(CultureInfo.InvariantCulture) };
        if (userId is not null) parameters["user_id"] = userId;
        else if (screenName is not null) parameters["screen_name"] = screenName;
        if (cursor is not null) parameters["cursor"] = cursor;
        return _base.GetAsync(endpoint, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> FollowersListAsync(string? userId, string? screenName, int count, string? cursor)
        => FriendshipsAsync(userId, screenName, count, V11Endpoint.FollowersList, cursor);

    public Task<ApiResponse> FriendsListAsync(string? userId, string? screenName, int count, string? cursor)
        => FriendshipsAsync(userId, screenName, count, V11Endpoint.FriendsList, cursor);

    public Task<ApiResponse> FollowersIdsAsync(string? userId, string? screenName, int count, string? cursor)
        => FriendshipsAsync(userId, screenName, count, V11Endpoint.FollowersIds, cursor);

    public Task<ApiResponse> FriendsIdsAsync(string? userId, string? screenName, int count, string? cursor)
        => FriendshipsAsync(userId, screenName, count, V11Endpoint.FriendsIds, cursor);

    public Task<ApiResponse> UpdateProfileAsync(Dictionary<string, string> fields)
    {
        // x.com always sends displayNameMaxLength alongside the edited fields;
        // without it the endpoint answers 200 and silently keeps the old profile.
        var data = new Dictionary<string, string> { ["displayNameMaxLength"] = "50" };
        foreach (var kv in fields) data[kv.Key] = kv.Value;
        // BaseHeaders pins content-type to application/json, which would
        // mislabel this form body - X then parses no fields at all.
        return _base.PostAsync(V11Endpoint.UpdateProfile, new RequestOptions { Form = data, Headers = FormHeaders(_base.BaseHeaders) });
    }

    public Task<ApiResponse> DmNewGroupAsync(IEnumerable<string> recipientIds, string text, string? mediaId)
    {
        var data = new JsonObject
        {
            ["cards_platform"] = "Web-12",
            ["dm_users"] = false,
            ["include_cards"] = 1,
            ["include_quote_count"] = true,
            ["recipient_ids"] = string.Join(",", recipientIds),
            ["text"] = text,
        };
        if (mediaId is not null) data["media_id"] = mediaId;
        return _base.PostAsync(V11Endpoint.DmNew, new RequestOptions { Json = data, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> DeleteConversationAsync(string conversationId)
        => _base.PostAsync(V11Endpoint.DeleteConversation(conversationId), new RequestOptions
        {
            Headers = FormHeaders(_base.BaseHeaders),
            Form = new Dictionary<string, string>(),
        });

    public Task<ApiResponse> DmNewAsync(string conversationId, string text, string? mediaId, string? replyTo)
    {
        var data = new JsonObject
        {
            ["cards_platform"] = "Web-12",
            ["conversation_id"] = conversationId,
            ["dm_users"] = false,
            ["include_cards"] = 1,
            ["include_quote_count"] = true,
            ["recipient_ids"] = false,
            ["text"] = text,
        };
        if (mediaId is not null) data["media_id"] = mediaId;
        if (replyTo is not null) data["reply_to_dm_id"] = replyTo;
        return _base.PostAsync(V11Endpoint.DmNew, new RequestOptions { Json = data, Headers = _base.BaseHeaders });
    }

    private static Dictionary<string, string> InboxParams() => new()
    {
        ["nsfw_filtering_enabled"] = "false",
        ["filter_low_quality"] = "false",
        ["include_quality"] = "all",
        ["include_groups"] = "true",
        ["include_inbox_timelines"] = "true",
        ["include_ext_media_color"] = "true",
        ["supports_reactions"] = "true",
    };

    public Task<ApiResponse> DmInboxAsync(string? cursor)
    {
        var parameters = InboxParams();
        if (cursor is not null) parameters["cursor"] = cursor;
        return _base.GetAsync(V11Endpoint.DmInbox, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    /// <remarks>
    /// inbox_initial_state only ever serves the first page - handing it a cursor returns the identical
    /// conversations. This is the endpoint that actually walks the inbox, and it refuses without a max_id (400, code 214).
    /// </remarks>
    public Task<ApiResponse> DmInboxTimelineAsync(string name, string maxId, int? count = null)
    {
        var parameters = InboxParams();
        parameters["max_id"] = maxId;
        if (count is not null) parameters["count"] = count.Value.ToString(CultureInfo.InvariantCulture);
        return _base.GetAsync(V11Endpoint.DmInboxTimeline(name), new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> DmConversationAsync(string conversationId, string? maxId)
    {
        var parameters = new Dictionary<string, string>
        {
            ["context"] = "FETCH_DM_CONVERSATION_HISTORY",
            ["include_conversation_info"] = "true",
        };
        if (maxId is not null) parameters["max_id"] = maxId;
        return _base.GetAsync(V11Endpoint.DmConversation(conversationId), new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> ConversationUpdateNameAsync(string groupId, string name)
        => _base.PostAsync(V11Endpoint.ConversationUpdateName(groupId), new RequestOptions
        {
            Form = new Dictionary<string, string> { ["name"] = name },
            Headers = FormHeaders(_base.BaseHeaders),
        });

    private Task<ApiResponse> NotificationsAsync(string endpoint, int count, string? cursor)
    {
        var parameters = new Dictionary<string, string> { ["count"] = count.ToString(CultureInfo.InvariantCulture) };
        if (cursor is not null) parameters["cursor"] = cursor;
        return _base.GetAsync(endpoint, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> NotificationsAllAsync(int count, string? cursor) => NotificationsAsync(V11Endpoint.NotificationsAll, count, cursor);
    public Task<ApiResponse> NotificationsVerifiedAsync(int count, string? cursor) => NotificationsAsync(V11Endpoint.NotificationsVerified, count, cursor);
    public Task<ApiResponse> NotificationsMentionsAsync(int count, string? cursor) => NotificationsAsync(V11Endpoint.NotificationsMentions, count, cursor);

    public Task<ApiResponse> LivePipelineUpdateSubscriptionsAsync(string session, string subscribe, string unsubscribe)
    {
        var data = new Dictionary<string, string> { ["sub_topics"] = subscribe, ["unsub_topics"] = unsubscribe };
        var headers = FormHeaders(_base.BaseHeaders);
        headers["LivePipeline-Session"] = session;
        return _base.PostAsync(V11Endpoint.LivePipelineUpdateSubscriptions, new RequestOptions { Form = data, Headers = headers });
    }

    public Task<ApiResponse> LiveVideoStreamStatusAsync(string mediaKey)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client"] = "web",
            ["use_syndication_guest_id"] = "false",
            ["cookie_set_host"] = "x.com",
        };
        return _base.GetAsync(V11Endpoint.LiveVideoStreamStatus(mediaKey), new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    /// <summary>スペースのライブチャット HTTP ストリームを開きます。行ごとに JSON として読んでください。</summary>
    public Task<ApiResponse> LiveChatAsync(string broadcastId, string? sessionId = null, string? cursor = null)
    {
        var parameters = new Dictionary<string, string> { ["broadcastId"] = broadcastId, ["sessionId"] = sessionId ?? "" };
        if (cursor is not null) parameters["cursor"] = cursor;
        return _base.GetAsync(V11Endpoint.LiveChat, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    /// <remarks>
    /// The 429 recovery path in Client.RequestAsync calls GetUserStateAsync(), which ends up back here;
    /// CheckUserState=false is passed down so that if this nested call also returns 429 we don't loop.
    /// </remarks>
    public Task<ApiResponse> UserStateAsync(bool checkUserState = true)
        => _base.GetAsync(V11Endpoint.UserState, new RequestOptions { Headers = _base.BaseHeaders, CheckUserState = checkUserState });
}
