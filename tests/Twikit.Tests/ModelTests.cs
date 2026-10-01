using System.Text.Json.Nodes;
using Twikit.Captcha;
using Twikit.Spaces;
using Twikit.UiMetrics;
using Xunit;

namespace Twikit.Tests;

public class ModelTests
{
    private static readonly Client DummyClient = new("ja", handler: new FakeHandler((_, _) => throw new InvalidOperationException("no network in model tests")));

    public const string SampleTweetEntry = """
    {
      "entryId": "tweet-1519480761749016577",
      "content": {
        "itemContent": {
          "tweet_results": {
            "result": {
              "__typename": "Tweet",
              "rest_id": "1519480761749016577",
              "source": "<a href=\"http://twitter.com/download/iphone\" rel=\"nofollow\">Twitter for iPhone</a>",
              "core": {
                "user_results": {
                  "result": {
                    "__typename": "User",
                    "rest_id": "44196397",
                    "is_blue_verified": true,
                    "parody_commentary_fan_label": "None",
                    "core": {"created_at": "Tue Jun 02 20:12:29 +0000 2009", "name": "Elon Musk", "screen_name": "elonmusk"},
                    "avatar": {"image_url": "https://pbs.twimg.com/profile_images/1.jpg"},
                    "verification": {"verified": false, "verified_type": "Business"},
                    "relationship_counts": {"followers": 100, "following": 10},
                    "tweet_counts": {"tweets": 5, "media_tweets": 2},
                    "legacy": {"description": "legacy bio", "listed_count": 7, "entities": {"description": {"urls": [{"url": "https://t.co/x", "expanded_url": "https://example.com"}]}}},
                    "affiliates_highlighted_label": {"label": {"description": "Automated", "longDescription": {"text": "Automated by @operator"}}}
                  }
                }
              },
              "edit_control": {"edit_tweet_ids": ["1519480761749016577"], "editable_until_msecs": "1651184057000", "is_edit_eligible": false, "edits_remaining": "5"},
              "is_translatable": false,
              "views": {"count": "12345", "state": "EnabledWithCount"},
              "card": {
                "rest_id": "card://1",
                "legacy": {
                  "name": "poll2choice_text_only",
                  "binding_values": [
                    {"key": "choice1_label", "value": {"string_value": "Yes"}},
                    {"key": "choice2_label", "value": {"string_value": "No"}},
                    {"key": "choice1_count", "value": {"string_value": "10"}},
                    {"key": "duration_minutes", "value": {"string_value": "1440"}},
                    {"key": "counts_are_final", "value": {"boolean_value": false}},
                    {"key": "title", "value": {"string_value": "Card title"}},
                    {"key": "thumbnail_image_original", "value": {"image_value": {"url": "https://pbs.twimg.com/card.jpg"}}}
                  ]
                }
              },
              "legacy": {
                "created_at": "Thu Apr 28 00:56:58 +0000 2022",
                "full_text": "hello #csharp https://t.co/abc",
                "lang": "en",
                "is_quote_status": false,
                "reply_count": 3, "favorite_count": 4, "retweet_count": 5, "quote_count": 6, "bookmark_count": 7,
                "favorited": false, "bookmarked": true, "possibly_sensitive": false,
                "entities": {"hashtags": [{"text": "csharp"}], "urls": [{"url": "https://t.co/abc", "expanded_url": "https://example.com/abc"}], "media": [{"id_str": "m1", "type": "photo", "media_url_https": "https://pbs.twimg.com/media/m1.jpg"}]},
                "extended_entities": {"media": [
                  {"id_str": "m1", "type": "photo", "media_url_https": "https://pbs.twimg.com/media/m1.jpg", "original_info": {"width": 100, "height": 50}},
                  {"id_str": "m2", "type": "video", "media_url_https": "https://pbs.twimg.com/media/m2.jpg", "video_info": {"aspect_ratio": [16, 9], "duration_millis": 1000, "variants": [
                    {"content_type": "application/x-mpegURL", "url": "https://video.twimg.com/m.m3u8"},
                    {"content_type": "video/mp4", "bitrate": 832000, "url": "https://video.twimg.com/m.mp4"}
                  ]}},
                  {"id_str": "m3", "type": "hologram"}
                ]}
              }
            }
          }
        }
      }
    }
    """;

