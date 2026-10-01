using System.Globalization;
using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

public partial class Client
{
    private async Task<JsonObject> SendDmCoreAsync(string conversationId, string text, string? mediaId, string? replyTo)
    {
        var response = await V11.DmNewAsync(conversationId, text, mediaId, replyTo).ConfigureAwait(false);
        return response.Object ?? new JsonObject();
    }

    private async Task<JsonObject> GetDmHistoryCoreAsync(string conversationId, string? maxId = null)
    {
        var response = await V11.DmConversationAsync(conversationId, maxId).ConfigureAwait(false);
        return response.Object ?? new JsonObject();
    }

    /// <summary>
    /// ユーザーに DM を送ります。
    /// </summary>
    /// <param name="userId">送信先ユーザーの ID。</param>
    /// <param name="text">本文。</param>
    /// <param name="mediaId">添付するメディア ID（<see cref="UploadMediaAsync(string, bool, double?, string?, string?, bool)"/> で取得）。</param>
    /// <param name="replyTo">返信先のメッセージ ID。</param>
    public async Task<Message> SendDmAsync(string userId, string text, string? mediaId = null, string? replyTo = null)
    {
        var response = await SendDmCoreAsync($"{userId}-{await UserIdAsync().ConfigureAwait(false)}", text, mediaId, replyTo).ConfigureAwait(false);

        if (response.FirstDict("message_data") is not JsonObject messageData)
            throw new TwitterException("X accepted the request but returned no message.");
        var users = response.Sub("users").Select(kv => kv.Value).OfType<JsonObject>().ToList();
        // The sender used to be read off dictionary order, which X does not
        // guarantee - a flipped pair made message.reply() answer yourself.
        var senderId = messageData.Str("sender_id") ?? users.ElementAtOrDefault(0).Str("id_str") ?? "";
        var recipientId = messageData.Str("recipient_id")
                          ?? (users.Count == 2 ? users[1].Str("id_str") : users.ElementAtOrDefault(0).Str("id_str"));
        return new Message(this, messageData, senderId, recipientId);
    }

    /// <summary>
    /// メッセージにリアクション（絵文字）を付けます。
    /// </summary>
    /// <param name="messageId">メッセージの ID。</param>
    /// <param name="conversationId">会話の ID（グループ ID、または <c>相手ID-自分ID</c>）。</param>
    /// <param name="emoji">付ける絵文字。</param>
    public async Task<HttpResponseMessage> AddReactionToMessageAsync(string messageId, string conversationId, string emoji)
        => (await Gql.UserDmReactionMutationAddMutationAsync(messageId, conversationId, emoji).ConfigureAwait(false)).Http;

    /// <summary>メッセージからリアクションを削除します。</summary>
    public async Task<HttpResponseMessage> RemoveReactionFromMessageAsync(string messageId, string conversationId, string emoji)
        => (await Gql.UserDmReactionMutationRemoveMutationAsync(messageId, conversationId, emoji).ConfigureAwait(false)).Http;

    /// <summary>DM を削除します。</summary>
    public async Task<HttpResponseMessage> DeleteDmAsync(string messageId)
        => (await Gql.DmMessageDeleteMutationAsync(messageId).ConfigureAwait(false)).Http;

    /// <summary>
    /// ユーザーとの DM 履歴を取得します。
    /// </summary>
    /// <param name="userId">相手のユーザー ID。</param>
    /// <param name="maxId">指定すると、この ID より古いメッセージを取得します。</param>
    public async Task<Result<Message>> GetDmHistoryAsync(string userId, string? maxId = null)
    {
        var response = await GetDmHistoryCoreAsync($"{userId}-{await UserIdAsync().ConfigureAwait(false)}", maxId).ConfigureAwait(false);

        var timeline = response.Sub("conversation_timeline");
        if (!timeline.ContainsKey("entries")) return Result<Message>.Empty();

        var messages = new List<Message>();
        foreach (var item in timeline.ArrOrEmpty("entries").Objects())
        {
            // A conversation timeline also carries non-message entries such as
            // `trust_conversation`, which have no `message` key at all.
            if (!item.ContainsKey("message")) continue;
            var messageInfo = item.Sub("message").Sub("message_data");
            messages.Add(new Message(this, messageInfo, messageInfo.Str("sender_id") ?? "", messageInfo.Str("recipient_id")));
        }

        if (messages.Count == 0) return Result<Message>.Empty();

        var lastId = messages[^1].Id;
        return new Result<Message>(messages, () => GetDmHistoryAsync(userId, lastId), lastId);
    }

