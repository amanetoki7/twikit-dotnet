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
