namespace Twikit.Examples;

/// <summary>Python 版 twikit の examples/download_tweet_media.py に相当します。</summary>
internal static class DownloadTweetMedia
{
    public static async Task RunAsync(string[] args)
    {
        using var client = await Common.CreateClientAsync();

        var tweetId = args.Length > 0 ? args[0] : "...";
        var tweet = await client.GetTweetByIdAsync(tweetId);

        // 添付メディアを種類ごとにダウンロードする
        var i = 0;
        foreach (var media in tweet.Media)
        {
            switch (media)
            {
                case Photo photo:
                    await photo.DownloadAsync($"media_{i}.jpg");
                    break;
                case AnimatedGif gif:
                    await gif.Streams[^1].DownloadAsync($"media_{i}.mp4");
                    break;
                case Video video:
                    await video.Streams[^1].DownloadAsync($"media_{i}.mp4");
                    break;
            }
            i++;
        }
    }
}
