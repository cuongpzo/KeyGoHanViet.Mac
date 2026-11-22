using Avalonia.Threading;
using KeyGoHanViet.Platform;
using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Bản macOS của <see cref="IClipboard"/>, dựng trên NSPasteboard qua
/// <see cref="Pasteboard"/>.
///
/// Không dùng clipboard của Avalonia: hàm ở đó chạy bất đồng bộ, mà chỗ gọi lại
/// cần kết quả ngay để dựng danh sách gợi ý.
/// </summary>
public sealed class MacClipboard : IClipboard
{
    /// <summary>Xem <c>WindowsClipboard.CopyTimeoutMs</c> - cùng lý do, cùng mức chờ.</summary>
    private const int CopyTimeoutMs = 400;

    private const int PollIntervalMs = 20;

    private readonly IntPtr _source;

    public MacClipboard()
    {
        _source = CGEventSourceCreate(kCGEventSourceStateHIDSystemState);

        // Đóng dấu như MacTextOutput, để ⌘C mình tự bơm ra không quay ngược vào
        // chính bộ bắt phím của mình.
        if (_source != IntPtr.Zero)
            CGEventSourceSetUserData(_source, MacKeyboardHook.InjectedMarker);
    }

    public string? GetText()
    {
        string? text = Pasteboard.GetString();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public bool SetText(string text)
    {
        if (text.Length == 0) { Pasteboard.Clear(); return true; }
        return Pasteboard.SetString(text);
    }

    public string? CopySelection()
    {
        if (_source == IntPtr.Zero) return null;

        string? backup = GetText();
        long before = Pasteboard.ChangeCount;

        TapCommandC();

        string? copied = WaitForClipboard(before);

        // Trả lại ngay: khác với lúc dán, ở đây không có ai đang chờ đọc clipboard
        // nữa, để nội dung lạ lại chỉ tổ làm người dùng dán nhầm ở lần ⌘V sau.
        if (backup is not null) SetText(backup);
        else if (copied is not null) Pasteboard.Clear();

        return copied;
    }

    private void TapCommandC()
    {
        IntPtr down = CGEventCreateKeyboardEvent(_source, MacKeyMap.kVK_ANSI_C, keyDown: true);
        IntPtr up = CGEventCreateKeyboardEvent(_source, MacKeyMap.kVK_ANSI_C, keyDown: false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
            return;
        }

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

    /// <summary>
    /// Chờ tới khi ứng dụng đích ghi xong kết quả sao chép.
    ///
    /// Vẫn chạy các việc đang xếp hàng của giao diện trong lúc chờ, vì luồng này
    /// cũng là luồng chạy vòng lặp chính - đứng im ở đây thì bộ bắt phím ngừng
    /// nhận sự kiện và macOS có thể tắt luôn nó vì chạy quá lâu.
    /// </summary>
    private static string? WaitForClipboard(long changeCountBefore)
    {
        for (int waited = 0; waited < CopyTimeoutMs; waited += PollIntervalMs)
        {
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
            Thread.Sleep(PollIntervalMs);

            if (Pasteboard.ChangeCount == changeCountBefore) continue;

            string? text = Pasteboard.GetString();
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return null;
    }
}
