using Twikit.Api;
using Twikit.Internal;

namespace Twikit;

/// <summary>クリエイター収益化プログラム。</summary>
public enum CreatorProgram
{
    /// <summary>オリジナルコンテンツ報酬（投稿の収益化）。</summary>
    OriginalContentRewards,
    /// <summary>クリエイターサブスクリプション。</summary>
    Subscriptions,
}

/// <summary>
/// クリエイター収益化プログラムの参加条件と、ログイン中のアカウントの達成状況
/// （クリエイタースタジオの「対象 / 対象外」ページの内容）。
/// </summary>
public sealed class CreatorEligibility
{
    /// <summary>どのプログラムか。</summary>
    public CreatorProgram Program { get; }
    /// <summary>X 側のページ名（<c>creators_original_content_rewards</c> など）。</summary>
    public string? Page { get; }
    /// <summary>条件一覧の前に出る説明文（「…次の条件を満たす必要があります:」「残念ながら…」など）。</summary>
    public string? Summary { get; }
    /// <summary>参加条件。ページに並ぶ順。</summary>
    public IReadOnlyList<CreatorRequirement> Requirements { get; }
    /// <summary>条件一覧の外にある補足文（注記やフッターの文）。</summary>
    public IReadOnlyList<string> Notes { get; }
    /// <summary>ヘルプセンターのリンク。</summary>
    public string? HelpUrl { get; }
    /// <summary>X がページを生成した時刻。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>全ての条件を満たしているか。</summary>
    public bool IsEligible => Requirements.Count > 0 && Requirements.All(r => r.IsMet);
    /// <summary>満たしていない条件だけ。</summary>
    public IEnumerable<CreatorRequirement> Unmet => Requirements.Where(r => !r.IsMet);

    public CreatorEligibility(CreatorProgram program, string? page, string? summary, IReadOnlyList<CreatorRequirement> requirements,
        IReadOnlyList<string> notes, string? helpUrl, DateTimeOffset timestamp)
    {
        Program = program;
        Page = page;
        Summary = summary;
        Requirements = requirements;
        Notes = notes;
        HelpUrl = helpUrl;
        Timestamp = timestamp;
    }

    public override string ToString()
        => $"<CreatorEligibility program={Program} met={Requirements.Count(r => r.IsMet)}/{Requirements.Count}>";

    // ------------------------------------------------------------------ parsing

    /// <summary>Jetfuel の応答から組み立てます。</summary>
    internal static CreatorEligibility Parse(CreatorProgram program, ApiResponse response)
        // Verified live 2026-10-23: the body is the raw binary document (Content-Type
        // text/plain, no base64) - the base64 in the fixtures is HAR's encoding of it.
        => Parse(program, JetfuelDocument.Decode(response.Bytes));

