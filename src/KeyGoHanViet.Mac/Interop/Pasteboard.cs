using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Truy cập clipboard của macOS (NSPasteboard) qua runtime Objective-C.
///
/// Không dùng clipboard của Avalonia vì hàm ở đó chạy bất đồng bộ, mà chỗ gọi
/// nó lại nằm ngay trong hàm xử lý của CGEventTap - chờ ở đó thì macOS coi là
/// tap chạy quá lâu và tắt luôn bộ gõ.
///
/// Chuỗi qua lại đều là CFString: CFString và NSString là hai mặt của cùng một
/// đối tượng ("toll-free bridged") nên truyền thẳng cho ObjC được, khỏi phải
/// chuyển qua UTF-8 và khỏi lo chữ ngoài mặt phẳng cơ bản bị hỏng.
/// </summary>
internal static class Pasteboard
{
    private static readonly IntPtr NSPasteboard = objc_getClass("NSPasteboard");
    private static readonly IntPtr SelGeneralPasteboard = sel_registerName("generalPasteboard");
    private static readonly IntPtr SelClearContents = sel_registerName("clearContents");
    private static readonly IntPtr SelSetStringForType = sel_registerName("setString:forType:");
    private static readonly IntPtr SelStringForType = sel_registerName("stringForType:");
    private static readonly IntPtr SelChangeCount = sel_registerName("changeCount");

    /// <summary>NSPasteboardTypeString. Sống suốt đời chương trình nên cố ý không giải phóng.</summary>
    private static readonly IntPtr TypeString = CFString("public.utf8-plain-text");

    private static IntPtr General =>
        NSPasteboard == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(NSPasteboard, SelGeneralPasteboard);

    /// <summary>
    /// Lấy bản sao nội dung văn bản hiện có, đã giữ tham chiếu.
    /// Người gọi phải tự <c>CFRelease</c>. Trả về con trỏ rỗng nếu clipboard
    /// đang không chứa văn bản.
    /// </summary>
    internal static IntPtr CopyCurrentString()
    {
        IntPtr pb = General;
        if (pb == IntPtr.Zero) return IntPtr.Zero;

        IntPtr current = objc_msgSend(pb, SelStringForType, TypeString);

        // Chuỗi nhận về đang nằm trong autorelease pool của vòng lặp chạy hiện
        // tại. Phải giữ tham chiếu vì nó cần sống qua 400 ms chờ dán xong.
        return current == IntPtr.Zero ? IntPtr.Zero : CFRetain(current);
    }

    /// <summary>
    /// Số lần clipboard bị ghi kể từ lúc máy khởi động - macOS tăng nó mỗi lần
    /// có ứng dụng đặt nội dung mới.
    ///
    /// Đây là cách duy nhất chắc chắn để biết ứng dụng đích đã xử lý xong phím
    /// ⌘C hay chưa: so nội dung không đủ, vì người dùng hoàn toàn có thể sao chép
    /// lại đúng đoạn đang nằm sẵn trong clipboard.
    /// </summary>
    internal static long ChangeCount
    {
        get
        {
            IntPtr pb = General;
            return pb == IntPtr.Zero ? 0 : objc_msgSend(pb, SelChangeCount).ToInt64();
        }
    }

    /// <summary>Nội dung văn bản hiện có, hoặc null nếu clipboard đang không chứa văn bản.</summary>
    internal static string? GetString()
    {
        IntPtr pb = General;
        if (pb == IntPtr.Zero) return null;

        IntPtr current = objc_msgSend(pb, SelStringForType, TypeString);
        return current == IntPtr.Zero ? null : CFStringToManaged(current);
    }

    /// <summary>Xoá sạch nội dung clipboard.</summary>
    internal static void Clear()
    {
        IntPtr pb = General;
        if (pb != IntPtr.Zero) objc_msgSend(pb, SelClearContents);
    }

    internal static bool SetString(string text)
    {
        IntPtr cf = CFString(text);
        if (cf == IntPtr.Zero) return false;

        try { return SetRetainedString(cf); }
        finally { CFRelease(cf); }
    }

    internal static bool SetRetainedString(IntPtr cfString)
    {
        IntPtr pb = General;
        if (pb == IntPtr.Zero || cfString == IntPtr.Zero) return false;

        objc_msgSend(pb, SelClearContents);
        return objc_msgSend_bool(pb, SelSetStringForType, cfString, TypeString);
    }
}
