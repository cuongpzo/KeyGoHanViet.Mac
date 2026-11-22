using System.Text;
using System.Text.RegularExpressions;

namespace KeyGoHanViet.Core;

/// <summary>
/// Chuyển đổi giữa chuỗi TELEX thô (a-z) và chữ Quốc ngữ có dấu.
/// Các quy tắc được port trực tiếp từ mục <c>translator.preedit_format</c>
/// trong <c>hannomPS.schema.yaml</c> nên hiển thị preedit khớp với Rime.
/// </summary>
public static class TelexCodec
{
    /// <summary>Các ký tự thanh điệu đứng cuối âm tiết. 'z' = thanh ngang.</summary>
    public const string ToneLetters = "zsxjfr";

    // ---------------------------------------------------------------- rules

    private sealed record Rule(Regex Pattern, string Replacement);

    private static Rule R(string pattern, string replacement) =>
        new(new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), replacement);

    /// <summary>
    /// Quy tắc bổ sung (không có trong schema gốc): đưa dấu thanh về nguyên âm
    /// đầu của các nhị trùng âm "ia / ua / ưa" theo lối đặt dấu hiện đại,
    /// ví dụ nghiax -> nghĩa thay vì nghiã.
    /// </summary>
    private static readonly Rule[] ModernToneRules =
    [
        // ưa: luôn đặt dấu trên ư  (cuwar -> cửa, thuwaf -> thừa)
        R(@"(uw)(a)([sfjxr])", "$1$3$2"),
        // ua: đặt dấu trên u, trừ "qu" vì ở đó u chỉ là âm đệm  (muaf -> mùa, quaf -> quà)
        R(@"(?<![qQ])(u)(a)([sfjxr])", "$1$3$2"),
        // ia: đặt dấu trên i, trừ "gi" vì ở đó i thuộc phụ âm đầu  (nghiax -> nghĩa, giaf -> già)
        R(@"(?<![gG])(i)(a)([sfjxr])", "$1$3$2"),
    ];

    /// <summary>Năm quy tắc regex đầu tiên của preedit_format: đẩy dấu thanh vào trước phụ âm cuối.</summary>
    private static readonly Rule[] ToneShiftRules =
    [
        R(@"([aeiouâăôê])(ng|nh|ch)([sfjxr])", "$1$3$2"),
        R(@"([aeiouâăôê])([iunptcmy])([sfjxr])", "$1$3$2"),
        R(@"([ae])([ou])([sfjxr])", "$1$3$2"),
        R(@"(aw|ow|uw)([iunptcmy])([sfjxr])", "$1$3$2"),
        R(@"(aw|ow|uw)(ng|nh|ch)([sfjxr])", "$1$3$2"),
    ];

    /// <summary>
    /// Bảng thay thế chuỗi theo đúng thứ tự trong schema. Thứ tự rất quan trọng:
    /// digraph (aa, aw, oo, ee, uw, ow) phải được dựng thành nguyên âm có mũ/móc
    /// trước khi ghép với ký tự thanh điệu.
    /// </summary>
    private static readonly (string From, string To)[] LiteralRules =
    [
        ("z", ""),        // thanh ngang - xoá khỏi preedit
        (",", ""),        // dấu phân cách dị thể
        ("A", "　"),  // khoảng trắng toàn giác

        ("dd", "đ"),

        ("aa", "â"),
        ("âs", "ấ"), ("âf", "ầ"), ("âj", "ậ"), ("âx", "ẫ"), ("âr", "ẩ"),

        ("aw", "ă"),
        ("ăs", "ắ"), ("ăf", "ằ"), ("ăj", "ặ"), ("ăx", "ẵ"), ("ăr", "ẳ"),

        ("oo", "ô"),
        ("ôs", "ố"), ("ôf", "ồ"), ("ôj", "ộ"), ("ôx", "ỗ"), ("ôr", "ổ"),

        ("ee", "ê"),
        ("ês", "ế"), ("êf", "ề"), ("êj", "ệ"), ("êx", "ễ"), ("êr", "ể"),

        ("uw", "ư"),
        ("ưs", "ứ"), ("ưf", "ừ"), ("ưj", "ự"), ("ưx", "ữ"), ("ưr", "ử"),

        ("ow", "ơ"),
        ("ơs", "ớ"), ("ơf", "ờ"), ("ơj", "ợ"), ("ơx", "ỡ"), ("ơr", "ở"),

        ("as", "á"), ("af", "à"), ("aj", "ạ"), ("ax", "ã"), ("ar", "ả"),
        ("is", "í"), ("if", "ì"), ("ij", "ị"), ("ix", "ĩ"), ("ir", "ỉ"),
        ("us", "ú"), ("uf", "ù"), ("uj", "ụ"), ("ux", "ũ"), ("ur", "ủ"),
        ("es", "é"), ("ef", "è"), ("ej", "ẹ"), ("ex", "ẽ"), ("er", "ẻ"),
        ("os", "ó"), ("of", "ò"), ("oj", "ọ"), ("ox", "õ"), ("or", "ỏ"),
        ("ys", "ý"), ("yf", "ỳ"), ("yj", "ỵ"), ("yx", "ỹ"), ("yr", "ỷ"),
    ];

    // ------------------------------------------------------------- rendering

    /// <summary>
    /// Dựng chữ Quốc ngữ có dấu từ một âm tiết TELEX thô.
    /// </summary>
    /// <param name="modernTonePlacement">
    /// true: đặt dấu theo lối hiện đại cho ia/ua/ưa (nghĩa), false: giữ nguyên hành vi schema (nghiã).
    /// </param>
    public static string RenderSyllable(string telex, bool modernTonePlacement = true)
    {
        if (string.IsNullOrEmpty(telex)) return string.Empty;

        string s = telex;

        if (modernTonePlacement)
            foreach (var rule in ModernToneRules)
                s = rule.Pattern.Replace(s, rule.Replacement);

        foreach (var rule in ToneShiftRules)
            s = rule.Pattern.Replace(s, rule.Replacement);

        foreach (var (from, to) in LiteralRules)
            if (s.Contains(from, StringComparison.Ordinal))
                s = s.Replace(from, to, StringComparison.Ordinal);

        return s;
    }

    /// <summary>Dựng cả cụm nhiều âm tiết (đã tách bằng khoảng trắng).</summary>
    public static string RenderPhrase(IEnumerable<string> syllables, bool modernTonePlacement = true)
    {
        var sb = new StringBuilder();
        foreach (var syl in syllables)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(RenderSyllable(syl, modernTonePlacement));
        }
        return sb.ToString();
    }

    // -------------------------------------------------------- normalisation

    /// <summary>
    /// Bỏ ký tự thanh điệu ở cuối âm tiết: vieetj -> vieet, namz -> nam.
    /// Tương ứng luật <c>derive/^(.*)[z,sxjfrA]$/$1/</c> của schema.
    /// </summary>
    public static string StripTone(string syllable)
    {
        if (syllable.Length < 2) return syllable;
        char last = syllable[^1];
        return ToneLetters.IndexOf(last) >= 0 ? syllable[..^1] : syllable;
    }

    /// <summary>
    /// Hạ digraph về chữ cái trần: vieet -> viet, hooi -> hoi, thuwowng -> thuong.
    /// Dùng cho chế độ gõ hoàn toàn không dấu.
    /// </summary>
    public static string ToPlain(string bareSyllable)
    {
        if (bareSyllable.Length == 0) return bareSyllable;

        var sb = new StringBuilder(bareSyllable.Length);
        for (int i = 0; i < bareSyllable.Length; i++)
        {
            if (i + 1 < bareSyllable.Length)
            {
                char a = bareSyllable[i], b = bareSyllable[i + 1];
                // dd -> d, aa -> a, aw -> a, ee -> e, oo -> o, uw -> u, ow -> o
                if ((a == 'd' && b == 'd') ||
                    (a == 'a' && (b == 'a' || b == 'w')) ||
                    (a == 'e' && b == 'e') ||
                    (a == 'o' && (b == 'o' || b == 'w')) ||
                    (a == 'u' && b == 'w'))
                {
                    sb.Append(a);
                    i++;           // bỏ qua ký tự thứ hai của digraph
                    continue;
                }
            }
            sb.Append(bareSyllable[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Chiều ngược của <see cref="RenderSyllable"/>: dựng mã TELEX chuẩn của từ
    /// điển từ một âm tiết Quốc ngữ có dấu. "đồ" -> "ddoof", "nam" -> "namz",
    /// "người" -> "nguwowif". Thanh điệu luôn đứng cuối, thanh ngang ghi 'z' -
    /// đúng lối viết mã của hannomPS nên khoá dựng ra tra thẳng được.
    ///
    /// Trả về chuỗi rỗng nếu âm tiết có ký tự ngoài bảng chữ Quốc ngữ (chữ số,
    /// dấu câu, chữ nước khác) - nơi gọi coi đó là dòng dữ liệu không dùng được.
    /// </summary>
    public static string FromVietSyllable(string syllable)
    {
        string nfd;
        try { nfd = syllable.ToLowerInvariant().Normalize(NormalizationForm.FormD); }
        catch (ArgumentException) { return ""; }

        var sb = new StringBuilder(nfd.Length + 3);
        char tone = 'z';
        bool hasLetter = false;

        foreach (char c in nfd)
        {
            switch (c)
            {
                case >= 'a' and <= 'z': sb.Append(c); hasLetter = true; break;
                case 'đ': sb.Append("dd"); hasLetter = true; break;

                // Dấu mũ, dấu trăng, dấu móc: viết lại thành phím TELEX đi sau nguyên âm.
                case '̂' when sb.Length > 0 && sb[^1] is 'a' or 'e' or 'o': sb.Append(sb[^1]); break;
                case '̆' when sb.Length > 0 && sb[^1] == 'a': sb.Append('w'); break;
                case '̛' when sb.Length > 0 && sb[^1] is 'o' or 'u': sb.Append('w'); break;

                // Năm dấu thanh: dồn về cuối âm tiết.
                case '́': tone = 's'; break;
                case '̀': tone = 'f'; break;
                case '̉': tone = 'r'; break;
                case '̃': tone = 'x'; break;
                case '̣': tone = 'j'; break;

                default: return "";
            }
        }

        if (!hasLetter) return "";
        sb.Append(tone);
        return sb.ToString();
    }

    /// <summary>Chỉ giữ lại a-z (loại bỏ mọi ký tự khác) và hạ chữ thường.</summary>
    public static string SanitizeInput(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
        {
            if (c is >= 'a' and <= 'z') sb.Append(c);
            else if (c is >= 'A' and <= 'Z') sb.Append((char)(c + 32));
        }
        return sb.ToString();
    }
}
