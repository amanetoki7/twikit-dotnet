using System.Text.Json.Nodes;

namespace Twikit.Api;

/// <summary>GraphQL エンドポイントの URL。</summary>
public static class GqlEndpoint
{
    public static string Url(string path) => $"https://{Constants.Domain}/i/api/graphql/{path}";

    public static readonly string SearchTimeline = Url("BGd0T_j7oVwlW5U79tO_0A/SearchTimeline");
    public static readonly string SimilarPosts = Url("6GbCDHT7fkPjgdkueQ9RKA/SimilarPosts");
    public static readonly string CreateNoteTweet = Url("WCcsCWTsiPteFwUxjI6OmA/CreateNoteTweet");
    public static readonly string CreateTweet = Url("wUgPBh9hEKhMMGlg8uDuFw/CreateTweet");
    public static readonly string CreateScheduledTweet = Url("LCVzRQGxOaGnOnYH01NQXg/CreateScheduledTweet");
    public static readonly string DeleteTweet = Url("nxpZCY2K-I6QoFHAHeojFQ/DeleteTweet");
    // Deliberately NOT the current document. The one the web client uses
    // (Gb-d6r0vxPOADdG62OEBpQ) returns `legacy` empty, and nine fields live
    // only there - listed_count, normal_followers_count, has_custom_timelines,
    // want_retweets, fast_followers_count, default_profile,
    // default_profile_image, is_translator, withheld_in_countries - with no
    // equivalent among the typed objects. This one still answers with legacy
    // populated and covers every field the typed objects carry.
    public static readonly string UserByScreenName = Url("NimuplG1OB7Fd2btCLdBOw/UserByScreenName");
    public static readonly string MutedAccounts = Url("dQiMIEnwsQjKtv-7PHMixQ/MutedAccounts");
    public static readonly string BlockedAccounts = Url("5oNXfRkE7HVkDX1Fd1gn3g/BlockedAccountsAll");
    public static readonly string CombinedLists = Url("15lgkbq4YMpgnv3Xf8BlXg/CombinedLists");
    public static readonly string ProfileSpotlights = Url("mzoqrVGwk-YTSGME1dRfXQ/ProfileSpotlightsQuery");
    public static readonly string AboutAccount = Url("TzOG2twZEfhr9KmClvVVqA/AboutAccountQuery");
    // Same reasoning as UserByScreenName above.
    public static readonly string UserByRestId = Url("tD8zKvQzwY3kdx5yz6YmOw/UserByRestId");
    public static readonly string TweetDetail = Url("559hs_YZNV4IgA3Z6zIIuw/TweetDetail");
    public static readonly string TweetResultByRestId = Url("LkId5Akr61BS6BmOIcffRg/TweetResultByRestId");
    public static readonly string FetchScheduledTweets = Url("H2elmT2R9DLhWoo0DZFNkA/FetchScheduledTweets");
    public static readonly string DeleteScheduledTweet = Url("CTOVqej0JBXAZSwkp1US0g/DeleteScheduledTweet");
    public static readonly string Retweeters = Url("_wJOTLm5HMqNdcr1nGWlyA/Retweeters");
    public static readonly string Favoriters = Url("JpUz3qfNTiMbhqmJOvVJSw/Favoriters");
    public static readonly string FetchCommunityNote = Url("fKWPPj271aTM-AB9Xp48IA/BirdwatchFetchOneNote");
    public static readonly string UserTweets = Url("eoJ5zbv51Z_KVl81v9PmLQ/UserTweets");
    public static readonly string UserTweetsAndReplies = Url("wc5DRl4VaW5lSqJ8YbftZQ/UserTweetsAndReplies");
    public static readonly string UserMedia = Url("2DC9TKrcUzwGC_QskSVl5w/UserMedia");
    public static readonly string UserLikes = Url("BEthBswU1Bt209H5xptp4Q/Likes");
    public static readonly string UserHighlightsTweets = Url("Ijy4LdX8ZYTy1PzQn8xC4g/UserHighlightsTweets");
    public static readonly string HomeTimeline = Url("-X_hcgQzmHGl29-UXxz4sw/HomeTimeline");
    public static readonly string HomeLatestTimeline = Url("U0cdisy7QFIoTfu3-Okw0A/HomeLatestTimeline");
    public static readonly string FavoriteTweet = Url("lI07N6Otwv1PhnEgXILM7A/FavoriteTweet");
    public static readonly string UnfavoriteTweet = Url("ZYKSe-w7KEslx3JhSIk5LA/UnfavoriteTweet");
    public static readonly string CreateRetweet = Url("mbRO74GrOvSfRcJnlMapnQ/CreateRetweet");
    public static readonly string DeleteRetweet = Url("ZyZigVsNiFO6v1dEks1eWg/DeleteRetweet");
    public static readonly string CreateBookmark = Url("aoDbu3RHznuiSkQ9aNM67Q/CreateBookmark");
    public static readonly string BookmarkToFolder = Url("4KHZvvNbHNf07bsgnL9gWA/bookmarkTweetToFolder");
    public static readonly string DeleteBookmark = Url("Wlmlj2-xzyS1GN3a6cj-mQ/DeleteBookmark");
    public static readonly string Bookmarks = Url("aqjes8lRHRFG0HUglVTfNg/Bookmarks");
    public static readonly string BookmarkFolderTimeline = Url("g5l-N4fpbp7B-1OAbOdGzw/BookmarkFolderTimeline");
    public static readonly string BookmarksAllDelete = Url("skiACZKC1GDYli-M8RzEPQ/BookmarksAllDelete");
    public static readonly string BookmarkFoldersSlice = Url("i78YDd0Tza-dV4SYs58kRg/BookmarkFoldersSlice");
    public static readonly string EditBookmarkFolder = Url("a6kPp1cS1Dgbsjhapz1PNw/EditBookmarkFolder");
    public static readonly string DeleteBookmarkFolder = Url("2UTTsO-6zs93XqlEUZPsSg/DeleteBookmarkFolder");
    public static readonly string CreateBookmarkFolder = Url("6Xxqpq8TM_CREYiuof_h5w/createBookmarkFolder");
    public static readonly string Followers = Url("vJijlO_CM7dyGFNjDd7iqQ/Followers");
    public static readonly string ExplorePage = Url("gjznU4bIOCEjvXD5Un47bw/ExplorePage");
    public static readonly string GenericTimelineById = Url("BrGScxnisMdTXyeLScaEhQ/GenericTimelineById");
    public static readonly string BlueVerifiedFollowers = Url("cg6WLW39UujWMeX77xBnOA/BlueVerifiedFollowers");
    public static readonly string FollowersYouKnow = Url("wIEyYIhzwtDEgBvqDRCDVQ/FollowersYouKnow");
    public static readonly string Following = Url("b8XpwALENnJdFSHchkK6rw/Following");
    public static readonly string UserCreatorSubscriptions = Url("n5c96Ql2BupZFGeEOIp9cA/UserCreatorSubscriptions");
    public static readonly string UserDmReactionMutationAddMutation = Url("VyDyV9pC2oZEj6g52hgnhA/useDMReactionMutationAddMutation");
    public static readonly string UserDmReactionMutationRemoveMutation = Url("bV_Nim3RYHsaJwMkTXJ6ew/useDMReactionMutationRemoveMutation");
    public static readonly string DmMessageDeleteMutation = Url("BJ6DtxA2llfjnRoRjaiIiw/DMMessageDeleteMutation");
    public static readonly string AddParticipantsMutation = Url("oBwyQ0_xVbAQ8FAyG0pCRA/AddParticipantsMutation");
    public static readonly string CreateList = Url("sTuzqjTr8MNpVBb9YF04Mg/CreateList");
    public static readonly string DeleteList = Url("UnN9Th1BDbeLjpgjGSpL3Q/DeleteList");
    public static readonly string EditListBanner = Url("E_ugomI2WMK7mJCTjRQjFQ/EditListBanner");
    public static readonly string DeleteListBanner = Url("3ZIyjR4JXXJ69HdoxlHcVw/DeleteListBanner");
    public static readonly string UpdateList = Url("dGqf-DouTmK767LtRJ2qeA/UpdateList");
    public static readonly string ListAddMember = Url("V2yIKI9d6o_9D9rJ9-a-2w/ListAddMember");
    public static readonly string ListRemoveMember = Url("NYsw9xBA6rSMA3N5sccSJA/ListRemoveMember");
    public static readonly string ListManagementPaceTimeline = Url("4zAcuxtfEt0_ds2pU17Liw/ListsManagementPageTimeline");
    public static readonly string ListByRestId = Url("niz0TtOxL2zIcbq6_NQiNw/ListByRestId");
    public static readonly string ListLatestTweetsTimeline = Url("jW040BLUjh8X6Tw2ODQufA/ListLatestTweetsTimeline");
    public static readonly string ListMembers = Url("wGce-45xnc5bs3HVvevC2w/ListMembers");
    public static readonly string ListSubscribers = Url("D4pxLunZzmExmyOfDK4xaA/ListSubscribers");
    public static readonly string SearchCommunity = Url("daVUkhfHn7-Z8llpYVKJSw/CommunitiesSearchQuery");
    public static readonly string CommunityQuery = Url("-ElI1vg3dYbttVMhBhGdLw/CommunityQuery");
    public static readonly string CommunityMediaTimeline = Url("Ht5K2ckaZYAOuRFmFfbHig/CommunityMediaTimeline");
    public static readonly string CommunityTweetsTimeline = Url("dD1uF9vQx0OX-e1rKA4YLw/CommunityTweetsTimeline");
    public static readonly string CommunitiesMainPageTimeline = Url("4-4iuIdaLPpmxKnA3mr2LA/CommunitiesMainPageTimeline");
    public static readonly string JoinCommunity = Url("xZQLbDwbI585YTG0QIpokw/JoinCommunity");
    public static readonly string LeaveCommunity = Url("OoS6Kd4-noNLXPZYHtygeA/LeaveCommunity");
    public static readonly string RequestToJoinCommunity = Url("XwWChphD_6g7JnsFus2f2Q/RequestToJoinCommunity");
    public static readonly string MembersSliceTimelineQuery = Url("KDAssJ5lafCy-asH4wm1dw/membersSliceTimeline_Query");
    public static readonly string ModeratorsSliceTimelineQuery = Url("9KI_r8e-tgp3--N5SZYVjg/moderatorsSliceTimeline_Query");
    public static readonly string CommunityTweetSearchModuleQuery = Url("5341rmzzvdjqfmPKfoHUBw/CommunityTweetSearchModuleQuery");
    public static readonly string TweetResultsByRestIds = Url("Tbh_EBpWw_VUFu5tMYAuNQ/TweetResultsByRestIds");

