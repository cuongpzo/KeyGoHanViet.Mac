using System.Text.Json;

namespace KeyGoHanViet.Core;

/// <summary>
/// Ghi nhớ lựa chọn của người dùng để lần sau đưa chữ hay dùng lên đầu
/// (yêu cầu §4.3 - xử lý xung đột theo mức độ sử dụng).
///
/// Hai lớp số đếm:
///
///   • Theo cặp (chuỗi gõ đã chuẩn hoá, chữ) - lớp chính, quyết định chữ nào
///     lên đầu khi gõ lại đúng âm đó. Chuỗi gõ đã qua <see cref="FreeOrderTelex"/>
///     nên "dofod", "ddofo" và "ddoof" dồn chung một số đếm.
///   • Theo chữ - tổng mọi lần chọn chữ đó dù gõ bằng đường nào. Chỉ dùng để
///     xếp lại trong cùng một bậc khớp: đã hay chọn 圖 khi gõ "ddoof" thì gõ
///     "ddo" (bỏ thanh) 圖 cũng đứng trên các chữ đồng âm khác.
///
/// Tệp chỉ lưu lớp thứ nhất (định dạng cũ, đọc lại được tệp của bản trước);
/// lớp thứ hai dựng lại lúc nạp.
/// </summary>
public sealed class UsageMemory
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _textTotals = new(StringComparer.Ordinal);
    private readonly object _ioLock = new();
    private string _path = "";
    private bool _dirty;
    private long _lastSaveTick;
    private long _snapshotSeq;       // tăng dần theo mỗi lần chụp
    private long _writtenSeq;        // bản chụp mới nhất đã ghi xuống đĩa

    /// <summary>
    /// Khoảng nghỉ tối thiểu giữa hai lần tự lưu. Trước đây lịch sử chỉ được ghi
    /// lúc thoát chương trình, nên tắt máy ngang hay chương trình bị dừng là mất
    /// trắng. Nay ghi ngay sau lần chọn, nhưng không dày hơn mức này để gõ
    /// liên tục không phải ghi đĩa trên từng chữ.
    /// </summary>
    private const int AutoSaveIntervalMs = 3000;

    public int Count => _counts.Count;
    public string FilePath => _path;

    private static string MakeKey(string input, string text) => input + '|' + text;

    public static UsageMemory Load(string path)
    {
        var mem = new UsageMemory { _path = path };
        if (!File.Exists(path)) return mem;

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
            if (data is not null)
                foreach (var kv in data)
                {
                    if (kv.Value <= 0) continue;
                    mem._counts[kv.Key] = kv.Value;
                    int bar = kv.Key.LastIndexOf('|');
                    if (bar >= 0) mem.AddTotal(kv.Key[(bar + 1)..], kv.Value);
                }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Bỏ qua tệp hỏng.
        }
        return mem;
    }

    public void Save()
    {
        if (!_dirty || _path.Length == 0) return;
        WriteSnapshot(JsonSerializer.Serialize(_counts), Interlocked.Increment(ref _snapshotSeq));
        _dirty = false;
    }

    /// <summary>
    /// Ghi qua tệp tạm rồi đổi tên, để mất điện giữa chừng cũng không để lại
    /// một tệp lịch sử cụt mà lần sau không đọc được.
    /// </summary>
    private void WriteSnapshot(string json, long seq)
    {
        lock (_ioLock)
        {
            // Bản chụp cũ từ luồng nền tới muộn thì bỏ, đừng đè lên bản mới hơn.
            if (seq <= _writtenSeq) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);
                _lastSaveTick = Environment.TickCount64;
                _writtenSeq = seq;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                /* không chặn luồng gõ vì lỗi ghi tệp */
            }
        }
    }

    public int Get(string input, string text) =>
        _counts.TryGetValue(MakeKey(input, text), out int n) ? n : 0;

    /// <summary>Tổng số lần đã chọn chữ này, gõ bằng bất kỳ chuỗi nào.</summary>
    public int GetTotal(string text) =>
        _textTotals.TryGetValue(text, out int n) ? n : 0;

    public void Record(string input, string text)
    {
        string k = MakeKey(input, text);
        _counts[k] = (_counts.TryGetValue(k, out int n) ? n : 0) + 1;
        AddTotal(text, 1);
        _dirty = true;
        AutoSave();
    }

    private void AddTotal(string text, int n) =>
        _textTotals[text] = (_textTotals.TryGetValue(text, out int old) ? old : 0) + n;

    /// <summary>
    /// Chụp nội dung ngay trên luồng gọi (nhanh, chỉ là tuần tự hoá), còn việc
    /// ghi đĩa đẩy sang luồng nền để phím gõ không phải chờ ổ đĩa.
    /// </summary>
    private void AutoSave()
    {
        if (_path.Length == 0) return;
        if (Environment.TickCount64 - _lastSaveTick < AutoSaveIntervalMs) return;

        _lastSaveTick = Environment.TickCount64;
        string json = JsonSerializer.Serialize(_counts);
        long seq = Interlocked.Increment(ref _snapshotSeq);
        _dirty = false;
        ThreadPool.QueueUserWorkItem(_ => WriteSnapshot(json, seq));
    }

    public void Clear()
    {
        _counts.Clear();
        _textTotals.Clear();
        _dirty = true;
        Save();
    }
}
