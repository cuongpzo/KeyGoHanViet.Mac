using System.Runtime.InteropServices;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Khai báo P/Invoke sang các framework của macOS.
///
/// Đây là chỗ tương ứng với <c>Interop\NativeMethods.cs</c> của bản Windows.
/// Ba nhóm hàm được dùng:
///
///   CoreGraphics       bắt phím (CGEventTap) và bơm phím ra (CGEventPost)
///   ApplicationServices  hỏi vị trí con trỏ nhập liệu qua API Trợ năng (AX)
///   CoreFoundation     kiểu chuỗi/mảng dùng chung của hai nhóm trên
///
/// <b>Hàm nào nhận hoặc trả <c>char[]</c> thì bắt buộc phải khai
/// <c>CharSet = CharSet.Unicode</c>.</b> Mặc định của <c>DllImport</c> là
/// <c>CharSet.Ansi</c>, khi đó .NET không truyền thẳng mảng mà chuyển nó sang
/// dãy một byte mỗi chữ - trên macOS là UTF-8. Mọi API dưới đây lại nhận
/// <c>UniChar*</c>, tức UTF-16 hai byte mỗi đơn vị, nên hai bên hiểu lệch nhau:
/// chữ 字 gửi đi thành ba byte E5 AD 97, phía kia đọc hai byte đầu thành U+ADE5
/// '귥'. Đó là lý do chữ Hán chèn ra thành chữ Hàn Quốc hoặc ký tự vô nghĩa.
///
/// Chuỗi khai bằng <c>UnmanagedType.LPStr</c> thì không dính: đó là những tên
/// lớp/ký hiệu ASCII của Objective-C, vốn đúng là chuỗi một byte thật.
/// </summary>
internal static class NativeMethods
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string LibObjC = "/usr/lib/libobjc.dylib";

    // ------------------------------------------------------------ kiểu dựng sẵn

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CFRange
    {
        public nint Location;
        public nint Length;
    }

    // ------------------------------------------------------- CGEventTap: bắt phím

    /// <summary>Bắt ở tầng phiên đăng nhập - đủ thấp để nuốt phím trước khi ứng dụng đích thấy.</summary>
    internal const uint kCGSessionEventTap = 1;

    /// <summary>Chèn lên đầu chuỗi tap, để mình được xem phím trước các tap khác.</summary>
    internal const uint kCGHeadInsertEventTap = 0;

    /// <summary>Tap được phép sửa và nuốt sự kiện (khác với chế độ chỉ nghe).</summary>
    internal const uint kCGEventTapOptionDefault = 0;

    internal const uint kCGEventKeyDown = 10;
    internal const uint kCGEventKeyUp = 11;
    internal const uint kCGEventFlagsChanged = 12;

    /// <summary>Hệ thống tự tắt tap khi hàm xử lý chạy quá lâu; phải bật lại.</summary>
    internal const uint kCGEventTapDisabledByTimeout = 0xFFFFFFFE;

    /// <summary>Hệ thống tắt tap theo yêu cầu của người dùng; cũng bật lại được.</summary>
    internal const uint kCGEventTapDisabledByUserInput = 0xFFFFFFFF;

    // Các trường đọc được từ một CGEvent.
    internal const uint kCGKeyboardEventKeycode = 9;

    /// <summary>
    /// Chỗ gắn dấu riêng của chương trình, tương đương <c>dwExtraInfo</c> của
    /// Windows: nhờ nó mà tap nhận ra phím do chính mình bơm ra và bỏ qua,
    /// không thì sẽ tự gõ lại vô hạn.
    /// </summary>
    internal const uint kCGEventSourceUserData = 42;

    // Cờ phím bổ trợ trong CGEventFlags.
    internal const ulong kCGEventFlagMaskAlphaShift = 0x00010000;   // CapsLock
    internal const ulong kCGEventFlagMaskShift = 0x00020000;
    internal const ulong kCGEventFlagMaskControl = 0x00040000;
    internal const ulong kCGEventFlagMaskAlternate = 0x00080000;    // phím Option (= Alt)
    internal const ulong kCGEventFlagMaskCommand = 0x00100000;      // phím ⌘ (= phím Windows)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    internal static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options,
                                                   ulong eventsOfInterest,
                                                   CGEventTapCallBack callback, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    internal static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);

    [DllImport(CoreGraphics)]
    internal static extern long CGEventGetIntegerValueField(IntPtr @event, uint field);

    [DllImport(CoreGraphics)]
    internal static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport(CoreGraphics)]
    internal static extern CGPoint CGEventGetLocation(IntPtr @event);

    // -------------------------------------------------- CGEvent: bơm phím ra ngoài

    internal const int kCGEventSourceStateHIDSystemState = 1;

    [DllImport(CoreGraphics)]
    internal static extern IntPtr CGEventSourceCreate(int stateID);

    /// <summary>Gắn dấu nhận dạng cho mọi sự kiện sinh ra từ nguồn này.</summary>
    [DllImport(CoreGraphics)]
    internal static extern void CGEventSourceSetUserData(IntPtr source, long userData);

    [DllImport(CoreGraphics)]
    internal static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey,
                                                             [MarshalAs(UnmanagedType.I1)] bool keyDown);

    /// <summary>
    /// Gắn chuỗi Unicode vào một sự kiện phím.
    ///
    /// <b>Bắt buộc phải nêu <c>CharSet.Unicode</c>.</b> Không nêu thì mặc định là
    /// <c>CharSet.Ansi</c>, và khi đó .NET tự chuyển <c>char[]</c> sang dãy byte
    /// một byte mỗi chữ - trên macOS là UTF-8. CoreGraphics lại đọc tham số này
    /// như <c>UniChar*</c> (UTF-16), nên chữ 字 gửi đi thành E5 AD 97 rồi được đọc
    /// lại thành U+ADE5 '귥': đúng kiểu "gõ ra chữ Hàn Quốc". Xem thêm ghi chú
    /// chung ở đầu tệp.
    /// </summary>
    [DllImport(CoreGraphics, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, nint stringLength,
                                                                [In] char[] unicodeString);

    /// <summary>
    /// Đọc chuỗi Unicode gắn kèm một sự kiện phím.
    ///
    /// Cần cho chiều ngược lại của <see cref="CGEventKeyboardSetUnicodeString"/>:
    /// bộ gõ Việt khác đang chạy sẵn cũng bơm chữ có dấu ra bằng cách gắn chuỗi
    /// vào sự kiện, mà những sự kiện đó mang mã phím ảo 0 nên không thể nhận ra
    /// chữ nếu chỉ nhìn mã phím.
    ///
    /// Ở chiều này, thiếu <c>CharSet.Unicode</c> còn nguy hiểm hơn chiều gửi: bộ
    /// sắp xếp cấp một vùng đệm chỉ <i>maxStringLength</i> byte, trong khi
    /// CoreGraphics ghi vào đó tới <i>2 × maxStringLength</i> byte.
    /// </summary>
    [DllImport(CoreGraphics, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern void CGEventKeyboardGetUnicodeString(IntPtr @event, nint maxStringLength,
                                                                out nint actualStringLength,
                                                                [Out] char[] unicodeString);

    [DllImport(CoreGraphics)]
    internal static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(CoreGraphics)]
    internal static extern void CGEventPost(uint tap, IntPtr @event);

    [DllImport(CoreGraphics)]
    internal static extern IntPtr CGEventCreate(IntPtr source);

    // ------------------------------------------------------------ CoreFoundation

    internal static readonly IntPtr kCFRunLoopCommonModes = GetCFStringConstant("kCFRunLoopCommonModes");

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFRunLoopGetMain();

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    internal static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    internal static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFRetain(IntPtr cf);

    /// <summary>Cũng nhận <c>UniChar*</c> - xem ghi chú CharSet ở đầu tệp.</summary>
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, [In] char[] chars, nint numChars);

    [DllImport(CoreFoundation)]
    internal static extern nint CFStringGetLength(IntPtr theString);

    /// <summary>Ghi ra <c>UniChar*</c> - xem ghi chú CharSet ở đầu tệp.</summary>
    [DllImport(CoreFoundation, CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern void CFStringGetCharacters(IntPtr theString, CFRange range, [Out] char[] buffer);

    [DllImport(CoreFoundation)]
    internal static extern IntPtr CFMachPortInvalidate(IntPtr port);

    // --------------------------------------------- ApplicationServices: Trợ năng

    internal const int kAXValueTypeCGRect = 3;
    internal const int kAXValueTypeCFRange = 4;

    /// <summary>true nếu người dùng đã bật quyền Trợ năng cho chương trình này.</summary>
    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    internal static extern IntPtr AXUIElementCreateSystemWide();

    [DllImport(ApplicationServices)]
    internal static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);

    [DllImport(ApplicationServices)]
    internal static extern int AXUIElementCopyParameterizedAttributeValue(IntPtr element, IntPtr attribute,
                                                                          IntPtr parameter, out IntPtr result);

    [DllImport(ApplicationServices)]
    internal static extern IntPtr AXValueCreate(int theType, ref CFRange valuePtr);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXValueGetValue(IntPtr value, int theType, out CGRect valuePtr);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool AXValueGetValue(IntPtr value, int theType, out CFRange valuePtr);

    // ------------------------------------------------------------- runtime ObjC

    [DllImport(LibObjC)]
    internal static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibObjC)]
    internal static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    /// <summary>
    /// Bản trả về số nguyên 32 bit (pid_t chẳng hạn). Cùng lý do với bản trả
    /// BOOL ngay dưới: giá trị chỉ chiếm nửa thấp của thanh ghi kết quả, đọc
    /// nguyên cả IntPtr 64 bit là vơ luôn cả những bit không được định nghĩa.
    /// </summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    internal static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

    /// <summary>
    /// Bản trả về BOOL. Phải khai báo riêng chứ không đọc lại giá trị IntPtr:
    /// BOOL của ObjC chỉ chiếm một byte thấp của thanh ghi kết quả, các bit còn
    /// lại không được định nghĩa nên so sánh nguyên cả 64 bit là sai.
    /// </summary>
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    // ------------------------------------------------------------------ tiện ích

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlopen([MarshalAs(UnmanagedType.LPStr)] string path, int mode);

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string symbol);

    /// <summary>
    /// Đọc một hằng chuỗi CFString do CoreFoundation xuất ra.
    ///
    /// Không khai báo trực tiếp bằng P/Invoke được vì đây là <b>biến</b> chứ không
    /// phải hàm: ký hiệu trỏ tới một con trỏ, nên phải dlsym rồi giải tham chiếu
    /// thêm một lần nữa.
    /// </summary>
    private static IntPtr GetCFStringConstant(string symbol)
    {
        const int RTLD_LAZY = 1;
        IntPtr handle = dlopen(CoreFoundation, RTLD_LAZY);
        if (handle == IntPtr.Zero) return IntPtr.Zero;

        IntPtr address = dlsym(handle, symbol);
        return address == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(address);
    }

    /// <summary>Dựng một CFString từ chuỗi .NET. Người gọi phải tự <see cref="CFRelease"/>.</summary>
    internal static IntPtr CFString(string value)
    {
        char[] chars = value.ToCharArray();
        return CFStringCreateWithCharacters(IntPtr.Zero, chars, chars.Length);
    }

    /// <summary>Đọc một CFString về chuỗi .NET. Không giải phóng đối tượng gốc.</summary>
    internal static string CFStringToManaged(IntPtr cfString)
    {
        if (cfString == IntPtr.Zero) return "";

        nint length = CFStringGetLength(cfString);
        if (length <= 0) return "";

        char[] buffer = new char[length];
        CFStringGetCharacters(cfString, new CFRange { Location = 0, Length = length }, buffer);
        return new string(buffer);
    }
}
