using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// ライブラリ全体で共有する定数。公開 Bearer トークン、ホスト名、
/// GraphQL の各ドキュメントが要求する機能フラグ（features）のセットを収めています。
/// </summary>
public static class Constants
{
    /// <summary>全アカウント共通の Bearer トークン。変更する必要はありません。</summary>
    public const string Token = "AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA";

    public const string Domain = "x.com";

    /// <summary>
    /// クライアントが認証に使うホスト。<c>ui_metrics</c> は意図的に twitter.com に
    /// 留まっているため、x.com だけに固定した Cookie では届きません。
    /// ログイン済みクライアントとゲストクライアントで食い違わないよう共有しています。
    /// </summary>
    public static readonly string[] CookieDomains = { "." + Domain, ".twitter.com" };

    /// <summary>GenericTimelineById が返すトレンドカテゴリのタイムライン ID。</summary>
    public static readonly IReadOnlyDictionary<string, string> TimelineIds = new Dictionary<string, string>
    {
        ["trending"] = "VGltZWxpbmU6DAC2CwABAAAACHRyZW5kaW5nAAA=",
        ["for-you"] = "VGltZWxpbmU6DAC2CwABAAAAB2Zvcl95b3UAAA==",
        ["news"] = "VGltZWxpbmU6DAC2CwABAAAABG5ld3MAAA==",
        ["sports"] = "VGltZWxpbmU6DAC2CwABAAAABnNwb3J0cwAA",
        ["entertainment"] = "VGltZWxpbmU6DAC2CwABAAAADWVudGVydGFpbm1lbnQAAA==",
    };

    private static JsonObject Flags(params (string Key, bool Value)[] flags)
    {
        var o = new JsonObject();
        foreach (var (key, value) in flags) o[key] = value;
        return o;
    }

    private static JsonObject Merge(JsonObject a, params (string Key, bool Value)[] extra)
    {
        var o = (JsonObject)a.DeepClone();
        foreach (var (key, value) in extra) o[key] = value;
        return o;
    }

    /// <summary>機能フラグセットの新しいコピーを返します（JsonNode は親を 1 つしか持てないため）。</summary>
    public static JsonObject Copy(JsonObject features) => (JsonObject)features.DeepClone();

    public static readonly JsonObject Features = Flags(
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("responsive_web_media_download_video_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_enhance_cards_enabled", false),
        // Without this X silently omits `parody_commentary_fan_label` from every
        // user it returns - no error, the key is simply absent.
        ("profile_label_improvements_pcf_label_in_post_enabled", true)
    );

    /// <summary>
    /// AudioSpaceById クエリが要求する機能フラグ。Web クライアントは標準セットに
    /// 加えて <c>spaces_2022_h2_*</c> の 2 つを固定で送ります。
    /// </summary>
    public static readonly JsonObject AudioSpaceFeatures = Merge(Features,
        ("spaces_2022_h2_spaces_communities", true),
        ("spaces_2022_h2_clipping", true),
        ("responsive_web_grok_analyze_button_fetch_trends_enabled", true),
        ("responsive_web_grok_analyze_post_followups_enabled", true),
        ("responsive_web_grok_show_grok_translated_post", true),
        ("responsive_web_grok_analysis_button_from_backend", true),
        ("responsive_web_grok_image_annotation_enabled", true),
        ("responsive_web_grok_imagine_annotation_enabled", true),
        ("responsive_web_grok_community_note_auto_translation_is_enabled", true),
        ("content_disclosure_indicator_enabled", true),
        ("content_disclosure_ai_generated_indicator_enabled", true),
        ("post_ctas_fetch_enabled", true),
        ("rweb_cashtags_enabled", true)
    );

    public static readonly JsonObject UserFeatures = Flags(
        ("hidden_profile_likes_enabled", true),
        ("hidden_profile_subscriptions_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("subscriptions_verification_info_is_identity_verified_enabled", true),
        ("subscriptions_verification_info_verified_since_enabled", true),
        ("highlights_tweets_tab_ui_enabled", true),
        ("responsive_web_twitter_article_notes_tab_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        // See the note in Features: this is what makes X return the
        // parody/commentary/fan label on a profile lookup.
        ("profile_label_improvements_pcf_label_in_post_enabled", true)
    );

    /// <summary>
    /// 2026-07-28 に Web クライアントから採取。リストの「管理」系操作はこの短い
    /// セットを取り、リストの「タイムライン」系操作は長いツイートタイムライン用の
    /// セットを取ります。
    /// </summary>
    public static readonly JsonObject CreateListFeatures = Flags(
        ("profile_label_improvements_pcf_label_in_post_enabled", true),
        ("responsive_web_profile_redirect_enabled", true),
        ("rweb_tipjar_consumption_enabled", false),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true)
    );

    public static readonly JsonObject ListFeatures = Flags(
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true)
    );

