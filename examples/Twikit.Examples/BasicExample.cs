namespace Twikit.Examples;

/// <summary>Python 版 twikit の examples/example.py に相当します。</summary>
internal static class BasicExample
{
    public static async Task RunAsync(string[] args)
    {
        using var client = await Common.CreateClientAsync();

        ///////////////////////////////////////////

        // 最新のツイートを検索
        var tweets = await client.SearchTweetAsync("query", "Latest");
        foreach (var tweet in tweets)
            Console.WriteLine($"{tweet} {tweet.User?.Name}: {tweet.Text}");
        // 続きのツイートを取得
        var moreTweets = await tweets.NextAsync();
        Console.WriteLine($"more: {moreTweets.Count}");

        ///////////////////////////////////////////

        // ユーザーを検索
        var users = await client.SearchUserAsync("query");
        foreach (var user in users)
            Console.WriteLine(user);
        // 続きのユーザーを取得
        var moreUsers = await users.NextAsync();
        Console.WriteLine($"more: {moreUsers.Count}");

        ///////////////////////////////////////////

        // スクリーンネームからユーザーを取得
        const string userScreenName = "example_user";
        var target = await client.GetUserByScreenNameAsync(userScreenName);

        // ユーザーの属性にアクセス
        Console.WriteLine($"id: {target.Id}");
        Console.WriteLine($"name: {target.Name}");
        Console.WriteLine($"followers: {target.FollowersCount}");
        Console.WriteLine($"tweets count: {target.StatusesCount}");

        // フォロー
        await target.FollowAsync();
        // フォロー解除
        await target.UnfollowAsync();

        // ユーザーのツイートを取得
        var userTweets = await target.GetTweetsAsync("Tweets");
        foreach (var tweet in userTweets)
            Console.WriteLine(tweet);
        // 続きのツイートを取得
        var moreUserTweets = await userTweets.NextAsync();
        Console.WriteLine($"more: {moreUserTweets.Count}");

        ///////////////////////////////////////////

        // ユーザーに DM を送る
        var mediaId = await client.UploadMediaAsync("./image.png");
        await target.SendDmAsync("dm text", mediaId);

        // DM の履歴を取得
        var messages = await target.GetDmHistoryAsync();
        foreach (var message in messages)
            Console.WriteLine(message);
        // 続きのメッセージを取得
        var moreMessages = await messages.NextAsync();
        Console.WriteLine($"more: {moreMessages.Count}");

        ///////////////////////////////////////////

        // ID からツイートを取得
        const string tweetId = "0000000000";
        var fetched = await client.GetTweetByIdAsync(tweetId);

        // ツイートの属性にアクセス
        Console.WriteLine($"id: {fetched.Id}");
        Console.WriteLine($"text: {fetched.Text}");
        Console.WriteLine($"favorite count: {fetched.FavoriteCount}");
        Console.WriteLine($"media: {string.Join(", ", fetched.Media)}");

        // いいね
        await fetched.FavoriteAsync();
        // いいね解除
        await fetched.UnfavoriteAsync();
        // リツイート
        await fetched.RetweetAsync();
        // リツイート解除
        await fetched.DeleteRetweetAsync();

        // ツイートに返信
        await fetched.ReplyAsync("tweet content");

        ///////////////////////////////////////////

        // メディア付きツイートを作成
        var mediaIds = new List<string>
        {
            await client.UploadMediaAsync("./media1.png"),
            await client.UploadMediaAsync("./media2.png"),
            await client.UploadMediaAsync("./media3.png"),
        };
        await client.CreateTweetAsync("tweet text", mediaIds);

        // 投票付きツイートを作成
        var pollUri = await client.CreatePollAsync(new[] { "Option 1", "Option 2", "Option 3" }, 60);
        await client.CreateTweetAsync("tweet text", pollUri: pollUri);

        ///////////////////////////////////////////

        // ニュースのトレンドを取得
        var trends = await client.GetTrendsAsync("news");
        foreach (var trend in trends)
            Console.WriteLine(trend);
    }
}
