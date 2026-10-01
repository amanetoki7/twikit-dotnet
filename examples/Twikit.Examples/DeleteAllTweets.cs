using System.Diagnostics;

namespace Twikit.Examples;

/// <summary>Python 版 twikit の examples/delete_all_tweets.py に相当します。</summary>
internal static class DeleteAllTweets
{
    public static async Task RunAsync(string[] args)
    {
        var started = Stopwatch.StartNew();
        using var client = await Common.CreateClientAsync();
        var me = await client.UserAsync();

        // すべての投稿を取得
        var all = new List<Tweet>();
        var tweets = await me.GetTweetsAsync("Replies");
        all.AddRange(tweets);
        while (tweets.Count != 0)
        {
            tweets = await tweets.NextAsync();
            all.AddRange(tweets);
        }

        await Task.WhenAll(all.Select(t => t.DeleteAsync()));

        Console.WriteLine($"{all.Count} 件のツイートを削除しました");
        Console.WriteLine($"所要時間: {started.Elapsed.TotalSeconds:F1} 秒");
    }
}
