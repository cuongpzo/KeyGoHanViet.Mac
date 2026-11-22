using Avalonia.Controls;
using Avalonia.Input;
using KeyGoHanViet.Core;

namespace KeyGoHanViet.Mac.UI;

/// <summary>
/// Ô nhận phím tắt: người dùng bấm thẳng tổ hợp muốn dùng, ô hiện lại tên tổ hợp
/// đó thay vì gõ chữ vào. Bản macOS của <c>UI\HotkeyTextBox.cs</c> bên Windows.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private VKey _hotkey = VKey.None;

    public HotkeyBox()
    {
        IsReadOnly = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        Text = HotkeySpec.Describe(VKey.None);
    }

    /// <summary>
    /// Bộ giao diện Fluent tra khuôn dựng theo ĐÚNG kiểu của điều khiển, nên lớp
    /// kế thừa TextBox mà không khai lại khoá này thì tra ra khoá "HotkeyBox",
    /// không khớp mục nào, hết template và ô vẽ ra cao 0 pixel — nhìn như thể
    /// không có ô nào. Khác với Window: lớp đó tự khai lại khoá nên OptionsWindow
    /// kế thừa vẫn có giao diện bình thường.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TextBox);

    public event EventHandler? HotkeyChanged;

    public VKey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Text = HotkeySpec.Describe(value);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Nuốt hết mọi phím: ô này để bấm tổ hợp, không để gõ chữ, và cũng
        // không được để Tab hay mũi tên nhảy sang điều khiển khác.
        e.Handled = true;

        // Backspace / Delete để xoá tổ hợp đang đặt.
        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            Hotkey = VKey.None;
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        VKey code = AvaloniaKeyMap.ToVKey(e.Key);
        VKey mods = AvaloniaKeyMap.ToModifiers(e.KeyModifiers);

        // Bấm riêng Ctrl/Alt/Shift thì chưa thành tổ hợp - chờ phím chính.
        if (code == VKey.None || HotkeySpec.IsModifierOnly(code))
        {
            Text = HotkeySpec.Describe(mods) + " + …";
            return;
        }

        Hotkey = code | mods;
        HotkeyChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        // Thả phím bổ trợ mà chưa chọn được phím chính thì trả về tổ hợp cũ.
        VKey code = AvaloniaKeyMap.ToVKey(e.Key);
        if (code == VKey.None || HotkeySpec.IsModifierOnly(code)) Text = HotkeySpec.Describe(_hotkey);
        e.Handled = true;
    }

    protected override void OnGotFocus(Avalonia.Input.GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Text = "Hãy bấm tổ hợp phím…";
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Text = HotkeySpec.Describe(_hotkey);
    }
}