    public static readonly JsonObject CommunityNoteFeatures = Flags(
        ("responsive_web_birdwatch_media_notes_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("rweb_tipjar_consumption_enabled", false),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false)
    );

    public static readonly JsonObject CommunityTweetsFeatures = Flags(
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject JoinCommunityFeatures = Flags(
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true)
    );

    public static readonly JsonObject NoteTweetFeatures = Flags(
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("articles_preview_enabled", false),
        ("rweb_video_timestamps_enabled", true),
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("tweet_with_visibility_results_prefer_gql_media_interstitial_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject SimilarPostsFeatures = Flags(
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("articles_preview_enabled", false),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("tweet_with_visibility_results_prefer_gql_media_interstitial_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject BookmarkFolderTimelineFeatures = Flags(
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("articles_preview_enabled", false),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("tweet_with_visibility_results_prefer_gql_media_interstitial_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject TweetResultByRestIdFeatures = Flags(
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("articles_preview_enabled", true),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject UserHighlightsTweetsFeatures = Flags(
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("articles_preview_enabled", true),
        ("tweetypie_unmention_optimization_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject SearchTimelineFeatures = Flags(
        ("rweb_video_screen_enabled", false),
        ("rweb_cashtags_enabled", true),
        ("profile_label_improvements_pcf_label_in_post_enabled", true),
        ("responsive_web_profile_redirect_enabled", false),
        ("rweb_tipjar_consumption_enabled", false),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("premium_content_api_read_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("responsive_web_grok_analyze_button_fetch_trends_enabled", false),
        ("responsive_web_grok_analyze_post_followups_enabled", true),
        ("responsive_web_jetfuel_frame", true),
        ("responsive_web_grok_share_attachment_enabled", true),
        ("responsive_web_grok_annotations_enabled", true),
        ("articles_preview_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("content_disclosure_indicator_enabled", true),
        ("content_disclosure_ai_generated_indicator_enabled", true),
        ("responsive_web_grok_show_grok_translated_post", true),
        ("responsive_web_grok_analysis_button_from_backend", true),
        ("post_ctas_fetch_enabled", true),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", false),
        ("responsive_web_grok_image_annotation_enabled", true),
        ("responsive_web_grok_imagine_annotation_enabled", true),
        ("responsive_web_grok_community_note_auto_translation_is_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject TweetResultsByRestIdsFeatures = Flags(
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("premium_content_api_read_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("responsive_web_grok_analyze_button_fetch_trends_enabled", false),
        ("responsive_web_grok_analyze_post_followups_enabled", true),
        ("responsive_web_grok_share_attachment_enabled", true),
        ("articles_preview_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("rweb_video_timestamps_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        // Was false here while every other set has it on, so tweets fetched by id
        // came back without the parody/commentary/fan label their authors carry.
        ("profile_label_improvements_pcf_label_in_post_enabled", true),
        ("rweb_tipjar_consumption_enabled", true),
        ("responsive_web_graphql_exclude_directive_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject ExplorePageFeatures = Flags(
        ("rweb_video_screen_enabled", false),
        ("payments_enabled", false),
        ("profile_label_improvements_pcf_label_in_post_enabled", true),
        ("rweb_tipjar_consumption_enabled", true),
        ("verified_phone_label_enabled", false),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("premium_content_api_read_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("responsive_web_grok_analyze_button_fetch_trends_enabled", false),
        ("responsive_web_grok_analyze_post_followups_enabled", true),
        ("responsive_web_jetfuel_frame", true),
        ("responsive_web_grok_share_attachment_enabled", true),
        ("articles_preview_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("responsive_web_grok_show_grok_translated_post", false),
        ("responsive_web_grok_analysis_button_from_backend", true),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_grok_image_annotation_enabled", true),
        ("responsive_web_grok_imagine_annotation_enabled", true),
        ("responsive_web_grok_community_note_auto_translation_is_enabled", false),
        ("responsive_web_enhance_cards_enabled", false)
    );

    public static readonly JsonObject GenericTimelineFeatures = Flags(
        ("rweb_video_screen_enabled", false),
        ("payments_enabled", false),
        ("profile_label_improvements_pcf_label_in_post_enabled", true),
        ("rweb_tipjar_consumption_enabled", true),
        ("verified_phone_label_enabled", false),
        ("creator_subscriptions_tweet_preview_api_enabled", true),
        ("responsive_web_graphql_timeline_navigation_enabled", true),
        ("responsive_web_graphql_skip_user_profile_image_extensions_enabled", false),
        ("premium_content_api_read_enabled", false),
        ("communities_web_enable_tweet_community_results_fetch", true),
        ("c9s_tweet_anatomy_moderator_badge_enabled", true),
        ("responsive_web_grok_analyze_button_fetch_trends_enabled", false),
        ("responsive_web_grok_analyze_post_followups_enabled", true),
        ("responsive_web_jetfuel_frame", true),
        ("responsive_web_grok_share_attachment_enabled", true),
        ("articles_preview_enabled", true),
        ("responsive_web_edit_tweet_api_enabled", true),
        ("graphql_is_translatable_rweb_tweet_is_translatable_enabled", true),
        ("view_counts_everywhere_api_enabled", true),
        ("longform_notetweets_consumption_enabled", true),
        ("responsive_web_twitter_article_tweet_consumption_enabled", true),
        ("tweet_awards_web_tipping_enabled", false),
        ("responsive_web_grok_show_grok_translated_post", false),
        ("responsive_web_grok_analysis_button_from_backend", true),
        ("creator_subscriptions_quote_tweet_preview_enabled", false),
        ("freedom_of_speech_not_reach_fetch_enabled", true),
        ("standardized_nudges_misinfo", true),
        ("tweet_with_visibility_results_prefer_gql_limited_actions_policy_enabled", true),
        ("longform_notetweets_rich_text_read_enabled", true),
        ("longform_notetweets_inline_media_enabled", true),
        ("responsive_web_grok_image_annotation_enabled", true),
        ("responsive_web_grok_imagine_annotation_enabled", true),
        ("responsive_web_grok_community_note_auto_translation_is_enabled", false),
        ("responsive_web_enhance_cards_enabled", false)
    );
}
