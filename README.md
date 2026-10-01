<h1 align="center">twikit-dotnet</h1>

<p align="center">
  <b>API キー不要</b>で使える、C# / .NET 向けの <b>Twitter / X</b> API クライアントライブラリ。<br>
  Python 製の <a href="https://github.com/d60/twikit">d60/twikit</a> に <a href="https://github.com/PawiX25/twifork">PawiX25/twifork</a> の修正と機能追加（2026 年時点で本家が動かなくなった不具合の修正、X スペース対応など）を取り込んだ <a href="https://github.com/amanetoki7/twikit">amanetoki7/twikit</a> を、そのまま C# に移植したものです。
</p>

<p align="center">
  <a href="https://github.com/amanetoki7/twikit-dotnet/actions/workflows/dotnet.yml"><img src="https://github.com/amanetoki7/twikit-dotnet/actions/workflows/dotnet.yml/badge.svg" alt="build"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%2B-512BD4" alt=".NET 8+">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
  <img src="https://img.shields.io/github/stars/amanetoki7/twikit-dotnet?style=flat&color=yellow" alt="Stars">
</p>

<p align="center">
  [日本語] · [<a href="README-en.md">English</a>] · [<a href="README-zh.md">中文</a>]
</p>

> **Python 版と同じ構造・同じ挙動。** メソッド名を C# の慣習（PascalCase、`...Async`）に置き換えただけなので、Python 版のコードやドキュメントはそのまま読み替えられます。対応表は [Python 版との対応](docs/articles/python-differences.md) を参照してください。Python 版そのものが必要な場合は [amanetoki7/twikit](https://github.com/amanetoki7/twikit) を使ってください。

---

## 構成

