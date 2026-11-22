using System.Text;

namespace KeyGoHanViet.Core;

/// <summary>
/// Một mảnh của kết quả chuyển tự.
///
/// Chuỗi gốc được chẻ thành hai loại mảnh xen kẽ nhau: mảnh chữ Hán (đúng một
/// chữ, có danh sách âm đọc) và mảnh còn lại (dấu câu, chữ Latin, khoảng trắng -
/// giữ nguyên không đụng tới).
/// </summary>
public sealed class HanVietToken
{
    /// <summary>Nguyên văn phần chuỗi gốc sinh ra mảnh này.</summary>
    public required string Source { get; init; }

    /// <summary>true nếu đây là một chữ Hán Nôm.</summary>
    public required bool IsHan { get; init; }

    /// <summary>
    /// Các âm đọc tra được, xếp theo thứ tự ưu tiên - xem
    /// <see cref="HanToVietTransliterator"/>. Rỗng nghĩa là từ điển không có chữ này.
    /// </summary>
    public required IReadOnlyList<string> Readings { get; init; }

    private int _chosen;

    /// <summary>Chỉ số âm đọc đang dùng; gán ngoài khoảng thì tự kẹp về khoảng hợp lệ.</summary>
    public int Chosen
    {
        get => _chosen;
        set => _chosen = Readings.Count == 0 ? 0 : Math.Clamp(value, 0, Readings.Count - 1);
    }

    /// <summary>Chữ sẽ xuất hiện trong kết quả: âm đọc đang chọn, hoặc chính chữ Hán nếu tra không ra.</summary>
    public string Text => Readings.Count > 0 ? Readings[_chosen] : Source;

    /// <summary>true nếu chữ này còn âm đọc khác để đổi sang.</summary>
    public bool HasAlternatives => Readings.Count > 1;

    /// <summary>true nếu là chữ Hán mà từ điển không có âm đọc nào.</summary>
    public bool IsUnknown => IsHan && Readings.Count == 0;

    /// <summary>Vị trí của mảnh này trong chuỗi kết quả, do <see cref="HanVietResult.Render"/> đặt.</summary>
    public int Start { get; internal set; }

    /// <summary>Độ dài của mảnh này trong chuỗi kết quả, do <see cref="HanVietResult.Render"/> đặt.</summary>
    public int Length { get; internal set; }
}

/// <summary>Kết quả chuyển một đoạn văn bản từ chữ Hán Nôm sang âm đọc Quốc ngữ.</summary>
public sealed class HanVietResult
{
    /// <summary>Đoạn văn bản gốc (đã cắt bớt nếu quá dài).</summary>
    public required string Source { get; init; }

    public required IReadOnlyList<HanVietToken> Tokens { get; init; }

    /// <summary>Số chữ Hán Nôm gặp trong đoạn.</summary>
    public required int HanCount { get; init; }

    /// <summary>Số chữ Hán Nôm mà từ điển không có âm đọc.</summary>
    public required int UnknownCount { get; init; }

    /// <summary>true nếu đoạn gốc đã bị cắt bớt vì quá dài.</summary>
    public bool Truncated { get; init; }

    private string _rendered = "";

    /// <summary>Chuỗi kết quả của lần <see cref="Render"/> gần nhất.</summary>
    public string Text => _rendered;

