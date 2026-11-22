using Avalonia.Threading;
using KeyGoHanViet.Platform;
using static KeyGoHanViet.Mac.Interop.NativeMethods;

namespace KeyGoHanViet.Mac.Interop;

/// <summary>
/// Theo dõi ứng dụng nào đang ở trước, bản macOS của
/// <see cref="IActiveAppWatcher"/>. Đối chiếu với <c>Interop\ActiveAppWatcher.cs</c>
/// bên Windows.
///
/// Hỏi <c>NSWorkspace.frontmostApplication</c> theo nhịp chứ không đăng ký nhận
/// thông báo <c>NSWorkspaceDidActivateApplicationNotification</c>: cách đăng ký
/// đòi một đối tượng Objective-C có phương thức nhận thông báo, mà dựng một lớp
/// ObjC từ P/Invoke thuần thì phải tự gọi <c>objc_allocateClassPair</c> rồi gắn
/// hàm - nhiều mã bản địa hơn hẳn, đổi lại chỉ nhanh hơn được vài trăm mili
/// giây. Hỏi theo nhịp chỉ là hai lời gọi <c>objc_msgSend</c> mỗi lần, rẻ hơn
/// một cú nhấp chuột.
///
/// Nhịp cố ý ngắn: người dùng bấm ⌘Tab rồi gõ ngay, chậm quá thì mấy chữ đầu ra
/// nhầm chế độ.
/// </summary>
public sealed class MacActiveAppWatcher : IActiveAppWatcher
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly DispatcherTimer _timer;
    private readonly int _ownPid = Environment.ProcessId;

    public event EventHandler<ActiveAppInfo>? ActiveAppChanged;

    public ActiveAppInfo? Current { get; private set; }

    public MacActiveAppWatcher()
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        if (_timer.IsEnabled) return;
        _timer.Start();
        Poll();
    }

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        var info = ReadFrontmost();
        if (info is null) return;

        if (Current is not null &&
            string.Equals(Current.Key, info.Key, StringComparison.OrdinalIgnoreCase)) return;

        Current = info;
        ActiveAppChanged?.Invoke(this, info);
    }

    /// <summary>
    /// <c>[[NSWorkspace sharedWorkspace] frontmostApplication]</c>, đọc ra khoá
    /// nhận dạng và tên hiển thị. Trả null khi chưa xác định được, hoặc khi ứng
    /// dụng ở trước chính là KeyGo.
    /// </summary>
    private ActiveAppInfo? ReadFrontmost()
    {
        IntPtr cls = objc_getClass("NSWorkspace");
        if (cls == IntPtr.Zero) return null;

        IntPtr workspace = objc_msgSend(cls, sel_registerName("sharedWorkspace"));
        if (workspace == IntPtr.Zero) return null;

        IntPtr app = objc_msgSend(workspace, sel_registerName("frontmostApplication"));
        if (app == IntPtr.Zero) return null;

        // Cửa sổ tuỳ chọn của chính KeyGo cũng là một ứng dụng ở trước. Tính nó
        // là "ứng dụng đang gõ" thì chế độ vừa bật lại được ghi cho KeyGo.
        if (objc_msgSend_int(app, sel_registerName("processIdentifier")) == _ownPid) return null;

        // bundleIdentifier ổn định hơn tên tệp chạy trên macOS: người dùng đổi
        // tên gói .app thoải mái mà định danh vẫn nguyên.
        string bundle = ReadString(app, "bundleIdentifier");
        string name = ReadString(app, "localizedName");

        string key = bundle.Length > 0 ? bundle : name;
        if (key.Length == 0) return null;

        return new ActiveAppInfo { Key = key.ToLowerInvariant(), Name = name.Length > 0 ? name : key };
    }

    /// <summary>Đọc một thuộc tính kiểu NSString của đối tượng ObjC về chuỗi .NET.</summary>
    private static string ReadString(IntPtr obj, string selector)
    {
        IntPtr value = objc_msgSend(obj, sel_registerName(selector));

        // NSString và CFString là cùng một thứ ở tầng dưới, nên đọc thẳng bằng
        // API của CoreFoundation được, không cần qua UTF8String.
        return value == IntPtr.Zero ? "" : CFStringToManaged(value);
    }

    public void Dispose() => Stop();
}