| パス | 内容 |
|---|---|
| [`src/Twikit/`](src/Twikit) | ライブラリ本体（`net8.0`）。依存パッケージは [AngleSharp](https://anglesharp.github.io/)（HTML の解析）と [Jint](https://github.com/sebastienros/jint)（ui_metrics チャレンジの JavaScript 評価）だけです |
| [`tests/Twikit.Tests/`](tests/Twikit.Tests) | xunit のテスト。ネットワークには接続しません |
| [`examples/Twikit.Examples/`](examples/Twikit.Examples) | サンプル（基本操作、メディアのダウンロード、新着ツイートの監視、DM の自動返信、全ツイート削除、X スペース） |
| [`docs/`](docs) | [DocFX](https://dotnet.github.io/docfx/) のドキュメント（日本語）。ガイドと、XML ドキュメントコメントから生成する API リファレンス |
| [`Twikit.sln`](Twikit.sln) | 上記 3 プロジェクトをまとめたソリューション |

## インストール

NuGet には公開していません。リポジトリを clone して、プロジェクト参照で使ってください。**.NET 8 以上**が必要です。

```bash
git clone https://github.com/amanetoki7/twikit-dotnet.git
```

```xml
<ItemGroup>
  <ProjectReference Include="path/to/twikit-dotnet/src/Twikit/Twikit.csproj" />
</ItemGroup>
```

パッケージとして配布する場合は `dotnet pack src/Twikit/Twikit.csproj -c Release` で `.nupkg` を作れます。

## 使い方

> [!IMPORTANT]
> **パスワードではなく Cookie でログインしてください。**
> X は `Client.LoginAsync()` が使うオンボーディングフローを廃止しており、`code 366, "flow name LoginFlow is currently not accessible"` を返します。2026-07-29 に実際の x.com で確認したところ、`/i/flow/login` は `/i/jf/onboarding/web` にリダイレクトされ、サイトは `/i/jfapi/onboarding/web/actions/begin_login` に POST します。この POST には難読化されたページ内 JavaScript が生成する約 5 KB の `$castle_token` が必要で、第一要素としてパスキー / WebAuthn も提示されます。いずれも素の HTTP クライアントからは到達できないため、**パスワードログインはライブラリ側では直せません**（`LoginRetiredException` が送出されます）。代わりに、ブラウザでログインしたセッションの Cookie を取り出して使ってください。

**Cookie の取り出し方**: ブラウザで x.com にログインし、開発者ツール（Application / Storage → Cookies）から `auth_token` と `ct0` の値をコピーします。この 2 つだけで十分です。

**クライアントを用意して Cookie をセットする。**

```csharp
using Twikit;

var client = new Client("ja");

// auth_token と ct0 だけで十分です
client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "...", ["ct0"] = "..." });
// または: client.LoadCookies("cookies.json");

if (!await client.IsLoggedInAsync())
    throw new Exception("Cookie が無効です。ブラウザから取り直してください。");

// 次回以降のために保存しておく
client.SaveCookies("cookies.json");
```

`SetCookies` には、ブラウザの拡張機能や Playwright が書き出す形式（`name` と `value` を持つオブジェクトの配列）も `JsonNode` として渡せます。

**メディア付きツイートを投稿する。**

```csharp
var mediaIds = new List<string>
{
    await client.UploadMediaAsync("media1.jpg"),
    await client.UploadMediaAsync("media2.jpg"),
};
await client.CreateTweetAsync("Example Tweet", mediaIds);
```

**キーワードで最新のツイートを検索する。**

```csharp
var tweets = await client.SearchTweetAsync("python", "Latest");
foreach (var tweet in tweets)
    Console.WriteLine($"{tweet.User?.Name}: {tweet.Text} ({tweet.CreatedAt})");

var more = await tweets.NextAsync();   // 続きを取得
```

**その他、よく使う呼び出し。**

```csharp
await client.GetUserTweetsAsync("123456", "Tweets");   // ユーザーのツイート
await client.SendDmAsync("123456789", "Hello");         // DM を送る
await client.GetTrendsAsync("trending");                // トレンド
```

### サンプルを動かす

[examples/Twikit.Examples](examples/Twikit.Examples) は 1 つのコンソールアプリで、第 1 引数でサンプルを選びます。Cookie は環境変数 `AUTH_TOKEN` と `CT0` で渡します（初回に `cookies.json` へ保存され、2 回目以降はそれが使われます）。

```bash
AUTH_TOKEN=... CT0=... dotnet run --project examples/Twikit.Examples -- basic
```

| 引数 | 内容 |
|---|---|
| `basic` | 検索・ユーザー取得・ツイート取得などの基本操作 |
| `download-media` | ツイートの添付メディアをダウンロード |
| `listen` | 新しいツイートを監視 |
| `dm-auto-reply` | DM に自動返信 |
| `delete-all` | 自分のツイートをすべて削除 |
| `spaces` | X スペースのデモ |
| `guest` | ゲストクライアント（現在 X 側で閉じられています） |

## 主な機能

- **API キー不要** — Web 版をスクレイピングして動作します。
- **無料・オープンソース**（MIT）。
- ツイート、検索、タイムライン、トレンド、ユーザー、DM、メディア、ブックマーク、リスト、コミュニティ、通知、ストリーミングなど、Python 版のすべての公開 API。
- **X スペース（Spaces）** — 読み取り・検索・作成・公開・モデレーション・終了、HLS ストリーム URL、チャット履歴・ライブチャット（WebSocket）。追加パッケージは不要です（WebRTC の音声だけは `IPeerConnectionFactory` の実装が必要です。後述）。詳しくは [X スペース](docs/articles/spaces.md) と [SpacesExample.cs](examples/Twikit.Examples/SpacesExample.cs) を参照してください。

  ```csharp
  var space = await client.Spaces.GetSpaceAsync("1DXGydznBYWKM");
  var stream = await client.Spaces.GetStreamAsync(space.MediaKey!);        // HLS URL
  var chat = await client.Spaces.ChatAsync(space);                          // 履歴
  var created = await client.Spaces.CreateSpaceAsync(title: "こんにちは");   // ライブ開始
  await client.Spaces.EndSpaceAsync(created["broadcast"]!["id"]!.GetValue<string>());
  ```

### 本家 twikit との違い

本家 d60/twikit の最終リリース（`twikit==2.3.3`）は 2026 年現在、いくつもの箇所が壊れています。このライブラリは twifork の修正を取り込んだ Python 版を移植しているため、本家で報告されている次の問題は解消しています（括弧内は本家の Issue）。

- **ClientTransaction / `Couldn't get KEY_BYTE indices`** — X の新しい webpack バンドルに合わせて `ondemand.s.js` の解析を更新し、GraphQL リクエストが通るようにしました。([#408](https://github.com/d60/twikit/issues/408), [#409](https://github.com/d60/twikit/issues/409), [#304](https://github.com/d60/twikit/issues/304))
- **散発的に出て、そのまま居座る `404`** — `X-Client-Transaction-Id` のアニメーションキー計算で抜けていた X 側の丸め処理を復元しました。長時間動かすプロセスでは `client.RefreshTransaction()` でハンドシェイクをやり直せます。([#357](https://github.com/d60/twikit/issues/357), [#397](https://github.com/d60/twikit/issues/397))
- **任意フィールド欠落時のクラッシュ、ユーザーの `Name` / `ScreenName` が空になる** — X が `legacy` から新しいサブオブジェクトへ移した `name`・`screen_name`・`created_at`・アイコンなどを新しい場所から読み、`legacy` にもフォールバックします。無いキーは null として扱います。([#417](https://github.com/d60/twikit/issues/417))
- **`GetTweetByIdAsync` の `itemContent` 欠落**、**ツイートが無いアカウントでの `GetUserTweetsAsync`** — 旧形式と新しい末尾カーソル形式の両方に対応し、空・カーソル無しのタイムラインでも空の結果を返します。([#332](https://github.com/d60/twikit/issues/332), [#363](https://github.com/d60/twikit/issues/363), [#361](https://github.com/d60/twikit/issues/361), [#216](https://github.com/d60/twikit/issues/216))
- **`GetTrendsAsync` が何も返さない** — `GenericTimelineById` ベースに作り直し、あわせて `GetExplorePageAsync()` を追加しました。無限リトライと、news / sports / entertainment が空で返る問題も解消しています。([#389](https://github.com/d60/twikit/issues/389))
- **`GetLatestFriendsAsync` の 404** — 廃止された v1.1 エンドポイントの代わりに GraphQL の `Following` を使います。([#397](https://github.com/d60/twikit/issues/397))
- **captcha 解除の処理**（[#333](https://github.com/d60/twikit/issues/333)）と、**`GetBookmarkFoldersAsync` のページネーションが無限ループする問題**（[#334](https://github.com/d60/twikit/issues/334), [#335](https://github.com/d60/twikit/issues/335)）を修正しました。
- **`GetLatestTimelineAsync` / `GetListTweetsAsync` が会話ツイートを取りこぼす** — home / list の会話エントリも展開します。([#336](https://github.com/d60/twikit/issues/336), [#337](https://github.com/d60/twikit/issues/337), [#340](https://github.com/d60/twikit/issues/340))
- **ページネーション** — カーソルの無いページで `NextAsync()` が同じページを取り直し続ける問題を修正しました。`count` は指定どおりに切り詰め、X が余分に返した分は `NextAsync()` から順に返します。
- **`Replies` タブと DM** — `GetUserTweetsAsync(..., "Replies")` で本人の返信ではなく相手のツイートが返る問題、固定ツイートの取りこぼし・重複、`SendDmAsync` の送信者の誤りを修正しました。
- **X の拒否を隠さない** — 権限エラー（コード 37 など）を空の結果として握りつぶさず例外を送出します。`CreateTweetAsync` の日次上限のメッセージもそのまま表示します。存在しないリスト・ユーザーは空のオブジェクトではなく `NotFoundException` / `UserNotFoundException` になります。
- **通知** — `GetNotificationsAsync("Mentions")` で `notifications` キーが無い新形式のレスポンスに対応しました。
- **新しいプロパティ** — フル解像度の画像 URL を返す `Media.SourceUrl`（[#376](https://github.com/d60/twikit/issues/376)）、引用元ツイートの ID を返す `Tweet.QuotedStatusId`（[#222](https://github.com/d60/twikit/issues/222)）のほか、`Tweet.Url`、`Tweet.Article`、`Tweet.Source`、`Tweet.ConversationIds`、`User.VerifiedType`、パロディ / 自動化ラベル、`Conversation`、`Message.ReplyData` など。
- **エンドポイント** — GraphQL の query id と features フラグを 2026 年時点のものへ更新しました。

`GuestClient` も移植していますが、X 側でゲストアクセスが閉じられているため `ActivateAsync()` は `InvalidSessionException` になります。未ログインのリクエストには webpack マニフェストを含まない約 34 KB のページの殻しか返ってこないため、`X-Client-Transaction-Id` のハンドシェイクを完了できません。これはライブラリ側では直せないので、Cookie を使ってください。([#192](https://github.com/d60/twikit/issues/192)) 同様に、X 側の制限（アカウント凍結、Cloudflare / IP ブロック、captcha、自動化の制約）が原因の問題も対象外です。

### 本家に無い追加 API

- `Client.IsLoggedInAsync()` — Cookie がまだ有効かを確認します。
- `Client.RateLimitRemaining` / `Client.RateLimitReset` — 直近のレスポンスに含まれるレート制限の残りとリセット時刻。
- `Client.RefreshTransaction()` — `X-Client-Transaction-Id` のハンドシェイクをやり直します。
- `Client.GetDmInboxAsync()`、`CreateGroupAsync()`、`DeleteDmConversationAsync()` — DM の受信箱一覧、グループ作成、会話の削除。
- `Client.UpdateProfileAsync()`、`GetAboutAccountAsync()`、`GetUserSpotlightsAsync()`、`GetUserListsAsync()`、`GetMutedUsersAsync()`、`GetBlockedUsersAsync()`。
- `Client.GetUserMentionsAsync()`、`SearchTweetsByDateAsync()`、`GetThreadAsync()`、`GetTweetByUrlAsync()`、`GetExplorePageAsync()`。
- `SearchOptions` / `SearchQuery.Build` — 検索クエリの組み立て。
- `Article`、`Conversation` — 長文記事と DM の会話。
- `Twikit.Spaces` 名前空間 — スペース関連のクラス群（`Spaces`、`Space`、`SpaceChat`、`SpaceVoiceSession` など）。
- 例外: `ClientTransactionException`、`InvalidSessionException`（Cookie が無効）、`LoginRetiredException`（パスワードログインは廃止）。

## Python 版との違い

- **`impersonate=`（curl_cffi による TLS 偽装）はありません。** .NET 標準に相当する仕組みが無いため、代わりに `new Client(handler: ...)` で独自の `HttpMessageHandler` を差し込めます。プロキシは `new Client("ja", proxy: "http://user:pass@127.0.0.1:8080")` のように指定します（`socks5://` も可）。
- **X スペースの音声（WebRTC）** は、.NET 標準に WebRTC 実装が無いため `IPeerConnection` / `IPeerConnectionFactory` インターフェイスを用意しています（[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) などで実装して `SpeakAsync` / `HostAsync` / `ListenAsync` に渡します）。メタデータ・検索・作成・終了・モデレーション・チャットは実装無しで動作します。
- **`List` は `TwitterList`** という名前です（`System.Collections.Generic.List<T>` との混同を避けるため）。
- 例外は `...Exception` 付きの名前（`NotFoundException` など）。基底クラスは Python 版と同じ `TwitterException` です。
- 一覧を返すメソッドは `Result<T>`（`IReadOnlyList<T>`）を返し、`NextAsync()` / `PreviousAsync()` で前後のページを取得します。ストリーミングは `IAsyncEnumerable` なので `await foreach` で読みます。

そのほかの細かな差異は [Python 版との対応](docs/articles/python-differences.md) にまとめています。

## ドキュメント

[DocFX](https://dotnet.github.io/docfx/) で日本語のドキュメントを生成できます。API リファレンスはソースコードの XML ドキュメントコメント（日本語）から作られます。

```bash
dotnet tool install -g docfx
docfx docs/docfx.json --serve   # http://localhost:8080
```

`main` に push すると [`.github/workflows/docs.yml`](.github/workflows/docs.yml) がサイトをビルドして GitHub Pages（https://amanetoki7.github.io/twikit-dotnet/）に公開します。リポジトリの Settings → Pages → Source を "GitHub Actions" にしておいてください。

- [はじめに](docs/articles/getting-started.md) — インストール、Cookie でのログイン、ページ送り、プロキシ
- [Python 版との対応](docs/articles/python-differences.md) — 名前の対応表と、C# 版で変わった点
- [X スペース](docs/articles/spaces.md) — 取得・検索・作成・モデレーション・チャット・音声
- [レート制限](docs/articles/ratelimits.md) / [アカウントを守るために](docs/articles/protect-account.md) / [エラーとエラーコード](docs/articles/errors.md)
- 本家の API リファレンス（Python、英語）: https://twikit.readthedocs.io/en/latest/twikit.html

## ビルドとテスト

```bash
dotnet build Twikit.sln -c Release
dotnet test tests/Twikit.Tests -c Release
```

テストはネットワークに接続しません。`X-Client-Transaction-Id` の計算（アニメーションキー、`float_to_hex`、3 次ベジェ、Python 互換の丸め）は、Python 版の実装で生成した [tests/Twikit.Tests/Fixtures](tests/Twikit.Tests/Fixtures) の値と bit 単位で一致することを確認しています。push と Pull Request のたびに [`.github/workflows/dotnet.yml`](.github/workflows/dotnet.yml) が同じビルドとテストを実行します。

## アカウントを守るために

このライブラリは非公式 API を使うため、使い方を誤るとアカウントが凍結される可能性があります。リクエストを送りすぎない、Cookie を使い回す、といった対策を [アカウントを守るために](docs/articles/protect-account.md) にまとめています。

## コミュニティ

本家 twikit の Discord: [![Discord](https://img.shields.io/badge/Discord-%235865F2.svg?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/nCrByrr8cX)

## コントリビュート

不具合を見つけたとき、修正があるときは、[Issues](https://github.com/amanetoki7/twikit-dotnet/issues) に Issue や Pull Request をお願いします。

役に立ったら、すたー を付けてもらえると励みになります。

## クレジット

- **[d60/twikit](https://github.com/d60/twikit)**（[@d60](https://github.com/d60)）— 元になる実装。功績はすべて原作者に帰属します。
- **[PawiX25/twifork](https://github.com/PawiX25/twifork)**（[@PawiX25](https://github.com/PawiX25) とコントリビューターの皆さん）— 2026 年時点の不具合修正と X スペース対応。
- **[amanetoki7/twikit](https://github.com/amanetoki7/twikit)** — 上記を取り込んだ Python 版。このリポジトリはそれを移植したものです。
- **[iSarabjitDhiman/TweeterPy](https://github.com/iSarabjitDhiman/TweeterPy)** — `X-Client-Transaction-Id` の計算の由来。
- すきくん **[八雲ゆかり](https://x.com/yukari_557fd8) さま** ([@yukari-557fd8](https://github.com/yukari-557fd8)) - 公式APIしか使ったことないわたしを救ってくれた。このリポジトリがうまれるきっかけにもなりました。

GitHubリポジトリは、いずれも **MIT ライセンス**で公開されています。

## 免責事項

このリポジトリは独立した非公式プロジェクトであり、**X Corp. とは一切の提携・承認・スポンサー関係はありません。**「X」および「Twitter」は X Corp. の商標です。利用にあたっては、適用される規約および法令を守ってください。
