# X スペース

`client.Spaces`（[`Twikit.Spaces.Spaces`](../api/Twikit.Spaces.Spaces.yml)）から、X スペースの読み取り・検索・作成・
公開・モデレーション・終了、HLS ストリーム URL の取得、チャット履歴とライブチャットが使えます。
ライブラリの他の機能と同じく、ログイン済みの `Client`（`auth_token` と `ct0` の Cookie）が必要です。

Web クライアントが使っている次の API を、そのまま呼び出しています。

| 用途 | API |
|---|---|
| メタデータ / 検索 | GraphQL（`AudioSpaceById`、`AudioSpaceSearch`、`BrowseSpaceTopics` ...） |
| ブロードキャストのライフサイクル | proxsee.pscp.tv（Periscope）v2 API（`createBroadcast`、`publishBroadcast`、`endBroadcast`） |
| コントロールプレーン（ミュート、設定、終了、承認 ...） | guest-cf.pscp.tv `/api/v1`（chatman） |
| チャット | proxsee `accessChat` → chatapi v1（HTTP 履歴 + WebSocket） |
| 音声（発言・聴取） | Janus WebRTC videoroom |

## 読み取り

```csharp
var space = await client.Spaces.GetSpaceAsync("1DXGydznBYWKM");   // URL 全体でも可
Console.WriteLine($"{space.State} {space.Title} host={space.HostUserId} speakers={space.SpeakerIds.Count}");

// 配信中のスペースを検索（'Top' / 'Live' / 'Upcoming'）
var live = await client.Spaces.SearchAsync("music", filter: "Live");

// HLS ストリーム URL（ffmpeg で聴取: ffmpeg -i <url> out.m4a）
var stream = await client.Spaces.GetStreamAsync(space.MediaKey!);
Console.WriteLine(stream.HlsUrl);

// チャット履歴（ライブ中でもリプレイでも）
var chat = await client.Spaces.ChatAsync(space);
foreach (var message in await chat.HistoryAsync(limit: 20))
    Console.WriteLine($"{message.Sender?["username"]}: {message.Body}");
```

ライブ中のスペースでは `chat.IsLive` が true になり、`ListenAsync()` でメッセージを受信、`SendAsync(text)` で送信できます
（WebSocket は .NET 標準の `ClientWebSocket` を使うため追加パッケージは不要です）。

```csharp
await foreach (var message in chat.ListenAsync())
    Console.WriteLine(message.Body);
```

`/live-chat` の HTTP ストリーム（Web クライアントのサイドバーが使うもの）も `StreamLiveChatAsync` から読めます。
`replay: true` を渡すと終了したスペースのチャットログを取得できます。

## 作成・終了

```csharp
var created = await client.Spaces.CreateSpaceAsync(title: "こんにちは", conversationControls: 2);
var spaceId = created["broadcast"]!["id"]!.GetValue<string>();

var liveSpace = await client.Spaces.GetSpaceAsync(spaceId);   // State == "Running"
await client.Spaces.EndSpaceAsync(spaceId);
```

`CreateSpaceAsync` は Web クライアントと同じ proxsee + Janus の HTTP フロー（Janus ルームの作成、publisher の登録、
`publishBroadcast`、`adminInvite`）を実行します。`scheduledStartTime` を渡すと予約スペースになります
（`GetScheduledSpacesAsync` / `CancelScheduledSpaceAsync`）。

## モデレーション

```csharp
await client.Spaces.MuteSpaceAsync(spaceId);
await client.Spaces.SetSpaceSettingsAsync(spaceId, conversationControls: 0);  // 0=リクエスト/承認、1=フォロー中、2=全員
await client.Spaces.AddAdminAsync(spaceId, userId);
var status = await client.Spaces.GetCallStatusAsync(spaceId);            // guest_sessions など
await client.Spaces.ApproveAsync(sessionUuid, spaceId);
```

リスナーとして参加してから発言をリクエストし、ホストの承認を待つこともできます。

```csharp
var joined = await client.Spaces.JoinAsync(space);
var sessionUuid = await client.Spaces.RequestToSpeakAsync(space);
var guest = await client.Spaces.WaitForSpeakerAsync(space.Id!, sessionUuid, timeoutSeconds: 120);
```

## 音声（WebRTC）

発言（`SpeakAsync`）、ホストの音声（`HostAsync`）、WebRTC での聴取（`ListenAsync`）には WebRTC の
ピア接続が必要です。Python 版は aiortc を使いますが、.NET 標準に WebRTC の実装は無いため、C# 版では
[`IPeerConnection`](../api/Twikit.Spaces.IPeerConnection.yml) と
[`IPeerConnectionFactory`](../api/Twikit.Spaces.IPeerConnectionFactory.yml) というインターフェイスを用意しています。
[SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) などで実装して渡してください。

```csharp
public sealed class MyFactory : IPeerConnectionFactory
{
    public IPeerConnection Create(IReadOnlyList<IceServer> iceServers) => new MyPeerConnection(iceServers);
}

var session = await client.Spaces.SpeakAsync(space, audioTrack: myTrack, peerConnectionFactory: new MyFactory());
// ... 発言中 ...
await session.CloseAsync();
```

Janus とのシグナリング（`JanusClient`: create / attach / join / configure / ロングポール）、chatman の
`negotiate` / `publish` / `unmute` などの手順は Python 版と同じ順序で実装されているので、実装する必要があるのは
SDP の offer / answer と音声トラックの送受信だけです。

> [!NOTE]
> 既定では TURN を使わず直接接続します。X の TURN サーバー（turns:turn.pscp.tv:443）は約 60 秒で TLS 接続を切り、
> メディアの経路が止まるためです。直接接続できない環境では `iceServers` を明示的に渡してください。

## 簡単に聴くには

WebRTC を使わなくても、`StreamUrlAsync(space)` が返す HLS の m3u8 を ffmpeg などで再生すれば聴取できます。

```bash
ffmpeg -i "<hls url>" out.m4a
```
