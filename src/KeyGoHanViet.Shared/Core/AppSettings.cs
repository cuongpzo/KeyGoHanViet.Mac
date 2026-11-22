using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyGoHanViet.Core;

/// <summary>Phím dùng để chuyển đổi giữa chế độ Hán Nôm (K) và chế độ Latin (Q).</summary>
public enum ToggleKeyKind
{
    /// <summary>Nhấn và thả Shift mà không kèm phím nào khác (đúng đặc tả §3).</summary>
    ShiftTap,
    ControlShift,
    AltZ,
    ControlSpace,
    /// <summary>Không dùng phím chuyển - chỉ đổi bằng biểu tượng khay hệ thống.</summary>
    None,
    /// <summary>Tổ hợp phím do người dùng tự bấm để đặt, lưu ở <see cref="AppSettings.CustomToggleKey"/>.</summary>
    Custom,
}

/// <summary>Cách gửi chữ Hán Nôm ra ứng dụng đích.</summary>
public enum OutputMethod
{
    /// <summary>
    /// Gửi thẳng sự kiện phím Unicode - hoạt động với hầu hết ứng dụng.
    /// Windows: SendInput với KEYEVENTF_UNICODE. macOS: CGEventKeyboardSetUnicodeString.
    /// </summary>
    SendInputUnicode,
    /// <summary>Ghi vào clipboard rồi gửi phím dán (Ctrl+V / ⌘V) - dự phòng cho ứng dụng khó tính.</summary>
    ClipboardPaste,
}

/// <summary>Hướng bố trí khung gợi ý.</summary>
public enum CandidateLayout
{
    Vertical,
    Horizontal,
}

/// <summary>Toàn bộ tuỳ chọn của chương trình, lưu ở %APPDATA%\KeyGoHanViet\settings.json.</summary>
public sealed class AppSettings
{
    // ------------------------------------------------------------ chung
    /// <summary>Bật/tắt toàn bộ bộ gõ.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Trạng thái hiện tại: true = gõ Hán Nôm, false = gõ Latin bình thường.</summary>
    public bool HanNomMode { get; set; } = true;

    public ToggleKeyKind ToggleKey { get; set; } = ToggleKeyKind.ShiftTap;

    /// <summary>
    /// Tổ hợp phím tự đặt, chỉ dùng khi <see cref="ToggleKey"/> là
    /// <see cref="ToggleKeyKind.Custom"/>. Lưu dạng chuỗi của <see cref="VKey"/>,
    /// ví dụ "Control, Alt, H".
    /// </summary>
    public string CustomToggleKey { get; set; } = "Control, Alt, H";

    public OutputMethod Output { get; set; } = OutputMethod.SendInputUnicode;

    /// <summary>
    /// Phím tắt đọc nội dung clipboard rồi tra ra chữ Hán Nôm, ví dụ dán sẵn
    /// "hán việt từ điển trích dẫn" ở đâu đó rồi bấm để lấy 漢越辭典摘引.
    /// Khung gợi ý hiện lên như khi gõ tay, chọn xong mới chèn vào ứng dụng.
    ///
    /// Cố ý KHÔNG chiếm Ctrl+V: dán code, dán đường dẫn, dán ảnh vẫn phải chạy
    /// bình thường thì người dùng mới dám để bộ gõ bật suốt.
    /// Chuỗi rỗng = tắt hẳn. Lưu cùng dạng với <see cref="CustomToggleKey"/>.
    /// </summary>
    public string ClipboardLookupHotkey { get; set; } = "Control, Shift, V";

    /// <summary>
    /// Phím tắt chuyển đoạn văn bản đang bôi đen trong ứng dụng đích: sao chép
    /// vùng chọn, tra từ điển, rồi thay thẳng vùng đó bằng chữ Hán Nôm.
    /// Chuỗi rỗng = tắt hẳn.
    /// </summary>
    public string ConvertSelectionHotkey { get; set; } = "Control, Shift, H";

