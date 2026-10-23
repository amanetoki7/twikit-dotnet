using System.Buffers.Binary;
using System.Text;

namespace Twikit.Internal;

/// <summary>
/// X の server-driven UI「Jetfuel」（<c>/i/jfapi/...</c>）が返すバイナリ文書のデコーダ。
/// </summary>
/// <remarks>
/// 応答本文は生のバイナリ（Content-Type は text/plain）で、<c>u32 LE 長さ + ペイロード</c> のフレーム列です
/// （HAR に保存すると base64 になるため、フィクスチャは base64 です。<see cref="Decode(string)"/> はその用）。
/// ペイロードの先頭 1 バイトがメッセージ種別（0 = dom_data、1 = dom_ready、2 = action）で、
/// dom_data は <c>要素配列 + プロパティ配列 + タイムスタンプ</c> です。要素はプロパティ配列への
/// インデックスを持ち、子要素も要素配列へのインデックスで、最後の要素がルートです。
/// 形式は <c>jetfuel.*.js</c> の <c>decodeMessage</c> のスキーマ（2026-10、<c>x-jf-v: JP-5</c>）を写したものです。
/// </remarks>
internal sealed class JetfuelDocument
{
    /// <summary>要素。最後の要素がページのルート。</summary>
    public IReadOnlyList<JetfuelElement> Elements { get; }
    /// <summary>プロパティ値。要素の <see cref="JetfuelElement.Props"/> はここへのインデックス。</summary>
    public IReadOnlyList<JetfuelProp> Props { get; }
    /// <summary>サーバーが付けた生成時刻（Unix 秒）。</summary>
    public int Timestamp { get; }
    /// <summary>dom_ready が指すルート要素のインデックス（無ければ最後の要素）。</summary>
    public int RootIndex { get; }

    public JetfuelElement Root => Elements[RootIndex];

    private JetfuelDocument(List<JetfuelElement> elements, List<JetfuelProp> props, int timestamp, int rootIndex)
    {
        Elements = elements;
        Props = props;
        Timestamp = timestamp;
        RootIndex = rootIndex;
    }

