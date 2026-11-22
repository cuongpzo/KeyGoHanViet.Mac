using System.Globalization;
using System.Text;

namespace KeyGoHanViet.Core;

/// <summary>
/// Chuẩn hoá chuỗi Quốc ngữ có dấu để dùng làm khoá tra cứu.
///
/// Cột thứ ba của từ điển v2 ghi âm đọc có dấu ("hán việt từ điển trích dẫn").
/// Muốn tra được bằng chính chuỗi đó - dù người dùng gõ ra hay dán vào - thì
/// hai bên phải quy về cùng một dạng, vì cùng một chữ có nhiều cách viết khác
/// nhau mà mắt thường không phân biệt được:
///
///   • Tổ hợp Unicode. "ế" viết liền một mã (U+1EBF) hay viết rời thành
///     "e" + dấu mũ + dấu sắc (U+0065 U+0302 U+0301) đều hiện ra như nhau.
///     Văn bản dán từ web hoặc từ macOS rất hay ở dạng rời.
///   • Hoa/thường và khoảng trắng. "Hán Việt" và "hán  việt" phải cùng khoá.
/// </summary>
public static class VietText
{
    /// <summary>
    /// true nếu ký tự là chữ Latin có dấu, hoặc là dấu phụ đứng rời của dạng NFD.
    ///
    /// Xét theo khối Unicode chứ không dùng <see cref="char.IsLetter(char)"/>:
    /// hàm đó nhận cả chữ Hán, chữ Nhật, chữ Hàn, mà ở đây cần phân biệt đúng
    /// "Quốc ngữ có dấu" với mọi thứ chữ khác.
    /// </summary>
    public static bool IsVietnameseLetter(char c) => c
        is >= 'À' and <= 'ɏ'    // Latin-1 mở rộng + Latin Ext-A/B: à â ă đ ơ ư…
        or >= '̀' and <= 'ͯ'    // dấu phụ đứng rời, gặp ở văn bản dạng NFD
        or >= 'Ḁ' and <= 'ỿ';   // Latin Extended Additional: phần lớn chữ Việt có dấu

    /// <summary>
    /// true nếu chuỗi có ít nhất một chữ Quốc ngữ mang dấu - dấu hiệu nhận biết
    /// đây là âm đọc chứ không phải chuỗi TELEX thô.
    /// </summary>
    public static bool ContainsDiacritics(string s)
    {
        foreach (char c in s)
            if (IsVietnameseLetter(c)) return true;
        return false;
    }

    /// <summary>
    /// Khoá tra cứu: dựng lại tổ hợp về dạng liền (NFC), hạ chữ thường, rồi chỉ
    /// giữ lại chữ cái. Khoảng trắng và dấu câu bị bỏ hẳn nên "hán việt từ điển
    /// trích dẫn", "Hán Việt Từ Điển Trích Dẫn" và "hán-việt, từ-điển…" cho ra
    /// cùng một khoá.
    /// </summary>
    public static string NormalizeKey(string s)
    {
        if (s.Length == 0) return "";

        string nfc = ToNfc(s).ToLowerInvariant();

        var sb = new StringBuilder(nfc.Length);
        foreach (char c in nfc)
            if (char.IsLetter(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// Bỏ toàn bộ dấu thanh và dấu mũ/móc: "hán việt" -> "han viet".
    /// Dùng để chuỗi có dấu vẫn tra được qua chỉ mục không dấu sẵn có, phòng khi
    /// mục từ đó không có cột Quốc ngữ trong từ điển.
    /// </summary>
    public static string RemoveDiacritics(string s)
    {
        if (s.Length == 0) return "";

        // Tách rời (NFD) thì mọi dấu thanh, dấu mũ, dấu móc đều thành ký tự
        // dấu-không-chiếm-chỗ đứng riêng, bỏ đi là xong. Riêng đ/Đ là chữ cái
        // độc lập chứ không phải d có dấu nên NFD không đụng tới, phải thay tay.
        string nfd = ToNfd(s);

        var sb = new StringBuilder(nfd.Length);
        foreach (char c in nfd)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
        }
        return sb.ToString();
    }

    /// <summary>
    /// Khoá không dấu tương ứng với chỉ mục <see cref="MatchMode.Plain"/>:
    /// bỏ dấu rồi chỉ giữ a-z. "Hán Việt" -> "hanviet".
    /// </summary>
    public static string ToPlainKey(string s) => TelexCodec.SanitizeInput(RemoveDiacritics(s));

    // Hai hàm chuẩn hoá được bọc lại vì Normalize ném ArgumentException khi chuỗi
    // chứa mã Unicode không hợp lệ - chuyện có thể xảy ra với văn bản dán vào từ
    // ứng dụng khác, và ngoại lệ đó mà lọt ra thì cả bộ gõ chết theo.

    private static string ToNfc(string s)
    {
        try { return s.IsNormalized(NormalizationForm.FormC) ? s : s.Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return s; }
    }

    private static string ToNfd(string s)
    {
        try { return s.IsNormalized(NormalizationForm.FormD) ? s : s.Normalize(NormalizationForm.FormD); }
        catch (ArgumentException) { return s; }
    }
}
