<h1 align="center">twikit-dotnet</h1>

<p align="center">
  一个无需 <b>API 密钥</b> 的 C# / .NET <b>Twitter / X</b> API 客户端库。<br>
  它是 <a href="https://github.com/amanetoki7/twikit">amanetoki7/twikit</a> 的 C# 移植版。amanetoki7/twikit 是 <a href="https://github.com/d60/twikit">d60/twikit</a> 的 Python 分支，并入了 <a href="https://github.com/PawiX25/twifork">PawiX25/twifork</a> 的修复与新增功能（让上游在 2026 年无法正常使用的那些问题的修复、X Spaces 支持等）。
</p>

<p align="center">
  <a href="https://github.com/amanetoki7/twikit-dotnet/actions/workflows/dotnet.yml"><img src="https://github.com/amanetoki7/twikit-dotnet/actions/workflows/dotnet.yml/badge.svg" alt="build"></a>
  <img src="https://img.shields.io/badge/.NET-8.0%2B-512BD4" alt=".NET 8+">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="MIT License">
  <img src="https://img.shields.io/github/stars/amanetoki7/twikit-dotnet?style=flat&color=yellow" alt="Stars">
</p>

<p align="center">
  [<a href="README.md">日本語</a>] · [<a href="README-en.md">English</a>] · [中文]
</p>

> **与 Python 版结构相同、行为相同。** 只是把方法名改成了 C# 的惯例（PascalCase、`...Async`），因此 Python 版的代码和文档可以一一对应地照搬。对应表见 [docs/articles/python-differences.md](docs/articles/python-differences.md)。本仓库的文档以日语编写。如果你需要的是 Python 库本身，请使用 [amanetoki7/twikit](https://github.com/amanetoki7/twikit)。

---

## 目录结构

