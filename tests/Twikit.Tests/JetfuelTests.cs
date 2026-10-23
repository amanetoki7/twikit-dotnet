using System.Net;
using System.Text.Json.Nodes;
using Twikit.Api;
using Twikit.Internal;
using Xunit;

namespace Twikit.Tests;

/// <summary>
/// Jetfuel（/i/jfapi）のバイナリ文書と、それを読むクリエイター収益化 API。
/// フィクスチャは 2026-10-22 の HAR から取った本文（HAR が base64 にしたもの）。X 自身は生のバイナリを
/// text/plain で返す（2026-10-23 に実測）ので、フェイクも復号したバイト列を返す。
/// </summary>
public class JetfuelTests
{
    private static readonly JsonObject Cases = (JsonObject)JsonNode.Parse(File.ReadAllText(Fixtures.Path("jetfuel_cases.json")))!;

    private static string Body(string name) => Cases[name]!["body"]!.GetValue<string>();

    private static FakeHandler JfHandler(params (string PathSuffix, string Case)[] routes) => Fixtures.HandshakeHandler((request, _) =>
    {
        var path = request.RequestUri!.AbsolutePath;
        foreach (var (suffix, name) in routes)
            if (path.EndsWith(suffix, StringComparison.Ordinal)) return FakeHandler.Bytes(Convert.FromBase64String(Body(name)));
        return FakeHandler.Json("{}", HttpStatusCode.NotFound);
    });

    [Fact]
    public void DecodesTheWholeFrame()
    {
        var doc = JetfuelDocument.Decode(Body("original_content_rewards"));
        Assert.Equal(56, doc.Elements.Count);
        Assert.Equal(60, doc.Props.Count);
        Assert.Equal(1792670996, doc.Timestamp);
        Assert.Equal(55, doc.RootIndex); // from the dom_ready frame
        Assert.Equal(JetfuelIds.Page, doc.Root.Type);
        var info = Assert.IsType<Dictionary<string, string>>(doc.Prop(doc.Root, JetfuelIds.PageInfo)!.Value);
        Assert.Equal("creators_original_content_rewards", info["page"]);
        Assert.Equal("eligibility", info["section"]);

        // The toolbar is reachable only through the `header` prop, and its back button carries a navigate action.
        var header = doc.Elements[doc.Int(doc.Root, JetfuelIds.Header)!.Value];
        var actions = doc.SelfAndDescendants(header)
            .SelectMany(e => e.Props.Select(p => doc.Props[p.Value]))
            .Where(p => p.Kind == JetfuelPropKind.Action)
            .Select(p => (JetfuelAction)p.Value!)
            .ToList();
        Assert.Contains(actions, a => a.Navigate == JetfuelNavigateKind.Link
            && doc.Props[a.UrlProp!.Value].Value is "https://help.x.com/using-x/original-content-rewards");
    }

    [Fact]
    public void DecodesPagesWithOtherPropKinds()
    {
        // The studio page mixes page/link navigations, str_dicts and bools: every byte has to be accounted for.
        var doc = JetfuelDocument.Decode(Body("studio"));
        Assert.Equal(91, doc.Elements.Count);
        Assert.Equal(90, doc.RootIndex);
        Assert.Equal(JetfuelIds.Page, doc.Root.Type);
        var urls = doc.Props.Where(p => p.Kind == JetfuelPropKind.Url).Select(p => (string)p.Value!).ToList();
        Assert.Contains("/creators/original_content_rewards", urls);
        Assert.Contains("/creators/subscriptionsv2/eligibility", urls);

        doc = JetfuelDocument.Decode(Body("subscriptions_eligibility"));
        Assert.Equal(39, doc.Elements.Count);
        Assert.Equal(39, doc.Props.Count);
    }

    [Fact]
    public void RejectsBodiesThatAreNotJetfuel()
    {
        Assert.Throws<TwitterException>(() => JetfuelDocument.Decode("{\"errors\":[{\"code\":32}]}"));
        Assert.Throws<TwitterException>(() => JetfuelDocument.Decode(Array.Empty<byte>()));
        // A truncated frame must fail loudly instead of returning a half page.
        var bytes = Convert.FromBase64String(Body("subscriptions_eligibility"));
        Assert.Throws<TwitterException>(() => JetfuelDocument.Decode(bytes.Take(bytes.Length / 2).ToArray()));
    }

