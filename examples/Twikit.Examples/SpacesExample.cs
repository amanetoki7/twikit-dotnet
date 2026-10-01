namespace Twikit.Examples;

/// <summary>
/// Python 版 twikit の examples/spaces.py に相当します。
/// 読み取り系（GetSpace、Search、ストリーム URL、チャット履歴）と作成 / 終了は追加パッケージ無しで動作します。
/// WebRTC の音声経路（SpeakAsync / HostAsync / ListenAsync）は IPeerConnectionFactory の実装（SIPSorcery など）が必要です。
/// </summary>
internal static class SpacesExample
{
    public static async Task RunAsync(string[] args)
    {
        // 任意の公開スペースの ID（13 文字の ID、または URL 全体）。
        var spaceId = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SPACE_ID") ?? "1DXGydznBYWKM";

        using var client = await Common.CreateClientAsync();
        var userId = await client.UserIdAsync();
        Console.WriteLine($"ログイン中のユーザー ID: {userId}");

        // 1. メタデータ
        var space = await client.Spaces.GetSpaceAsync(spaceId);
        Console.WriteLine($"space: {space.State} | {space.Title}");
        Console.WriteLine($"  host: {space.HostUserId} | speakers: {space.SpeakerIds.Count}");

        // 2. 配信中のスペースを検索
        var live = await client.Spaces.SearchAsync("music", filter: "Live");
        Console.WriteLine($"配信中のスペースの検索結果: {live.Count} 件");
        foreach (var s in live.Take(3)) Console.WriteLine($"  - {s.Id}");

        // 3. HLS ストリーム URL（ffmpeg で聴取: ffmpeg -i <url> out.m4a）
        if (!string.IsNullOrEmpty(space.MediaKey))
        {
            var stream = await client.Spaces.GetStreamAsync(space.MediaKey);
            Console.WriteLine($"HLS url: {stream.HlsUrl}");
        }

        // 4. チャット履歴（ライブ中でもリプレイでも取得可能）
        try
        {
            var chat = await client.Spaces.ChatAsync(space);
            Console.WriteLine($"chat read_only={chat.ReadOnly} ws={chat.IsLive}");
            foreach (var msg in (await chat.HistoryAsync(limit: 5)).Take(3))
                Console.WriteLine($"  msg: {(msg.Body ?? "")[..Math.Min(60, (msg.Body ?? "").Length)]}");
            await chat.CloseAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine($"チャットは利用できません: {e.Message}");
        }

        // 5. 作成 -> ライブ確認 -> 終了（明示的に有効化したときだけ。外から見える操作です）
        if (Environment.GetEnvironmentVariable("SPACES_CREATE") is not null)
        {
            var created = await client.Spaces.CreateSpaceAsync(title: "twikit spaces demo", conversationControls: 2);
            var createdId = created["broadcast"]?["id"]?.GetValue<string>();
            Console.WriteLine($"作成しました: {createdId}");
            await Task.Delay(3000);
            var liveSpace = await client.Spaces.GetSpaceAsync(createdId!);
            Console.WriteLine($"state: {liveSpace.State} (Running のはず)");
            await client.Spaces.EndSpaceAsync(createdId!);
            Console.WriteLine($"終了しました: {createdId}");
        }

        client.Spaces.Close();
    }
}