    /// <summary>
    /// DM の受信箱（会話の一覧。中身ではありません）を取得します。
    /// </summary>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Conversation>> GetDmInboxAsync(string? cursor = null)
    {
        var response = cursor is null
            ? await V11.DmInboxAsync(null).ConfigureAwait(false)
            // inbox_initial_state only ever serves the first page - passing it
            // a cursor returns the identical conversations forever.
            : await V11.DmInboxTimelineAsync("trusted", cursor).ConfigureAwait(false);
        var state = response.Object.Obj("inbox_initial_state") ?? response.Object.Obj("user_events") ?? new JsonObject();

        var conversations = state.Sub("conversations");
        var myId = await UserIdAsync().ConfigureAwait(false);
        var results = conversations.Select(kv => kv.Value).OfType<JsonObject>()
            .Select(data => new Conversation(this, data, myId)).ToList();

        // X sorts the inbox by recency; conversations arrives as a mapping so
        // that order is not guaranteed to survive. `sort_timestamp` is the
        // recency X itself orders by.
        static long Recency(Conversation conversation)
        {
            foreach (var key in new[] { "sort_timestamp", "sort_event_id" })
            {
                var value = conversation.Data.Get(key).AsLong();
                if (value is not null) return value.Value;
            }
            return 0;
        }

        results = results.OrderByDescending(Recency).ToList();

        // X hands back a cursor on every inbox response, including the last
        // one. The end of the inbox is announced by the timelines instead.
        var timelines = state.Sub("inbox_timelines");
        var trusted = timelines.Sub("trusted");
        var atEnd = trusted.Count > 0 ? trusted.Str("status") == "AT_END" : timelines.Count == 0;

        // The next page is asked for by max_id, which is the oldest entry of this one.
        var nextCursor = atEnd ? null : (trusted.Str("min_entry_id") ?? state.Str("min_entry_id"));
        Func<Task<Result<Conversation>>>? fetchNext = HasCursor(nextCursor) ? () => GetDmInboxAsync(nextCursor) : null;
        return new Result<Conversation>(results, fetchNext, nextCursor);
    }

    /// <summary>
    /// 最初のメッセージを送ってグループ会話を作成します。X に「グループ作成」の呼び出しは無く、
    /// 複数の宛先に送った DM がグループになります。宛先が 1 人だと通常の 1 対 1 の会話になるので、2 人以上を渡してください。
    /// </summary>
    /// <param name="userIds">グループに入れるユーザーの ID。</param>
    /// <param name="text">最初のメッセージ。</param>
    /// <param name="mediaId">最初のメッセージに添付するメディア。</param>
    public async Task<Group> CreateGroupAsync(IList<string> userIds, string text, string? mediaId = null)
    {
        if (userIds.Count == 0) throw new ArgumentException("`userIds` must not be empty.", nameof(userIds));

        var response = await V11.DmNewGroupAsync(userIds, text, mediaId).ConfigureAwait(false);

        string? conversationId = null;
        foreach (var entry in response.Object.ArrOrEmpty("entries").Objects())
        {
            var message = entry.Obj("message");
            if (message is not null)
            {
                conversationId = message.Str("conversation_id");
                break;
            }
        }
        conversationId ??= response.Object.Sub("conversations").Select(kv => kv.Key).FirstOrDefault();
        if (conversationId is null) throw new TwitterException("X did not return a conversation for the new group.");

        return await GetGroupAsync(conversationId).ConfigureAwait(false);
    }

