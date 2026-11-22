namespace KeyGoHanViet.Core;

/// <summary>Một mục từ trong từ điển Hán Nôm.</summary>
public sealed class HanEntry
{
    /// <summary>Chữ Hán Nôm, ví dụ 越南.</summary>
    public required string Text { get; init; }

    /// <summary>Các âm tiết TELEX đầy đủ, ví dụ ["vieetj", "namz"].</summary>
    public required string[] Syllables { get; init; }

    /// <summary>
    /// Tần suất sử dụng lấy từ từ điển. Lưu ý: trong hannomPS, giá trị 10000 không
    /// phải tần suất mà là cờ đánh dấu 𡨸標準 (chữ chuẩn) — nó được tách sang
    /// <see cref="IsStandard"/> để không lấn át tần suất thật.
    /// </summary>
    public required int Weight { get; init; }

    /// <summary>true nếu từ điển đánh dấu đây là chữ chuẩn (dòng trọng số 10000).</summary>
    public required bool IsStandard { get; init; }

    /// <summary>Điểm xếp hạng tổng hợp: chữ chuẩn luôn đứng trên chữ dị thể cùng âm.</summary>
    public int Rank => Weight + (IsStandard ? 100_000 : 0);

    /// <summary>Ghi chú trong từ điển, ví dụ "#Loan or homonym".</summary>
    public string? Comment { get; init; }

    /// <summary>true nếu mục từ đến từ từ điển người dùng.</summary>
    public bool FromUserDict { get; init; }

    /// <summary>Khoá gõ đủ dấu, ví dụ "vieetjnamz".</summary>
    public required string FullKey { get; init; }

    /// <summary>Khoá gõ bỏ thanh điệu, ví dụ "vieetnam".</summary>
    public required string BareKey { get; init; }

    /// <summary>Khoá gõ hoàn toàn không dấu, ví dụ "vietnam".</summary>
    public required string PlainKey { get; init; }

    /// <summary>Khoá viết tắt lấy chữ cái đầu mỗi âm tiết, ví dụ "vn".</summary>
    public required string AbbrevKey { get; init; }

    /// <summary>
    /// Âm đọc Quốc ngữ có dấu, ví dụ "hán việt từ điển trích dẫn". Lấy từ cột
    /// thứ ba của từ điển v2; với từ điển v1 (không có cột đó) thì dựng từ mã
    /// TELEX. Đây là chuỗi hiển thị, còn khoá tra cứu là <see cref="VietKey"/>.
    /// </summary>
    public required string Viet { get; init; }

    /// <summary>
    /// Khoá tra theo âm đọc, ví dụ "hánviệttừđiểntríchdẫn": đã hạ chữ thường,
    /// bỏ khoảng trắng và chuẩn hoá tổ hợp Unicode - xem <see cref="VietText.NormalizeKey"/>.
    /// </summary>
    public required string VietKey { get; init; }

    /// <summary>true nếu âm đọc được ghi thẳng trong từ điển chứ không phải suy ra từ mã TELEX.</summary>
    public bool HasVietColumn { get; init; }

    /// <summary>Mã TELEX hiển thị cho người dùng, ví dụ "vieetj namz".</summary>
    public string CodeDisplay => string.Join(' ', Syllables);

    /// <summary>Số âm tiết.</summary>
    public int Length => Syllables.Length;

    public override string ToString() => $"{Text}\t{CodeDisplay}\t{Weight}";
}
