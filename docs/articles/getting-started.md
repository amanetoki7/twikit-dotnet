# はじめに

twikit の C# 版は、Python 版と同じく **API キー不要**で Twitter / X の内部 API（Web 版が使っている
GraphQL と v1.1 のエンドポイント）を呼び出します。ブラウザでログインした状態の Cookie を使って認証します。

## 動作環境

- .NET 8 以降
- 依存パッケージ: [AngleSharp](https://anglesharp.github.io/)（HTML の解析）、[Jint](https://github.com/sebastienros/jint)（ui_metrics の JavaScript 評価）

## インストール

NuGet には公開していません。リポジトリを clone して、プロジェクト参照で使ってください。

```bash
git clone https://github.com/amanetoki7/twikit-dotnet.git
```

```xml
<ItemGroup>
  <ProjectReference Include="path/to/twikit-dotnet/src/Twikit/Twikit.csproj" />
</ItemGroup>
```

パッケージとして配布したい場合は `dotnet pack src/Twikit/Twikit.csproj -c Release` で `.nupkg` を作れます。

## Cookie でログインする

> [!IMPORTANT]
> **パスワードではなく Cookie でログインしてください。**
> X は `Client.LoginAsync()` が使うオンボーディングフローを廃止しており、`code 366, "flow name LoginFlow is currently not accessible"` を返します。
> パスワードログインはライブラリ側では直せません（`LoginRetiredException` が送出されます）。

1. ブラウザで x.com にログインします。
2. 開発者ツール（Application / Storage → Cookies）から `auth_token` と `ct0` の値をコピーします。この 2 つだけで十分です。

```csharp
using Twikit;

var client = new Client("ja");
client.SetCookies(new Dictionary<string, string>
{
    ["auth_token"] = "...",
    ["ct0"] = "...",
});
// または: client.LoadCookies("cookies.json");

if (!await client.IsLoggedInAsync())
    throw new Exception("Cookie が無効です。ブラウザから取り直してください。");

// 次回以降のために保存しておく
client.SaveCookies("cookies.json");
```

`SetCookies` には、ブラウザの拡張機能や Playwright が書き出す形式（`name` と `value` を持つオブジェクトの配列）も
`JsonNode` として渡せます。

## 基本的な使い方

すべてのメソッドは非同期（`Task` / `Task<T>`）で、名前は `...Async` で終わります。

```csharp
// キーワードで最新のツイートを検索する
var tweets = await client.SearchTweetAsync("python", "Latest");
foreach (var tweet in tweets)
    Console.WriteLine($"{tweet.User?.Name}: {tweet.Text} ({tweet.CreatedAt})");

// 続きを取得する
var more = await tweets.NextAsync();

// ユーザーを取得してツイートを読む
var user = await client.GetUserByScreenNameAsync("example_user");
var userTweets = await user.GetTweetsAsync("Tweets");

// メディア付きツイートを投稿する
var mediaIds = new List<string>
{
    await client.UploadMediaAsync("media1.jpg"),
    await client.UploadMediaAsync("media2.jpg"),
};
await client.CreateTweetAsync("Example Tweet", mediaIds);

// DM を送る
await client.SendDmAsync("123456789", "Hello");

// トレンド
var trends = await client.GetTrendsAsync("trending");
```

### ハイライト（Premium）

プロフィールの「ハイライト」タブは Premium 機能です。X はタイムラインやツイート取得にハイライト済みかどうかを
含めないため、`Tweet.Highlighted` は `GetUserHighlightsTweetsAsync()` で取得したツイートか、
`IsHighlightedAsync()` / `HighlightAsync()` / `UnhighlightAsync()` を呼んだあと以外は null（不明）です。

```csharp
var tweet = await client.GetTweetByIdAsync("2103725182292820029");
Console.WriteLine(tweet.Highlighted);                 // null: タイムライン系の応答にはフラグが無い
Console.WriteLine(await tweet.IsHighlightedAsync());  // X に問い合わせる。tweet.Highlighted も更新される
await tweet.HighlightAsync();                         // 自分のツイートのみ。他人のものは TwitterException
await tweet.UnhighlightAsync();

var highlights = await client.GetUserHighlightsTweetsAsync(userId);   // 全件 Highlighted == true
```

### ピン留め

プロフィールのピンは 1 件だけで、別のツイートをピン留めすると置き換わります。ピン留めできるのは自分のツイートだけで、
他人のツイートは `NotFoundException` になります。ピン留めしていないツイートの解除は何もせず成功します。
レート制限はヘッダーの値ではなく pin / unpin それぞれ 100 回なので、[レート制限](ratelimits.md) の脚注も参照してください。

```csharp
await client.PinTweetAsync("2103725182292820029");    // v1.1 account/pin_tweet.json（フォームの `id`）
await client.UnpinTweetAsync("2103725182292820029");

var tweet = await client.GetTweetByIdAsync("2103725182292820029");
await tweet.PinAsync();
await tweet.UnpinAsync();
```

### クリエイター収益化の参加条件（Premium）

クリエイタースタジオの「オリジナルコンテンツ報酬」「サブスクリプション」ページにある参加条件の一覧と、
ログイン中のアカウントがどこまで満たしているかを取得します。X はこのページを GraphQL ではなく
server-driven UI（`/i/jfapi/...`、text/plain の生バイナリ）で返すので、このライブラリがデコードして
[`CreatorEligibility`](../api/Twikit.CreatorEligibility.yml) に組み立てます。

```csharp
var rewards = await client.GetOriginalContentRewardsEligibilityAsync();
Console.WriteLine(rewards.Summary);        // 「…登録するには、次の条件を満たす必要があります:」
foreach (var r in rewards.Requirements)
    Console.WriteLine($"{(r.IsMet ? "OK" : "--")} {r.Text} {r.Value} {r.Progress:P0}");
// OK Premium、Premium+、またはPremium Businessに登録
// OK 18歳以上
// -- 認証済みフォロワーが500人以上いる 136 25%
// -- 過去90日間でホームタイムラインの認証済みインプレッションが50万回以上 26.9K 5%
Console.WriteLine(rewards.IsEligible);     // 全条件を満たしていれば true

var subscriptions = await client.GetCreatorSubscriptionsEligibilityAsync();
foreach (var r in subscriptions.Unmet) Console.WriteLine(r.Text);
```

`Value` と `Progress` は数値条件の行だけに入ります（X は進捗バーを 20 分割で丸めるので `Progress` は目安です）。
行に付く注記（「返信は含まれません」など）は `Notes`、ヘルプセンターのリンクは `HelpUrl` です。
他の Jetfuel ページは `client.Jetfuel.GetAsync("creators/studio")` で生の応答（`ApiResponse.Bytes` にバイナリ）を取得できます。

### 下書き

「未送信ポスト」→「下書き」の一覧・保存・削除・投稿です。保存するときは投稿時と同じ `post_tweet_request`（本文、メディア ID、
引用元 URL、返信先）を送ります。X に「下書きを投稿する」API は無く、Web クライアントは保存内容から通常の CreateTweet を
送ったあと DeleteDraftTweet を呼ぶので、`PostDraftTweetAsync` も同じ 2 リクエストを送ります。

```csharp
var id = await client.CreateDraftTweetAsync("あとで書く", attachmentUrl: "https://x.com/user/status/1");
foreach (var d in await client.GetDraftTweetsAsync())       // 新しい順
    Console.WriteLine($"{d.Id} {d.Text} media={d.MediaIds.Count} quote={d.AttachmentUrl} reply={d.InReplyToTweetId}");
var tweet = await client.PostDraftTweetAsync(id);            // 投稿して下書きを削除（DraftTweet.PostAsync() も同じ）
await client.DeleteDraftTweetAsync(id);                      // まとめて消すなら DeleteDraftTweetsAsync(ids)
```

スレッド（`thread_tweets` を持つ下書き）の投稿は Web クライアントでの送り方が未確認のため、`PostDraftTweetAsync` は例外にします。

### コミュニティノート

投稿に提案されたノートの一覧（表示前のものを含む）、協力者プロフィール、自分が書いたノート、`x.com/i/communitynotes` の 3 タブ、
ノートの評価です。評価の効果 `RatingImpact` と執筆の効果 `WritingImpact` は Web クライアントと同じ式
（`successful.total − unsuccessful.helpful_count − 2 × unsuccessful.not_helpful_count`、`currently_rated_helpful − currently_rated_not_helpful`）で計算します。

```csharp
var me = await client.GetCommunityNotesProfileAsync();                 // 自分（alias を渡すと他人の公開統計）
Console.WriteLine($"{me.Alias} 評価の効果={me.RatingImpact} 執筆の効果={me.WritingImpact} 執筆解放まで={me.SuccessfulRatingNeededToEarnIn}");
var mine = await client.GetContributorCommunityNotesAsync();            // 自分が書いたノート（includeDrafts: true で下書きも）

var posts = await client.GetCommunityNotesTimelineAsync(CommunityNotesTimelineTab.NeedsYourHelp);   // New / RatedHelpful も同じ
foreach (var post in posts)
{
    Console.WriteLine($"{post.Id} {post.CommunityNote?.Text}");          // X がその投稿に対して選んだノート
    foreach (var note in await post.GetCommunityNotesAsync())             // 投稿の全ノート（評価できる形）
        if (note.RatingStatus == "NeedsMoreRatings")
            await note.RateAsync(CommunityNoteHelpfulness.Helpful, helpfulTags: new[] { CommunityNoteHelpfulTags.GoodSources });
}

await client.RateCommunityNoteAsync(noteId, tweetId, CommunityNoteHelpfulness.NotHelpful,
    notHelpfulTags: new[] { CommunityNoteNotHelpfulTags.NoSources, CommunityNoteNotHelpfulTags.OpinionSpeculation });
```

理由タグは `Helpful` / `SomewhatHelpful` のとき `helpfulTags`、`NotHelpful` / `SomewhatHelpful` のとき `notHelpfulTags` を Web のフォームが尋ねます。
不明なレベルやタグは `ArgumentException` です。`needs_your_help` タブは約 15 件の固定ピックで、3 タブともカーソル無しで終端します。

### ページ送り（`Result<T>`）

一覧を返すメソッドは [`Result<T>`](../api/Twikit.Result-1.yml) を返します。`IReadOnlyList<T>` なので
`foreach` やインデックスで読め、`NextAsync()` / `PreviousAsync()` で前後のページを取得できます。

> [!WARNING]
> `NextCursor` を自分でメソッドに渡し直さないでください。X は多くのエンドポイントで `count` を無視して
> 要求より多く返してくるため、余剰分は `Result<T>` の内部に蓄えられ、`NextAsync()` から順に返されます。
> `NextCursor` はその余剰分を飛び越えた位置を指しているので、カーソルを渡して呼び直すと蓄えた分を読み飛ばします。

```csharp
var page = await client.GetUserFollowersAsync(userId, 20);
while (page.Count > 0)          // カーソルではなく中身で終了判定する
{
    foreach (var follower in page) Console.WriteLine(follower.ScreenName);
    page = await page.NextAsync();
}
```

### 生のレスポンスを読む

各モデルは X から受け取った JSON を `Data` プロパティ（`JsonObject`）として保持しています。
モデルに無いフィールドは `tweet.Data["legacy"]?["some_field"]` のように直接読めます。
[`JsonExtensions`](../api/Twikit.JsonExtensions.yml) には `Str` / `Int` / `Bool` / `Sub` / `FindDict` といった
「無いキーは null」で読むためのヘルパーがあります。

### レート制限を見張る

直近のレスポンスに含まれる残りリクエスト数とリセット時刻は `client.RateLimitRemaining` と
`client.RateLimitReset` で確認できます。詳しくは [レート制限](ratelimits.md) を参照してください。

### ハンドシェイクのやり直し

すべてのリクエストは `X-Client-Transaction-Id` ヘッダーを必要とし、そのキーは X が数日ごとに入れ替える
webpack バンドルから取得します。長時間動かすプロセスで散発的な 404 が出始めたら `client.RefreshTransaction()`
を呼んでハンドシェイクをやり直してください。`SetCookies` を呼んだときは自動でやり直されます。

### プロキシと独自の HttpMessageHandler

```csharp
var client = new Client("ja", proxy: "http://user:pass@127.0.0.1:8080");   // socks5:// も可
```

Python 版の `impersonate=`（curl_cffi によるブラウザ TLS フィンガープリントの偽装）に相当する仕組みは .NET
標準にはありません。必要であれば、コンストラクターの `handler` 引数に独自の `HttpMessageHandler` を渡してください。
Cookie とリダイレクトはライブラリ側で処理するので、ハンドラーの `UseCookies` と `AllowAutoRedirect` は無効になります
（`HttpClientHandler` / `SocketsHttpHandler` なら自動で無効化します）。

## サンプル

[examples/Twikit.Examples](https://github.com/amanetoki7/twikit-dotnet/tree/main/examples/Twikit.Examples) に、
Python 版 twikit の `examples/*.py` に対応するサンプルがあります。

```bash
AUTH_TOKEN=... CT0=... dotnet run --project examples/Twikit.Examples -- basic
```