    /// <summary>
    /// Phím tắt xem nhanh âm đọc: sao chép đoạn đang bôi đen, chuyển từng chữ
    /// Hán Nôm sang âm Quốc ngữ rồi hiện ra trong một cửa sổ nhỏ để đọc và sao
    /// chép (滴答滴答答 → "Trích đáp trích đáp đáp").
    ///
    /// Đây là chiều ngược của bộ gõ nên không đụng gì tới văn bản trong ứng dụng
    /// đích, và chạy được cả khi đang ở chế độ Latin (Q).
    /// Chuỗi rỗng = tắt hẳn. Lưu cùng dạng với <see cref="CustomToggleKey"/>.
    /// </summary>
    public string HanToVietHotkey { get; set; } = "Shift, F10";

    /// <summary>Bật tính năng xem nhanh âm đọc bằng <see cref="HanToVietHotkey"/>.</summary>
    public bool EnableHanToViet { get; set; } = true;

    /// <summary>Tự chạy cùng Windows.</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>Thu nhỏ xuống khay hệ thống khi đóng cửa sổ tuỳ chọn.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Nhớ chế độ K/Q riêng cho từng ứng dụng.
    ///
    /// Bật lên thì chế độ Hán Nôm chỉ ăn vào đúng ứng dụng đang ở trước lúc bấm
    /// phím chuyển; mọi ứng dụng khác vẫn gõ Latin cho tới khi người dùng tự bật
    /// chữ Hán ở đó. Danh sách ứng dụng nào đang K, ứng dụng nào đang Q nằm ở
    /// <c>config.json</c> cạnh tệp chạy - xem <see cref="AppModeStore"/>.
    ///
    /// Tắt đi thì bộ gõ trở lại lối cũ: một chế độ dùng chung cho toàn hệ thống.
    /// </summary>
    public bool PerAppMode { get; set; } = true;

    // ------------------------------------------------------ hành vi gõ
    /// <summary>Chèn thêm một dấu cách sau khi xác nhận bằng phím Space.</summary>
    public bool AppendSpaceAfterCommit { get; set; }

    /// <summary>Cho phép chọn ứng viên bằng phím số 1-9.</summary>
    public bool SelectByDigit { get; set; } = true;

    /// <summary>Bật gõ hoàn toàn không dấu (vietnam -> 越南).</summary>
    public bool EnablePlainInput { get; set; } = true;

    /// <summary>
    /// Bật tra cứu bằng chính âm đọc Quốc ngữ có dấu ("hán việt từ điển trích dẫn"
    /// -> 漢越辭典摘引), bên cạnh lối gõ mã TELEX sẵn có. Đây là điều kiện cần cho cả
    /// hai phím tắt <see cref="ClipboardLookupHotkey"/> và <see cref="ConvertSelectionHotkey"/>.
    /// </summary>
    public bool EnableVietInput { get; set; } = true;

    /// <summary>
    /// Cho gõ TELEX không theo thứ tự: "dofod", "ddofo" đều ra đồ như "ddoof".
    /// Xem <see cref="FreeOrderTelex"/>.
    /// </summary>
    public bool EnableFreeOrderTelex { get; set; } = true;

    /// <summary>
    /// Nạp thêm bảng âm đọc <c>ChinesePhienAmWords.txt</c> trong thư mục Data:
    /// vài nghìn chữ Hán và âm đọc mà từ điển chính chưa có, luôn xếp sau âm chuẩn.
    /// </summary>
    public bool EnableSupplementDictionary { get; set; } = true;

    /// <summary>Bật ghép cụm từ nhiều mục từ điển.</summary>
    public bool EnableComposite { get; set; } = true;

    /// <summary>Bật bảng gõ tắt của người dùng.</summary>
    public bool EnableMacros { get; set; } = true;

    /// <summary>Bật khớp viết tắt chữ cái đầu suy ra tự động từ từ điển.</summary>
    public bool EnableAbbrevFromDictionary { get; set; } = true;