    /// <summary>base64 で保存された本文（HAR のエクスポートなど）をデコードします。実際の応答は <see cref="Decode(byte[])"/> へ。</summary>
    public static JetfuelDocument Decode(string base64)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException e)
        {
            throw new TwitterException("Jetfuel response is not base64: " + Truncate(base64), e);
        }
        return Decode(bytes);
    }

    public static JetfuelDocument Decode(byte[] bytes)
    {
        List<JetfuelElement>? elements = null;
        List<JetfuelProp>? props = null;
        var timestamp = 0;
        int? root = null;

        var offset = 0;
        while (bytes.Length - offset > 4)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            offset += 4;
            if (length < 0 || bytes.Length - offset < length)
                throw new TwitterException($"Jetfuel frame of {length} bytes exceeds the {bytes.Length - offset} bytes left.");
            var reader = new Reader(bytes, offset, length);
            offset += length;

            switch (reader.U8())
            {
                case 0: // dom_data
                    elements = reader.List(reader.Element);
                    props = reader.List(reader.Prop);
                    timestamp = reader.I32();
                    reader.ExpectEnd();
                    break;
                case 1: // dom_ready
                    root = reader.Index();
                    reader.Optional(reader.I32);
                    break;
                case 2: // action
                    reader.Action();
                    break;
                default:
                    throw new TwitterException("Unknown Jetfuel message kind.");
            }
        }

        if (elements is null || props is null || elements.Count == 0)
            throw new TwitterException("Jetfuel response carries no page (dom_data).");
        if (root is null || root < 0 || root >= elements.Count) root = elements.Count - 1;
        return new JetfuelDocument(elements, props, timestamp, root.Value);
    }

    /// <summary>要素のプロパティ値（無ければ null）。</summary>
    public JetfuelProp? Prop(JetfuelElement element, ushort key)
    {
        foreach (var (k, index) in element.Props)
            if (k == key && index >= 0 && index < Props.Count) return Props[index];
        return null;
    }

    public string? Str(JetfuelElement element, ushort key)
        => Prop(element, key) is { Kind: JetfuelPropKind.Str or JetfuelPropKind.Url, Value: string s } ? s : null;

    public int? Int(JetfuelElement element, ushort key)
        => Prop(element, key) is { Kind: JetfuelPropKind.Int or JetfuelPropKind.Enum or JetfuelPropKind.Element, Value: int i } ? i : null;

    /// <summary>ルートから深さ優先で全要素を列挙します（同じ要素が複数の親から参照されていれば複数回）。</summary>
    public IEnumerable<JetfuelElement> Descendants(JetfuelElement element)
    {
        foreach (var child in element.Children)
        {
            if (child < 0 || child >= Elements.Count) continue;
            var e = Elements[child];
            yield return e;
            foreach (var d in Descendants(e)) yield return d;
        }
    }

    public IEnumerable<JetfuelElement> SelfAndDescendants(JetfuelElement element)
    {
        yield return element;
        foreach (var d in Descendants(element)) yield return d;
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s.Substring(0, 80) + "...";

    // ---------------------------------------------------------------- reader

    private sealed class Reader
    {
        private readonly byte[] _b;
        private readonly int _end;
        private int _p;

        public Reader(byte[] bytes, int offset, int length)
        {
            _b = bytes;
            _p = offset;
            _end = offset + length;
        }

        private void Need(int n)
        {
            if (_end - _p < n) throw new TwitterException("Jetfuel frame ended early.");
        }

        public void ExpectEnd()
        {
            if (_p != _end) throw new TwitterException($"Jetfuel dom_data left {_end - _p} undecoded bytes.");
        }

        public byte U8()
        {
            Need(1);
            return _b[_p++];
        }

        public bool Bool() => U8() != 0;

        /// <summary>LEB128 の符号なし可変長整数。</summary>
        public ulong UInt()
        {
            ulong result = 0;
            var shift = 0;
            while (true)
            {
                var c = U8();
                if (shift < 64) result |= (ulong)(c & 0x7f) << shift;
                shift += 7;
                if (c < 0x80) return result;
            }
        }

        /// <summary>要素・プロパティ配列へのインデックスなど、int に収まるはずの可変長整数。</summary>
        public int Index()
        {
            var v = UInt();
            return v > int.MaxValue ? -1 : (int)v;
        }

        public short I16()
        {
            Need(2);
            var v = BinaryPrimitives.ReadInt16LittleEndian(_b.AsSpan(_p, 2));
            _p += 2;
            return v;
        }

        public int I32()
        {
            Need(4);
            var v = BinaryPrimitives.ReadInt32LittleEndian(_b.AsSpan(_p, 4));
            _p += 4;
            return v;
        }

        public long I64()
        {
            Need(8);
            var v = BinaryPrimitives.ReadInt64LittleEndian(_b.AsSpan(_p, 8));
            _p += 8;
            return v;
        }

        public double F64()
        {
            Need(8);
            var v = BinaryPrimitives.ReadDoubleLittleEndian(_b.AsSpan(_p, 8));
            _p += 8;
            return v;
        }

        public string Str()
        {
            var n = Index();
            if (n < 0) throw new TwitterException("Jetfuel string length overflow.");
            Need(n);
            var s = Encoding.UTF8.GetString(_b, _p, n);
            _p += n;
            return s;
        }

        public List<T> List<T>(Func<T> item)
        {
            var n = Index();
            if (n < 0 || n > _end - _p) throw new TwitterException("Jetfuel list length overflow.");
            var list = new List<T>(n);
            for (var i = 0; i < n; i++) list.Add(item());
            return list;
        }

        public T? Optional<T>(Func<T> item) where T : struct => Bool() ? item() : null;

        public List<KeyValuePair<TK, TV>> Map<TK, TV>(Func<TK> key, Func<TV> value)
            => List(() => new KeyValuePair<TK, TV>(key(), value()));

        public JetfuelElement Element()
        {
            var type = (ushort)I16();
            var props = Map(() => (ushort)I16(), Index);
            var children = List(Index);
            var id = Optional(I64);
            var extend = Optional(Index);
            return new JetfuelElement(type, props, children, id, extend);
        }

        public JetfuelProp Prop()
        {
            var kind = (JetfuelPropKind)U8();
            object? value = kind switch
            {
                JetfuelPropKind.Str => Str(),
                JetfuelPropKind.Url => Str(),
                JetfuelPropKind.Int => I32(),
                JetfuelPropKind.Mods => Mods(),
                JetfuelPropKind.ConditionalMods => Skip(() => { List(() => { Mods(); Predicate(); return 0; }); return 0; }),
                JetfuelPropKind.Id => I64(),
                JetfuelPropKind.Date => I64(),
                JetfuelPropKind.F64 => F64(),
                JetfuelPropKind.Bool => Bool(),
                JetfuelPropKind.Element => Index(),
                JetfuelPropKind.Enum => Index(),
                JetfuelPropKind.Scribe => Index(),
                JetfuelPropKind.Elements => List(Index).ToArray(),
                JetfuelPropKind.List => List(Index).ToArray(),
                JetfuelPropKind.Feed => List(Index).ToArray(),
                JetfuelPropKind.RichText => List(() => (U8(), Str(), Bool() ? Str() : null)).ToArray(),
                JetfuelPropKind.Bag => Map(I16, Index),
                JetfuelPropKind.StrDict => StrDict(),
                JetfuelPropKind.Atom => Skip(() => { Ref(); Index(); Bool(); return 0; }),
                JetfuelPropKind.Subatom => Skip(() => { Ref(); return 0; }),
                JetfuelPropKind.Action => Action(),
                JetfuelPropKind.Predicate => Skip(() => { Predicate(); return 0; }),
                JetfuelPropKind.StrList => List(Str).ToArray(),
                JetfuelPropKind.IntList => List(I32).ToArray(),
                JetfuelPropKind.F64List => List(F64).ToArray(),
                JetfuelPropKind.BoolList => List(Bool).ToArray(),
                JetfuelPropKind.Form => Skip(() => { Str(); StrDict(); StrDict(); Str(); U8(); return 0; }),
                JetfuelPropKind.NotFound => null,
                _ => throw new TwitterException($"Unknown Jetfuel prop kind {(int)kind}."),
            };
            return new JetfuelProp(kind, value);
        }

        private static object? Skip(Func<int> consume)
        {
            consume();
            return null;
        }

        private int[][] Mods() => List(() => List(I32).ToArray()).ToArray();

        private Dictionary<string, string> StrDict()
        {
            var dict = new Dictionary<string, string>();
            foreach (var (k, v) in Map(Str, Str)) dict[k] = v;
            return dict;
        }

        /// <summary>atom / bag_key / form_field などへの参照。値は使わないので読み飛ばします。</summary>
        private void Ref()
        {
            switch (U8())
            {
                case 0: I64(); break;                 // atom
                case 4: I64(); Index(); break;        // atom_with_root
                case 1: I16(); Index(); break;        // bag_key
                case 2: case 3: case 7: case 8:       // str_key, url_param, form_field, form_field_error
                    Str(); Index(); break;
                case 5: case 6: case 9: case 10: case 11:
                    Index(); break;                   // transforming_*, form_submission_status, form_submit_url, form_values
                default: throw new TwitterException("Unknown Jetfuel ref kind.");
            }
        }

        private void Change()
        {
            var kind = U8();
            Ref();
            switch (kind)
            {
                case 0: break;                                   // null
                case 1: case 5: case 7: case 8: Index(); break;  // set, list_remove, merge_bag, merge_str_dict
                case 2: I16(); break;                            // inc
                case 3: Str(); break;                            // str_append
                case 4: Index(); Optional(I16); break;           // list_insert
                case 6: if (Bool()) { Index(); Index(); } break; // toggle
                default: throw new TwitterException("Unknown Jetfuel change kind.");
            }
        }

        private void Predicate()
        {
            switch (U8())
            {
                case 0: case 15: Ref(); break;                                   // null, not_null
                case 1: case 2: case 5: case 6: case 7: case 8: Ref(); Index(); break; // is, is_not, gt, gte, lt, lte
                case 3: case 4: Ref(); List(Index); break;                       // in, not_in
                case 9: case 10: case 11: Ref(); Str(); break;                   // starts_with, ends_with, includes
                case 12: case 13: Predicate(); Predicate(); break;               // and, or
                case 14: Predicate(); break;                                     // not
                default: throw new TwitterException("Unknown Jetfuel predicate kind.");
            }
        }

        /// <summary>アクション。ページ遷移（navigate）だけ URL のプロパティ番号を返し、他は読み飛ばします。</summary>
        public JetfuelAction Action()
        {
            var kind = (JetfuelActionKind)U8();
            switch (kind)
            {
                case JetfuelActionKind.Change: Change(); break;
                case JetfuelActionKind.Confirm: Index(); Action(); if (Bool()) Action(); break;
                case JetfuelActionKind.Multi: List(Action); break;
                case JetfuelActionKind.PostRequest:
                    Index(); Index();
                    for (var i = 0; i < 3; i++) if (Bool()) Action();
                    break;
                case JetfuelActionKind.Haptic: Action(); I16(); break;
                case JetfuelActionKind.Present: Index(); U8(); break;
                case JetfuelActionKind.PresentToast: Index(); I16(); Bool(); break;
                case JetfuelActionKind.PresentRotatable: Index(); U8(); Bool(); break;
                case JetfuelActionKind.PresentSheet: Index(); Index(); Str(); break;
                case JetfuelActionKind.Navigate: return Navigate();
                case JetfuelActionKind.Refresh: U8(); Optional(I64); break;
                case JetfuelActionKind.CacheClear: Str(); break;
                case JetfuelActionKind.Prefetch: List(Str); U8(); break;
                case JetfuelActionKind.Scribe: Str(); Index(); break;
                case JetfuelActionKind.System: U8(); Index(); break;
                case JetfuelActionKind.Delayed: Action(); I16(); break;
                case JetfuelActionKind.Auth: Str(); Str(); Str(); break;
                case JetfuelActionKind.Alert: Str(); if (Bool()) Str(); break;
                case JetfuelActionKind.FormSubmit: Index(); Index(); break;
                case JetfuelActionKind.FormReset: Index(); List(Str); break;
                case JetfuelActionKind.FormValidate: Index(); Index(); break;
                case JetfuelActionKind.FormSetFocus: Index(); Str(); break;
                default: throw new TwitterException($"Unknown Jetfuel action kind {(int)kind}.");
            }
            return new JetfuelAction(kind, null, null);
        }

        private JetfuelAction Navigate()
        {
            var kind = (JetfuelNavigateKind)U8();
            int? url = null;
            switch (kind)
            {
                case JetfuelNavigateKind.Page:
                case JetfuelNavigateKind.PageResetTo:
                    url = Index(); Optional(Index); Bool(); break;
                case JetfuelNavigateKind.PagePost:
                    url = Index(); Optional(Index); Optional(Index); Bool(); break;
                case JetfuelNavigateKind.Link:
                    url = Index(); break;
                case JetfuelNavigateKind.ScrollId:
                    Index(); break;
                case JetfuelNavigateKind.Dismiss:
                case JetfuelNavigateKind.Back:
                case JetfuelNavigateKind.BackRoot:
                case JetfuelNavigateKind.ScrollTop:
                case JetfuelNavigateKind.ScrollBottom:
                    break;
                default: throw new TwitterException($"Unknown Jetfuel navigate kind {(int)kind}.");
            }
            return new JetfuelAction(JetfuelActionKind.Navigate, kind, url);
        }
    }
}

