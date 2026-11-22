using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyGoHanViet.Core;

/// <summary>Một mục gõ tắt: chuỗi viết tắt -> danh sách kết quả Hán Nôm.</summary>
public sealed class MacroItem
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    /// <summary>Các lựa chọn, phần tử đầu tiên là mặc định.</summary>
    [JsonPropertyName("values")]
    public List<string> Values { get; set; } = [];

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>
/// Bảng gõ tắt do người dùng định nghĩa (§2.3 của đặc tả).
/// Lưu tại <c>%APPDATA%\KeyGoHanViet\macros.json</c>.
/// </summary>
public sealed class MacroStore
{
    private Dictionary<string, MacroItem> _map = new(StringComparer.OrdinalIgnoreCase);

    public string FilePath { get; private set; } = "";
    public int Count => _map.Count;
    public IEnumerable<MacroItem> Items => _map.Values.OrderBy(m => m.Key, StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static MacroStore Load(string path, string? seedPath = null)
    {
        var store = new MacroStore { FilePath = path };

        string? source = File.Exists(path) ? path
                       : (seedPath is not null && File.Exists(seedPath) ? seedPath : null);

        if (source is not null)
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<MacroItem>>(File.ReadAllText(source), JsonOpts);
                if (items is not null)
                    foreach (var it in items)
                        if (!string.IsNullOrWhiteSpace(it.Key) && it.Values.Count > 0)
                            store._map[it.Key.Trim()] = it;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Tệp hỏng: bắt đầu với bảng rỗng thay vì làm sập ứng dụng.
            }
        }

        return store;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Items.ToList(), JsonOpts));
    }

    public bool TryGet(string key, out MacroItem item) => _map.TryGetValue(key, out item!);

    public void Set(string key, IEnumerable<string> values, string? note = null)
    {
        key = key.Trim();
        if (key.Length == 0) return;
        _map[key] = new MacroItem { Key = key, Values = values.Where(v => v.Length > 0).ToList(), Note = note };
    }

    public bool Remove(string key) => _map.Remove(key.Trim());

    public void Replace(IEnumerable<MacroItem> items)
    {
        _map = new Dictionary<string, MacroItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
            if (!string.IsNullOrWhiteSpace(it.Key) && it.Values.Count > 0)
                _map[it.Key.Trim()] = it;
    }
}
