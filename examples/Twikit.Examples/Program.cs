// twikit-dotnet のサンプル集。各サンプルは Python 版 twikit の examples/*.py に対応しています。
//
//   dotnet run --project examples/Twikit.Examples -- <example> [args]
//
// example:
//   basic            検索・ユーザー取得・ツイート取得などの基本操作（example.py 相当）
//   download-media   ツイートの添付メディアをダウンロード（download_tweet_media.py 相当）
//   listen           新しいツイートを監視（listen_for_new_tweets.py 相当）
//   dm-auto-reply    DM に自動返信（dm_auto_reply.py 相当）
//   delete-all       自分のツイートをすべて削除（delete_all_tweets.py 相当）
//   spaces           X スペースのデモ（spaces.py 相当）
//   guest            ゲストクライアント（guest.py 相当。現在 X 側で閉じられています）
//
// 認証はブラウザから取り出した Cookie（auth_token と ct0）を環境変数 AUTH_TOKEN / CT0 に
// 入れるか、cookies.json（Client.SaveCookies の出力）を用意してください。

using Twikit.Examples;

var name = args.Length > 0 ? args[0] : "basic";
var rest = args.Skip(1).ToArray();
switch (name)
{
    case "basic": await BasicExample.RunAsync(rest); break;
    case "download-media": await DownloadTweetMedia.RunAsync(rest); break;
    case "listen": await ListenForNewTweets.RunAsync(rest); break;
    case "dm-auto-reply": await DmAutoReply.RunAsync(rest); break;
    case "delete-all": await DeleteAllTweets.RunAsync(rest); break;
    case "spaces": await SpacesExample.RunAsync(rest); break;
    case "guest": await GuestExample.RunAsync(rest); break;
    default:
        Console.Error.WriteLine($"unknown example: {name}");
        return 1;
}
return 0;
