using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Twikit.Api;

namespace Twikit.Tests;

/// <summary>Records requests and answers them through a routing function.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = new();
    public Func<HttpRequestMessage, string?, HttpResponseMessage> Route { get; set; }

    public FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> route)
    {
        Route = route;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return Route(request, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    public static HttpResponseMessage Html(string html, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    public static HttpResponseMessage Text(string text, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };

    public string? Header(int index, string name)
        => Requests[index].Request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}

public static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static JsonArray LoadArray(string name) => (JsonArray)JsonNode.Parse(File.ReadAllText(Path(name)))!;

    /// <summary>A transaction fixture case: the synthetic x.com page and its ondemand.s chunk.</summary>
    public static JsonObject TransactionCase(int index = 0) => (JsonObject)LoadArray("transaction_cases.json")[index]!;

    /// <summary>Routes the handshake (x.com home page + ondemand.s) using fixture case 0, then the given API router.</summary>
    public static FakeHandler HandshakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> api)
    {
        var c = TransactionCase();
        var page = c["page"]!.GetValue<string>();
        var ondemand = c["ondemand"]!.GetValue<string>();
        return new FakeHandler((request, body) =>
        {
            var url = request.RequestUri!.ToString();
            if (url == "https://x.com/" || url == "https://x.com") return FakeHandler.Html(page);
            if (url.StartsWith("https://abs.twimg.com/", StringComparison.Ordinal)) return FakeHandler.Text(ondemand);
            return api(request, body);
        });
    }

    public static Client NewClient(FakeHandler handler, bool withCookies = true)
    {
        var client = new Client("ja", handler: handler);
        if (withCookies)
            client.SetCookies(new Dictionary<string, string> { ["auth_token"] = "auth123", ["ct0"] = "csrf456" });
        return client;
    }
}