/// <summary>Jetfuel の 1 要素。<see cref="Type"/> はコンポーネント名の murmur3 ハッシュの下位 16 ビット。</summary>
internal sealed class JetfuelElement
{
    public ushort Type { get; }
    /// <summary>プロパティ名のハッシュ（下位 16 ビット）→ プロパティ配列のインデックス。</summary>
    public IReadOnlyList<KeyValuePair<ushort, int>> Props { get; }
    /// <summary>子要素のインデックス。</summary>
    public IReadOnlyList<int> Children { get; }
    public long? Id { get; }
    public int? Extend { get; }

    public JetfuelElement(ushort type, List<KeyValuePair<ushort, int>> props, List<int> children, long? id, int? extend)
    {
        Type = type;
        Props = props;
        Children = children;
        Id = id;
        Extend = extend;
    }

    public override string ToString() => $"<JetfuelElement type=0x{Type:x4} props={Props.Count} children={Children.Count}>";
}

/// <summary>プロパティ値。読み飛ばした種類（atom、predicate など）は <see cref="Value"/> が null。</summary>
internal sealed class JetfuelProp
{
    public JetfuelPropKind Kind { get; }
    public object? Value { get; }

    public JetfuelProp(JetfuelPropKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    public override string ToString() => $"<JetfuelProp {Kind}={Value}>";
}

internal sealed record JetfuelAction(JetfuelActionKind Kind, JetfuelNavigateKind? Navigate, int? UrlProp);

internal enum JetfuelPropKind : byte
{
    Str = 0, Int = 1, Mods = 3, Id = 4, F64 = 5, Bool = 6, Element = 7, Elements = 8, Enum = 10, Url = 11,
    RichText = 12, NotFound = 13, Date = 14, Scribe = 15, Bag = 16, StrDict = 17, Atom = 18, Action = 19,
    List = 21, Predicate = 22, Feed = 24, ConditionalMods = 25, StrList = 26, IntList = 27, F64List = 28,
    BoolList = 29, Form = 30, Subatom = 31,
}

internal enum JetfuelActionKind : byte
{
    Change = 0, Confirm = 1, Multi = 2, PostRequest = 3, Haptic = 4, Present = 5, Navigate = 6, Refresh = 7,
    CacheClear = 8, Prefetch = 9, Scribe = 10, System = 11, Delayed = 12, Auth = 13, Alert = 14, FormSubmit = 15,
    FormReset = 16, FormValidate = 17, FormSetFocus = 18, PresentRotatable = 19, PresentSheet = 20, PresentToast = 21,
}

internal enum JetfuelNavigateKind : byte
{
    Page = 0, PagePost = 1, Dismiss = 2, Back = 3, BackRoot = 4, ScrollTop = 5, ScrollBottom = 6, ScrollId = 7,
    Link = 8, PageResetTo = 9,
}

/// <summary>
/// 既知のコンポーネント / プロパティ / アイコンの番号。コンポーネントとプロパティは名前の
/// murmur3_32（seed 0、UTF-8）の下位 16 ビット、アイコンは Web クライアントのアイコン名配列のインデックス。
/// </summary>
internal static class JetfuelIds
{
    // components
    public const ushort View = 0xe075;
    public const ushort Text = 0x9933;
    public const ushort Icon = 0xc4d9;
    public const ushort Page = 0xd118;
    public const ushort Grid = 0xf913;
    public const ushort GridRow = 0x2008;
    public const ushort GridCell = 0x3139;
    public const ushort RichText = 0x7335;      // RichText2
    public const ushort RichTextText = 0x0fbb;  // RichText.Text

    // props
    public const ushort Mod = 0xe940;
    public const ushort TextValue = 0xdf4d;     // Text / RichText.Text の本文
    public const ushort IconKey = 0x6a44;       // key
    public const ushort Size = 0x17fa;
    public const ushort Columns = 0x01bc;
    public const ushort Header = 0x4ca9;
    public const ushort Link = 0xd234;
    public const ushort PageInfo = 0x5729;      // {page, section} の str_dict

    // icons
    public const int IconCheckmarkCircleFill = 84;
    public const int IconCloseCircle = 101;
    public const int IconInformationCircle = 224;
}
