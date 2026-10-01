using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit;

/// <summary>
/// 受信箱内の DM の会話。
/// </summary>
public sealed class Conversation : IEquatable<Conversation>
{
    private readonly Client _client;

    /// <summary>生の JSON。</summary>
    public JsonObject Data { get; }

    /// <summary>
    /// 会話 ID。1 対 1 の会話では 2 人の ID をソートしてダッシュで繋いだもの（<c>相手-自分</c> ではないので
    /// 位置から相手を読み取れません。<see cref="PartnerId"/> を使ってください）。グループでは単なる ID。
    /// </summary>
    public string Id { get; }
    /// <summary>'ONE_TO_ONE' または 'GROUP_DM'。判定には <see cref="IsGroup"/> を使ってください。</summary>
    public string? Type { get; }
    /// <summary>グループ名（1 対 1 の会話では null）。</summary>
    public string? Name { get; }
    /// <summary>参加者全員の ID。自分自身との会話では自分が 1 回だけ含まれます。</summary>
    public List<string> ParticipantIds { get; }
    /// <summary>1 対 1 の会話の相手（自分自身との会話では自分の ID）。グループでは null。</summary>
    public string? PartnerId { get; }
    /// <summary>既読にした最後のイベントの ID。</summary>
    public string? LastReadEventId { get; }
    /// <summary>メッセージリクエストではなく信頼済みの受信箱にあるか。</summary>
    public bool Trusted { get; }
    /// <summary>ミュートされているか。</summary>
    public bool Muted { get; }
    /// <summary>グループの会話か。</summary>
    public bool IsGroup { get; }

    public Conversation(Client client, JsonObject data, string? myId = null)
    {
        _client = client;
        Data = data;
        Id = data.Str("conversation_id") ?? "";
        Type = data.Str("type");
        Name = data.Str("name");
        ParticipantIds = data.ArrOrEmpty("participants").Objects().Select(p => p.Str("user_id") ?? "").ToList();
        LastReadEventId = data.Str("last_read_event_id");
        Trusted = data.BoolOr("trusted", false);
        Muted = data.BoolOr("muted", false);
        // X labels group conversations GROUP_DM, not GROUP - matching the
        // bare word silently treats every group as one-to-one.
        IsGroup = (Type ?? "").StartsWith("GROUP", StringComparison.Ordinal);
        if (myId is not null && !IsGroup)
        {
            // A conversation with yourself lists exactly one participant - you -
            // so there is no "other" id to find.
            PartnerId = ParticipantIds.FirstOrDefault(p => p != myId) ?? myId;
        }
    }

    /// <summary>この会話のメッセージ履歴を取得します。</summary>
    public async Task<IReadOnlyList<Message>> GetHistoryAsync(string? maxId = null)
    {
        if (IsGroup)
            return await _client.GetGroupDmHistoryAsync(Id, maxId).ConfigureAwait(false);
        return await _client.GetDmHistoryAsync(PartnerId ?? "", maxId).ConfigureAwait(false);
    }

    public override string ToString() => $"<Conversation id=\"{Id}\">";
    public bool Equals(Conversation? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Conversation);
    public override int GetHashCode() => Id.GetHashCode();
}

/// <summary>
/// ダイレクトメッセージ。
/// </summary>
public class Message : IEquatable<Message>
{
    protected readonly Client ClientRef;

    /// <summary>送信者の ID。</summary>
    public string SenderId { get; }
    /// <summary>受信者の ID（グループでは null）。</summary>
    public string? RecipientId { get; }
    /// <summary>メッセージの ID。</summary>
    public string Id { get; }
    /// <summary>タイムスタンプ。</summary>
    public string? Time { get; }
    /// <summary>本文。</summary>
    public string Text { get; }
    /// <summary>添付の情報。</summary>
    public JsonObject? Attachment { get; }
    /// <summary>返信元のメッセージ（新規メッセージでは null）。</summary>
    public JsonObject? ReplyData { get; }

    public Message(Client client, JsonObject data, string senderId, string? recipientId)
    {
        ClientRef = client;
        SenderId = senderId;
        RecipientId = recipientId;
        Id = data.Str("id") ?? "";
        Time = data.Str("time");
        Text = data.Str("text") ?? "";
        Attachment = data.Obj("attachment");
        // X carries the message being replied to inline as `reply_data`.
        ReplyData = data.Obj("reply_data");
    }

    /// <summary>
    /// 添付メディアをダウンロードします。X が <see cref="Attachment"/> に入れる URL は ton.twitter.com を
    /// 指しており公開 CDN ではないため、ログイン中のクライアントを通して取得します。
    /// </summary>
    public async Task<byte[]> DownloadAttachmentAsync()
    {
        var url = AttachmentUrl ?? throw new InvalidOperationException("This message has no attachment.");
        // Two things are needed: the bearer headers (without them the redirect
        // target answers 404) and following the redirect at all.
        var headers = ClientRef.BaseHeaders;
        headers.Remove("content-type");
        var response = await ClientRef.SendAsync(HttpMethod.Get, url, new RequestOptions
        {
            Headers = headers,
            FollowRedirects = true,
        }).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    /// <summary>添付メディアの直接 URL（無ければ null）。</summary>
    public string? AttachmentUrl
    {
        get
        {
            if (Attachment is null) return null;
            foreach (var kv in Attachment)
            {
                if (kv.Value is JsonObject value)
                {
                    var url = value.Str("media_url_https") ?? value.Str("media_url");
                    if (!string.IsNullOrEmpty(url)) return url;
                }
            }
            return null;
        }
    }

    /// <summary>返信元のメッセージ ID。</summary>
    public string? RepliedToId => ReplyData.Str("id");

    /// <summary>返信元のメッセージ本文。</summary>
    public string? RepliedToText => ReplyData.Str("text");

    /// <summary>このメッセージに返信します。</summary>
    public virtual async Task<Message> ReplyAsync(string text, string? mediaId = null)
    {
        var userId = await ClientRef.UserIdAsync().ConfigureAwait(false);
        var sendTo = userId == SenderId ? RecipientId : SenderId;
        return await ClientRef.SendDmAsync(sendTo ?? "", text, mediaId, Id).ConfigureAwait(false);
    }

    /// <summary>リアクションを追加します。</summary>
    public virtual async Task<HttpResponseMessage> AddReactionAsync(string emoji)
    {
        var userId = await ClientRef.UserIdAsync().ConfigureAwait(false);
        var partnerId = userId == SenderId ? RecipientId : SenderId;
        return await ClientRef.AddReactionToMessageAsync(Id, $"{partnerId}-{userId}", emoji).ConfigureAwait(false);
    }

    /// <summary>リアクションを削除します。</summary>
    public virtual async Task<HttpResponseMessage> RemoveReactionAsync(string emoji)
    {
        var userId = await ClientRef.UserIdAsync().ConfigureAwait(false);
        var partnerId = userId == SenderId ? RecipientId : SenderId;
        return await ClientRef.RemoveReactionFromMessageAsync(Id, $"{partnerId}-{userId}", emoji).ConfigureAwait(false);
    }

    /// <summary>メッセージを削除します。</summary>
    public Task<HttpResponseMessage> DeleteAsync() => ClientRef.DeleteDmAsync(Id);

    public override string ToString() => $"<Message id=\"{Id}\">";
    public bool Equals(Message? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Message);
    public override int GetHashCode() => Id.GetHashCode();
}
