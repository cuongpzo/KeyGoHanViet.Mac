using KeyGoHanViet.Core;

namespace KeyGoHanViet.Platform;

/// <summary>Thông tin một sự kiện phím do hook bắt được.</summary>
public sealed class HookedKeyEventArgs : EventArgs
{
    public required VKey Key { get; init; }
    public required bool IsKeyDown { get; init; }
    public required bool Shift { get; init; }
    public required bool Control { get; init; }
    public required bool Alt { get; init; }

    /// <summary>Phím Windows trên PC, phím Command (⌘) trên macOS.</summary>
    public required bool Win { get; init; }

    public required bool CapsLock { get; init; }

    /// <summary>
    /// Ký tự Unicode mà sự kiện này chở theo, hoặc '\0' nếu không có.
    ///
    /// Khác rỗng khi phím không phải do bàn phím vật lý sinh ra mà do một bộ gõ
    /// khác bơm thẳng chuỗi Unicode vào - chẳng hạn UniKey đang chạy sẵn xuất ra
    /// "á", "ệ". Những sự kiện đó không mang mã phím ảo có nghĩa, nên nếu chỉ
    /// nhìn <see cref="Key"/> thì bộ gõ sẽ không thấy chúng.
    /// </summary>
    public char Character { get; init; }

    /// <summary>Đặt true để nuốt phím, không cho ứng dụng đích nhận.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Hook bàn phím toàn hệ thống.
///
/// Windows hiện thực bằng <c>WH_KEYBOARD_LL</c>; macOS bằng <c>CGEventTap</c>.
/// Hai bên khác nhau ở chỗ xin quyền: Windows chỉ cần luồng có vòng lặp thông
/// điệp, macOS bắt buộc người dùng cấp quyền Trợ năng (Accessibility) trong
/// Cài đặt Hệ thống, nếu không thì <see cref="Install"/> ném ngoại lệ.
/// </summary>
public interface IKeyboardHook : IDisposable
{
    event EventHandler<HookedKeyEventArgs>? KeyEvent;

    bool IsInstalled { get; }

    /// <summary>Cài hook. Ném <see cref="InvalidOperationException"/> kèm lời giải thích cho người dùng nếu hệ điều hành từ chối.</summary>
    void Install();

    void Uninstall();
}
