namespace KeyGoHanViet;

/// <summary>Vị trí các tệp dữ liệu và cấu hình của chương trình.</summary>
public static class AppPaths
{
    /// <summary>Thư mục chứa file thực thi.</summary>
    public static string AppDir { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// Thư mục Data đi kèm chương trình (từ điển gốc).
    ///
    /// Trên Windows nằm ngay cạnh tệp .exe. Trong gói .app của macOS, tệp thực
    /// thi nằm ở <c>Contents/MacOS/</c> còn tài nguyên theo quy ước của Apple
    /// nằm ở <c>Contents/Resources/</c>, nên phải xét thêm chỗ đó.
    /// </summary>
    public static string BundledDataDir { get; } = FindBundledDataDir();

    private static string FindBundledDataDir()
    {
        string beside = Path.Combine(AppDir, "Data");
        if (Directory.Exists(beside)) return beside;

        string resources = Path.GetFullPath(Path.Combine(AppDir, "..", "Resources", "Data"));
        if (Directory.Exists(resources)) return resources;

        return beside;   // chưa có thì vẫn trả chỗ mặc định để thông báo lỗi chỉ đúng đường
    }

    /// <summary>
    /// Thư mục hồ sơ người dùng, theo đúng quy ước của từng hệ điều hành:
    /// Windows là <c>%APPDATA%\KeyGoHanViet</c>, macOS là
    /// <c>~/Library/Application Support/KeyGoHanViet</c>.
    ///
    /// Phải tự xét thay vì dùng thẳng <c>SpecialFolder.ApplicationData</c>: trên
    /// macOS, .NET trả về <c>~/.config</c> theo quy ước XDG của Linux, chỗ mà
    /// người dùng Mac không bao giờ mở tới để sửa từ điển.
    /// </summary>
    public static string ProfileDir { get; } = OperatingSystem.IsMacOS()
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "KeyGoHanViet")
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyGoHanViet");

    public static string SettingsFile => Path.Combine(ProfileDir, "settings.json");

    /// <summary>
    /// Tệp <c>config.json</c> ghi chế độ gõ của từng ứng dụng, đặt ngay cạnh tệp
    /// chạy để mở ra xem là thấy.
    ///
    /// Trong gói .app của macOS thì không: thư mục đó nằm trong phần đã ký, ghi
    /// vào là phá chữ ký. Ở đó tệp về thẳng hồ sơ người dùng. Trên Windows, nếu
    /// chương trình được cài vào Program Files thì thư mục cũng không cho ghi -
    /// trường hợp đó <see cref="Core.AppModeStore"/> tự lùi về
    /// <see cref="AppConfigFallback"/> lúc chạy.
    /// </summary>
    public static string AppConfigFile { get; } = InsideMacBundle
        ? Path.Combine(ProfileDir, "config.json")
        : Path.Combine(AppDir, "config.json");

    /// <summary>Chỗ lùi về khi thư mục chương trình không cho ghi.</summary>
    public static string AppConfigFallback => Path.Combine(ProfileDir, "config.json");

    private static bool InsideMacBundle =>
        OperatingSystem.IsMacOS() &&
        AppDir.Replace('\\', '/').Contains(".app/Contents/", StringComparison.Ordinal);

    public static string MacrosFile => Path.Combine(ProfileDir, "macros.json");
    public static string UsageFile => Path.Combine(ProfileDir, "usage.json");

    public static string BundledMacros => Path.Combine(BundledDataDir, "macros.default.json");
    public static string BundledUserDict => Path.Combine(BundledDataDir, "user.dict.yaml");

    /// <summary>Tên bảng âm đọc bổ sung dạng <c>漢=hán</c>, đi kèm trong thư mục Data.</summary>
    public const string SupplementFileName = "ChinesePhienAmWords.txt";

    /// <summary>
    /// Bảng âm đọc bổ sung: lấy bản trong Data, chạy thẳng từ mã nguồn thì leo
    /// lên gốc kho mã tìm. Không có ở đâu thì trả null - tính năng tự tắt.
    /// </summary>
    public static string? ResolveSupplement()
    {
        string bundled = Path.Combine(BundledDataDir, SupplementFileName);
        if (File.Exists(bundled)) return bundled;
        return FindUpwards(SupplementFileName);
    }

    /// <summary>
    /// Tên các tệp từ điển chính, xếp theo thứ tự ưu tiên. Bản v2 có thêm cột âm
    /// đọc Quốc ngữ nên tra được bằng "hán việt từ điển trích dẫn"; bản v1 vẫn
    /// nạp được và vẫn tra được bằng âm đọc, chỉ khác là âm đọc phải dựng lại từ
    /// mã TELEX nên có chỗ sai lệch với cách viết trong sách.
    /// </summary>
    private static readonly string[] MainDictNames = ["hannomPS.dict_v2.yaml", "hannomPS.dict.yaml"];

    public static string BundledMainDict
    {
        get
        {
            foreach (string name in MainDictNames)
            {
                string p = Path.Combine(BundledDataDir, name);
                if (File.Exists(p)) return p;
            }
            return Path.Combine(BundledDataDir, MainDictNames[0]);
        }
    }

    /// <summary>Từ điển người dùng có thể sửa được, đặt trong hồ sơ.</summary>
    public static string ProfileUserDict => Path.Combine(ProfileDir, "user.dict.yaml");

    public static void EnsureProfile()
    {
        Directory.CreateDirectory(ProfileDir);

        // Lần chạy đầu: chép từ điển bổ sung mẫu sang hồ sơ để người dùng tự sửa.
        if (!File.Exists(ProfileUserDict) && File.Exists(BundledUserDict))
            File.Copy(BundledUserDict, ProfileUserDict);
    }

    /// <summary>Giải đường dẫn từ điển chính theo cấu hình, có dự phòng.</summary>
    public static string ResolveMainDict(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        string bundled = BundledMainDict;
        if (File.Exists(bundled)) return bundled;

        // Chạy thẳng từ thư mục mã nguồn (F5 trong Visual Studio, `dotnet run`).
        // Leo ngược từ thư mục chạy lên tìm bản gốc ở gốc kho mã, thay vì đếm
        // cứng số cấp: độ sâu của bin\ khác nhau giữa các project và giữa bản
        // có RID (win-x64, osx-arm64) với bản không có.
        foreach (string name in MainDictNames)
            if (FindUpwards(name) is { } found) return found;

        return bundled;
    }

    /// <summary>Tìm một tệp ở thư mục chạy rồi leo dần lên các thư mục cha.</summary>
    private static string? FindUpwards(string fileName)
    {
        var dir = new DirectoryInfo(AppDir);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, fileName);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public static string ResolveUserDict(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return ProfileUserDict;
    }
}