    /// <summary>
    /// ログイン中のアカウントの受信箱から会話を削除します。他の参加者側には残ります（X 自体の挙動と同じ）。
    /// </summary>
    public async Task<HttpResponseMessage> DeleteDmConversationAsync(string conversationId)
        => (await V11.DeleteConversationAsync(conversationId).ConfigureAwait(false)).Http;

    /// <summary>
    /// グループにメッセージを送ります。
    /// </summary>
    /// <param name="groupId">グループの ID。</param>
    /// <param name="text">本文。</param>
    /// <param name="mediaId">添付するメディア ID。</param>
    /// <param name="replyTo">返信先のメッセージ ID。</param>
    public async Task<GroupMessage> SendDmToGroupAsync(string groupId, string text, string? mediaId = null, string? replyTo = null)
    {
        var response = await SendDmCoreAsync(groupId, text, mediaId, replyTo).ConfigureAwait(false);

        if (response.FirstDict("message_data") is not JsonObject messageData)
            throw new TwitterException("X accepted the request but returned no message.");
        var users = response.Sub("users").Select(kv => kv.Value).OfType<JsonObject>().ToList();
        var senderId = messageData.Str("sender_id") ?? users.ElementAtOrDefault(0).Str("id_str") ?? "";
        return new GroupMessage(this, messageData, senderId, groupId);
    }

    /// <summary>
    /// グループの DM 履歴を取得します。
    /// </summary>
    /// <param name="groupId">グループの ID。</param>
    /// <param name="maxId">指定すると、この ID より古いメッセージを取得します。</param>
    public async Task<Result<GroupMessage>> GetGroupDmHistoryAsync(string groupId, string? maxId = null)
    {
        var response = await GetDmHistoryCoreAsync(groupId, maxId).ConfigureAwait(false);
        var timeline = response.Sub("conversation_timeline");
        if (!timeline.ContainsKey("entries")) return Result<GroupMessage>.Empty();

        var messages = new List<GroupMessage>();
        foreach (var item in timeline.ArrOrEmpty("entries").Objects())
        {
            if (!item.ContainsKey("message")) continue;
            var messageInfo = item.Sub("message").Sub("message_data");
            messages.Add(new GroupMessage(this, messageInfo, messageInfo.Str("sender_id") ?? "", groupId));
        }

        if (messages.Count == 0) return Result<GroupMessage>.Empty();

        var lastId = messages[^1].Id;
        return new Result<GroupMessage>(messages, () => GetGroupDmHistoryAsync(groupId, lastId), lastId);
    }

    /// <summary>ID を指定してグループを取得します。</summary>
    public async Task<Group> GetGroupAsync(string groupId)
    {
        var response = await GetDmHistoryCoreAsync(groupId).ConfigureAwait(false);
        return new Group(this, groupId, response);
    }

    /// <summary>グループにメンバーを追加します。</summary>
    public async Task<HttpResponseMessage> AddMembersToGroupAsync(string groupId, IList<string> userIds)
        => (await Gql.AddParticipantsMutationAsync(groupId, userIds).ConfigureAwait(false)).Http;

    /// <summary>グループ名を変更します。</summary>
    public async Task<HttpResponseMessage> ChangeGroupNameAsync(string groupId, string name)
        => (await V11.ConversationUpdateNameAsync(groupId, name).ConfigureAwait(false)).Http;