    [Fact]
    public async Task OriginalContentRewardsEligibility()
    {
        var handler = JfHandler(("/i/jfapi/creators/original_content_rewards", "original_content_rewards"));
        using var client = Fixtures.NewClient(handler);

        var eligibility = await client.GetOriginalContentRewardsEligibilityAsync();

        var request = handler.Requests.Single(r => r.Request.RequestUri!.AbsolutePath.EndsWith("/original_content_rewards", StringComparison.Ordinal)).Request;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://x.com/i/jfapi/creators/original_content_rewards", request.RequestUri!.ToString());
        Assert.Equal("JP-5", request.Headers.GetValues("x-jf-v").Single());
        Assert.Equal("light", request.Headers.GetValues("x-jf-client-theme").Single());
        Assert.Equal("https://x.com/i/jf/creators/original_content_rewards", request.Headers.GetValues("Referer").Single());
        Assert.Equal("csrf456", request.Headers.GetValues("X-Csrf-Token").Single());
        Assert.NotEmpty(request.Headers.GetValues("X-Client-Transaction-Id").Single());

        Assert.Equal(CreatorProgram.OriginalContentRewards, eligibility.Program);
        Assert.Equal("creators_original_content_rewards", eligibility.Page);
        Assert.Equal("オリジナルコンテンツ報酬プログラムに参加して、投稿から収益を得るまであと少しです。登録するには、次の条件を満たす必要があります:", eligibility.Summary);
        Assert.Equal("https://help.x.com/using-x/original-content-rewards", eligibility.HelpUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 12, 9, 56, TimeSpan.Zero), eligibility.Timestamp);
        Assert.False(eligibility.IsEligible);
        Assert.Equal(new[] { "オリジナルコンテンツ報酬プログラムの詳細はここをクリックをご覧ください。" }, eligibility.Notes);

        var r = eligibility.Requirements;
        Assert.Equal(4, r.Count);
        Assert.Equal(new[]
        {
            "Premium、Premium+、またはPremium Businessに登録",
            "18歳以上",
            "認証済みフォロワーが500人以上いる",
            "過去90日間でホームタイムラインの認証済みインプレッションが50万回以上",
        }, r.Select(x => x.Text));
        Assert.Equal(new[] { true, true, false, false }, r.Select(x => x.IsMet));
        Assert.Equal(new[] { null, null, "136", "26.9K" }, r.Select(x => x.Value));
        Assert.Equal(new double?[] { null, null, 0.25, 0.05 }, r.Select(x => x.Progress));
        Assert.Equal(new[] { 5, 15 }, r[2].ProgressColumns);
        Assert.Equal(new[] { 1, 19 }, r[3].ProgressColumns);
        Assert.All(r.Take(3), x => Assert.Empty(x.Notes));
        Assert.Equal(new[] { "返信は含まれません" }, r[3].Notes);
        Assert.All(r, x => Assert.Empty(x.Details));
        Assert.Equal(r.Skip(2), eligibility.Unmet);
    }

    [Fact]
    public async Task CreatorSubscriptionsEligibility()
    {
        var handler = JfHandler(("/i/jfapi/creators/subscriptionsv2/eligibility", "subscriptions_eligibility"));
        using var client = Fixtures.NewClient(handler);

        var eligibility = await client.GetCreatorSubscriptionsEligibilityAsync();

        Assert.Equal(CreatorProgram.Subscriptions, eligibility.Program);
        Assert.Equal("subscriptionsv2_eligibility", eligibility.Page);
        Assert.Equal("残念ながら、現時点ではサブスクリプションの利用資格を満たしていません。", eligibility.Summary);
        Assert.Equal("https://help.x.com/using-x/subscriptions-creator", eligibility.HelpUrl);
        Assert.False(eligibility.IsEligible);
        Assert.Empty(eligibility.Notes);

        var r = eligibility.Requirements;
        Assert.Equal(new[]
        {
            "Premiumに登録して認証を取得",
            "認証済みフォロワーが2,000人以上",
            "過去3ヶ月でオーガニックインプレッションが500万回以上",
            "直近30日間、Xで活動",
            "18歳以上",
            "𝕏 Moneyの対象",
        }, r.Select(x => x.Text));
        Assert.Equal(new[] { true, false, false, true, true, true }, r.Select(x => x.IsMet));
        Assert.All(r, x => Assert.Null(x.Value));
        Assert.All(r, x => Assert.Null(x.Progress));
        Assert.All(r, x => Assert.Empty(x.Notes));
        Assert.Equal(2, eligibility.Unmet.Count());
    }

    [Fact]
    public async Task JetfuelClientAcceptsRelativePaths()
    {
        var handler = JfHandler(("/i/jfapi/creators/studio", "studio"));
        using var client = Fixtures.NewClient(handler);
        client.Jetfuel.Theme = "dark";

        var response = await client.Jetfuel.GetAsync("creators/studio");
        Assert.Equal(200, response.StatusCode);
        Assert.Null(response.Json); // binary, not JSON
        Assert.Equal(Convert.FromBase64String(Body("studio")), response.Bytes);
        var request = handler.Requests.Last().Request;
        Assert.Equal(JetfuelEndpoint.CreatorsStudio, request.RequestUri!.ToString());
        Assert.Equal("dark", request.Headers.GetValues("x-jf-client-theme").Single());

        var doc = JetfuelDocument.Decode(response.Bytes);
        Assert.Equal(91, doc.Elements.Count);
        // Reading the binary through Text (UTF-8) is lossy - the old base64 path - and must not be used.
        Assert.Throws<TwitterException>(() => JetfuelDocument.Decode(response.Text));
    }
}
