using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Twikit;

/// <summary>
/// <see cref="JsonNode"/> を Python の dict のように扱うためのヘルパー。
/// 欠けたキーは例外ではなく null として読みます（X は任意フィールドを平気で省くため）。
/// </summary>
public static class JsonExtensions
{
    /// <summary>オブジェクトのプロパティを安全に読みます。オブジェクトでなければ null。</summary>
    public static JsonNode? Get(this JsonNode? node, string key)
        => node is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null;

    /// <summary>配列の要素を安全に読みます。Python 同様に負のインデックスは末尾からの位置です。</summary>
    public static JsonNode? At(this JsonNode? node, int index)
    {
        if (node is not JsonArray a) return null;
        if (index < 0) index += a.Count;
        return index >= 0 && index < a.Count ? a[index] : null;
    }

    public static bool Has(this JsonNode? node, string key) => node is JsonObject o && o.ContainsKey(key);

    public static JsonObject? Obj(this JsonNode? node, string key) => Get(node, key) as JsonObject;

    /// <summary>
    /// <c>legacy</c> の隣に X が置くようになったネストしたプロファイルオブジェクトを読みます。
    /// v1.1 のペイロードは <c>location</c> を同じ名前で文字列として持つため、
    /// マッピングでない値は「無い」ものとして扱い、呼び出し側が <c>legacy</c> にフォールバックできるようにします。
    /// </summary>
    public static JsonObject Sub(this JsonNode? node, string key) => Get(node, key) as JsonObject ?? new JsonObject();

    public static JsonArray? Arr(this JsonNode? node, string key) => Get(node, key) as JsonArray;

    public static JsonArray ArrOrEmpty(this JsonNode? node, string key) => Get(node, key) as JsonArray ?? new JsonArray();

    public static string? Str(this JsonNode? node, string key) => Get(node, key).AsStr();

    public static long? Long(this JsonNode? node, string key) => Get(node, key).AsLong();

    public static int? Int(this JsonNode? node, string key) => Get(node, key).AsInt();

    public static double? Double(this JsonNode? node, string key) => Get(node, key).AsDouble();

    public static bool? Bool(this JsonNode? node, string key) => Get(node, key).AsBool();

    public static bool BoolOr(this JsonNode? node, string key, bool fallback) => Get(node, key).AsBool() ?? fallback;

    public static int IntOr(this JsonNode? node, string key, int fallback) => Get(node, key).AsInt() ?? fallback;