    // --- Spaces ---
    // Query IDs below were extracted from the current web bundle
    // (main.*.js + shared AudioSpace* chunks, verified 2026-08).
    public static readonly string AudioSpaceById = Url("CSzFYPoVPLfqWFAJDsYZxQ/AudioSpaceById");
    public static readonly string AudioSpaceSearch = Url("NTq79TuSz6fHj8lQaferJw/AudioSpaceSearch");
    public static readonly string BrowseSpaceTopics = Url("TYpVV9QioZfViHqEqRZxJA/BrowseSpaceTopics");
    public static readonly string AudioSpaceAddSharing = Url("G0NcfBzL8WWWg7KYRzVjUg/AudioSpaceAddSharing");
    public static readonly string AudioSpaceDeleteSharing = Url("YMbfLMTUUEzEEMibvvR26Q/AudioSpaceDeleteSharing");
    public static readonly string SubscribeToScheduledSpace = Url("Sxn4YOlaAwEKjnjWV0h7Mw/SubscribeToScheduledSpace");
    public static readonly string UnsubscribeFromScheduledSpace = Url("Zevhh76Msw574ZSs2NQHGQ/UnsubscribeFromScheduledSpace");
    // Returns the JWT that authenticates against the Periscope (proxsee) API.
    public static readonly string AuthenticatePeriscope = Url("r7VUmxbfqNkx7uwjgONSNw/AuthenticatePeriscope");
}