    /// <summary>
    /// 通知を取得します。
    /// </summary>
    /// <param name="type">'All'（すべて）、'Verified'（認証済みユーザー関連）、'Mentions'（メンション）のいずれか。</param>
    /// <param name="count">取得する件数。</param>
    /// <param name="cursor">ページ送り用カーソル。</param>
    public async Task<Result<Notification>> GetNotificationsAsync(string type, int count = 40, string? cursor = null)
    {
        type = Capitalize(type);
        Func<int, string?, Task<ApiResponse>> f = type switch
        {
            "All" => V11.NotificationsAllAsync,
            "Verified" => V11.NotificationsVerifiedAsync,
            "Mentions" => V11.NotificationsMentionsAsync,
            _ => throw new ArgumentException($"Invalid type '{type}'; expected one of All, Verified, Mentions.", nameof(type)),
        };
        var response = await f(count, cursor).ConfigureAwait(false);

        var globalObjects = response.Object.Sub("globalObjects");
        var users = new Dictionary<string, User>();
        foreach (var kv in globalObjects.Sub("users"))
            if (kv.Value is JsonObject data) users[kv.Key] = new User(this, Utils.BuildUserData(data));
        var tweets = new Dictionary<string, Tweet>();
        foreach (var kv in globalObjects.Sub("tweets"))
        {
            if (kv.Value is not JsonObject tweetData) continue;
            var userId = tweetData.Str("user_id_str") ?? "";
            users.TryGetValue(userId, out var user);
            tweets[kv.Key] = new Tweet(this, Utils.BuildTweetData(tweetData), user);
        }

        var notifications = new List<Notification>();
        var rawNotifications = globalObjects.Obj("notifications");
        if (rawNotifications is not null)
        {
            foreach (var kv in rawNotifications)
            {
                if (kv.Value is not JsonObject notification) continue;
                var userActions = notification.Sub("template").Sub("aggregateUserActionsV1");
                var targetObjects = userActions.ArrOrEmpty("targetObjects");
                Tweet? tweet = null;
                if (targetObjects.Count > 0 && targetObjects[0].Has("tweet"))
                {
                    var tweetId = targetObjects[0].Sub("tweet").Str("id") ?? "";
                    tweets.TryGetValue(tweetId, out tweet);
                }
                var fromUsers = userActions.ArrOrEmpty("fromUsers");
                User? user = null;
                if (fromUsers.Count > 0 && fromUsers[0].Has("user"))
                {
                    var userId = fromUsers[0].Sub("user").Str("id") ?? "";
                    users.TryGetValue(userId, out user);
                }
                notifications.Add(new Notification(this, notification, tweet, user));
            }
        }
        else if (type == "Mentions")
        {
            // The Mentions timeline omits the `notifications` key entirely: the
            // mention/reply tweets are referenced by `tweet-*` timeline entries.
            var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
            var seenTweetIds = new HashSet<string>();
            foreach (var entry in entries.Objects())
            {
                var entryId = entry.EntryId();
                if (!entryId.StartsWith("tweet-", StringComparison.Ordinal)) continue;
                var tweetId = entryId.Substring("tweet-".Length);
                if (seenTweetIds.Contains(tweetId)) continue;
                if (!tweets.TryGetValue(tweetId, out var tweet)) continue;
                seenTweetIds.Add(tweetId);
                // Tweet ids are snowflakes; recover the creation time from the id.
                var timestampMs = Utils.SnowflakeToTimestampMs(tweet.Id);
                var data = new JsonObject
                {
                    ["id"] = tweet.Id,
                    ["timestampMs"] = timestampMs.ToString(CultureInfo.InvariantCulture),
                    ["icon"] = new JsonObject(),
                    ["message"] = new JsonObject { ["text"] = "" },
                };
                notifications.Add(new Notification(this, data, tweet, tweet.User));
            }
        }

        string? nextCursor = null;
        var allEntries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
        var cursorBottom = allEntries.Objects().FirstOrDefault(e => e.EntryId().StartsWith("cursor-bottom", StringComparison.Ordinal));
        if (cursorBottom is not null) nextCursor = cursorBottom.FirstDict("value").AsStr();

        var type1 = type;
        return Page(notifications, count, nextCursor, () => GetNotificationsAsync(type1, count, nextCursor));
    }
}