    /// <summary>
    /// Dựng chuỗi kết quả, đồng thời ghi lại vị trí từng mảnh để giao diện biết
    /// người dùng vừa bấm vào chữ nào.
    ///
    /// Gọi lại được nhiều lần: sau khi người dùng đổi âm đọc của một chữ, chuỗi
    /// và các vị trí đều được tính lại từ đầu.
    /// </summary>
    public string Render()
    {
        var sb = new StringBuilder(Source.Length * 4);

        // Viết hoa chữ đầu câu, nhưng chỉ với phần do chính ta dựng ra. Phần
        // nguyên bản của người dùng (chữ Latin, dấu câu) thì không đụng tới:
        // sửa hoa/thường trong đó là tự tiện chữa văn bản của người ta.
        bool atSentenceStart = true;
        bool previousWasHan = false;

        foreach (var token in Tokens)
        {
            string piece = token.Text;

            if (token.IsHan)
            {
                // Hai âm đọc liền nhau phải có dấu cách, nhưng đừng thêm sau dấu
                // mở ngoặc hay sau khoảng trắng vốn đã có trong bản gốc.
                if (sb.Length > 0 && NeedsSpaceBefore(sb[^1])) sb.Append(' ');

                token.Start = sb.Length;

                if (atSentenceStart && piece.Length > 0 && char.IsLetter(piece[0]))
                {
                    sb.Append(char.ToUpperInvariant(piece[0]));
                    sb.Append(piece, 1, piece.Length - 1);
                }
                else sb.Append(piece);

                token.Length = sb.Length - token.Start;
                atSentenceStart = false;
            }
            else
            {
                // Âm đọc dính liền chữ Latin ngay sau nó thì đọc thành một từ
                // lạ, nên tách ra - trừ khi cái đi sau vốn đã là dấu câu.
                if (previousWasHan && piece.Length > 0 && NeedsSpaceAfterReading(piece[0])) sb.Append(' ');

                token.Start = sb.Length;
                sb.Append(piece);
                token.Length = piece.Length;

                foreach (char c in piece)
                {
                    if (IsSentenceEnd(c)) atSentenceStart = true;
                    else if (!char.IsWhiteSpace(c)) atSentenceStart = false;
                }
            }

            previousWasHan = token.IsHan;
        }

        _rendered = sb.ToString();
        return _rendered;
    }

    /// <summary>Mảnh nằm tại vị trí <paramref name="offset"/> của chuỗi kết quả, hoặc null.</summary>
    public HanVietToken? TokenAt(int offset)
    {
        foreach (var t in Tokens)
            if (offset >= t.Start && offset < t.Start + t.Length) return t;
        return null;
    }

    /// <summary>Có phải thêm dấu cách trước một âm đọc sắp ghi ra không?</summary>
    private static bool NeedsSpaceBefore(char prev) =>
        !char.IsWhiteSpace(prev) && !IsOpeningMark(prev);

    /// <summary>Có phải thêm dấu cách giữa âm đọc vừa ghi và ký tự gốc ngay sau nó không?</summary>
    private static bool NeedsSpaceAfterReading(char next) =>
        char.IsLetterOrDigit(next) || IsOpeningMark(next);

    private static bool IsOpeningMark(char c) =>
        c is '(' or '[' or '{' or '"' or '\'' or '“' or '‘' or '（' or '「' or '『' or '《' or '〈' or '〔';

    private static bool IsSentenceEnd(char c) =>
        c is '.' or '!' or '?' or '\n' or '…' or '。' or '！' or '？';
}

/// <summary>
/// Chuyển chữ Hán Nôm sang âm đọc Quốc ngữ, <b>từng chữ một</b>: 滴答滴答答 →
/// "Trích đáp trích đáp đáp".
///
/// Đây là chiều ngược của bộ gõ. Bộ gõ đi từ âm đọc ra chữ vuông và cho người
/// dùng chọn trong khung gợi ý; lớp này đi từ chữ vuông ra âm đọc để đọc hiểu
/// một đoạn văn bản có sẵn, nên phải tự quyết định lấy một âm cho mỗi chữ.
///
/// <b>Thứ tự ưu tiên âm đọc</b> của một chữ:
///
///   1. Mục từ điển người dùng - người dùng tự thêm thì hẳn là cố ý.
///   2. Âm không phải mượn Nôm (dòng không có ghi chú "Loan or homonym").
///   3. Chữ được từ điển đánh dấu 𡨸標準 cho âm đó (<see cref="HanEntry.IsStandard"/>).
///      Đây là dấu hiệu sát nhất của "âm Hán Việt chính thống": với âm mượn Nôm,
///      chữ chuẩn của âm đó là một chữ khác chứ không phải chữ đang xét.
///   4. Tần suất trong từ điển, cao trước.
///
/// Quy tắc này không thể lúc nào cũng đúng - chữ đa âm là chuyện thường của Hán
/// Nôm - nên mọi âm còn lại vẫn nằm trong <see cref="HanVietToken.Readings"/> để
/// giao diện cho người dùng đổi sang bằng một cú bấm chuột.
/// </summary>
public sealed class HanToVietTransliterator
{
    /// <summary>
    /// Số ký tự tối đa nhận vào. Đủ cho một đoạn văn dài; quá mốc này thì cắt
    /// bớt chứ không từ chối, vì người dùng bôi đen cả trang thì vẫn nên thấy
    /// được phần đầu.
    /// </summary>
    public const int MaxLength = 2000;

    private readonly Dictionary<string, string[]> _readings;