    [Fact]
    public void TweetFromDataReadsEveryField()
    {
        var tweet = Tweet.FromData(DummyClient, JsonNode.Parse(SampleTweetEntry))!;
        Assert.Equal("1519480761749016577", tweet.Id);
        Assert.Equal("hello #csharp https://t.co/abc", tweet.Text);
        Assert.Equal("https://x.com/elonmusk/status/1519480761749016577", tweet.Url);
        Assert.Equal("Twitter for iPhone", tweet.SourceName);
        Assert.Equal("http://twitter.com/download/iphone", tweet.SourceUrl);
        Assert.Equal(3, tweet.ReplyCount);
        Assert.Equal(7, tweet.BookmarkCount);
        Assert.True(tweet.Bookmarked);
        Assert.Equal(12345L, tweet.ViewCount);
        Assert.Equal("EnabledWithCount", tweet.ViewCountState);
        Assert.Equal(new[] { "csharp" }, tweet.Hashtags);
        Assert.Single(tweet.Urls);
        Assert.Equal("https://example.com/abc", tweet.Urls[0].ExpandedUrl);
        Assert.Equal(new DateTimeOffset(2022, 4, 28, 0, 56, 58, TimeSpan.Zero), tweet.CreatedAtDatetime);
        Assert.Equal(new List<string> { "1519480761749016577" }, tweet.EditTweetIds);
        Assert.Equal(5, tweet.EditsRemaining);
        Assert.True(tweet.HasCard);
        Assert.Equal("Card title", tweet.ThumbnailTitle);
        Assert.Equal("https://pbs.twimg.com/card.jpg", tweet.ThumbnailUrl);
        Assert.Null(tweet.Quote);
        Assert.Null(tweet.RetweetedTweet);
        Assert.Equal("hello #csharp https://t.co/abc", tweet.FullText);

        // extended_entities wins over entities and unknown media types are skipped with a warning
        var warnings = new List<string>();
        TwikitWarnings.Handler = warnings.Add;
        try
        {
            var media = tweet.Media;
            Assert.Equal(2, media.Count);
            Assert.IsType<Photo>(media[0]);
            Assert.Equal(100, media[0].Width);
            Assert.Equal("https://pbs.twimg.com/media/m1.jpg?name=orig", media[0].SourceUrl);
            var video = Assert.IsType<Video>(media[1]);
            Assert.Equal((16, 9), video.AspectRatio);
            Assert.Single(video.Streams);
            Assert.Equal(832000L, video.Streams[0].Bitrate);
            Assert.Single(warnings);
            Assert.Contains("hologram", warnings[0]);
        }
        finally
        {
            TwikitWarnings.Handler = null;
        }

        var poll = tweet.Poll!;
        Assert.Equal("card://1", poll.Id);
        Assert.Equal(2, poll.Choices.Count);
        Assert.Equal("Yes", poll.Choices[0].Label);
        Assert.Equal("10", poll.Choices[0].Count);
        Assert.Equal("0", poll.Choices[1].Count);
        Assert.Equal(1440, poll.DurationMinutes);
        Assert.False(poll.CountsAreFinal);
        Assert.Null(poll.SelectedChoice);

        var user = tweet.User!;
        Assert.Equal("elonmusk", user.ScreenName);
        Assert.Equal("Elon Musk", user.Name);
        Assert.Equal(100, user.FollowersCount);
        Assert.Equal(10, user.FollowingCount);
        Assert.Equal(5, user.StatusesCount);
        Assert.Equal(2, user.MediaCount);
        Assert.Equal(7, user.ListedCount);
        Assert.Equal("legacy bio", user.Description);
        Assert.Single(user.DescriptionUrls);
        Assert.False(user.Verified);
        Assert.Equal("Business", user.VerifiedType);
        Assert.True(user.IsBlueVerified);
        Assert.Null(user.ParodyCommentaryFanLabel);
        Assert.True(user.IsAutomated);
        Assert.Equal("operator", user.AutomatedBy);
        Assert.Equal(new DateTimeOffset(2009, 6, 2, 20, 12, 29, TimeSpan.Zero), user.CreatedAtDatetime);
        Assert.Equal(tweet, Tweet.FromData(DummyClient, JsonNode.Parse(SampleTweetEntry)));
    }

    [Fact]
    public void TweetFromDataRejectsTombstonesAndPromotedUsers()
    {
        Assert.Null(Tweet.FromData(DummyClient, JsonNode.Parse("""{"content":{"itemContent":{"tweet_results":{"result":{"__typename":"TweetTombstone"}}}}}""")));
        Assert.Null(Tweet.FromData(DummyClient, JsonNode.Parse("""{"result":{"core":{"name":"advertiser"},"legacy":{}}}""")));
        Assert.Null(Tweet.FromData(DummyClient, JsonNode.Parse("""{"result":{"core":{"user_results":{"result":{"rest_id":"1"}}}}}""")));
        Assert.Null(Tweet.FromData(DummyClient, JsonNode.Parse("""{"entryId":"cursor"}""")));
        // TweetWithVisibilityResults nests the tweet one level down
        var nested = Tweet.FromData(DummyClient, JsonNode.Parse("""{"result":{"__typename":"TweetWithVisibilityResults","tweet":{"rest_id":"9","core":{"user_results":{"result":{"rest_id":"1"}}},"legacy":{"full_text":"t"}}}}"""));
        Assert.Equal("9", nested!.Id);
    }

