namespace KeyGoHanViet.Core;

/// <summary>
/// Đọc, ghi và so khớp tổ hợp phím tắt do người dùng tự đặt.
///
/// Tổ hợp được biểu diễn bằng <see cref="VKey"/>: phần <c>VKey.KeyCode</c> là phím
/// chính, phần <c>VKey.Modifiers</c> là Ctrl/Alt/Shift. Phím Windows (⌘ trên macOS)
/// không dùng làm phím bổ trợ vì hệ điều hành đã giữ hầu hết tổ hợp có phím đó.
/// </summary>
public static class HotkeySpec
{
    /// <summary>Chuỗi rỗng hoặc không đọc được thì trả <see cref="VKey.None"/>.</summary>
    public static VKey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return VKey.None;
        return Enum.TryParse<VKey>(text, ignoreCase: true, out var k) ? k : VKey.None;
    }

    public static string Format(VKey hotkey) => hotkey == VKey.None ? "" : hotkey.ToString();

    /// <summary>Tên hiển thị cho người dùng, ví dụ "Ctrl + Alt + H".</summary>
    public static string Describe(VKey hotkey)
    {
        if (hotkey == VKey.None) return "(chưa đặt)";

        var parts = new List<string>(4);
        if (hotkey.HasFlag(VKey.Control)) parts.Add("Ctrl");
        if (hotkey.HasFlag(VKey.Alt)) parts.Add("Alt");
        if (hotkey.HasFlag(VKey.Shift)) parts.Add("Shift");

        VKey code = hotkey & VKey.KeyCode;
        if (code != VKey.None) parts.Add(DescribeKeyCode(code));

        return string.Join(" + ", parts);
    }

    private static string DescribeKeyCode(VKey code) => code switch
    {
        >= VKey.D0 and <= VKey.D9 => ((char)('0' + (code - VKey.D0))).ToString(),
        >= VKey.NumPad0 and <= VKey.NumPad9 => "Num " + (code - VKey.NumPad0),
        VKey.Oemtilde => "`",
        VKey.OemMinus => "-",
        VKey.Oemplus => "=",
        VKey.OemOpenBrackets => "[",
        VKey.OemCloseBrackets => "]",
        VKey.OemPipe => "\\",
        VKey.OemSemicolon => ";",
        VKey.OemQuotes => "'",
        VKey.Oemcomma => ",",
        VKey.OemPeriod => ".",
        VKey.OemQuestion => "/",
        VKey.Capital => "CapsLock",
        VKey.Space => "Space",
        _ => code.ToString(),
    };

    /// <summary>true nếu <paramref name="code"/> chỉ là phím bổ trợ, không đứng riêng làm phím tắt được.</summary>
    public static bool IsModifierOnly(VKey code) => (code & VKey.KeyCode) is
        VKey.ControlKey or VKey.LControlKey or VKey.RControlKey or
        VKey.ShiftKey or VKey.LShiftKey or VKey.RShiftKey or
        VKey.Menu or VKey.LMenu or VKey.RMenu or
        VKey.LWin or VKey.RWin or VKey.None;

    /// <summary>
    /// Gộp trái/phải về phím chung: LShiftKey -> ShiftKey. Hook cấp thấp báo mã
    /// phân biệt trái phải, còn tổ hợp lưu trong cấu hình thì không.
    /// </summary>
    public static VKey Normalize(VKey key) => key switch
    {
        VKey.LControlKey or VKey.RControlKey => VKey.ControlKey,
        VKey.LShiftKey or VKey.RShiftKey => VKey.ShiftKey,
        VKey.LMenu or VKey.RMenu => VKey.Menu,
        _ => key,
    };

    /// <summary>
    /// So khớp một sự kiện phím với tổ hợp đã đặt. Yêu cầu bộ phím bổ trợ trùng
    /// khít, nếu không thì Ctrl+Alt+H sẽ ăn cả Ctrl+Shift+Alt+H.
    /// </summary>
    public static bool Matches(VKey hotkey, VKey pressedKey, bool control, bool alt, bool shift)
    {
        if (hotkey == VKey.None) return false;

        VKey wanted = hotkey & VKey.KeyCode;
        if (wanted == VKey.None || Normalize(pressedKey) != wanted) return false;

        VKey wantedMods = hotkey & VKey.Modifiers;
        VKey actualMods = (control ? VKey.Control : VKey.None)
                        | (alt ? VKey.Alt : VKey.None)
                        | (shift ? VKey.Shift : VKey.None);

        return wantedMods == actualMods;
    }
}