/// <summary>
/// GraphQL エンドポイント層。各メソッドは 1 つの GraphQL ドキュメントに対応し、
/// 生の <see cref="ApiResponse"/> を返します（<see cref="Client"/> がモデルに変換します）。
/// </summary>
public sealed class GqlClient
{
    private readonly IRequestClient _base;

    public GqlClient(IRequestClient baseClient)
    {
        _base = baseClient;
    }

    private static JsonObject V() => new();

    /// <summary>GET で GraphQL クエリを送ります。</summary>
    public Task<ApiResponse> GqlGetAsync(string url, JsonObject variables, JsonObject? features = null,
        Dictionary<string, string>? headers = null, JsonObject? extraParams = null)
    {
        var parameters = new JsonObject { ["variables"] = variables };
        if (features is not null) parameters["features"] = Constants.Copy(features);
        if (extraParams is not null)
            foreach (var kv in extraParams) parameters[kv.Key] = kv.Value.Clone();
        return _base.GetAsync(url, new RequestOptions
        {
            Params = Utils.FlattenParams(parameters),
            Headers = headers ?? _base.BaseHeaders,
        });
    }

    /// <summary>POST で GraphQL ミューテーションを送ります。</summary>
    public Task<ApiResponse> GqlPostAsync(string url, JsonObject variables, JsonObject? features = null,
        Dictionary<string, string>? headers = null, JsonObject? extraData = null)
    {
        var data = new JsonObject { ["variables"] = variables, ["queryId"] = Utils.GetQueryId(url) };
        if (features is not null) data["features"] = Constants.Copy(features);
        if (extraData is not null)
            foreach (var kv in extraData) data[kv.Key] = kv.Value.Clone();
        return _base.PostAsync(url, new RequestOptions
        {
            Json = data,
            Headers = headers ?? _base.BaseHeaders,
        });
    }

    public Task<ApiResponse> SearchTimelineAsync(string query, string product, int count, string? cursor)
    {
        var variables = new JsonObject
        {
            ["rawQuery"] = query,
            ["count"] = count,
            ["querySource"] = "typed_query",
            ["product"] = product,
            ["withGrokTranslatedBio"] = true,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.SearchTimeline, variables, Constants.SearchTimelineFeatures);
    }

    public Task<ApiResponse> SimilarPostsAsync(string tweetId)
        => GqlGetAsync(GqlEndpoint.SimilarPosts, new JsonObject { ["tweet_id"] = tweetId }, Constants.SimilarPostsFeatures);

    public Task<ApiResponse> CreateTweetAsync(
        bool isNoteTweet, string text, JsonArray mediaEntities, string? pollUri, string? replyTo,
        string? attachmentUrl, string? communityId, bool shareWithFollowers, JsonArray? richtextOptions,
        string? editTweetId, string? limitMode)
    {
        var variables = new JsonObject
        {
            ["tweet_text"] = text,
            ["dark_request"] = false,
            ["media"] = new JsonObject
            {
                ["media_entities"] = mediaEntities,
                ["possibly_sensitive"] = false,
            },
            ["semantic_annotation_ids"] = new JsonArray(),
        };

        if (pollUri is not null) variables["card_uri"] = pollUri;

        if (replyTo is not null)
        {
            variables["reply"] = new JsonObject
            {
                ["in_reply_to_tweet_id"] = replyTo,
                ["exclude_reply_user_ids"] = new JsonArray(),
            };
        }

        if (limitMode is not null) variables["conversation_control"] = new JsonObject { ["mode"] = limitMode };

        if (attachmentUrl is not null) variables["attachment_url"] = attachmentUrl;

        if (communityId is not null)
        {
            variables["semantic_annotation_ids"] = new JsonArray
            {
                new JsonObject { ["entity_id"] = communityId, ["group_id"] = "8", ["domain_id"] = "31" },
            };
            variables["broadcast"] = shareWithFollowers;
        }

        if (richtextOptions is not null)
        {
            isNoteTweet = true;
            variables["richtext_options"] = new JsonObject { ["richtext_tags"] = richtextOptions };
        }
        if (editTweetId is not null)
            variables["edit_options"] = new JsonObject { ["previous_tweet_id"] = editTweetId };

        return isNoteTweet
            ? GqlPostAsync(GqlEndpoint.CreateNoteTweet, variables, Constants.NoteTweetFeatures)
            : GqlPostAsync(GqlEndpoint.CreateTweet, variables, Constants.Features);
    }

