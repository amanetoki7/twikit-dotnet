using Twikit.Streaming;

namespace Twikit.Examples;

/// <summary>Python 版 twikit の examples/dm_auto_reply.py に相当します。</summary>
internal static class DmAutoReply
{
    public static async Task RunAsync(string[] args)
    {
        using var client = await Common.CreateClientAsync();

        var userId = args.Length > 0 ? args[0] : "1752362966203469824"; // DM を監視する相手のユーザー ID
        const string replyMessage = "Hello";

        var myId = await client.UserIdAsync();
        var topics = new HashSet<string> { Topic.DmUpdate($"{myId}-{userId}") };
        var session = await client.GetStreamingSessionAsync(topics);

        await foreach (var (topic, payload) in session)
        {
            if (payload.DmUpdate is { } update)
            {
                // 自分が送ったメッセージには反応しない
                if (update.UserId == myId) continue;
                await client.SendDmAsync(update.UserId, replyMessage);
            }
        }
    }
}
