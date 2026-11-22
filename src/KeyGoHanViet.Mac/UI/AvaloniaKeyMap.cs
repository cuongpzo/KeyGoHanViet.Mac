using Avalonia.Input;
using KeyGoHanViet.Core;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Ánh xạ phím của Avalonia sang <see cref="VKey"/>.
///
/// Chỉ dùng cho ô đặt phím tắt trong cửa sổ tuỳ chọn - luồng gõ thật đi qua
/// <c>MacKeyMap</c> vì nó bắt phím ở tầng hệ điều hành chứ không qua Avalonia.
///
/// Cần bảng riêng vì <c>Avalonia.Input.Key</c> đánh số theo enum của WPF, không
/// trùng mã phím ảo của Windows mà <c>VKey</c> đang dùng. Viết bằng tên phím
/// chứ không bằng số để trình biên dịch bắt lỗi giúp nếu Avalonia đổi enum.
/// </summary>
internal static class AvaloniaKeyMap
{
    internal static VKey ToModifiers(KeyModifiers modifiers)
    {
        VKey result = VKey.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= VKey.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= VKey.Alt;      // ⌥ Option
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= VKey.Shift;
        return result;
    }

    internal static VKey ToVKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => VKey.A + (key - Key.A),
        >= Key.D0 and <= Key.D9 => VKey.D0 + (key - Key.D0),
        >= Key.NumPad0 and <= Key.NumPad9 => VKey.NumPad0 + (key - Key.NumPad0),
        >= Key.F1 and <= Key.F24 => VKey.F1 + (key - Key.F1),

        Key.Space => VKey.Space,
        Key.Tab => VKey.Tab,
        Key.Enter => VKey.Enter,
        Key.Escape => VKey.Escape,
        Key.Back => VKey.Back,
        Key.Delete => VKey.Delete,
        Key.Insert => VKey.Insert,
        Key.Home => VKey.Home,
        Key.End => VKey.End,
        Key.PageUp => VKey.PageUp,
        Key.PageDown => VKey.PageDown,
        Key.Left => VKey.Left,
        Key.Right => VKey.Right,
        Key.Up => VKey.Up,
        Key.Down => VKey.Down,
        Key.CapsLock => VKey.Capital,

        Key.OemSemicolon => VKey.OemSemicolon,
        Key.OemPlus => VKey.Oemplus,
        Key.OemComma => VKey.Oemcomma,
        Key.OemMinus => VKey.OemMinus,
        Key.OemPeriod => VKey.OemPeriod,
        Key.OemQuestion => VKey.OemQuestion,
        Key.OemTilde => VKey.Oemtilde,
        Key.OemOpenBrackets => VKey.OemOpenBrackets,
        Key.OemPipe => VKey.OemPipe,
        Key.OemCloseBrackets => VKey.OemCloseBrackets,
        Key.OemQuotes => VKey.OemQuotes,
        Key.OemBackslash => VKey.OemBackslash,

        Key.Multiply => VKey.Multiply,
        Key.Add => VKey.Add,
        Key.Subtract => VKey.Subtract,
        Key.Decimal => VKey.Decimal,
        Key.Divide => VKey.Divide,

        // Phím bổ trợ: trả mã trái/phải để HotkeySpec.IsModifierOnly nhận ra và
        // chờ tiếp phím chính, đúng như bản Windows.
        Key.LeftShift => VKey.LShiftKey,
        Key.RightShift => VKey.RShiftKey,
        Key.LeftCtrl => VKey.LControlKey,
        Key.RightCtrl => VKey.RControlKey,
        Key.LeftAlt => VKey.LMenu,
        Key.RightAlt => VKey.RMenu,
        Key.LWin => VKey.LWin,
        Key.RWin => VKey.RWin,

        _ => VKey.None,
    };
}
