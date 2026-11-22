using KeyGoHanViet.Core;
using KeyGoHanViet.Platform;
using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Hook bàn phím toàn hệ thống bằng CGEventTap - bản macOS của
/// <see cref="IKeyboardHook"/>, thay cho WH_KEYBOARD_LL của Windows.
///
/// Ba điểm khác biệt đáng kể so với bản Windows:
///
///   1. <b>Quyền.</b> macOS chỉ cho đặt tap khi người dùng đã tự tay bật quyền
///      Trợ năng cho chương trình trong Cài đặt Hệ thống. Không có quyền thì
///      <see cref="CGEventTapCreate"/> trả về con trỏ rỗng.
///
///   2. <b>Phím bổ trợ.</b> Shift/Control/Option/Command không sinh ra sự kiện
///      nhấn-thả như phím thường mà đi qua một loại sự kiện riêng
///      (<c>FlagsChanged</c>) chỉ báo "bộ cờ vừa đổi". Lớp này tự dựng lại cặp
///      nhấn/thả cho chúng, nhờ vậy quy tắc "gõ nhẹ Shift để đổi chế độ" dùng
///      chung được đúng đoạn mã của bản Windows.
///
///   3. <b>Tap bị tắt giữa chừng.</b> Nếu hàm xử lý chạy quá lâu, macOS lặng lẽ
///      vô hiệu hoá tap và gửi vào một sự kiện báo hiệu. Không bật lại thì bộ gõ
///      chết câm mà không có lỗi nào. Lớp này bắt và bật lại ngay.
/// </summary>
public sealed class MacKeyboardHook : IKeyboardHook
{
    /// <summary>Dấu nhận dạng phím do chính chương trình bơm ra, để tap bỏ qua.</summary>
    public const long InjectedMarker = 0x4B47_484E;   // "KGHN"

    private readonly CGEventTapCallBack _callback;    // giữ tham chiếu để GC không thu hồi

    private IntPtr _tap = IntPtr.Zero;
    private IntPtr _runLoopSource = IntPtr.Zero;

    public event EventHandler<HookedKeyEventArgs>? KeyEvent;

    public bool IsInstalled => _tap != IntPtr.Zero;

    public MacKeyboardHook() => _callback = HookCallback;

    public void Install()
    {
        if (IsInstalled) return;

        if (!AXIsProcessTrusted())
            throw new InvalidOperationException(
                "KeyGo chưa được cấp quyền Trợ năng nên không đọc được bàn phím.\n\n" +
                "Mở Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng, " +
                "rồi bật KeyGo Hán Việt trong danh sách.");

        ulong mask = (1UL << (int)kCGEventKeyDown)
                   | (1UL << (int)kCGEventKeyUp)
                   | (1UL << (int)kCGEventFlagsChanged);

        _tap = CGEventTapCreate(kCGSessionEventTap, kCGHeadInsertEventTap,
                                kCGEventTapOptionDefault, mask, _callback, IntPtr.Zero);

        if (_tap == IntPtr.Zero)
            throw new InvalidOperationException(
                "Không đặt được bộ bắt phím của hệ thống.\n\n" +
                "Thường là do quyền Trợ năng vừa bị thu hồi. Hãy tắt rồi bật lại " +
                "KeyGo Hán Việt trong Cài đặt Hệ thống ▸ Quyền riêng tư & Bảo mật ▸ Trợ năng.");

        _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _tap, 0);
        CFRunLoopAddSource(CFRunLoopGetMain(), _runLoopSource, kCFRunLoopCommonModes);
        CGEventTapEnable(_tap, true);
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;

        CGEventTapEnable(_tap, false);

