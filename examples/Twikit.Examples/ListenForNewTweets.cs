namespace Twikit.Examples;

/// <summary>Python 版 twikit の examples/listen_for_new_tweets.py に相当します。</summary>
internal static class ListenForNewTweets
{
    private const string UserId = "44196397";                     // 監視するユーザーの ID
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5); // 確認する間隔

    private static void Callback(Tweet tweet) => Console.WriteLine($"新しいツイートが投稿されました : {tweet.Text}");

    public static async Task RunAsync(string[] args)
    {
        using var client = await Common.CreateClientAsync();
        var userId = args.Length > 0 ? args[0] : UserId;

        async Task<Tweet> GetLatestTweetAsync() => (await client.GetUserTweetsAsync(userId, "Replies"))[0];

        var before = await GetLatestTweetAsync();

        while (true)
        {
            await Task.Delay(CheckInterval);
            var latest = await GetLatestTweetAsync();
            if (!before.Equals(latest) && before.CreatedAtDatetime < latest.CreatedAtDatetime)
                Callback(latest);
            before = latest;
        }
    }
}
