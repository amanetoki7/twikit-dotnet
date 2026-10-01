using System.Collections;

namespace Twikit;

/// <summary>
/// 複数件の結果を保持するクラス。<see cref="NextAsync"/> でさらに結果を取得できます。
/// 通常のリストと同じくインデックスでアクセスでき、foreach で列挙できます。
/// </summary>
/// <remarks>
/// <para>
/// <b>ページ送りは <see cref="NextAsync"/> で行い、<see cref="NextCursor"/> を自分でメソッドに
/// 渡し直さないでください。</b> X はほとんどのエンドポイントで <c>count</c> を無視し、
/// 要求よりはるかに多く（20 件要求して 70 ユーザーなど）返してきます。余剰分はこの
/// オブジェクトの内部に蓄えられ、次のリクエストを送る前に <see cref="NextAsync"/> から
/// 順に返されます。<see cref="NextCursor"/> はすでにその余剰分を飛び越えた位置を指すため、
/// カーソルを渡してメソッドを呼び直すと蓄えた分を丸ごと読み飛ばします。
/// </para>
/// <para>
/// 終了判定はカーソルではなくページの中身で行ってください。カーソルは位置を示すだけで
/// 「続きがある」保証ではなく、X は空ページにもカーソルを返します。<c>while (page.NextCursor != null)</c>
/// は同じ空ページを取り続けますが、<c>while (page.Count > 0)</c> は空ページが返った時点で終わります。
/// </para>
/// </remarks>
/// <typeparam name="T">要素の型。</typeparam>
public sealed class Result<T> : IReadOnlyList<T>
{
    private readonly List<T> _results;
    private readonly Func<Task<Result<T>>>? _fetchNext;
    private readonly Func<Task<Result<T>>>? _fetchPrevious;
    // Items X sent beyond the requested count. Honouring `count` by simply
    // dropping them would skip data, because the cursor already points past
    // everything that arrived, so they are handed out first instead.
    private readonly List<T> _overflow;
    // How many items the caller asked for, so the surplus is handed back
    // in pages of that size rather than in one lump.
    private readonly int? _pageSize;

    /// <summary>次の結果を取得するためのカーソル。蓄えた余剰分を飛び越えた位置を指します（remarks 参照）。</summary>
    public string? NextCursor { get; }

    /// <summary>前の結果を取得するためのカーソル。</summary>
    public string? PreviousCursor { get; }

    /// <summary><see cref="NextCursor"/> の別名。</summary>
    public string? Token => NextCursor;

    /// <summary><see cref="NextCursor"/> の別名。</summary>
    public string? Cursor => NextCursor;

    public Result(
        IEnumerable<T> results,
        Func<Task<Result<T>>>? fetchNextResult = null,
        string? nextCursor = null,
        Func<Task<Result<T>>>? fetchPreviousResult = null,
        string? previousCursor = null,
        IEnumerable<T>? overflow = null,
        int? pageSize = null)
    {
        _results = results as List<T> ?? results.ToList();
        _fetchNext = fetchNextResult;
        NextCursor = nextCursor;
        _fetchPrevious = fetchPreviousResult;
        PreviousCursor = previousCursor;
        _overflow = overflow?.ToList() ?? new List<T>();
        _pageSize = pageSize ?? (_results.Count > 0 ? _results.Count : null);
    }

    /// <summary>次の結果。蓄えた余剰分があればそれを先に返し、無ければ次のページを取得します。</summary>
    public async Task<Result<T>> NextAsync()
    {
        if (_overflow.Count > 0)
        {
            var (page, rest) = Paging.Limited(_overflow, _pageSize);
            return new Result<T>(page, _fetchNext, NextCursor, _fetchPrevious, PreviousCursor, rest, _pageSize);
        }
        if (_fetchNext is null) return Empty();
        return await _fetchNext().ConfigureAwait(false);
    }

    /// <summary>前の結果。</summary>
    public async Task<Result<T>> PreviousAsync()
    {
        if (_fetchPrevious is null) return Empty();
        return await _fetchPrevious().ConfigureAwait(false);
    }

    /// <summary>空の結果。</summary>
    public static Result<T> Empty() => new(new List<T>());

    public T this[int index] => _results[index];

    public int Count => _results.Count;

    public IEnumerator<T> GetEnumerator() => _results.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>この結果のスナップショットをリストとして返します。</summary>
    public List<T> ToList() => new(_results);

    public override string ToString() => "[" + string.Join(", ", _results.Select(r => r?.ToString())) + "]";
}

/// <summary>ページ分割の補助。</summary>
public static class Paging
{
    /// <summary>
    /// 要求件数でページを分割します。今返す先頭部分と <see cref="Result{T}.NextAsync"/> 用に
    /// 取っておく残りを返すので、5 件頼んだ呼び出し側は残りを失わずに 5 件受け取れます。
    /// </summary>
    public static (List<T> Page, List<T> Remaining) Limited<T>(List<T> results, int? count)
    {
        if (count is null || count <= 0 || results.Count <= count)
            return (results, new List<T>());
        return (results.GetRange(0, count.Value), results.GetRange(count.Value, results.Count - count.Value));
    }
}