    public Task<ApiResponse> CreateScheduledTweetAsync(long scheduledAt, string text, IEnumerable<string>? mediaIds)
    {
        var variables = new JsonObject
        {
            ["post_tweet_request"] = new JsonObject
            {
                ["auto_populate_reply_metadata"] = false,
                ["status"] = text,
                ["exclude_reply_user_ids"] = new JsonArray(),
                ["media_ids"] = mediaIds is null ? null : mediaIds.ToJsonArray(),
            },
            ["execute_at"] = scheduledAt,
        };
        return GqlPostAsync(GqlEndpoint.CreateScheduledTweet, variables);
    }

    public Task<ApiResponse> DeleteTweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.DeleteTweet, new JsonObject { ["tweet_id"] = tweetId, ["dark_request"] = false });

    public Task<ApiResponse> UserByScreenNameAsync(string screenName)
    {
        var variables = new JsonObject { ["screen_name"] = screenName, ["withSafetyModeUserFields"] = false };
        var extra = new JsonObject { ["fieldToggles"] = new JsonObject { ["withAuxiliaryUserLabels"] = false } };
        return GqlGetAsync(GqlEndpoint.UserByScreenName, variables, Constants.UserFeatures, extraParams: extra);
    }

    public Task<ApiResponse> MutedAccountsAsync(int count, string? cursor)
    {
        var variables = new JsonObject { ["count"] = count, ["includePromotedContent"] = false };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.MutedAccounts, variables, Constants.Features);
    }

    public Task<ApiResponse> BlockedAccountsAsync(int count, string? cursor)
    {
        var variables = new JsonObject { ["count"] = count, ["includePromotedContent"] = false };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.BlockedAccounts, variables, Constants.Features);
    }

    public Task<ApiResponse> CombinedListsAsync(string userId, int count, string? cursor)
    {
        var variables = new JsonObject { ["userId"] = userId, ["count"] = count };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.CombinedLists, variables, Constants.Features);
    }

    public Task<ApiResponse> ProfileSpotlightsAsync(string screenName)
        => GqlGetAsync(GqlEndpoint.ProfileSpotlights, new JsonObject { ["screen_name"] = screenName });

    public Task<ApiResponse> AboutAccountAsync(string screenName)
        => GqlGetAsync(GqlEndpoint.AboutAccount, new JsonObject { ["screenName"] = screenName });

    public Task<ApiResponse> UserByRestIdAsync(string userId)
    {
        var variables = new JsonObject { ["userId"] = userId, ["withSafetyModeUserFields"] = true };
        return GqlGetAsync(GqlEndpoint.UserByRestId, variables, Constants.UserFeatures);
    }

    public Task<ApiResponse> TweetDetailAsync(string tweetId, string? cursor)
    {
        var variables = new JsonObject
        {
            ["focalTweetId"] = tweetId,
            ["with_rux_injections"] = false,
            ["includePromotedContent"] = true,
            ["withCommunity"] = true,
            ["withQuickPromoteEligibilityTweetFields"] = true,
            ["withBirdwatchNotes"] = true,
            ["withVoice"] = true,
            ["withV2Timeline"] = true,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        var extra = new JsonObject { ["fieldToggles"] = new JsonObject { ["withAuxiliaryUserLabels"] = false } };
        return GqlGetAsync(GqlEndpoint.TweetDetail, variables, Constants.Features, extraParams: extra);
    }

    public Task<ApiResponse> FetchScheduledTweetsAsync()
        => GqlGetAsync(GqlEndpoint.FetchScheduledTweets, new JsonObject { ["ascending"] = true });

    public Task<ApiResponse> DeleteScheduledTweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.DeleteScheduledTweet, new JsonObject { ["scheduled_tweet_id"] = tweetId });

    public Task<ApiResponse> TweetEngagementsAsync(string tweetId, int count, string? cursor, string endpoint)
    {
        var variables = new JsonObject { ["tweetId"] = tweetId, ["count"] = count, ["includePromotedContent"] = true };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(endpoint, variables, Constants.Features);
    }

    public Task<ApiResponse> RetweetersAsync(string tweetId, int count, string? cursor)
        => TweetEngagementsAsync(tweetId, count, cursor, GqlEndpoint.Retweeters);

    public Task<ApiResponse> FavoritersAsync(string tweetId, int count, string? cursor)
        => TweetEngagementsAsync(tweetId, count, cursor, GqlEndpoint.Favoriters);

    public Task<ApiResponse> ExplorePageAsync()
        => GqlGetAsync(GqlEndpoint.ExplorePage, new JsonObject { ["cursor"] = "" }, Constants.ExplorePageFeatures);

    public Task<ApiResponse> GenericTimelineByIdAsync(string timelineId, int count, JsonObject? extraParams = null)
    {
        var variables = new JsonObject { ["timelineId"] = timelineId, ["count"] = count };
        if (extraParams is not null)
            foreach (var kv in extraParams) variables[kv.Key] = kv.Value.Clone();
        return GqlGetAsync(GqlEndpoint.GenericTimelineById, variables, Constants.GenericTimelineFeatures);
    }

    public Task<ApiResponse> BirdWatchOneNoteAsync(string noteId)
        => GqlGetAsync(GqlEndpoint.FetchCommunityNote, new JsonObject { ["note_id"] = noteId }, Constants.CommunityNoteFeatures);

    private Task<ApiResponse> GetUserTweetsAsync(string userId, int count, string? cursor, string endpoint)
    {
        var variables = new JsonObject
        {
            ["userId"] = userId,
            ["count"] = count,
            ["includePromotedContent"] = true,
            ["withQuickPromoteEligibilityTweetFields"] = true,
            ["withVoice"] = true,
            ["withV2Timeline"] = true,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(endpoint, variables, Constants.Features);
    }

    public Task<ApiResponse> UserTweetsAsync(string userId, int count, string? cursor)
        => GetUserTweetsAsync(userId, count, cursor, GqlEndpoint.UserTweets);

    public Task<ApiResponse> UserTweetsAndRepliesAsync(string userId, int count, string? cursor)
        => GetUserTweetsAsync(userId, count, cursor, GqlEndpoint.UserTweetsAndReplies);

    public Task<ApiResponse> UserMediaAsync(string userId, int count, string? cursor)
        => GetUserTweetsAsync(userId, count, cursor, GqlEndpoint.UserMedia);

    public Task<ApiResponse> UserLikesAsync(string userId, int count, string? cursor)
        => GetUserTweetsAsync(userId, count, cursor, GqlEndpoint.UserLikes);

    public Task<ApiResponse> UserHighlightsTweetsAsync(string userId, int count, string? cursor)
    {
        var variables = new JsonObject
        {
            ["userId"] = userId,
            ["count"] = count,
            ["includePromotedContent"] = true,
            ["withVoice"] = true,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.UserHighlightsTweets, variables, Constants.UserHighlightsTweetsFeatures, _base.BaseHeaders);
    }

    public Task<ApiResponse> HomeTimelineAsync(int count, IEnumerable<string>? seenTweetIds, string? cursor)
    {
        var variables = new JsonObject
        {
            ["count"] = count,
            ["includePromotedContent"] = true,
            ["latestControlAvailable"] = true,
            ["requestContext"] = "launch",
            ["withCommunity"] = true,
            ["seenTweetIds"] = (seenTweetIds ?? Array.Empty<string>()).ToJsonArray(),
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlPostAsync(GqlEndpoint.HomeTimeline, variables, Constants.Features);
    }

    public Task<ApiResponse> HomeLatestTimelineAsync(int count, IEnumerable<string>? seenTweetIds, string? cursor)
    {
        var variables = new JsonObject
        {
            ["count"] = count,
            ["includePromotedContent"] = true,
            ["latestControlAvailable"] = true,
            ["requestContext"] = "launch",
            ["withCommunity"] = true,
            ["seenTweetIds"] = (seenTweetIds ?? Array.Empty<string>()).ToJsonArray(),
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlPostAsync(GqlEndpoint.HomeLatestTimeline, variables, Constants.Features);
    }

    public Task<ApiResponse> FavoriteTweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.FavoriteTweet, new JsonObject { ["tweet_id"] = tweetId });

    public Task<ApiResponse> UnfavoriteTweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.UnfavoriteTweet, new JsonObject { ["tweet_id"] = tweetId });

    public Task<ApiResponse> RetweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.CreateRetweet, new JsonObject { ["tweet_id"] = tweetId, ["dark_request"] = false });

    public Task<ApiResponse> DeleteRetweetAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.DeleteRetweet, new JsonObject { ["source_tweet_id"] = tweetId, ["dark_request"] = false });

    public Task<ApiResponse> CreateBookmarkAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.CreateBookmark, new JsonObject { ["tweet_id"] = tweetId });

    public Task<ApiResponse> BookmarkTweetToFolderAsync(string tweetId, string folderId)
        => GqlPostAsync(GqlEndpoint.BookmarkToFolder, new JsonObject { ["tweet_id"] = tweetId, ["bookmark_collection_id"] = folderId });

    public Task<ApiResponse> DeleteBookmarkAsync(string tweetId)
        => GqlPostAsync(GqlEndpoint.DeleteBookmark, new JsonObject { ["tweet_id"] = tweetId });

    public Task<ApiResponse> BookmarksAsync(int count, string? cursor)
    {
        var variables = new JsonObject { ["count"] = count, ["includePromotedContent"] = true };
        var features = Constants.Copy(Constants.Features);
        features["graphql_timeline_v2_bookmark_timeline"] = true;
        if (cursor is not null) variables["cursor"] = cursor;
        var parameters = Utils.FlattenParams(new JsonObject { ["variables"] = variables, ["features"] = features });
        return _base.GetAsync(GqlEndpoint.Bookmarks, new RequestOptions { Params = parameters, Headers = _base.BaseHeaders });
    }

    public Task<ApiResponse> BookmarkFolderTimelineAsync(int count, string? cursor, string folderId)
    {
        var variables = new JsonObject
        {
            ["count"] = count,
            ["includePromotedContent"] = true,
            ["bookmark_collection_id"] = folderId,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.BookmarkFolderTimeline, variables, Constants.BookmarkFolderTimelineFeatures);
    }

    public Task<ApiResponse> DeleteAllBookmarksAsync() => GqlPostAsync(GqlEndpoint.BookmarksAllDelete, V());

    public Task<ApiResponse> BookmarkFoldersSliceAsync(string? cursor)
    {
        var variables = new JsonObject();
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.BookmarkFoldersSlice, variables);
    }

    public Task<ApiResponse> EditBookmarkFolderAsync(string folderId, string name)
        => GqlPostAsync(GqlEndpoint.EditBookmarkFolder, new JsonObject { ["bookmark_collection_id"] = folderId, ["name"] = name });

    public Task<ApiResponse> DeleteBookmarkFolderAsync(string folderId)
        => GqlPostAsync(GqlEndpoint.DeleteBookmarkFolder, new JsonObject { ["bookmark_collection_id"] = folderId });

    public Task<ApiResponse> CreateBookmarkFolderAsync(string name)
        => GqlPostAsync(GqlEndpoint.CreateBookmarkFolder, new JsonObject { ["name"] = name });

    private Task<ApiResponse> FriendshipsAsync(string userId, int count, string endpoint, string? cursor)
    {
        var variables = new JsonObject { ["userId"] = userId, ["count"] = count, ["includePromotedContent"] = false };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(endpoint, variables, Constants.Features);
    }

    public Task<ApiResponse> FollowersAsync(string userId, int count, string? cursor)
        => FriendshipsAsync(userId, count, GqlEndpoint.Followers, cursor);

    public Task<ApiResponse> BlueVerifiedFollowersAsync(string userId, int count, string? cursor)
        => FriendshipsAsync(userId, count, GqlEndpoint.BlueVerifiedFollowers, cursor);

    public Task<ApiResponse> FollowersYouKnowAsync(string userId, int count, string? cursor)
        => FriendshipsAsync(userId, count, GqlEndpoint.FollowersYouKnow, cursor);

    public Task<ApiResponse> FollowingAsync(string userId, int count, string? cursor)
        => FriendshipsAsync(userId, count, GqlEndpoint.Following, cursor);

    public Task<ApiResponse> UserCreatorSubscriptionsAsync(string userId, int count, string? cursor)
        => FriendshipsAsync(userId, count, GqlEndpoint.UserCreatorSubscriptions, cursor);

    public Task<ApiResponse> UserDmReactionMutationAddMutationAsync(string messageId, string conversationId, string emoji)
    {
        var variables = new JsonObject
        {
            ["messageId"] = messageId,
            ["conversationId"] = conversationId,
            ["reactionTypes"] = new JsonArray("Emoji"),
            ["emojiReactions"] = new JsonArray(emoji),
        };
        return GqlPostAsync(GqlEndpoint.UserDmReactionMutationAddMutation, variables);
    }

    public Task<ApiResponse> UserDmReactionMutationRemoveMutationAsync(string messageId, string conversationId, string emoji)
    {
        var variables = new JsonObject
        {
            ["conversationId"] = conversationId,
            ["messageId"] = messageId,
            ["reactionTypes"] = new JsonArray("Emoji"),
            ["emojiReactions"] = new JsonArray(emoji),
        };
        return GqlPostAsync(GqlEndpoint.UserDmReactionMutationRemoveMutation, variables);
    }

    public Task<ApiResponse> DmMessageDeleteMutationAsync(string messageId)
        => GqlPostAsync(GqlEndpoint.DmMessageDeleteMutation, new JsonObject { ["messageId"] = messageId });

    public Task<ApiResponse> AddParticipantsMutationAsync(string groupId, IEnumerable<string> userIds)
    {
        var variables = new JsonObject { ["addedParticipants"] = userIds.ToJsonArray(), ["conversationId"] = groupId };
        return GqlPostAsync(GqlEndpoint.AddParticipantsMutation, variables);
    }

    public Task<ApiResponse> CreateListAsync(string name, string description, bool isPrivate)
    {
        var variables = new JsonObject { ["isPrivate"] = isPrivate, ["name"] = name, ["description"] = description };
        return GqlPostAsync(GqlEndpoint.CreateList, variables, Constants.CreateListFeatures);
    }

    public Task<ApiResponse> DeleteListAsync(string listId)
        => GqlPostAsync(GqlEndpoint.DeleteList, new JsonObject { ["listId"] = listId }, Constants.CreateListFeatures);

    public Task<ApiResponse> EditListBannerAsync(string listId, string mediaId)
        => GqlPostAsync(GqlEndpoint.EditListBanner, new JsonObject { ["listId"] = listId, ["mediaId"] = mediaId }, Constants.ListFeatures);

    public Task<ApiResponse> DeleteListBannerAsync(string listId)
        => GqlPostAsync(GqlEndpoint.DeleteListBanner, new JsonObject { ["listId"] = listId }, Constants.ListFeatures);

    public Task<ApiResponse> UpdateListAsync(string listId, string? name, string? description, bool? isPrivate)
    {
        var variables = new JsonObject { ["listId"] = listId };
        if (name is not null) variables["name"] = name;
        if (description is not null) variables["description"] = description;
        if (isPrivate is not null) variables["isPrivate"] = isPrivate.Value;
        return GqlPostAsync(GqlEndpoint.UpdateList, variables, Constants.ListFeatures);
    }

    public Task<ApiResponse> ListAddMemberAsync(string listId, string userId)
        => GqlPostAsync(GqlEndpoint.ListAddMember, new JsonObject { ["listId"] = listId, ["userId"] = userId }, Constants.ListFeatures);

    public Task<ApiResponse> ListRemoveMemberAsync(string listId, string userId)
        => GqlPostAsync(GqlEndpoint.ListRemoveMember, new JsonObject { ["listId"] = listId, ["userId"] = userId }, Constants.ListFeatures);

    public Task<ApiResponse> ListManagementPaceTimelineAsync(int count, string? cursor)
    {
        var variables = new JsonObject { ["count"] = count };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.ListManagementPaceTimeline, variables, Constants.Features);
    }

    public Task<ApiResponse> ListByRestIdAsync(string listId)
        => GqlGetAsync(GqlEndpoint.ListByRestId, new JsonObject { ["listId"] = listId }, Constants.ListFeatures);

    public Task<ApiResponse> ListLatestTweetsTimelineAsync(string listId, int count, string? cursor)
    {
        var variables = new JsonObject { ["listId"] = listId, ["count"] = count };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.ListLatestTweetsTimeline, variables, Constants.Features);
    }

    private Task<ApiResponse> ListUsersAsync(string endpoint, string listId, int count, string? cursor)
    {
        var variables = new JsonObject { ["listId"] = listId, ["count"] = count };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(endpoint, variables, Constants.Features);
    }

    public Task<ApiResponse> ListMembersAsync(string listId, int count, string? cursor)
        => ListUsersAsync(GqlEndpoint.ListMembers, listId, count, cursor);

    public Task<ApiResponse> ListSubscribersAsync(string listId, int count, string? cursor)
        => ListUsersAsync(GqlEndpoint.ListSubscribers, listId, count, cursor);

    public Task<ApiResponse> SearchCommunityAsync(string query, string? cursor)
    {
        var variables = new JsonObject { ["query"] = query };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.SearchCommunity, variables);
    }

    public Task<ApiResponse> CommunityQueryAsync(string communityId)
    {
        var features = new JsonObject
        {
            ["c9s_list_members_action_api_enabled"] = false,
            ["c9s_superc9s_indication_enabled"] = false,
        };
        return GqlGetAsync(GqlEndpoint.CommunityQuery, new JsonObject { ["communityId"] = communityId }, features);
    }

    public Task<ApiResponse> CommunityMediaTimelineAsync(string communityId, int count, string? cursor)
    {
        var variables = new JsonObject { ["communityId"] = communityId, ["count"] = count, ["withCommunity"] = true };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.CommunityMediaTimeline, variables, Constants.CommunityTweetsFeatures);
    }

    public Task<ApiResponse> CommunityTweetsTimelineAsync(string communityId, string rankingMode, int count, string? cursor)
    {
        var variables = new JsonObject
        {
            ["communityId"] = communityId,
            ["count"] = count,
            ["withCommunity"] = true,
            ["rankingMode"] = rankingMode,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.CommunityTweetsTimeline, variables, Constants.CommunityTweetsFeatures);
    }

    public Task<ApiResponse> CommunitiesMainPageTimelineAsync(int count, string? cursor)
    {
        var variables = new JsonObject { ["count"] = count, ["withCommunity"] = true };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.CommunitiesMainPageTimeline, variables, Constants.CommunityTweetsFeatures);
    }

    public Task<ApiResponse> JoinCommunityAsync(string communityId)
        => GqlPostAsync(GqlEndpoint.JoinCommunity, new JsonObject { ["communityId"] = communityId }, Constants.JoinCommunityFeatures);

    public Task<ApiResponse> LeaveCommunityAsync(string communityId)
        => GqlPostAsync(GqlEndpoint.LeaveCommunity, new JsonObject { ["communityId"] = communityId }, Constants.JoinCommunityFeatures);

    public Task<ApiResponse> RequestToJoinCommunityAsync(string communityId, string? answer)
    {
        var variables = new JsonObject { ["communityId"] = communityId, ["answer"] = answer ?? "" };
        return GqlPostAsync(GqlEndpoint.RequestToJoinCommunity, variables, Constants.JoinCommunityFeatures);
    }

    private Task<ApiResponse> GetCommunityUsersAsync(string endpoint, string communityId, int count, string? cursor)
    {
        var variables = new JsonObject { ["communityId"] = communityId, ["count"] = count };
        var features = new JsonObject { ["responsive_web_graphql_timeline_navigation_enabled"] = true };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(endpoint, variables, features);
    }

    public Task<ApiResponse> MembersSliceTimelineQueryAsync(string communityId, int count, string? cursor)
        => GetCommunityUsersAsync(GqlEndpoint.MembersSliceTimelineQuery, communityId, count, cursor);

    public Task<ApiResponse> ModeratorsSliceTimelineQueryAsync(string communityId, int count, string? cursor)
        => GetCommunityUsersAsync(GqlEndpoint.ModeratorsSliceTimelineQuery, communityId, count, cursor);

    public Task<ApiResponse> CommunityTweetSearchModuleQueryAsync(string communityId, string query, int count, string? cursor)
    {
        var variables = new JsonObject
        {
            ["count"] = count,
            ["query"] = query,
            ["communityId"] = communityId,
            ["includePromotedContent"] = false,
            ["withBirdwatchNotes"] = true,
            ["withVoice"] = false,
            ["isListMemberTargetUserId"] = "0",
            ["withCommunity"] = false,
            ["withSafetyModeUserFields"] = true,
        };
        if (cursor is not null) variables["cursor"] = cursor;
        return GqlGetAsync(GqlEndpoint.CommunityTweetSearchModuleQuery, variables, Constants.CommunityTweetsFeatures);
    }

    public Task<ApiResponse> TweetResultsByRestIdsAsync(IEnumerable<string> tweetIds)
    {
        var variables = new JsonObject
        {
            ["tweetIds"] = tweetIds.ToJsonArray(),
            ["includePromotedContent"] = true,
            ["withBirdwatchNotes"] = true,
            ["withVoice"] = true,
            ["withCommunity"] = true,
        };
        return GqlGetAsync(GqlEndpoint.TweetResultsByRestIds, variables, Constants.TweetResultsByRestIdsFeatures);
    }

    // ---------------------------------------------------------------- Spaces

    public Task<ApiResponse> AudioSpaceByIdAsync(string spaceId, bool withReplays = true, bool withListeners = true)
    {
        var variables = new JsonObject
        {
            ["id"] = spaceId,
            ["isMetatagsQuery"] = false,
            ["withReplays"] = withReplays,
            ["withListeners"] = withListeners,
            ["withSuperFollowsUserFields"] = true,
            ["withSuperFollowsTweetFields"] = true,
            ["withBirdwatchPivots"] = false,
            ["withDownvotePerspective"] = false,
            ["withReactionsMetadata"] = false,
            ["withReactionsPerspective"] = false,
            ["withScheduledSpaces"] = true,
        };
        return GqlGetAsync(GqlEndpoint.AudioSpaceById, variables, Constants.AudioSpaceFeatures);
    }

    /// <remarks>The web client executes this operation as a POST even though it is declared `query`; GET returns 404.</remarks>
    public Task<ApiResponse> AudioSpaceSearchAsync(string query, string filter = "Live")
        => GqlPostAsync(GqlEndpoint.AudioSpaceSearch, new JsonObject { ["filter"] = filter, ["query"] = query });

    public Task<ApiResponse> BrowseSpaceTopicsAsync() => GqlGetAsync(GqlEndpoint.BrowseSpaceTopics, V());

    public Task<ApiResponse> AudioSpaceAddSharingAsync(string spaceId, string tweetId)
    {
        var variables = new JsonObject
        {
            ["audio_space_id"] = spaceId,
            ["sharing"] = new JsonObject { ["shared_tweet"] = new JsonObject { ["tweet_id"] = tweetId } },
        };
        return GqlPostAsync(GqlEndpoint.AudioSpaceAddSharing, variables);
    }

    public Task<ApiResponse> AudioSpaceDeleteSharingAsync(string spaceId, string sharingId)
        => GqlPostAsync(GqlEndpoint.AudioSpaceDeleteSharing, new JsonObject { ["audio_space_id"] = spaceId, ["sharing_id"] = sharingId });

    public Task<ApiResponse> SubscribeToScheduledSpaceAsync(string spaceId)
        => GqlPostAsync(GqlEndpoint.SubscribeToScheduledSpace, new JsonObject { ["id"] = spaceId });

    public Task<ApiResponse> UnsubscribeFromScheduledSpaceAsync(string spaceId)
        => GqlPostAsync(GqlEndpoint.UnsubscribeFromScheduledSpace, new JsonObject { ["id"] = spaceId });

    public Task<ApiResponse> AuthenticatePeriscopeAsync() => GqlGetAsync(GqlEndpoint.AuthenticatePeriscope, V());

    // ---------------------------------------------------------------- guest

    public Task<ApiResponse> TweetResultByRestIdAsync(string tweetId)
    {
        var variables = new JsonObject
        {
            ["tweetId"] = tweetId,
            ["withCommunity"] = false,
            ["includePromotedContent"] = false,
            ["withVoice"] = false,
        };
        var extra = new JsonObject
        {
            ["fieldToggles"] = new JsonObject
            {
                ["withArticleRichContentState"] = true,
                ["withArticlePlainText"] = false,
                ["withGrokAnalyze"] = false,
            },
        };
        return GqlGetAsync(GqlEndpoint.TweetResultByRestId, variables, Constants.TweetResultByRestIdFeatures, extraParams: extra);
    }
}