    /// <summary>
    /// ページは「View（行）→ Icon（checkmark_circle_fill / close_circle）+ Text（条件文）[+ Grid（進捗バー）+ Text（現在値）]
    /// [+ View（information_circle + Text の注記）]」の並びです。要素の型ではなく「状態アイコンと文を両方含む
    /// 最小の要素」を行とみなすので、入れ子の深さが変わっても追従します。
    /// </summary>
    internal static CreatorEligibility Parse(CreatorProgram program, JetfuelDocument doc)
    {
        var root = doc.Root;
        var header = doc.Int(root, JetfuelIds.Header);
        var page = doc.Prop(root, JetfuelIds.PageInfo)?.Value is Dictionary<string, string> info ? info.GetValueOrDefault("page") : null;

        var requirements = new List<CreatorRequirement>();
        var notes = new List<string>();
        string? summary = null;

        void Walk(JetfuelElement element, int index)
        {
            if (index == header) return;
            if (IsRow(doc, element, IsStatusIcon))
            {
                requirements.Add(ParseRow(doc, element));
                return;
            }
            if (element.Type == JetfuelIds.Text || element.Type == JetfuelIds.RichText)
            {
                var text = TextOf(doc, element);
                if (string.IsNullOrWhiteSpace(text)) return;
                if (summary is null && requirements.Count == 0) summary = text;
                else notes.Add(text);
                return;
            }
            foreach (var child in element.Children)
                if (child >= 0 && child < doc.Elements.Count) Walk(doc.Elements[child], child);
        }

        Walk(root, doc.RootIndex);

        var helpUrl = doc.Props
            .Where(p => p.Kind == JetfuelPropKind.Url && p.Value is string s && s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Select(p => (string)p.Value!)
            .FirstOrDefault();

        return new CreatorEligibility(program, page, summary, requirements, notes, helpUrl,
            DateTimeOffset.FromUnixTimeSeconds(doc.Timestamp));
    }

    private static bool IsStatusIcon(JetfuelDocument doc, JetfuelElement e)
        => e.Type == JetfuelIds.Icon && doc.Int(e, JetfuelIds.IconKey) is JetfuelIds.IconCheckmarkCircleFill or JetfuelIds.IconCloseCircle;

    private static bool IsNoteIcon(JetfuelDocument doc, JetfuelElement e)
        => e.Type == JetfuelIds.Icon && !IsStatusIcon(doc, e);

    private static bool HasText(JetfuelDocument doc, JetfuelElement e)
        => doc.SelfAndDescendants(e).Any(d => d.Type == JetfuelIds.Text || d.Type == JetfuelIds.RichText);

    /// <summary>アイコン（<paramref name="icon"/>）と文を両方含み、どの子要素も単独では両方を含まない要素。</summary>
    private static bool IsRow(JetfuelDocument doc, JetfuelElement e, Func<JetfuelDocument, JetfuelElement, bool> icon)
    {
        if (!doc.SelfAndDescendants(e).Any(d => icon(doc, d)) || !HasText(doc, e)) return false;
        foreach (var child in e.Children)
        {
            if (child < 0 || child >= doc.Elements.Count) continue;
            var c = doc.Elements[child];
            if (doc.SelfAndDescendants(c).Any(d => icon(doc, d)) && HasText(doc, c)) return false;
        }
        return true;
    }

    private static CreatorRequirement ParseRow(JetfuelDocument doc, JetfuelElement row)
    {
        var texts = new List<string>();
        var notes = new List<string>();
        int? iconKey = null;
        int[]? columns = null;
        var valueIndex = -1;

        void Walk(JetfuelElement e)
        {
            if (e != row && IsRow(doc, e, IsNoteIcon))
            {
                var note = string.Join("", doc.SelfAndDescendants(e)
                    .Where(d => d.Type == JetfuelIds.Text || d.Type == JetfuelIds.RichText).Select(d => TextOf(doc, d)));
                if (!string.IsNullOrWhiteSpace(note)) notes.Add(note);
                return;
            }
            if (iconKey is null && IsStatusIcon(doc, e))
            {
                iconKey = doc.Int(e, JetfuelIds.IconKey);
                return;
            }
            if (e.Type == JetfuelIds.Text || e.Type == JetfuelIds.RichText)
            {
                var text = TextOf(doc, e);
                if (!string.IsNullOrWhiteSpace(text)) texts.Add(text);
                return;
            }
            if (e.Type == JetfuelIds.Grid && columns is null)
            {
                columns = doc.Descendants(e)
                    .Where(d => d.Type == JetfuelIds.GridCell)
                    .Select(d => doc.Int(d, JetfuelIds.Columns) ?? 0)
                    .ToArray();
                valueIndex = texts.Count;
                return;
            }
            foreach (var child in e.Children)
                if (child >= 0 && child < doc.Elements.Count) Walk(doc.Elements[child]);
        }

        Walk(row);

        var requirement = texts.Count > 0 ? texts[0] : "";
        string? value = null;
        if (columns is not null && valueIndex >= 1 && valueIndex < texts.Count) value = texts[valueIndex];
        var details = texts.Skip(1).Where(t => t != value).ToList();

        double? progress = null;
        if (columns is { Length: > 0 })
        {
            var total = columns.Sum();
            if (total > 0) progress = (double)columns[0] / total;
        }

        return new CreatorRequirement(requirement, iconKey == JetfuelIds.IconCheckmarkCircleFill, value, progress, columns, details, notes);
    }

    private static string TextOf(JetfuelDocument doc, JetfuelElement e)
    {
        if (e.Type == JetfuelIds.RichText)
            return string.Join("", doc.Descendants(e).Where(d => d.Type == JetfuelIds.RichTextText).Select(d => doc.Str(d, JetfuelIds.TextValue)));
        return doc.Str(e, JetfuelIds.TextValue) ?? "";
    }
}

/// <summary>参加条件 1 件。</summary>
public sealed class CreatorRequirement
{
    /// <summary>条件の文（「認証済みフォロワーが500人以上いる」など）。</summary>
    public string Text { get; }
    /// <summary>満たしているか（チェックマークか × か）。</summary>
    public bool IsMet { get; }
    /// <summary>進捗バーに添えられた現在値の表示（「136」「26.9K」など）。無ければ null。</summary>
    public string? Value { get; }
    /// <summary>進捗バーの割合（0～1）。X は 20 分割で丸めるので目安です。バーが無ければ null。</summary>
    public double? Progress { get; }
    /// <summary>進捗バーの各セルの幅（<c>columns</c>）。先頭が達成分。</summary>
    public IReadOnlyList<int>? ProgressColumns { get; }
    /// <summary>行の中のその他の文。</summary>
    public IReadOnlyList<string> Details { get; }
    /// <summary>行に付いた注記（「返信は含まれません」など）。</summary>
    public IReadOnlyList<string> Notes { get; }

    public CreatorRequirement(string text, bool isMet, string? value, double? progress, IReadOnlyList<int>? progressColumns,
        IReadOnlyList<string> details, IReadOnlyList<string> notes)
    {
        Text = text;
        IsMet = isMet;
        Value = value;
        Progress = progress;
        ProgressColumns = progressColumns;
        Details = details;
        Notes = notes;
    }

    public override string ToString() => $"<CreatorRequirement met={IsMet} \"{Text}\"{(Value is null ? "" : " value=" + Value)}>";
}
