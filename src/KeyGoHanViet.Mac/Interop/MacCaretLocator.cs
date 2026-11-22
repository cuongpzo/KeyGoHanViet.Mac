using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>Khung chữ nhật của con trỏ nhập liệu, tính theo <b>điểm</b> (point) toạ độ màn hình.</summary>
public readonly record struct CaretRect(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
}

/// <summary>
/// Xác định vị trí con trỏ nhập liệu (caret) để khung gợi ý bám sát điểm đang gõ.
/// Bản macOS của <c>Interop\CaretLocator.cs</c> bên Windows.
///
/// Windows hỏi qua GUITHREADINFO, macOS hỏi qua API Trợ năng (Accessibility):
/// lấy phần tử đang có tiêu điểm trong toàn hệ thống, hỏi nó đang chọn đoạn văn
/// bản nào, rồi hỏi đoạn đó nằm ở đâu trên màn hình. Ứng dụng nào không công bố
/// thông tin Trợ năng (thường là ứng dụng nền Electron) thì lùi về vị trí con trỏ
/// chuột, giống hệt đường lùi của bản Windows.
///
/// Toạ độ trả về theo hệ của Trợ năng: gốc ở góc <b>trên</b> bên trái màn hình
/// chính, đơn vị là điểm chứ không phải điểm ảnh - trên màn Retina một điểm bằng
/// hai điểm ảnh.
/// </summary>
public static class MacCaretLocator
{
    private static readonly IntPtr SystemWide = AXUIElementCreateSystemWide();

    private static readonly IntPtr AttrFocusedUIElement = CFString("AXFocusedUIElement");
    private static readonly IntPtr AttrSelectedTextRange = CFString("AXSelectedTextRange");
    private static readonly IntPtr AttrBoundsForRange = CFString("AXBoundsForRange");

    private const int kAXErrorSuccess = 0;

    /// <summary>Vị trí dùng khi không hỏi được gì cả, kể cả con trỏ chuột.</summary>
    private static readonly CaretRect Fallback = new(100, 100, 1, 20);

    public static CaretRect GetCaretRect()
    {
        var fromAccessibility = TryGetFromAccessibility();
        if (fromAccessibility is { } rect) return rect;

        var mouse = TryGetMouseLocation();
        if (mouse is { } point) return new CaretRect(point.X, point.Y, 1, 20);

        return Fallback;
    }

    private static CaretRect? TryGetFromAccessibility()
    {
        if (SystemWide == IntPtr.Zero) return null;

        IntPtr focused = IntPtr.Zero;
        IntPtr rangeValue = IntPtr.Zero;

        try
        {
            if (AXUIElementCopyAttributeValue(SystemWide, AttrFocusedUIElement, out focused) != kAXErrorSuccess
                || focused == IntPtr.Zero)
                return null;

            if (AXUIElementCopyAttributeValue(focused, AttrSelectedTextRange, out rangeValue) != kAXErrorSuccess
                || rangeValue == IntPtr.Zero)
                return null;

            if (!AXValueGetValue(rangeValue, kAXValueTypeCFRange, out CFRange selected))
                return null;

            // Hỏi khung của một ký tự thay vì của đoạn chọn rỗng: phần lớn ứng
            // dụng trả về hình chữ nhật rỗng cho đoạn dài 0. Nếu ký tự đó không
            // tồn tại (con trỏ ở cuối văn bản) thì mới lùi về đoạn rỗng.
            return BoundsOf(focused, selected.Location, 1)
                ?? BoundsOf(focused, selected.Location, 0);
        }
        finally
        {
            if (rangeValue != IntPtr.Zero) CFRelease(rangeValue);
            if (focused != IntPtr.Zero) CFRelease(focused);
        }
    }

    private static CaretRect? BoundsOf(IntPtr element, nint location, nint length)
    {
        var range = new CFRange { Location = location, Length = length };

        IntPtr axRange = AXValueCreate(kAXValueTypeCFRange, ref range);
        if (axRange == IntPtr.Zero) return null;

        IntPtr boundsValue = IntPtr.Zero;
        try
        {
            if (AXUIElementCopyParameterizedAttributeValue(element, AttrBoundsForRange, axRange, out boundsValue)
                    != kAXErrorSuccess || boundsValue == IntPtr.Zero)
                return null;

            if (!AXValueGetValue(boundsValue, kAXValueTypeCGRect, out CGRect r)) return null;

            // Ứng dụng không biết caret ở đâu thường trả về khung toàn số 0.
            if (r.Width <= 0 && r.Height <= 0) return null;

            return new CaretRect(r.X, r.Y, Math.Max(r.Width, 1), Math.Max(r.Height, 16));
        }
        finally
        {
            if (boundsValue != IntPtr.Zero) CFRelease(boundsValue);
            CFRelease(axRange);
        }
    }

    private static CGPoint? TryGetMouseLocation()
    {
        IntPtr probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return null;

        try { return CGEventGetLocation(probe); }
        finally { CFRelease(probe); }
    }
}
