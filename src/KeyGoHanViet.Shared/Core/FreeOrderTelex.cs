using System.Text;

namespace KeyGoHanViet.Core;

/// <summary>
/// Cho phép gõ TELEX không theo thứ tự: "dofod", "ddofo", "dodof"… đều quy về
/// mã chuẩn "ddoof" (đồ) như các bộ gõ Việt quen thuộc vẫn làm.
///
/// Ý tưởng: mỗi âm tiết TELEX gồm hai loại phím.
///
///   • Phím <b>chữ gốc</b> - phụ âm và nguyên âm đứng đúng thứ tự: d, o.
///   • Phím <b>bổ trợ</b> - chỉ thêm dấu cho chữ gốc nên đặt đâu cũng được:
///     d thứ hai (đ), nguyên âm lặp (â ê ô), w (ă ơ ư), năm phím thanh s f r x j.
///
/// Hai cách gõ được coi là một nếu chuỗi chữ gốc giống hệt nhau và túi phím bổ
/// trợ giống hệt nhau - gọi chung là "chữ ký" (<see cref="Signature"/>). Bảng
/// chữ ký dựng sẵn từ mọi âm tiết có trong từ điển (gồm cả các âm lấy từ
/// ChinesePhienAmWords.txt), nên chỉ những âm có thật mới được nhận.
///
/// Để không phá lối gõ không dấu, có ba ràng buộc:
///
///   1. f j z w luôn là bổ trợ (tiếng Việt không có phụ âm này). s x r chỉ là
///      thanh khi đã có nguyên âm đứng trước và ngay sau nó không phải nguyên
///      âm - nếu không thì là phụ âm đầu: "sa", "tr", và quan trọng nhất là
///      chuỗi không dấu "daso", "hasi" vẫn là "da so", "ha si".
///   2. d chỉ là bổ trợ khi âm tiết mở đầu bằng d, và chỉ một lần. Đứng sát
///      ("dd") thì luôn nhận; đứng xa ("dod") thì chỉ nhận khi âm tiết đã có
///      phím thanh hoặc w, để "didu" vẫn là "di du".
///   3. Nguyên âm lặp (a/e/o thứ hai):
///      • Âm tiết đã có phím thanh hoặc w - tức là rõ ràng đang gõ TELEX - thì
///        đặt đâu cũng được: "dodof", "dofdo", "vietej" đều nhận.
///      • Chưa có thì chỉ nhận khi đứng sát chữ gốc (chỉ cách nhau phím thanh),
///        để chuỗi gõ không dấu "toto", "dodo", "dodong" giữ nguyên nghĩa
///        "to to", "do do", "do dong" chứ không thành tốt / đô / đông.
/// </summary>
public sealed class FreeOrderTelex
{
    private const string AlwaysModifiers = "fjzw";
    private const string ToneAfterVowel = "sxr";
    private const string Vowels = "aeiouy";

    /// <summary>chữ ký → các khoá thật trong từ điển, âm phổ biến nhất đứng đầu.</summary>
    private readonly Dictionary<string, string[]> _bySignature;

    private readonly HashSet<string> _validRaw;
    private readonly int _maxLen;

    public FreeOrderTelex(HanNomDictionary dict)
    {
        _validRaw = new HashSet<string>(dict.FullSyllables, StringComparer.Ordinal);
        _validRaw.UnionWith(dict.BareSyllables);
        _validRaw.UnionWith(dict.PlainSyllables);

        // Mỗi âm tiết đủ dấu sinh hai mục: bản có thanh ("ddoof") cho người gõ
        // có dấu thanh, và bản bỏ thanh ("ddoo") cho người không gõ thanh -
        // bản sau tra chỉ mục Bare nên ra đủ cả năm thanh.
        var buckets = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (string full in dict.FullSyllables)
        {
            int popularity = dict.LookupFull(full).Count;
            AddKey(buckets, full, popularity);
            AddKey(buckets, TelexCodec.StripTone(full), popularity);
        }

        _bySignature = new Dictionary<string, string[]>(buckets.Count, StringComparer.Ordinal);
        foreach (var (sig, keys) in buckets)
            _bySignature[sig] = keys
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key)
                .ToArray();

