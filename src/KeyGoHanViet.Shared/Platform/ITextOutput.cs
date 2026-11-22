namespace KeyGoHanViet.Platform;

/// <summary>
/// Gửi chuỗi ký tự Unicode tới ứng dụng đang có tiêu điểm.
///
/// Cả hai hệ đều phải xử lý được chữ nằm ngoài mặt phẳng cơ bản (như 𡨸,
/// U+21A38) — nghĩa là gửi theo từng đơn vị mã UTF-16 chứ không theo từng
/// <c>char</c> hiểu theo nghĩa "một chữ".
/// </summary>
public interface ITextOutput
{
    /// <summary>Gửi thẳng chuỗi Unicode. Trả false nếu hệ điều hành từ chối.</summary>
    bool SendUnicode(string text);

    /// <summary>
    /// Dự phòng qua clipboard rồi bấm phím dán (Ctrl+V trên Windows, ⌘V trên
    /// macOS), cho những ứng dụng không nhận sự kiện phím Unicode.
    /// Nội dung clipboard cũ được trả lại sau khi ứng dụng đích đã kịp dán.
    /// </summary>
    bool SendViaClipboard(string text);
}
