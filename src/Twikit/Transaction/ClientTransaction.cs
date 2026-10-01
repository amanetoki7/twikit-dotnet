using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Twikit.Api;
using Twikit.Internal;

namespace Twikit.Transaction;

/// <summary>
/// <c>X-Client-Transaction-Id</c> ヘッダーの生成。
/// </summary>
/// <remarks>
/// <para>
/// このプロジェクトには次のオープンソースプロジェクトのコードが含まれています:
/// https://github.com/iSarabjitDhiman/TweeterPy — 原作者の尽力に深く感謝します。
/// </para>
/// <para>
/// ホームページの <c>twitter-site-verification</c> メタタグと <c>loading-x-anim-*</c> の SVG、
/// および webpack の <c>ondemand.s</c> チャンクから読み取ったインデックスを組み合わせて
/// 「アニメーションキー」を計算し、リクエストごとにメソッド・パス・時刻を混ぜた ID を作ります。
/// </para>
/// </remarks>
public sealed class ClientTransaction
{
    public const int AdditionalRandomNumber = 3;
    public const string DefaultKeyword = "obfiowerehiring";

    private static readonly Regex OnDemandFileRegex = new(@",(\d+):[""']ondemand\.s[""']", RegexOptions.Compiled | RegexOptions.Multiline);
    private const string OnDemandHashPattern = @",{0}:""([0-9a-f]+)""";
    private static readonly Regex IndicesRegex = new(@"\[(\d+)\],\s*16", RegexOptions.Compiled);
    private static readonly Regex NonDigits = new(@"[^\d]+", RegexOptions.Compiled);
    private static readonly Regex DotOrDash = new(@"[.-]", RegexOptions.Compiled);

    /// <summary>ondemand.s から読んだ行インデックス（キーバイトの添字）。</summary>
    public int? DefaultRowIndex { get; private set; }

    /// <summary>ondemand.s から読んだフレーム時間計算用のキーバイト添字。</summary>
    public int[]? DefaultKeyBytesIndices { get; private set; }

    /// <summary>twitter-site-verification のキー。</summary>
    public string? Key { get; private set; }

    public byte[]? KeyBytes { get; private set; }

    public string? AnimationKey { get; private set; }

    /// <summary>ハンドシェイクで取得したホームページ。</summary>
    public HomePage? HomePageResponse { get; private set; }

    private bool _inited;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    // Bumped by Reset(). A handshake that started before the reset finishes
    // against the previous session, so its result has to be discarded.
    private int _generation;

    /// <summary>
    /// ハンドシェイクを忘れ、次のリクエストでやり直させます。
    /// </summary>
    /// <remarks>
    /// キーは X が数日ごとに入れ替える webpack バンドルに由来し、取得したセッションにも
    /// 紐付いています。Cookie を入れ替えた後も古いキーで作った ID は X に黙って 404 を返されます。
    /// </remarks>
    public void Reset()
    {
        _inited = false;
        Interlocked.Increment(ref _generation);
        Key = null;
        KeyBytes = null;
        AnimationKey = null;
        // GetIndicesAsync() falls back to this when the fresh page cannot be
        // parsed, so leaving it behind let a page fetched by the previous
        // session be reused after the reset.
        HomePageResponse = null;
    }

    /// <summary>ハンドシェイクが完了しているか。</summary>
    public bool IsInited() => _inited;