    public HanToVietTransliterator(HanNomDictionary dict)
    {
        var buckets = new Dictionary<string, List<HanEntry>>(StringComparer.Ordinal);

        foreach (var e in dict.Entries)
        {
            // Chỉ lấy mục một chữ một âm: kết quả cần khớp 1-1 từng ký tự, mà
            // cụm nhiều chữ thì không cắt ra từng chữ được.
            if (e.Syllables.Length != 1) continue;
            if (e.Viet.Length == 0) continue;
            if (!HanText.IsSingleHanChar(e.Text)) continue;

            if (!buckets.TryGetValue(e.Text, out var list)) buckets[e.Text] = list = [];
            list.Add(e);
        }

        _readings = new Dictionary<string, string[]>(buckets.Count, StringComparer.Ordinal);

        foreach (var (text, list) in buckets)
        {
            list.Sort(ByPriority);

            // Hai dòng khác mã TELEX vẫn có thể cho cùng một âm đọc; giữ lại một.
            var seen = new List<string>(list.Count);
            foreach (var e in list)
            {
                string reading = e.Viet.Trim();
                if (reading.Length == 0) continue;
                if (!seen.Contains(reading, StringComparer.OrdinalIgnoreCase)) seen.Add(reading);
            }

            if (seen.Count > 0) _readings[text] = [.. seen];
        }
    }

    /// <summary>Số chữ Hán Nôm tra được âm đọc.</summary>
    public int CharCount => _readings.Count;

    /// <summary>Các âm đọc của một chữ, đã xếp theo thứ tự ưu tiên. Rỗng nếu không có.</summary>
    public IReadOnlyList<string> ReadingsOf(string hanChar) =>
        _readings.TryGetValue(hanChar, out var r) ? r : [];

    private static int ByPriority(HanEntry a, HanEntry b)
    {
        if (a.FromUserDict != b.FromUserDict) return a.FromUserDict ? -1 : 1;

        bool la = IsLoan(a), lb = IsLoan(b);
        if (la != lb) return la ? 1 : -1;

        if (a.IsStandard != b.IsStandard) return a.IsStandard ? -1 : 1;
        if (a.Weight != b.Weight) return b.Weight.CompareTo(a.Weight);

        return string.CompareOrdinal(a.Viet, b.Viet);
    }

    /// <summary>
    /// Dòng có ghi chú "#Loan or homonym" là âm mượn - chữ được mượn để ghi một
    /// tiếng Việt đồng âm, không phải âm Hán Việt của chính nó.
    /// </summary>
    private static bool IsLoan(HanEntry e) =>
        e.Comment is not null &&
        (e.Comment.Contains("loan", StringComparison.OrdinalIgnoreCase) ||
         e.Comment.Contains("homonym", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Chuyển cả đoạn. Chữ Hán ra âm đọc, mọi thứ khác (dấu câu, chữ Latin,
    /// xuống dòng) giữ nguyên tại chỗ.
    /// </summary>
    public HanVietResult Convert(string? text, int maxLength = MaxLength)
    {
        string src = text ?? "";
        bool truncated = false;

        if (src.Length > maxLength)
        {
            src = src[..maxLength];
            // Đừng cắt ngang một cặp thay thế, nếu không chữ cuối thành ký tự hỏng.
            if (src.Length > 0 && char.IsHighSurrogate(src[^1])) src = src[..^1];
            truncated = true;
        }

        var tokens = new List<HanVietToken>();
        var plain = new StringBuilder();
        int hanCount = 0, unknown = 0;

        void FlushPlain()
        {
            if (plain.Length == 0) return;
            tokens.Add(new HanVietToken { Source = plain.ToString(), IsHan = false, Readings = [] });
            plain.Clear();
        }

        for (int i = 0; i < src.Length;)
        {
            int len = HanText.RuneLength(src, i);
            int cp = HanText.CodePointAt(src, i);
            string piece = src.Substring(i, len);

            if (cp >= 0 && HanText.IsHan(cp))
            {
                FlushPlain();

                var readings = ReadingsOf(piece);
                tokens.Add(new HanVietToken { Source = piece, IsHan = true, Readings = readings });

                hanCount++;
                if (readings.Count == 0) unknown++;
            }
            else
            {
                plain.Append(piece);
            }

            i += len;
        }

        FlushPlain();

        var result = new HanVietResult
        {
            Source = src,
            Tokens = tokens,
            HanCount = hanCount,
            UnknownCount = unknown,
            Truncated = truncated,
        };

        result.Render();
        return result;
    }
}
