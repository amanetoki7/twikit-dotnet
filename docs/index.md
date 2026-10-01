---
_layout: landing
---

# twikit-dotnet

**API キー不要**で使える、.NET 向けの **Twitter / X** API クライアントライブラリです。
Python 版 [amanetoki7/twikit](https://github.com/amanetoki7/twikit)（[d60/twikit](https://github.com/d60/twikit) に
[PawiX25/twifork](https://github.com/PawiX25/twifork) の 2026 年時点の修正と X スペース対応を取り込んだフォーク）を、
そのまま C# に移植したものです。

- [はじめに](articles/getting-started.md) — インストール、Cookie でのログイン、基本的な使い方
- [Python 版との対応](articles/python-differences.md) — 名前の対応表と、C# 版で変わった点
- [X スペース](articles/spaces.md) — スペースの取得・検索・作成・モデレーション・チャット
- [レート制限](articles/ratelimits.md) / [アカウントを守るために](articles/protect-account.md) / [エラー](articles/errors.md)
- [API リファレンス](api/index.md) — すべての公開クラス・メソッド（XML ドキュメントコメントから生成）

```csharp
using Twikit;

var client = new Client("ja");
client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "...", ["ct0"] = "..." });

var tweets = await client.SearchTweetAsync("python", "Latest");
foreach (var tweet in tweets)
    Console.WriteLine($"{tweet.User?.Name}: {tweet.Text}");
```
