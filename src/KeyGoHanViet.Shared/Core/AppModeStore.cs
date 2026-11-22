using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyGoHanViet.Core;

/// <summary>Một ứng dụng đã từng được nhìn thấy, kèm chế độ gõ của riêng nó.</summary>
public sealed class AppModeEntry
{
    /// <summary>
    /// Khoá nhận dạng ứng dụng: tên tệp chạy, không phần mở rộng, viết thường
    /// ("chrome", "winword", "notepad").
    ///
    /// Không dùng tiêu đề cửa sổ vì tiêu đề đổi theo từng tài liệu đang mở, cũng
    /// không dùng đường dẫn đầy đủ vì cùng một chương trình có thể nằm ở chỗ
    /// khác trên máy khác.
    /// </summary>
    public string Process { get; set; } = "";

    /// <summary>Tên dễ đọc để người dùng nhận ra khi mở tệp này lên xem.</summary>
    public string Name { get; set; } = "";

    /// <summary>true = ứng dụng này gõ chữ Hán Nôm (K), false = gõ Latin (Q).</summary>
    public bool HanNom { get; set; }

    /// <summary>Lần gần nhất ứng dụng này được đưa lên trước.</summary>
    public DateTime LastSeen { get; set; }

    /// <summary>Lần gần nhất người dùng đổi chế độ cho ứng dụng này.</summary>
    public DateTime LastChanged { get; set; }
}

/// <summary>Nội dung tệp <c>config.json</c>.</summary>
public sealed class AppModeFile
{
    public int Version { get; set; } = 1;

    /// <summary>Ghi thẳng vào tệp một dòng giải thích, cho người mở tệp bằng tay.</summary>
    public string Note { get; set; } =
        "Danh sách ứng dụng và chế độ gõ của từng ứng dụng. " +
        "hanNom = true nghĩa là ứng dụng đó gõ ra chữ Hán Nôm (biểu tượng K), " +
        "false nghĩa là gõ Latin bình thường (biểu tượng Q).";

    public DateTime UpdatedAt { get; set; }

    public List<AppModeEntry> Apps { get; set; } = [];
}

/// <summary>
/// Nhớ chế độ gõ riêng cho từng ứng dụng, lưu ở <c>config.json</c> cạnh tệp chạy.
///
/// Bật <see cref="AppSettings.PerAppMode"/> thì phím chuyển K ⇄ Q chỉ ăn vào
/// đúng ứng dụng đang ở trước lúc bấm. Chuyển sang ứng dụng khác, bộ gõ đọc lại
/// danh sách này: có tên trong đó và đang bật thì tiếp tục gõ chữ Hán, chưa có
/// tên thì mặc định gõ Latin. Nhờ vậy có thể vừa gõ chữ Hán trong Chrome vừa gõ
/// tiếng Anh trong Terminal mà không phải bấm chuyển qua lại.
///
/// Tệp nằm cạnh chương trình chứ không nằm trong hồ sơ người dùng vì đây cũng là
/// một cuốn nhật ký để xem mắt thường: mở ra là biết ứng dụng nào đang gõ được
/// chữ Hán, ứng dụng nào không, và lần cuối dùng tới là khi nào. Thư mục chương
/// trình đôi khi không cho ghi (cài trong Program Files, hay trong gói .app đã
/// ký của macOS) - gặp vậy thì tự lùi về hồ sơ người dùng, xem
/// <see cref="Load"/>.
/// </summary>
public sealed class AppModeStore
{
    private readonly Dictionary<string, AppModeEntry> _byProcess =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<AppModeEntry> _order = [];

    private string _primaryPath = "";
    private string _fallbackPath = "";

    /// <summary>Tệp đang thực sự được ghi.</summary>
    public string FilePath { get; private set; } = "";

    /// <summary>
    /// Ghi chú và số phiên bản đọc được từ tệp, giữ nguyên để ghi lại - không
    /// làm mất phần người dùng tự sửa tay.
    /// </summary>
    private AppModeFile _file = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Danh sách ứng dụng, mới dùng nhất đứng trước.</summary>
    public IReadOnlyList<AppModeEntry> Items
    {
        get
        {
            var list = new List<AppModeEntry>(_order);
            list.Sort((a, b) => b.LastSeen.CompareTo(a.LastSeen));
            return list;
        }
    }

    public int Count => _order.Count;

