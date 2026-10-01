using Twikit.Guest;

namespace Twikit.Examples;

/// <summary>
/// Python 版 twikit の examples/guest.py に相当します。
/// 注意: 2026 年現在、X はゲストアクセスを閉じています。未ログインのリクエストには webpack マニフェストを
/// 含まないページの殻しか返ってこないため、x-client-transaction-id のハンドシェイクが完了できず、
/// ActivateAsync() は InvalidSessionException を送出します。これはライブラリ側では直せません。
/// </summary>
internal static class GuestExample
{
    public static async Task RunAsync(string[] args)
    {
        using var client = new GuestClient();

        // ゲストトークンを生成してクライアントを有効化する
        await client.ActivateAsync();

        // スクリーンネームからユーザーを取得
        var user = await client.GetUserByScreenNameAsync("elonmusk");
        Console.WriteLine(user);
        // ID からユーザーを取得
        user = await client.GetUserByIdAsync("44196397");
        Console.WriteLine(user);

        var userTweets = await client.GetUserTweetsAsync("44196397");
        Console.WriteLine(string.Join("\n", userTweets));

        var tweet = await client.GetTweetByIdAsync("1519480761749016577");
        Console.WriteLine(tweet);
    }
}
