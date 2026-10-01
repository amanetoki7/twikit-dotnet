# エラーとエラーコード

すべての例外は [`TwitterException`](../api/Twikit.TwitterException.yml) を継承しています。
HTTP ステータスに対応する例外には、そのレスポンスのヘッダーが `Headers` に入ります。

| 例外 | 意味 |
|---|---|
| `BadRequestException` | 400 Bad Request |
| `UnauthorizedException` | 401 Unauthorized |
| `ForbiddenException` | 403 Forbidden |
| `NotFoundException` | 404 Not Found。存在しないリスト・コミュニティ・ノートなどにも使われます |
| `RequestTimeoutException` | 408 Request Timeout |
| `TooManyRequestsException` | 429 Too Many Requests。`RateLimitReset` にリセット時刻が入ります |
| `ServerErrorException` | 5xx |
| `CouldNotTweetException` | ツイートを送信できなかった（日次上限など。X のメッセージをそのまま含みます） |
| `DuplicateTweetException` | 重複ツイート（code 187） |
| `TweetNotAvailableException` | ツイートが利用できない（削除済み・非公開など） |
| `InvalidMediaException` | メディア ID に問題がある（code 324） |
| `UserNotFoundException` | ユーザーが存在しない |
| `UserUnavailableException` | ユーザーが利用できない（鍵・凍結・削除済みなど） |
| `AccountSuspendedException` | アカウントが凍結されている |
| `AccountLockedException` | アカウントがロックされている（多くの場合 Arkose チャレンジ）。`Capsolver` を渡すと自動解除を試みます |
| `ClientTransactionException` | `X-Client-Transaction-Id` のハンドシェイクを完了できない |
| `InvalidSessionException` | x.com が未ログインのページの殻を返した。Cookie が無い・期限切れ・拒否された |
| `LoginRetiredException` | パスワードログインは X 側で廃止されている |

## X のエラーコード

X は HTTP ステータスに加えて本文中に数値の `code` を返し、実際に何が起きたかを伝えるのはそちらです。実際に観測したコードの一覧:

| code | 意味 |
|---|---|
| 32 | 認証情報が不正または期限切れ。Cookie が死んでいるときの典型 |
| 34 | エンドポイントが存在しない。v1.1 のいくつかがこれを返すようになった |
| 37 | このリソースへの権限が無い。**凍結ではない**（文言に "suspend" があるときだけ凍結として扱います） |
| 63 | アカウントが凍結されている |
| 64 | 認証中のアカウントが凍結されている |
| 88 | レート制限超過。`Client.RateLimitReset` を参照 |
| 89 | トークンが不正または期限切れ |
| 139 | すでにいいね済み |
| 144 | その ID のツイートは存在しない |
| 179 | このツイートを見る権限が無い（鍵アカウント） |
| 187 | 重複ツイート。同じ本文を直近に投稿している |
| 226 | 自動化されたリクエストと判定された。アカウント単位のスパムスコア |
| 279 | DM の会話が存在しない |
| 326 | アカウントが一時的にロックされている（`AccountLockedException`） |
| 349 | このユーザーにはメッセージを送れない（フォロー外からの DM を受け付けていない） |
| 398 | 人間であることを確認できない（ログイン時の Arkose/FunCaptcha） |
| 399 | castle トークンが必要なログインチャレンジ |

## 散発的な 404

同じ呼び出しで出たり出なかったりする 404 は、ほぼ例外なくエンドポイントではなく `x-client-transaction-id` のハンドシェイクが原因です。
X はこのヘッダーを選択的に検証するため、古いキーで作った ID は通る操作と弾かれる操作があります。
キーの元になる `ondemand.s` バンドルは数日ごとに入れ替わるので、長時間動かすプロセスは散発的な 404 に劣化していきます。
`client.RefreshTransaction()` でハンドシェイクをやり直すと解消します。