    /// <summary>
    /// Nạp danh sách. <paramref name="primaryPath"/> là tệp cạnh chương trình;
    /// <paramref name="fallbackPath"/> là chỗ lùi về khi thư mục đó không cho ghi.
    ///
    /// Tệp nào tồn tại thì đọc tệp đó, ưu tiên tệp cạnh chương trình. Hỏng định
    /// dạng thì bắt đầu lại từ danh sách rỗng chứ không chặn bộ gõ khởi động:
    /// mất danh sách chỉ tốn vài cú bấm phím chuyển để dựng lại.
    /// </summary>
    public static AppModeStore Load(string primaryPath, string fallbackPath)
    {
        var store = new AppModeStore
        {
            _primaryPath = primaryPath,
            _fallbackPath = fallbackPath,
        };

        string source = File.Exists(primaryPath) ? primaryPath
                      : File.Exists(fallbackPath) ? fallbackPath
                      : "";

        if (source.Length > 0)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<AppModeFile>(File.ReadAllText(source), JsonOpts);
                if (parsed is not null)
                {
                    store._file = parsed;
                    foreach (var entry in parsed.Apps)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Process)) continue;
                        store.Put(entry);
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Danh sách hỏng thì dựng lại từ đầu.
            }
        }

        // Đã đọc được từ chỗ lùi thì cứ ghi tiếp về đó, đừng cố ghi ngược lên
        // thư mục chương trình mỗi lần đổi chế độ.
        store.FilePath = source == fallbackPath && source.Length > 0 ? fallbackPath : primaryPath;
        return store;
    }

    private void Put(AppModeEntry entry)
    {
        entry.Process = entry.Process.Trim();
        if (_byProcess.TryGetValue(entry.Process, out var existing))
        {
            _order.Remove(existing);
            _byProcess.Remove(entry.Process);
        }
        _byProcess[entry.Process] = entry;
        _order.Add(entry);
    }

    /// <summary>Chế độ đã nhớ cho một ứng dụng; chưa có tên thì mặc định gõ Latin.</summary>
    public bool GetMode(string process) =>
        _byProcess.TryGetValue(process, out var e) && e.HanNom;

    public bool Knows(string process) => _byProcess.ContainsKey(process);

    /// <summary>
    /// Ghi nhận ứng dụng vừa được đưa lên trước. Trả về true nếu đây là lần đầu
    /// thấy nó - lúc đó người gọi nên lưu tệp ngay để danh sách kịp có mặt.
    /// </summary>
    public bool Touch(string process, string name)
    {
        if (string.IsNullOrWhiteSpace(process)) return false;

        if (_byProcess.TryGetValue(process, out var entry))
        {
            entry.LastSeen = DateTime.Now;
            if (name.Length > 0) entry.Name = name;
            return false;
        }

        Put(new AppModeEntry
        {
            Process = process,
            Name = name,
            HanNom = false,
            LastSeen = DateTime.Now,
            LastChanged = DateTime.Now,
        });
        return true;
    }

    /// <summary>
    /// Đặt chế độ cho một ứng dụng. Trả về true nếu giá trị thật sự đổi, để
    /// người gọi biết có cần ghi tệp hay không.
    /// </summary>
    public bool SetMode(string process, string name, bool hanNom)
    {
        if (string.IsNullOrWhiteSpace(process)) return false;

        if (!_byProcess.TryGetValue(process, out var entry))
        {
            Put(new AppModeEntry
            {
                Process = process,
                Name = name,
                HanNom = hanNom,
                LastSeen = DateTime.Now,
                LastChanged = DateTime.Now,
            });
            return true;
        }

        entry.LastSeen = DateTime.Now;
        if (name.Length > 0) entry.Name = name;

        if (entry.HanNom == hanNom) return false;

        entry.HanNom = hanNom;
        entry.LastChanged = DateTime.Now;
        return true;
    }

    public void Remove(string process)
    {
        if (!_byProcess.TryGetValue(process, out var entry)) return;
        _byProcess.Remove(process);
        _order.Remove(entry);
    }

    public void Clear()
    {
        _byProcess.Clear();
        _order.Clear();
    }

    /// <summary>
    /// Ghi danh sách xuống đĩa. Thư mục chương trình không cho ghi thì chuyển
    /// hẳn sang hồ sơ người dùng và ghi ở đó từ đây về sau.
    /// </summary>
    public void Save()
    {
        _file.Version = 1;
        _file.UpdatedAt = DateTime.Now;
        _file.Apps = [.. Items];

        if (TryWrite(FilePath)) return;

        // Không ghi được chỗ chính: lùi về hồ sơ người dùng, nhớ luôn để lần sau
        // khỏi thử lại vô ích trên mỗi lần đổi chế độ.
        if (FilePath != _fallbackPath && _fallbackPath.Length > 0 && TryWrite(_fallbackPath))
            FilePath = _fallbackPath;
    }

    private bool TryWrite(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(_file, JsonOpts));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Chỗ lẽ ra phải ghi, dùng cho phần hiển thị trong cửa sổ tuỳ chọn.</summary>
    public string PreferredPath => _primaryPath;
}
