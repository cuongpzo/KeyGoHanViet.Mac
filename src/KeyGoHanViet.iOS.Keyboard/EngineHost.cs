using Foundation;
using KeyGoHanViet.Core;

namespace KeyGoHanViet.iOS.Keyboard;

/// <summary>
/// Nạp bộ máy gợi ý một lần cho cả tiến trình extension.
///
/// iOS hay giữ tiến trình bàn phím sống giữa các lần hiện / ẩn, mỗi lần lại tạo
/// một <see cref="KeyboardViewController"/> mới - nên từ điển (≈ 23 MB) giữ ở
/// đây chứ không nạp lại theo từng controller.
/// </summary>
internal static class EngineHost
{
    private static readonly object Gate = new();
    private static Task<CandidateEngine?>? _task;

    /// <summary>Lý do nạp hỏng, để hiện lên thanh gợi ý.</summary>
    public static string? Error { get; private set; }

    public static Task<CandidateEngine?> GetAsync()
    {
        lock (Gate)
            return _task ??= Task.Run(Load);
    }

    private static CandidateEngine? Load()
    {
        try
        {
            string data = Path.Combine(NSBundle.MainBundle.BundlePath, "Data");

            // Hồ sơ riêng của extension (Library/KeyGoHanViet trong vùng chứa của nó).
            string library = NSFileManager.DefaultManager
                .GetUrls(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User)[0].Path!;
            string profile = Path.Combine(library, "KeyGoHanViet");
            Directory.CreateDirectory(profile);

            var settings = new AppSettings();

            string supplement = Path.Combine(data, "ChinesePhienAmWords.txt");
            var dict = HanNomDictionary.Load(
                Path.Combine(data, "hannomPS.dict_v2.yaml"),
                Path.Combine(data, "user.dict.yaml"),
                settings.EnableSupplementDictionary && File.Exists(supplement) ? supplement : null);

            var macros = MacroStore.Load(Path.Combine(profile, "macros.json"),
                                         Path.Combine(data, "macros.default.json"));
            var usage = UsageMemory.Load(Path.Combine(profile, "usage.json"));

            var engine = new CandidateEngine(dict, macros, usage, settings);

            // Dọn rác của quá trình đọc tệp: bàn phím iOS bị giới hạn RAM rất chặt.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return engine;
        }
        catch (Exception ex)
        {
            Error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }
}