    [Fact]
    public void RetweetFullTextComesFromOriginal()
    {
        var data = JsonNode.Parse("""{"result":{"rest_id":"2","core":{"user_results":{"result":{"rest_id":"1"}}},"legacy":{"full_text":"RT @a: truncated…","retweeted_status_result":{"result":{"rest_id":"3","core":{"user_results":{"result":{"rest_id":"5"}}},"legacy":{"full_text":"the whole text"}}}}}}""");
        var tweet = Tweet.FromData(DummyClient, data)!;
        Assert.Equal("RT @a: truncated…", tweet.Text);
        Assert.Equal("the whole text", tweet.FullText);
        Assert.Equal("3", tweet.RetweetedTweet!.Id);
    }

    [Fact]
    public void NoteTweetProvidesFullTextAndEntities()
    {
        var data = JsonNode.Parse("""{"result":{"rest_id":"2","core":{"user_results":{"result":{"rest_id":"1"}}},"note_tweet":{"note_tweet_results":{"result":{"text":"long text","entity_set":{"hashtags":[{"text":"h"}],"urls":[{"url":"u"}]}}}},"legacy":{"full_text":"short"}}}""");
        var tweet = Tweet.FromData(DummyClient, data)!;
        Assert.Equal("long text", tweet.FullText);
        Assert.Equal(new[] { "h" }, tweet.Hashtags);
        Assert.Equal("u", tweet.Urls[0].Url);
    }

    [Fact]
    public void UserFallsBackToLegacyAndReadsRelationship()
    {
        var data = (JsonObject)JsonNode.Parse("""{"rest_id":"1","legacy":{"name":"Legacy Name","screen_name":"legacy","followers_count":42,"friends_count":3,"verified":true,"following":true,"withheld_in_countries":["JP"],"pinned_tweet_ids_str":["7"]},"relationship_perspectives":{"blocking":true},"privacy":{"protected":true},"location":"Tokyo"}""")!;
        var user = new User(DummyClient, data);
        Assert.Equal("Legacy Name", user.Name);
        Assert.Equal("legacy", user.ScreenName);
        Assert.Equal(42, user.FollowersCount);
        Assert.Equal(3, user.FollowingCount);
        Assert.True(user.Verified);
        Assert.True(user.Following);
        Assert.True(user.Blocking);
        Assert.True(user.Protected);
        Assert.Equal(new[] { "JP" }, user.WithheldInCountries);
        Assert.Equal(new[] { "7" }, user.PinnedTweetIds);
        // `location` as a v1.1 string must not be read as a sub-object
        Assert.Equal("", user.Location);
        Assert.Equal("<User id=\"1\">", user.ToString());
    }

