namespace KeyGoHanViet.Core;

/// <summary>
/// Nhận diện ký tự chữ Hán Nôm.
///
/// Đối xứng với <see cref="VietText"/>: lớp kia lo phía chữ Quốc ngữ, lớp này lo
/// phía chữ vuông. Không dùng <see cref="char.IsLetter(char)"/> được vì hàm đó
/// nhận cả chữ Việt, chữ Nhật, chữ Hàn.
///
/// Xét theo <b>điểm mã</b> chứ không theo từng <c>char</c>: rất nhiều chữ Nôm
/// nằm ngoài mặt phẳng cơ bản (𡨸, 𣛠, 𡗶) nên trong chuỗi .NET chúng chiếm hai
/// <c>char</c> - một cặp thay thế. Duyệt từng <c>char</c> là cắt đôi những chữ đó.
/// </summary>
public static class HanText
{
    /// <summary>true nếu điểm mã nằm trong các khối chữ vuông.</summary>
    public static bool IsHan(int codePoint) => codePoint switch
    {
        0x3005 or 0x3007 => true,                       // 々 dấu lặp, 〇 số không
        >= 0x2E80 and <= 0x2FDF => true,                // bộ thủ CJK + bộ thủ Khang Hi
        >= 0x3400 and <= 0x4DBF => true,                // Ext A
        >= 0x4E00 and <= 0x9FFF => true,                // khối cơ bản
        >= 0xF900 and <= 0xFAFF => true,                // dạng tương thích
        >= 0x20000 and <= 0x2FA1F => true,              // Ext B…F + tương thích bổ sung
        >= 0x30000 and <= 0x323AF => true,              // Ext G, H
        _ => false,
    };

    /// <summary>
    /// Độ dài (tính bằng <c>char</c>) của điểm mã bắt đầu ở vị trí
    /// <paramref name="index"/>: 2 cho một cặp thay thế hợp lệ, 1 cho phần còn lại.
    /// </summary>
    public static int RuneLength(string s, int index) =>
        index + 1 < s.Length && char.IsHighSurrogate(s[index]) && char.IsLowSurrogate(s[index + 1]) ? 2 : 1;

    /// <summary>
    /// Điểm mã bắt đầu ở <paramref name="index"/>, hoặc -1 nếu gặp nửa cặp thay
    /// thế đứng lẻ - chuyện vẫn xảy ra với văn bản cắt dở từ ứng dụng khác.
    /// </summary>
    public static int CodePointAt(string s, int index)
    {
        if (RuneLength(s, index) == 2) return char.ConvertToUtf32(s[index], s[index + 1]);
        return char.IsSurrogate(s[index]) ? -1 : s[index];
    }

    /// <summary>true nếu chuỗi đúng bằng một chữ Hán Nôm, không thừa ký tự nào.</summary>
    public static bool IsSingleHanChar(string s)
    {
        if (s.Length == 0) return false;
        int len = RuneLength(s, 0);
        if (len != s.Length) return false;
        int cp = CodePointAt(s, 0);
        return cp >= 0 && IsHan(cp);
    }

    /// <summary>Số chữ Hán Nôm có trong chuỗi.</summary>
    public static int CountHan(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i += RuneLength(s, i))
        {
            int cp = CodePointAt(s, i);
            if (cp >= 0 && IsHan(cp)) count++;
        }
        return count;
    }

    /// <summary>true nếu chuỗi có ít nhất một chữ Hán Nôm.</summary>
    public static bool ContainsHan(string s)
    {
        for (int i = 0; i < s.Length; i += RuneLength(s, i))
        {
            int cp = CodePointAt(s, i);
            if (cp >= 0 && IsHan(cp)) return true;
        }
        return false;
    }
}
