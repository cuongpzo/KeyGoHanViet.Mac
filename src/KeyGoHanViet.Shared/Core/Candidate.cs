namespace KeyGoHanViet.Core;

/// <summary>Mức độ ưu tiên của một ứng viên - số nhỏ hơn thì đứng trước.</summary>
public enum CandidateTier
{
    /// <summary>Gõ tắt do người dùng định nghĩa.</summary>
    Macro = 0,
    /// <summary>Khớp chính xác mã TELEX đủ dấu.</summary>
    ExactFull = 1,
    /// <summary>
    /// Khớp chính xác âm đọc Quốc ngữ có dấu ("hán việt từ điển trích dẫn").
    /// Xếp ngang hàng với mã TELEX đủ dấu vì cũng là một chuỗi đủ thông tin
    /// thanh điệu - chỉ khác đường vào.
    /// </summary>
    ExactViet = 2,
    /// <summary>Khớp chính xác khi bỏ thanh điệu.</summary>
    ExactBare = 3,
    /// <summary>Khớp chính xác khi bỏ hoàn toàn dấu.</summary>
    ExactPlain = 4,
    /// <summary>Ghép từ nhiều cụm/chữ theo cách tách âm tiết.</summary>
    Composite = 5,
    /// <summary>Khớp viết tắt chữ cái đầu suy ra từ từ điển.</summary>
    Abbrev = 6,
    /// <summary>Gợi ý khi mới gõ dở (khoá bắt đầu bằng chuỗi đang gõ).</summary>
    Prefix = 7,
}

/// <summary>Một ứng viên hiển thị trong khung gợi ý.</summary>
public sealed class Candidate
{
    public required string Text { get; init; }

    /// <summary>Chú thích hiển thị bên phải: mã TELEX hoặc nguồn gốc.</summary>
    public string Hint { get; init; } = "";

    /// <summary>Âm đọc Quốc ngữ có dấu của ứng viên, ví dụ "việt nam". Rỗng nếu không suy ra được.</summary>
    public string Reading { get; init; } = "";

    public required CandidateTier Tier { get; init; }

    /// <summary>Trọng số gốc trong từ điển.</summary>
    public int Weight { get; init; }

    /// <summary>Số lần người dùng đã chọn ứng viên này cho cùng chuỗi gõ.</summary>
    public int UsageCount { get; set; }

    /// <summary>Tổng số lần đã chọn chữ này, gõ bằng bất kỳ chuỗi nào.</summary>
    public int TextUsage { get; set; }

    /// <summary>
    /// true nếu ứng viên đến từ cách hiểu dự phòng của chuỗi gõ: hoặc từ chuỗi
    /// đã sắp lại thứ tự phím (<see cref="FreeOrderTelex"/>) khi nguyên văn đã
    /// tra ra chữ, hoặc từ nguyên văn khi chính nó không tra ra chữ nào. Trong
    /// cùng một bậc khớp, ứng viên dự phòng đứng sau.
    /// </summary>
    public bool Fallback { get; set; }

    public override string ToString() => $"{Text} ({Tier}, w={Weight}, u={UsageCount})";
}