| 路径 | 内容 |
|---|---|
| [`src/Twikit/`](src/Twikit) | 库本体（`net8.0`）。仅依赖 [AngleSharp](https://anglesharp.github.io/)（解析 HTML）和 [Jint](https://github.com/sebastienros/jint)（执行 ui_metrics 挑战的 JavaScript） |
| [`tests/Twikit.Tests/`](tests/Twikit.Tests) | xunit 测试，不访问网络 |
| [`examples/Twikit.Examples/`](examples/Twikit.Examples) | 示例：基本操作、下载媒体、监听新推文、私信自动回复、删除全部推文、X Spaces |
| [`docs/`](docs) | [DocFX](https://dotnet.github.io/docfx/) 文档（日语）：指南，以及由 XML 文档注释生成的 API 参考 |
| [`Twikit.sln`](Twikit.sln) | 把上述三个项目组合在一起的解决方案 |

## 安装

没有发布到 NuGet。请克隆仓库并通过项目引用使用。需要 **.NET 8 或更高版本**。

```bash
git clone https://github.com/amanetoki7/twikit-dotnet.git
```

```xml
<ItemGroup>
  <ProjectReference Include="path/to/twikit-dotnet/src/Twikit/Twikit.csproj" />
</ItemGroup>
```

如需以包的形式分发，执行 `dotnet pack src/Twikit/Twikit.csproj -c Release` 即可生成 `.nupkg`。

## 快速上手

> [!IMPORTANT]
> **请用 cookie 登录，不要用密码。**
> X 已经下线了 `Client.LoginAsync()` 所依赖的 onboarding 流程，它会返回 `code 366, "flow name LoginFlow is currently not accessible"`。2026-07-29 对线上 x.com 的验证结果：`/i/flow/login` 会重定向到 `/i/jf/onboarding/web`，网站会向 `/i/jfapi/onboarding/web/actions/begin_login` 发送 POST，该请求需要由页面内混淆 JavaScript 生成的约 5 KB 的 `$castle_token`，并把 passkey / WebAuthn 作为第一因素提供。这些都无法从普通的 HTTP 客户端触达，因此**密码登录无法实现**，会抛出 `LoginRetiredException`。请改为导出浏览器会话中的 cookie。

**获取 cookie**：在浏览器中登录 x.com，打开开发者工具（Application / Storage → Cookies），复制 `auth_token` 和 `ct0` 的值。这两个就够了。

**创建客户端并载入 cookie。**

```csharp
using Twikit;

var client = new Client("zh-cn");

// auth_token 和 ct0 就够了
client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "...", ["ct0"] = "..." });
// 或者：client.LoadCookies("cookies.json");

if (!await client.IsLoggedInAsync())
    throw new Exception("cookie 已失效，请重新导出。");

// 保存起来供下次使用
client.SaveCookies("cookies.json");
```

`SetCookies` 也接受浏览器扩展或 Playwright 导出的列表格式（由带 `name` 和 `value` 的对象组成的数组），以 `JsonNode` 传入即可。

**发布带图片的推文。**

```csharp
var mediaIds = new List<string>
{
    await client.UploadMediaAsync("media1.jpg"),
    await client.UploadMediaAsync("media2.jpg"),
};
await client.CreateTweetAsync("Example Tweet", mediaIds);
```

**按关键词搜索最新推文。**

```csharp
var tweets = await client.SearchTweetAsync("python", "Latest");
foreach (var tweet in tweets)
    Console.WriteLine($"{tweet.User?.Name}: {tweet.Text} ({tweet.CreatedAt})");

var more = await tweets.NextAsync();   // 下一页
```

**其他常用调用。**

```csharp
await client.GetUserTweetsAsync("123456", "Tweets");   // 某个用户的推文
await client.SendDmAsync("123456789", "Hello");         // 发送私信
await client.GetTrendsAsync("trending");                // 热门趋势
```

### 运行示例

[examples/Twikit.Examples](examples/Twikit.Examples) 是一个控制台应用，用第一个参数选择示例。cookie 通过环境变量 `AUTH_TOKEN` 和 `CT0` 传入（第一次运行时会保存到 `cookies.json`，之后直接复用）。

```bash
AUTH_TOKEN=... CT0=... dotnet run --project examples/Twikit.Examples -- basic
```

| 参数 | 作用 |
|---|---|
| `basic` | 搜索、获取用户、读取推文等基本操作 |
| `download-media` | 下载推文附带的媒体 |
| `listen` | 监听某个用户的新推文 |
| `dm-auto-reply` | 自动回复私信 |
| `delete-all` | 删除自己账户上的全部推文 |
| `spaces` | X Spaces 演示 |
| `guest` | 游客客户端（目前已被 X 关闭） |

## 特性

- **无需 API 密钥** — 通过抓取网页端工作。
- **免费、开源**（MIT）。
- 推文、搜索、时间线、趋势、用户、私信、媒体、书签、列表、社区、通知、流式推送等，Python 版的全部公开 API。
- **X Spaces** — 读取、搜索、创建、发布、管理和结束语音空间，HLS 流地址，聊天记录和实时聊天（WebSocket）。不需要额外的包；只有 WebRTC 语音需要你提供 `IPeerConnectionFactory` 的实现（见下文）。参见 [docs/articles/spaces.md](docs/articles/spaces.md) 和 [SpacesExample.cs](examples/Twikit.Examples/SpacesExample.cs)：

  ```csharp
  var space = await client.Spaces.GetSpaceAsync("1DXGydznBYWKM");
  var stream = await client.Spaces.GetStreamAsync(space.MediaKey!);   // HLS 地址
  var chat = await client.Spaces.ChatAsync(space);                     // 聊天记录
  var created = await client.Spaces.CreateSpaceAsync(title: "hi");     // 开始直播
  await client.Spaces.EndSpaceAsync(created["broadcast"]!["id"]!.GetValue<string>());
  ```

### 相对上游 twikit 修复了什么

上游的最后一个版本（`twikit==2.3.3`）在 2026 年已经有多处无法使用。本库移植自并入 twifork 修复的 Python 分支，因此上游报告的下列问题在这里都不会出现（每一项都链接到对应的上游 Issue）：

- **ClientTransaction / `Couldn't get KEY_BYTE indices`** — 按 X 新的 webpack 打包结构更新了 `ondemand.s.js` 的解析，GraphQL 请求可以正常使用。([#408](https://github.com/d60/twikit/issues/408)、[#409](https://github.com/d60/twikit/issues/409)、[#304](https://github.com/d60/twikit/issues/304))
- **时有时无、且会"卡住"的 `404`** — 计算 `X-Client-Transaction-Id` 的动画密钥时漏掉了 X 的一个取整步骤，现已补回。长期运行的进程可以调用 `client.RefreshTransaction()` 重新握手。([#357](https://github.com/d60/twikit/issues/357)、[#397](https://github.com/d60/twikit/issues/397))
- **可选字段缺失时崩溃、用户 `Name` / `ScreenName` 为空** — X 把 `name`、`screen_name`、`created_at`、头像等字段从 `legacy` 移到了新的子对象，现在会读取新位置并回退到 `legacy`，缺失的键视为 null。([#417](https://github.com/d60/twikit/issues/417))
- **`GetTweetByIdAsync` 缺少 `itemContent`**、**没有推文的账户上的 `GetUserTweetsAsync`** — 同时兼容旧版和新版的末尾游标结构，空的、没有游标的时间线会返回空结果。([#332](https://github.com/d60/twikit/issues/332)、[#363](https://github.com/d60/twikit/issues/363)、[#361](https://github.com/d60/twikit/issues/361)、[#216](https://github.com/d60/twikit/issues/216))
- **`GetTrendsAsync` 什么都不返回** — 基于 `GenericTimelineById` 重写，并新增了 `GetExplorePageAsync()`。无限重试以及 news / sports / entertainment 分类返回空的问题也已解决。([#389](https://github.com/d60/twikit/issues/389))
- **`GetLatestFriendsAsync` 的 404** — 在 v1.1 端点下线后，改走 GraphQL 的 `Following` 端点。([#397](https://github.com/d60/twikit/issues/397))
- 修复了**验证码解锁流程**（[#333](https://github.com/d60/twikit/issues/333)）和 **`GetBookmarkFoldersAsync` 分页死循环**（[#334](https://github.com/d60/twikit/issues/334)、[#335](https://github.com/d60/twikit/issues/335)）。
- **`GetLatestTimelineAsync` / `GetListTweetsAsync` 漏掉会话推文** — 现在会展开 home / list 的会话条目。([#336](https://github.com/d60/twikit/issues/336)、[#337](https://github.com/d60/twikit/issues/337)、[#340](https://github.com/d60/twikit/issues/340))
- **分页** — X 不返回游标时，`NextAsync()` 不再反复请求同一页。`count` 会被遵守，X 多给的部分通过 `NextAsync()` 依次返回。
- **回复标签页与私信** — `GetUserTweetsAsync(..., "Replies")` 返回的是对方的推文而不是用户自己的回复；置顶推文被漏掉或重复；`SendDmAsync` 报告了错误的发送者。这些都已修复。
- **不再隐藏 X 的拒绝** — 权限错误（代码 37 等）不再以空结果返回，`CreateTweetAsync` 会原样显示 X 的每日上限提示，找不到的列表 / 用户会抛出 `NotFoundException` / `UserNotFoundException` 而不是返回空对象。
- **通知** — `GetNotificationsAsync("Mentions")` 支持不含 `notifications` 键的新结构。
- **新增属性** — 用于获取原图的 `Media.SourceUrl`（[#376](https://github.com/d60/twikit/issues/376)）、返回被引用推文 ID 的 `Tweet.QuotedStatusId`（[#222](https://github.com/d60/twikit/issues/222)），以及 `Tweet.Url`、`Tweet.Article`、`Tweet.Source`、`Tweet.ConversationIds`、`User.VerifiedType`、恶搞 / 自动化标签、`Conversation`、`Message.ReplyData` 等。
- **端点** — GraphQL 的 query id 与 feature 标志更新到 2026 年的值。

`GuestClient` 也移植了，但游客访问已被 X 关闭，所以 `ActivateAsync()` 会抛出 `InvalidSessionException`：未登录的请求只能拿到一个约 34 KB、不含 webpack 清单的页面外壳，`X-Client-Transaction-Id` 握手无法完成。库层面无法修复，请使用 cookie。([#192](https://github.com/d60/twikit/issues/192)) 同样，由 X 一侧限制引起的问题（账户封禁、Cloudflare / IP 封锁、验证码、自动化限制）也不在本项目范围内。

### 相对上游新增的 API

- `Client.IsLoggedInAsync()` — 检查 cookie 是否仍然有效。
- `Client.RateLimitRemaining` / `Client.RateLimitReset` — 最近一次响应中报告的限流余量和重置时间。
- `Client.RefreshTransaction()` — 重新进行 `X-Client-Transaction-Id` 握手。
- `Client.GetDmInboxAsync()`、`CreateGroupAsync()`、`DeleteDmConversationAsync()` — 私信收件箱、创建群组、删除会话。
- `Client.UpdateProfileAsync()`、`GetAboutAccountAsync()`、`GetUserSpotlightsAsync()`、`GetUserListsAsync()`、`GetMutedUsersAsync()`、`GetBlockedUsersAsync()`。
- `Client.GetUserMentionsAsync()`、`SearchTweetsByDateAsync()`、`GetThreadAsync()`、`GetTweetByUrlAsync()`、`GetExplorePageAsync()`。
- `SearchOptions` / `SearchQuery.Build` — 构建搜索查询。
- `Article`、`Conversation` — 长文与私信会话。
- `Twikit.Spaces` 命名空间 — Spaces 相关的类（`Spaces`、`Space`、`SpaceChat`、`SpaceVoiceSession` 等）。
- 异常：`ClientTransactionException`、`InvalidSessionException`（cookie 被拒绝）、`LoginRetiredException`（密码登录已下线）。

## 与 Python 版的区别

- **没有 `impersonate=`**（通过 curl_cffi 伪装浏览器 TLS 指纹）。.NET 没有内置的对应机制，可以改用 `new Client(handler: ...)` 传入自己的 `HttpMessageHandler`。代理通过 `new Client("zh-cn", proxy: "http://user:pass@127.0.0.1:8080")` 指定（也支持 `socks5://`）。
- **X Spaces 语音（WebRTC）** — .NET 没有内置的 WebRTC 实现，因此库提供了 `IPeerConnection` / `IPeerConnectionFactory` 接口。用 [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) 等实现后传给 `SpeakAsync` / `HostAsync` / `ListenAsync` 即可。元数据、搜索、创建、结束、管理和聊天无需任何实现即可使用。
- **`List` 叫做 `TwitterList`**，以避免与 `System.Collections.Generic.List<T>` 混淆。
- 异常带有 `...Exception` 后缀（如 `NotFoundException`）。基类与 Python 版一样是 `TwitterException`。
- 返回集合的方法返回 `Result<T>`（即 `IReadOnlyList<T>`），用 `NextAsync()` / `PreviousAsync()` 获取前后页。流式推送是 `IAsyncEnumerable`，用 `await foreach` 读取。

其余细小差异见 [docs/articles/python-differences.md](docs/articles/python-differences.md)。

## 文档

文档用 [DocFX](https://dotnet.github.io/docfx/) 生成，以日语编写。API 参考来自源码中的 XML 文档注释。

```bash
dotnet tool install -g docfx
docfx docs/docfx.json --serve   # http://localhost:8080
```

每次推送到 `main` 时，[`.github/workflows/docs.yml`](.github/workflows/docs.yml) 会构建站点并发布到 GitHub Pages（https://amanetoki7.github.io/twikit-dotnet/）。需要先把仓库的 Settings → Pages → Source 设为 "GitHub Actions"。

- [入门](docs/articles/getting-started.md) — 安装、cookie 登录、分页、代理
- [与 Python 版的对应](docs/articles/python-differences.md) — 名称对应表以及 C# 版的变化
- [X Spaces](docs/articles/spaces.md) — 读取、搜索、创建、管理、聊天、语音
- [限流说明](docs/articles/ratelimits.md) / [保护你的账户](docs/articles/protect-account.md) / [错误与错误代码](docs/articles/errors.md)
- 上游 API 参考（Python，英文）：https://twikit.readthedocs.io/en/latest/twikit.html

## 构建与测试

```bash
dotnet build Twikit.sln -c Release
dotnet test tests/Twikit.Tests -c Release
```

测试不访问网络。`X-Client-Transaction-Id` 的计算（动画密钥、`float_to_hex`、三次贝塞尔曲线、与 Python 兼容的取整）会与用 Python 实现生成的 [tests/Twikit.Tests/Fixtures](tests/Twikit.Tests/Fixtures) 逐位比对。每次 push 和 Pull Request 时，[`.github/workflows/dotnet.yml`](.github/workflows/dotnet.yml) 都会执行同样的构建和测试。

## 保护你的账户

本库使用非官方 API，使用不当可能导致账户被封禁。不要发送过多请求、复用 cookie，以及其他注意事项见 [docs/articles/protect-account.md](docs/articles/protect-account.md)（日语）。

## 社区

上游 twikit 的 Discord：[![Discord](https://img.shields.io/badge/Discord-%235865F2.svg?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/nCrByrr8cX)

## 参与贡献

发现 bug 或者有修复方案？欢迎到 [amanetoki7/twikit-dotnet](https://github.com/amanetoki7/twikit-dotnet/issues) 提交 Issue 或 Pull Request。

如果它帮你省了麻烦，欢迎点个 ⭐。

## 致谢

- **[d60/twikit](https://github.com/d60/twikit)**（[@d60](https://github.com/d60)）— 原始实现，全部功劳归原作者所有。
- **[PawiX25/twifork](https://github.com/PawiX25/twifork)**（[@PawiX25](https://github.com/PawiX25) 及贡献者们）— 2026 年的修复与 X Spaces 支持。
- **[amanetoki7/twikit](https://github.com/amanetoki7/twikit)** — 并入上述内容的 Python 分支，本仓库即由它移植而来。
- **[iSarabjitDhiman/TweeterPy](https://github.com/iSarabjitDhiman/TweeterPy)** — `X-Client-Transaction-Id` 计算方法的来源。

以上均基于 **MIT 许可证**发布。

## 免责声明

本项目是一个独立的非官方项目，**与 X Corp. 不存在任何隶属、认可或赞助关系。**"X" 和 "Twitter" 是 X Corp. 的商标。请在遵守相关条款和法律的前提下使用。
