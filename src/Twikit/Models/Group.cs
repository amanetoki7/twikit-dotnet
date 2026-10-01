using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// グループ DM。
/// </summary>
public sealed class Group
{
    private readonly Client _client;

    /// <summary>グループの ID。</summary>
    public string Id { get; }
    /// <summary>グループ名（未設定なら null）。</summary>
    public string? Name { get; private set; }
    /// <summary>
    /// 現在の参加者。X は作成時に要求した全員を黙って入れないことがあるので、
    /// 全員入ったと仮定せずここを確認してください。
    /// </summary>
    public List<User> Members { get; private set; } = new();

    public Group(Client client, string groupId, JsonObject data)
    {
        _client = client;
        Id = groupId;
        Load(data);
    }

    private void Load(JsonObject data)
    {
        var conversationTimeline = data.Sub("conversation_timeline");
        var conversations = conversationTimeline.Sub("conversations");
        // A group only gets a `name` once somebody sets one, and the id is
        // missing from `conversations` entirely until the timeline settles.
        Name = conversations.Sub(Id).Str("name");

        // `users` holds everyone who appeared anywhere in the fetched slice of
        // the timeline. The actual roster is `participants` on the conversation.
        var users = conversationTimeline.Sub("users");
        var participants = conversations.Sub(Id).Arr("participants");
        var members = new List<JsonObject>();
        if (participants is { Count: > 0 })
        {
            foreach (var p in participants.Objects())
            {
                var userId = p.Str("user_id");
                if (userId is not null && users.Get(userId) is JsonObject u) members.Add(u);
            }
        }
        else
        {
            members.AddRange(users.Select(kv => kv.Value).OfType<JsonObject>());
        }
        Members = members.Select(m => new User(_client, Utils.BuildUserData(m))).ToList();
    }

    /// <summary>グループの DM 履歴を取得します。</summary>
    public Task<Result<GroupMessage>> GetHistoryAsync(string? maxId = null) => _client.GetGroupDmHistoryAsync(Id, maxId);

    /// <summary>メンバーを追加します。</summary>
    public Task<HttpResponseMessage> AddMembersAsync(IList<string> userIds) => _client.AddMembersToGroupAsync(Id, userIds);

    /// <summary>グループ名を変更します。</summary>
    public Task<HttpResponseMessage> ChangeNameAsync(string name) => _client.ChangeGroupNameAsync(Id, name);

    /// <summary>グループにメッセージを送ります。</summary>
    public Task<GroupMessage> SendMessageAsync(string text, string? mediaId = null, string? replyTo = null)
        => _client.SendDmToGroupAsync(Id, text, mediaId, replyTo);

    /// <summary>最新の情報で更新します。</summary>
    public async Task UpdateAsync()
    {
        var fresh = await _client.GetGroupAsync(Id).ConfigureAwait(false);
        Name = fresh.Name;
        Members = fresh.Members;
    }

    public override string ToString() => $"<Group id=\"{Id}\">";
}

/// <summary>
/// グループ DM のメッセージ。
/// </summary>
public sealed class GroupMessage : Message
{
    /// <summary>グループの ID。</summary>
    public string GroupId { get; }

    public GroupMessage(Client client, JsonObject data, string senderId, string groupId)
        : base(client, data, senderId, null)
    {
        GroupId = groupId;
    }

    /// <summary>メッセージが送られたグループを取得します。</summary>
    public Task<Group> GetGroupAsync() => ClientRef.GetGroupAsync(GroupId);

    /// <summary>このメッセージに返信します。</summary>
    public override async Task<Message> ReplyAsync(string text, string? mediaId = null)
        => await ClientRef.SendDmToGroupAsync(GroupId, text, mediaId, Id).ConfigureAwait(false);

    /// <summary>リアクションを追加します。</summary>
    public override Task<HttpResponseMessage> AddReactionAsync(string emoji)
        => ClientRef.AddReactionToMessageAsync(Id, GroupId, emoji);

    /// <summary>リアクションを削除します。</summary>
    public override Task<HttpResponseMessage> RemoveReactionAsync(string emoji)
        => ClientRef.RemoveReactionFromMessageAsync(Id, GroupId, emoji);

    public override string ToString() => $"<GroupMessage id=\"{Id}\">";
}
