using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// オンボーディング（ログイン）フローの状態。
/// </summary>
public sealed class Flow
{
    private readonly Client _client;

    /// <summary>ゲストトークン。</summary>
    public string GuestToken { get; }

    /// <summary>直近のタスク応答。</summary>
    public JsonObject? Response { get; private set; }

    public Flow(Client client, string guestToken)
    {
        _client = client;
        GuestToken = guestToken;
    }

    /// <summary>サブタスクを実行します。</summary>
    public async Task ExecuteTaskAsync(JsonArray? subtaskInputs = null, Dictionary<string, string>? parameters = null, JsonObject? data = null)
    {
        var response = await _client.V11.OnboardingTaskAsync(GuestToken, Token, subtaskInputs, data, parameters).ConfigureAwait(false);
        Response = response.Object;
    }

    /// <summary>1 つのサブタスク入力でタスクを実行します。</summary>
    public Task ExecuteTaskAsync(JsonObject subtaskInput) => ExecuteTaskAsync(new JsonArray(subtaskInput));

    public Task SsoInitAsync(string provider) => _client.V11.SsoInitAsync(provider, GuestToken);

    /// <summary>フロートークン。</summary>
    public string? Token => Response?.Str("flow_token");

    /// <summary>現在のサブタスク ID。</summary>
    public string? TaskId
    {
        get
        {
            var subtasks = Response?.Arr("subtasks");
            if (subtasks is null || subtasks.Count == 0) return null;
            return subtasks[0].Str("subtask_id");
        }
    }
}
