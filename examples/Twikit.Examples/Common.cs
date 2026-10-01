namespace Twikit.Examples;

/// <summary>サンプル共通: Cookie を用意したクライアントを作ります。</summary>
internal static class Common
{
    public const string CookiesFile = "cookies.json";

    public static async Task<Client> CreateClientAsync(string language = "ja")
    {
        var client = new Client(language);
        if (File.Exists(CookiesFile))
        {
            // 2 回目以降は保存した Cookie を読み込む
            client.LoadCookies(CookiesFile);
        }
        else
        {
            // 初回はブラウザから取り出した Cookie（auth_token と ct0）をセットして保存する。
            // パスワードによるログイン（Client.LoginAsync）は X 側で廃止されています。
            var authToken = Environment.GetEnvironmentVariable("AUTH_TOKEN");
            var ct0 = Environment.GetEnvironmentVariable("CT0");
            if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(ct0))
                throw new InvalidOperationException("環境変数 AUTH_TOKEN と CT0 を設定するか、cookies.json を用意してください。");
            client.SetCookies(new Dictionary<string, string> { ["auth_token"] = authToken, ["ct0"] = ct0 });
            client.SaveCookies(CookiesFile);
        }

        if (!await client.IsLoggedInAsync())
            throw new InvalidOperationException("Cookie が無効です。ブラウザから取り直してください。");
        return client;
    }
}