    [Fact]
    public void ListRequiresIdAndName()
    {
        Assert.Throws<NotFoundException>(() => new TwitterList(DummyClient, (JsonObject)JsonNode.Parse("""{"id_str":"1"}""")!));
        Assert.Throws<NotFoundException>(() => new TwitterList(DummyClient, new JsonObject()));
        var list = new TwitterList(DummyClient, (JsonObject)JsonNode.Parse("""{"id_str":"1","name":"L","created_at":1700000000000,"member_count":2,"mode":"Public","default_banner_media":{"media_info":{"url":"b"}}}""")!);
        Assert.Equal("L", list.Name);
        Assert.Equal(2, list.MemberCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000), list.CreatedAtDatetime);
        Assert.Equal("b", list.Banner.Str("url"));
        var seconds = new TwitterList(DummyClient, (JsonObject)JsonNode.Parse("""{"id_str":"1","name":"L","created_at":1700000000}""")!);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), seconds.CreatedAtDatetime);
    }

    [Fact]
    public void CommunityDecodesCreatorWithoutRestId()
    {
        var data = (JsonObject)JsonNode.Parse("""{"rest_id":"c1","name":"Comm","members_facepile_results":[{"result":{"avatar":{"image_url":"a"}}}],"default_banner_media":{"media_info":{}},"creator_results":{"result":{"id":"VXNlcjoxMjM=","core":{"screen_name":"creator"},"verification":{"verified":true}}},"rules":[{"rest_id":"r1","name":"Be nice"}]}""")!;
        var community = new Community(DummyClient, data);
        Assert.Equal("c1", community.Id);
        Assert.Null(community.Creator);
        Assert.Equal(new CommunityCreator("123", "creator", true), community.CreatorInfo);
        Assert.Equal(new[] { "a" }, community.MembersFacepileResults);
        Assert.Single(community.Rules!);
        Assert.Throws<NotFoundException>(() => new Community(DummyClient, new JsonObject()));
    }

    [Fact]
    public void ConversationResolvesPartnerAndGroups()
    {
        var one = new Conversation(DummyClient, (JsonObject)JsonNode.Parse("""{"conversation_id":"1-2","type":"ONE_TO_ONE","participants":[{"user_id":"1"},{"user_id":"2"}],"trusted":true}""")!, "2");
        Assert.Equal("1", one.PartnerId);
        Assert.False(one.IsGroup);
        Assert.True(one.Trusted);
        var self = new Conversation(DummyClient, (JsonObject)JsonNode.Parse("""{"conversation_id":"2-2","type":"ONE_TO_ONE","participants":[{"user_id":"2"}]}""")!, "2");
        Assert.Equal("2", self.PartnerId);
        var group = new Conversation(DummyClient, (JsonObject)JsonNode.Parse("""{"conversation_id":"g","type":"GROUP_DM","name":"team","participants":[{"user_id":"1"},{"user_id":"2"},{"user_id":"3"}]}""")!, "2");
        Assert.True(group.IsGroup);
        Assert.Null(group.PartnerId);
        Assert.Equal("team", group.Name);
    }

    [Fact]
    public void MessageReadsAttachmentAndReply()
    {
        var message = new Message(DummyClient, (JsonObject)JsonNode.Parse("""{"id":"m1","time":"1","text":"hi","attachment":{"photo":{"media_url_https":"https://ton.twitter.com/x.jpg"}},"reply_data":{"id":"m0","text":"orig"}}""")!, "1", "2");
        Assert.Equal("https://ton.twitter.com/x.jpg", message.AttachmentUrl);
        Assert.Equal("m0", message.RepliedToId);
        Assert.Equal("orig", message.RepliedToText);
        Assert.Equal("<Message id=\"m1\">", message.ToString());
    }

    [Fact]
    public void GroupUsesParticipantsAsRoster()
    {
        var data = (JsonObject)JsonNode.Parse("""{"conversation_timeline":{"conversations":{"g1":{"name":"team","participants":[{"user_id":"1"}]}},"users":{"1":{"id_str":"1","screen_name":"a"},"2":{"id_str":"2","screen_name":"left"}}}}""")!;
        var group = new Group(DummyClient, "g1", data);
        Assert.Equal("team", group.Name);
        Assert.Single(group.Members);
        Assert.Equal("a", group.Members[0].ScreenName);
        var fresh = new Group(DummyClient, "g2", (JsonObject)JsonNode.Parse("""{"conversation_timeline":{"users":{"1":{"id_str":"1"}}}}""")!);
        Assert.Null(fresh.Name);
        Assert.Single(fresh.Members);
    }

    [Fact]
    public void ArticleAndCommunityNote()
    {
        var article = new Article((JsonObject)JsonNode.Parse("""{"rest_id":"a","title":"T","preview_text":"p","cover_media":{"media_info":{"original_img_url":"img"}},"metadata":{"first_published_at_secs":10},"lifecycle_state":{"modified_at_secs":20}}""")!);
        Assert.Equal("T", article.Title);
        Assert.Equal("img", article.CoverImageUrl);
        Assert.Equal(10L, article.PublishedAt);
        Assert.Equal(20L, article.ModifiedAt);

        var note = new CommunityNote(DummyClient, (JsonObject)JsonNode.Parse("""{"rest_id":"n","data_v1":{"summary":{"text":"note"},"misleading_tags":["a"],"trustworthy_sources":true},"tweet_results":{"result":{"rest_id":"t"}}}""")!);
        Assert.Equal("note", note.Text);
        Assert.Equal("t", note.TweetId);
        Assert.True(note.TrustworthySources);
    }

    [Fact]
    public void TrendAndPlace()
    {
        var trend = new Trend(DummyClient, (JsonObject)JsonNode.Parse("""{"name":"#x","trend_metadata":{"meta_description":"12K posts","domain_context":"Trending"},"grouped_trends":[{"name":"y"}]}""")!);
        Assert.Equal("#x", trend.Name);
        Assert.Equal("12K posts", trend.TweetsCount);
        Assert.Equal(new[] { "y" }, trend.GroupedTrends);

        var place = new Place(DummyClient, (JsonObject)JsonNode.Parse("""{"id":"p","name":"Tokyo","full_name":"Tokyo, Japan","country":"Japan","country_code":"JP","url":"u","place_type":"city","bounding_box":{},"centroid":[139.7,35.7],"contained_within":[{"id":"jp","name":"Japan","full_name":"Japan","country":"Japan","country_code":"JP","url":"u","place_type":"country","bounding_box":{}}]}""")!);
        Assert.Equal("Tokyo", place.Name);
        Assert.Equal(new[] { 139.7, 35.7 }, place.Centroid);
        Assert.Single(place.ContainedWithin);
    }

    [Fact]
    public void SpaceModelsParse()
    {
        var space = new Space((JsonObject)JsonNode.Parse("""{"metadata":{"rest_id":"1DXGydznBYWKM","state":"Running","title":"hi","media_key":"28_1"},"participants":{"speakers":[{"user_results":{"result":{"rest_id":"7"}}}]},"host":{"user_results":{"result":{"rest_id":"9"}}},"sharings":{"items":[{"sharing_id":"s1"}]}}""")!);
        Assert.True(space.IsLive);
        Assert.Equal(new[] { "7" }, space.SpeakerIds);
        Assert.Equal("9", space.HostUserId);
        Assert.Equal(new[] { "s1" }, space.SharingIds);
        Assert.Equal("1DXGydznBYWKM", SpaceUtils.ExtractSpaceId("https://x.com/i/spaces/1DXGydznBYWKM/"));

        var stream = SpaceStream.FromResponse((JsonObject)JsonNode.Parse("""{"session_id":"s","chatToken":"c","source":{"location":"https://hls","noRedirectPlaybackUrl":"https://direct","stream_type":"HLS"}}""")!);
        Assert.Equal("https://direct", stream.HlsUrl);
        Assert.Equal("c", stream.ChatToken);

        var message = ChatMessage.FromPayload((JsonObject)JsonNode.Parse("""{"kind":1,"payload":"{\"body\":\"{\\\"body\\\":\\\"hello\\\",\\\"type\\\":1}\",\"sender\":{\"username\":\"u\"},\"timestamp\":5}"}""")!);
        Assert.Equal("hello", message.Body);
        Assert.Equal("1", message.Kind);
        Assert.Equal("u", message.Sender.Str("username"));
        Assert.Equal(5L, message.Timestamp);

        var control = ChatMessage.FromPayload((JsonObject)JsonNode.Parse("""{"kind":2,"payload":{"body":{"kind":"join","text":null}}}""")!);
        Assert.Null(control.Body);
        Assert.Equal("join", control.Kind);
    }

    [Fact]
    public void UiMetricsSolverRunsObfuscatedFunction()
    {
        var js = "function XxYyZ() {var s=document.createElement('div');document.getElementsByTagName('body')[0].appendChild(s);" +
                 "var t=document.createElement('span');s.appendChild(t);var qwert=1,asdfg=2,zxcvb=3;" +
                 "var n=s.children.length;var last=s.lastElementChild.tagName;s.removeChild(t);s.remove();" +
                 "return {rf:{'a':n,'b':(!qwert||asdfg)==zxcvb},s:last};}";
        var result = UiMetricsSolver.Solve(js);
        Assert.Equal("{\"rf\":{\"a\":1,\"b\":false},\"s\":\"span\"}", result);
        Assert.Throws<ArgumentException>(() => UiMetricsSolver.Solve("nothing here"));
    }

    [Fact]
    public void ParseUnlockHtml()
    {
        var html = "<html><body><form><input name=\"authenticity_token\" value=\"AUTH\"/><input name=\"assignment_token\" value=\"ASSIGN\"/>" +
                   "<input id=\"verification_string\"/><input type=\"submit\" value=\"Continue to X\"/></form>" +
                   "<iframe id=\"arkose_iframe\" src=\"https://iframe.arkoselabs.com/x?data=BLOB123\"></iframe></body></html>";
        var parsed = CaptchaSolver.ParseUnlockHtml(html);
        Assert.Equal("AUTH", parsed.AuthenticityToken);
        Assert.Equal("ASSIGN", parsed.AssignmentToken);
        Assert.True(parsed.NeedsUnlock);
        Assert.True(parsed.FinishButton);
        Assert.False(parsed.StartButton);
        Assert.Equal("BLOB123", parsed.Blob);
    }
}