    /// <summary>ハンドシェイクを実行します（同時呼び出しは直列化されます）。</summary>
    public async Task InitAsync(IRawHttpSession session, Dictionary<string, string> headers)
    {
        // Serialise concurrent callers: without this every in-flight request
        // runs its own handshake (N requests -> N home page + ondemand.s fetches).
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Looped because a Reset() can land while the handshake is in
            // flight; see the generation check below.
            while (!_inited)
            {
                var generation = Volatile.Read(ref _generation);
                var homePage = ValidateResponse(await HomePage.HandleXMigrationAsync(session, headers).ConfigureAwait(false));
                // Everything is computed into locals first: partial state must
                // never be published, otherwise a failure here leaves the
                // object wedged forever. GetAnimationKey() reads DefaultRowIndex /
                // DefaultKeyBytesIndices off this, so those two land before it runs.
                var (rowIndex, keyBytesIndices) = await GetIndicesAsync(homePage, session, headers).ConfigureAwait(false);
                DefaultRowIndex = rowIndex;
                DefaultKeyBytesIndices = keyBytesIndices;
                var key = GetKey(homePage);
                var keyBytes = GetKeyBytes(key);
                var animationKey = GetAnimationKey(keyBytes, homePage);

                if (generation != Volatile.Read(ref _generation))
                {
                    // The cookies changed while this handshake was running, so
                    // these keys belong to an account that is no longer current.
                    // Redo the handshake so the caller still gets a usable id.
                    continue;
                }

                // Published last and together: IsInited() is the only gate the
                // client checks, so nothing here may be visible before it flips.
                HomePageResponse = homePage;
                Key = key;
                KeyBytes = keyBytes;
                AnimationKey = animationKey;
                _inited = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// ホームページの webpack チャンクマップから <c>ondemand.s</c> のハッシュを見つけ、
    /// そのファイルから KEY_BYTE のインデックスを読みます。
    /// </summary>
    public async Task<(int RowIndex, int[] KeyBytesIndices)> GetIndicesAsync(HomePage? homePage, IRawHttpSession session, Dictionary<string, string> headers)
    {
        var page = ValidateResponse(homePage ?? HomePageResponse);
        var responseStr = page.Text;
        var onDemandFile = OnDemandFileRegex.Match(responseStr);
        if (!onDemandFile.Success)
        {
            // Three unrelated causes used to share one message here, each
            // needing a different fix: refresh the cookies, deal with the
            // account, or update the parser. Report which one it is.
            if (!responseStr.Contains("ondemand.s", StringComparison.Ordinal))
            {
                // A restricted account is bounced to /account/access instead,
                // which needs a completely different fix from expired cookies
                // and so must not be reported as an invalid session.
                if (responseStr.Contains("/account/access", StringComparison.Ordinal))
                {
                    throw new AccountLockedException(
                        "x.com redirected to /account/access instead of serving the app, so the " +
                        "X-Client-Transaction-Id handshake cannot be performed. The account is restricted - open " +
                        $"https://{Constants.Domain}/account/access in a browser to see whether it is locked or suspended.");
                }
                throw new InvalidSessionException(
                    $"x.com returned the logged-out page shell ({responseStr.Length} bytes, no webpack manifest), so the " +
                    "X-Client-Transaction-Id handshake cannot be performed. The cookies are most likely missing, expired or rejected " +
                    "- log in again and refresh them.");
            }
            throw new ClientTransactionException(
                "Couldn't locate the ondemand.s chunk id in the page source (the webpack chunk map layout changed).");
        }
        var onDemandFileIndex = onDemandFile.Groups[1].Value;
        var hashRegex = new Regex(string.Format(CultureInfo.InvariantCulture, OnDemandHashPattern, onDemandFileIndex));
        var hashMatch = hashRegex.Match(responseStr);
        if (!hashMatch.Success)
            throw new ClientTransactionException($"Couldn't find the ondemand.s hash for chunk id {onDemandFileIndex}.");
        var filename = hashMatch.Groups[1].Value;
        var onDemandFileUrl = $"https://abs.twimg.com/responsive-web/client-web/ondemand.s.{filename}a.js";
        var onDemandResponse = await session.RequestAsync(HttpMethod.Get, onDemandFileUrl, headers).ConfigureAwait(false);
        var onDemandText = await onDemandResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        var indices = new List<int>();
        foreach (Match m in IndicesRegex.Matches(onDemandText))
            indices.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        if (indices.Count == 0)
            throw new ClientTransactionException("Couldn't get KEY_BYTE indices");
        return (indices[0], indices.Skip(1).ToArray());
    }

    private static HomePage ValidateResponse(HomePage? page)
        => page ?? throw new ClientTransactionException("invalid response");

    /// <summary><c>twitter-site-verification</c> メタタグからキーを読みます。</summary>
    public string GetKey(HomePage? page = null)
    {
        var doc = ValidateResponse(page ?? HomePageResponse).Document;
        var element = doc.QuerySelector("[name='twitter-site-verification']");
        if (element is null)
        {
            throw new InvalidSessionException(
                "Couldn't get key from the page source - the twitter-site-verification meta tag is missing, " +
                "which means x.com did not serve a logged-in page. Refresh the cookies.");
        }
        return element.GetAttribute("content") ?? "";
    }

    public static byte[] GetKeyBytes(string key) => Convert.FromBase64String(key);

    /// <summary><c>loading-x-anim-0</c> ～ <c>loading-x-anim-3</c> の要素。</summary>
    public IHtmlCollection<IElement> GetFrames(HomePage? page = null)
        => ValidateResponse(page ?? HomePageResponse).Document.QuerySelectorAll("[id^='loading-x-anim']");

    /// <summary>選んだフレームの 2 つ目の path の <c>d</c> 属性を "C" で区切って数値の 2 次元配列にします。</summary>
    public List<List<int>> Get2dArray(byte[] keyBytes, HomePage? page = null, IHtmlCollection<IElement>? frames = null)
    {
        frames ??= GetFrames(page);
        if (frames.Length == 0)
            throw new ClientTransactionException("Couldn't find the loading-x-anim frames in the page source.");
        var frame = frames[keyBytes[5] % 4];
        var d = frame.Children[0].Children[1].GetAttribute("d") ?? "";
        var result = new List<List<int>>();
        foreach (var item in d.Substring(9).Split('C'))
        {
            var numbers = NonDigits.Replace(item, " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result.Add(numbers.Select(n => int.Parse(n, CultureInfo.InvariantCulture)).ToList());
        }
        return result;
    }

    public static double Solve(double value, double minVal, double maxVal, bool rounding)
    {
        var result = value * (maxVal - minVal) / 255 + minVal;
        return rounding ? Math.Floor(result) : PyCompat.Round(result, 2);
    }

    /// <summary>フレーム行と目標時刻からアニメーションキーを計算します。</summary>
    public string Animate(IReadOnlyList<int> frames, double targetTime)
    {
        var fromColor = new List<double> { frames[0], frames[1], frames[2], 1 };
        var toColor = new List<double> { frames[3], frames[4], frames[5], 1 };
        var fromRotation = new List<double> { 0.0 };
        var toRotation = new List<double> { Solve(frames[6], 60.0, 360.0, true) };
        var rest = frames.Skip(7).ToList();
        var curves = new List<double>();
        for (var counter = 0; counter < rest.Count; counter++)
            curves.Add(Solve(rest[counter], TransactionMath.IsOdd(counter), 1.0, false));
        var cubic = new Cubic(curves);
        var val = cubic.GetValue(targetTime);
        var color = TransactionMath.Interpolate(fromColor, toColor, val)
            .Select(v => Math.Max(0, Math.Min(255, v))).ToArray();
        var rotation = TransactionMath.Interpolate(fromRotation, toRotation, val);
        var matrix = TransactionMath.ConvertRotationToMatrix(rotation[0]);
        var strArr = new List<string>();
        for (var i = 0; i < color.Length - 1; i++)
            strArr.Add(PyCompat.RoundToInt(color[i]).ToString("x", CultureInfo.InvariantCulture));
        foreach (var value in matrix)
        {
            var rounded = PyCompat.Round(value, 2);
            if (rounded < 0) rounded = -rounded;
            var hexValue = TransactionMath.FloatToHex(rounded);
            strArr.Add(hexValue.StartsWith('.') ? ("0" + hexValue).ToLowerInvariant()
                : hexValue.Length > 0 ? hexValue : "0");
        }
        strArr.Add("0");
        strArr.Add("0");
        return DotOrDash.Replace(string.Concat(strArr), "");
    }

    /// <summary>キーバイトとホームページからアニメーションキーを求めます。</summary>
    public string GetAnimationKey(byte[] keyBytes, HomePage? page = null)
    {
        if (DefaultRowIndex is null || DefaultKeyBytesIndices is null)
            throw new ClientTransactionException("KEY_BYTE indices have not been loaded; run the handshake first.");
        const double totalTime = 4096;
        var rowIndex = keyBytes[DefaultRowIndex.Value] % 16;
        long frameTime = 1;
        foreach (var index in DefaultKeyBytesIndices)
            frameTime *= keyBytes[index] % 16;
        frameTime = (long)Math.Floor(frameTime / 10.0 + 0.5) * 10;
        var arr = Get2dArray(keyBytes, page);
        var frameRow = arr[rowIndex];
        var targetTime = frameTime / totalTime;
        return Animate(frameRow, targetTime);
    }

    /// <summary>
    /// リクエスト 1 件分の <c>X-Client-Transaction-Id</c> を生成します。
    /// </summary>
    /// <param name="method">HTTP メソッド（大文字）。</param>
    /// <param name="path">URL のパス部分。</param>
    /// <param name="page">ハンドシェイク済みでない場合に使うホームページ。</param>
    /// <param name="key">明示的なキー。</param>
    /// <param name="animationKey">明示的なアニメーションキー。</param>
    /// <param name="timeNow">明示的な時刻（テスト用、2023-05-01 からの秒数）。</param>
    /// <param name="randomNumber">明示的な乱数（テスト用、0-255）。</param>
    public string GenerateTransactionId(string method, string path, HomePage? page = null, string? key = null,
        string? animationKey = null, long? timeNow = null, int? randomNumber = null)
    {
        if (!_inited && key is null && page is null)
        {
            // Falling through here reached GetKey(null) and raised "invalid
            // response", which names nothing and sends people looking at the
            // wrong end of the problem.
            throw new ClientTransactionException(
                "The X-Client-Transaction-Id handshake has not completed, so no id can be generated. " +
                "Call ClientTransaction.InitAsync() first, or let Client.RequestAsync() do it - if it keeps failing, " +
                "the earlier InvalidSession / AccountLocked error says why.");
        }
        var now = timeNow ?? (long)Math.Floor((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1682924400L * 1000) / 1000.0);
        var timeNowBytes = new byte[4];
        for (var i = 0; i < 4; i++) timeNowBytes[i] = (byte)((now >> (i * 8)) & 0xFF);
        key ??= Key ?? GetKey(page);
        var keyBytes = GetKeyBytes(key);
        animationKey ??= AnimationKey ?? GetAnimationKey(keyBytes, page);
        var hashInput = $"{method}!{path}!{now}{DefaultKeyword}{animationKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput));
        var random = randomNumber ?? RandomNumberGenerator.GetInt32(0, 256);
        var bytesArr = new List<byte>();
        bytesArr.AddRange(keyBytes);
        bytesArr.AddRange(timeNowBytes);
        bytesArr.AddRange(hash.Take(16));
        bytesArr.Add(AdditionalRandomNumber);
        var output = new byte[bytesArr.Count + 1];
        output[0] = (byte)random;
        for (var i = 0; i < bytesArr.Count; i++) output[i + 1] = (byte)(bytesArr[i] ^ random);
        return TransactionMath.Base64Encode(output).TrimEnd('=');
    }
}
