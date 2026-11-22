namespace KeyGoHanViet.Platform;

/// <summary>Ứng dụng đang ở trước, theo cách bộ gõ nhận dạng nó.</summary>
public sealed class ActiveAppInfo
{
    /// <summary>
    /// Khoá nhận dạng: tên tệp chạy viết thường, không phần mở rộng
    /// ("chrome", "winword"). Đây cũng là khoá trong <c>config.json</c>.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Tên dễ đọc để hiện cho người dùng ("Google Chrome").</summary>
    public required string Name { get; init; }

    public override string ToString() => Name.Length > 0 ? $"{Name} ({Key})" : Key;
}

/// <summary>
/// Theo dõi ứng dụng nào đang ở trước, để bộ gõ nhớ chế độ K/Q riêng cho từng
/// ứng dụng.
///
/// Windows hiện thực bằng <c>SetWinEventHook(EVENT_SYSTEM_FOREGROUND)</c>; macOS
/// bằng cách hỏi <c>NSWorkspace.frontmostApplication</c> theo nhịp. Cả hai đều
/// phải tự lọc bỏ cửa sổ của chính KeyGo và các cửa sổ vỏ hệ thống (khay, trình
/// đơn Bắt đầu): bấm vào biểu tượng khay để đổi chế độ mà bị tính là "đã chuyển
/// sang ứng dụng khác" thì chế độ vừa bật lại rơi sang nhầm ứng dụng.
/// </summary>
public interface IActiveAppWatcher : IDisposable
{
    /// <summary>Phát ra khi ứng dụng ở trước đổi sang một ứng dụng khác.</summary>
    event EventHandler<ActiveAppInfo>? ActiveAppChanged;

    /// <summary>Ứng dụng đang ở trước, hoặc null nếu chưa xác định được.</summary>
    ActiveAppInfo? Current { get; }

    void Start();

    void Stop();
}
