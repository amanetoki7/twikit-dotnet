using System.Text.Json.Nodes;
using Twikit.Api;
using Twikit.Internal;
using Twikit.Transaction;
using Xunit;

namespace Twikit.Tests;

/// <summary>Cross-checks the transaction id math against values produced by the Python implementation.</summary>
public class TransactionTests
{
    private sealed class FixtureSession : IRawHttpSession
    {
        private readonly string _page;
        private readonly string _ondemand;
        public List<string> Urls { get; } = new();

        public FixtureSession(string page, string ondemand)
        {
            _page = page;
            _ondemand = ondemand;
        }

        public Task<HttpResponseMessage> RequestAsync(HttpMethod method, string url, Dictionary<string, string>? headers = null, Dictionary<string, string>? form = null)
        {
            Urls.Add(url);
            var body = url.StartsWith("https://abs.twimg.com/", StringComparison.Ordinal) ? _ondemand : _page;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        var cases = Fixtures.LoadArray("transaction_cases.json");
        for (var i = 0; i < cases.Count; i++) yield return new object[] { i };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task HandshakeAndTransactionIdMatchPython(int index)
    {
        var c = Fixtures.TransactionCase(index);
        var session = new FixtureSession(c["page"]!.GetValue<string>(), c["ondemand"]!.GetValue<string>());
        var ct = new ClientTransaction();
        await ct.InitAsync(session, new Dictionary<string, string>());

        Assert.True(ct.IsInited());
        Assert.Equal(c["ondemandUrl"]!.GetValue<string>(), session.Urls[1]);
        Assert.Equal(c["key"]!.GetValue<string>(), ct.Key);
        Assert.Equal(c["rowIndex"]!.GetValue<int>(), ct.DefaultRowIndex);
        Assert.Equal(c["keyBytesIndices"]!.AsArray().Select(x => x!.GetValue<int>()).ToArray(), ct.DefaultKeyBytesIndices);
        Assert.Equal(c["animationKey"]!.GetValue<string>(), ct.AnimationKey);

        var tid = ct.GenerateTransactionId(
            c["method"]!.GetValue<string>(),
            c["path"]!.GetValue<string>(),
            timeNow: c["timeNow"]!.GetValue<long>(),
            randomNumber: c["randomNumber"]!.GetValue<int>());
        Assert.Equal(c["transactionId"]!.GetValue<string>(), tid);
    }

    [Fact]
    public void GenerateBeforeHandshakeThrowsClearError()
    {
        var ct = new ClientTransaction();
        var e = Assert.Throws<ClientTransactionException>(() => ct.GenerateTransactionId("GET", "/x"));
        Assert.Contains("handshake has not completed", e.Message);
    }

    [Fact]
    public async Task LoggedOutShellRaisesInvalidSession()
    {
        var session = new FixtureSession("<html><head></head><body>logged out</body></html>", "");
        var ct = new ClientTransaction();
        var e = await Assert.ThrowsAsync<InvalidSessionException>(() => ct.InitAsync(session, new Dictionary<string, string>()));
        Assert.Contains("logged-out page shell", e.Message);
        Assert.False(ct.IsInited());
    }

    [Fact]
    public async Task AccountAccessRedirectRaisesAccountLocked()
    {
        var session = new FixtureSession("<html><body><a href=\"/account/access\">unlock</a></body></html>", "");
        var ct = new ClientTransaction();
        await Assert.ThrowsAsync<AccountLockedException>(() => ct.InitAsync(session, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ResetForcesNewHandshake()
    {
        var c = Fixtures.TransactionCase();
        var session = new FixtureSession(c["page"]!.GetValue<string>(), c["ondemand"]!.GetValue<string>());
        var ct = new ClientTransaction();
        await ct.InitAsync(session, new Dictionary<string, string>());
        Assert.True(ct.IsInited());
        ct.Reset();
        Assert.False(ct.IsInited());
        Assert.Null(ct.Key);
        await ct.InitAsync(session, new Dictionary<string, string>());
        Assert.True(ct.IsInited());
        Assert.Equal(4, session.Urls.Count);
    }

    [Fact]
    public async Task ConcurrentInitRunsHandshakeOnce()
    {
        var c = Fixtures.TransactionCase();
        var session = new FixtureSession(c["page"]!.GetValue<string>(), c["ondemand"]!.GetValue<string>());
        var ct = new ClientTransaction();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ct.InitAsync(session, new Dictionary<string, string>())));
        Assert.Equal(2, session.Urls.Count);
    }

    [Fact]
    public void FloatToHexAndRoundMatchPython()
    {
        foreach (var node in Fixtures.LoadArray("float_cases.json"))
        {
            var c = (JsonObject)node!;
            var value = c["value"]!.GetValue<double>();
            var rounded = PyCompat.Round(value, 2);
            Assert.Equal(c["round2"]!.GetValue<double>(), rounded);
            var abs = rounded < 0 ? -rounded : rounded;
            Assert.Equal(c["hex"]!.GetValue<string>(), TransactionMath.FloatToHex(abs));
            Assert.Equal(c["roundInt"]!.GetValue<long>(), PyCompat.RoundToInt(value));
        }
    }

    [Fact]
    public void CubicMatchesPython()
    {
        foreach (var node in Fixtures.LoadArray("cubic_cases.json"))
        {
            var c = (JsonObject)node!;
            var curves = c["curves"]!.AsArray().Select(x => x!.GetValue<double>()).ToList();
            var expected = c["value"]!.GetValue<double>();
            var actual = new Cubic(curves).GetValue(c["time"]!.GetValue<double>());
            Assert.Equal(expected, actual, 12);
        }
    }

    [Fact]
    public void SolveMatchesPython()
    {
        foreach (var node in Fixtures.LoadArray("solve_cases.json"))
        {
            var c = (JsonObject)node!;
            var actual = ClientTransaction.Solve(c["value"]!.GetValue<double>(), c["min"]!.GetValue<double>(),
                c["max"]!.GetValue<double>(), c["rounding"]!.GetValue<bool>());
            Assert.Equal(c["result"]!.GetValue<double>(), actual);
        }
    }

    [Fact]
    public void RotationMatrixIsCosSinSinCos()
    {
        var m = TransactionMath.ConvertRotationToMatrix(90);
        Assert.Equal(0, m[0], 12);
        Assert.Equal(-1, m[1], 12);
        Assert.Equal(1, m[2], 12);
        Assert.Equal(0, m[3], 12);
    }

    [Fact]
    public async Task MigrationFormIsSubmitted()
    {
        // twitter.com -> x.com migration: the first page carries a form, the POST answers the real home page.
        var c = Fixtures.TransactionCase();
        var page = c["page"]!.GetValue<string>();
        var calls = new List<(string Url, Dictionary<string, string>? Form)>();
        var session = new DelegateSession((method, url, headers, form) =>
        {
            calls.Add((url, form));
            if (url == "https://x.com")
                return "<html><body><form name=\"f\" action=\"https://x.com/x/migrate\" method=\"post\"><input name=\"tok\" value=\"abc\"/></form></body></html>";
            if (url.StartsWith("https://x.com/x/migrate", StringComparison.Ordinal)) return page;
            return c["ondemand"]!.GetValue<string>();
        });
        var home = await HomePage.HandleXMigrationAsync(session, new Dictionary<string, string>());
        Assert.Equal("https://x.com/x/migrate/?mx=2", calls[1].Url);
        Assert.Equal("abc", calls[1].Form!["tok"]);
        Assert.NotNull(home.Document.QuerySelector("[name='twitter-site-verification']"));
    }

    private sealed class DelegateSession : IRawHttpSession
    {
        private readonly Func<HttpMethod, string, Dictionary<string, string>?, Dictionary<string, string>?, string> _f;
        public DelegateSession(Func<HttpMethod, string, Dictionary<string, string>?, Dictionary<string, string>?, string> f) => _f = f;
        public Task<HttpResponseMessage> RequestAsync(HttpMethod method, string url, Dictionary<string, string>? headers = null, Dictionary<string, string>? form = null)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(_f(method, url, headers, form)) });
    }
}
