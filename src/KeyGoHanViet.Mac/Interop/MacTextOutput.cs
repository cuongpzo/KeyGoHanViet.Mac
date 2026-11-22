using Avalonia.Threading;
using KeyGoHanViet.Platform;
using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Gửi chữ Hán Nôm tới ứng dụng đang có tiêu điểm - bản macOS của
/// <see cref="ITextOutput"/>, thay cho SendInput của Windows.
/// </summary>
public sealed class MacTextOutput : ITextOutput
{
    /// <summary>
    /// Một sự kiện phím chỉ chở được một số lượng đơn vị mã UTF-16 nhất định.
    /// Cắt nhỏ ở mức an toàn thay vì dò giới hạn thật, vì chuỗi ở đây luôn ngắn
    /// (dài nhất là một mục gõ tắt vài chữ).
    /// </summary>
    private const int ChunkSize = 16;

    private readonly IntPtr _source;

    public MacTextOutput()
    {
        _source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);

        // Đóng dấu lên nguồn: mọi sự kiện sinh ra từ đây đều mang dấu, nhờ vậy
        // MacKeyboardHook nhận ra phím của chính mình mà bỏ qua. Đây là chỗ
        // tương đương dwExtraInfo bên Windows.
        if (_source != IntPtr.Zero)
            CGEventSourceSetUserData(_source, MacKeyboardHook.InjectedMarker);
    }

    /// <summary>
    /// Gửi thẳng chuỗi Unicode kèm theo sự kiện phím.
    ///
    /// Gắn chuỗi vào sự kiện thay vì tra ra mã phím, nên chữ nào cũng gửi được
    /// bất kể bàn phím người dùng đang dùng bố cục gì. Chuỗi đi theo đơn vị mã
    /// UTF-16 nên chữ Nôm ngoài mặt phẳng cơ bản (như 𡨸) vẫn tới đích đúng,
    /// miễn là không cắt ngang một cặp thay thế - xem <see cref="SafeChunkLength"/>.
    /// </summary>
    public bool SendUnicode(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (_source == IntPtr.Zero) return false;

        int offset = 0;
        while (offset < text.Length)
        {
            int take = SafeChunkLength(text, offset);
            char[] chunk = text.ToCharArray(offset, take);

            if (!PostUnicodeChunk(chunk)) return false;
            offset += take;
        }

        return true;
    }

    /// <summary>
    /// Độ dài mảnh cắt ra từ <paramref name="offset"/>, đã lùi lại một ký tự nếu
    /// điểm cắt rơi đúng vào giữa một cặp thay thế. Cắt đôi cặp thay thế thì
    /// ứng dụng đích nhận được hai nửa vô nghĩa thay vì một chữ.
    /// </summary>
    private static int SafeChunkLength(string text, int offset)
    {
        int take = Math.Min(ChunkSize, text.Length - offset);
        if (take < text.Length - offset && char.IsHighSurrogate(text[offset + take - 1])) take--;
        return take;
    }

    private bool PostUnicodeChunk(char[] chunk)
    {
        IntPtr down = CGEventCreateKeyboardEvent(_source, virtualKey: 0, keyDown: true);
        IntPtr up = CGEventCreateKeyboardEvent(_source, virtualKey: 0, keyDown: false);

        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
            return false;
        }

        try
        {
            // Xoá sạch cờ phím bổ trợ: người dùng có thể đang giữ Shift khi chữ
            // được xuất ra, mà cờ đó dính vào sự kiện thì ứng dụng đích sẽ hiểu
            // thành một tổ hợp phím chứ không phải chữ cần chèn.
            CGEventSetFlags(down, 0);
            CGEventSetFlags(up, 0);

            CGEventKeyboardSetUnicodeString(down, chunk.Length, chunk);
            CGEventKeyboardSetUnicodeString(up, chunk.Length, chunk);

            CGEventPost(kCGSessionEventTap, down);
            CGEventPost(kCGSessionEventTap, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    // --------------------------------------------------------- dự phòng clipboard

    /// <summary>
    /// Dán qua clipboard rồi bơm ⌘V: dự phòng cho những ứng dụng không nhận
    /// chuỗi Unicode gắn kèm sự kiện phím (một số terminal, ứng dụng Java/Qt cũ).
    /// </summary>
    public bool SendViaClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (_source == IntPtr.Zero) return false;

        IntPtr backup = Pasteboard.CopyCurrentString();

        if (!Pasteboard.SetString(text))
        {
            if (backup != IntPtr.Zero) CFRelease(backup);
            return false;
        }

        TapCommandV();

        // Trả lại clipboard sau khi ứng dụng đích đã kịp dán.
        if (backup != IntPtr.Zero) RestoreLater(backup);
        return true;
    }

    private void TapCommandV()
    {
        IntPtr down = CGEventCreateKeyboardEvent(_source, MacKeyMap.kVK_ANSI_V, keyDown: true);
        IntPtr up = CGEventCreateKeyboardEvent(_source, MacKeyMap.kVK_ANSI_V, keyDown: false);
        if (down == IntPtr.Zero || up == IntPtr.Zero) return;

        try
        {
            CGEventSetFlags(down, kCGEventFlagMaskCommand);
            CGEventSetFlags(up, kCGEventFlagMaskCommand);
            CGEventPost(kCGSessionEventTap, down);
            CGEventPost(kCGSessionEventTap, up);
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    private static void RestoreLater(IntPtr retainedString)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Pasteboard.SetRetainedString(retainedString);
            CFRelease(retainedString);
        };
        timer.Start();
    }
}
