# Python 版との対応

C# 版は Python 版（`twikit` 2.4.0）の構造をそのまま移植しています。Python の関数名を C# の慣習
（PascalCase、非同期メソッドは `...Async`）に置き換えただけなので、Python 版のコードは機械的に読み替えられます。

## 名前の対応

| Python | C# |
|---|---|
| `from twikit import Client` | `using Twikit;` / `new Client(...)` |
| `client.search_tweet(q, 'Latest')` | `client.SearchTweetAsync(q, "Latest")` |
| `client.get_user_by_screen_name(name)` | `client.GetUserByScreenNameAsync(name)` |
| `client.get_user_tweets(id, 'Tweets')` | `client.GetUserTweetsAsync(id, "Tweets")` |
| `client.create_tweet(text, media_ids=...)` | `client.CreateTweetAsync(text, mediaIds)` |
| `client.upload_media(path)` | `client.UploadMediaAsync(path)`（`byte[]` を受けるオーバーロードもあります） |
| `client.get_dm_history(user_id)` | `client.GetDmHistoryAsync(userId)` |
| `client.get_streaming_session(topics)` | `client.GetStreamingSessionAsync(topics)` |
| `client.spaces.get_space(id)` | `client.Spaces.GetSpaceAsync(id)` |
| `client.set_cookies({...})` | `client.SetCookies(new Dictionary<string, string> {...})` |
| `client.load_cookies(path)` / `save_cookies` | `client.LoadCookies(path)` / `client.SaveCookies(path)` |
| `client.user_id()` / `client.user()` | `client.UserIdAsync()` / `client.UserAsync()` |
| `client.rate_limit_remaining` | `client.RateLimitRemaining` |
| `client.refresh_transaction()` | `client.RefreshTransaction()` |
| `result.next()` / `result.previous()` | `result.NextAsync()` / `result.PreviousAsync()` |
| `tweet.text` / `tweet.full_text` / `tweet.user` | `tweet.Text` / `tweet.FullText` / `tweet.User` |
| `tweet.created_at_datetime` | `tweet.CreatedAtDatetime`（`DateTimeOffset`） |
| `twikit.List` | `TwitterList`（`System.Collections.Generic.List<T>` との混同を避けるため） |
| `twikit.build_query(text, options)` | `SearchQuery.Build(text, new SearchOptions { ... })` |
| `twikit.streaming.Topic.dm_update(id)` | `Twikit.Streaming.Topic.DmUpdate(id)` |
| `twikit.guest.GuestClient` | `Twikit.Guest.GuestClient` |
| `twikit.Capsolver` | `Twikit.Captcha.Capsolver` |

例外は `...Exception` を付けた名前になります: `BadRequestException`、`UnauthorizedException`、`ForbiddenException`、
`NotFoundException`、`RequestTimeoutException`、`TooManyRequestsException`、`ServerErrorException`、`CouldNotTweetException`、
`DuplicateTweetException`、`TweetNotAvailableException`、`InvalidMediaException`、`UserNotFoundException`、
`UserUnavailableException`、`AccountSuspendedException`、`AccountLockedException`、`ClientTransactionException`、
`LoginRetiredException`、`InvalidSessionException`。基底クラスは Python 版と同じ `TwitterException` です。
`ValueError` / `TypeError` に相当する引数エラーは `ArgumentException` になります。

## 変わった点

- **型付き** — Python 版で `dict` を返していたメソッドの多くは、C# ではモデルクラスか `JsonObject`（`System.Text.Json.Nodes`）を返します。
  `get_place_trends` は `PlaceTrends` クラス、`vote` は `Poll`、`get_about_account` は `JsonObject` です。
- **ミューテーションの戻り値** — `favorite_tweet` などが返していた `httpx.Response` は `HttpResponseMessage` になります。
- **`Tweet.replies`** — Python 版では `Result` とリストが混在していましたが、C# 版では常に `Result<Tweet>?` です。
- **`Tweet.community_note`** — 辞書ではなく `BirdwatchNote`（`Id`、`Text`）を返します。`Tweet.urls` / `User.urls` は
  `UrlEntity` のリストです（生の JSON は `Raw` から読めます）。
- **`Community.creator`** — 完全なユーザー情報があるときは `Creator`（`User`）、ID とスクリーンネームだけのときは
  `CreatorInfo`（`CommunityCreator`）に入ります。
- **Cookie** — `CookieContainer` を使い、`.x.com` と `.twitter.com` に固定して保存します（Python 版と同じ挙動）。
  X が返すホスト単位の `ct0` の重複は、Python 版と同じく応答のたびに取り除かれます。
- **`impersonate=` は無い** — curl_cffi 相当の TLS 偽装は .NET 標準に無いため、代わりに `handler` 引数で独自の
  `HttpMessageHandler` を差し込めます。
- **ストリーミング** — `StreamingSession` は `IAsyncEnumerable<(string? Topic, Payload Payload)>` なので `await foreach` で読みます。
- **X スペースの音声（WebRTC）** — Python 版は aiortc を使いますが、.NET 標準に WebRTC の実装は無いため、
  `IPeerConnection` / `IPeerConnectionFactory` インターフェイスを用意しています。SIPSorcery などで実装して
  `SpeakAsync` / `HostAsync` / `ListenAsync` に渡してください。メタデータ・検索・作成・終了・モデレーション・
  チャット（履歴 + WebSocket）は実装無しで動作します。
- **警告** — Python 版の `warnings.warn` は `TwikitWarnings.Handler`（既定は `Trace.TraceWarning`）に流れます。
- **`login` の `code_callback`** — `Func<string, Task<string>>` を渡します（既定は標準入力）。
- **ゲストクライアント** — `GuestClient` も移植していますが、Python 版と同じく X 側でゲストアクセスが閉じられているため
  `ActivateAsync()` は `InvalidSessionException` になります。

## 検証について

`X-Client-Transaction-Id` の計算（アニメーションキー、`float_to_hex`、3 次ベジェ、丸め）は、Python 版の実装で生成した
テストフィクスチャ（`tests/Twikit.Tests/Fixtures`）と bit 単位で一致することをテストで確認しています。
Python の `round()` は .NET の `Math.Round` と結果が異なる値があるため（例: `2.675`）、C# 版では double の厳密な
二進値から丸める `PyCompat.Round` を使っています。
