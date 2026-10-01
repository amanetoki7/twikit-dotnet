using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// 通知。
/// </summary>
public sealed class Notification : IEquatable<Notification>
{
    /// <summary>通知の一意な ID。</summary>
    public string Id { get; }
    /// <summary>通知のタイムスタンプ（ミリ秒）。</summary>
    public long TimestampMs { get; }
    /// <summary>アイコンの情報。</summary>
    public JsonObject? Icon { get; }
    /// <summary>通知のメッセージ。</summary>
    public string Message { get; }
    /// <summary>関連するツイート。</summary>
    public Tweet? Tweet { get; }
    /// <summary>通知を発生させたユーザー。</summary>
    public User? FromUser { get; }

    public Notification(Client client, JsonObject data, Tweet? tweet, User? fromUser)
    {
        _ = client;
        Tweet = tweet;
        FromUser = fromUser;
        Id = data.Str("id") ?? "";
        TimestampMs = data.Long("timestampMs") ?? 0;
        Icon = data.Obj("icon");
        Message = data.Sub("message").Str("text") ?? "";
    }

    public override string ToString() => $"<Notification id=\"{Id}\">";
    public bool Equals(Notification? other) => other is not null && Id == other.Id;
    public override bool Equals(object? obj) => Equals(obj as Notification);
    public override int GetHashCode() => Id.GetHashCode();
}