    /// <summary>値を文字列として読みます。数値・真偽値は文字列化し、オブジェクト・配列・null は null。</summary>
    public static string? AsStr(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<string>(out var s)) return s;
        if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        if (v.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetValue<double>(out var d)) return d.ToString("R", CultureInfo.InvariantCulture);
        if (v.TryGetValue<decimal>(out var m)) return m.ToString(CultureInfo.InvariantCulture);
        if (v.TryGetValue<JsonElement>(out var element))
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null,
            };
        }
        return v.ToJsonString().Trim('"');
    }

    public static long? AsLong(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<decimal>(out var m)) return (long)m;
        if (v.TryGetValue<string>(out var s))
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        if (v.TryGetValue<JsonElement>(out var e))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var el)) return el;
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var ed)) return (long)ed;
            if (e.ValueKind == JsonValueKind.String && long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var es)) return es;
        }
        return null;
    }

    public static int? AsInt(this JsonNode? node)
    {
        var l = node.AsLong();
        return l is null ? null : (int)l.Value;
    }

    public static double? AsDouble(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<decimal>(out var m)) return (double)m;
        if (v.TryGetValue<string>(out var s))
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        if (v.TryGetValue<JsonElement>(out var e))
        {
            if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var ed)) return ed;
            if (e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var es)) return es;
        }
        return null;
    }

    public static bool? AsBool(this JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<JsonElement>(out var e))
        {
            if (e.ValueKind == JsonValueKind.True) return true;
            if (e.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    /// <summary>Python の真偽判定: null / false / 0 / "" / 空のオブジェクト・配列は偽。</summary>
    public static bool IsTruthy(this JsonNode? node)
    {
        switch (node)
        {
            case null: return false;
            case JsonObject o: return o.Count > 0;
            case JsonArray a: return a.Count > 0;
            case JsonValue v:
                if (v.TryGetValue<bool>(out var b)) return b;
                if (v.TryGetValue<string>(out var s)) return s.Length > 0;
                var d = v.AsDouble();
                if (d is not null) return d.Value != 0;
                if (v.TryGetValue<JsonElement>(out var e))
                {
                    return e.ValueKind switch
                    {
                        JsonValueKind.Null => false,
                        JsonValueKind.False => false,
                        JsonValueKind.True => true,
                        JsonValueKind.String => e.GetString()!.Length > 0,
                        JsonValueKind.Number => e.GetDouble() != 0,
                        _ => true,
                    };
                }
                return true;
        }
        return true;
    }

    /// <summary>
    /// ネストした JSON から <paramref name="key"/> を持つ要素を深さ優先で集めます
    /// （Python 版の <c>find_dict</c>）。<paramref name="findOne"/> なら最初の 1 件で打ち切ります。
    /// </summary>
    public static List<JsonNode?> FindDict(this JsonNode? obj, string key, bool findOne = false)
    {
        var results = new List<JsonNode?>();
        FindDictInto(obj, key, findOne, results);
        return results;
    }

    private static bool FindDictInto(JsonNode? obj, string key, bool findOne, List<JsonNode?> results)
    {
        if (obj is JsonObject o)
        {
            if (o.TryGetPropertyValue(key, out var direct))
            {
                results.Add(direct);
                if (findOne) return true;
            }
            foreach (var kv in o)
            {
                var before = results.Count;
                var done = FindDictInto(kv.Value, key, findOne, results);
                if (done && findOne && results.Count > before) return true;
            }
        }
        else if (obj is JsonArray a)
        {
            foreach (var elem in a)
            {
                var before = results.Count;
                var done = FindDictInto(elem, key, findOne, results);
                if (done && findOne && results.Count > before) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// <see cref="FindDict"/> が最初に見つけた値、無ければ null（Python 版の <c>first_dict</c>）。
    /// <c>find_dict(...)[0]</c> を直接添字で読んでいたのが "list index out of range" の主因でした。
    /// </summary>
    public static JsonNode? FirstDict(this JsonNode? data, string key)
    {
        var found = data.FindDict(key, findOne: true);
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>末尾のタイムラインエントリが持つカーソル値、無ければ null。</summary>
    public static string? LastCursor(JsonArray? entries)
    {
        if (entries is null || entries.Count == 0) return null;
        var content = entries[^1].Get("content");
        if (content is not JsonObject) return null;
        var value = content.Str("value");
        if (value is not null) return value;
        var itemContent = content.Get("itemContent");
        return itemContent is JsonObject ? itemContent.Str("value") : null;
    }

    /// <summary>位置 <paramref name="index"/>（負数は末尾から）のエントリのカーソル値、無ければ null。</summary>
    public static string? CursorAt(JsonArray? entries, int index)
    {
        if (entries is null || entries.Count == 0) return null;
        var entry = entries.At(index);
        if (entry is null) return null;
        var content = entry.Get("content");
        return content is JsonObject ? content.Str("value") : null;
    }

    /// <summary>
    /// GraphQL レスポンスを実際に沈めたエラーの一覧、なければ null（Python 版の <c>fatal_errors</c>）。
    /// </summary>
    /// <param name="response">レスポンス全体。</param>
    /// <param name="required">
    /// 呼び出し側が <c>data</c> から必要とするキー。拒否応答は <c>data</c> に殻だけを
    /// 入れて返ってくることがあるため、このキーが見つからなければエラーとして扱います。
    /// 成功時にしか現れないキーを選ぶこと（<c>rest_id</c> のような汎用名は殻にも含まれる）。
    /// </param>
    public static List<JsonObject>? FatalErrors(JsonNode? response, string? required = null)
    {
        if (response is not JsonObject r) return null;
        if (r.Get("errors") is not JsonArray errorsRaw || errorsRaw.Count == 0) return null;
        // X occasionally sends a non-object entry, and every caller reads
        // errors[0]["message"] - normalise here so none of them has to guard.
        var errors = new List<JsonObject>();
        foreach (var e in errorsRaw)
        {
            if (e is JsonObject eo) errors.Add(eo);
            else errors.Add(new JsonObject { ["message"] = e.AsStr() ?? e?.ToJsonString() ?? "None" });
        }
        var data = r.Get("data");
        if (!data.IsTruthy()) return errors;
        if (required is not null && data.FindDict(required, findOne: true).Count == 0) return errors;
        return null;
    }

    /// <summary>先頭エラーのメッセージ（無ければ <paramref name="fallback"/>）。</summary>
    public static string ErrorMessage(this List<JsonObject> errors, string fallback)
        => errors.Count > 0 ? (errors[0].Str("message") ?? fallback) : fallback;

    /// <summary><c>type</c> が一致する最初のエントリ（Python 版の <c>find_entry_by_type</c>）。</summary>
    public static JsonObject? FindEntryByType(JsonArray? entries, string typeFilter)
    {
        if (entries is null) return null;
        foreach (var entry in entries)
            if (entry is JsonObject o && o.Str("type") == typeFilter) return o;
        return null;
    }

    /// <summary>エントリの <c>entryId</c>（無ければ空文字）。</summary>
    public static string EntryId(this JsonNode? entry) => entry.Str("entryId") ?? "";

    /// <summary>親から切り離した深いコピー（JsonNode は親を 1 つしか持てません）。</summary>
    public static JsonNode? Clone(this JsonNode? node) => node?.DeepClone();

    public static JsonObject? CloneObj(this JsonNode? node) => node is JsonObject o ? (JsonObject)o.DeepClone() : null;

    /// <summary>配列の要素を JsonObject として列挙します（それ以外はスキップ）。</summary>
    public static IEnumerable<JsonObject> Objects(this JsonArray? array)
    {
        if (array is null) yield break;
        foreach (var item in array)
            if (item is JsonObject o) yield return o;
    }

    /// <summary>文字列の配列に変換します（文字列でない要素はスキップ）。</summary>
    public static List<string> Strings(this JsonNode? array)
    {
        var list = new List<string>();
        if (array is not JsonArray a) return list;
        foreach (var item in a)
        {
            var s = item.AsStr();
            if (s is not null) list.Add(s);
        }
        return list;
    }

    /// <summary>文字列のシーケンスから JsonArray を作ります。</summary>
    public static JsonArray ToJsonArray(this IEnumerable<string> values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(v);
        return a;
    }

    /// <summary>JSON テキストを解析します。JSON でなければ null。</summary>
    public static JsonNode? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Python の <c>json.dumps</c> 相当（コンパクト出力）。</summary>
    public static string Dump(this JsonNode? node) => node is null ? "null" : node.ToJsonString();
}