        if (_runLoopSource != IntPtr.Zero)
        {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), _runLoopSource, kCFRunLoopCommonModes);
            CFRelease(_runLoopSource);
            _runLoopSource = IntPtr.Zero;
        }

        CFMachPortInvalidate(_tap);
        CFRelease(_tap);
        _tap = IntPtr.Zero;
    }

    // ------------------------------------------------------------ hàm xử lý

    private IntPtr HookCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo)
    {
        // Tap vừa bị hệ thống tắt: bật lại rồi cho sự kiện đi tiếp.
        if (type is kCGEventTapDisabledByTimeout or kCGEventTapDisabledByUserInput)
        {
            if (_tap != IntPtr.Zero) CGEventTapEnable(_tap, true);
            return @event;
        }

        // Bỏ qua chính các phím do chương trình bơm ra, tránh vòng lặp vô hạn.
        if (CGEventGetIntegerValueField(@event, kCGEventSourceUserData) == InjectedMarker)
            return @event;

        long macCode = CGEventGetIntegerValueField(@event, kCGKeyboardEventKeycode);
        VKey key = MacKeyMap.ToVKey(macCode);

        // Chữ có dấu do bộ gõ khác bơm vào đi kèm mã phím ảo 0, mà 0 lại đúng là
        // mã của phím A - nên không phân biệt được bằng mã phím, buộc phải đọc
        // chuỗi Unicode của mọi sự kiện nhấn.
        char character = type == kCGEventKeyDown ? ReadCharacter(@event) : '\0';

        if (key == VKey.None && character == '\0') return @event;

        ulong flags = CGEventGetFlags(@event);

        bool isDown;
        if (type == kCGEventFlagsChanged)
        {
            // Sự kiện này chỉ nói "bộ cờ vừa đổi", không nói nhấn hay thả.
            // Suy ra bằng cách xem cờ tương ứng với phím đó hiện còn bật không.
            ulong ownMask = MaskOf(macCode);
            if (ownMask == 0) return @event;
            isDown = (flags & ownMask) != 0;
        }
        else
        {
            isDown = type == kCGEventKeyDown;
        }

        var args = new HookedKeyEventArgs
        {
            Key = key,
            Character = character,
            IsKeyDown = isDown,
            Shift = (flags & kCGEventFlagMaskShift) != 0,
            Control = (flags & kCGEventFlagMaskControl) != 0,
            Alt = (flags & kCGEventFlagMaskAlternate) != 0,      // ⌥ Option
            Win = (flags & kCGEventFlagMaskCommand) != 0,        // ⌘ Command
            CapsLock = (flags & kCGEventFlagMaskAlphaShift) != 0,
        };

        try
        {
            KeyEvent?.Invoke(this, args);
        }
        catch (Exception)
        {
            // Ngoại lệ thoát ra khỏi hàm này sẽ làm hỏng tap - luôn nuốt lỗi ở đây.
            args.Handled = false;
        }

        // Trả về con trỏ rỗng để nuốt phím, không cho ứng dụng đích nhận.
        return args.Handled ? IntPtr.Zero : @event;
    }

    /// <summary>
    /// Ký tự Unicode gắn kèm sự kiện, hoặc '\0' nếu không có.
    ///
    /// Chỉ nhận kết quả dài đúng một đơn vị mã: chữ Quốc ngữ có dấu nào cũng nằm
    /// gọn trong một đơn vị, còn chuỗi dài hơn là do phím chức năng hay do một
    /// ứng dụng khác bơm cả cụm - không phải thứ cần đưa vào bộ đệm gõ.
    /// </summary>
    private static char ReadCharacter(IntPtr @event)
    {
        char[] buffer = new char[4];
        CGEventKeyboardGetUnicodeString(@event, buffer.Length, out nint actual, buffer);
        return actual == 1 ? buffer[0] : '\0';
    }

    /// <summary>Cờ trong CGEventFlags ứng với từng phím bổ trợ, dùng để suy ra nhấn hay thả.</summary>
    private static ulong MaskOf(long macKeyCode) => macKeyCode switch
    {
        0x37 or 0x36 => kCGEventFlagMaskCommand,      // ⌘ trái / phải
        0x38 or 0x3C => kCGEventFlagMaskShift,
        0x3A or 0x3D => kCGEventFlagMaskAlternate,    // ⌥ trái / phải
        0x3B or 0x3E => kCGEventFlagMaskControl,
        0x39 => kCGEventFlagMaskAlphaShift,           // CapsLock
        _ => 0,
    };

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