        _maxLen = Math.Max(1, dict.MaxSyllableLength);
    }

    private static void AddKey(Dictionary<string, Dictionary<string, int>> buckets, string key, int popularity)
    {
        string sig = Signature(key);
        if (sig.Length == 0) return;
        if (!buckets.TryGetValue(sig, out var keys)) buckets[sig] = keys = new(StringComparer.Ordinal);
        keys[key] = keys.TryGetValue(key, out int old) ? old + popularity : popularity;
    }

    /// <summary>Số chữ ký trong bảng - để kiểm thử và hiển thị thống kê.</summary>
    public int SignatureCount => _bySignature.Count;

    /// <summary>
    /// Chữ ký của một âm tiết gõ: "chữ gốc|phím bổ trợ đã sắp xếp".
    /// "dofod", "ddofo", "ddoof" cùng cho ra "do|dfo".
    ///
    /// Nhiều phím w gộp lại làm một, vì "nguoiw" và "nguwowi" cùng là người -
    /// gõ một w cho cả cụm ươ là thói quen phổ biến.
    /// </summary>
    public static string Signature(string syllable)
    {
        if (syllable.Length == 0) return "";

        var skeleton = new StringBuilder(syllable.Length);
        var mods = new List<char>(4);

        bool startsWithD = syllable[0] == 'd';
        bool usedD = false;
        bool seenVowel = false;
        bool twinOpen = false;     // nguyên âm gốc cuối cùng còn nhận được chữ lặp
        bool telex = HasTelexMarker(syllable);
        Span<bool> doubled = stackalloc bool[syllable.Length];

        for (int i = 0; i < syllable.Length; i++)
        {
            char c = syllable[i];

            if (IsModifierKey(syllable, i, seenVowel))
            {
                if (c == 'w')
                {
                    if (!mods.Contains('w')) mods.Add('w');
                    twinOpen = false;
                }
                else mods.Add(c);
                continue;
            }

            if (c == 'd' && i > 0 && startsWithD && !usedD && (i == 1 || telex))
            {
                usedD = true;
                mods.Add('d');
                if (!telex) twinOpen = false;
                continue;
            }

            if (c is 'a' or 'e' or 'o')
            {
                int twin = telex
                    ? FindTwin(skeleton, doubled, c)
                    : twinOpen && skeleton[^1] == c ? skeleton.Length - 1 : -1;
                if (twin >= 0)
                {
                    doubled[twin] = true;
                    mods.Add(c);
                    twinOpen = false;
                    continue;
                }
            }

            skeleton.Append(c);
            bool vowel = Vowels.Contains(c);
            seenVowel |= vowel;
            twinOpen = c is 'a' or 'e' or 'o';
        }

        mods.Sort();
        return skeleton.Append('|').Append(mods.ToArray()).ToString();
    }

    /// <summary>true nếu chuỗi có phím thanh hoặc w - dấu hiệu chắc chắn đang gõ TELEX.</summary>
    private static bool HasTelexMarker(string syllable)
    {
        bool seenVowel = false;
        for (int i = 0; i < syllable.Length; i++)
        {
            if (IsModifierKey(syllable, i, seenVowel)) return true;
            seenVowel |= Vowels.Contains(syllable[i]);
        }
        return false;
    }

    /// <summary>Phím thứ <paramref name="i"/> là phím thanh hoặc w (xem luật 1).</summary>
    private static bool IsModifierKey(string s, int i, bool seenVowel)
    {
        char c = s[i];
        if (AlwaysModifiers.Contains(c)) return true;
        return seenVowel && ToneAfterVowel.Contains(c) &&
               (i + 1 == s.Length || !Vowels.Contains(s[i + 1]));
    }

    /// <summary>
    /// Tìm trong cụm nguyên âm cuối của chữ gốc một nguyên âm trùng <paramref name="c"/>
    /// chưa được lặp, bỏ qua các phụ âm cuối đứng sau nó ("viet" + e → ê).
    /// </summary>
    private static int FindTwin(StringBuilder skeleton, Span<bool> doubled, char c)
    {
        int j = skeleton.Length - 1;
        while (j >= 0 && !Vowels.Contains(skeleton[j])) j--;
        for (; j >= 0 && Vowels.Contains(skeleton[j]); j--)
            if (skeleton[j] == c && !doubled[j]) return j;
        return -1;
    }

    /// <summary>
    /// Đưa chuỗi gõ về mã TELEX chuẩn của từ điển. Trả về null nếu chuỗi đã
    /// chuẩn sẵn hoặc không có cách hiểu nào khác - khi đó nơi gọi cứ tra
    /// nguyên văn như cũ.
    ///
    /// Quy hoạch động trên chuỗi gõ, mỗi mảnh là một âm tiết. Tiêu chí: (1) ít
    /// âm tiết nhất, (2) ít mảnh phải sắp lại nhất. Tiêu chí (2) giữ cho chuỗi
    /// gõ đúng chuẩn không bao giờ bị hiểu khác đi.
    /// </summary>
    public string? Canonicalize(string input)
    {
        int n = input.Length;
        if (n == 0) return null;

        var pieces = new int[n + 1];
        var changed = new int[n + 1];
        var take = new int[n + 1];
        var keyAt = new string?[n + 1];

        Array.Fill(pieces, int.MaxValue);
        pieces[n] = 0;

        for (int i = n - 1; i >= 0; i--)
        {
            int limit = Math.Min(_maxLen, n - i);
            for (int len = 1; len <= limit; len++)
            {
                if (pieces[i + len] == int.MaxValue) continue;

                string piece = input.Substring(i, len);
                string? key;
                int cost;

                if (_validRaw.Contains(piece)) { key = piece; cost = 0; }
                else if (_bySignature.TryGetValue(Signature(piece), out var keys)) { key = keys[0]; cost = 1; }
                else continue;

                int p = pieces[i + len] + 1;
                int c = changed[i + len] + cost;
                if (p < pieces[i] || (p == pieces[i] && c < changed[i]))
                {
                    pieces[i] = p;
                    changed[i] = c;
                    take[i] = len;
                    keyAt[i] = key;
                }
            }
        }

        if (pieces[0] == int.MaxValue || changed[0] == 0) return null;

        var sb = new StringBuilder(n + 4);
        for (int pos = 0; pos < n; pos += take[pos]) sb.Append(keyAt[pos]);

        string result = sb.ToString();
        return result == input ? null : result;
    }

    /// <summary>
    /// Mọi khoá chuẩn ứng với một âm tiết gõ tự do, phổ biến nhất trước.
    /// Dùng cho kiểm thử và cho tra cứu một âm tiết đơn lẻ.
    /// </summary>
    public IReadOnlyList<string> Resolve(string syllable) =>
        _bySignature.TryGetValue(Signature(syllable), out var keys) ? keys : [];
}
