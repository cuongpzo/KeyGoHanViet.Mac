namespace KeyGoHanViet.Platform;

/// <summary>
/// Đọc/ghi clipboard và lấy đoạn văn bản đang bôi đen ở ứng dụng đích.
///
/// Tách khỏi <see cref="ITextOutput"/> vì đây là chiều ngược lại: <c>ITextOutput</c>
/// đẩy chữ ra, còn lớp này lấy chữ vào để tra từ điển.
///
/// <b>Mọi hàm ở đây đều phải gọi từ luồng giao diện, không được gọi trong hàm
/// xử lý của hook bàn phím.</b> Clipboard của Windows đòi luồng có kiểu căn hộ
/// STA, còn <see cref="CopySelection"/> thì phải chờ ứng dụng đích xử lý xong
/// phím sao chép - mà hook chạy quá lâu thì cả Windows lẫn macOS đều lặng lẽ gỡ
/// bỏ nó, bộ gõ chết câm mà không báo lỗi gì.
/// </summary>
public interface IClipboard
{
    /// <summary>Nội dung văn bản hiện có, hoặc null nếu clipboard trống hay đang chứa thứ khác.</summary>
    string? GetText();

    bool SetText(string text);

    /// <summary>
    /// Gửi tổ hợp sao chép (Ctrl+C / ⌘C) tới ứng dụng đang có tiêu điểm rồi đọc
    /// lại clipboard, để lấy đoạn người dùng đang bôi đen.
    ///
    /// Trả về null nếu không bôi đen gì, hoặc ứng dụng đích không chịu sao chép.
    /// Nội dung clipboard cũ được trả lại trước khi hàm kết thúc.
    /// </summary>
    string? CopySelection();
}