    /// <summary>Hiện gợi ý ngay khi mới gõ dở.</summary>
    public bool EnablePrefixSuggestions { get; set; } = true;

    /// <summary>Số ký tự tối thiểu trước khi gợi ý theo tiền tố.</summary>
    public int MinPrefixLength { get; set; } = 3;

    /// <summary>Ghi nhớ lựa chọn để xếp hạng lần sau.</summary>
    public bool LearnUsage { get; set; } = true;

    /// <summary>Đặt dấu thanh theo lối hiện đại (nghĩa) thay vì lối schema gốc (nghiã).</summary>
    public bool ModernTonePlacement { get; set; } = true;

    // -------------------------------------------------------- giao diện
    /// <summary>Số ứng viên hiện cùng lúc trong khung gợi ý - tức cỡ một trang.</summary>
    public int MaxCandidates { get; set; } = 9;

    /// <summary>
    /// Số ứng viên tối đa mà bộ máy giữ lại cho một chuỗi gõ, gộp hết mọi trang.
    ///
    /// Khung gợi ý chỉ hiện <see cref="MaxCandidates"/> dòng một lúc, nhưng một
    /// âm như "tuwz" (tư) có tới vài trăm chữ đồng âm. Trước đây phần dôi ra bị
    /// vứt thẳng, nên 491 chữ không bao giờ tới được tay người dùng; nay chúng
    /// được giữ lại và lật xem bằng phím Tab.
    ///
    /// Đặt số quá lớn không sai, chỉ tốn công xếp hạng trên mỗi phím gõ.
    /// </summary>
    public int CandidatePoolSize { get; set; } = 500;

    public CandidateLayout Layout { get; set; } = CandidateLayout.Vertical;

    public string HanFontFamily { get; set; } = "Nom Na Tong";

    public float HanFontSize { get; set; } = 18f;

    public string UiFontFamily { get; set; } = "Segoe UI";

    public float UiFontSize { get; set; } = 9f;

    /// <summary>Hiện mã TELEX bên phải mỗi ứng viên.</summary>
    public bool ShowHints { get; set; } = true;

    /// <summary>Hiện vùng preedit Quốc ngữ phía trên danh sách.</summary>
    public bool ShowPreedit { get; set; } = true;

    public bool DarkTheme { get; set; }

    // -------------------------------------------------------- từ điển
    /// <summary>Đường dẫn tệp từ điển chính; rỗng = dùng tệp mặc định cạnh chương trình.</summary>
    public string MainDictionaryPath { get; set; } = "";

    /// <summary>Đường dẫn từ điển bổ sung của người dùng.</summary>
    public string UserDictionaryPath { get; set; } = "";

    // ----------------------------------------------------------- lưu/nạp

    [JsonIgnore]
    public string FilePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            settings = new AppSettings();
        }

        settings.FilePath = path;
        settings.Clamp();
        return settings;
    }

    public void Save()
    {
        if (FilePath.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch (IOException) { /* không chặn luồng gõ vì lỗi ghi tệp */ }
    }

    private void Clamp()
    {
        MaxCandidates = Math.Clamp(MaxCandidates, 1, 20);
        CandidatePoolSize = Math.Clamp(CandidatePoolSize, MaxCandidates, 5000);
        MinPrefixLength = Math.Clamp(MinPrefixLength, 1, 10);
        HanFontSize = Math.Clamp(HanFontSize, 10f, 48f);
        UiFontSize = Math.Clamp(UiFontSize, 7f, 20f);
    }

    public AppSettings Clone() =>
        (JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts) ?? new AppSettings())
        .WithPath(FilePath);

    private AppSettings WithPath(string path) { FilePath = path; return this; }

    /// <summary>Sao chép mọi giá trị từ <paramref name="other"/> vào đối tượng hiện tại.</summary>
    public void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
        {
            if (!p.CanWrite || p.Name == nameof(FilePath)) continue;
            p.SetValue(this, p.GetValue(other));
        }
        Clamp();
    }
}
